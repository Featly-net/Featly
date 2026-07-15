using System.Security.Claims;
using System.Text.Json;
using Featly.Server.Approval;
using Microsoft.AspNetCore.Http;
using ChangeNotification = Featly.Storage.ChangeNotification;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// The steps every admin write handler repeats (issue #224): resolve the acting
/// identity, run the approval gate, and announce the change to the SDK stream.
/// The flag / config / segment / experiment create+update handlers shared these
/// verbatim; they now differ only in the entity-specific parts (which sub-store,
/// which event type, how the request maps onto the entity).
/// </summary>
/// <remarks>
/// Deliberately a handful of small, explicit helpers rather than a generic
/// <c>EntityWriteFlow&lt;TRequest, TEntity&gt;</c>: threading get/upsert/map/
/// validate through ~9 delegates would hide the flow behind an abstraction that
/// is harder to read than the code it replaces, against the project's
/// "predictable, not magical" principle. Each handler still reads top-to-bottom.
/// </remarks>
internal static class AdminWrite
{
    /// <summary>
    /// The identity to attribute the write to — the authenticated principal's
    /// name, or <c>"anonymous"</c> when the pipeline resolved no name.
    /// </summary>
    public static string ResolveActor(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var name = user.Identity?.Name;
        return string.IsNullOrEmpty(name) ? "anonymous" : name;
    }

    /// <summary>
    /// Publishes the in-process change notification that drives the SDK's SSE
    /// stream, so connected clients re-sync their snapshot.
    /// </summary>
    public static ValueTask NotifyAsync(StorageFacade store, Guid environmentId, string entityType, string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.Changes.NotifyAsync(new ChangeNotification(environmentId, entityType, key, DateTimeOffset.UtcNow), ct);
    }

    /// <summary>
    /// Runs the approval gate over a proposed write. Returns the response to
    /// short-circuit with when the gate handled the request itself (a 202 pending
    /// change, an emergency bypass, or a dry run), or <c>null</c> when the caller
    /// should go ahead and apply the write directly.
    /// </summary>
    public static async Task<IResult?> GateOrNullAsync(
        ChangeGate gate,
        string entityType,
        string key,
        Environment environment,
        ChangeAction action,
        object body,
        ClaimsPrincipal user,
        bool dryRun,
        bool emergency,
        string? reason,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(gate);
        var gated = await gate.InterceptAsync(entityType, key, environment, action,
            JsonSerializer.SerializeToElement(body, ChangeJson.Options), user, dryRun, emergency, reason, ct).ConfigureAwait(false);
        return gated.Outcome == GateOutcome.Handled ? gated.Response! : null;
    }
}
