using Featly.Authorization;
using Microsoft.Extensions.Options;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Authentication;

/// <summary>
/// Default <see cref="IFeatlyPermissionChecker"/> for v0.0.x. Each user is
/// effectively a single role:
/// <list type="bullet">
///   <item>The bootstrap admin identifier from <see cref="FeatlyAuthorizationOptions"/> is mapped to the seeded <c>admin</c> system role.</item>
///   <item>Legacy <c>AdminApiKey</c> requests (resolved as <c>api-key:Admin</c>) also map to <c>admin</c>.</item>
///   <item>Legacy <c>SdkApiKey</c> requests map to <c>viewer</c> for read endpoints; admin endpoints deny.</item>
///   <item>Any other authenticated user gets the <c>viewer</c> system role.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// This is the deliberately-thin checker that ships in M6 PR 6C. Full
/// <c>RoleAssignment</c>-driven resolution (per ARCHITECTURE.md §11) with
/// project / environment scoping, user groups, and custom roles lands in M7.
/// </para>
/// <para>
/// The checker pulls the role's <see cref="Role.Permissions"/> from the store
/// each call. Cache invalidation isn't a concern at v0.0.x scale — admin
/// throughput is human-paced and Argon2 already dominates the per-request
/// budget. M7 introduces an in-process role cache.
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

        var roleKey = ResolveRoleKey(user.Identifier);
        var role = await store.Roles.GetByKeyAsync(roleKey, ct).ConfigureAwait(false);
        if (role is null)
        {
            // Role not seeded yet (boot race or storage misconfiguration).
            // Fail closed.
            return false;
        }
        return role.Permissions.Contains(permission);
    }

    private string ResolveRoleKey(string identifier)
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

        return SystemRoles.ViewerKey;
    }
}
