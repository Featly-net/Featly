using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Featly.Server;
using Featly.Server.Authentication;
using Featly.Server.Endpoints;
using Featly.Storage.InMemory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Tests;

/// <summary>
/// Cookie-based dashboard session: <c>POST /api/auth/login</c>,
/// <c>POST /api/auth/logout</c>, <c>GET /api/auth/me</c>. Login accepts the
/// legacy <c>AdminApiKey</c> from configuration plus real <see cref="ApiKey"/>
/// rows with the <see cref="ApiKeyScope.AdminWrite"/> scope. The SDK key is
/// rejected for dashboard use even though it's a valid bearer for the SDK
/// endpoints.
/// </summary>
public class AuthEndpointsTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";

    [Fact]
    public async Task Login_with_legacy_admin_key_returns_identity_and_sets_cookie()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new LoginRequest(AdminKey),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MeResponse>(
            TestJson.Options, cancellationToken: TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.Identifier.Should().Be("api-key:AdminWrite");
        body.DisplayName.Should().Be("Admin (legacy key)");

        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        cookies!.Should().ContainMatch("featly.session=*");
    }

    [Fact]
    public async Task Login_with_unknown_key_returns_401()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new LoginRequest("nope"),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_with_sdk_key_is_rejected()
    {
        // SDK keys give SDK clients read access to /api/sdk/*. They are not
        // dashboard users — the dashboard wants write access, audit logs, and
        // a real human identity. Refuse to mint a session for them.
        using var host = await FeatlyTestHost.CreateAsync();
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new LoginRequest(SdkKey),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_with_missing_apiKey_returns_400()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new LoginRequest(""),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_with_new_admin_api_key_returns_identity_and_sets_cookie()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var hasher = host.Services.GetRequiredService<ApiKeyHasher>();

        var plaintext = hasher.GenerateToken();
        var prefix = ApiKeyHasher.ExtractPrefix(plaintext);
        var hash = hasher.Hash(plaintext);
        var project = await store.Projects.GetDefaultAsync(TestContext.Current.CancellationToken);
        var env = await store.Environments.GetDefaultAsync(project!.Id, TestContext.Current.CancellationToken);

        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = "ci-bot",
            Prefix = prefix,
            Hash = hash,
            Scope = ApiKeyScope.AdminWrite,
            EnvironmentId = env!.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "test",
        };
        await store.ApiKeys.CreateAsync(apiKey, TestContext.Current.CancellationToken);

        using var client = host.GetTestClient();
        var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new LoginRequest(plaintext),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MeResponse>(
            TestJson.Options, cancellationToken: TestContext.Current.CancellationToken);
        body!.Identifier.Should().Be("ci-bot");
        body.DisplayName.Should().Be("ci-bot");
    }

    [Fact]
    public async Task Login_with_expired_api_key_is_rejected()
    {
        // Expiry is enforced on the Bearer path since the expiry feature; the
        // dashboard login must refuse the same key — otherwise an expired key
        // could still open a 7-day sliding cookie session.
        using var host = await FeatlyTestHost.CreateAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var hasher = host.Services.GetRequiredService<ApiKeyHasher>();

        var plaintext = hasher.GenerateToken();
        var project = await store.Projects.GetDefaultAsync(TestContext.Current.CancellationToken);
        var env = await store.Environments.GetDefaultAsync(project!.Id, TestContext.Current.CancellationToken);

        await store.ApiKeys.CreateAsync(new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = "expired-admin",
            Prefix = ApiKeyHasher.ExtractPrefix(plaintext),
            Hash = hasher.Hash(plaintext),
            Scope = ApiKeyScope.AdminWrite,
            EnvironmentId = env!.Id,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            CreatedBy = "test",
        }, TestContext.Current.CancellationToken);

        using var client = host.GetTestClient();
        var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new LoginRequest(plaintext),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_with_sdk_scope_api_key_is_rejected()
    {
        // Same reasoning as the legacy SDK key case: SdkRead scope is not a
        // dashboard user, even when it lives in the ApiKey store.
        using var host = await FeatlyTestHost.CreateAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var hasher = host.Services.GetRequiredService<ApiKeyHasher>();

        var plaintext = hasher.GenerateToken();
        var hash = hasher.Hash(plaintext);
        var project = await store.Projects.GetDefaultAsync(TestContext.Current.CancellationToken);
        var env = await store.Environments.GetDefaultAsync(project!.Id, TestContext.Current.CancellationToken);

        await store.ApiKeys.CreateAsync(new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = "sdk-bot",
            Prefix = ApiKeyHasher.ExtractPrefix(plaintext),
            Hash = hash,
            Scope = ApiKeyScope.SdkRead,
            EnvironmentId = env!.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "test",
        }, TestContext.Current.CancellationToken);

        using var client = host.GetTestClient();
        var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new LoginRequest(plaintext),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_without_cookie_returns_401()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        using var client = host.GetTestClient();

        var response = await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_after_login_returns_identity_with_cookie()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        using var client = host.GetTestClient();

        var login = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new LoginRequest(AdminKey),
            TestContext.Current.CancellationToken);
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var setCookie = login.Headers.GetValues("Set-Cookie").First();
        var cookieValue = setCookie.Split(';')[0]; // "featly.session=<value>"

        using var me = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/auth/me", UriKind.Relative));
        me.Headers.Add("Cookie", cookieValue);

        var response = await client.SendAsync(me, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<MeResponse>(
            TestJson.Options, cancellationToken: TestContext.Current.CancellationToken);
        body!.Identifier.Should().Be("api-key:AdminWrite");
    }

    [Fact]
    public async Task Logout_returns_204_and_clears_cookie()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        using var client = host.GetTestClient();

        var login = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new LoginRequest(AdminKey),
            TestContext.Current.CancellationToken);
        var sessionCookie = login.Headers.GetValues("Set-Cookie").First().Split(';')[0];

        using var logout = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/auth/logout", UriKind.Relative));
        logout.Headers.Add("Cookie", sessionCookie);

        var response = await client.SendAsync(logout, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // ASP.NET Core clears the cookie by re-issuing it with an expiry in
        // the past — Set-Cookie shows up with expires=Thu, 01 Jan 1970.
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        cookies!.Should().ContainMatch("*featly.session=*expires=*1970*");
    }

    [Fact]
    public async Task Admin_endpoint_accepts_cookie_session_alongside_bearer()
    {
        // The whole point of the cookie scheme is that the dashboard can hit
        // /api/admin/* with credentials: 'include' and no Authorization header.
        using var host = await FeatlyTestHost.CreateAsync();
        using var client = host.GetTestClient();

        var login = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new LoginRequest(AdminKey),
            TestContext.Current.CancellationToken);
        var sessionCookie = login.Headers.GetValues("Set-Cookie").First().Split(';')[0];

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/admin/environments", UriKind.Relative));
        request.Headers.Add("Cookie", sessionCookie);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Bearer_admin_key_still_works_on_admin_endpoints()
    {
        // Regression: adding the cookie scheme to the admin policy must not
        // break the existing Bearer flow that SDKs and scripts rely on.
        using var host = await FeatlyTestHost.CreateAsync();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        var response = await client.GetAsync(new Uri("/api/admin/environments", UriKind.Relative), TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

}
