using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Sqlite.Configurations;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Projects");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .ValueGeneratedNever();

        builder.Property(p => p.Key)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.Description)
            .HasMaxLength(2048);

        builder.Property(p => p.IsDefault);

        // Persist timestamps as 64-bit UTC ticks so MAX / ORDER BY work in SQL.
        builder.Property(p => p.CreatedAt).HasConversion<DateTimeOffsetTicksConverter>();

        builder.HasIndex(p => p.Key)
            .IsUnique();
    }
}
