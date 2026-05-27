using Featly.Authorization;
using Microsoft.AspNetCore.Http;

namespace Featly.AspNetCore.Authorization;

/// <summary>
/// Pluggable authorization gate the dashboard middleware (and any other
/// admin-scoped route) consults to decide whether a request may proceed.
/// </summary>
/// <remarks>
/// <para>
/// Featly ships two built-in implementations (<see cref="FeatlyBasicAuthFilter"/>
/// and <see cref="FeatlyLoopbackAuthFilter"/>); consumers can register their
/// own that read the user from any source (OIDC claim, JWT, session cookie,
/// custom header).
/// </para>
/// <para>
/// A filter returns a <see cref="ResolvedUser"/> if the request is
/// authenticated, or <c>null</c> to deny. The caller (middleware in M6 PR 6C)
/// translates a <c>null</c> result into <c>401</c>.
/// </para>
/// <para>
/// This is the user-identification gate only — the per-action
/// <c>Permission</c> check happens downstream through
/// <see cref="IFeatlyPermissionChecker"/>.
/// </para>
/// </remarks>
public interface IFeatlyDashboardAuthorizationFilter
{
    /// <summary>
    /// Inspect <paramref name="context"/> and return the resolved user, or
    /// <c>null</c> to deny.
    /// </summary>
    Task<ResolvedUser?> AuthorizeAsync(HttpContext context, CancellationToken ct);
}
