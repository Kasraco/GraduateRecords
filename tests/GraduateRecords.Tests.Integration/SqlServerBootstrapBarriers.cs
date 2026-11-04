using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using Xunit.Sdk;

namespace GraduateRecords.Tests.Integration;

public sealed record ProbeEvidence(int SessionId, string Mode, string Description);

/// <summary>
/// The approved test-owned barrier on the unique NationalId index: two
/// absent candidate values are locked with <c>UPDLOCK, HOLDLOCK</c> through the
/// catalog-validated <c>IX_AspNetUsers_NationalId</c> index so that whichever
/// bootstrap operation acquires the Administrator role-row lock first pauses
/// <b>inside</b> its transaction, before its AspNetUsers insert commits.
/// No production code, hint or behaviour is involved — the barrier is a raw,
/// test-owned connection that is always rolled back in <c>DisposeAsync</c>.
/// </summary>
public sealed class NationalIdIndexBarrier : IAsyncDisposable
{
    public const string IndexName = "IX_AspNetUsers_NationalId";

    /// <summary>Identical statement shape for barrier and probes: only the value differs.</summary>
    public const string HintedSelect =
        "SELECT 1 FROM [dbo].[AspNetUsers] WITH (UPDLOCK, HOLDLOCK, INDEX(" + IndexName +
        ")) WHERE [NationalId] = @nationalId";

    private readonly SqlConnection _connection;
    private readonly SqlTransaction _transaction;
    private int _released;

    private NationalIdIndexBarrier(SqlConnection connection, SqlTransaction transaction, int sessionId, IReadOnlyList<string> protectedNationalIds)
    {
        _connection = connection;
        _transaction = transaction;
        SessionId = sessionId;
        ProtectedNationalIds = protectedNationalIds;
    }

    public int SessionId { get; }

    public IReadOnlyList<string> ProtectedNationalIds { get; }

    /// <summary>
    /// Resource descriptions the barrier currently holds on the NationalId
    /// index HoBT. Refreshed by <see cref="ReadCoverageAsync"/>; used by the T1
    /// evidence (C3) as the membership set for the INSERT's waited resource.
    /// </summary>
    public IReadOnlySet<string> GrantedDescriptions { get; private set; } =
        new HashSet<string>(StringComparer.Ordinal);

