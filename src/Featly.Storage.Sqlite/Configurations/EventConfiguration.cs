using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Sqlite.Configurations;

internal sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.FlagKey).HasMaxLength(128);
        builder.Property(e => e.ConfigKey).HasMaxLength(128);
        builder.Property(e => e.CustomKey).HasMaxLength(128);
        builder.Property(e => e.SubjectKey).IsRequired().HasMaxLength(256);
        builder.Property(e => e.VariantKey).HasMaxLength(128);
        builder.Property(e => e.EnvironmentId).IsRequired();

        builder.Property(e => e.At).HasConversion<DateTimeOffsetTicksConverter>();

        // Arbitrary properties (revenue, plan, etc.) stored as a JSON object.
        // A value comparer keeps EF's change tracking happy for the dictionary.
        var propertiesComparer = new ValueComparer<Dictionary<string, JsonElement>?>(
            static (a, b) => JsonEquals(a, b),
            static d => d == null ? 0 : JsonSerializer.Serialize(d, JsonOpts).GetHashCode(StringComparison.Ordinal),
            static d => d == null ? null : new Dictionary<string, JsonElement>(d, StringComparer.Ordinal));

        builder.Property(e => e.Properties)
            .HasConversion(
                static value => value == null ? null : JsonSerializer.Serialize(value, JsonOpts),
                static text => string.IsNullOrWhiteSpace(text)
                    ? null
                    : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(text, JsonOpts))
            .Metadata.SetValueComparer(propertiesComparer);

        // Analytics queries filter by environment + type + flag/custom key.
        builder.HasIndex(e => e.EnvironmentId);
        builder.HasIndex(e => new { e.EnvironmentId, e.Type });
        builder.HasIndex(e => new { e.EnvironmentId, e.FlagKey });
        builder.HasIndex(e => new { e.EnvironmentId, e.CustomKey });
        builder.HasIndex(e => e.SubjectKey);
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static bool JsonEquals(Dictionary<string, JsonElement>? a, Dictionary<string, JsonElement>? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a == null || b == null)
        {
            return false;
        }

        return JsonSerializer.Serialize(a, JsonOpts) == JsonSerializer.Serialize(b, JsonOpts);
    }
}
