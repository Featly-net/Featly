using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.MySql.Configurations;

internal sealed class WebhookEndpointConfiguration : IEntityTypeConfiguration<WebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<WebhookEndpoint> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("WebhookEndpoints");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Name).IsRequired().HasMaxLength(256);
        builder.Property(e => e.Url).IsRequired().HasMaxLength(2048);
        builder.Property(e => e.Secret).HasMaxLength(512);
        builder.Property(e => e.Enabled);
        builder.Property(e => e.EnvironmentId);

        // Circuit-breaker state (issue #207). CircuitOpenUntil maps to a native
        // nullable datetime(6).
        builder.Property(e => e.ConsecutiveFailures);
        builder.Property(e => e.CircuitOpenUntil);

        // Pomelo does not implement OwnsMany(...).ToJson() or PrimitiveCollection()
        // query translation (see ADR-0033) — EventTypes is a plain JSON-column-
        // backed scalar here instead, same fallback as Flag.Tags.
        builder.Property(e => e.EventTypes).AsJsonColumn();

        builder.Property(e => e.CreatedAt);
        builder.Property(e => e.UpdatedAt);

        builder.HasIndex(e => e.EnvironmentId);
    }
}
