using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Featly.Server;
using Featly.Storage.InMemory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Featly.Server.Tests;

public class AdminSegmentsEndpointTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";

    [Fact]
    public async Task POST_creates_a_segment_then_GET_returns_it()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        var create = await client.PostAsJsonAsync("/api/admin/segments", new
        {
            key = "enterprise",
            name = "Enterprise customers",
            description = "Paid tier",
            conditions = new[]
            {
                new
                {
                    attribute = "user.plan",
                    @operator = "Equals",
                    value = "enterprise",
                },
            },
        }, TestContext.Current.CancellationToken);

        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var get = await client.GetAsync(new Uri("/api/admin/segments/enterprise", UriKind.Relative), TestContext.Current.CancellationToken);
        get.StatusCode.Should().Be(HttpStatusCode.OK);

        var loaded = await get.Content.ReadFromJsonAsync<Segment>(TestJson.Options, cancellationToken: TestContext.Current.CancellationToken);
        loaded.Should().NotBeNull();
        loaded!.Key.Should().Be("enterprise");
        loaded.Conditions.Should().ContainSingle()
            .Which.Attribute.Should().Be("user.plan");
    }

    [Fact]
    public async Task PUT_overwrites_segment_conditions()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        await client.PostAsJsonAsync("/api/admin/segments", new
        {
            key = "beta",
            name = "Beta testers",
            conditions = new[]
            {
                new { attribute = "user.beta", @operator = "Equals", value = (object)true },
            },
        }, TestContext.Current.CancellationToken);

        // System.Text.Json walks elements via their runtime type, so a mixed
        // `object[]` carrying two distinct anonymous types serializes cleanly
        // (one condition's value is a string, the other's an int).
        var put = await client.PutAsJsonAsync("/api/admin/segments/beta", new
        {
            key = "beta",
            name = "Beta testers (renamed)",
            conditions = new object[]
            {
                new { attribute = "user.tier", @operator = "Equals", value = "beta" },
                new { attribute = "user.age", @operator = "GreaterThan", value = 21 },
            },
        }, TestContext.Current.CancellationToken);

        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await put.Content.ReadFromJsonAsync<Segment>(TestJson.Options, cancellationToken: TestContext.Current.CancellationToken);
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Beta testers (renamed)");
        updated.Conditions.Should().HaveCount(2);
    }

    [Fact]
    public async Task DELETE_removes_segment_then_GET_returns_404()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        await client.PostAsJsonAsync("/api/admin/segments", new
        {
            key = "to-delete",
            name = "X",
            conditions = Array.Empty<object>(),
        }, TestContext.Current.CancellationToken);

        var del = await client.DeleteAsync(new Uri("/api/admin/segments/to-delete", UriKind.Relative), TestContext.Current.CancellationToken);
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await client.GetAsync(new Uri("/api/admin/segments/to-delete", UriKind.Relative), TestContext.Current.CancellationToken);
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Archive_hides_segment_from_active_list_and_lists_it_under_archived()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        await client.PostAsJsonAsync("/api/admin/segments", new
        {
            key = "legacy",
            name = "Legacy",
            conditions = Array.Empty<object>(),
        }, TestContext.Current.CancellationToken);

        var archive = await client.PostAsync(new Uri("/api/admin/segments/legacy/archive", UriKind.Relative), null, TestContext.Current.CancellationToken);
        archive.StatusCode.Should().Be(HttpStatusCode.OK);
        var archived = await archive.Content.ReadFromJsonAsync<Segment>(TestJson.Options, cancellationToken: TestContext.Current.CancellationToken);
        archived!.Archived.Should().BeTrue();

        var active = await client.GetFromJsonAsync<List<Segment>>(new Uri("/api/admin/segments", UriKind.Relative), TestJson.Options, TestContext.Current.CancellationToken);
        active!.Should().NotContain(s => s.Key == "legacy");

        var listArchived = await client.GetFromJsonAsync<List<Segment>>(new Uri("/api/admin/segments?archived=true", UriKind.Relative), TestJson.Options, TestContext.Current.CancellationToken);
        listArchived!.Should().ContainSingle(s => s.Key == "legacy");
    }

    [Fact]
    public async Task Unarchive_restores_segment_to_active_list()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        await client.PostAsJsonAsync("/api/admin/segments", new
        {
            key = "legacy",
            name = "Legacy",
            conditions = Array.Empty<object>(),
        }, TestContext.Current.CancellationToken);
        await client.PostAsync(new Uri("/api/admin/segments/legacy/archive", UriKind.Relative), null, TestContext.Current.CancellationToken);

        var restore = await client.PostAsync(new Uri("/api/admin/segments/legacy/unarchive", UriKind.Relative), null, TestContext.Current.CancellationToken);
        restore.StatusCode.Should().Be(HttpStatusCode.OK);
        var restored = await restore.Content.ReadFromJsonAsync<Segment>(TestJson.Options, cancellationToken: TestContext.Current.CancellationToken);
        restored!.Archived.Should().BeFalse();

        var active = await client.GetFromJsonAsync<List<Segment>>(new Uri("/api/admin/segments", UriKind.Relative), TestJson.Options, TestContext.Current.CancellationToken);
        active!.Should().ContainSingle(s => s.Key == "legacy");
    }

    [Fact]
    public async Task Archive_returns_404_for_unknown_segment()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        var archive = await client.PostAsync(new Uri("/api/admin/segments/ghost/archive", UriKind.Relative), null, TestContext.Current.CancellationToken);
        archive.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_rejects_unauthenticated_requests()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/admin/segments", new
        {
            key = "x",
            name = "X",
            conditions = Array.Empty<object>(),
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_rejects_when_using_sdk_scope_key()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SdkKey);

        var response = await client.PostAsJsonAsync("/api/admin/segments", new
        {
            key = "x",
            name = "X",
            conditions = Array.Empty<object>(),
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

}
