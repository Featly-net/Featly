using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.Postgres.Configurations;

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

        // Npgsql maps DateTimeOffset to a native timestamptz column — no ticks
        // conversion needed (ADR-0026), unlike the SQLite provider.
        builder.Property(k => k.CreatedAt);
        builder.Property(k => k.CreatedBy).HasMaxLength(256);
        builder.Property(k => k.LastUsedAt);
        builder.Property(k => k.ExpiresAt);

        // Lookup path: WHERE Prefix = ? AND Revoked = false. Indexing Prefix
        // keeps that O(log n).
        builder.HasIndex(k => k.Prefix);
        builder.HasIndex(k => k.EnvironmentId);
    }
}
