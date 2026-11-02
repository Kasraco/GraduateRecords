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

        Assert.All(outcomes, outcome => Assert.True(outcome.IsSuccess, outcome.Error?.Message));
        Assert.True(
            await CountAdministratorsAsync(provider) >= 1,
            "The combined result must never leave zero effective Administrators.");
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
}
