using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Postgres.Configurations;

internal sealed class ApprovalPolicyConfiguration : IEntityTypeConfiguration<ApprovalPolicy>
{
    public void Configure(EntityTypeBuilder<ApprovalPolicy> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ApprovalPolicies");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.EnvironmentId).IsRequired();
        builder.Property(p => p.Required);
        builder.Property(p => p.MinApprovals);
        builder.Property(p => p.AuthorCanApproveOwnChange);
        builder.Property(p => p.AllowEmergencyBypass);

        // Approver rules persisted as owned jsonb.
        builder.OwnsMany(p => p.ApproverRules, r =>
        {
            r.ToJson();
            r.Property(x => x.Id).ValueGeneratedNever();
            r.Property(x => x.Type).HasConversion<string>();
            r.Property(x => x.UserId);
            r.Property(x => x.RoleId);
            r.Property(x => x.GroupId);
            r.Property(x => x.Mandatory);
            r.Property(x => x.MinFromThisRule);
        });

        // One policy per environment.
        builder.HasIndex(p => p.EnvironmentId).IsUnique();
    }
}
