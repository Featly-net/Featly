using System.Text.Json;
using AwesomeAssertions;
using Featly.Sdk.Internal;
using Xunit;

namespace Featly.Sdk.Tests;

/// <summary>
/// Covers the SDK's on-disk snapshot cache and static bootstrap file (issue #238):
/// a fresh snapshot round-trips through the cache file, a bare server-shaped
/// snapshot loads as a bootstrap, and missing / corrupt files fail soft to null.
/// </summary>
public class SnapshotFileStoreTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly JsonElement True = JsonSerializer.SerializeToElement(true);
    private static readonly JsonElement False = JsonSerializer.SerializeToElement(false);

    private static ConfigSnapshot SampleSnapshot() => new(
        Guid.NewGuid(),
        "development",
        DateTimeOffset.UtcNow,
        Flags: [new Flag
        {
            Id = Guid.NewGuid(),
            Key = "demo",
            Name = "Demo",
            Type = FlagType.Boolean,
            Enabled = true,
            DefaultVariantKey = "off",
            EnvironmentId = Guid.NewGuid(),
            Variants =
            [
                new Variant { Key = "on", Name = "On", Value = True },
                new Variant { Key = "off", Name = "Off", Value = False },
            ],
        }],
        Segments: [],
        Configs: []);

    [Fact]
    public async Task Cache_round_trips_snapshot_and_etag()
    {
        var path = Path.Combine(Path.GetTempPath(), $"featly-cache-{Guid.NewGuid():N}.json");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var snapshot = SampleSnapshot();
            await FeatlySnapshotFileStore.SaveCacheAsync(path, snapshot, "\"etag-123\"", ct);

            var loaded = await FeatlySnapshotFileStore.LoadCacheAsync(path, ct);

            loaded.Should().NotBeNull();
            loaded!.Value.Etag.Should().Be("\"etag-123\"");
            loaded.Value.Snapshot.Flags.Should().ContainSingle(f => f.Key == "demo" && f.Enabled);
            loaded.Value.Snapshot.Flags[0].Variants.Should().Contain(v => v.Key == "on");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Bootstrap_loads_a_bare_server_shaped_snapshot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"featly-boot-{Guid.NewGuid():N}.json");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            // The bootstrap file is exactly what GET /api/sdk/config returns.
            var json = JsonSerializer.Serialize(SampleSnapshot(), Web);
            await File.WriteAllTextAsync(path, json, ct);

            var loaded = await FeatlySnapshotFileStore.LoadBootstrapAsync(path, ct);

            loaded.Should().NotBeNull();
            loaded!.Flags.Should().ContainSingle(f => f.Key == "demo");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Missing_and_corrupt_files_return_null()
    {
        var ct = TestContext.Current.CancellationToken;
        var missing = Path.Combine(Path.GetTempPath(), $"featly-missing-{Guid.NewGuid():N}.json");
        (await FeatlySnapshotFileStore.LoadCacheAsync(missing, ct)).Should().BeNull();
        (await FeatlySnapshotFileStore.LoadBootstrapAsync(missing, ct)).Should().BeNull();

        var corrupt = Path.Combine(Path.GetTempPath(), $"featly-corrupt-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(corrupt, "{ not valid json", ct);
            (await FeatlySnapshotFileStore.LoadCacheAsync(corrupt, ct)).Should().BeNull();
            (await FeatlySnapshotFileStore.LoadBootstrapAsync(corrupt, ct)).Should().BeNull();
        }
        finally
        {
            File.Delete(corrupt);
        }
    }
}
