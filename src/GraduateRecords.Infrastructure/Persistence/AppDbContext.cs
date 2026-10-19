using Microsoft.EntityFrameworkCore;

namespace GraduateRecords.Infrastructure.Persistence;

/// <summary>
/// Persistence entry point. Phase 0 exposes only the structured application-log
/// table (ADR-0006 §6.1); business entities arrive in Phase 1+.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ApplicationLogEntry> ApplicationLogs => Set<ApplicationLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
