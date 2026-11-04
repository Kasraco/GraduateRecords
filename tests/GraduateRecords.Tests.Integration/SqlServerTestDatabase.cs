using GraduateRecords.Application.Common.Auditing;
using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Security;
using GraduateRecords.Application.Common.Time;
using GraduateRecords.Infrastructure.Identity;
using GraduateRecords.Infrastructure.Persistence;
using GraduateRecords.Infrastructure.Time;
using GraduateRecords.Web.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
    private readonly string _baseConnectionString;

    private SqlServerTestDatabase(string name, string baseConnectionString, string connectionString)
    {
        Name = name;
        _baseConnectionString = baseConnectionString;
        _connectionString = connectionString;
    }

    /// <summary>The generated database name — never an application database name.</summary>
    public string Name { get; }

    /// <summary>
    /// Connection string of the generated database. Test-owned raw connections
    /// need it: barriers, probes and DMV reads never go through EF (approved
    /// SQL-G2/A evidence design).
    /// </summary>
    public string ConnectionString => _connectionString;

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

        return new SqlServerTestDatabase(name, baseConnectionString!, connectionString);
    }

    /// <summary>
    /// Test-only isolation setup for SQL-G2-B (approved design): enables
    /// <c>READ_COMMITTED_SNAPSHOT</c> — the ADR-0007 §3.2 target configuration —
    /// on this disposable database and then <b>verifies</b> it. Fails closed
    /// (throws) when it cannot be enabled or verified: the revoke-versus-
    /// bootstrap concurrency test must never run in a mode where the handler's
    /// pre-port reads would block before the role-row lock.
    /// </summary>
    public async Task EnableReadCommittedSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteOnMasterAsync(
            _baseConnectionString,
            $"ALTER DATABASE [{Name}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE",
            cancellationToken);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT CONVERT(bit, is_read_committed_snapshot_on) FROM sys.databases WHERE name = DB_NAME();";

        var enabled = await command.ExecuteScalarAsync(cancellationToken);
        if (enabled is not true)
        {
            throw new InvalidOperationException(
                $"READ_COMMITTED_SNAPSHOT could not be enabled or verified on '{Name}'; " +
                "the SQL-G2-B concurrency test fails closed without it.");
        }
    }

    /// <summary>Production-shaped DI: the real Identity stores and the real port.</summary>
    public ServiceProvider BuildServiceProvider(Action<IServiceCollection>? configure = null)
    {
        var loggerProvider = new RecordingLoggerProvider();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(loggerProvider));
        services.AddSingleton(loggerProvider);
        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(_connectionString));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<AppDbContext>();
        services.AddScoped<IIdentityAdministration, IdentityAdministration>();

        // T3c: the real bootstrap/revoke handlers under test and their ports.
        // Nothing here replaces production behaviour — only test recorders and
        // one actor fake are added; the port, the stores, the transaction and
        // the role-row lock are the production ones.
        services.AddScoped<ILocalUserDirectory, LocalUserDirectory>();
        services.AddScoped<IClock, SystemClock>();
        services.AddSingleton<RecordingAuditWriter>();
        services.AddScoped<IAuditWriter>(provider => provider.GetRequiredService<RecordingAuditWriter>());
        services.AddSingleton<BootstrapTestActor>();
        services.AddScoped<ICurrentUser>(provider => provider.GetRequiredService<BootstrapTestActor>());
        services.AddQueryHandlers(typeof(GraduateRecords.Application.AssemblyMarker).Assembly);

        configure?.Invoke(services);
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
