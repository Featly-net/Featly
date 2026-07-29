using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.MySql.Configurations;

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

        builder.Property(s => s.CreatedAt);
        builder.Property(s => s.UpdatedAt);
        builder.Property(s => s.CreatedBy).HasMaxLength(256);
        builder.Property(s => s.UpdatedBy).HasMaxLength(256);

        // Pomelo does not implement OwnsMany(...).ToJson() (see ADR-0033) —
        // Conditions is a plain JSON-column-backed scalar here instead of an
        // EF owned collection, same fallback as Flag.Tags/Variants/Rules.
        builder.Property(s => s.Conditions).AsJsonColumn();

        builder.HasIndex(s => new { s.EnvironmentId, s.Key })
            .IsUnique();

        builder.HasIndex(s => s.EnvironmentId);
    }
}
