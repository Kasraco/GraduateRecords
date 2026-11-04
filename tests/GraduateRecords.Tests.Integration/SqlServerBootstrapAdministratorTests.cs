using GraduateRecords.Application.Common.Auditing;
using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Messaging;
using GraduateRecords.Application.Common.Results;
using GraduateRecords.Application.Common.Security;
using GraduateRecords.Application.Features.Administration;
using GraduateRecords.Application.Features.Administration.Bootstrap;
using GraduateRecords.Application.Features.Administration.Roles;
using GraduateRecords.Domain.Auditing;
using GraduateRecords.Infrastructure.Identity;
using GraduateRecords.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GraduateRecords.Tests.Integration;

/// <summary>
/// SQL Server proof of ADR-0016 bootstrap concurrency (approved T3c evidence
/// design). Every test runs the REAL <see cref="IdentityAdministration"/> —
/// real transaction, real <c>UPDLOCK/HOLDLOCK</c> on the Administrator role
/// row, real Identity EF stores — against a dedicated, generated database that
/// the test creates and drops itself.
/// <para>
/// Evidence discipline (approved design): stable-state verification with
/// lock-pair re-observation while a test-controlled barrier is held — never
/// samples combined across moments, never <c>most_recent_sql_handle</c> for a
/// passing assertion, never lock modes learned from a first run. Without
/// <c>GRADUATERECORDS_TEST_SQLSERVER</c> these tests are reported as Skipped.
/// </para>
/// <para>
/// Final evidence rules (corrective refinement C1–C4): (a) the barrier is shown
/// to be the <i>actual</i> blocker of the production AspNetUsers INSERT through
/// <c>blocking_session_id</c> equality with the barrier session plus membership
/// of the waited resource in the barrier's granted set on the NationalId-index
/// HoBT; (b) the holder is shown to be the <i>actual</i> blocker of the second
/// operation through <c>blocking_session_id</c> equality with the holder session
/// — zero, null or any other blocker id is failure, never a pass; (c) the
/// production locking stage is distinguished from the pre-lock role lookup by
/// requiring <c>WITH (UPDLOCK, HOLDLOCK)</c> in the current statement text
/// (<c>BootstrapEvidence.IsAdministratorRoleLockStatement</c>, unit-tested by
/// <c>RoleLockStatementMatcherTests</c>); (d) same-key identity is claimed only
/// while both resources are reported as <c>KEY</c> with string-equal
/// descriptions — under PAGE/RID granularity the documented fallback relies on
/// same database + exact HoBT + explicit resource type + blocker equality +
/// locking stage + open transaction + re-observation and makes no same-key
/// claim. Together these close FP1 (pre-lock lookup mistaken for the lock
/// statement), FP2 (a different key inside the same partition), and — combined
/// with the structural exclusivity of the closed per-test database, session
/// exclusivity and probes rolled back before any operation starts — FP3 (a
/// third holder on the NationalId index) and FP4 (a third session mistaken for
/// the contender).
/// </para>
/// </summary>
public sealed class SqlServerBootstrapAdministratorTests
{
    private const string Mobile = "09123456789";
    private const string TestingEnvironment = "Testing";

    // ---------------------------------------------------------------------
    // Layout: five sentinel rows separate two candidate gaps; both controls
    // sit outside every barrier boundary, so a control that waits proves the
    // barrier is over-broad while a candidate that does not wait proves it is
    // ineffective — either way the test fails closed.
    // ---------------------------------------------------------------------

    private sealed record BootstrapLayout(
        IReadOnlyList<string> OrderedValues,
        IReadOnlyList<string> Sentinels,
        IReadOnlyList<string> Candidates,
        IReadOnlyList<string> Controls);

