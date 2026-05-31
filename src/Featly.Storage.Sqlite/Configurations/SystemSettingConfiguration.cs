using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Sqlite.Configurations;

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
        builder.Property(s => s.UpdatedAt).HasConversion<DateTimeOffsetTicksConverter>();

        // The typed settings aggregate as raw JSON text, same pattern as
        // AuditEntry.Data / PendingChange state.
        builder.Property(s => s.Payload)
            .IsRequired()
            .HasConversion(
                static value => value.GetRawText(),
                static text => ConditionValueParser.ParseJsonElement(text));
    }
}
