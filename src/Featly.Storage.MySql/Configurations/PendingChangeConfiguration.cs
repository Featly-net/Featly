using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.MySql.Configurations;

internal sealed class PendingChangeConfiguration : IEntityTypeConfiguration<PendingChange>
{
    public void Configure(EntityTypeBuilder<PendingChange> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PendingChanges");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.EntityType).IsRequired().HasMaxLength(64);
        builder.Property(c => c.EntityKey).IsRequired().HasMaxLength(128);
        builder.Property(c => c.EnvironmentId).IsRequired();

        builder.Property(c => c.Action).HasConversion<string>().HasMaxLength(16);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(16);

        // Proposed / current state are JsonElement scalars, same fallback as
        // Config.DefaultValue.
        builder.Property(c => c.ProposedState).AsJsonColumn();
        builder.Property(c => c.CurrentState).AsJsonColumn();

        builder.Property(c => c.AuthorUserId).IsRequired();
        builder.Property(c => c.AuthorMessage).HasMaxLength(2048);
        builder.Property(c => c.AppliedByUserId);
        builder.Property(c => c.RejectionReason).HasMaxLength(2048);
        builder.Property(c => c.WasEmergencyBypass);
        builder.Property(c => c.EmergencyReason).HasMaxLength(2048);

        builder.Property(c => c.CreatedAt);
        builder.Property(c => c.UpdatedAt);
        builder.Property(c => c.AppliedAt);
        builder.Property(c => c.RejectedAt);
        builder.Property(c => c.ScheduledApplyAt);

        // Pomelo does not implement OwnsMany(...).ToJson() (see ADR-0033) —
        // Approvals/Comments are plain JSON-column-backed scalars here instead
        // of EF owned collections, same fallback as Flag.Tags/Variants/Rules.
        builder.Property(c => c.Approvals).AsJsonColumn();
        builder.Property(c => c.Comments).AsJsonColumn();

        builder.HasIndex(c => c.Status);
        builder.HasIndex(c => c.EnvironmentId);
        // The scheduled-apply worker polls "Approved rows due to apply" —
        // mirrors WebhookDelivery's (Status, NextAttemptAt) index shape.
        builder.HasIndex(c => new { c.Status, c.ScheduledApplyAt });
    }
}
