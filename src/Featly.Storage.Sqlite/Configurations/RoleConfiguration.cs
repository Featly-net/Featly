using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Sqlite.Configurations;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Roles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Key).IsRequired().HasMaxLength(64);
        builder.Property(r => r.Name).IsRequired().HasMaxLength(128);
        builder.Property(r => r.Description).HasMaxLength(512);
        builder.Property(r => r.IsSystem);

        builder.Property(r => r.CreatedAt).HasConversion<DateTimeOffsetTicksConverter>();
        builder.Property(r => r.UpdatedAt).HasConversion<DateTimeOffsetTicksConverter>();

        // Permission list serialised as a JSON array of enum names. Storing the
        // string names (not the int values) means a future re-ordering of the
        // enum doesn't silently re-shuffle persisted role contents.
        builder.Property(r => r.Permissions)
            .HasConversion(
                static perms => PermissionListSerializer.Serialize(perms),
                static text => PermissionListSerializer.Deserialize(text),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<Permission>>(
                    (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
                    v => v.Aggregate(0, (hash, p) => HashCode.Combine(hash, p)),
                    v => v.ToList()));

        builder.HasIndex(r => r.Key).IsUnique();
    }
}
