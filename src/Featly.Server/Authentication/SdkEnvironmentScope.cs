using System.Security.Claims;

namespace Featly.Server.Authentication;

/// <summary>
/// Helper for enforcing an SDK credential's environment binding (ADR-0009). A
/// persisted <see cref="ApiKey"/> carries a <see cref="FeatlyAuthenticationDefaults.EnvironmentClaim"/>;
/// the static bootstrap key does not and is treated as wildcard.
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
}
