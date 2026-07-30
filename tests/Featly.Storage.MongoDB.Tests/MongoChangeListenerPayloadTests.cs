using System.Text.Json;
using AwesomeAssertions;
using MongoDB.Bson;
using Xunit;

namespace Featly.Storage.MongoDB.Tests;

/// <summary>
/// Covers <see cref="MongoChangeListenerHostedService.TryDecodeFullDocument"/>
/// in isolation from a live Change Stream — the decode step is pure, so a
/// malformed document doesn't need a real MongoDB round-trip to exercise.
/// </summary>
public class MongoChangeListenerPayloadTests
{
    [Fact]
    public void Valid_document_round_trips_the_notification()
    {
        var original = new ChangeNotification(Guid.NewGuid(), "Flag", "checkout", DateTimeOffset.UtcNow);
        var document = new BsonDocument { { "payload", JsonSerializer.Serialize(original) }, { "at", DateTime.UtcNow } };

        var decoded = MongoChangeListenerHostedService.TryDecodeFullDocument(document, out var error);

        error.Should().BeNull();
        decoded.Should().Be(original);
    }

    [Fact]
    public void Missing_payload_field_returns_null_and_the_error_instead_of_throwing()
    {
        var document = new BsonDocument { { "at", DateTime.UtcNow } };

        var decoded = MongoChangeListenerHostedService.TryDecodeFullDocument(document, out var error);

        decoded.Should().BeNull();
        error.Should().NotBeNull();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"incomplete\":")]
    [InlineData("")]
    public void Malformed_payload_returns_null_and_the_error_instead_of_throwing(string payload)
    {
        var document = new BsonDocument { { "payload", payload } };

        var decoded = MongoChangeListenerHostedService.TryDecodeFullDocument(document, out var error);

        decoded.Should().BeNull();
        error.Should().NotBeNull();
    }
}
