using AwesomeAssertions;
using Featly.Cli.Infrastructure;
using Xunit;

namespace Featly.Cli.Tests;

public sealed class SqlServerConnectionStringResolverTests
{
    [Fact]
    public void Explicit_connection_string_is_used_verbatim()
    {
        SqlServerConnectionStringResolver.Resolve("Server=db;Database=featly;User Id=featly;Password=secret")
            .Should().Be("Server=db;Database=featly;User Id=featly;Password=secret");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_value_throws_because_there_is_no_sensible_default(string? value)
    {
        // Unlike SQLite, a SQL Server deployment always points at a server the
        // operator chose — there is nothing sensible to fall back to.
        var resolve = () => SqlServerConnectionStringResolver.Resolve(value);

        resolve.Should().Throw<InvalidOperationException>()
            .WithMessage("*connection string is required*");
    }
}
