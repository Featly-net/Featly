using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Sqlite.Configurations;

internal sealed class FlagConfiguration : IEntityTypeConfiguration<Flag>
{
    public void Configure(EntityTypeBuilder<Flag> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Flags");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Id)
            .ValueGeneratedNever();

        builder.Property(f => f.Key)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(f => f.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(f => f.Description)
            .HasMaxLength(2048);

        builder.Property(f => f.Type)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(f => f.Enabled);

        builder.Property(f => f.DefaultVariantKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(f => f.EnvironmentId)
            .IsRequired();

        builder.Property(f => f.Archived);

        builder.Property(f => f.CreatedAt);
        builder.Property(f => f.UpdatedAt);
        builder.Property(f => f.CreatedBy).HasMaxLength(256);
        builder.Property(f => f.UpdatedBy).HasMaxLength(256);

        // Primitive collection serialised as a JSON column. EF Core 8+ has
        // native support; SQLite stores it as TEXT.
        builder.PrimitiveCollection(f => f.Tags);

        // Owned collection persisted as JSON inside the Flags row. The whole
        // list is read/written atomically with the parent flag — fine for our
        // access pattern (we always load the full flag at evaluation time).
        builder.OwnsMany(f => f.Variants, variants =>
        {
            variants.ToJson();
            variants.Property(v => v.Key).IsRequired().HasMaxLength(128);
            variants.Property(v => v.Name).IsRequired().HasMaxLength(256);
            variants.Property(v => v.Description).HasMaxLength(2048);
            // EF Core doesn't recognise JsonElement as a primitive. Round-trip
            // it through the raw JSON text so the value flows verbatim into
            // the owning JSON column.
            variants.Property(v => v.Value)
                .HasConversion(
                    static value => value.GetRawText(),
                    static text => ParseJsonElement(text));
        });

        builder.HasIndex(f => new { f.EnvironmentId, f.Key })
            .IsUnique();

        builder.HasIndex(f => f.EnvironmentId);
    }

    private static JsonElement ParseJsonElement(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return default;
        }

        using var doc = JsonDocument.Parse(text);
        // Clone detaches the element from the JsonDocument lifetime
        // so it survives the using block.
        return doc.RootElement.Clone();
    }
}
