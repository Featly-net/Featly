using System.Net;
using Featly.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Featly.AspNetCore.Authorization;

/// <summary>
/// Authorization filter that accepts any request whose remote address is the
/// loopback interface (<c>127.0.0.1</c> or <c>::1</c>) and assigns it to a
/// configured bootstrap identifier. Useful for embedded quickstart scenarios
/// where the operator runs the dashboard side-by-side with the host process
/// and does not want to wire a real auth scheme.
/// </summary>
/// <remarks>
/// <para>
/// Production deployments must NOT enable this filter — if the host listens
/// on an external interface a reverse proxy can spoof the remote address.
/// The DI extension that registers it logs a warning at startup when the
/// process is bound to anything other than loopback.
/// </para>
/// <para>
/// Configuration binds from <c>Featly:Auth:Loopback</c>; the only knob is the
/// identifier the filter returns when the request matches.
/// </para>
/// </remarks>
public sealed class FeatlyLoopbackAuthFilter(IOptions<FeatlyLoopbackAuthOptions> options) : IFeatlyDashboardAuthorizationFilter
{
    private readonly FeatlyLoopbackAuthOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public Task<ResolvedUser?> AuthorizeAsync(HttpContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        var remote = context.Connection.RemoteIpAddress;
        if (remote is null || !IPAddress.IsLoopback(remote))
        {
            return Task.FromResult<ResolvedUser?>(null);
        }

        var identifier = string.IsNullOrEmpty(_options.Identifier) ? "loopback" : _options.Identifier;
        var display = string.IsNullOrEmpty(_options.DisplayName) ? identifier : _options.DisplayName;
        return Task.FromResult<ResolvedUser?>(new ResolvedUser(identifier, display));
    }
}

/// <summary>Options for <see cref="FeatlyLoopbackAuthFilter"/>. Binds from <c>Featly:Auth:Loopback</c>.</summary>
public sealed class FeatlyLoopbackAuthOptions
{
    /// <summary>Configuration section name (<c>Featly:Auth:Loopback</c>).</summary>
    public const string SectionName = "Featly:Auth:Loopback";

    /// <summary>Identifier the resolved user carries. Defaults to <c>loopback</c>.</summary>
    public string Identifier { get; set; } = "loopback";

    /// <summary>Friendly name. Defaults to <see cref="Identifier"/> when empty.</summary>
    public string DisplayName { get; set; } = "";
}
