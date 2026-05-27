using Featly.Authorization;
using Microsoft.AspNetCore.Http;

namespace Featly.AspNetCore.Authorization;

/// <summary>
/// Extension point for the auth pipeline. The configured
/// <see cref="IFeatlyUserResolver"/> inspects the incoming request and returns
/// a <see cref="ResolvedUser"/> (or <c>null</c> if the request is
/// unauthenticated).
/// </summary>
/// <remarks>
/// <para>
/// Lives in <c>Featly.AspNetCore</c> because it takes an <see cref="HttpContext"/>;
/// <see cref="ResolvedUser"/> itself stays in <c>Featly.Abstractions</c> so
/// non-web code (engine, SDK) can reference the contract without taking a
/// framework dependency.
/// </para>
/// <para>
/// Featly ships two built-in resolvers (basic auth and loopback) in M6 PR 6B.
/// Consumers wire their own by implementing this interface — typically pulling
/// the identifier from an OIDC claim, a JWT, or session cookie.
/// </para>
/// <para>
/// Returning <c>null</c> means "no user attached" and the permission checker
/// will deny everything; returning a <see cref="ResolvedUser"/> the system
/// does not know about triggers the auto-provision logic per
/// <c>AuthorizationSettings.AutoProvisionMode</c>.
/// </para>
/// </remarks>
public interface IFeatlyUserResolver
{
    /// <summary>
    /// Resolve the user attached to <paramref name="context"/>, or <c>null</c>
    /// if the request is anonymous.
    /// </summary>
    Task<ResolvedUser?> ResolveAsync(HttpContext context, CancellationToken ct);
}
