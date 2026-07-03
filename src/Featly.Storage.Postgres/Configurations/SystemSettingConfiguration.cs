using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Postgres.Configurations;

internal sealed class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SystemSettings");

        // The aggregate key is the natural primary key — one row per singleton.
        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key).HasMaxLength(64);

        builder.Property(s => s.UpdatedBy).HasMaxLength(256);

        // Npgsql maps DateTimeOffset to a native timestamptz column — no ticks
        // conversion needed (ADR-0026), unlike the SQLite provider.
        builder.Property(s => s.UpdatedAt);

        // The typed settings aggregate as raw JSON text, same pattern as
        // AuditEntry.Data / PendingChange state.
        builder.Property(s => s.Payload)
            .IsRequired()
            .HasConversion(
                static value => value.GetRawText(),
                static text => ConditionValueParser.ParseJsonElement(text));
    }
}
