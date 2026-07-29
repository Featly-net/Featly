using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.MySql.Configurations;

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

        // DefaultValue is a bare JsonElement scalar, same fallback as
        // Flag.Tags/Variants/Rules but via JsonScalarConversion since it's
        // not a List<T>.
        builder.Property(c => c.DefaultValue).AsJsonColumn();

        builder.Property(c => c.EnvironmentId)
            .IsRequired();

        builder.Property(c => c.Archived);

        builder.Property(c => c.CreatedAt);
        builder.Property(c => c.UpdatedAt);
        builder.Property(c => c.CreatedBy).HasMaxLength(256);
        builder.Property(c => c.UpdatedBy).HasMaxLength(256);

        // Pomelo does not implement OwnsMany(...).ToJson() (see ADR-0033) —
        // Tags/Rules are plain JSON-column-backed scalars here instead of EF
        // owned collections, same fallback as Flag.Tags/Variants/Rules.
        builder.Property(c => c.Tags).AsJsonColumn();
        builder.Property(c => c.Rules).AsJsonColumn();

        builder.HasIndex(c => new { c.EnvironmentId, c.Key })
            .IsUnique();

        builder.HasIndex(c => c.EnvironmentId);
    }
}
