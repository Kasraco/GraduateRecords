using System.Data;
using GraduateRecords.Application.Common.Auditing;
using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Results;
using GraduateRecords.Application.Common.Security;
using GraduateRecords.Domain.Auditing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Xunit.Sdk;
using PermissionNames = GraduateRecords.Application.Common.Security.Permissions;
using Scope = GraduateRecords.Application.Common.Security.StaffScope;

namespace GraduateRecords.Tests.Integration;

// ---------------------------------------------------------------------------
// Test recorders and doubles used by the SQL Server bootstrap tests.
// They observe; they never replace the production port, transaction or lock.
// ---------------------------------------------------------------------------

/// <summary>Captures formatted log messages so a test can sweep them for PII (AGENTS §14).</summary>
public sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _messages = [];
    private readonly object _gate = new();

    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
        {
            return _messages.ToList();
        }
    }

    public ILogger CreateLogger(string categoryName) => new Recorder(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Recorder(RecordingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (owner._gate)
            {
                owner._messages.Add($"{category}: {formatter(state, exception)}");
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}

/// <summary>Records every audit entry the handlers write (ADR-0016 §12 events).</summary>
public sealed class RecordingAuditWriter : IAuditWriter
{
    private readonly List<AuditEntry> _entries = [];
    private readonly object _gate = new();

    public IReadOnlyList<AuditEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToList();
        }
    }

    public int CountFor(string action) => Snapshot().Count(entry => entry.Action == action);

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _entries.Add(entry);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Authorized actor for the revoke handler under test: holds the single
/// <c>Roles.Assign</c> capability and is never one of the seeded Administrators,
/// so the self-role-change guard and the last-Administrator rule stay testable.
/// </summary>
public sealed class BootstrapTestActor : ICurrentUser
{
    private static readonly string[] ActorPermissions = [PermissionNames.RolesAssign];

    public bool IsAuthenticated => true;

    public string? UserId => "sql-bootstrap-test-actor";

    public string? DisplayName => "بازیگر آزمایشی";

    public IReadOnlyCollection<string> Roles => Array.Empty<string>();

    public IReadOnlyCollection<string> Permissions => ActorPermissions;

    public bool HasPermission(string permission) =>
        !string.IsNullOrEmpty(permission) && Permissions.Contains(permission, StringComparer.Ordinal);

    public StaffScope StaffScope => Scope.None;
}

/// <summary>
/// Decorator around the REAL port that counts invocations of the Bootstrap
/// operation — used to prove that a handler really reached the port (or really
/// short-circuited through the fast path) instead of inferring it from outcomes.
/// </summary>
public sealed class CountingIdentityAdministration(IIdentityAdministration inner) : IIdentityAdministration
{
    private int _establishCalls;

    public int EstablishCalls => Volatile.Read(ref _establishCalls);

    public Task<IdentityUserPage> ListUsersAsync(string? searchTerm, int page, int pageSize, CancellationToken cancellationToken = default) =>
        inner.ListUsersAsync(searchTerm, page, pageSize, cancellationToken);

    public Task<IdentityUserDetail?> FindUserAsync(string userId, CancellationToken cancellationToken = default) =>
        inner.FindUserAsync(userId, cancellationToken);

    public Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken cancellationToken = default) =>
        inner.GetRolesAsync(userId, cancellationToken);

    public Task<IReadOnlyList<string>> GetEffectiveAdministratorIdsAsync(CancellationToken cancellationToken = default) =>
        inner.GetEffectiveAdministratorIdsAsync(cancellationToken);

    public Task<Result<string>> CreateUserAsync(CreateIdentityUser request, CancellationToken cancellationToken = default) =>
        inner.CreateUserAsync(request, cancellationToken);

    public Task<Result<bool>> GrantRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default) =>
        inner.GrantRoleAsync(userId, roleName, cancellationToken);

    public Task<Result<bool>> RevokeRoleAsync(
        string userId,
        string roleName,
        IReadOnlyCollection<string>? observedEffectiveAdministratorIds,
        CancellationToken cancellationToken = default) =>
        inner.RevokeRoleAsync(userId, roleName, observedEffectiveAdministratorIds, cancellationToken);

    public Task<Result<FirstAdministratorEstablishment>> EstablishFirstAdministratorAsync(
        CreateIdentityUser request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _establishCalls);
        return inner.EstablishFirstAdministratorAsync(request, cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// Evidence primitives: bounded polling (termination only) and DMV reads.
// ---------------------------------------------------------------------------

/// <summary>
/// Bounded waiting for evidence states. The bound only guarantees the test
/// terminates — no assertion depends on elapsed time, sample counts or delays
/// (approved evidence design). A bound expiry is reported as
/// "expected state not observed", never as a pass.
/// </summary>
public static class EvidencePoll
{
    public static readonly TimeSpan SafetyBound = TimeSpan.FromSeconds(45);

    public static async Task<T> UntilAsync<T>(
        Func<CancellationToken, Task<T?>> probe,
        string expectation,
        Func<Task<string>> diagnostics,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var deadline = DateTime.UtcNow + SafetyBound;
        while (true)
        {
            var found = await probe(cancellationToken);
            if (found is not null)
            {
                return found;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new XunitException($"{expectation}\n{await diagnostics()}");
            }

            // Loop hygiene only — never an assertion threshold.
            await Task.Delay(25, cancellationToken);
        }
    }

    /// <summary>Awaits a test-controlled operation with the safety bound, failing with a full dump on expiry.</summary>
    public static async Task<T> AwaitAsync<T>(Task<T> operation, string what, Func<Task<string>> diagnostics)
    {
        try
        {
            return await operation.WaitAsync(SafetyBound);
        }
        catch (TimeoutException exception)
        {
            throw new XunitException($"{what} did not complete within the safety bound: {exception.Message}\n{await diagnostics()}");
        }
    }
}

public sealed record LockRow(int SessionId, string Status, string Mode, string Type, long HobtId, string? Description);

public sealed record RequestRow(
    int SessionId,
    string Status,
    string WaitType,
    string? WaitResource,
    byte[]? SqlHandle,
    int? BlockingSessionId);

public sealed record EstablishInsertEvidence(
    int SessionId,
    string InsertText,
    string WaitMode,
    string RoleLockDescription,
    string RoleLockType,
    int? BlockingSessionId);

public sealed record RoleLockWaiterEvidence(int SessionId, string Description, string StatementText, string ResourceType);

public sealed record RevokeDeleteEvidence(int SessionId, string RoleLockDescription, string DeleteText, string RoleLockType);

/// <summary>
/// DMV reads for the bootstrap concurrency evidence. Every query is
/// parameterized and every comparison stays inside one database, one
/// <c>KEY</c> resource type and one exact HoBT — <c>resource_description</c> is
/// an opaque hash and is never parsed (approved evidence design).
/// </summary>
public sealed class SqlServerLockDiagnostics : IAsyncDisposable
{
    /// <summary>Update-family modes a <c>SELECT … UPDLOCK</c> requests (approved family).</summary>
    public static readonly string[] UpdateFamily = ["U", "RangeS-U", "RangeU-U"];

    private readonly SqlConnection _connection;

    public int DatabaseId { get; }

    /// <summary>SPID of the diagnostics connection itself — reported in dumps so sessions can be identified.</summary>
    public int SessionId { get; }

    /// <summary>HoBT of the clustered primary key of <c>AspNetRoles</c> (the production role-row lock target).</summary>
    public long RolesHobt { get; }

    /// <summary>HoBT of <c>IX_AspNetUsers_NationalId</c> (the barrier target).</summary>
    public long NationalIdIndexHobt { get; }

    /// <summary>HoBT of the clustered primary key of <c>AspNetUserRoles</c> (SQL-G2-B membership target).</summary>
    public long MembershipHobt { get; }

    private SqlServerLockDiagnostics(
        SqlConnection connection, int databaseId, int sessionId, long rolesHobt, long nationalIdIndexHobt, long membershipHobt)
    {
        _connection = connection;
        DatabaseId = databaseId;
        SessionId = sessionId;
        RolesHobt = rolesHobt;
        NationalIdIndexHobt = nationalIdIndexHobt;
        MembershipHobt = membershipHobt;
    }

    public static async Task<SqlServerLockDiagnostics> CreateAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        try
        {
            var databaseId = await ScalarAsync(connection, "SELECT CONVERT(int, DB_ID())", cancellationToken);
            var sessionId = await ScalarAsync(connection, "SELECT CONVERT(int, @@SPID)", cancellationToken);
            var rolesHobt = await ClusteredHobtAsync(connection, "AspNetRoles", cancellationToken);
            var membershipHobt = await ClusteredHobtAsync(connection, "AspNetUserRoles", cancellationToken);
            var nationalIdIndexHobt = await IndexedHobtAsync(
                connection, "AspNetUsers", "IX_AspNetUsers_NationalId", cancellationToken);
            return new SqlServerLockDiagnostics(
                connection, databaseId, sessionId, rolesHobt, nationalIdIndexHobt, membershipHobt);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Lock rows on the given index partitions at row/page granularity.
    /// Key-range locking on a tiny table (AspNetRoles holds three rows) may be
    /// granted at PAGE level instead of KEY level, so the evidence never filters
    /// to KEY alone — callers apply the strongest rule that fits both cases.
    /// </summary>
    public async Task<IReadOnlyList<LockRow>> LocksAsync(params long[] hobtIds)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            "SELECT request_session_id, request_status, request_mode, resource_type, " +
            "resource_associated_entity_id, resource_description " +
            "FROM sys.dm_tran_locks " +
            "WHERE resource_database_id = @databaseId " +
            "AND resource_type IN (N'KEY', N'PAGE', N'RID') " +
            "AND resource_associated_entity_id IN (@hobt0, @hobt1, @hobt2)";
        command.Parameters.Add("@databaseId", SqlDbType.Int).Value = DatabaseId;
        for (var index = 0; index < 3; index++)
        {
            var value = index < hobtIds.Length ? hobtIds[index] : -1L;
            command.Parameters.Add($"@hobt{index}", SqlDbType.BigInt).Value = value;
        }

        var rows = new List<LockRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new LockRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return rows;
    }

    public async Task<RequestRow?> RequestAsync(int sessionId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            "SELECT session_id, status, wait_type, wait_resource, sql_handle, blocking_session_id " +
            "FROM sys.dm_exec_requests WHERE session_id = @sessionId";
        command.Parameters.Add("@sessionId", SqlDbType.Int).Value = sessionId;

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        // blocking_session_id: 0 means "no blocker"; normalize NULL and 0 to
        // null so zero/null/unexpected blocker values can never satisfy an
        // assertion (C1) — a wrong blocker is evidence failure, never a pass.
        int? blockingSessionId = null;
        if (!reader.IsDBNull(5))
        {
            var blocker = Convert.ToInt32(reader.GetValue(5));
            blockingSessionId = blocker > 0 ? blocker : null;
        }

        return new RequestRow(
            // session_id is smallint in sys.dm_exec_requests.
            Convert.ToInt32(reader.GetValue(0)),
            reader.GetString(1),
            reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : (byte[])reader.GetValue(4),
            blockingSessionId);
    }

    public async Task<string?> SqlTextAsync(byte[]? sqlHandle)
    {
        if (sqlHandle is null || sqlHandle.Length == 0)
        {
            return null;
        }

        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT [text] FROM sys.dm_exec_sql_text(@handle)";
        command.Parameters.Add("@handle", SqlDbType.VarBinary, 64).Value = sqlHandle;

        var text = await command.ExecuteScalarAsync();
        return text as string;
    }

    public async Task<int?> OpenTransactionCountAsync(int sessionId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT open_transaction_count FROM sys.dm_exec_sessions WHERE session_id = @sessionId";
        command.Parameters.Add("@sessionId", SqlDbType.Int).Value = sessionId;

        var value = await command.ExecuteScalarAsync();
        return value is int count ? count : null;
    }

    public static bool IsUpdateFamily(string mode) =>
        Array.IndexOf(UpdateFamily, mode) >= 0;

    /// <summary>Insert-side family observed for a blocked production INSERT (corroborating only).</summary>
    public static bool IsInsertFamily(string mode) =>
        mode.StartsWith("RangeI", StringComparison.Ordinal) || mode is "U" or "X";

    /// <summary>
    /// Full failure diagnostics: every lock of this database (all resource
    /// types — key-range locking on tiny tables can surface at page or object
    /// level) plus every lock-waiting request with its current statement —
    /// enough to distinguish an ineffective barrier from an over-broad one, a
    /// mis-anchored session or a wait recorded on a non-KEY resource.
    /// </summary>
    public async Task<string> DumpAsync(string context)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"[{context}] database={DatabaseId} diagnosticsSession={SessionId} rolesHobt={RolesHobt} " +
                           $"nationalIdIndexHobt={NationalIdIndexHobt} membershipHobt={MembershipHobt}");
        ThreadPool.GetAvailableThreads(out var availableWorkers, out var availableIo);
        ThreadPool.GetMaxThreads(out var maxWorkers, out var maxIo);
        builder.AppendLine(
            $"  threadpool availableWorker={availableWorkers}/{maxWorkers} availableIo={availableIo}/{maxIo} " +
            $"threadCount={ThreadPool.ThreadCount} queued={ThreadPool.PendingWorkItemCount}");

        var allLocks = new List<LockRow>();
        using (var command = _connection.CreateCommand())
        {
            command.CommandText =
                "SELECT request_session_id, request_status, request_mode, resource_type, " +
                "resource_associated_entity_id, resource_description " +
                "FROM sys.dm_tran_locks WHERE resource_database_id = @databaseId";
            command.Parameters.Add("@databaseId", SqlDbType.Int).Value = DatabaseId;

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                allLocks.Add(new LockRow(
                    Convert.ToInt32(reader.GetValue(0)),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt64(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
            }
        }

        foreach (var row in allLocks.OrderBy(row => row.SessionId).ThenBy(row => row.HobtId))
        {
            builder.AppendLine(
                $"  lock session={row.SessionId} status={row.Status} mode={row.Mode} type={row.Type} " +
                $"assoc={row.HobtId} resource={row.Description ?? "<none>"}");
        }

        var waitingSessions = allLocks
            .Where(row => !string.Equals(row.Status, "GRANT", StringComparison.OrdinalIgnoreCase))
            .Select(row => row.SessionId)
            .Distinct()
            .ToList();

        foreach (var sessionId in waitingSessions)
        {
            var request = await RequestAsync(sessionId);
            if (request is null)
            {
                continue;
            }

            var text = await SqlTextAsync(request.SqlHandle);
            builder.AppendLine(
                $"  request session={request.SessionId} status={request.Status} waitType={request.WaitType} " +
                $"waitResource={request.WaitResource ?? "<none>"} openTx={await OpenTransactionCountAsync(sessionId)}");
            builder.AppendLine($"  statement: {(text ?? "<unavailable>")}");
        }

        // Active requests and open transactions of this database — a session
        // with no lock rows (snapshot reads, idle transaction) still shows here.
        // Rows are materialized first: the connection has MARS disabled, so no
        // second command may run while a reader is open.
        var activeRequests = new List<(int SessionId, string Status, string WaitType, string? WaitResource, byte[]? Handle, int OpenTx)>();
        using (var command = _connection.CreateCommand())
        {
            command.CommandText =
                "SELECT r.session_id, r.status, ISNULL(r.wait_type, N''), ISNULL(r.wait_resource, N''), " +
                "r.sql_handle, ISNULL(s.open_transaction_count, 0) " +
                "FROM sys.dm_exec_requests r " +
                "LEFT JOIN sys.dm_exec_sessions s ON s.session_id = r.session_id " +
                "WHERE r.database_id = @databaseId OR ISNULL(s.open_transaction_count, 0) > 0";
            command.Parameters.Add("@databaseId", SqlDbType.Int).Value = DatabaseId;

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                activeRequests.Add((
                    Convert.ToInt32(reader.GetValue(0)),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : (byte[])reader.GetValue(4),
                    reader.GetInt32(5)));
            }
        }

        foreach (var request in activeRequests)
        {
            var text = await SqlTextAsync(request.Handle);
            builder.AppendLine(
                $"  active session={request.SessionId} status={request.Status} waitType={request.WaitType} " +
                $"waitResource={request.WaitResource} openTx={request.OpenTx}");
            builder.AppendLine($"  statement: {(text ?? "<unavailable>")}");
        }

        // Where every user session is currently waiting (lock or otherwise) and
        // its connection state — the decisive view for a session that holds no
        // locks because it has not executed anything yet.
        var sessions = new List<(int SessionId, string Status, int OpenTx)>();
        using (var command = _connection.CreateCommand())
        {
            command.CommandText =
                "SELECT session_id, status, open_transaction_count FROM sys.dm_exec_sessions " +
                "WHERE is_user_process = 1 AND (status <> N'sleeping' OR open_transaction_count > 0 " +
                "OR session_id >= @recentFrom)";
            command.Parameters.Add("@recentFrom", SqlDbType.Int).Value = Math.Max(1, SessionId - 40);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                sessions.Add((Convert.ToInt32(reader.GetValue(0)), reader.GetString(1), reader.GetInt32(2)));
            }
        }

        foreach (var session in sessions)
        {
            builder.AppendLine($"  session={session.SessionId} status={session.Status} openTx={session.OpenTx}");
        }

        using (var command = _connection.CreateCommand())
        {
            command.CommandText =
                "SELECT TOP 20 session_id, wait_duration_ms, wait_type, ISNULL(resource_description, N'') " +
                "FROM sys.dm_os_waiting_tasks WHERE session_id IS NOT NULL ORDER BY wait_duration_ms DESC";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                builder.AppendLine(
                    $"  waiting task session={Convert.ToInt32(reader.GetValue(0))} for={reader.GetInt64(1)}ms " +
                    $"wait={reader.GetString(2)} resource={reader.GetString(3)}");
            }
        }

        return builder.ToString();
    }

    private static async Task<long> ClusteredHobtAsync(SqlConnection connection, string tableName, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT p.hobt_id FROM sys.partitions p " +
            "JOIN sys.objects o ON o.object_id = p.object_id " +
            "WHERE o.name = @tableName AND o.type = N'U' AND p.index_id IN (0, 1)";
        command.Parameters.Add("@tableName", SqlDbType.NVarChar, 128).Value = tableName;

        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is not long hobtId)
        {
            throw new XunitException($"Could not resolve the clustered HoBT of '{tableName}'.");
        }

        return hobtId;
    }

    private static async Task<long> IndexedHobtAsync(SqlConnection connection, string tableName, string indexName, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT p.hobt_id FROM sys.partitions p " +
            "JOIN sys.objects o ON o.object_id = p.object_id " +
            "JOIN sys.indexes i ON i.object_id = p.object_id AND i.index_id = p.index_id " +
            "WHERE o.name = @tableName AND i.name = @indexName";
        command.Parameters.Add("@tableName", SqlDbType.NVarChar, 128).Value = tableName;
        command.Parameters.Add("@indexName", SqlDbType.NVarChar, 128).Value = indexName;

        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is not long hobtId)
        {
            throw new XunitException($"Could not resolve the HoBT of index '{indexName}' on '{tableName}'.");
        }

        return hobtId;
    }

    private static async Task<int> ScalarAsync(SqlConnection connection, string text, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(text, connection);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is int id ? id : throw new XunitException($"Scalar query returned '{value}'.");
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}

