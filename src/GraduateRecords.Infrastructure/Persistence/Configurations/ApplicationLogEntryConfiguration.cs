using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GraduateRecords.Infrastructure.Persistence.Configurations;

public sealed class ApplicationLogEntryConfiguration : IEntityTypeConfiguration<ApplicationLogEntry>
{
    public void Configure(EntityTypeBuilder<ApplicationLogEntry> builder)
    {
        builder.ToTable("ApplicationLog", "log");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).UseIdentityColumn();
        builder.Property(entry => entry.OccurredAtUtc).HasColumnType("datetime2");
        builder.Property(entry => entry.Level).HasMaxLength(16).IsRequired();
        builder.Property(entry => entry.Message).HasMaxLength(4000).IsRequired();
        builder.Property(entry => entry.ExceptionType).HasMaxLength(256);
        builder.Property(entry => entry.CorrelationId).HasMaxLength(64);
        builder.Property(entry => entry.Application).HasMaxLength(128).IsRequired();
        builder.HasIndex(entry => entry.OccurredAtUtc);
    }
}
