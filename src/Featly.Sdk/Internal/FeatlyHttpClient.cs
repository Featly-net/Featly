using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Featly.Sdk.Internal;

/// <summary>
/// Thin HTTP layer used by <see cref="FeatlyConfigSyncService"/>. Handles
/// auth header, ETag negotiation, and JSON deserialization.
/// </summary>
internal sealed class FeatlyHttpClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: true) },
    };

    /// <summary>Result of a polled config fetch.</summary>
    /// <param name="Snapshot">The fresh snapshot when the server returned 200; <c>null</c> on 304.</param>
    /// <param name="Etag">The new ETag, or <c>null</c> when none was supplied.</param>
    /// <param name="NotModified"><c>true</c> when the server returned 304.</param>
    public sealed record FetchResult(ConfigSnapshot? Snapshot, string? Etag, bool NotModified);

    public async Task<FetchResult> FetchConfigAsync(
        string? environmentKey,
        string? ifNoneMatch,
        CancellationToken ct)
    {
        var path = string.IsNullOrEmpty(environmentKey)
            ? "/api/sdk/config"
            : $"/api/sdk/config?env={Uri.EscapeDataString(environmentKey)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrEmpty(ifNoneMatch))
        {
            request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(ifNoneMatch));
        }

        using var response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return new FetchResult(null, ifNoneMatch, true);
        }

        response.EnsureSuccessStatusCode();

        var etag = response.Headers.ETag?.Tag;
        var snapshot = await response.Content
            .ReadFromJsonAsync<ConfigSnapshot>(s_jsonOptions, ct)
            .ConfigureAwait(false);

        return new FetchResult(snapshot, etag, false);
    }

    /// <summary>
    /// Opens the SSE stream and returns the underlying response stream.
    /// Caller owns the lifetime.
    /// </summary>
    public async Task<HttpResponseMessage> OpenStreamAsync(string? environmentKey, CancellationToken ct)
    {
        var path = string.IsNullOrEmpty(environmentKey)
            ? "/api/sdk/stream"
            : $"/api/sdk/stream?env={Uri.EscapeDataString(environmentKey)}";

        // `using` so the request is disposed on the way out; HttpClient does
        // not own the message and SendAsync only needs it during the call.
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        return await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
    }
}
