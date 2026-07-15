using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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
        DateTimeOffset? expiresAt,
        CancellationToken ct)
    {
        var body = new
        {
            name,
            scope,
            userIdentifier,
            environmentKey,
            expiresAt,
        };
        return PostAsync<MintedKey>("/api/admin/apikeys", body, ct);
    }

    /// <summary>
    /// Rotates an API key: mints a replacement (same name/scope/environment/user
    /// binding) and revokes the old key. Returns the replacement's one-time token.
    /// </summary>
    public Task<MintedKey> RotateApiKeyAsync(Guid id, DateTimeOffset? expiresAt, CancellationToken ct)
    {
        var body = new { expiresAt };
        return PostAsync<MintedKey>($"/api/admin/apikeys/{id}/rotate", body, ct);
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

    /// <summary>Lists every flag in an environment.</summary>
    public async Task<IReadOnlyList<FlagSummary>> ListFlagsAsync(string? environmentKey, CancellationToken ct)
    {
        var path = "/api/admin/flags" + EnvQuery(environmentKey);
        using var response = await http.GetAsync(new Uri(path, UriKind.Relative), ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        var flags = await response.Content.ReadFromJsonAsync<List<FlagSummary>>(JsonOptions, ct).ConfigureAwait(false);
        return flags ?? [];
    }

    /// <summary>
    /// Flips a flag's master switch. Reads the current definition and PUTs it
    /// back with only <c>enabled</c> changed, so every other field (variants,
    /// rules, tags) round-trips untouched — the CLI never needs its own model
    /// of the full flag shape.
    /// </summary>
    public async Task SetFlagEnabledAsync(string key, bool enabled, string? environmentKey, CancellationToken ct)
    {
        var envQuery = EnvQuery(environmentKey);
        var path = $"/api/admin/flags/{Uri.EscapeDataString(key)}{envQuery}";

        using var getResponse = await http.GetAsync(new Uri(path, UriKind.Relative), ct).ConfigureAwait(false);
        await EnsureSuccessAsync(getResponse, ct).ConfigureAwait(false);
        var raw = await getResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var flag = JsonNode.Parse(raw) as JsonObject
            ?? throw new InvalidOperationException("The server returned an unexpected flag shape.");
        flag["enabled"] = enabled;

        using var putContent = new StringContent(flag.ToJsonString(JsonOptions), Encoding.UTF8, "application/json");
        using var putResponse = await http.PutAsync(new Uri(path, UriKind.Relative), putContent, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(putResponse, ct).ConfigureAwait(false);
    }

    /// <summary>Exports an environment's definitions as a raw JSON bundle (returned verbatim).</summary>
    public async Task<string> ExportAsync(string? environmentKey, CancellationToken ct)
    {
        var path = "/api/admin/export" + EnvQuery(environmentKey);
        using var response = await http.GetAsync(new Uri(path, UriKind.Relative), ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Imports a JSON bundle into the target environment. Returns the per-kind counts.</summary>
    public async Task<ImportResult> ImportAsync(string? environmentKey, string bundleJson, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleJson);
        var path = "/api/admin/import" + EnvQuery(environmentKey);
        using var content = new StringContent(bundleJson, System.Text.Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(new Uri(path, UriKind.Relative), content, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<ImportResult>(JsonOptions, ct).ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("The server returned an empty import result.");
    }

    private static string EnvQuery(string? environmentKey) =>
        string.IsNullOrWhiteSpace(environmentKey) ? "" : $"?env={Uri.EscapeDataString(environmentKey)}";

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
                // RFC 7807 validation problem: surface the per-field messages.
                if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                {
                    var messages = errors.EnumerateObject()
                        .SelectMany(p => p.Value.ValueKind == JsonValueKind.Array
                            ? p.Value.EnumerateArray().Select(v => v.GetString())
                            : new[] { p.Value.GetString() })
                        .Where(m => !string.IsNullOrWhiteSpace(m));
                    var joined = string.Join(" ", messages);
                    if (!string.IsNullOrWhiteSpace(joined))
                    {
                        return joined;
                    }
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
internal sealed record MintedKey(Guid Id, string Name, string Prefix, string Scope, Guid? UserId, DateTimeOffset? ExpiresAt, string Token);

/// <summary>The bootstrapped first admin. <see cref="Token"/> is the one-time admin key.</summary>
internal sealed record BootstrappedAdmin(string Identifier, Guid UserId, Guid ApiKeyId, string Token);

/// <summary>Per-kind counts returned by an import.</summary>
internal sealed record ImportResult(string EnvironmentKey, int Flags, int Configs, int Segments);

/// <summary>Listing-shape view of a flag — enough for the CLI table, not the full definition.</summary>
internal sealed record FlagSummary(string Key, string Name, string Type, bool Enabled, IReadOnlyList<FlagVariantSummary>? Variants);

/// <summary>A flag variant's key, for the CLI's variant-count column.</summary>
internal sealed record FlagVariantSummary(string Key);
