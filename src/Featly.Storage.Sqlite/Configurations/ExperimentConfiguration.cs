using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Sqlite.Configurations;

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

        // Metric event keys as a JSON array, same pattern as Flag.Tags.
        builder.PrimitiveCollection(e => e.MetricKeys);

        builder.Property(e => e.CreatedAt).HasConversion<DateTimeOffsetTicksConverter>();
        builder.Property(e => e.UpdatedAt).HasConversion<DateTimeOffsetTicksConverter>();
        builder.Property(e => e.StartedAt)
            .HasConversion(
                static v => v.HasValue ? v.Value.UtcTicks : (long?)null,
                static t => t.HasValue ? new DateTimeOffset(t.Value, TimeSpan.Zero) : (DateTimeOffset?)null);
        builder.Property(e => e.StoppedAt)
            .HasConversion(
                static v => v.HasValue ? v.Value.UtcTicks : (long?)null,
                static t => t.HasValue ? new DateTimeOffset(t.Value, TimeSpan.Zero) : (DateTimeOffset?)null);

        // Computed convenience property — not a column.
        builder.Ignore(e => e.IsActive);

        builder.HasIndex(e => new { e.EnvironmentId, e.Key }).IsUnique();
        builder.HasIndex(e => e.EnvironmentId);
    }
}
