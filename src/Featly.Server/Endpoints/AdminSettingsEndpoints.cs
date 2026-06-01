using System.Security.Claims;
using System.Text.Json;
using Featly.Server.Authentication;
using Featly.Server.Events;
using Featly.Server.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ChangeNotification = Featly.Storage.ChangeNotification;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// DB-overridable settings management (ARCHITECTURE.md §15). Reads return the
/// effective value (after the hardcoded → appsettings → database precedence
/// merge) plus which layer supplied it; writes persist the database singleton,
/// audit the change, refresh the provider, and emit a change notification so
/// other instances reload.
/// </summary>
internal static class AdminSettingsEndpoints
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    public static RouteGroupBuilder MapAdminSettings(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin/settings").RequireAuthorization(FeatlyAuthenticationDefaults.AdminPolicy);

        admin.MapGet("/webhook", GetWebhookAsync).WithName("Featly.Admin.Settings.GetWebhook").RequirePermission(Permission.SettingsRead);
        admin.MapPut("/webhook", PutWebhookAsync).WithName("Featly.Admin.Settings.PutWebhook").RequirePermission(Permission.SettingsUpdate);

        return group;
    }

    private static IResult GetWebhookAsync(IFeatlySettingsProvider provider)
        => Results.Ok(new SettingView<FeatlyWebhookSettings>(provider.Webhook, provider.WebhookSource.ToString()));

    private static async Task<IResult> PutWebhookAsync(
        FeatlyWebhookSettings body,
        IFeatlySettingsProvider provider,
        StorageFacade store,
        IFeatlyEventPublisher events,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (body.MaxAttempts < 1)
        {
            return Results.BadRequest(new { error = "maxAttempts must be at least 1." });
        }
        if (body.BaseRetryDelaySeconds < 1)
        {
            return Results.BadRequest(new { error = "baseRetryDelaySeconds must be at least 1." });
        }
        if (body.MaxRetryDelaySeconds < body.BaseRetryDelaySeconds)
        {
            return Results.BadRequest(new { error = "maxRetryDelaySeconds must be greater than or equal to baseRetryDelaySeconds." });
        }

        await store.Settings.UpsertAsync(new SystemSetting
        {
            Key = FeatlySettingsKeys.Webhook,
            Payload = JsonSerializer.SerializeToElement(body, s_json),
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = principal.Identity?.Name,
        }, ct).ConfigureAwait(false);

        // Refresh this instance immediately so the response reflects the write,
        // then audit and notify so other instances reload (ARCHITECTURE.md §15).
        await provider.ReloadAsync(ct).ConfigureAwait(false);
        await events.PublishAsync(FeatlyEventTypes.SettingChanged, FeatlySettingsKeys.ChangeEntityType, FeatlySettingsKeys.Webhook, null, principal, body, ct).ConfigureAwait(false);
        await store.Changes.NotifyAsync(
            new ChangeNotification(null, FeatlySettingsKeys.ChangeEntityType, FeatlySettingsKeys.Webhook, DateTimeOffset.UtcNow),
            ct).ConfigureAwait(false);

        return Results.Ok(new SettingView<FeatlyWebhookSettings>(provider.Webhook, provider.WebhookSource.ToString()));
    }
}

/// <summary>The effective value of a settings aggregate plus its precedence source.</summary>
public sealed record SettingView<T>(T Value, string Source);
