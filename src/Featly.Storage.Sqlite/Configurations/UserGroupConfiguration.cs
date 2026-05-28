using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Sqlite.Configurations;

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

        // Membership stored inline as a JSON array of user ids.
        builder.PrimitiveCollection(g => g.MemberUserIds);

        builder.Property(g => g.CreatedAt).HasConversion<DateTimeOffsetTicksConverter>();
        builder.Property(g => g.UpdatedAt).HasConversion<DateTimeOffsetTicksConverter>();

        builder.HasIndex(g => g.Key).IsUnique();
    }
}
