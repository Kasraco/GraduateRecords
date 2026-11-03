using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Results;
using GraduateRecords.Application.Common.Security;
using GraduateRecords.Application.Features.Administration;
using GraduateRecords.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace GraduateRecords.Tests.Integration;

/// <summary>
/// The SQL Server proof of ADR-0017 §17–§19 (see the UC3 completion report).
/// <para>
/// Runs the <b>real</b> <c>IdentityAdministration</c> — real transaction, real
/// <c>UPDLOCK/HOLDLOCK</c> row lock, real Identity EF stores — against a
/// dedicated, uniquely named database that this test creates and drops itself.
/// It never touches an existing application database: the server comes from
/// <see cref="SqlServerFactAttribute.ConnectionStringVariable"/>, the database
/// name is generated per run, and cleanup only ever drops that generated name.
/// </para>
/// <para>
/// Without the environment variable these tests are reported as Skipped by
/// <see cref="SqlServerFactAttribute"/> and nothing here executes.
/// </para>
/// </summary>
public sealed class SqlServerLastAdministratorInvariantTests
{
    /// <summary>
    /// The core invariant: two revocations that both observed the same two
    /// Administrators must not both succeed. Exactly one wins; the loser gets
    /// <c>identity.admin.concurrent-change</c> and writes nothing; exactly one
    /// effective Administrator remains (ADR-0017 §19).
    /// </summary>
    [SqlServerFact]
    public async Task Two_concurrent_revocations_cannot_leave_zero_effective_administrators()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var provider = database.BuildServiceProvider();

        var (firstAdmin, secondAdmin, observed) = await SeedTwoAdministratorsAsync(provider);
        Assert.Equal(2, observed.Count);

        var outcomes = await Task.WhenAll(
            RevokeAdministratorAsync(provider, secondAdmin, observed),
            RevokeAdministratorAsync(provider, firstAdmin, observed));

        Assert.Equal(1, outcomes.Count(outcome => outcome.IsSuccess));
        var loser = outcomes.Single(outcome => !outcome.IsSuccess);
        Assert.Equal(ErrorKind.Conflict, loser.Error!.Kind);
        Assert.Equal(AdministrationErrors.ConcurrentAdministratorChangeCode, loser.Error.Code);

