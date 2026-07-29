using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.MySql.Configurations;

internal sealed class ExperimentConfiguration : IEntityTypeConfiguration<Experiment>
{
    public void Configure(EntityTypeBuilder<Experiment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Experiments");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Key).IsRequired().HasMaxLength(128);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(256);
        builder.Property(e => e.Hypothesis).HasMaxLength(2048);
        builder.Property(e => e.FlagKey).IsRequired().HasMaxLength(128);
        builder.Property(e => e.StickyAssignments);
        builder.Property(e => e.EnvironmentId).IsRequired();

        // Pomelo does not implement OwnsMany(...).ToJson() or PrimitiveCollection()
        // query translation (see ADR-0033) — MetricKeys is a plain JSON-column-
        // backed scalar here instead, same fallback as Flag.Tags.
        builder.Property(e => e.MetricKeys).AsJsonColumn();

        builder.Property(e => e.CreatedAt);
        builder.Property(e => e.UpdatedAt);
        builder.Property(e => e.StartedAt);
        builder.Property(e => e.StoppedAt);

        // Computed convenience property — not a column.
        builder.Ignore(e => e.IsActive);

        builder.HasIndex(e => new { e.EnvironmentId, e.Key }).IsUnique();
        builder.HasIndex(e => e.EnvironmentId);
    }
}
