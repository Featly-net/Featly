using AwesomeAssertions;
using Featly.Cli.Infrastructure;
using Xunit;

namespace Featly.Cli.Tests;

public sealed class MongoConnectionStringResolverTests
{
    [Fact]
    public void Explicit_connection_string_is_used_verbatim()
    {
        MongoConnectionStringResolver.Resolve("mongodb://localhost/featly?replicaSet=rs0")
            .Should().Be("mongodb://localhost/featly?replicaSet=rs0");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_value_throws_because_there_is_no_sensible_default(string? value)
    {
        // Unlike SQLite, a MongoDB deployment always points at a replica set
        // the operator chose — there is nothing sensible to fall back to.
        var resolve = () => MongoConnectionStringResolver.Resolve(value);

        resolve.Should().Throw<InvalidOperationException>()
            .WithMessage("*connection string is required*");
    }
}
