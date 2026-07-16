using AwesomeAssertions;
using Featly.Cli.Infrastructure;
using Xunit;

namespace Featly.Cli.Tests;

public sealed class PostgresConnectionStringResolverTests
{
    [Fact]
    public void Explicit_connection_string_is_used_verbatim()
    {
        PostgresConnectionStringResolver.Resolve("Host=db;Database=featly;Username=featly;Password=secret")
            .Should().Be("Host=db;Database=featly;Username=featly;Password=secret");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_value_throws_because_there_is_no_sensible_default(string? value)
    {
        // Unlike SQLite, a Postgres deployment always points at a server the
        // operator chose — there is nothing sensible to fall back to.
        var resolve = () => PostgresConnectionStringResolver.Resolve(value);

        resolve.Should().Throw<InvalidOperationException>()
            .WithMessage("*connection string is required*");
    }
}
