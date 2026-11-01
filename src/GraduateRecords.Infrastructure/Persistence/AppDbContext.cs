using GraduateRecords.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GraduateRecords.Infrastructure.Persistence;

/// <summary>
/// Persistence entry point. Phase 1A UC1 added the local Identity user store;
/// UC2.2 adds the role store (ADR-0017 §Migration impact). The context derives
/// from <see cref="IdentityDbContext{TUser,TRole,TKey}"/> with the canonical
/// <see cref="ApplicationRole"/> and string keys: the user-side base chain is
/// identical to the previous <c>IdentityUserContext&lt;ApplicationUser&gt;</c>
/// (users, user claims, logins and tokens map unchanged), while roles,
/// user-role memberships and role claims are now mapped alongside them.
/// Business entities arrive in later phases. No repository/Unit-of-Work
/// abstraction exists — this DbContext is the unit of work (AGENTS §2.1).
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, string>(options)
{
    public DbSet<ApplicationLogEntry> ApplicationLogs => Set<ApplicationLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
