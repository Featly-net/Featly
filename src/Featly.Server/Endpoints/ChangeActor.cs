using System.Security.Claims;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// Resolves the acting <see cref="User"/> for change-workflow operations from
/// the request principal, auto-creating the row on first sight. Legacy
/// <c>api-key:*</c> pseudo-identities are not real users and resolve to
/// <c>null</c> — propose / approve / comment are human actions.
/// </summary>
internal static class ChangeActor
{
    public static async Task<User?> ResolveOrCreateAsync(StorageFacade store, ClaimsPrincipal principal, CancellationToken ct)
    {
        var identifier = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(identifier) || identifier.StartsWith("api-key:", StringComparison.Ordinal))
        {
            return null;
        }

        var existing = await store.Users.GetByIdentifierAsync(identifier, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        await store.Users.UpsertAsync(new User
        {
            Id = Guid.NewGuid(),
            Identifier = identifier,
            DisplayName = identifier,
            Email = identifier.Contains('@', StringComparison.Ordinal) ? identifier : null,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = "change-workflow",
            UpdatedBy = "change-workflow",
        }, actor: "change-workflow", ct).ConfigureAwait(false);
        return await store.Users.GetByIdentifierAsync(identifier, ct).ConfigureAwait(false);
    }
}
