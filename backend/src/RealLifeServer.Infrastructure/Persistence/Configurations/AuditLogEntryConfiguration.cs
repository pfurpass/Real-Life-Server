using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RealLifeServer.Domain.Entities;

namespace RealLifeServer.Infrastructure.Persistence.Configurations;

public class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("audit_log_entries");
        builder.Property(a => a.Action).HasMaxLength(128).IsRequired();
        builder.Property(a => a.EntityType).HasMaxLength(64).IsRequired();
        builder.Property(a => a.DetailsJson).HasColumnType("jsonb");
        builder.HasIndex(a => a.OccurredAt);
    }
}
