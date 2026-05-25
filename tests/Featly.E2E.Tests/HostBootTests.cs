using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Featly.E2E.Tests;

/// <summary>
/// Boots the SelfHosted sample and verifies the M1 contract: the host starts,
/// the dashboard placeholder is served, and the health endpoint responds.
/// </summary>
public class HostBootTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HostBootTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HealthLive_returns_200()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Dashboard_serves_coming_soon_placeholder()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri("/featly", UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        body.Should().Contain("Featly").And.Contain("coming soon");
    }
}
