using FluentAssertions;
using Xunit;

namespace Featly.Storage.Sqlite.Tests;

/// <summary>
/// Persistence + lookup behaviour of <see cref="IApiKeyStore"/>. The store
/// holds only Argon2 hashes — these tests poke at the row shape and the
/// indexed prefix lookup the auth pipeline calls on every request.
/// </summary>
public class SqliteApiKeyStoreTests
{
    [Fact]
    public async Task Create_then_FindCandidatesByPrefix_returns_the_row()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();

        var key = NewKey(envId, "featly_AAAAA");
        await host.Store.ApiKeys.CreateAsync(key, ct);

        var matches = await host.Store.ApiKeys.FindCandidatesByPrefixAsync("featly_AAAAA", ct);
        matches.Should().ContainSingle().Which.Id.Should().Be(key.Id);
    }

    [Fact]
    public async Task FindCandidatesByPrefix_omits_revoked_keys()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();

        var key = NewKey(envId, "featly_BBBBB");
        await host.Store.ApiKeys.CreateAsync(key, ct);
        await host.Store.ApiKeys.RevokeAsync(key.Id, actor: "admin", ct);

        var matches = await host.Store.ApiKeys.FindCandidatesByPrefixAsync("featly_BBBBB", ct);
        matches.Should().BeEmpty();

        // GetById still returns the revoked row so audit can resolve it.
        var byId = await host.Store.ApiKeys.GetByIdAsync(key.Id, ct);
        byId!.Revoked.Should().BeTrue();
    }

    [Fact]
    public async Task ListAsync_filters_by_environment_and_sorts_by_creation_desc()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envA = Guid.NewGuid();
        var envB = Guid.NewGuid();

        var older = NewKey(envA, "featly_OLDER", DateTimeOffset.UtcNow.AddMinutes(-5));
        await host.Store.ApiKeys.CreateAsync(older, ct);

        var newer = NewKey(envA, "featly_NEWER", DateTimeOffset.UtcNow);
        await host.Store.ApiKeys.CreateAsync(newer, ct);

        await host.Store.ApiKeys.CreateAsync(NewKey(envB, "featly_OTHER"), ct);

        var listA = await host.Store.ApiKeys.ListAsync(envA, ct);
        listA.Select(k => k.Prefix).Should().BeEquivalentTo(["featly_NEWER", "featly_OLDER"], opts => opts.WithStrictOrdering());

        var listB = await host.Store.ApiKeys.ListAsync(envB, ct);
        listB.Should().ContainSingle().Which.Prefix.Should().Be("featly_OTHER");
    }

    [Fact]
    public async Task TouchLastUsed_updates_the_timestamp()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();

        var key = NewKey(envId, "featly_CCCCC");
        await host.Store.ApiKeys.CreateAsync(key, ct);

        var when = new DateTimeOffset(2026, 5, 27, 9, 0, 0, TimeSpan.Zero);
        await host.Store.ApiKeys.TouchLastUsedAsync(key.Id, when, ct);

        var loaded = await host.Store.ApiKeys.GetByIdAsync(key.Id, ct);
        loaded!.LastUsedAt.Should().Be(when);

        // Missing id is a no-op.
        await host.Store.ApiKeys.TouchLastUsedAsync(Guid.NewGuid(), when, ct);
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
