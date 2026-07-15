using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Postgres.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("AuditEntries");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Action).IsRequired().HasMaxLength(128);
        builder.Property(a => a.EntityType).IsRequired().HasMaxLength(64);
        builder.Property(a => a.EntityKey).HasMaxLength(256);
        builder.Property(a => a.EnvironmentId);
        builder.Property(a => a.ActorIdentifier).HasMaxLength(256);

        // Npgsql maps DateTimeOffset to a native timestamptz column — no ticks
        // conversion needed (ADR-0026), unlike the SQLite provider.
        builder.Property(a => a.At);

        // Structured detail (before/after) as raw JSON text, same pattern as
        // PendingChange.CurrentState.
        builder.Property(a => a.Data)
            .HasConversion(
                static value => value.HasValue ? value.Value.GetRawText() : null,
                static text => text == null ? (System.Text.Json.JsonElement?)null : ConditionValueParser.ParseJsonElement(text));

        // Tamper-evident hash chain (issue #208). SHA-256 hex is 64 chars.
        builder.Property(a => a.Sequence);
        builder.Property(a => a.PreviousHash).HasMaxLength(64);
        builder.Property(a => a.Hash).HasMaxLength(64);

        builder.HasIndex(a => a.At);
        builder.HasIndex(a => new { a.EntityType, a.EntityKey });
        builder.HasIndex(a => a.EnvironmentId);
        builder.HasIndex(a => a.Sequence);
    }
}
