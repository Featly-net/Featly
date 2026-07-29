using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.SqlServer.Configurations;

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

        // SQL Server maps DateTimeOffset to a native datetimeoffset column —
        // no ticks conversion needed (ADR-0032), unlike the SQLite provider.
        builder.Property(a => a.AssignedAt);

        // One assignment per subject per experiment — enforces first-write-wins.
        builder.HasIndex(a => new { a.ExperimentId, a.SubjectKey }).IsUnique();
    }
}
