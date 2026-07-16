using AwesomeAssertions;
using Featly.Cli.Infrastructure;
using Xunit;

namespace Featly.Cli.Tests;

public sealed class SqliteConnectionStringResolverTests
{
    [Fact]
    public void Explicit_full_connection_string_is_used_verbatim()
    {
        SqliteConnectionStringResolver.Resolve("Data Source=:memory:;Cache=Shared")
            .Should().Be("Data Source=:memory:;Cache=Shared");
    }

    [Fact]
    public void Bare_path_is_wrapped_as_data_source()
    {
        SqliteConnectionStringResolver.Resolve("./custom.db")
            .Should().Be("Data Source=./custom.db");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_value_falls_back_to_default(string? value)
    {
        SqliteConnectionStringResolver.Resolve(value)
            .Should().Be(SqliteConnectionStringResolver.Default);
    }
}
