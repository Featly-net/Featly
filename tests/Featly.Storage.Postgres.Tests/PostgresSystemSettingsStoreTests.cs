using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.Postgres.Tests;

/// <summary>
/// Round-trips for <c>PostgresSystemSettingsStore</c>, mirroring
/// <c>SqliteSystemSettingsStoreTests</c>: a typed-singleton row keyed by
/// aggregate key, with a JSON payload and audit stamps. Upsert is
/// replace-in-place by key.
/// </summary>
[Trait("Category", "RequiresPostgres")]
public class PostgresSystemSettingsStoreTests
{
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public async Task Upsert_then_get_round_trips_the_payload()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.SystemSettingsStore;

        var setting = new SystemSetting
        {
            Key = "webhook",
            Payload = Json(new { MaxRetries = 8, RetryBackoffMinSeconds = 2 }),
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = "ada@featly.dev",
        };
        await store.UpsertAsync(setting, ct);

        var loaded = await store.GetAsync("webhook", ct);
        loaded.Should().NotBeNull();
        loaded!.Key.Should().Be("webhook");
        loaded.UpdatedBy.Should().Be("ada@featly.dev");
        loaded.Payload.GetProperty("MaxRetries").GetInt32().Should().Be(8);
        loaded.Payload.GetProperty("RetryBackoffMinSeconds").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Upsert_replaces_in_place_by_key()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.SystemSettingsStore;

        await store.UpsertAsync(new SystemSetting
        {
            Key = "webhook",
            Payload = Json(new { MaxRetries = 5 }),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedBy = "first",
        }, ct);

        var later = DateTimeOffset.UtcNow;
        await store.UpsertAsync(new SystemSetting
        {
            Key = "webhook",
            Payload = Json(new { MaxRetries = 12 }),
            UpdatedAt = later,
            UpdatedBy = "second",
        }, ct);

        (await store.ListAsync(ct)).Should().ContainSingle();
        var loaded = await store.GetAsync("webhook", ct);
        loaded!.Payload.GetProperty("MaxRetries").GetInt32().Should().Be(12);
        loaded.UpdatedBy.Should().Be("second");
        loaded.UpdatedAt.Should().BeCloseTo(later, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Get_returns_null_for_an_unknown_key()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        (await host.SystemSettingsStore.GetAsync("missing", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task List_returns_every_singleton_ordered_by_key()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.SystemSettingsStore;

        await store.UpsertAsync(new SystemSetting { Key = "webhook", Payload = Json(new { a = 1 }), UpdatedAt = DateTimeOffset.UtcNow }, ct);
        await store.UpsertAsync(new SystemSetting { Key = "authorization", Payload = Json(new { b = 2 }), UpdatedAt = DateTimeOffset.UtcNow }, ct);

        var all = await store.ListAsync(ct);
        all.Select(s => s.Key).Should().Equal("authorization", "webhook");
    }
}
