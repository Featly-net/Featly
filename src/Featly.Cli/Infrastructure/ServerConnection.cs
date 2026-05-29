using System.Net.Http.Headers;

namespace Featly.Cli.Infrastructure;

/// <summary>
/// Resolves how the online commands reach a running Featly server: the base URL
/// and the admin API key, each from an explicit option, then an environment
/// variable, then (for the URL) a localhost default.
/// </summary>
internal static class ServerConnection
{
    /// <summary>Environment variable for the server base URL.</summary>
    public const string ServerUrlEnv = "FEATLY_SERVER_URL";

    /// <summary>Environment variable for the admin API key.</summary>
    public const string ApiKeyEnv = "FEATLY_API_KEY";

    /// <summary>Base URL used when neither the option nor the environment variable is set.</summary>
    public const string DefaultServerUrl = "http://localhost:5080";

    /// <summary>Resolves the server base URL (option &gt; env &gt; localhost default).</summary>
    public static string ResolveServerUrl(string? option)
    {
        if (!string.IsNullOrWhiteSpace(option))
        {
            return option;
        }

        var env = System.Environment.GetEnvironmentVariable(ServerUrlEnv);
        return string.IsNullOrWhiteSpace(env) ? DefaultServerUrl : env;
    }

    /// <summary>Resolves the admin API key (option &gt; env), or <c>null</c> when neither is set.</summary>
    public static string? ResolveApiKey(string? option)
    {
        if (!string.IsNullOrWhiteSpace(option))
        {
            return option;
        }

        var env = System.Environment.GetEnvironmentVariable(ApiKeyEnv);
        return string.IsNullOrWhiteSpace(env) ? null : env;
    }

    /// <summary>
    /// Builds an <see cref="HttpClient"/> pointed at the server, optionally
    /// carrying a bearer credential. The caller owns disposal.
    /// </summary>
    public static HttpClient CreateClient(string serverUrl, string? apiKey)
    {
        var client = new HttpClient { BaseAddress = new Uri(serverUrl, UriKind.Absolute) };
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
        return client;
    }
}
