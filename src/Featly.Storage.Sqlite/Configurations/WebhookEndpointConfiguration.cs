using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Sqlite.Configurations;

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

        // Subscribed event types as a JSON array, same pattern as Flag.Tags.
        builder.PrimitiveCollection(e => e.EventTypes);

        builder.Property(e => e.CreatedAt).HasConversion<DateTimeOffsetTicksConverter>();
        builder.Property(e => e.UpdatedAt).HasConversion<DateTimeOffsetTicksConverter>();

        builder.HasIndex(e => e.EnvironmentId);
    }
}
