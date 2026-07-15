using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
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
/// Admin CRUD for projects, consumed by the dashboard's Projects screen.
/// </summary>
public class AdminProjectsEndpointTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";

    [Fact]
    public async Task GET_admin_projects_rejects_unauthenticated_requests()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var client = host.GetTestClient();

        var response = await client.GetAsync(new Uri("/api/admin/projects", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_admin_projects_rejects_sdk_scope_key()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SdkKey);

        var response = await client.GetAsync(new Uri("/api/admin/projects", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GET_admin_projects_returns_the_bootstrap_default()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var client = AdminClient(host);

        var response = await client.GetAsync(new Uri("/api/admin/projects", UriKind.Relative), TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var projects = await response.Content.ReadFromJsonAsync<List<Project>>(TestJson.Options, cancellationToken: TestContext.Current.CancellationToken);
        projects.Should().NotBeNull();
        projects!.Should().ContainSingle(p => p.IsDefault);
    }

    [Fact]
    public async Task Create_then_get_round_trips_a_new_project()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var client = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        var created = await client.PostAsJsonAsync("/api/admin/projects", new
        {
            key = "mobile",
            name = "Mobile",
            description = "Mobile apps",
        }, ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await created.Content.ReadFromJsonAsync<Project>(TestJson.Options, cancellationToken: ct);
        body!.Key.Should().Be("mobile");
        body.Name.Should().Be("Mobile");
        body.IsDefault.Should().BeFalse();

        var fetched = await client.GetFromJsonAsync<Project>("/api/admin/projects/mobile", TestJson.Options, ct);
        fetched!.Name.Should().Be("Mobile");
        fetched.Description.Should().Be("Mobile apps");
    }

    [Fact]
    public async Task Create_with_duplicate_key_returns_Conflict()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var client = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        (await client.PostAsJsonAsync("/api/admin/projects", new { key = "dup", name = "First" }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        (await client.PostAsJsonAsync("/api/admin/projects", new { key = "dup", name = "Second" }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_changes_name_and_description()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var client = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        (await client.PostAsJsonAsync("/api/admin/projects", new { key = "web", name = "Web" }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var updated = await client.PutAsJsonAsync("/api/admin/projects/web", new
        {
            key = "web",
            name = "Web Platform",
            description = "Customer-facing web",
        }, ct);
        updated.StatusCode.Should().Be(HttpStatusCode.OK);

        var fetched = await client.GetFromJsonAsync<Project>("/api/admin/projects/web", TestJson.Options, ct);
        fetched!.Name.Should().Be("Web Platform");
        fetched.Description.Should().Be("Customer-facing web");
        fetched.Key.Should().Be("web");
    }

    [Fact]
    public async Task Update_unknown_project_returns_NotFound()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var client = AdminClient(host);

        var resp = await client.PutAsJsonAsync("/api/admin/projects/ghost", new { key = "ghost", name = "Ghost" }, TestContext.Current.CancellationToken);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static HttpClient AdminClient(IHost host)
    {
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        return client;
    }

}
