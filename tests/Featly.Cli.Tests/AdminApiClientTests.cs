using System.Net;
using System.Text;
using Featly.Cli.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Featly.Cli.Tests;

/// <summary>
/// Request shaping + response parsing + error surfacing for the online admin
/// client, exercised against a stub <see cref="HttpMessageHandler"/> (no real
/// server). The server-side contract is covered by the Server test suite.
/// </summary>
public sealed class AdminApiClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MintApiKey_posts_to_apikeys_and_parses_the_token()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created,
            """{"id":"00000000-0000-0000-0000-000000000001","name":"ci","prefix":"featly_AAA","scope":"AdminWrite","userId":null,"token":"featly_secret"}"""));
        var client = new AdminApiClient(Client(handler));

        var minted = await client.MintApiKeyAsync("ci", scope: null, userIdentifier: "alice@x.io", environmentKey: null, Ct);

        minted.Token.Should().Be("featly_secret");
        minted.Name.Should().Be("ci");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/api/admin/apikeys");
        handler.LastBody.Should().Contain("\"name\":\"ci\"").And.Contain("\"userIdentifier\":\"alice@x.io\"");
    }

    [Fact]
    public async Task Bootstrap_posts_to_bootstrap_and_parses_the_admin()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created,
            """{"identifier":"founder@x.io","userId":"00000000-0000-0000-0000-000000000002","apiKeyId":"00000000-0000-0000-0000-000000000003","token":"featly_admin"}"""));
        var client = new AdminApiClient(Client(handler));

        var admin = await client.BootstrapAsync("founder@x.io", displayName: "Founder", Ct);

        admin.Token.Should().Be("featly_admin");
        admin.Identifier.Should().Be("founder@x.io");
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/api/admin/bootstrap");
    }

    [Theory]
    [InlineData(true, "/api/admin/environments/production/lock")]
    [InlineData(false, "/api/admin/environments/production/unlock")]
    public async Task SetEnvironmentReadOnly_posts_to_the_right_path(bool readOnly, string expectedPath)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new AdminApiClient(Client(handler));

        await client.SetEnvironmentReadOnlyAsync("production", readOnly, Ct);

        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be(expectedPath);
    }

    [Fact]
    public async Task Non_success_surfaces_the_server_error_message()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Conflict, """{"error":"a user already exists."}"""));
        var client = new AdminApiClient(Client(handler));

        var act = async () => await client.BootstrapAsync("x", null, Ct);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*409*").WithMessage("*a user already exists.*");
    }

    private static HttpClient Client(StubHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://localhost:5080") };

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            return responder(request);
        }
    }
}
