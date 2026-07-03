using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Postgres.Configurations;

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

        // Proposed / current state are JsonElement — store as raw JSON text and
        // parse on read, same pattern as Config.DefaultValue.
        builder.Property(c => c.ProposedState)
            .HasConversion(
                static value => value.GetRawText(),
                static text => ConditionValueParser.ParseJsonElement(text));
        builder.Property(c => c.CurrentState)
            .HasConversion(
                static value => value.HasValue ? value.Value.GetRawText() : null,
                static text => text == null ? (System.Text.Json.JsonElement?)null : ConditionValueParser.ParseJsonElement(text));

        builder.Property(c => c.AuthorUserId).IsRequired();
        builder.Property(c => c.AuthorMessage).HasMaxLength(2048);
        builder.Property(c => c.AppliedByUserId);
        builder.Property(c => c.RejectionReason).HasMaxLength(2048);
        builder.Property(c => c.WasEmergencyBypass);
        builder.Property(c => c.EmergencyReason).HasMaxLength(2048);

        // Npgsql maps DateTimeOffset to a native timestamptz column — no ticks
        // conversion needed (ADR-0026), unlike the SQLite provider.
        builder.Property(c => c.CreatedAt);
        builder.Property(c => c.UpdatedAt);
        builder.Property(c => c.AppliedAt);
        builder.Property(c => c.RejectedAt);
        builder.Property(c => c.ScheduledApplyAt);

        // Approvals + comments persisted as owned jsonb, same nested-JSON
        // pattern as Flag.Rules / Config.Rules.
        builder.OwnsMany(c => c.Approvals, a =>
        {
            a.ToJson();
            a.Property(x => x.Id).ValueGeneratedNever();
            a.Property(x => x.PendingChangeId);
            a.Property(x => x.ApproverUserId);
            a.Property(x => x.Decision).HasConversion<string>();
            a.Property(x => x.Comment);
            a.Property(x => x.At);
        });
        builder.OwnsMany(c => c.Comments, cm =>
        {
            cm.ToJson();
            cm.Property(x => x.Id).ValueGeneratedNever();
            cm.Property(x => x.PendingChangeId);
            cm.Property(x => x.AuthorUserId);
            cm.Property(x => x.Body);
            cm.Property(x => x.At);
        });

        builder.HasIndex(c => c.Status);
        builder.HasIndex(c => c.EnvironmentId);
        // The scheduled-apply worker polls "Approved rows due to apply" —
        // mirrors WebhookDelivery's (Status, NextAttemptAt) index shape.
        builder.HasIndex(c => new { c.Status, c.ScheduledApplyAt });
    }
}
