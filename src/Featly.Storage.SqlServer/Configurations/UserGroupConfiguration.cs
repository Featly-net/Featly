using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.SqlServer.Configurations;

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

        // Membership stored as a JSON array of user ids (nvarchar(max)) — SQL
        // Server has no native array type, unlike Postgres's uuid[].
        builder.PrimitiveCollection(g => g.MemberUserIds);

        // SQL Server maps DateTimeOffset to a native datetimeoffset column —
        // no ticks conversion needed (ADR-0032), unlike the SQLite provider.
        builder.Property(g => g.CreatedAt);
        builder.Property(g => g.UpdatedAt);

        builder.HasIndex(g => g.Key).IsUnique();
    }
}
