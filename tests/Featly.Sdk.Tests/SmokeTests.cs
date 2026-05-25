using FluentAssertions;
using Xunit;

namespace Featly.Sdk.Tests;

public class SmokeTests
{
    [Fact]
    public void Sdk_Assembly_Is_Reachable()
    {
        // Placeholder for M1. Real client tests land in M2.
        var assembly = typeof(SdkMarker).Assembly;
        assembly.Should().NotBeNull();
    }
}
