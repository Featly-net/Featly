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

namespace Featly.Server.Tests;

/// <summary>
/// Minting + bootstrap of user-bound API keys (M12 PR 12B). Proves that a
/// minted key authenticates over Bearer, that a bound key acts as its real user
/// (closing the M8 limitation where an API key was not a real approver), and
/// that the guarded bootstrap endpoint provisions the first admin exactly once.
/// </summary>
public class AdminApiKeysAndBootstrapTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";

    private static readonly Uri ApiKeysUri = new("/api/admin/apikeys", UriKind.Relative);
    private static readonly Uri BootstrapUri = new("/api/admin/bootstrap", UriKind.Relative);
    private static readonly Uri FlagsUri = new("/api/admin/flags", UriKind.Relative);

    [Fact]
    public async Task Mint_rejects_unauthenticated_and_sdk_scope()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var ct = TestContext.Current.CancellationToken;

        var anon = host.GetTestClient();
        (await anon.PostAsJsonAsync(ApiKeysUri, new { name = "k" }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var sdk = host.GetTestClient();
        sdk.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SdkKey);
        (await sdk.PostAsJsonAsync(ApiKeysUri, new { name = "k" }, ct))
            .StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Minted_key_authenticates_over_bearer()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        var admin = AdminClient(host);

        var mint = await admin.PostAsJsonAsync(ApiKeysUri, new { name = "ci-key" }, ct);
        mint.StatusCode.Should().Be(HttpStatusCode.Created);
        var minted = await mint.Content.ReadFromJsonAsync<ApiKeyMintResponse>(TestJson.Options, ct);
        minted!.Token.Should().StartWith("featly_");

        // The freshly minted key authenticates over Bearer for a viewer-level read.
        var withMinted = host.GetTestClient();
        withMinted.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", minted.Token);
        (await withMinted.GetAsync(FlagsUri, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Bound_key_acts_as_the_user_not_blanket_admin()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        var admin = AdminClient(host);

        // Bind to a user with no role assignments. In Open auto-provision mode it
        // resolves to the viewer floor: can read, cannot mutate.
        var mint = await admin.PostAsJsonAsync(ApiKeysUri, new { name = "alice-key", userIdentifier = "alice@example.com" }, ct);
        mint.StatusCode.Should().Be(HttpStatusCode.Created);
        var minted = await mint.Content.ReadFromJsonAsync<ApiKeyMintResponse>(TestJson.Options, ct);
        minted!.UserId.Should().NotBeNull();

        var asAlice = host.GetTestClient();
        asAlice.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", minted.Token);

        // Read allowed (viewer floor) ...
        (await asAlice.GetAsync(FlagsUri, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        // ... but an admin-only write is forbidden — the key is NOT a blanket admin.
        var create = await asAlice.PostAsJsonAsync(FlagsUri, new
        {
            key = "nope",
            name = "Nope",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = new[] { new { key = "off", name = "Off", value = false } },
        }, ct);
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Revoked_key_no_longer_authenticates()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        var admin = AdminClient(host);

        var minted = await (await admin.PostAsJsonAsync(ApiKeysUri, new { name = "temp" }, ct))
            .Content.ReadFromJsonAsync<ApiKeyMintResponse>(TestJson.Options, ct);

        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", minted!.Token);
        (await client.GetAsync(FlagsUri, ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await admin.PostAsync(new Uri($"/api/admin/apikeys/{minted.Id}/revoke", UriKind.Relative), null, ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.GetAsync(FlagsUri, ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_returns_metadata_without_secret()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        var admin = AdminClient(host);

        await admin.PostAsJsonAsync(ApiKeysUri, new { name = "listed" }, ct);

        var raw = await (await admin.GetAsync(ApiKeysUri, ct)).Content.ReadAsStringAsync(ct);
        raw.Should().NotContain("argon2id", "the hash must never be serialized");
        raw.Should().NotContain("\"token\"", "the plaintext is only returned at creation");

        var list = await admin.GetFromJsonAsync<List<ApiKeyView>>(ApiKeysUri, TestJson.Options, ct);
        list!.Should().ContainSingle(k => k.Name == "listed" && !k.Revoked);
    }

    [Fact]
    public async Task Bootstrap_provisions_first_admin_once_and_attributes_to_real_user()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var ct = TestContext.Current.CancellationToken;

        // First call (unauthenticated) provisions the first admin.
        var anon = host.GetTestClient();
        var resp = await anon.PostAsJsonAsync(BootstrapUri, new { identifier = "founder@example.com" }, ct);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var boot = await resp.Content.ReadFromJsonAsync<BootstrapResponse>(TestJson.Options, ct);
        boot!.Token.Should().StartWith("featly_");
        boot.Identifier.Should().Be("founder@example.com");

        // The bootstrap key has real admin power: it can create a flag.
        var asFounder = host.GetTestClient();
        asFounder.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", boot.Token);
        (await asFounder.PostAsJsonAsync(FlagsUri, new
        {
            key = "by-founder",
            name = "By Founder",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = new[] { new { key = "off", name = "Off", value = false } },
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        // The action is attributed to the real user identifier, not "api-key:AdminWrite".
        var audit = await asFounder.GetFromJsonAsync<List<AuditEntry>>("/api/admin/audit?entityType=Flag", TestJson.Options, ct);
        audit!.Should().Contain(a => a.Action == FeatlyEventTypes.FlagCreated && a.ActorIdentifier == "founder@example.com");

        // Second call is refused — bootstrap is first-run only.
        (await anon.PostAsJsonAsync(BootstrapUri, new { identifier = "second@example.com" }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Mint_with_past_expiry_is_rejected()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        var admin = AdminClient(host);

        var resp = await admin.PostAsJsonAsync(ApiKeysUri, new
        {
            name = "already-dead",
            expiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        }, ct);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Mint_with_future_expiry_returns_and_lists_it()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        var admin = AdminClient(host);

        var expiry = DateTimeOffset.UtcNow.AddDays(30);
        var mint = await admin.PostAsJsonAsync(ApiKeysUri, new { name = "expiring", expiresAt = expiry }, ct);
        mint.StatusCode.Should().Be(HttpStatusCode.Created);
        var minted = await mint.Content.ReadFromJsonAsync<ApiKeyMintResponse>(TestJson.Options, ct);
        minted!.ExpiresAt.Should().BeCloseTo(expiry, TimeSpan.FromSeconds(1));

        // The key authenticates while the expiry is in the future.
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", minted.Token);
        (await client.GetAsync(FlagsUri, ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await admin.GetFromJsonAsync<List<ApiKeyView>>(ApiKeysUri, TestJson.Options, ct);
        list!.Should().ContainSingle(k => k.Name == "expiring")
            .Which.ExpiresAt.Should().BeCloseTo(expiry, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Expired_key_no_longer_authenticates()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var ct = TestContext.Current.CancellationToken;

        // Seed an already-expired key straight into the store — the mint
        // endpoint (correctly) refuses to create one.
        var store = host.Services.GetRequiredService<Featly.Storage.IFeatlyStore>();
        var hasher = host.Services.GetRequiredService<Featly.Server.Authentication.ApiKeyHasher>();
        var project = await store.Projects.GetDefaultAsync(ct);
        var environment = await store.Environments.GetDefaultAsync(project!.Id, ct);
        var plaintext = hasher.GenerateToken();
        await store.ApiKeys.CreateAsync(new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = "expired",
            Prefix = Featly.Server.Authentication.ApiKeyHasher.ExtractPrefix(plaintext),
            Hash = hasher.Hash(plaintext),
            Scope = ApiKeyScope.AdminWrite,
            EnvironmentId = environment!.Id,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            CreatedBy = "test",
        }, ct);

        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", plaintext);
        (await client.GetAsync(FlagsUri, ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Rotate_mints_replacement_and_revokes_old()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        var admin = AdminClient(host);

        var minted = await (await admin.PostAsJsonAsync(ApiKeysUri, new { name = "rotate-me", userIdentifier = "bob@example.com" }, ct))
            .Content.ReadFromJsonAsync<ApiKeyMintResponse>(TestJson.Options, ct);

        var rotate = await admin.PostAsJsonAsync(new Uri($"/api/admin/apikeys/{minted!.Id}/rotate", UriKind.Relative), new { }, ct);
        rotate.StatusCode.Should().Be(HttpStatusCode.Created);
        var replacement = await rotate.Content.ReadFromJsonAsync<ApiKeyMintResponse>(TestJson.Options, ct);

        // The replacement carries the old key's name, scope, and user binding.
        replacement!.Id.Should().NotBe(minted.Id);
        replacement.Name.Should().Be("rotate-me");
        replacement.Scope.Should().Be(minted.Scope);
        replacement.UserId.Should().Be(minted.UserId);
        replacement.Token.Should().NotBe(minted.Token);

        // Old token stops authenticating; the replacement works.
        var oldClient = host.GetTestClient();
        oldClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", minted.Token);
        (await oldClient.GetAsync(FlagsUri, ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var newClient = host.GetTestClient();
        newClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", replacement.Token);
        (await newClient.GetAsync(FlagsUri, ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        // The old row is kept, revoked, for audit.
        var list = await admin.GetFromJsonAsync<List<ApiKeyView>>(ApiKeysUri, TestJson.Options, ct);
        list!.Should().Contain(k => k.Id == minted.Id && k.Revoked);
        list.Should().Contain(k => k.Id == replacement.Id && !k.Revoked);
    }

    [Fact]
    public async Task Rotate_refuses_a_revoked_key()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        var admin = AdminClient(host);

        var minted = await (await admin.PostAsJsonAsync(ApiKeysUri, new { name = "revoked-then-rotated" }, ct))
            .Content.ReadFromJsonAsync<ApiKeyMintResponse>(TestJson.Options, ct);
        await admin.PostAsync(new Uri($"/api/admin/apikeys/{minted!.Id}/revoke", UriKind.Relative), null, ct);

        var rotate = await admin.PostAsJsonAsync(new Uri($"/api/admin/apikeys/{minted.Id}/rotate", UriKind.Relative), new { }, ct);
        rotate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private static HttpClient AdminClient(IHost host)
    {
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        return client;
    }

}
