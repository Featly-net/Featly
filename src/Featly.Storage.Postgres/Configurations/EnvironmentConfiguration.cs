using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Postgres.Configurations;

internal sealed class EnvironmentConfiguration : IEntityTypeConfiguration<Environment>
{
    public void Configure(EntityTypeBuilder<Environment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Environments");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.ProjectId)
            .IsRequired();

        builder.Property(e => e.Key)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.IsDefault);

        builder.Property(e => e.ReadOnly);

        // Npgsql maps DateTimeOffset to a native timestamptz column — no ticks
        // conversion needed (ADR-0026), unlike the SQLite provider.
        builder.Property(e => e.CreatedAt);

        builder.HasIndex(e => new { e.ProjectId, e.Key })
            .IsUnique();
    }
}
