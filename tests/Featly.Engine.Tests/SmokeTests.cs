using AwesomeAssertions;
using Xunit;

namespace Featly.Engine.Tests;

public class SmokeTests
{
    [Fact]
    public void Engine_Assembly_Is_Reachable()
    {
        // M3 expands this to full evaluation engine coverage. M2's evaluator
        // tests live alongside the boolean-flag behavior in EvaluatorTests.
        var assembly = typeof(Featly.Engine.Evaluator).Assembly;
        assembly.Should().NotBeNull();
    }
}
