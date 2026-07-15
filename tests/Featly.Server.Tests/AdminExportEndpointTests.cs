using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Featly.Server;
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
/// Export / import of an environment's flag + config + segment definitions
/// (M12 PR 12D).
/// </summary>
public class AdminExportEndpointTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";

    private static readonly Uri ExportUri = new("/api/admin/export", UriKind.Relative);
    private static readonly Uri ImportUri = new("/api/admin/import", UriKind.Relative);

    [Fact]
    public async Task Export_rejects_sdk_scope()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var sdk = host.GetTestClient();
        sdk.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SdkKey);

        (await sdk.GetAsync(ExportUri, TestContext.Current.CancellationToken))
            .StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Import_invalidates_the_sdk_snapshot_etag()
    {
        // An import rewrites definitions, so SDK caches must revalidate (issue
        // #228). It used to write without announcing anything — clients only
        // noticed because the ETag was derived from max(UpdatedAt).
        using var host = await FeatlyTestHost.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        var admin = AdminClient(host);
        var sdk = host.SdkClient();

        // Seed a flag and take a real bundle, so the import body is definitely valid.
        (await admin.PostAsJsonAsync("/api/admin/flags", new
        {
            key = "imported",
            name = "Imported",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = new[] { new { key = "off", name = "Off", value = false } },
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        var bundleJson = await (await admin.GetAsync(ExportUri, ct)).Content.ReadAsStringAsync(ct);

        var before = await sdk.GetAsync(new Uri("/api/sdk/config", UriKind.Relative), ct);
        before.StatusCode.Should().Be(HttpStatusCode.OK);
        var beforeEtag = before.Headers.ETag!.Tag;

        using var content = new StringContent(bundleJson, System.Text.Encoding.UTF8, "application/json");
        (await admin.PostAsync(ImportUri, content, ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await sdk.GetAsync(new Uri("/api/sdk/config", UriKind.Relative), ct);
        after.Headers.ETag!.Tag.Should().NotBe(beforeEtag, "an import rewrites the snapshot");
    }

    [Fact]
    public async Task Export_then_import_round_trips_definitions()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        var admin = AdminClient(host);

        // Seed one of each definition.
        (await admin.PostAsJsonAsync("/api/admin/flags", new
        {
            key = "exp-flag",
            name = "Exp Flag",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = new[] { new { key = "off", name = "Off", value = false } },
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        (await admin.PostAsJsonAsync("/api/admin/configs", new
        {
            key = "exp-config",
            name = "Exp Config",
            type = "Int",
            defaultValue = 30,
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        (await admin.PostAsJsonAsync("/api/admin/segments", new
        {
            key = "exp-seg",
            name = "Exp Segment",
            conditions = new[] { new { attribute = "user.country", @operator = "Equals", value = "BR" } },
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        // Export returns the bundle.
        var exportResp = await admin.GetAsync(ExportUri, ct);
        exportResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var bundleJson = await exportResp.Content.ReadAsStringAsync(ct);
        bundleJson.Should().Contain("exp-flag").And.Contain("exp-config").And.Contain("exp-seg");

        // Import the same bundle back: upserts by key, returns counts.
        using var content = new StringContent(bundleJson, System.Text.Encoding.UTF8, "application/json");
        var importResp = await admin.PostAsync(ImportUri, content, ct);
        importResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await importResp.Content.ReadFromJsonAsync<ImportResult>(TestJson.Options, ct);
        result!.Flags.Should().Be(1);
        result.Configs.Should().Be(1);
        result.Segments.Should().Be(1);

        // The definitions are still present (overwrite-in-place, no duplicates).
        var flags = await admin.GetFromJsonAsync<List<Flag>>("/api/admin/flags", TestJson.Options, ct);
        flags!.Count(f => f.Key == "exp-flag").Should().Be(1);
    }

    [Fact]
    public async Task Import_creates_a_new_flag_from_a_minimal_bundle()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        var admin = AdminClient(host);

        var bundle = new
        {
            environmentKey = "development",
            exportedAt = "2026-05-29T00:00:00Z",
            flags = new[]
            {
                new
                {
                    key = "imported-flag",
                    name = "Imported",
                    // environmentId is a required member of the Flag entity; the
                    // import handler rebinds it onto the target environment.
                    environmentId = "00000000-0000-0000-0000-000000000000",
                    type = "Boolean",
                    enabled = true,
                    defaultVariantKey = "off",
                    variants = new[] { new { key = "off", name = "Off", value = false } },
                    rules = Array.Empty<object>(),
                    tags = Array.Empty<string>(),
                },
            },
            configs = Array.Empty<object>(),
            segments = Array.Empty<object>(),
        };

        var resp = await admin.PostAsJsonAsync(ImportUri, bundle, ct);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadFromJsonAsync<ImportResult>(TestJson.Options, ct))!.Flags.Should().Be(1);

        var flags = await admin.GetFromJsonAsync<List<Flag>>("/api/admin/flags", TestJson.Options, ct);
        flags!.Should().Contain(f => f.Key == "imported-flag");
    }

    [Fact]
    public async Task Import_is_rejected_when_the_environment_is_readonly()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        var store = host.Services.GetRequiredService<StorageFacade>();
        var admin = AdminClient(host);

        var project = await store.Projects.GetDefaultAsync(ct);
        var env = await store.Environments.GetDefaultAsync(project!.Id, ct);
        await store.Environments.SetReadOnlyAsync(env!.Id, readOnly: true, ct);

        var bundle = new
        {
            environmentKey = env.Key,
            exportedAt = "2026-05-29T00:00:00Z",
            flags = new[]
            {
                new
                {
                    key = "frozen-import",
                    name = "Frozen",
                    environmentId = "00000000-0000-0000-0000-000000000000",
                    type = "Boolean",
                    enabled = true,
                    defaultVariantKey = "off",
                    variants = new[] { new { key = "off", name = "Off", value = false } },
                    rules = Array.Empty<object>(),
                    tags = Array.Empty<string>(),
                },
            },
            configs = Array.Empty<object>(),
            segments = Array.Empty<object>(),
        };

        var resp = await admin.PostAsJsonAsync(ImportUri, bundle, ct);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await store.Flags.GetAsync(env.Id, "frozen-import", ct)).Should().BeNull();
    }

    [Fact]
    public async Task Export_and_import_require_the_dedicated_backup_permissions()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        var admin = AdminClient(host);

        // A viewer-floor identity holds every *Read permission (including
        // FlagRead, the old export gate) but not BackupExport/BackupImport.
        var mint = await admin.PostAsJsonAsync("/api/admin/apikeys", new { name = "viewer-key", userIdentifier = "viewer@example.com" }, ct);
        var minted = await mint.Content.ReadFromJsonAsync<ApiKeyMintResponse>(TestJson.Options, ct);

        var viewer = host.GetTestClient();
        viewer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", minted!.Token);

        // Sanity: the viewer floor can read flags...
        (await viewer.GetAsync(new Uri("/api/admin/flags", UriKind.Relative), ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        // ...but can neither export nor import a bundle.
        (await viewer.GetAsync(ExportUri, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var content = new StringContent("""{"environmentKey":"development","flags":[],"configs":[],"segments":[]}""", System.Text.Encoding.UTF8, "application/json");
        (await viewer.PostAsync(ImportUri, content, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static HttpClient AdminClient(IHost host)
    {
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        return client;
    }

}
