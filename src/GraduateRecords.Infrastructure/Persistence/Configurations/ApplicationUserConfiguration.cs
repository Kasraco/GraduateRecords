using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GraduateRecords.Infrastructure.Persistence.Configurations;

/// <summary>
/// ADR-0001 rules 6–7: the National ID column is exactly 10 characters and
/// unique, so a National ID can never map to two users at the database level.
/// ADR-0015 §2/§9: the account kind is stored as a string with a fail-closed
/// <c>Student</c> default, so an account without an explicitly assigned kind
/// can never qualify for local password login.
/// </summary>
public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.NationalId)
            .HasMaxLength(ApplicationUser.NationalIdMaxLength)
            .IsRequired();

        builder.Property(u => u.FullName)
            .HasMaxLength(ApplicationUser.FullNameMaxLength)
            .IsRequired();

        builder.Property(u => u.UserKind)
            .HasConversion<string>()
            .HasMaxLength(ApplicationUser.UserKindMaxLength)
            .HasDefaultValue(LocalUserKind.Student)
            .IsRequired();

        // The unique index on NormalizedUserName comes from the Identity base model.
        builder.HasIndex(u => u.NationalId).IsUnique();
    }
}
