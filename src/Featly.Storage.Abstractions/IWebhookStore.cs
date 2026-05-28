namespace Featly.Storage;

/// <summary>
/// Persistence for registered <see cref="WebhookEndpoint"/> rows. Admin-managed;
/// the webhook dispatcher reads enabled endpoints to decide where to fan out.
/// </summary>
public interface IWebhookStore
{
    /// <summary>Returns the endpoint with the given id, or <c>null</c>.</summary>
    Task<WebhookEndpoint?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Lists every registered endpoint, newest first.</summary>
    Task<IReadOnlyList<WebhookEndpoint>> ListAsync(CancellationToken ct);

    /// <summary>Inserts or updates the endpoint matched by id.</summary>
    Task UpsertAsync(WebhookEndpoint endpoint, CancellationToken ct);

    /// <summary>Deletes an endpoint by id. Idempotent for a missing id.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct);
}
