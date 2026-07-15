using System.Security.Cryptography;
using System.Text.Json;
using Featly.Server.Authentication;
using Featly.Server.Webhooks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// Admin API for managing <see cref="WebhookEndpoint"/>s and inspecting their
/// deliveries (ARCHITECTURE.md §17). Behind the admin policy with per-route
/// <c>Webhook*</c> permissions.
/// </summary>
internal static class AdminWebhooksEndpoints
{
    private const int RecentDeliveries = 50;

    public static RouteGroupBuilder MapAdminWebhooks(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin/webhooks").RequireAuthorization(FeatlyAuthenticationDefaults.AdminPolicy);

        admin.MapGet("/", ListAsync).WithName("Featly.Admin.Webhooks.List").RequirePermission(Permission.WebhookRead);
        admin.MapGet("/{id:guid}", GetAsync).WithName("Featly.Admin.Webhooks.Get").RequirePermission(Permission.WebhookRead);
        admin.MapGet("/{id:guid}/deliveries", DeliveriesAsync).WithName("Featly.Admin.Webhooks.Deliveries").RequirePermission(Permission.WebhookRead);
        admin.MapPost("/", CreateAsync).WithName("Featly.Admin.Webhooks.Create").RequirePermission(Permission.WebhookCreate);
        admin.MapPut("/{id:guid}", UpdateAsync).WithName("Featly.Admin.Webhooks.Update").RequirePermission(Permission.WebhookUpdate);
        admin.MapDelete("/{id:guid}", DeleteAsync).WithName("Featly.Admin.Webhooks.Delete").RequirePermission(Permission.WebhookDelete);
        admin.MapPost("/{id:guid}/test", TestAsync).WithName("Featly.Admin.Webhooks.Test").RequirePermission(Permission.WebhookUpdate);
        admin.MapPost("/{id:guid}/deliveries/{deliveryId:guid}/resend", ResendAsync).WithName("Featly.Admin.Webhooks.Resend").RequirePermission(Permission.WebhookUpdate);

        return group;
    }

    private static async Task<IResult> ListAsync(StorageFacade store, CancellationToken ct)
    {
        var endpoints = await store.Webhooks.ListAsync(ct).ConfigureAwait(false);
        return Results.Ok(endpoints.Select(ToResponse).ToList());
    }

    private static async Task<IResult> GetAsync(Guid id, StorageFacade store, CancellationToken ct)
    {
        var endpoint = await store.Webhooks.GetByIdAsync(id, ct).ConfigureAwait(false);
        return endpoint is null ? Problems.NotFound($"Webhook '{id}' not found.") : Results.Ok(ToResponse(endpoint));
    }

    private static async Task<IResult> DeliveriesAsync(Guid id, StorageFacade store, CancellationToken ct)
    {
        var endpoint = await store.Webhooks.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (endpoint is null)
        {
            return Problems.NotFound($"Webhook '{id}' not found.");
        }

        var deliveries = await store.WebhookDeliveries.ListByEndpointAsync(id, RecentDeliveries, ct).ConfigureAwait(false);
        return Results.Ok(deliveries);
    }

    private static async Task<IResult> CreateAsync(WebhookWriteRequest body, StorageFacade store, IOptions<WebhookOptions> options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrWhiteSpace(body.Name) || string.IsNullOrWhiteSpace(body.Url))
        {
            return Problems.BadRequest("name and url are required.");
        }
        if (!Uri.TryCreate(body.Url, UriKind.Absolute, out var createUri))
        {
            return Problems.Validation("url", "url must be an absolute URL.");
        }
        if (BlockedTarget(createUri, options.Value) is { } createBlock)
        {
            return createBlock;
        }

        var environmentId = await ResolveEnvironmentIdAsync(store, body.EnvironmentKey, ct).ConfigureAwait(false);
        if (body.EnvironmentKey is not null && environmentId is null)
        {
            return Problems.NotFound($"Environment '{body.EnvironmentKey}' not found.");
        }

