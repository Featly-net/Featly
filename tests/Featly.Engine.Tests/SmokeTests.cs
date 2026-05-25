using FluentAssertions;
using Xunit;

namespace Featly.Engine.Tests;

public class SmokeTests
{
    [Fact]
    public void Engine_Assembly_Is_Reachable()
    {
        // Placeholder for M1: ensures the test pipeline runs end-to-end.
        // M3 replaces this with full evaluation engine coverage.
        var assembly = typeof(Featly.Engine.EngineMarker).Assembly;
        assembly.Should().NotBeNull();
    }
}
