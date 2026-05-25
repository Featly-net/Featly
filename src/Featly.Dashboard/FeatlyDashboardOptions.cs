namespace Featly.Dashboard;

/// <summary>
/// Configuration for the embedded Featly dashboard middleware.
/// </summary>
public sealed class FeatlyDashboardOptions
{
    /// <summary>
    /// URL prefix where the dashboard is served. Defaults to <c>/featly</c>.
    /// </summary>
    public string MountPath { get; set; } = "/featly";
}
