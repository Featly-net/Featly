using System.Net;
using AwesomeAssertions;
using Featly.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Featly.Server.Tests;

/// <summary>
/// Version negotiation on the HTTP API (issue #227): a client pins a major with
/// <c>Accept-Version</c>, every response echoes the served version, an
/// unsupported pin is refused with 406, and a deprecated major is announced with
/// <c>Deprecation</c> + <c>Sunset</c>.
/// </summary>
public class ApiVersionNegotiationTests
{
    [Theory]
    [InlineData("1", "1")]      // bare major
    [InlineData("1.4", "1")]    // minor is ignored — the pin is the major
    [InlineData("v1", "1")]     // tolerated "v" prefix
    [InlineData("", "1")]       // no pin -> current
    public async Task Supported_pin_is_served_and_echoed(string pin, string expected)
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var client = host.AdminClient();
        if (pin.Length > 0)
        {
            client.DefaultRequestHeaders.Add(FeatlyApiVersion.RequestHeader, pin);
        }

        var response = await client.GetAsync(new Uri("/api/admin/environments", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues(FeatlyApiVersion.ResponseHeader).Should().ContainSingle().Which.Should().Be(expected);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("0")]
    [InlineData("banana")]
    public async Task Unsupported_pin_is_refused_with_406(string pin)
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var client = host.AdminClient();
        client.DefaultRequestHeaders.Add(FeatlyApiVersion.RequestHeader, pin);

        var response = await client.GetAsync(new Uri("/api/admin/environments", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotAcceptable);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain(pin);
    }

    [Fact]
    public async Task Deprecated_version_still_serves_but_announces_its_sunset()
    {
        // No major is deprecated yet, so drive the filter directly with a policy
        // that retires the current one — this is the path that starts running the
        // day a v2 lands and v1 is scheduled for removal.
        var sunset = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var filter = new FeatlyApiVersionFilter(new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal)
        {
            [FeatlyApiVersion.Current] = sunset,
        });

        var http = new DefaultHttpContext();
        var context = EndpointFilterInvocationContext.Create(http);
        var reached = false;

        var result = await filter.InvokeAsync(context, _ =>
        {
            reached = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        reached.Should().BeTrue("a deprecated version is still served");
        result.Should().NotBeNull();
        http.Response.Headers[FeatlyApiVersion.ResponseHeader].ToString().Should().Be(FeatlyApiVersion.Current);
        http.Response.Headers["Deprecation"].ToString().Should().Be("true");
        http.Response.Headers["Sunset"].ToString().Should().Be(sunset.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("1", "1")]
    [InlineData("2.7.3", "2")]
    [InlineData("V3", "3")]
    [InlineData("1.x", "1")]
    [InlineData("abc", null)]
    [InlineData("-1", null)]
    public void Major_reduces_a_pin_to_its_major_token(string? pin, string? expected)
    {
        FeatlyApiVersion.Major(pin).Should().Be(expected);
    }
}
