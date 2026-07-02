namespace Featly.Server.RateLimiting;

/// <summary>
/// <c>appsettings.json</c> binding for request rate limiting
/// (<c>Featly:RateLimiting</c>). This is the middle precedence layer; the
/// database singleton (edited via <c>/api/admin/settings/rate-limit</c>)
/// overrides it per the "DB beats config" principle (ARCHITECTURE.md §15).
/// Defaults mirror <see cref="Settings.FeatlyRateLimitSettings"/>.
/// </summary>
public sealed class FeatlyRateLimitOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Featly:RateLimiting";

    /// <summary>Master switch. Off by default so embedded hosts opt in.</summary>
    public bool Enabled { get; set; }

    /// <summary>Requests per minute per client against <c>/api/auth/*</c>. 0 = unlimited.</summary>
    public int AuthPermitsPerMinute { get; set; } = 10;

    /// <summary>Requests per minute per client against <c>/api/admin/*</c>. 0 = unlimited.</summary>
    public int AdminPermitsPerMinute { get; set; } = 300;

    /// <summary>Requests per minute per client against <c>/api/sdk/*</c>. 0 = unlimited.</summary>
    public int SdkPermitsPerMinute { get; set; } = 1000;
}
