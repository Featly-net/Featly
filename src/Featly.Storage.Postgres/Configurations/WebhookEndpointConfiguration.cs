using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Postgres.Configurations;

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
        // nullable timestamptz.
        builder.Property(e => e.ConsecutiveFailures);
        builder.Property(e => e.CircuitOpenUntil);

        // Subscribed event types serialised as a native jsonb array, same
        // pattern as Flag.Tags.
        builder.PrimitiveCollection(e => e.EventTypes);

        // Npgsql maps DateTimeOffset to a native timestamptz column — no ticks
        // conversion needed (ADR-0026), unlike the SQLite provider.
        builder.Property(e => e.CreatedAt);
        builder.Property(e => e.UpdatedAt);

        builder.HasIndex(e => e.EnvironmentId);
    }
}
