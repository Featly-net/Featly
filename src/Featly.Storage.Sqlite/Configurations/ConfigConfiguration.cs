using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Sqlite.Configurations;

internal sealed class ConfigConfiguration : IEntityTypeConfiguration<Config>
{
    public void Configure(EntityTypeBuilder<Config> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Configs");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .ValueGeneratedNever();

        builder.Property(c => c.Key)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.Description)
            .HasMaxLength(2048);

        builder.Property(c => c.Type)
            .HasConversion<string>()
            .HasMaxLength(32);

        // DefaultValue is a JsonElement — store as raw JSON text and parse on read,
        // same pattern as Variant.Value / Condition.Value.
        builder.Property(c => c.DefaultValue)
            .HasConversion(
                static value => value.GetRawText(),
                static text => ConditionValueParser.ParseJsonElement(text));

        builder.Property(c => c.EnvironmentId)
            .IsRequired();

        builder.Property(c => c.Archived);

        builder.Property(c => c.CreatedAt);
        builder.Property(c => c.UpdatedAt);
        builder.Property(c => c.CreatedBy).HasMaxLength(256);
        builder.Property(c => c.UpdatedBy).HasMaxLength(256);

        // Tags persisted as JSON primitive collection, same as Flag.Tags.
        builder.PrimitiveCollection(c => c.Tags);

        // Targeting rules persisted as owned JSON: each rule carries its
        // conditions and the typed value served on match. Same nested-JSON
        // pattern as Flag.Rules.
        builder.OwnsMany(c => c.Rules, rules =>
        {
            rules.ToJson();
            rules.Property(r => r.Id).ValueGeneratedNever();
            rules.Property(r => r.Order);
            rules.Property(r => r.Name).HasMaxLength(256);
            rules.Property(r => r.Enabled);
            rules.Property(r => r.Value)
                .HasConversion(
                    static value => value.GetRawText(),
                    static text => ConditionValueParser.ParseJsonElement(text));

            rules.OwnsMany(r => r.Conditions, conditions =>
            {
                conditions.Property(co => co.Attribute).IsRequired().HasMaxLength(256);
                conditions.Property(co => co.Operator).HasConversion<string>().HasMaxLength(32);
                conditions.Property(co => co.Negate);
                conditions.Property(co => co.Value)
                    .HasConversion(
                        static value => value.GetRawText(),
                        static text => ConditionValueParser.ParseJsonElement(text));
            });
        });

        builder.HasIndex(c => new { c.EnvironmentId, c.Key })
            .IsUnique();

        builder.HasIndex(c => c.EnvironmentId);
    }
}
