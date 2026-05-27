using FluentAssertions;
using Xunit;

namespace Featly.Storage.Sqlite.Tests;

/// <summary>
/// Round-trips for the SQLite-backed user store. The store is the persistence
/// layer the M6 auth pipeline consumes; this fixture covers create, update,
/// disable, list, and the uniqueness constraint on <see cref="User.Identifier"/>.
/// </summary>
public class SqliteUserStoreTests
{
    [Fact]
    public async Task Upsert_then_get_round_trips()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        await host.Store.Users.UpsertAsync(NewUser("alice@example.com", "Alice"), "bootstrap", ct);

        var loaded = await host.Store.Users.GetByIdentifierAsync("alice@example.com", ct);
        loaded.Should().NotBeNull();
        loaded!.DisplayName.Should().Be("Alice");
        loaded.Disabled.Should().BeFalse();
        loaded.UpdatedBy.Should().Be("bootstrap");
    }

    [Fact]
    public async Task Upsert_updates_existing_user_by_identifier_keeping_id()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        await host.Store.Users.UpsertAsync(NewUser("alice@example.com", "Alice"), "bootstrap", ct);
        var originalId = (await host.Store.Users.GetByIdentifierAsync("alice@example.com", ct))!.Id;

        var update = NewUser("alice@example.com", "Alice Souza");
        update.Email = "alice.souza@example.com";
        await host.Store.Users.UpsertAsync(update, "admin", ct);

        var loaded = await host.Store.Users.GetByIdentifierAsync("alice@example.com", ct);
        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(originalId);
        loaded.DisplayName.Should().Be("Alice Souza");
        loaded.Email.Should().Be("alice.souza@example.com");
        loaded.UpdatedBy.Should().Be("admin");
    }

    [Fact]
    public async Task Disable_marks_user_as_disabled_and_is_idempotent_for_missing_user()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        await host.Store.Users.UpsertAsync(NewUser("alice@example.com", "Alice"), "bootstrap", ct);

        await host.Store.Users.DisableAsync("alice@example.com", "admin", ct);
        var loaded = await host.Store.Users.GetByIdentifierAsync("alice@example.com", ct);
        loaded!.Disabled.Should().BeTrue();
        loaded.UpdatedBy.Should().Be("admin");

        // Missing identifier is a no-op, not an error.
        await host.Store.Users.DisableAsync("ghost@example.com", "admin", ct);
    }

    [Fact]
    public async Task ListAsync_returns_users_sorted_by_identifier()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        await host.Store.Users.UpsertAsync(NewUser("bob@example.com", "Bob"), "t", ct);
        await host.Store.Users.UpsertAsync(NewUser("alice@example.com", "Alice"), "t", ct);

        var users = await host.Store.Users.ListAsync(ct);
        users.Select(u => u.Identifier).Should().BeEquivalentTo(["alice@example.com", "bob@example.com"], opts => opts.WithStrictOrdering());
    }

    private static User NewUser(string identifier, string displayName) => new()
    {
        Id = Guid.NewGuid(),
        Identifier = identifier,
        DisplayName = displayName,
        Email = identifier,
        Disabled = false,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        CreatedBy = "test",
        UpdatedBy = "test",
    };
}
