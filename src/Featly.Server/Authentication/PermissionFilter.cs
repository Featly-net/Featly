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
/// Scope: M6 PR 6C wires the filter onto every admin endpoint group. The filter
/// resolves the request's target environment (from the <c>?env=</c> query the
/// entity endpoints use, else the default environment) and passes it — with the
/// default project — to the checker, so environment-scoped role assignments are
/// honored (issue #193). A wildcard assignment (<c>EnvironmentId == null</c>)
/// still matches any environment, so this only tightens scoping; it never
/// broadens it. Multi-project request scoping (a per-request project selector)
/// remains future work.
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

        // Resolve the request's (project, environment) so environment-scoped role
        // assignments are actually honored (issue #193). The environment comes
        // from the ?env= query the entity endpoints already use; absent that, the
        // default environment. A wildcard assignment (EnvironmentId == null) still
        // matches any concrete environment, so this only tightens scoping.
        var (projectId, environmentId) = await ResolveScopeAsync(http).ConfigureAwait(false);

        var ok = await checker.HasAsync(resolved, projectId, environmentId, required, http.RequestAborted).ConfigureAwait(false);
        if (!ok)
        {
            return Results.Problem(
                detail: $"User '{identifier}' lacks the '{required}' permission.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the (project, environment) a request targets for the permission
    /// check. Reads the <c>env</c> query the entity endpoints use; falls back to
    /// the default environment. Returns <c>(Guid.Empty, null)</c> when the store
    /// or default project is unavailable, preserving the pre-#193 behavior.
    /// </summary>
    private static async Task<(Guid ProjectId, Guid? EnvironmentId)> ResolveScopeAsync(HttpContext http)
    {
        var store = http.RequestServices.GetService<StorageFacade>();
        if (store is null)
        {
            return (Guid.Empty, null);
        }

        var ct = http.RequestAborted;
        var project = await store.Projects.GetDefaultAsync(ct).ConfigureAwait(false);
        if (project is null)
        {
            return (Guid.Empty, null);
        }

        var envKey = http.Request.Query["env"].ToString();
        var environment = string.IsNullOrWhiteSpace(envKey)
            ? await store.Environments.GetDefaultAsync(project.Id, ct).ConfigureAwait(false)
            : await store.Environments.GetByKeyAsync(project.Id, envKey, ct).ConfigureAwait(false);

        return (project.Id, environment?.Id);
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