    public static async Task<NationalIdIndexBarrier> InstallAsync(
        string connectionString,
        IReadOnlyList<string> protectedNationalIds,
        CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        try
        {
            // Fail closed before any hint: the constant and the catalog must agree.
            using (var validation = connection.CreateCommand())
            {
                validation.CommandText =
                    "SELECT CONVERT(int, COUNT(*)) FROM sys.indexes i " +
                    "WHERE i.object_id = OBJECT_ID(N'dbo.AspNetUsers') AND i.name = @name AND i.is_unique = 1";
                validation.Parameters.Add("@name", SqlDbType.NVarChar, 128).Value = IndexName;
                if ((int?)await validation.ExecuteScalarAsync(cancellationToken) != 1)
                {
                    throw new XunitException($"The unique index '{IndexName}' does not exist on dbo.AspNetUsers.");
                }
            }

            int sessionId;
            using (var session = connection.CreateCommand())
            {
                session.CommandText = "SELECT CONVERT(int, @@SPID)";
                sessionId = (int)await session.ExecuteScalarAsync(cancellationToken)!;
            }

            var transaction = connection.BeginTransaction();
            try
            {
                foreach (var nationalId in protectedNationalIds)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = HintedSelect;
                    command.Parameters.Add("@nationalId", SqlDbType.NVarChar, 450).Value = nationalId;
                    await command.ExecuteScalarAsync(cancellationToken);
                }

                return new NationalIdIndexBarrier(connection, transaction, sessionId, protectedNationalIds.ToList());
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// The barrier's granted KEY resources on the NationalId index HoBT — the
    /// set candidate probes must contend with. Descriptions are opaque hashes
    /// compared only inside this database, this KEY type and this HoBT.
    /// Observed modes are recorded for diagnostics (live validation), never used
    /// as an automatic first-run oracle.
    /// </summary>
    public async Task<BarrierCoverage> ReadCoverageAsync(SqlServerLockDiagnostics diagnostics)
    {
        var rows = await diagnostics.LocksAsync(diagnostics.NationalIdIndexHobt);
        var granted = rows
            .Where(row => row.SessionId == SessionId && row.Status == "GRANT" && row.Description is not null)
            .ToList();

        if (granted.Count == 0)
        {
            throw new XunitException("The barrier holds no granted KEY lock on IX_AspNetUsers_NationalId.");
        }

        var coverage = new BarrierCoverage(
            granted.Select(row => row.Description!).ToHashSet(StringComparer.Ordinal),
            granted.Select(row => row.Mode).Distinct(StringComparer.Ordinal).ToList());
        GrantedDescriptions = coverage.GrantedDescriptions;
        return coverage;
    }

    /// <summary>
    /// Fail-closed barrier validation before either operation starts:
    /// (1) the database's own collation orders every layout value as expected;
    /// (2) each candidate probe waits on a barrier-protected KEY (same
    /// statement shape, different value); (3) both control probes outside the
    /// protected gaps complete without waiting. A failure in any step aborts
    /// the test with a full DMV dump.
    /// </summary>
    public async Task ValidateCoverageAsync(
        string connectionString,
        SqlServerLockDiagnostics diagnostics,
        IReadOnlyList<string> orderedLayoutValues,
        IReadOnlyList<string> candidateNationalIds,
        IReadOnlyList<string> controlNationalIds,
        CancellationToken cancellationToken = default)
    {
        var observedOrder = await ReadOrderAsync(connectionString, orderedLayoutValues, cancellationToken);
        if (!observedOrder.SequenceEqual(orderedLayoutValues, StringComparer.Ordinal))
        {
            throw new XunitException(
                "Barrier layout unverified: the test database orders the layout values differently than expected, " +
                "so gap membership cannot be proven.\n" +
                $"  expected: {string.Join(" < ", orderedLayoutValues)}\n" +
                $"  observed: {string.Join(" < ", observedOrder)}");
        }

        var coverage = await ReadCoverageAsync(diagnostics);

        foreach (var candidate in candidateNationalIds)
        {
            await AssertProbeAsync(
                connectionString, diagnostics, coverage.GrantedDescriptions, candidate,
                $"candidate {candidate}", mustBlock: true, cancellationToken);
        }

        foreach (var control in controlNationalIds)
        {
            await AssertProbeAsync(
                connectionString, diagnostics, coverage.GrantedDescriptions, control,
                $"control {control}", mustBlock: false, cancellationToken);
        }
    }

    private static async Task AssertProbeAsync(
        string connectionString,
        SqlServerLockDiagnostics diagnostics,
        IReadOnlySet<string> grantedDescriptions,
        string nationalId,
        string label,
        bool mustBlock,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var sessionId = await ScalarIntAsync(connection, "SELECT CONVERT(int, @@SPID)", cancellationToken);
        var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = HintedSelect;
        command.Parameters.Add("@nationalId", SqlDbType.NVarChar, 450).Value = nationalId;

        var probe = command.ExecuteScalarAsync(cancellationToken);
        try
        {
            if (mustBlock)
            {
                await EvidencePoll.UntilAsync(
                    async token =>
                    {
                        var rows = await diagnostics.LocksAsync(diagnostics.NationalIdIndexHobt);
                        var hit = rows.FirstOrDefault(row =>
                            row.SessionId == sessionId
                            && row.Status == "WAIT"
                            && row.Description is not null
                            && grantedDescriptions.Contains(row.Description));
                        return hit is null ? null : new ProbeEvidence(hit.SessionId, hit.Mode, hit.Description!);
                    },
                    $"probe for {label} must wait on a barrier-protected KEY of {NationalIdIndexBarrier.IndexName} " +
                    "(the barrier must cover this exact gap)",
                    () => diagnostics.DumpAsync($"probe {label}"),
                    cancellationToken);
            }
            else
            {
                try
                {
                    // Termination bound only: completion, not duration, is the assertion.
                    await probe.WaitAsync(EvidencePoll.SafetyBound);
                }
                catch (TimeoutException)
                {
                    throw new XunitException(
                        $"control probe for {label} did not complete — the barrier appears to cover more than " +
                        $"the protected gaps.\n{await diagnostics.DumpAsync($"control {label}")}");
                }
            }
        }
        finally
        {
            // Safe release of a possibly still-blocked probe: attention, await
            // full completion, dispose the command, then rollback — no blocked
            // session and no open reader is ever left on the connection.
            try
            {
                command.Cancel();
            }
            catch (InvalidOperationException)
            {
                // already completed
            }

            try
            {
                await probe;
            }
            catch
            {
                // cancelled or faulted probe — the outcome is already decided
            }

            command.Dispose();

            try
            {
                transaction.Rollback();
            }
            catch (SqlException)
            {
                // already completed/rolled back
            }
        }
    }

    private static async Task<IReadOnlyList<string>> ReadOrderAsync(
        string connectionString,
        IReadOnlyList<string> values,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();

        var sql = new StringBuilder("SELECT v FROM (VALUES ");
        for (var index = 0; index < values.Count; index++)
        {
            if (index > 0)
            {
                sql.Append(", ");
            }

            sql.Append("(@v").Append(index).Append(')');
            command.Parameters.Add($"@v{index}", SqlDbType.NVarChar, 450).Value = values[index];
        }

        sql.Append(") AS t(v) ORDER BY v");
        command.CommandText = sql.ToString();

        var ordered = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ordered.Add(reader.GetString(0));
        }

        return ordered;
    }

    private static async Task<int> ScalarIntAsync(SqlConnection connection, string text, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(text, connection);
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>Releases the barrier exactly once; further calls are no-ops.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return;
        }

        try
        {
            _transaction.Rollback();
        }
        catch (SqlException)
        {
            // already released
        }

        await _connection.DisposeAsync();
    }
}

