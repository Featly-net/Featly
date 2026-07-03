using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Postgres.Configurations;

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

        // Npgsql maps DateTimeOffset to a native timestamptz column — no ticks
        // conversion needed (ADR-0026), unlike the SQLite provider.
        builder.Property(f => f.CreatedAt);
        builder.Property(f => f.UpdatedAt);
        builder.Property(f => f.CreatedBy).HasMaxLength(256);
        builder.Property(f => f.UpdatedBy).HasMaxLength(256);

        // Primitive collection serialised as a jsonb column (Npgsql's default
        // for primitive collections in EF Core 9+).
        builder.PrimitiveCollection(f => f.Tags);

        // Owned collection persisted as jsonb inside the Flags row. The whole
        // list is read/written atomically with the parent flag — fine for our
        // access pattern (we always load the full flag at evaluation time).
        builder.OwnsMany(f => f.Variants, variants =>
        {
            variants.ToJson();
            variants.Property(v => v.Key).IsRequired().HasMaxLength(128);
            variants.Property(v => v.Name).IsRequired().HasMaxLength(256);
            variants.Property(v => v.Description).HasMaxLength(2048);
            variants.Property(v => v.Value)
                .HasConversion(
                    static value => value.GetRawText(),
                    static text => ConditionValueParser.ParseJsonElement(text));
        });

        // Targeting rules persisted as a single jsonb document. Each rule carries
        // its own ordered conditions and the outcome (either a fixed variant or
        // a weighted split). The engine walks rules ordered by Rule.Order ASC.
        builder.OwnsMany(f => f.Rules, rules =>
        {
            rules.ToJson();
            rules.Property(r => r.Id).ValueGeneratedNever();
            rules.Property(r => r.Order);
            rules.Property(r => r.Name).HasMaxLength(256);
            rules.Property(r => r.Enabled);

            rules.OwnsOne(r => r.Outcome, outcome =>
            {
                outcome.Property(o => o.VariantKey).HasMaxLength(128);
                outcome.OwnsMany(o => o.Splits!, splits =>
                {
                    splits.Property(s => s.VariantKey).IsRequired().HasMaxLength(128);
                    splits.Property(s => s.Weight);
                });
            });

            rules.OwnsMany(r => r.Conditions, conditions =>
            {
                conditions.Property(c => c.Attribute).IsRequired().HasMaxLength(256);
                conditions.Property(c => c.Operator).HasConversion<string>().HasMaxLength(32);
                conditions.Property(c => c.Negate);
                conditions.Property(c => c.Value)
                    .HasConversion(
                        static value => value.GetRawText(),
                        static text => ConditionValueParser.ParseJsonElement(text));
            });
        });

        builder.HasIndex(f => new { f.EnvironmentId, f.Key })
            .IsUnique();

        builder.HasIndex(f => f.EnvironmentId);
    }
}
