using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Authentication;

/// <summary>
/// Boot-time advisory for the static bootstrap admin key (issue #209). The
/// appsettings <c>Featly:Server:AdminApiKey</c> is a shared, unattributable,
/// non-rotatable credential meant only to get the first admin in. Once a real,
/// role-bound admin user exists it can mint per-user keys, and the static key
/// should be removed. This detects that situation so the server can warn on
/// boot.
/// </summary>
/// <remarks>
/// The key is deliberately <b>not</b> disabled automatically: silently revoking
/// a live credential risks locking an operator out of a running deployment (and
/// runs against the project's "predictable, not magical" principle). Retiring it
/// stays a deliberate operator action, prompted by the warning. Detection is
/// read-only and runs once at startup, so it never touches the auth hot path.
/// </remarks>
internal static class BootstrapKeyAdvisor
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="adminApiKey"/> is configured and
    /// at least one enabled user holds the system <c>admin</c> role — the
    /// condition under which the operator should retire the static key.
    /// </summary>
    public static async Task<bool> ShouldWarnAsync(StorageFacade store, string? adminApiKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (string.IsNullOrEmpty(adminApiKey))
        {
            return false;
        }

        var adminRole = await store.Roles.GetByKeyAsync(SystemRoles.AdminKey, ct).ConfigureAwait(false);
        if (adminRole is null)
        {
            return false;
        }

        var users = await store.Users.ListAsync(ct).ConfigureAwait(false);
        foreach (var user in users)
        {
            if (user.Disabled)
            {
                continue;
            }

            var assignments = await store.RoleAssignments.ListForAssigneeAsync(user.Id, ct).ConfigureAwait(false);
            if (assignments.Any(a => a.RoleId == adminRole.Id))
            {
                return true;
            }
        }

        return false;
    }
}
