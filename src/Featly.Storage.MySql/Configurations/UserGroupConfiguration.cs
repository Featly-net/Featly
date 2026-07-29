using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.MySql.Configurations;

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

        // Pomelo does not implement OwnsMany(...).ToJson() or EF Core's
        // PrimitiveCollection() query translation (see ADR-0033) — membership
        // is a plain JSON-column-backed scalar here instead, same fallback as
        // Flag.Tags. This means MySqlUserGroupStore.ListForMemberAsync cannot
        // push the containment check server-side and filters in memory
        // instead — see that store for the reasoning.
        builder.Property(g => g.MemberUserIds).AsJsonColumn();

        builder.Property(g => g.CreatedAt);
        builder.Property(g => g.UpdatedAt);

        builder.HasIndex(g => g.Key).IsUnique();
    }
}
