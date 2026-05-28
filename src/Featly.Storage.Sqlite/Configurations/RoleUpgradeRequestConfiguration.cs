using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Sqlite.Configurations;

internal sealed class RoleUpgradeRequestConfiguration : IEntityTypeConfiguration<RoleUpgradeRequest>
{
    public void Configure(EntityTypeBuilder<RoleUpgradeRequest> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RoleUpgradeRequests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.UserId).IsRequired();
        builder.Property(r => r.TargetProjectId).IsRequired();
        builder.Property(r => r.TargetEnvironmentId);
        builder.Property(r => r.RequestedRoleId).IsRequired();
        builder.Property(r => r.Justification).HasMaxLength(2048);

        // Enum persisted as its name so a future reorder can't reinterpret rows.
        builder.Property(r => r.Status)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(r => r.DecidedByUserId);
        builder.Property(r => r.DecisionComment).HasMaxLength(2048);
        builder.Property(r => r.CreatedAt).HasConversion<DateTimeOffsetTicksConverter>();
        builder.Property(r => r.DecidedAt)
            .HasConversion(
                static v => v.HasValue ? v.Value.UtcTicks : (long?)null,
                static t => t.HasValue ? new DateTimeOffset(t.Value, TimeSpan.Zero) : (DateTimeOffset?)null);

        // Inbox view queries pending requests; index the status.
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.UserId);
    }
}
