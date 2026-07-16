using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.Postgres.Tests;

/// <summary>
/// Covers <see cref="PostgresChangeListenerHostedService.TryDecodePayload"/> in
/// isolation from a live connection — the decode step is pure, so a malformed
/// NOTIFY payload doesn't need a real Postgres round-trip to exercise.
/// </summary>
public class PostgresChangeListenerPayloadTests
{
    [Fact]
    public void Valid_payload_round_trips_the_notification()
    {
        var original = new ChangeNotification(Guid.NewGuid(), "Flag", "checkout", DateTimeOffset.UtcNow);
        var payload = System.Text.Json.JsonSerializer.Serialize(original);

        var decoded = PostgresChangeListenerHostedService.TryDecodePayload(payload, out var error);

        error.Should().BeNull();
        decoded.Should().Be(original);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"incomplete\":")]
    [InlineData("")]
    public void Malformed_payload_returns_null_and_the_error_instead_of_throwing(string payload)
    {
        var decoded = PostgresChangeListenerHostedService.TryDecodePayload(payload, out var error);

        decoded.Should().BeNull();
        error.Should().NotBeNull();
    }
}