    private static BootstrapLayout CreateLayout()
    {
        // Every value is computed exactly once so the ordered layout, the
        // created sentinels and the protected candidates can never disagree.
        var controlLow = ValidNationalId("1");   // control, below every sentinel
        var bufferLow = ValidNationalId("2");    // buffer sentinel (lower)
        var separatorLow = ValidNationalId("3"); // lower bound of candidate one's gap
        var candidateOne = SsoTestFlow.NextNationalId(); // 4…
        var separatorHigh = ValidNationalId("5"); // sentinel between the candidates
        var candidateTwo = ValidNationalId("6");   // candidate two
        var separatorTop = ValidNationalId("7");   // upper bound of candidate two's gap
        var bufferHigh = ValidNationalId("8");     // buffer sentinel (upper)
        var controlHigh = ValidNationalId("9");    // control, above every sentinel

        return new BootstrapLayout(
            OrderedValues: [controlLow, bufferLow, separatorLow, candidateOne, separatorHigh, candidateTwo, separatorTop, bufferHigh, controlHigh],
            Sentinels: [bufferLow, separatorLow, separatorHigh, separatorTop, bufferHigh],
            Candidates: [candidateOne, candidateTwo],
            Controls: [controlLow, controlHigh]);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static string ValidNationalId(string firstDigit)
    {
        // Same checksum algorithm as SsoTestFlow.NextNationalId / Unit/NationalIdTests.
        var nineDigits = firstDigit + "00000000";
        var sum = 0;
        for (var index = 0; index < nineDigits.Length; index++)
        {
            sum += (nineDigits[index] - '0') * (10 - index);
        }

        var remainder = sum % 11;
        var checkDigit = remainder < 2 ? remainder : 11 - remainder;
        return nineDigits + checkDigit;
    }

    private static async Task SeedCanonicalRolesAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        await SqlServerTestDatabase.SeedCanonicalRolesAsync(scope.ServiceProvider);
    }

    /// <summary>Port-level call (no Application handler): the approved SQL-G1/G2/bootstrap race shape.</summary>
    private static async Task<Result<FirstAdministratorEstablishment>> EstablishTaskAsync(ServiceProvider provider, string nationalId)
    {
        using var scope = provider.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityAdministration>();
        return await identity.EstablishFirstAdministratorAsync(
            new CreateIdentityUser(
                nationalId,
                BootstrapAdministratorHandler.BootstrapDisplayName,
                Mobile,
                LocalUserKind.Administrator,
                [RoleNames.Administrator]));
    }

    private static async Task<Result<BootstrapOutcome>> HandleTaskAsync(ServiceProvider provider, string nationalId)
    {
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<BootstrapAdministratorCommand, BootstrapOutcome>>();
        return await handler.HandleAsync(new BootstrapAdministratorCommand(nationalId, Mobile, TestingEnvironment));
    }

    private static async Task<Result<RoleChangeResult>> RevokeTaskAsync(ServiceProvider provider, string targetUserId)
    {
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RevokeRoleCommand, RoleChangeResult>>();
        return await handler.HandleAsync(new RevokeRoleCommand(targetUserId, RoleNames.Administrator, new RequestAuditContext()));
    }

    private static async Task<Result<bool>> GrantTaskAsync(ServiceProvider provider, string userId)
    {
        using var scope = provider.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityAdministration>();
        return await identity.GrantRoleAsync(userId, RoleNames.Administrator);
    }

    private static async Task<string> CreateUserAsync(ServiceProvider provider, string fullName, LocalUserKind kind, params string[] roles) =>
        await CreateUserWithNationalIdAsync(provider, SsoTestFlow.NextNationalId(), fullName, kind, roles);

    private static async Task<string> CreateUserWithNationalIdAsync(
        ServiceProvider provider, string nationalId, string fullName, LocalUserKind kind, params string[] roles)
    {
        using var scope = provider.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityAdministration>();
        var created = await identity.CreateUserAsync(
            new CreateIdentityUser(nationalId, fullName, Mobile, kind, roles));
        Assert.True(created.IsSuccess, created.Error?.Message);
        return created.Value!;
    }