public sealed record BarrierCoverage(IReadOnlySet<string> GrantedDescriptions, IReadOnlyList<string> ObservedGrantedModes);

/// <summary>
/// SQL-G2-B barrier: holds a <c>UPDLOCK/HOLDLOCK</c> on the existing
/// <c>AspNetUserRoles (target, Administrator)</c> row from a test-owned
/// connection. The revoke handler reaches it only after taking the
/// Administrator role-row lock (READ_COMMITTED_SNAPSHOT is enabled on the test
/// database so its pre-port reads never block), so its DELETE waits while the
/// transaction stays open with the production lock held.
/// </summary>
public sealed class MembershipRowBarrier : IAsyncDisposable
{
    public const string HintedSelect =
        "SELECT [UserId], [RoleId] FROM [dbo].[AspNetUserRoles] WITH (UPDLOCK, HOLDLOCK) " +
        "WHERE [UserId] = @userId AND [RoleId] = @roleId";

    private readonly SqlConnection _connection;
    private readonly SqlTransaction _transaction;
    private int _released;

    private MembershipRowBarrier(SqlConnection connection, SqlTransaction transaction, int sessionId)
    {
        _connection = connection;
        _transaction = transaction;
        SessionId = sessionId;
    }

    public int SessionId { get; }

    public static async Task<MembershipRowBarrier> InstallAsync(
        string connectionString,
        string userId,
        string roleId,
        CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        try
        {
            int sessionId;
            using (var session = connection.CreateCommand())
            {
                session.CommandText = "SELECT CONVERT(int, @@SPID)";
                sessionId = (int)await session.ExecuteScalarAsync(cancellationToken)!;
            }

            var transaction = connection.BeginTransaction();
            try
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = HintedSelect;
                command.Parameters.Add("@userId", SqlDbType.NVarChar, 450).Value = userId;
                command.Parameters.Add("@roleId", SqlDbType.NVarChar, 450).Value = roleId;

                var found = await command.ExecuteScalarAsync(cancellationToken);
                if (found is null)
                {
                    // The arrangement is broken (the membership row to hold does
                    // not exist) — fail closed instead of running an unraceable test.
                    transaction.Rollback();
                    throw new XunitException(
                        "The membership row to hold does not exist; the SQL-G2-B arrangement cannot be established.");
                }

                return new MembershipRowBarrier(connection, transaction, sessionId);
            }
            catch (XunitException)
            {
                throw;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return;
        }

        try
        {
            _transaction.Rollback();
        }
        catch (SqlException)
        {
            // already released
        }

        await _connection.DisposeAsync();
    }
}
