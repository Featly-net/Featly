using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.MySql.Configurations;

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

        // Pomelo does not implement OwnsMany(...).ToJson() (see ADR-0033) —
        // ApproverRules is a plain JSON-column-backed scalar here instead of
        // an EF owned collection, same fallback as Flag.Tags/Variants/Rules.
        builder.Property(p => p.ApproverRules).AsJsonColumn();

        // One policy per environment.
        builder.HasIndex(p => p.EnvironmentId).IsUnique();
    }
}
