using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    private static readonly JsonSerializerOptions s_json = CreateJson();
    private static JsonSerializerOptions CreateJson()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

    public static RouteGroupBuilder MapAdminSettings(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin/settings").RequireAuthorization(FeatlyAuthenticationDefaults.AdminPolicy);

        admin.MapGet("/webhook", GetWebhookAsync).WithName("Featly.Admin.Settings.GetWebhook").RequirePermission(Permission.SettingsRead);
        admin.MapPut("/webhook", PutWebhookAsync).WithName("Featly.Admin.Settings.PutWebhook").RequirePermission(Permission.SettingsUpdate);
        admin.MapGet("/authorization", GetAuthorizationAsync).WithName("Featly.Admin.Settings.GetAuthorization").RequirePermission(Permission.SettingsRead);
        admin.MapPut("/authorization", PutAuthorizationAsync).WithName("Featly.Admin.Settings.PutAuthorization").RequirePermission(Permission.SettingsUpdate);
        admin.MapGet("/audit", GetAuditAsync).WithName("Featly.Admin.Settings.GetAudit").RequirePermission(Permission.SettingsRead);
        admin.MapPut("/audit", PutAuditAsync).WithName("Featly.Admin.Settings.PutAudit").RequirePermission(Permission.SettingsUpdate);
        admin.MapGet("/approval-defaults", GetApprovalDefaultsAsync).WithName("Featly.Admin.Settings.GetApprovalDefaults").RequirePermission(Permission.SettingsRead);
        admin.MapPut("/approval-defaults", PutApprovalDefaultsAsync).WithName("Featly.Admin.Settings.PutApprovalDefaults").RequirePermission(Permission.SettingsUpdate);

        return group;
    }

    private static IResult GetApprovalDefaultsAsync(IFeatlySettingsProvider provider)
        => Results.Ok(new SettingView<FeatlyApprovalDefaultsSettings>(provider.ApprovalDefaults, provider.ApprovalDefaultsSource.ToString()));

    private static async Task<IResult> PutApprovalDefaultsAsync(
        FeatlyApprovalDefaultsSettings body,
        IFeatlySettingsProvider provider,
        StorageFacade store,
        IFeatlyEventPublisher events,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        if ((body.Prod ?? new()).MinApprovals < 1 || (body.NonProd ?? new()).MinApprovals < 1)
        {
            return Results.BadRequest(new { error = "minApprovals must be at least 1 for each template." });
        }

        await PersistAsync(FeatlySettingsKeys.ApprovalDefaults, body, provider, store, events, principal, ct).ConfigureAwait(false);
        return Results.Ok(new SettingView<FeatlyApprovalDefaultsSettings>(provider.ApprovalDefaults, provider.ApprovalDefaultsSource.ToString()));
    }

    private static IResult GetAuditAsync(IFeatlySettingsProvider provider)
        => Results.Ok(new SettingView<FeatlyAuditSettings>(provider.Audit, provider.AuditSource.ToString()));

    private static async Task<IResult> PutAuditAsync(
        FeatlyAuditSettings body,
        IFeatlySettingsProvider provider,
        StorageFacade store,
        IFeatlyEventPublisher events,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.RetentionDays < 0)
        {
            return Results.BadRequest(new { error = "retentionDays must be 0 (keep forever) or a positive number of days." });
        }

        await PersistAsync(FeatlySettingsKeys.Audit, body, provider, store, events, principal, ct).ConfigureAwait(false);
        return Results.Ok(new SettingView<FeatlyAuditSettings>(provider.Audit, provider.AuditSource.ToString()));
    }

    private static IResult GetAuthorizationAsync(IFeatlySettingsProvider provider)
        => Results.Ok(new SettingView<FeatlyAuthorizationSettings>(provider.Authorization, provider.AuthorizationSource.ToString()));

    private static async Task<IResult> PutAuthorizationAsync(
        FeatlyAuthorizationSettings body,
        IFeatlySettingsProvider provider,
        StorageFacade store,
        IFeatlyEventPublisher events,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!Enum.IsDefined(body.AutoProvisionMode))
        {
            return Results.BadRequest(new { error = "autoProvisionMode must be 'Open' or 'Closed'." });
        }

        await PersistAsync(FeatlySettingsKeys.Authorization, body, provider, store, events, principal, ct).ConfigureAwait(false);
        return Results.Ok(new SettingView<FeatlyAuthorizationSettings>(provider.Authorization, provider.AuthorizationSource.ToString()));
    }

    // Persist a settings singleton, refresh this instance immediately, then audit
    // and notify so other instances reload (ARCHITECTURE.md §15).
    private static async Task PersistAsync<T>(
        string key, T body, IFeatlySettingsProvider provider, StorageFacade store,
        IFeatlyEventPublisher events, ClaimsPrincipal principal, CancellationToken ct)
    {
        await store.Settings.UpsertAsync(new SystemSetting
        {
            Key = key,
            Payload = JsonSerializer.SerializeToElement(body, s_json),
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = principal.Identity?.Name,
        }, ct).ConfigureAwait(false);

        await provider.ReloadAsync(ct).ConfigureAwait(false);
        await events.PublishAsync(FeatlyEventTypes.SettingChanged, FeatlySettingsKeys.ChangeEntityType, key, null, principal, body, ct).ConfigureAwait(false);
        await store.Changes.NotifyAsync(
            new ChangeNotification(null, FeatlySettingsKeys.ChangeEntityType, key, DateTimeOffset.UtcNow),
            ct).ConfigureAwait(false);
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

        await PersistAsync(FeatlySettingsKeys.Webhook, body, provider, store, events, principal, ct).ConfigureAwait(false);
        return Results.Ok(new SettingView<FeatlyWebhookSettings>(provider.Webhook, provider.WebhookSource.ToString()));
    }
}

/// <summary>The effective value of a settings aggregate plus its precedence source.</summary>
public sealed record SettingView<T>(T Value, string Source);