        var now = DateTimeOffset.UtcNow;
        var endpoint = new WebhookEndpoint
        {
            Id = Guid.NewGuid(),
            Name = body.Name,
            Url = body.Url,
            // Auto-generate a signing secret when the caller doesn't supply one.
            Secret = string.IsNullOrWhiteSpace(body.Secret) ? GenerateSecret() : body.Secret,
            Enabled = body.Enabled ?? true,
            EventTypes = [.. (body.EventTypes ?? [])],
            EnvironmentId = environmentId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await store.Webhooks.UpsertAsync(endpoint, ct).ConfigureAwait(false);
        // The signing secret is returned exactly once, at creation — like a
        // minted API key. Read paths (list/get/update) never echo it again.
        return Results.Created($"/api/admin/webhooks/{endpoint.Id}", ToCreatedResponse(endpoint));
    }

    private static async Task<IResult> UpdateAsync(Guid id, WebhookWriteRequest body, StorageFacade store, IOptions<WebhookOptions> options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        var endpoint = await store.Webhooks.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (endpoint is null)
        {
            return Problems.NotFound($"Webhook '{id}' not found.");
        }
        if (!string.IsNullOrWhiteSpace(body.Url))
        {
            if (!Uri.TryCreate(body.Url, UriKind.Absolute, out var updateUri))
            {
                return Problems.Validation("url", "url must be an absolute URL.");
            }
            if (BlockedTarget(updateUri, options.Value) is { } updateBlock)
            {
                return updateBlock;
            }
        }

        var environmentId = await ResolveEnvironmentIdAsync(store, body.EnvironmentKey, ct).ConfigureAwait(false);
        if (body.EnvironmentKey is not null && environmentId is null)
        {
            return Problems.NotFound($"Environment '{body.EnvironmentKey}' not found.");
        }

        if (!string.IsNullOrWhiteSpace(body.Name))
        {
            endpoint.Name = body.Name;
        }
        if (!string.IsNullOrWhiteSpace(body.Url))
        {
            endpoint.Url = body.Url;
        }
        if (!string.IsNullOrWhiteSpace(body.Secret))
        {
            endpoint.Secret = body.Secret;
        }
        if (body.Enabled is { } enabled)
        {
            endpoint.Enabled = enabled;
        }
        if (body.EventTypes is not null)
        {
            endpoint.EventTypes = [.. body.EventTypes];
        }
        endpoint.EnvironmentId = body.EnvironmentKey is null ? endpoint.EnvironmentId : environmentId;

        await store.Webhooks.UpsertAsync(endpoint, ct).ConfigureAwait(false);
        return Results.Ok(ToResponse(endpoint));
    }