        Assert.Equal(1, await CountAdministratorsAsync(provider)); // one removed, one left — never zero
    }

    /// <summary>
    /// A grant and a revocation issued together serialize on the same lock, so
    /// the combined result can never be empty (ADR-0017 §17 covers every
    /// mutating path, not only revocations).
    /// <para>
    /// The two operations are serialized in one of exactly two orders, and the
    /// contract allows <b>either</b> (ADR-0017 §19 only forbids both succeeding
    /// when the combined result would leave zero Administrators — a grant plus
    /// a revocation can never do that):
    /// </para>
    /// <list type="number">
    /// <item><b>Revocation wins the lock:</b> its observation is still fresh, so
    /// it applies, and the grant then applies too — both succeed, 2 remain.</item>
    /// <item><b>Grant wins the lock:</b> the revocation's observed set has
    /// become stale, so the exact-set precondition rejects it with
    /// <c>identity.admin.concurrent-change</c> and it writes nothing — 3 remain.
    /// A grant itself never fails: no grant path carries a precondition.</item>
    /// </list>
    /// Asserting "both succeed" would assert an ordering the contract does not
    /// promise; the invariant that must hold under every interleaving is that
    /// the administrator set is never left empty.
    /// </summary>
    [SqlServerFact]
    public async Task A_concurrent_grant_and_revocation_never_leave_zero_effective_administrators()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var provider = database.BuildServiceProvider();

        var (firstAdmin, secondAdmin, observed) = await SeedTwoAdministratorsAsync(provider);
        var newcomerId = await CreateUserAsync(provider, "کاربر تازه");

        var outcomes = await Task.WhenAll(
            RevokeAdministratorAsync(provider, secondAdmin, observed),
            GrantAdministratorAsync(provider, newcomerId));

        var revoke = outcomes[0];
        var grant = outcomes[1];

        // A grant carries no observed-set precondition (ADR-0017 §19 does not
        // require a grant to fail, and no grant path has one to lose).
        Assert.True(grant.IsSuccess, grant.Error?.Message);

        // The revocation either applied against a fresh observation or was
        // rejected as a stale decision — never anything else.
        if (!revoke.IsSuccess)
        {
            Assert.Equal(ErrorKind.Conflict, revoke.Error!.Kind);
            Assert.Equal(AdministrationErrors.ConcurrentAdministratorChangeCode, revoke.Error.Code);
        }

        // Security invariant (ADR-0017 §19): whatever the interleaving, the
        // administrator set is never left empty.
        var finalCount = await CountAdministratorsAsync(provider);
        Assert.True(
            finalCount >= 1,
            "The combined result must never leave zero effective Administrators.");

        // Exactly one of the two orders in the summary happened, and the loser
        // must not have written anything: 2 admins − 1 + 1 leaves 2 when the
        // revocation applied and 3 when it was rejected as stale.
        if (revoke.IsSuccess)
        {
            Assert.Equal(2, finalCount);
            Assert.False(await IsAdministratorAsync(provider, secondAdmin));
        }
        else
        {
            Assert.Equal(3, finalCount);
            Assert.True(
                await IsAdministratorAsync(provider, secondAdmin),
                "A rejected revocation must not have removed anyone.");
        }
    }

    /// <summary>
    /// Deterministic proof of the exact-set precondition — no race involved: a
    /// grant that lands between the handler's observation and the revocation's
    /// locked read makes that observation stale, so the revocation is rejected
    /// with <c>identity.admin.concurrent-change</c> and writes nothing
    /// (ADR-0017 §19; <see cref="IIdentityAdministration.RevokeRoleAsync"/>).
    /// </summary>
    [SqlServerFact]
    public async Task A_revocation_whose_observation_was_made_stale_by_a_grant_is_rejected_without_writing()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var provider = database.BuildServiceProvider();

        var (firstAdmin, secondAdmin, observed) = await SeedTwoAdministratorsAsync(provider);
        var newcomerId = await CreateUserAsync(provider, "کاربر تازه");

        // The grant completes first: the set the revocation is about to be
        // checked against is no longer the set it observed.
        var grant = await GrantAdministratorAsync(provider, newcomerId);
        Assert.True(grant.IsSuccess, grant.Error?.Message);

        var revoke = await RevokeAdministratorAsync(provider, secondAdmin, observed);

        Assert.False(revoke.IsSuccess, "A stale observation must never be applied.");
        Assert.Equal(ErrorKind.Conflict, revoke.Error!.Kind);
        Assert.Equal(AdministrationErrors.ConcurrentAdministratorChangeCode, revoke.Error.Code);

        // Nothing was written: all three accounts are still Administrators.
        Assert.Equal(3, await CountAdministratorsAsync(provider));
    }

    /// <summary>
    /// The other legal order of the same two operations: when the revocation
    /// applies first, its observation is fresh, both operations succeed and
    /// two effective Administrators remain. "Both succeed" is therefore a valid
    /// outcome of the race — just not a guaranteed one (see the concurrent
    /// grant/revoke test above).
    /// </summary>
    [SqlServerFact]
    public async Task A_revocation_that_completes_before_a_grant_and_the_grant_both_succeed()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var provider = database.BuildServiceProvider();

        var (firstAdmin, secondAdmin, observed) = await SeedTwoAdministratorsAsync(provider);
        var newcomerId = await CreateUserAsync(provider, "کاربر تازه");

        var revoke = await RevokeAdministratorAsync(provider, secondAdmin, observed);
        Assert.True(revoke.IsSuccess, revoke.Error?.Message);

        var grant = await GrantAdministratorAsync(provider, newcomerId);
        Assert.True(grant.IsSuccess, grant.Error?.Message);

        Assert.Equal(2, await CountAdministratorsAsync(provider)); // first admin + newcomer
    }

    private static async Task<(string First, string Second, IReadOnlyList<string> Observed)>
        SeedTwoAdministratorsAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;

        await SqlServerTestDatabase.SeedCanonicalRolesAsync(services);

        var identity = services.GetRequiredService<IIdentityAdministration>();
        var first = await CreateUserAsync(services, "مدیر نخست");
        var second = await CreateUserAsync(services, "مدیر دوم");

        await GrantAsync(identity, first);
        await GrantAsync(identity, second);

        var observed = await identity.GetEffectiveAdministratorIdsAsync();
        return (first, second, observed);
    }

    private static async Task GrantAsync(IIdentityAdministration identity, string userId)
    {
        var granted = await identity.GrantRoleAsync(userId, RoleNames.Administrator);
        Assert.True(granted.IsSuccess, granted.Error?.Message);
    }

    private static async Task<string> CreateUserAsync(IServiceProvider services, string fullName)
    {
        var identity = services.GetRequiredService<IIdentityAdministration>();
        var created = await identity.CreateUserAsync(new CreateIdentityUser(
            SsoTestFlow.NextNationalId(),
            fullName,
            "09123456789",
            LocalUserKind.Administrator,
            Array.Empty<string>()));

        Assert.True(created.IsSuccess, created.Error?.Message);
        return created.Value!;
    }

    private static async Task<string> CreateUserAsync(ServiceProvider provider, string fullName)
    {
        using var scope = provider.CreateScope();
        return await CreateUserAsync(scope.ServiceProvider, fullName);
    }

    private static async Task<Result<bool>> RevokeAdministratorAsync(
        ServiceProvider provider,
        string targetUserId,
        IReadOnlyList<string> observed)
    {
        using var scope = provider.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityAdministration>();
        return await identity.RevokeRoleAsync(targetUserId, RoleNames.Administrator, observed);
    }

    private static async Task<Result<bool>> GrantAdministratorAsync(ServiceProvider provider, string userId)
    {
        using var scope = provider.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityAdministration>();
        return await identity.GrantRoleAsync(userId, RoleNames.Administrator);
    }

    private static async Task<int> CountAdministratorsAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityAdministration>();
        return (await identity.GetEffectiveAdministratorIdsAsync()).Count;
    }

    private static async Task<bool> IsAdministratorAsync(ServiceProvider provider, string userId)
    {
        using var scope = provider.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityAdministration>();
        return (await identity.GetRolesAsync(userId)).Contains(RoleNames.Administrator);
    }
}
