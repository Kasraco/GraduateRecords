using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace GraduateRecords.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for <see cref="AppDbContext"/> so the EF tools can scaffold
/// migrations without booting the Web host. The host enforces validated security
/// options on start (AGENTS §18), and a design-time machine intentionally has no
/// SSO secrets or database connection; scaffolding never opens a connection, so a
/// placeholder is enough. Real connections come from configuration at runtime
/// (<c>DependencyInjection.AddInfrastructure</c>). Apply migrations with an
/// explicit connection or from the application's own dev-time path, never
/// automatically in production (AGENTS §17).
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) {
            var basePath = Directory.GetCurrentDirectory();

            var configuration = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", false)
                .AddJsonFile("appsettings.Development.json", true)
                .AddUserSecrets("8ae81d74-6adf-40cc-b44a-fc14832155e8")
                .Build();

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlServer(configuration.GetConnectionString("Default"));

            var context = new AppDbContext(optionsBuilder.Options);

            try
            {
                var canConnect = context.Database.CanConnect();
                Console.WriteLine($">>> Can connect to DB: {canConnect}");
            }
            catch (Exception ex)
            {
                Console.WriteLine(">>> DB connection failed");
                Console.WriteLine(ex.Message);
                throw;
            }

            return context;

    }
}