    private static async Task<int> AdministratorCountAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityAdministration>();
        return (await identity.GetEffectiveAdministratorIdsAsync()).Count;
    }

    private static async Task<bool> HasUserAsync(ServiceProvider provider, string nationalId)
    {
        using var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<ILocalUserDirectory>();
        return await users.FindByNationalIdAsync(nationalId) is not null;
    }

    private static async Task<IReadOnlyList<string>> RolesOfAsync(ServiceProvider provider, string userId)
    {
        using var scope = provider.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityAdministration>();
        return await identity.GetRolesAsync(userId);
    }

    private static async Task<string> UserIdAsync(ServiceProvider provider, string nationalId)
    {
        using var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<ILocalUserDirectory>();
        var user = await users.FindByNationalIdAsync(nationalId);
        Assert.NotNull(user);
        return user!.Id;
    }

    private static async Task<string> AdministratorRoleIdAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Roles
            .AsNoTracking()
            .Where(role => role.Name == RoleNames.Administrator)
            .Select(role => role.Id)
            .SingleAsync();
    }

    private static RecordingLoggerProvider LogsOf(ServiceProvider provider) =>
        provider.GetRequiredService<RecordingLoggerProvider>();

    private static RecordingAuditWriter AuditOf(ServiceProvider provider) =>
        provider.GetRequiredService<RecordingAuditWriter>();

    /// <summary>AGENTS §14 sweep: no National ID and no mobile number may reach logs or audit payloads.</summary>
    private static void AssertNoPersonalData(ServiceProvider provider, params string[] secrets)
    {
        var text = new System.Text.StringBuilder();
        foreach (var message in LogsOf(provider).Snapshot())
        {
            text.Append('\n').Append(message);
        }

        foreach (var entry in AuditOf(provider).Snapshot())
        {
            text.Append('\n').Append(entry.Action)
                .Append('|').Append(entry.Summary)
                .Append('|').Append(entry.DetailsJson)
                .Append('|').Append(entry.EntityId);
        }

        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, text.ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Release-before-drain (approved cleanup): waits for a test-started
    /// operation so no blocked command is in flight when scopes and the
    /// provider are disposed. Exceptions are swallowed — they belong to the
    /// operation and resurface through the real await after a successful run.
    /// </summary>
    private static async Task DrainQuietlyAsync(Task? operation)
    {
        if (operation is null)
        {
            return;
        }

        try
        {
            await operation.WaitAsync(EvidencePoll.SafetyBound);
        }
        catch
        {
            // cleanup only
        }
    }

    private static string DescribeTask(string name, Task? task)
    {
        if (task is null)
        {
            return $"{name}=not started";
        }

        if (task.IsFaulted)
        {
            var failure = task.Exception?.GetBaseException();
            return $"{name}=faulted {failure?.GetType().FullName}: {failure?.Message}";
        }

        if (task.IsCanceled)
        {
            return $"{name}=canceled";
        }

        if (task.IsCompletedSuccessfully)
        {
            return $"{name}=completed successfully";
        }

        return $"{name}=pending({task.Status})";
    }

    private static void CountingPort(IServiceCollection services)
    {
        services.RemoveAll<IIdentityAdministration>();
        services.AddScoped<IdentityAdministration>();
        services.AddScoped<CountingIdentityAdministration>(provider =>
            new CountingIdentityAdministration(provider.GetRequiredService<IdentityAdministration>()));
        services.AddScoped<IIdentityAdministration>(provider =>
            provider.GetRequiredService<CountingIdentityAdministration>());
    }

    // ---------------------------------------------------------------------
    // SQL-A — primary regression: two competing Establish calls, one winner.
    // ---------------------------------------------------------------------

    [SqlServerFact]
    public async Task Two_concurrent_establish_operations_contend_on_the_role_row_lock()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var provider = database.BuildServiceProvider();
        await SeedCanonicalRolesAsync(provider);

        var layout = CreateLayout();
        foreach (var sentinel in layout.Sentinels)
        {
            _ = await CreateUserWithNationalIdAsync(provider, sentinel, "کاربر مرزی", LocalUserKind.Staff);
        }

        await using var diagnostics = await SqlServerLockDiagnostics.CreateAsync(database.ConnectionString);
        await using var barrier = await NationalIdIndexBarrier.InstallAsync(database.ConnectionString, layout.Candidates);
        await barrier.ValidateCoverageAsync(
            database.ConnectionString, diagnostics, layout.OrderedValues, layout.Candidates, layout.Controls);

        // Operation one reaches the production INSERT and is pinned behind the
        // barrier while holding the Administrator role-row lock (T1 evidence).
        Task<Result<FirstAdministratorEstablishment>>? first = null;
        Task<Result<FirstAdministratorEstablishment>>? second = null;
        try
        {
            first = EstablishTaskAsync(provider, layout.Candidates[0]);
            var insertEvidence = await diagnostics.WaitForEstablishInsertAsync(barrier);

            // Operation two then queues on the identical role-row KEY.
            second = EstablishTaskAsync(provider, layout.Candidates[1]);
            var waiterEvidence = await diagnostics.WaitForRoleLockWaiterAsync(
                insertEvidence.SessionId, insertEvidence.RoleLockDescription, insertEvidence.RoleLockType,
                barrier.SessionId,
                extraContext: () => DescribeTask("second establish", second));
            Assert.NotEqual(insertEvidence.SessionId, waiterEvidence.SessionId);
        }
        finally
        {
            // Release-before-drain: nothing stays blocked while scopes/provider dispose.
            await barrier.DisposeAsync();
            await DrainQuietlyAsync(first);
            await DrainQuietlyAsync(second);
        }

        var firstOutcome = await EvidencePoll.AwaitAsync(
            first!, "the first Establish call", () => diagnostics.DumpAsync("first establish"));
        var secondOutcome = await EvidencePoll.AwaitAsync(
            second!, "the second Establish call", () => diagnostics.DumpAsync("second establish"));

        Assert.Equal(FirstAdministratorEstablishment.Created, firstOutcome.Value);
        Assert.Equal(FirstAdministratorEstablishment.AlreadyEstablished, secondOutcome.Value);
        Assert.Equal(1, await AdministratorCountAsync(provider));
        Assert.False(
            await HasUserAsync(provider, layout.Candidates[1]),
            "The losing National ID must not have produced a user row.");

        AssertNoPersonalData(provider, layout.Candidates[0], layout.Candidates[1], Mobile);
    }

    // ---------------------------------------------------------------------
    // SQL-G1 — deterministic sequential ordering tests (labelled as such: they
    // verify the legal orders, the concurrent proof is SQL-A above).
    // ---------------------------------------------------------------------

    [SqlServerFact]
    public async Task Grant_commits_first_then_bootstrap_observes_the_administrator_and_is_inert()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var provider = database.BuildServiceProvider();
        await SeedCanonicalRolesAsync(provider);

        var user = await CreateUserAsync(provider, "کاربر نخست", LocalUserKind.Staff);
        var granted = await GrantTaskAsync(provider, user);
        Assert.True(granted.IsSuccess, granted.Error?.Message);

        var candidate = SsoTestFlow.NextNationalId();
        var outcome = await EstablishTaskAsync(provider, candidate);

        Assert.Equal(FirstAdministratorEstablishment.AlreadyEstablished, outcome.Value);
        Assert.Equal(1, await AdministratorCountAsync(provider));
        Assert.False(await HasUserAsync(provider, candidate));
        Assert.Equal(0, AuditOf(provider).CountFor(AuditActions.BootstrapAdministratorProvisioned));
        AssertNoPersonalData(provider, candidate, Mobile);
    }

    [SqlServerFact]
    public async Task Bootstrap_commits_first_then_the_uc3_grant_may_establish_a_second_administrator()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var provider = database.BuildServiceProvider();
        await SeedCanonicalRolesAsync(provider);

        var candidate = SsoTestFlow.NextNationalId();
        var outcome = await EstablishTaskAsync(provider, candidate);
        Assert.Equal(FirstAdministratorEstablishment.Created, outcome.Value);

        var user = await CreateUserAsync(provider, "کاربر دوم", LocalUserKind.Staff);
        var granted = await GrantTaskAsync(provider, user);
        Assert.True(granted.IsSuccess, granted.Error?.Message);

        // Legal sequential result: both operations fully applied, no duplicates.
        Assert.Equal(2, await AdministratorCountAsync(provider));
        Assert.True(await HasUserAsync(provider, candidate));
        var creatorId = await UserIdAsync(provider, candidate);
        Assert.Contains(RoleNames.Administrator, await RolesOfAsync(provider, creatorId));
        Assert.Contains(RoleNames.Administrator, await RolesOfAsync(provider, user));
        AssertNoPersonalData(provider, candidate, Mobile);
    }

    // ---------------------------------------------------------------------
    // SQL-G2-A — sequential ordering tests (revoke vs bootstrap) plus the
    // last-Administrator assertion.
    // ---------------------------------------------------------------------

    [SqlServerFact]
    public async Task Bootstrap_completes_before_revoke_and_the_set_stays_non_empty()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var provider = database.BuildServiceProvider();
        await SeedCanonicalRolesAsync(provider);

        var firstAdminNationalId = SsoTestFlow.NextNationalId();
        var first = await EstablishTaskAsync(provider, firstAdminNationalId);
        Assert.Equal(FirstAdministratorEstablishment.Created, first.Value);
        var firstAdminId = await UserIdAsync(provider, firstAdminNationalId);
        _ = await CreateUserAsync(provider, "مدیر دوم", LocalUserKind.Administrator, RoleNames.Administrator);

        var candidate = SsoTestFlow.NextNationalId();
        var bootstrap = await EstablishTaskAsync(provider, candidate);
        Assert.Equal(FirstAdministratorEstablishment.AlreadyEstablished, bootstrap.Value);

        var secondAdminId = await UserIdOfAdministratorAsync(provider, excludeUserId: firstAdminId);
        var revoked = await RevokeTaskAsync(provider, secondAdminId);
        Assert.True(revoked.IsSuccess, revoked.Error?.Message);

        Assert.Equal(1, await AdministratorCountAsync(provider));
        Assert.Contains(RoleNames.Administrator, await RolesOfAsync(provider, firstAdminId));
        Assert.False(await HasUserAsync(provider, candidate));
        Assert.Equal(0, AuditOf(provider).CountFor(AuditActions.BootstrapAdministratorProvisioned));
        Assert.Contains(AuditOf(provider).Snapshot(), entry =>
            entry.Action == AuditActions.AuthorizationRoleRevoked && entry.Result == AuditResult.Success);
        AssertNoPersonalData(provider, candidate, Mobile);
    }

    [SqlServerFact]
    public async Task Revoke_completes_before_bootstrap_and_the_last_administrator_stays_protected()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var provider = database.BuildServiceProvider();
        await SeedCanonicalRolesAsync(provider);

        var firstAdminNationalId = SsoTestFlow.NextNationalId();
        var first = await EstablishTaskAsync(provider, firstAdminNationalId);
        Assert.Equal(FirstAdministratorEstablishment.Created, first.Value);
        var firstAdminId = await UserIdAsync(provider, firstAdminNationalId);
        var secondAdminId = await CreateUserAsync(provider, "مدیر دوم", LocalUserKind.Administrator, RoleNames.Administrator);

        var revoked = await RevokeTaskAsync(provider, secondAdminId);
        Assert.True(revoked.IsSuccess, revoked.Error?.Message);
        Assert.Equal(1, await AdministratorCountAsync(provider));

        var candidate = SsoTestFlow.NextNationalId();
        var bootstrap = await EstablishTaskAsync(provider, candidate);
        Assert.Equal(FirstAdministratorEstablishment.AlreadyEstablished, bootstrap.Value);
        Assert.Equal(1, await AdministratorCountAsync(provider));
        Assert.False(await HasUserAsync(provider, candidate));

        // Existing last-Administrator protection stays intact (ADR-0017 §17 via
        // the real RevokeRoleHandler): the remaining Administrator cannot go.
        var lastAdminRevoke = await RevokeTaskAsync(provider, firstAdminId);
        Assert.False(lastAdminRevoke.IsSuccess);
        Assert.Equal(AdministrationErrors.LastAdministratorCode, lastAdminRevoke.Error!.Code);
        Assert.Equal(1, await AdministratorCountAsync(provider));
        Assert.Contains(RoleNames.Administrator, await RolesOfAsync(provider, firstAdminId));
        Assert.Contains(AuditOf(provider).Snapshot(), entry =>
            entry.Action == AuditActions.AuthorizationRoleRevoked && entry.Result == AuditResult.Denied);
        AssertNoPersonalData(provider, candidate, Mobile);
    }

    // ---------------------------------------------------------------------
    // SQL-G2-B — actual concurrency: the revoke handler holds the role-row
    // lock while its DELETE waits on the membership barrier; bootstrap queues
    // on the same role-row KEY. Requires READ_COMMITTED_SNAPSHOT (fail closed).
    // ---------------------------------------------------------------------

    [SqlServerFact]
    public async Task Revoke_and_bootstrap_serialize_on_the_role_row_lock_under_real_contention()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await database.EnableReadCommittedSnapshotAsync();
        await using var provider = database.BuildServiceProvider();
        await SeedCanonicalRolesAsync(provider);

        var firstAdminNationalId = SsoTestFlow.NextNationalId();
        var first = await EstablishTaskAsync(provider, firstAdminNationalId);
        Assert.Equal(FirstAdministratorEstablishment.Created, first.Value);
        var firstAdminId = await UserIdAsync(provider, firstAdminNationalId);
        var secondAdminId = await CreateUserAsync(provider, "مدیر دوم", LocalUserKind.Administrator, RoleNames.Administrator);
        var administratorRoleId = await AdministratorRoleIdAsync(provider);

        await using var diagnostics = await SqlServerLockDiagnostics.CreateAsync(database.ConnectionString);
        await using var membershipBarrier = await MembershipRowBarrier.InstallAsync(
            database.ConnectionString, secondAdminId, administratorRoleId);

        var candidate = SsoTestFlow.NextNationalId();
        Task<Result<RoleChangeResult>>? revoke = null;
        Task<Result<FirstAdministratorEstablishment>>? bootstrap = null;
        try
        {
            revoke = RevokeTaskAsync(provider, secondAdminId);
            var revokeEvidence = await diagnostics.WaitForRevokeDeleteAsync(
                membershipBarrier, extraContext: () => DescribeTask("revoke", revoke));

            bootstrap = EstablishTaskAsync(provider, candidate);
            var waiterEvidence = await diagnostics.WaitForRoleLockWaiterAsync(
                revokeEvidence.SessionId, revokeEvidence.RoleLockDescription, revokeEvidence.RoleLockType,
                membershipBarrier.SessionId,
                extraContext: () => DescribeTask("bootstrap", bootstrap) + Environment.NewLine + DescribeTask("revoke", revoke));
            Assert.NotEqual(revokeEvidence.SessionId, waiterEvidence.SessionId);
        }
        finally
        {
            // Release-before-drain: nothing stays blocked while scopes/provider dispose.
            await membershipBarrier.DisposeAsync();
            await DrainQuietlyAsync(revoke);
            await DrainQuietlyAsync(bootstrap);
        }

        var revoked = await EvidencePoll.AwaitAsync(
            revoke!, "the revoke handler", () => diagnostics.DumpAsync("revoke"));
        var bootstrapOutcome = await EvidencePoll.AwaitAsync(
            bootstrap!, "the bootstrap call", () => diagnostics.DumpAsync("bootstrap"));

        // Both operations fully applied exactly once; bootstrap wrote nothing.
        Assert.True(revoked.IsSuccess, revoked.Error?.Message);
        Assert.Equal(FirstAdministratorEstablishment.AlreadyEstablished, bootstrapOutcome.Value);
        Assert.Equal(1, await AdministratorCountAsync(provider));
        Assert.Contains(RoleNames.Administrator, await RolesOfAsync(provider, firstAdminId));
        Assert.DoesNotContain(RoleNames.Administrator, await RolesOfAsync(provider, secondAdminId));
        Assert.False(await HasUserAsync(provider, candidate));
        Assert.Equal(0, AuditOf(provider).CountFor(AuditActions.BootstrapAdministratorProvisioned));
        Assert.Contains(AuditOf(provider).Snapshot(), entry =>
            entry.Action == AuditActions.AuthorizationRoleRevoked && entry.Result == AuditResult.Success);

        // Last-Administrator protection remains intact after the race.
        var lastAdminRevoke = await RevokeTaskAsync(provider, firstAdminId);
        Assert.False(lastAdminRevoke.IsSuccess);
        Assert.Equal(AdministrationErrors.LastAdministratorCode, lastAdminRevoke.Error!.Code);
        Assert.Equal(1, await AdministratorCountAsync(provider));
        Assert.Contains(RoleNames.Administrator, await RolesOfAsync(provider, firstAdminId));

        AssertNoPersonalData(provider, candidate, Mobile);
    }

    // ---------------------------------------------------------------------
    // Handler-level audit coverage (counting decorator proves fast-path
    // behaviour instead of inferring it from outcomes).
    // ---------------------------------------------------------------------

    [SqlServerFact]
    public async Task Handler_second_invocation_is_inert_through_the_fast_path()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var provider = database.BuildServiceProvider(CountingPort);
        await SeedCanonicalRolesAsync(provider);

        var first = SsoTestFlow.NextNationalId();
        var second = SsoTestFlow.NextNationalId();

        using (var scope = provider.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<BootstrapAdministratorCommand, BootstrapOutcome>>();
            var counter = scope.ServiceProvider.GetRequiredService<CountingIdentityAdministration>();

            var firstOutcome = await handler.HandleAsync(new BootstrapAdministratorCommand(first, Mobile, TestingEnvironment));
            var secondOutcome = await handler.HandleAsync(new BootstrapAdministratorCommand(second, Mobile, TestingEnvironment));

            // Order is deterministic here: sequential invocations, so the first
            // created and the second was inert through the fast path.
            Assert.Equal(BootstrapOutcome.Created, firstOutcome.Value);
            Assert.Equal(BootstrapOutcome.AlreadyEstablished, secondOutcome.Value);

            // The second invocation short-circuited: the port was entered exactly once.
            Assert.Equal(1, counter.EstablishCalls);
        }

        var audit = AuditOf(provider);
        Assert.Equal(1, audit.CountFor(AuditActions.BootstrapAdministratorProvisioned));
        Assert.Equal(0, audit.CountFor(AuditActions.BootstrapAdministratorRoleEstablished));
        Assert.Contains(audit.Snapshot(), entry =>
            entry.Action == AuditActions.BootstrapAdministratorProvisioned
            && entry.Result == AuditResult.Success
            && entry.ActorUserId is null);
        Assert.Equal(1, await AdministratorCountAsync(provider));
        Assert.False(await HasUserAsync(provider, second));
        AssertNoPersonalData(provider, first, second, Mobile);
    }

    [SqlServerFact]
    public async Task Two_concurrent_bootstrap_handlers_emit_exactly_one_provisioning_audit()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var provider = database.BuildServiceProvider(CountingPort);
        await SeedCanonicalRolesAsync(provider);

        var layout = CreateLayout();
        foreach (var sentinel in layout.Sentinels)
        {
            _ = await CreateUserWithNationalIdAsync(provider, sentinel, "کاربر مرزی", LocalUserKind.Staff);
        }

        await using var diagnostics = await SqlServerLockDiagnostics.CreateAsync(database.ConnectionString);
        await using var barrier = await NationalIdIndexBarrier.InstallAsync(database.ConnectionString, layout.Candidates);
        await barrier.ValidateCoverageAsync(
            database.ConnectionString, diagnostics, layout.OrderedValues, layout.Candidates, layout.Controls);

        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var firstHandler = firstScope.ServiceProvider.GetRequiredService<ICommandHandler<BootstrapAdministratorCommand, BootstrapOutcome>>();
        var secondHandler = secondScope.ServiceProvider.GetRequiredService<ICommandHandler<BootstrapAdministratorCommand, BootstrapOutcome>>();
        var firstCounter = firstScope.ServiceProvider.GetRequiredService<CountingIdentityAdministration>();
        var secondCounter = secondScope.ServiceProvider.GetRequiredService<CountingIdentityAdministration>();

        Task<Result<BootstrapOutcome>>? first = null;
        Task<Result<BootstrapOutcome>>? second = null;
        try
        {
            // Both handlers start back-to-back: whichever acquires the
            // Administrator role-row lock first is pinned by the barrier and the
            // other must queue on that same resource. The evidence functions are
            // agnostic to which candidate wins, so the assertions are too.
            first = firstHandler.HandleAsync(new BootstrapAdministratorCommand(layout.Candidates[0], Mobile, TestingEnvironment));
            second = secondHandler.HandleAsync(new BootstrapAdministratorCommand(layout.Candidates[1], Mobile, TestingEnvironment));

            var insertEvidence = await diagnostics.WaitForEstablishInsertAsync(barrier);
            await diagnostics.WaitForRoleLockWaiterAsync(
                insertEvidence.SessionId, insertEvidence.RoleLockDescription, insertEvidence.RoleLockType,
                barrier.SessionId,
                extraContext: () =>
                    $"barrier={barrier.SessionId} diagnostics={diagnostics.SessionId} " +
                    $"firstCalls={firstCounter.EstablishCalls} secondCalls={secondCounter.EstablishCalls}");

            // Both invocations passed the application fast path and reached the
            // port BEFORE the barrier is released - observed through the counting
            // decorator, not inferred from outcomes.
            Assert.Equal(1, firstCounter.EstablishCalls);
            Assert.Equal(1, secondCounter.EstablishCalls);
            Assert.Equal(0, AuditOf(provider).CountFor(AuditActions.BootstrapAdministratorProvisioned));
        }
        finally
        {
            // Release-before-drain: nothing stays blocked while scopes/provider dispose.
            await barrier.DisposeAsync();
            await DrainQuietlyAsync(first);
            await DrainQuietlyAsync(second);
        }

        var firstOutcome = await EvidencePoll.AwaitAsync(
            first!, "the first bootstrap handler", () => diagnostics.DumpAsync("first handler"));
        var secondOutcome = await EvidencePoll.AwaitAsync(
            second!, "the second bootstrap handler", () => diagnostics.DumpAsync("second handler"));

        // Whichever handler acquired the lock created its identity; the other was
        // inert - only the one-of-each contract is asserted, never the order.
        var candidateOutcomes = new[]
        {
            (NationalId: layout.Candidates[0], Outcome: firstOutcome),
            (NationalId: layout.Candidates[1], Outcome: secondOutcome),
        };
        Assert.Contains(candidateOutcomes, pair => pair.Outcome.Value == BootstrapOutcome.Created);
        Assert.Contains(candidateOutcomes, pair => pair.Outcome.Value == BootstrapOutcome.AlreadyEstablished);

        var audit = AuditOf(provider);
        Assert.Equal(1, audit.CountFor(AuditActions.BootstrapAdministratorProvisioned));
        Assert.Equal(0, audit.CountFor(AuditActions.BootstrapAdministratorRoleEstablished));
        Assert.Equal(1, await AdministratorCountAsync(provider));
        var createdIdentity = candidateOutcomes.Single(pair => pair.Outcome.Value == BootstrapOutcome.Created).NationalId;
        var inertIdentity = candidateOutcomes.Single(pair => pair.Outcome.Value == BootstrapOutcome.AlreadyEstablished).NationalId;
        Assert.True(await HasUserAsync(provider, createdIdentity));
        Assert.False(await HasUserAsync(provider, inertIdentity));
        AssertNoPersonalData(provider, layout.Candidates[0], layout.Candidates[1], Mobile);
    }

    private static async Task<string> UserIdOfAdministratorAsync(ServiceProvider provider, string excludeUserId)
    {
        using var scope = provider.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityAdministration>();
        var administrators = await identity.GetEffectiveAdministratorIdsAsync();
        var other = administrators.FirstOrDefault(id => !string.Equals(id, excludeUserId, StringComparison.Ordinal));
        Assert.NotNull(other);
        return other!;
    }
}
