using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Postgres.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.Identifier).IsRequired().HasMaxLength(256);
        builder.Property(u => u.DisplayName).IsRequired().HasMaxLength(256);
        builder.Property(u => u.Email).HasMaxLength(256);
        builder.Property(u => u.Disabled);

        // Npgsql maps DateTimeOffset to a native timestamptz column — no ticks
        // conversion needed (ADR-0026), unlike the SQLite provider.
        builder.Property(u => u.CreatedAt);
        builder.Property(u => u.UpdatedAt);
        builder.Property(u => u.CreatedBy).HasMaxLength(256);
        builder.Property(u => u.UpdatedBy).HasMaxLength(256);

        builder.HasIndex(u => u.Identifier).IsUnique();
    }
}
