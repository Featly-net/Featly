using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Sqlite.Configurations;

internal sealed class AssignmentConfiguration : IEntityTypeConfiguration<Assignment>
{
    public void Configure(EntityTypeBuilder<Assignment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Assignments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.ExperimentId).IsRequired();
        builder.Property(a => a.SubjectKey).IsRequired().HasMaxLength(256);
        builder.Property(a => a.VariantKey).IsRequired().HasMaxLength(128);
        builder.Property(a => a.AssignedAt).HasConversion<DateTimeOffsetTicksConverter>();

        // One assignment per subject per experiment — enforces first-write-wins.
        builder.HasIndex(a => new { a.ExperimentId, a.SubjectKey }).IsUnique();
    }
}
