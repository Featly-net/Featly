namespace Featly;

/// <summary>
/// What an <see cref="ApiKey"/> can do. Two coarse scopes mirror the legacy
/// <c>AdminApiKey</c> / <c>SdkApiKey</c> split that <c>v0.0.x</c> shipped with
/// — fine-grained permissions come from the role layer (<see cref="Permission"/>),
/// not from the key itself.
/// </summary>
public enum ApiKeyScope
{
    /// <summary>
    /// Bearer can read the SDK snapshot (<c>GET /api/sdk/config</c>) and
    /// subscribe to the SSE stream. No admin endpoints.
    /// </summary>
    SdkRead,

    /// <summary>
    /// Bearer can call every admin endpoint. The actual <see cref="Permission"/>
    /// each call requires is still enforced by the permission checker in M6 PR 6C.
    /// </summary>
    AdminWrite,
}
