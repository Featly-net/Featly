using System.Net.Http.Json;
using System.Text.Json;

namespace Featly.Cli.Infrastructure;

/// <summary>
/// Thin HTTP client over the Featly server's admin API — the online half of the
/// hybrid CLI. Wraps the endpoints added in M12 PR 12B (API key minting, the
/// first-admin bootstrap) plus the environment lock/unlock from the M10 polish.
/// Takes a pre-configured <see cref="HttpClient"/> (base address + bearer) so it
/// can be exercised against a stub handler in tests.
/// </summary>
internal sealed class AdminApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Mints an API key, optionally bound to a user. Returns the one-time token.</summary>
    public Task<MintedKey> MintApiKeyAsync(
        string name,
        string? scope,
        string? userIdentifier,
        string? environmentKey,
        CancellationToken ct)
    {
        var body = new
        {
            name,
            scope,
            userIdentifier,
            environmentKey,
        };
        return PostAsync<MintedKey>("/api/admin/apikeys", body, ct);
    }

    /// <summary>Provisions the first admin (only valid while the server has no users).</summary>
    public Task<BootstrappedAdmin> BootstrapAsync(string identifier, string? displayName, CancellationToken ct)
    {
        var body = new { identifier, displayName };
        return PostAsync<BootstrappedAdmin>("/api/admin/bootstrap", body, ct);
    }

    /// <summary>Locks or unlocks an environment (toggles its ReadOnly freeze).</summary>
    public Task SetEnvironmentReadOnlyAsync(string environmentKey, bool readOnly, CancellationToken ct)
    {
        var verb = readOnly ? "lock" : "unlock";
        return PostNoContentAsync($"/api/admin/environments/{Uri.EscapeDataString(environmentKey)}/{verb}", ct);
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(path, body, JsonOptions, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("The server returned an empty response.");
    }

    private async Task PostNoContentAsync(string path, CancellationToken ct)
    {
        using var response = await http.PostAsync(new Uri(path, UriKind.Relative), content: null, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await ReadErrorAsync(response, ct).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        throw new InvalidOperationException(
            detail is null
                ? $"the server returned HTTP {status} ({response.ReasonPhrase})."
                : $"the server returned HTTP {status}: {detail}");
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            // The server reports failures as { "error": "..." } or a problem document.
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString();
                }
                if (doc.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                {
                    return detail.GetString();
                }
                if (doc.RootElement.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                {
                    return title.GetString();
                }
            }
            return raw;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>A freshly minted API key. <see cref="Token"/> is the one-time plaintext.</summary>
internal sealed record MintedKey(Guid Id, string Name, string Prefix, string Scope, Guid? UserId, string Token);

/// <summary>The bootstrapped first admin. <see cref="Token"/> is the one-time admin key.</summary>
internal sealed record BootstrappedAdmin(string Identifier, Guid UserId, Guid ApiKeyId, string Token);
