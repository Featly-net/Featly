using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Postgres.Configurations;

internal sealed class SegmentConfiguration : IEntityTypeConfiguration<Segment>
{
    public void Configure(EntityTypeBuilder<Segment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Segments");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .ValueGeneratedNever();

        builder.Property(s => s.Key)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(s => s.Description)
            .HasMaxLength(2048);

        builder.Property(s => s.EnvironmentId)
            .IsRequired();

        builder.Property(s => s.Archived);

        // Npgsql maps DateTimeOffset to a native timestamptz column — no ticks
        // conversion needed (ADR-0026), unlike the SQLite provider.
        builder.Property(s => s.CreatedAt);
        builder.Property(s => s.UpdatedAt);
        builder.Property(s => s.CreatedBy).HasMaxLength(256);
        builder.Property(s => s.UpdatedBy).HasMaxLength(256);

        // Conditions persisted atomically as jsonb, same pattern as Flag.Variants.
        builder.OwnsMany(s => s.Conditions, conditions =>
        {
            conditions.ToJson();
            conditions.Property(c => c.Attribute).IsRequired().HasMaxLength(256);
            conditions.Property(c => c.Operator).HasConversion<string>().HasMaxLength(32);
            conditions.Property(c => c.Negate);
            conditions.Property(c => c.Value)
                .HasConversion(
                    static value => value.GetRawText(),
                    static text => ConditionValueParser.ParseJsonElement(text));
        });

        builder.HasIndex(s => new { s.EnvironmentId, s.Key })
            .IsUnique();

        builder.HasIndex(s => s.EnvironmentId);
    }
}
