using System.Net;
using AwesomeAssertions;
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
    public async Task Dashboard_root_serves_the_shell()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri("/featly", UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        body.Should().Contain("Featly").And.Contain("app.js");
        // The mount-path placeholder must be substituted before the response goes out.
        body.Should().NotContain("__MOUNT_PATH__");
    }

    [Fact]
    public async Task Dashboard_deep_link_falls_back_to_the_shell()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri("/featly/flags", UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        body.Should().Contain("app.js");
    }

    [Fact]
    public async Task Dashboard_serves_embedded_css_and_js()
    {
        var client = _factory.CreateClient();

        var css = await client.GetAsync(new Uri("/featly/app.css", UriKind.Relative), TestContext.Current.CancellationToken);
        css.StatusCode.Should().Be(HttpStatusCode.OK);
        css.Content.Headers.ContentType?.MediaType.Should().Be("text/css");

        var js = await client.GetAsync(new Uri("/featly/app.js", UriKind.Relative), TestContext.Current.CancellationToken);
        js.StatusCode.Should().Be(HttpStatusCode.OK);
        js.Content.Headers.ContentType?.MediaType.Should().Be("text/javascript");
    }

    [Theory]
    [InlineData("/featly")]
    [InlineData("/featly/flags")]
    [InlineData("/featly/app.js")]
    public async Task Dashboard_responses_carry_security_headers(string path)
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        response.Headers.TryGetValues("Content-Security-Policy", out var csp).Should().BeTrue();
        csp!.Single().Should().Contain("frame-ancestors 'none'").And.Contain("script-src 'self'");
        response.Headers.TryGetValues("X-Content-Type-Options", out var nosniff).Should().BeTrue();
        nosniff!.Single().Should().Be("nosniff");
        response.Headers.TryGetValues("X-Frame-Options", out var frame).Should().BeTrue();
        frame!.Single().Should().Be("DENY");
        response.Headers.TryGetValues("Referrer-Policy", out var referrer).Should().BeTrue();
        referrer!.Single().Should().Be("no-referrer");
    }
}
