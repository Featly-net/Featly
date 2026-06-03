using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Featly.Server.Endpoints;

/// <summary>
/// Public metadata about this Featly instance. The embedded dashboard reads it at
/// boot to render only the enabled feature areas (ADR-0024). Unauthenticated: it
/// exposes only which areas are on, nothing sensitive.
/// </summary>
internal static class MetaEndpoints
{
    public static RouteGroupBuilder MapMeta(this RouteGroupBuilder group, FeatlyFeatureOptions features)
    {
        ArgumentNullException.ThrowIfNull(features);

        var snapshot = new FeatlyFeatureFlags(
            features.Flags,
            features.Configs,
            features.Segments,
            features.Experiments,
            features.Approvals,
            features.Webhooks,
            features.Audit,
            features.Rbac);

        group.MapGet("/meta", () => Results.Ok(new FeatlyMetaResponse(snapshot)))
            .WithName("Featly.Meta")
            .WithDescription("Public instance metadata — the enabled feature areas the dashboard renders.");

        return group;
    }
}

/// <summary>Public instance metadata returned by <c>GET /api/meta</c>.</summary>
public sealed record FeatlyMetaResponse(FeatlyFeatureFlags Features);

/// <summary>Which feature areas are enabled on this instance (ADR-0024).</summary>
public sealed record FeatlyFeatureFlags(
    bool Flags,
    bool Configs,
    bool Segments,
    bool Experiments,
    bool Approvals,
    bool Webhooks,
    bool Audit,
    bool Rbac);
