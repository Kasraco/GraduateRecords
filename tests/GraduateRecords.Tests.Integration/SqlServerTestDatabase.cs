using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Security;
using GraduateRecords.Infrastructure.Identity;
using GraduateRecords.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace GraduateRecords.Tests.Integration;

/// <summary>
/// A dedicated, uniquely named, disposable SQL Server database for the UC3
/// concurrency tests (AGENTS §22: SQL Server only where persistence,
/// transaction or concurrency behaviour is under test).
/// <para>
/// Safety rules: the server comes only from
/// <see cref="SqlServerFactAttribute.ConnectionStringVariable"/>; the database
/// name is generated per run and is the <b>only</b> name ever created or
/// dropped; setup runs <c>MigrateAsync</c> (never <c>EnsureCreated</c>, AGENTS
/// §17); and cleanup drops only that generated name.
/// </para>
/// </summary>
public sealed class SqlServerTestDatabase : IAsyncDisposable
{
    private readonly string _connectionString;

    private SqlServerTestDatabase(string name, string connectionString)
    {
        Name = name;
        _connectionString = connectionString;
    }

    /// <summary>The generated database name — never an application database name.</summary>
    public string Name { get; }

    /// <summary>Creates the database, applies the real migrations and returns it.</summary>
    public static async Task<SqlServerTestDatabase> CreateAsync(CancellationToken cancellationToken = default)
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.ConnectionStringVariable);
        Assert.False(
            string.IsNullOrWhiteSpace(baseConnectionString),
            $"{SqlServerFactAttribute.ConnectionStringVariable} must be set for this test to run.");

        var name = "GraduateRecords_Uc3_" + Guid.NewGuid().ToString("N")[..12];
        await ExecuteOnMasterAsync(baseConnectionString!,
            $"CREATE DATABASE [{name}]", cancellationToken);

        var connectionString = WithCatalog(baseConnectionString!, name);

        await using (var context = new AppDbContext(
                         new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options))
        {
            // AGENTS §17: real migrations, never EnsureCreated.
            await context.Database.MigrateAsync(cancellationToken);
        }

        return new SqlServerTestDatabase(name, connectionString);
    }

    /// <summary>Production-shaped DI: the real Identity stores and the real port.</summary>
    public ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(_connectionString));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<AppDbContext>();
        services.AddScoped<IIdentityAdministration, IdentityAdministration>();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Idempotent canonical seed (ADR-0017 §8): the three roles with the Phase
    /// 1A permission claims of <see cref="RolePermissionCatalog"/>, exactly as
    /// the application's startup reconciliation produces them.
    /// </summary>
    public static async Task SeedCanonicalRolesAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();

        foreach (var roleName in RoleNames.AllSupported)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                var created = await roleManager.CreateAsync(new ApplicationRole
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = roleName,
                });
                Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(error => error.Code)));
                role = await roleManager.FindByNameAsync(roleName);
            }

            Assert.NotNull(role);

            var existing = await roleManager.GetClaimsAsync(role!);
            foreach (var permission in RolePermissionCatalog.GetPermissions(roleName))
            {
                if (existing.Any(claim => claim.Type == PermissionClaims.Type && claim.Value == permission))
                {
                    continue;
                }

                var added = await roleManager.AddClaimAsync(
                    role!, new System.Security.Claims.Claim(PermissionClaims.Type, permission));
                Assert.True(added.Succeeded, string.Join(", ", added.Errors.Select(error => error.Code)));
            }
        }
    }

    /// <summary>Drops only the database this instance created.</summary>
    public async ValueTask DisposeAsync()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            return;
        }

        await ExecuteOnMasterAsync(
            baseConnectionString,
            $"ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}];",
            CancellationToken.None);
    }

    private static string WithCatalog(string connectionString, string catalog)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = catalog,
            Pooling = false,
        };
        return builder.ConnectionString;
    }

    private static async Task ExecuteOnMasterAsync(
        string connectionString,
        string commandText,
        CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master",
            Pooling = false,
        };

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
