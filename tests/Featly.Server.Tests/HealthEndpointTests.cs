using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Featly.Server.Tests;

public class HealthEndpointTests
{
    [Fact]
    public async Task HealthLive_returns_200_with_live_status()
    {
        // Liveness is anonymous, so the host needs no API keys.
        using var host = await FeatlyTestHost.CreateAsync(withStaticApiKeys: false);

        var client = host.GetTestClient();
        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("live");
    }
}
