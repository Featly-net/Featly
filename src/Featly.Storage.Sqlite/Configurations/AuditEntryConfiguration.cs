using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Sqlite.Configurations;

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

        builder.Property(a => a.At).HasConversion<DateTimeOffsetTicksConverter>();

        // Structured detail (before/after) as raw JSON text, same pattern as
        // PendingChange.CurrentState.
        builder.Property(a => a.Data)
            .HasConversion(
                static value => value.HasValue ? value.Value.GetRawText() : null,
                static text => text == null ? (System.Text.Json.JsonElement?)null : ConditionValueParser.ParseJsonElement(text));

        builder.HasIndex(a => a.At);
        builder.HasIndex(a => new { a.EntityType, a.EntityKey });
        builder.HasIndex(a => a.EnvironmentId);
    }
}
