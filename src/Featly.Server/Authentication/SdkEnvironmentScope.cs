using System.Security.Claims;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Authentication;

/// <summary>
/// Helper for resolving and enforcing an SDK credential's environment binding
/// (ADR-0009, issue #188). A persisted <see cref="ApiKey"/> carries a
/// <see cref="FeatlyAuthenticationDefaults.EnvironmentClaim"/>; the static
/// bootstrap key does not and is treated as wildcard. Shared by the SDK config,
/// stream, and events endpoints so the resolution/enforcement logic lives once.
/// </summary>
internal static class SdkEnvironmentScope
{
    /// <summary>
    /// The environment id the presented credential is bound to, or <c>null</c>
    /// when the credential is unbound (static bootstrap key) and may read any
    /// environment.
    /// </summary>
    public static Guid? BoundEnvironmentId(ClaimsPrincipal? user)
    {
        var value = user?.FindFirst(FeatlyAuthenticationDefaults.EnvironmentClaim)?.Value;
        return Guid.TryParse(value, out var id) ? id : null;
    }

    /// <summary>
    /// True when a credential bound to <paramref name="boundEnvironmentId"/> is
    /// allowed to act on <paramref name="targetEnvironmentId"/>. Unbound
    /// credentials (<c>null</c>) are allowed everywhere.
    /// </summary>
    public static bool Allows(Guid? boundEnvironmentId, Guid targetEnvironmentId)
        => boundEnvironmentId is not Guid bound || bound == targetEnvironmentId;

    /// <summary>
    /// Resolves the environment an SDK request targets. When the credential is
    /// env-bound and the caller didn't name an environment, resolution goes to
    /// the key's own environment (ergonomic and leak-free) rather than the
    /// project default. Returns <c>null</c> when nothing resolves.
    /// </summary>
    public static async Task<Environment?> ResolveAsync(
        StorageFacade store, string? envKey, Guid? boundEnvironmentId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(envKey) && boundEnvironmentId is Guid boundId)
        {
            return await store.Environments.GetByIdAsync(boundId, ct).ConfigureAwait(false);
        }

        var project = await store.Projects.GetDefaultAsync(ct).ConfigureAwait(false);
        if (project is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(envKey)
            ? await store.Environments.GetDefaultAsync(project.Id, ct).ConfigureAwait(false)
            : await store.Environments.GetByKeyAsync(project.Id, envKey, ct).ConfigureAwait(false);
    }
}
