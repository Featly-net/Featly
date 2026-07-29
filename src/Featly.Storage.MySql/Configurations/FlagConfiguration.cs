using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.MySql.Configurations;

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

        // Pomelo does not yet implement EF Core's OwnsMany(...).ToJson() owned-
        // entity JSON mapping (PomeloFoundation/Pomelo.EntityFrameworkCore.MySql#1752),
        // unlike every other relational provider here. JsonCollectionConversion
        // is the fallback: the whole list serialized as JSON text into a native
        // MySQL json column — semantically equivalent for our access pattern
        // (always load/replace the whole list with the parent row). See ADR-0033.
        builder.Property(f => f.Tags).AsJsonColumn();
        builder.Property(f => f.Variants).AsJsonColumn();
        builder.Property(f => f.Rules).AsJsonColumn();
        builder.Property(f => f.Prerequisites).AsJsonColumn();

        builder.HasIndex(f => new { f.EnvironmentId, f.Key })
            .IsUnique();

        builder.HasIndex(f => f.EnvironmentId);
    }
}
