using System.Collections.Concurrent;

namespace Featly.Storage.InMemory;

internal sealed class InMemoryUserStore : IUserStore
{
    // Identifier is the natural key; row id is generated server-side.
    private readonly ConcurrentDictionary<string, User> _byIdentifier = new(StringComparer.Ordinal);

    public Task<User?> GetByIdentifierAsync(string identifier, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        return Task.FromResult(_byIdentifier.TryGetValue(identifier, out var u) ? u : null);
    }

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_byIdentifier.Values.FirstOrDefault(u => u.Id == id));

    public Task<IReadOnlyList<User>> ListAsync(CancellationToken ct)
    {
        var list = _byIdentifier.Values
            .OrderBy(u => u.Identifier, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<IReadOnlyList<User>>(list);
    }

    public Task UpsertAsync(User user, string actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        user.UpdatedAt = DateTimeOffset.UtcNow;
        user.UpdatedBy = actor;

        _byIdentifier.AddOrUpdate(
            user.Identifier,
            _ => user,
            (_, existing) =>
            {
                existing.DisplayName = user.DisplayName;
                existing.Email = user.Email;
                existing.Disabled = user.Disabled;
                existing.UpdatedAt = user.UpdatedAt;
                existing.UpdatedBy = user.UpdatedBy;
                return existing;
            });
        return Task.CompletedTask;
    }

    public Task DisableAsync(string identifier, string actor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        if (_byIdentifier.TryGetValue(identifier, out var existing))
        {
            existing.Disabled = true;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.UpdatedBy = actor;
        }
        return Task.CompletedTask;
    }
}
