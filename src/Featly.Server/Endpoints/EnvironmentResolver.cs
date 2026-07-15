using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// Shared environment resolution for the admin endpoints (issue #220). Resolves
/// the default project's environment by key, or its default environment when no
/// key is given. Consolidates the copy of this logic that previously lived in
/// every admin endpoint file. SDK endpoints use
/// <see cref="Authentication.SdkEnvironmentScope"/> instead, which layers the
/// per-key environment binding on top.
/// </summary>
internal static class EnvironmentResolver
{
    /// <summary>Resolves the target environment, or <c>null</c> when it cannot be found.</summary>
    public static async Task<Environment?> ResolveAsync(StorageFacade store, string? envKey, CancellationToken ct)
    {
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
