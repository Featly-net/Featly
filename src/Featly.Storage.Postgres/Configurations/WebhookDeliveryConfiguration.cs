using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Postgres.Configurations;

internal sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("WebhookDeliveries");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.WebhookEndpointId).IsRequired();
        builder.Property(d => d.EventType).IsRequired().HasMaxLength(128);
        builder.Property(d => d.Payload).IsRequired();
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(d => d.AttemptCount);
        builder.Property(d => d.LastStatusCode);
        builder.Property(d => d.LastError).HasMaxLength(4096);

        // Npgsql maps DateTimeOffset to a native timestamptz column — no ticks
        // conversion needed (ADR-0026), unlike the SQLite provider.
        builder.Property(d => d.NextAttemptAt);
        builder.Property(d => d.CreatedAt);
        builder.Property(d => d.UpdatedAt);
        builder.Property(d => d.DeliveredAt);

        // The worker's claim query filters by status + due time.
        builder.HasIndex(d => new { d.Status, d.NextAttemptAt });
        builder.HasIndex(d => d.WebhookEndpointId);
    }
}
