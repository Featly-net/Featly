using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.Postgres.Tests;

/// <summary>
/// Persistence + lookup behaviour of <c>PostgresApiKeyStore</c>, mirroring
/// <c>SqliteApiKeyStoreTests</c>. The store holds only Argon2 hashes — these
/// tests poke at the row shape and the indexed prefix lookup the auth
/// pipeline calls on every request.
/// </summary>
[Trait("Category", "RequiresPostgres")]
public class PostgresApiKeyStoreTests
{
    [Fact]
    public async Task Create_then_FindCandidatesByPrefix_returns_the_row()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.ApiKeyStore;
        var envId = Guid.NewGuid();

        var key = NewKey(envId, "featly_AAAAA");
        await store.CreateAsync(key, ct);

        var matches = await store.FindCandidatesByPrefixAsync("featly_AAAAA", ct);
        matches.Should().ContainSingle().Which.Id.Should().Be(key.Id);
    }

    [Fact]
    public async Task FindCandidatesByPrefix_omits_revoked_keys()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.ApiKeyStore;
        var envId = Guid.NewGuid();

        var key = NewKey(envId, "featly_BBBBB");
        await store.CreateAsync(key, ct);
        await store.RevokeAsync(key.Id, actor: "admin", ct);

        var matches = await store.FindCandidatesByPrefixAsync("featly_BBBBB", ct);
        matches.Should().BeEmpty();

        // GetById still returns the revoked row so audit can resolve it.
        var byId = await store.GetByIdAsync(key.Id, ct);
        byId!.Revoked.Should().BeTrue();
    }

    [Fact]
    public async Task ListAsync_filters_by_environment_and_sorts_by_creation_desc()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.ApiKeyStore;
        var envA = Guid.NewGuid();
        var envB = Guid.NewGuid();

        var older = NewKey(envA, "featly_OLDER", DateTimeOffset.UtcNow.AddMinutes(-5));
        await store.CreateAsync(older, ct);

        var newer = NewKey(envA, "featly_NEWER", DateTimeOffset.UtcNow);
        await store.CreateAsync(newer, ct);

        await store.CreateAsync(NewKey(envB, "featly_OTHER"), ct);

        var listA = await store.ListAsync(envA, ct);
        listA.Select(k => k.Prefix).Should().BeEquivalentTo(["featly_NEWER", "featly_OLDER"], opts => opts.WithStrictOrdering());

        var listB = await store.ListAsync(envB, ct);
        listB.Should().ContainSingle().Which.Prefix.Should().Be("featly_OTHER");
    }

    [Fact]
    public async Task UserId_binding_round_trips()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.ApiKeyStore;
        var envId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var bound = new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = "bound key",
            Prefix = "featly_BOUND",
            Hash = "argon2id$v=19$m=65536,t=3,p=2$" + Convert.ToBase64String(new byte[16]) + "$" + Convert.ToBase64String(new byte[32]),
            Scope = ApiKeyScope.AdminWrite,
            EnvironmentId = envId,
            UserId = userId,
            Revoked = false,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "test",
        };
        await store.CreateAsync(bound, ct);

        var loaded = await store.GetByIdAsync(bound.Id, ct);
        loaded!.UserId.Should().Be(userId);

        // An unbound key keeps a null UserId.
        var unbound = NewKey(envId, "featly_UNBND");
        await store.CreateAsync(unbound, ct);
        (await store.GetByIdAsync(unbound.Id, ct))!.UserId.Should().BeNull();
    }

    [Fact]
    public async Task TouchLastUsed_updates_the_timestamp()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.ApiKeyStore;
        var envId = Guid.NewGuid();

        var key = NewKey(envId, "featly_CCCCC");
        await store.CreateAsync(key, ct);

        var when = new DateTimeOffset(2026, 5, 27, 9, 0, 0, TimeSpan.Zero);
        await store.TouchLastUsedAsync(key.Id, when, ct);

        var loaded = await store.GetByIdAsync(key.Id, ct);
        loaded!.LastUsedAt.Should().Be(when);

        // Missing id is a no-op.
        await store.TouchLastUsedAsync(Guid.NewGuid(), when, ct);
    }

    [Fact]
    public async Task ExpiresAt_round_trips_and_defaults_to_null()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.ApiKeyStore;
        var envId = Guid.NewGuid();

        var expiry = new DateTimeOffset(2027, 3, 15, 12, 30, 0, TimeSpan.Zero);
        var expiring = new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = "expiring key",
            Prefix = "featly_EXPIR",
            Hash = "argon2id$v=19$m=65536,t=3,p=2$" + Convert.ToBase64String(new byte[16]) + "$" + Convert.ToBase64String(new byte[32]),
            Scope = ApiKeyScope.AdminWrite,
            EnvironmentId = envId,
            ExpiresAt = expiry,
            Revoked = false,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "test",
        };
        await store.CreateAsync(expiring, ct);

        var loaded = await store.GetByIdAsync(expiring.Id, ct);
        loaded!.ExpiresAt.Should().Be(expiry);

        // A key without an expiry keeps a null ExpiresAt.
        var forever = NewKey(envId, "featly_FORVR");
        await store.CreateAsync(forever, ct);
        (await store.GetByIdAsync(forever.Id, ct))!.ExpiresAt.Should().BeNull();
    }

    private static ApiKey NewKey(Guid environmentId, string prefix, DateTimeOffset? createdAt = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "test key",
        Prefix = prefix,
        Hash = "argon2id$v=19$m=65536,t=3,p=2$" + Convert.ToBase64String(new byte[16]) + "$" + Convert.ToBase64String(new byte[32]),
        Scope = ApiKeyScope.AdminWrite,
        EnvironmentId = environmentId,
        Revoked = false,
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        CreatedBy = "test",
    };
}
