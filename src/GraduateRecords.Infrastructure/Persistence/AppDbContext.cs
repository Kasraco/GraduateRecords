using Microsoft.EntityFrameworkCore;

namespace GraduateRecords.Infrastructure.Persistence;

/// <summary>
/// Persistence entry point. No business entities in Phase 0; used for readiness checks
/// and later migrations (Identity, domain model) in Phase 1+.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);
