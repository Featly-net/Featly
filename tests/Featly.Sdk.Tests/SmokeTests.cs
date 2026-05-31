using AwesomeAssertions;
using Xunit;

namespace Featly.Sdk.Tests;

public class SmokeTests
{
    [Fact]
    public void Sdk_Assembly_Is_Reachable()
    {
        // Real client behavior is exercised by FlagClientTests.
        var assembly = typeof(FeatlyClientBuilder).Assembly;
        assembly.Should().NotBeNull();
    }
}
