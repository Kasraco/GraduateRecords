using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

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
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(design-time);Database=GraduateRecords;TrustServerCertificate=False;")
            .Options);
}
