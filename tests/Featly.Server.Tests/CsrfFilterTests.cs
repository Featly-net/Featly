using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Featly.Server.Endpoints;
using Featly.Storage.InMemory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Featly.Server.Tests;

/// <summary>
/// Synchronizer-token CSRF layer for the dashboard session (SECURITY_AUDIT.md
/// follow-up): login mints a per-session token (a claim inside the HttpOnly
/// cookie, echoed back in the login//me JSON); cookie-authenticated mutations
/// must present it in <c>X-Featly-Csrf</c>; Bearer requests are exempt.
/// </summary>
public class CsrfFilterTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";

    private static readonly Uri LoginUri = new("/api/auth/login", UriKind.Relative);
    private static readonly Uri FlagsUri = new("/api/admin/flags", UriKind.Relative);

    private static object NewFlag(string key) => new
    {
        key,
        name = "Flag " + key,
        type = "Boolean",
        enabled = true,
        defaultVariantKey = "off",
        variants = new[] { new { key = "off", name = "Off", value = false } },
    };

    [Fact]
    public async Task Login_and_me_return_the_same_session_token()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        var client = host.GetTestClient();

        var login = await client.PostAsJsonAsync(LoginUri, new LoginRequest(AdminKey), ct);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = await login.Content.ReadFromJsonAsync<MeResponse>(TestJson.Options, ct);
        me!.CsrfToken.Should().NotBeNullOrEmpty();

        var cookie = SessionCookie(login);
        using var probe = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/auth/me", UriKind.Relative));
        probe.Headers.Add("Cookie", cookie);
        var probed = await (await client.SendAsync(probe, ct)).Content.ReadFromJsonAsync<MeResponse>(TestJson.Options, ct);

        probed!.CsrfToken.Should().Be(me.CsrfToken);
    }

    [Fact]
    public async Task Cookie_mutation_without_the_header_is_forbidden()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        var client = host.GetTestClient();

        var login = await client.PostAsJsonAsync(LoginUri, new LoginRequest(AdminKey), ct);
        var cookie = SessionCookie(login);

        using var create = new HttpRequestMessage(HttpMethod.Post, FlagsUri) { Content = JsonContent.Create(NewFlag("csrf-no-header")) };
        create.Headers.Add("Cookie", cookie);

        var response = await client.SendAsync(create, ct);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(ct)).Should().Contain("X-Featly-Csrf");
    }

    [Fact]
    public async Task Cookie_mutation_with_the_header_succeeds_and_wrong_token_fails()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        var client = host.GetTestClient();

        var login = await client.PostAsJsonAsync(LoginUri, new LoginRequest(AdminKey), ct);
        var me = await login.Content.ReadFromJsonAsync<MeResponse>(TestJson.Options, ct);
        var cookie = SessionCookie(login);

        using var good = new HttpRequestMessage(HttpMethod.Post, FlagsUri) { Content = JsonContent.Create(NewFlag("csrf-good")) };
        good.Headers.Add("Cookie", cookie);
        good.Headers.Add("X-Featly-Csrf", me!.CsrfToken);
        (await client.SendAsync(good, ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        using var bad = new HttpRequestMessage(HttpMethod.Post, FlagsUri) { Content = JsonContent.Create(NewFlag("csrf-bad")) };
        bad.Headers.Add("Cookie", cookie);
        bad.Headers.Add("X-Featly-Csrf", "not-the-token");
        (await client.SendAsync(bad, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cookie_reads_do_not_need_the_header()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        var client = host.GetTestClient();

        var login = await client.PostAsJsonAsync(LoginUri, new LoginRequest(AdminKey), ct);
        var cookie = SessionCookie(login);

        using var read = new HttpRequestMessage(HttpMethod.Get, FlagsUri);
        read.Headers.Add("Cookie", cookie);
        (await client.SendAsync(read, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Bearer_mutations_are_exempt()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        var bearer = host.GetTestClient();
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        (await bearer.PostAsJsonAsync(FlagsUri, NewFlag("csrf-bearer"), ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Logout_still_works_without_the_header()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        var client = host.GetTestClient();

        var login = await client.PostAsJsonAsync(LoginUri, new LoginRequest(AdminKey), ct);
        var cookie = SessionCookie(login);

        using var logout = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/auth/logout", UriKind.Relative));
        logout.Headers.Add("Cookie", cookie);
        (await client.SendAsync(logout, ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private static string SessionCookie(HttpResponseMessage login)
        => login.Headers.GetValues("Set-Cookie").First().Split(';')[0];

}
