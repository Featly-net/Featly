using Featly.Authorization;
using Microsoft.Extensions.Options;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Authentication;

/// <summary>
/// <see cref="IFeatlyPermissionChecker"/> backed by <see cref="RoleAssignment"/>
/// rows (ARCHITECTURE.md §11). Effective permissions are the union of every
/// role granted to the user by an assignment that matches the request's project
/// and an environment of either the request's or the wildcard (<c>null</c>).
/// </summary>
/// <remarks>
/// <para>
/// Two identities bypass assignment resolution with a hardcoded shortcut, so
/// the bootstrap story keeps working without seeding assignment rows:
/// <list type="bullet">
///   <item>Legacy <c>AdminApiKey</c> (resolved as <c>api-key:AdminWrite</c>) maps to <c>admin</c>.</item>
///   <item>Legacy <c>SdkApiKey</c> (resolved as <c>api-key:SdkRead</c>) maps to <c>viewer</c>.</item>
///   <item>The <see cref="FeatlyAuthorizationOptions.BootstrapAdminIdentifier"/> maps to <c>admin</c>.</item>
/// </list>
/// </para>
/// <para>
/// For every other (real) user, resolution unions the matching assignment
/// roles. When no assignment grants the asked-for permission, the fallback
/// depends on <see cref="FeatlyAuthorizationOptions.AutoProvisionMode"/>:
/// <c>Open</c> (the development default) treats everyone as at least
/// <c>viewer</c>; <c>Closed</c> denies unless an explicit assignment grants it.
/// </para>
/// <para>
/// The request currently arrives with <c>projectId == Guid.Empty</c> (the
/// <see cref="PermissionFilter"/> does not yet resolve a per-request project),
/// so the checker substitutes the default project. Per-project request scoping
/// lands when the dashboard grows a project selector later in M7.
/// </para>
/// <para>
/// Stores are read per call. Admin throughput is human-paced and Argon2 already
/// dominates the per-request budget, so a role cache is deferred.
/// </para>
/// </remarks>
internal sealed class DefaultFeatlyPermissionChecker(
    StorageFacade store,
    IOptions<FeatlyAuthorizationOptions> options) : IFeatlyPermissionChecker
{
    // Matches the names emitted by FeatlyApiKeyAuthenticationHandler when a
    // legacy AdminApiKey / SdkApiKey is presented as Bearer.
    private const string LegacyAdminIdentifier = "api-key:AdminWrite";
    private const string LegacySdkIdentifier = "api-key:SdkRead";

    public async Task<bool> HasAsync(
        ResolvedUser user,
        Guid projectId,
        Guid? environmentId,
        Permission permission,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);

        // 1. Hardcoded shortcuts for the legacy api keys and the bootstrap admin.
        var shortcutRoleKey = ResolveShortcutRoleKey(user.Identifier);
        if (shortcutRoleKey is not null)
        {
            var role = await store.Roles.GetByKeyAsync(shortcutRoleKey, ct).ConfigureAwait(false);
            return role is not null && role.Permissions.Contains(permission);
        }

        // 2. Real-user resolution via RoleAssignment union.
        var effectiveProjectId = await ResolveEffectiveProjectIdAsync(projectId, ct).ConfigureAwait(false);

        var userRow = await store.Users.GetByIdentifierAsync(user.Identifier, ct).ConfigureAwait(false);
        if (userRow is not null && !userRow.Disabled)
        {
            var assignments = await store.RoleAssignments
                .ListForAssigneesAsync([userRow.Id], ct)
                .ConfigureAwait(false);

            foreach (var roleId in MatchingRoleIds(assignments, effectiveProjectId, environmentId))
            {
                var role = await store.Roles.GetByIdAsync(roleId, ct).ConfigureAwait(false);
                if (role is not null && role.Permissions.Contains(permission))
                {
                    return true;
                }
            }
        }

        // 3. No assignment granted it. Apply the auto-provision-mode floor.
        var mode = options.Value.AutoProvisionMode ?? AutoProvisionMode.Open;
        if (mode == AutoProvisionMode.Open)
        {
            var viewer = await store.Roles.GetByKeyAsync(SystemRoles.ViewerKey, ct).ConfigureAwait(false);
            return viewer is not null && viewer.Permissions.Contains(permission);
        }

        // Closed: deny unless an explicit assignment granted it above.
        return false;
    }

    private static IEnumerable<Guid> MatchingRoleIds(
        IReadOnlyList<RoleAssignment> assignments,
        Guid effectiveProjectId,
        Guid? environmentId)
    {
        var seen = new HashSet<Guid>();
        foreach (var a in assignments)
        {
            if (a.ProjectId != effectiveProjectId)
            {
                continue;
            }
            // Wildcard (null) assignments apply to every environment; an
            // env-scoped assignment only applies when the request targets that
            // same environment.
            if (a.EnvironmentId is not null && a.EnvironmentId != environmentId)
            {
                continue;
            }
            if (seen.Add(a.RoleId))
            {
                yield return a.RoleId;
            }
        }
    }

    private string? ResolveShortcutRoleKey(string identifier)
    {
        if (string.Equals(identifier, LegacyAdminIdentifier, StringComparison.Ordinal))
        {
            return SystemRoles.AdminKey;
        }
        if (string.Equals(identifier, LegacySdkIdentifier, StringComparison.Ordinal))
        {
            return SystemRoles.ViewerKey;
        }

        var bootstrap = options.Value.BootstrapAdminIdentifier;
        if (!string.IsNullOrWhiteSpace(bootstrap) &&
            string.Equals(identifier, bootstrap, StringComparison.Ordinal))
        {
            return SystemRoles.AdminKey;
        }

        return null;
    }

    private async Task<Guid> ResolveEffectiveProjectIdAsync(Guid projectId, CancellationToken ct)
    {
        if (projectId != Guid.Empty)
        {
            return projectId;
        }
        var defaultProject = await store.Projects.GetDefaultAsync(ct).ConfigureAwait(false);
        return defaultProject?.Id ?? Guid.Empty;
    }
}
