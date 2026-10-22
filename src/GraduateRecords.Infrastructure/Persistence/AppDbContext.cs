using GraduateRecords.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GraduateRecords.Infrastructure.Persistence;

/// <summary>
/// Persistence entry point. Phase 1A adds the local Identity user store
/// (ADR-0001); business entities arrive in later phases. The context derives from
/// <see cref="IdentityUserContext{TUser}"/> so the Identity user model is mapped
/// without any role store (roles are the Use Case 2 slice).
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityUserContext<ApplicationUser>(options)
{
    public DbSet<ApplicationLogEntry> ApplicationLogs => Set<ApplicationLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
