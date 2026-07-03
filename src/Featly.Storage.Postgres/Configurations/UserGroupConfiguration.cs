using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Postgres.Configurations;

internal sealed class UserGroupConfiguration : IEntityTypeConfiguration<UserGroup>
{
    public void Configure(EntityTypeBuilder<UserGroup> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("UserGroups");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).ValueGeneratedNever();

        builder.Property(g => g.Key).IsRequired().HasMaxLength(64);
        builder.Property(g => g.Name).IsRequired().HasMaxLength(128);
        builder.Property(g => g.Description).HasMaxLength(512);

        // Membership stored as a native jsonb array of user ids.
        builder.PrimitiveCollection(g => g.MemberUserIds);

        // Npgsql maps DateTimeOffset to a native timestamptz column — no ticks
        // conversion needed (ADR-0026), unlike the SQLite provider.
        builder.Property(g => g.CreatedAt);
        builder.Property(g => g.UpdatedAt);

        builder.HasIndex(g => g.Key).IsUnique();
    }
}
