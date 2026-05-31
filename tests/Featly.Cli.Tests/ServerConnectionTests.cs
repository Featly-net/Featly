using AwesomeAssertions;
using Featly.Cli.Infrastructure;
using Xunit;

namespace Featly.Cli.Tests;

public sealed class ServerConnectionTests
{
    [Fact]
    public void Explicit_server_url_wins()
    {
        ServerConnection.ResolveServerUrl("http://featly.internal:9000")
            .Should().Be("http://featly.internal:9000");
    }

    [Fact]
    public void Missing_server_url_falls_back_to_localhost_default()
    {
        // Assumes FEATLY_SERVER_URL is unset in the test environment.
        ServerConnection.ResolveServerUrl(null)
            .Should().Be(ServerConnection.DefaultServerUrl);
    }

    [Fact]
    public void Explicit_api_key_wins_and_missing_is_null()
    {
        ServerConnection.ResolveApiKey("featly_key").Should().Be("featly_key");
        ServerConnection.ResolveApiKey("   ").Should().BeNull();
    }

    [Fact]
    public void CreateClient_sets_base_address_and_bearer()
    {
        using var withKey = ServerConnection.CreateClient("http://localhost:5080", "featly_key");
        withKey.BaseAddress.Should().Be(new Uri("http://localhost:5080"));
        withKey.DefaultRequestHeaders.Authorization!.Scheme.Should().Be("Bearer");
        withKey.DefaultRequestHeaders.Authorization.Parameter.Should().Be("featly_key");

        using var noKey = ServerConnection.CreateClient("http://localhost:5080", apiKey: null);
        noKey.DefaultRequestHeaders.Authorization.Should().BeNull();
    }
}
