using Featly.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Authentication;

/// <summary>
/// Minimal-API endpoint filter that enforces a single <see cref="Permission"/>
/// after the request has been authenticated. The auth scheme (existing
/// <see cref="FeatlyApiKeyAuthenticationHandler"/> in v0.0.x) writes the
/// identifier into <see cref="System.Security.Claims.ClaimTypes.Name"/>; this
/// filter rebuilds a <see cref="ResolvedUser"/> from that claim and asks
/// <see cref="IFeatlyPermissionChecker"/> whether the user holds the asked-for
/// permission. Returns <c>403</c> when not, <c>401</c> when there's no user.
/// </summary>
/// <remarks>
/// <para>
/// Scope: M6 PR 6C wires the filter onto every admin endpoint group. The
/// project / environment ids passed to the checker are <c>Guid.Empty</c> /
/// <c>null</c> until M7 introduces project-scoped role assignments. That's
/// fine — the v0.0.x default checker ignores those parameters and resolves
/// against a single per-user role.
/// </para>
/// </remarks>
internal sealed class PermissionFilter(Permission required) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var user = http.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        var identifier = user.Identity.Name;
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return Results.Unauthorized();
        }

        var resolved = new ResolvedUser(identifier, identifier);
        var checker = http.RequestServices.GetRequiredService<IFeatlyPermissionChecker>();

        // Auto-provision (Open mode): create a Viewer user row on first sight
        // so audit downstream has something to point at. Cheap and idempotent.
        await AutoProvisionAsync(http, resolved).ConfigureAwait(false);

        var ok = await checker.HasAsync(resolved, projectId: Guid.Empty, environmentId: null, required, http.RequestAborted).ConfigureAwait(false);
        if (!ok)
        {
            return Results.Problem(
                detail: $"User '{identifier}' lacks the '{required}' permission.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context).ConfigureAwait(false);
    }

    private static async Task AutoProvisionAsync(HttpContext http, ResolvedUser resolved)
    {
        // Skip legacy api-key pseudo-identifiers and never auto-provision them
        // as real Users.
        if (resolved.Identifier.StartsWith("api-key:", StringComparison.Ordinal))
        {
            return;
        }

        var store = http.RequestServices.GetService<StorageFacade>();
        if (store is null)
        {
            return;
        }

        var existing = await store.Users.GetByIdentifierAsync(resolved.Identifier, http.RequestAborted).ConfigureAwait(false);
        if (existing is not null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        await store.Users.UpsertAsync(new User
        {
            Id = Guid.NewGuid(),
            Identifier = resolved.Identifier,
            DisplayName = resolved.DisplayName,
            Email = resolved.Identifier.Contains('@', StringComparison.Ordinal) ? resolved.Identifier : null,
            Disabled = false,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = "auto-provision",
            UpdatedBy = "auto-provision",
        }, actor: "auto-provision", http.RequestAborted).ConfigureAwait(false);
    }
}

/// <summary>
/// DI helper for attaching the per-endpoint <see cref="Permission"/> filter
/// in a fluent way (<c>group.MapGet(…).RequirePermission(Permission.FlagRead)</c>).
/// </summary>
internal static class PermissionFilterExtensions
{
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, Permission permission)
        where TBuilder : Microsoft.AspNetCore.Builder.IEndpointConventionBuilder
        => builder.AddEndpointFilter(new PermissionFilter(permission));
}