/// <summary>
/// The three stable-state evidence waits of the approved design. Each one
/// verifies, inside a single probe iteration: current request visible and
/// suspended with a lock wait, a non-null current <c>sql_handle</c> whose text
/// identifies the exact statement, the required lock pairing on the exact
/// HoBTs, an open transaction — and then re-observes the pairing so no pass is
/// ever assembled from stale samples. Lock modes are recorded as corroborating
/// diagnostics only; they are never learned from a first run and never decide
/// a result.
/// </summary>
public static class BootstrapEvidence
{
    /// <summary>
    /// T1: the production AspNetUsers INSERT waits behind the barrier while the
    /// same session holds the Administrator role-row lock. Evidence (C1/C3/C4):
    /// a currently visible suspended request with an LCK wait and a non-null
    /// current <c>sql_handle</c> whose text is the actual INSERT; the request's
    /// <c>blocking_session_id</c> equals the barrier session — the barrier is
    /// the actual blocker (zero, null or any other blocker is failure, never
    /// inferred from exclusivity); the waited <c>resource_description</c>
    /// belongs to the barrier's granted set on the exact NationalId-index HoBT;
    /// an open transaction; and a fresh re-observation of the pairing, so no
    /// pass is ever assembled from stale samples.
    /// </summary>
    public static Task<EstablishInsertEvidence> WaitForEstablishInsertAsync(
        this SqlServerLockDiagnostics diagnostics,
        NationalIdIndexBarrier barrier,
        CancellationToken cancellationToken = default) =>
        EvidencePoll.UntilAsync<EstablishInsertEvidence>(
            async token =>
            {
                // C3: refresh the barrier's granted resource set — the INSERT's
                // waited resource must be a member of it below. Stable while the
                // barrier is held, but read fresh on every sample so the
                // membership assertion never rests on a stale observation.
                await barrier.ReadCoverageAsync(diagnostics);

                var locks = await diagnostics.LocksAsync(diagnostics.RolesHobt, diagnostics.NationalIdIndexHobt);

                // The barrier's own granted locks must still be present — the
                // observation happened inside the pinned window.
                var barrierHolds = locks.Any(row =>
                    row.SessionId == barrier.SessionId
                    && row.HobtId == diagnostics.NationalIdIndexHobt
                    && row.Status == "GRANT");
                if (!barrierHolds)
                {
                    return null;
                }

                // Prefer KEY rows when selecting the holder so the description
                // handed to the waiter check is a key description (page-level
                // range locks on the tiny table remain a fallback).
                foreach (var roleLock in locks.Where(row =>
                             row.HobtId == diagnostics.RolesHobt && row.Status == "GRANT")
                             .OrderBy(row => string.Equals(row.Type, "KEY", StringComparison.Ordinal) ? 0 : 1))
                {
                    var insertWait = locks.FirstOrDefault(row =>
                        row.HobtId == diagnostics.NationalIdIndexHobt
                        && row.Status == "WAIT"
                        && row.SessionId == roleLock.SessionId);
                    if (insertWait is null)
                    {
                        continue;
                    }

                    // C3: the INSERT's waited resource must belong to the
                    // barrier's granted set — same database, KEY/PAGE/RID
                    // family and exact NationalId-index HoBT are already
                    // enforced by LocksAsync and the hobt filters.
                    if (insertWait.Description is null
                        || !barrier.GrantedDescriptions.Contains(insertWait.Description))
                    {
                        continue;
                    }

                    var request = await diagnostics.RequestAsync(roleLock.SessionId);
                    if (request is null
                        || !string.Equals(request.Status, "suspended", StringComparison.OrdinalIgnoreCase)
                        || !request.WaitType.StartsWith("LCK", StringComparison.Ordinal)
                        || request.SqlHandle is null)
                    {
                        continue;
                    }

                    var text = await diagnostics.SqlTextAsync(request.SqlHandle);
                    if (text is null
                        || !text.Contains("INSERT", StringComparison.OrdinalIgnoreCase)
                        || !text.Contains("AspNetUsers", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (await diagnostics.OpenTransactionCountAsync(roleLock.SessionId) is not > 0)
                    {
                        continue;
                    }

                    // C1: the INSERT request must be blocked by the barrier
                    // session itself. Zero, null or any other blocker id is
                    // evidence failure — never a pass, never inferred.
                    if (request.BlockingSessionId != barrier.SessionId)
                    {
                        continue;
                    }

                    // Re-observe the pairing in a fresh snapshot: the pass must
                    // rest on a currently consistent state, never on samples
                    // combined from different moments.
                    var again = await diagnostics.LocksAsync(diagnostics.RolesHobt, diagnostics.NationalIdIndexHobt);
                    var pairingHolds =
                        again.Any(row => row.SessionId == roleLock.SessionId && row.HobtId == diagnostics.RolesHobt && row.Status == "GRANT")
                        && again.Any(row => row.SessionId == roleLock.SessionId && row.HobtId == diagnostics.NationalIdIndexHobt && row.Status == "WAIT");
                    if (!pairingHolds)
                    {
                        continue;
                    }

                    return new EstablishInsertEvidence(
                        roleLock.SessionId, text, insertWait.Mode, roleLock.Description ?? string.Empty,
                        roleLock.Type, request.BlockingSessionId);
                }

                return null;
            },
            "expected exactly one session holding the Administrator role-row GRANT while its AspNetUsers " +
            "INSERT waits on the IX_AspNetUsers_NationalId HoBT (current request + sql_handle text required)",
            () => diagnostics.DumpAsync("establish-insert T1"),
            cancellationToken);

    /// <summary>
    /// A second production operation queued on the Administrator role-row lock
    /// the holder grants. Final evidence rule (C1–C4):
    /// <list type="bullet">
    /// <item><b>Blocker identity (C1):</b> the waiting request's
    /// <c>blocking_session_id</c> must equal the holder session — zero, null or
    /// any other blocker is evidence failure, never a pass; the blocker is never
    /// inferred from session exclusivity alone.</item>
    /// <item><b>Stage (C2):</b> the current statement must be the production
    /// Administrator locking statement (<c>WITH (UPDLOCK, HOLDLOCK)</c> on
    /// <c>AspNetRoles</c>), which explicitly rejects the pre-lock
    /// <c>FindByNameAsync</c> lookup that also names <c>AspNetRoles</c>.</item>
    /// <item><b>Resource identity (C3):</b> while both holder and waiter rows
    /// are <c>KEY</c>, their <c>resource_description</c> values must be
    /// string-equal — that is the same-key proof.</item>
    /// <item><b>Documented granularity fallback (C3/C4):</b> on this three-row
    /// table SQL Server may report the holder at KEY level and the waiter at
    /// PAGE/RID level (or the reverse). Opaque descriptions are then NOT
    /// compared across resource types and <b>no same-key claim is made</b>;
    /// identity rests on the same database, the exact rolesHobt, explicit
    /// resource-type evidence, <c>blocking_session_id</c> equality, the
    /// production locking statement, the open transaction and a fresh
    /// re-observation of the pairing.</item>
    /// </list>
    /// </summary>
    public static Task<RoleLockWaiterEvidence> WaitForRoleLockWaiterAsync(
        this SqlServerLockDiagnostics diagnostics,
        int holderSessionId,
        string holderDescription,
        string holderType,
        int barrierSessionId,
        Func<string>? extraContext = null,
        CancellationToken cancellationToken = default) =>
        EvidencePoll.UntilAsync<RoleLockWaiterEvidence>(
            async token =>
            {
                var locks = await diagnostics.LocksAsync(diagnostics.RolesHobt);
                foreach (var wait in locks.Where(row =>
                             row.HobtId == diagnostics.RolesHobt
                             && row.Status == "WAIT"
                             && row.SessionId != holderSessionId
                             && row.SessionId != barrierSessionId
                             && IsRoleRowLockResource(row)))
                {
                    if (wait.Description is null)
                    {
                        // C3: resource identity must be explicit — a row-level
                        // lock without a description cannot be evidence.
                        continue;
                    }

                    var request = await diagnostics.RequestAsync(wait.SessionId);
                    if (request is null
                        || !string.Equals(request.Status, "suspended", StringComparison.OrdinalIgnoreCase)
                        || !request.WaitType.StartsWith("LCK", StringComparison.Ordinal)
                        || request.SqlHandle is null)
                    {
                        continue;
                    }

                    // C1: the waiter must be blocked BY THE HOLDER session.
                    // Zero, null or any other blocker fails this sample.
                    if (request.BlockingSessionId != holderSessionId)
                    {
                        continue;
                    }

                    var text = await diagnostics.SqlTextAsync(request.SqlHandle);
                    if (text is null || !IsAdministratorRoleLockStatement(text))
                    {
                        // C2: the pre-lock FindByNameAsync lookup is rejected here.
                        continue;
                    }

                    if (await diagnostics.OpenTransactionCountAsync(wait.SessionId) is not > 0)
                    {
                        continue;
                    }

                    // C3: same-key proof is mandatory while both sides are KEY.
                    if (string.Equals(wait.Type, "KEY", StringComparison.Ordinal)
                        && string.Equals(holderType, "KEY", StringComparison.Ordinal)
                        && !string.Equals(wait.Description, holderDescription, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // C3/C4 documented fallback: if either side is PAGE/RID the
                    // descriptions are different resource labels and are not
                    // comparable — identity then rests on the HoBT, the explicit
                    // resource types, blocking_session_id, the locking statement,
                    // the open transaction and the re-observation below. No
                    // same-key claim is made in that branch.

                    var again = await diagnostics.LocksAsync(diagnostics.RolesHobt);
                    if (!again.Any(row => row.SessionId == wait.SessionId && row.Status == "WAIT"
                                           && IsRoleRowLockResource(row)))
                    {
                        continue;
                    }

                    return new RoleLockWaiterEvidence(
                        wait.SessionId, wait.Description, text, wait.Type);
                }

                return null;
            },
            $"expected a second session waiting on the Administrator role-row resource of {diagnostics.RolesHobt} " +
            $"blocked by the holder (session {holderSessionId}, lock {holderDescription}/{holderType}): " +
            "production WITH (UPDLOCK, HOLDLOCK) statement, row-level KEY/PAGE/RID resource, " +
            "blocking_session_id equality, open transaction, re-observed pairing",
            async () => await diagnostics.DumpAsync("role-row waiter") + (extraContext is null ? string.Empty : Environment.NewLine + extraContext()),
            cancellationToken);

    /// <summary>
    /// A row-level lock resource on the role-row partition: KEY, PAGE or RID.
    /// The HoBT itself is already pinned by the query; the lock *granularity*
    /// on this three-row table varies (observed KEY vs PAGE/RID), so granularity
    /// is validated as a family and the exact description stays diagnostic.
    /// </summary>
    private static bool IsRoleRowLockResource(LockRow row) =>
        row.Type is "KEY" or "PAGE" or "RID";

    /// <summary>
    /// C2: true only for the production Administrator role-row locking statement
    /// executed by the port — <c>SELECT … FROM [AspNetRoles] WITH (UPDLOCK,
    /// HOLDLOCK) WHERE [Id] = …</c> (EF may prefix a parameter declaration and
    /// SET lines around it). Explicitly rejects the pre-lock <c>RoleManager</c>
    /// lookup (<c>… WHERE [r].[NormalizedName] = …</c>), which also names
    /// <c>AspNetRoles</c> but carries no lock hints, and rejects statements whose
    /// only mention of UPDLOCK/HOLDLOCK is not the actual WITH-clause (the hint
    /// must follow <c>WITH (</c>). Narrowly scoped on purpose: three positive
    /// markers plus one negative marker, ordinal-ignore-case, no whitespace or
    /// punctuation normalization of the SQL text — pinned by
    /// <c>RoleLockStatementMatcherTests</c>.
    /// </summary>
    public static bool IsAdministratorRoleLockStatement(string? sql) =>
        sql is not null
        && sql.Contains("AspNetRoles", StringComparison.OrdinalIgnoreCase)
        && sql.Contains("WITH (UPDLOCK", StringComparison.OrdinalIgnoreCase)
        && sql.Contains("HOLDLOCK", StringComparison.OrdinalIgnoreCase)
        && !sql.Contains("NormalizedName", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// SQL-G2-B: the revoke handler holds the Administrator role-row lock while
    /// its AspNetUserRoles DELETE waits on the membership barrier.
    /// </summary>
    public static Task<RevokeDeleteEvidence> WaitForRevokeDeleteAsync(
        this SqlServerLockDiagnostics diagnostics,
        MembershipRowBarrier barrier,
        Func<string>? extraContext = null,
        CancellationToken cancellationToken = default) =>
        EvidencePoll.UntilAsync<RevokeDeleteEvidence>(
            async token =>
            {
                var locks = await diagnostics.LocksAsync(diagnostics.RolesHobt, diagnostics.MembershipHobt);
                foreach (var roleLock in locks.Where(row =>
                             row.HobtId == diagnostics.RolesHobt && row.Status == "GRANT"))
                {
                    var deleteWait = locks.FirstOrDefault(row =>
                        row.HobtId == diagnostics.MembershipHobt
                        && row.Status == "WAIT"
                        && row.SessionId == roleLock.SessionId);
                    if (deleteWait is null)
                    {
                        continue;
                    }

                    var request = await diagnostics.RequestAsync(roleLock.SessionId);
                    if (request is null
                        || !string.Equals(request.Status, "suspended", StringComparison.OrdinalIgnoreCase)
                        || !request.WaitType.StartsWith("LCK", StringComparison.Ordinal)
                        || request.SqlHandle is null)
                    {
                        continue;
                    }

                    var text = await diagnostics.SqlTextAsync(request.SqlHandle);
                    if (text is null
                        || !text.Contains("DELETE", StringComparison.OrdinalIgnoreCase)
                        || !text.Contains("AspNetUserRoles", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (await diagnostics.OpenTransactionCountAsync(roleLock.SessionId) is not > 0)
                    {
                        continue;
                    }

                    var again = await diagnostics.LocksAsync(diagnostics.RolesHobt, diagnostics.MembershipHobt);
                    var pairingHolds =
                        again.Any(row => row.SessionId == roleLock.SessionId && row.HobtId == diagnostics.RolesHobt && row.Status == "GRANT")
                        && again.Any(row => row.SessionId == roleLock.SessionId && row.HobtId == diagnostics.MembershipHobt && row.Status == "WAIT");
                    if (!pairingHolds)
                    {
                        continue;
                    }

                    return new RevokeDeleteEvidence(
                        roleLock.SessionId, roleLock.Description ?? string.Empty, text, roleLock.Type);
                }

                return null;
            },
            "expected the revoke session to hold the Administrator role-row GRANT while its AspNetUserRoles " +
            "DELETE waits on the membership barrier (current request + sql_handle text required)",
            async () => await diagnostics.DumpAsync("revoke-delete") + (extraContext is null ? string.Empty : Environment.NewLine + extraContext()),
            cancellationToken);
}
