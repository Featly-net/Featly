using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Sqlite.Configurations;

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ApiKeys");
        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).ValueGeneratedNever();

        builder.Property(k => k.Name).IsRequired().HasMaxLength(128);
        builder.Property(k => k.Prefix).IsRequired().HasMaxLength(32);
        builder.Property(k => k.Hash).IsRequired().HasMaxLength(512);

        builder.Property(k => k.Scope)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(k => k.EnvironmentId).IsRequired();
        builder.Property(k => k.UserId);
        builder.Property(k => k.Revoked);

        builder.Property(k => k.CreatedAt).HasConversion<DateTimeOffsetTicksConverter>();
        builder.Property(k => k.CreatedBy).HasMaxLength(256);
        builder.Property(k => k.LastUsedAt)
            .HasConversion(
                static v => v.HasValue ? v.Value.UtcTicks : (long?)null,
                static t => t.HasValue ? new DateTimeOffset(t.Value, TimeSpan.Zero) : (DateTimeOffset?)null);
        builder.Property(k => k.ExpiresAt)
            .HasConversion(
                static v => v.HasValue ? v.Value.UtcTicks : (long?)null,
                static t => t.HasValue ? new DateTimeOffset(t.Value, TimeSpan.Zero) : (DateTimeOffset?)null);

        // Lookup path: WHERE Prefix = ? AND Revoked = false. Indexing Prefix
        // keeps that O(log n).
        builder.HasIndex(k => k.Prefix);
        builder.HasIndex(k => k.EnvironmentId);
    }
}
