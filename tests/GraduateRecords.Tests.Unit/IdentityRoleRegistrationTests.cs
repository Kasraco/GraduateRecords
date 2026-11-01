using GraduateRecords.Infrastructure;
using GraduateRecords.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GraduateRecords.Tests.Unit;

/// <summary>
/// UC2.2 registration wiring — proves the two load-bearing ordering rules
/// documented in <c>DependencyInjection.AddIdentityAuthentication</c>:
/// (1) <c>AddRoles&lt;ApplicationRole&gt;()</c> must run before
/// <c>AddEntityFrameworkStores&lt;AppDbContext&gt;()</c>, otherwise no
/// <c>IRoleStore&lt;ApplicationRole&gt;</c> is registered and every principal
/// build would fail; (2) the <c>ApplicationUserClaimsPrincipalFactory</c>
/// Replace must run after the chain, otherwise the framework's own role-aware
/// factory would win and <c>full_name</c> would be lost. Resolving every
/// involved service from the production registration proves both.
/// </summary>
public sealed class IdentityRoleRegistrationTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "GraduateRecordsRoleRegistrationTests");

    [Fact]
    public void The_role_store_role_manager_and_application_claims_factory_are_all_wired()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService<IRoleStore<ApplicationRole>>());
        Assert.NotNull(scope.ServiceProvider.GetService<RoleManager<ApplicationRole>>());

        var principalFactory =
            scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
        Assert.IsType<ApplicationUserClaimsPrincipalFactory>(principalFactory);
    }

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Same security values as OidcEnvironmentStartupTests; Development is
        // the one environment that may run without SSO configuration.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Server=db;Database=GraduateRecords;Encrypt=True;TrustServerCertificate=False",
            ["FileStorage:RootPath"] = Path.Combine(Root, "files"),
            ["Identity:Lockout:MaxFailedAccessAttempts"] = "5",
            ["Identity:Lockout:LockoutDurationMinutes"] = "15",
            ["Identity:Lockout:AllowedForNewUsers"] = "true",
            ["Identity:Password:RequiredLength"] = "14",
            ["Identity:CookieLifetime:IdleMinutes"] = "30",
            ["Identity:CookieLifetime:AbsoluteHours"] = "8",
            ["Identity:LoginRateLimit:PermitsPerMinute"] = "10",
        }).Build();

        services.AddInfrastructure(configuration, Environments.Development, Path.Combine(Root, "wwwroot"));
        services.AddIdentityAuthentication(configuration, Environments.Development);
        return services.BuildServiceProvider();
    }
}
