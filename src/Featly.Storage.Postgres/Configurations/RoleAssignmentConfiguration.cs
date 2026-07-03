using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Postgres.Configurations;

internal sealed class RoleAssignmentConfiguration : IEntityTypeConfiguration<RoleAssignment>
{
    public void Configure(EntityTypeBuilder<RoleAssignment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RoleAssignments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        // Enum persisted as its name (not the int) so a future reorder of
        // AssigneeType doesn't silently reinterpret existing rows.
        builder.Property(a => a.AssigneeType)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(a => a.AssigneeId).IsRequired();
        builder.Property(a => a.ProjectId).IsRequired();
        builder.Property(a => a.EnvironmentId);
        builder.Property(a => a.RoleId).IsRequired();

        // Npgsql maps DateTimeOffset to a native timestamptz column — no ticks
        // conversion needed (ADR-0026), unlike the SQLite provider.
        builder.Property(a => a.AssignedAt);
        builder.Property(a => a.AssignedByUserId);

        // Hot path: the permission checker looks up assignments by assignee id.
        builder.HasIndex(a => a.AssigneeId);
        // Admin views and the upgrade-request flow scope by project.
        builder.HasIndex(a => a.ProjectId);
    }
}