    private static async Task<IResult> DeleteAsync(Guid id, StorageFacade store, CancellationToken ct)
    {
        await store.Webhooks.DeleteAsync(id, ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> TestAsync(Guid id, StorageFacade store, CancellationToken ct)
    {
        var endpoint = await store.Webhooks.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (endpoint is null)
        {
            return Problems.NotFound($"Webhook '{id}' not found.");
        }

        var now = DateTimeOffset.UtcNow;
        var probe = new FeatlyDomainEvent
        {
            Type = "webhook.test",
            EntityType = "Webhook",
            EntityKey = endpoint.Id.ToString(),
            EnvironmentId = endpoint.EnvironmentId,
            ActorIdentifier = "webhook-test",
            At = now,
        };
        var delivery = new WebhookDelivery
        {
            Id = Guid.NewGuid(),
            WebhookEndpointId = endpoint.Id,
            EventType = probe.Type,
            Payload = WebhookDispatcher.BuildPayload(probe),
            Status = WebhookDeliveryStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await store.WebhookDeliveries.EnqueueAsync([delivery], ct).ConfigureAwait(false);
        return Results.Accepted(value: new { deliveryId = delivery.Id });
    }

    // Re-enqueues a fresh delivery cloned from an existing one (same event type and
    // payload). The original row is left untouched as an audit record.
    private static async Task<IResult> ResendAsync(Guid id, Guid deliveryId, StorageFacade store, CancellationToken ct)
    {
        var endpoint = await store.Webhooks.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (endpoint is null)
        {
            return Problems.NotFound($"Webhook '{id}' not found.");
        }

        var original = await store.WebhookDeliveries.GetByIdAsync(deliveryId, ct).ConfigureAwait(false);
        if (original is null || original.WebhookEndpointId != id)
        {
            return Problems.NotFound($"Delivery '{deliveryId}' not found for webhook '{id}'.");
        }

        var now = DateTimeOffset.UtcNow;
        var resend = new WebhookDelivery
        {
            Id = Guid.NewGuid(),
            WebhookEndpointId = endpoint.Id,
            EventType = original.EventType,
            Payload = original.Payload,
            Status = WebhookDeliveryStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await store.WebhookDeliveries.EnqueueAsync([resend], ct).ConfigureAwait(false);
        return Results.Accepted(value: new { deliveryId = resend.Id });
    }

    private static async Task<Guid?> ResolveEnvironmentIdAsync(StorageFacade store, string? envKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(envKey))
        {
            return null;
        }
        var project = await store.Projects.GetDefaultAsync(ct).ConfigureAwait(false);
        if (project is null)
        {
            return null;
        }
        var environment = await store.Environments.GetByKeyAsync(project.Id, envKey, ct).ConfigureAwait(false);
        return environment?.Id;
    }

    /// <summary>
    /// Returns a <c>400</c> result when the URL points at a blocked (internal)
    /// target and the operator hasn't opted into private targets; <c>null</c>
    /// when the target is acceptable. Delivery re-checks with DNS (issue #189).
    /// </summary>
    private static IResult? BlockedTarget(Uri uri, WebhookOptions options)
    {
        if (options.AllowPrivateNetworkTargets || WebhookTargetGuard.IsAllowedAtWrite(uri))
        {
            return null;
        }
        return Results.BadRequest(new
        {
            error = "url must be a public http(s) endpoint; loopback, private, and link-local targets are blocked (set Featly:Webhooks:AllowPrivateNetworkTargets to allow internal receivers).",
        });
    }

    private static string GenerateSecret()
        => "whsec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>Metadata-only projection: the signing secret is never echoed on read paths.</summary>
    private static WebhookResponse ToResponse(WebhookEndpoint e) => new(
        Id: e.Id,
        Name: e.Name,
        Url: e.Url,
        Enabled: e.Enabled,
        EventTypes: e.EventTypes,
        EnvironmentId: e.EnvironmentId,
        HasSecret: !string.IsNullOrEmpty(e.Secret),
        CreatedAt: e.CreatedAt,
        UpdatedAt: e.UpdatedAt);

    /// <summary>Creation projection: carries the signing secret once so the operator can configure the receiver.</summary>
    private static WebhookCreatedResponse ToCreatedResponse(WebhookEndpoint e) => new(
        Id: e.Id,
        Name: e.Name,
        Url: e.Url,
        Enabled: e.Enabled,
        EventTypes: e.EventTypes,
        EnvironmentId: e.EnvironmentId,
        HasSecret: !string.IsNullOrEmpty(e.Secret),
        CreatedAt: e.CreatedAt,
        UpdatedAt: e.UpdatedAt,
        Secret: e.Secret);
}

/// <summary>
/// Metadata view of a <see cref="WebhookEndpoint"/> returned by read paths. The
/// HMAC signing secret is intentionally omitted; <see cref="HasSecret"/> reports
/// only whether one is configured.
/// </summary>
public sealed record WebhookResponse(
    Guid Id,
    string Name,
    string Url,
    bool Enabled,
    IReadOnlyList<string> EventTypes,
    Guid? EnvironmentId,
    bool HasSecret,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Creation response: same metadata as <see cref="WebhookResponse"/> plus the
/// signing <see cref="Secret"/>, returned exactly once at creation.
/// </summary>
public sealed record WebhookCreatedResponse(
    Guid Id,
    string Name,
    string Url,
    bool Enabled,
    IReadOnlyList<string> EventTypes,
    Guid? EnvironmentId,
    bool HasSecret,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Secret);

/// <summary>
/// Inbound shape for creating/updating a webhook endpoint. On update, null
/// fields are left unchanged; an empty <c>secret</c> on create auto-generates one.
/// </summary>
public sealed record WebhookWriteRequest(
    string? Name = null,
    string? Url = null,
    string? Secret = null,
    bool? Enabled = null,
    IReadOnlyList<string>? EventTypes = null,
    string? EnvironmentKey = null);
