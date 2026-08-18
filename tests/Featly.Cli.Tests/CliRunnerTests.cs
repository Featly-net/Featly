using AwesomeAssertions;
using Featly.Cli.Infrastructure;
using Xunit;

namespace Featly.Cli.Tests;

/// <summary>
/// <see cref="CliRunner.RunAsync"/> is the single error boundary every command
/// action passes through: it maps success / cancellation / failure onto the
/// process exit code and turns any exception into a one-line message on stderr.
/// </summary>
public sealed class CliRunnerTests
{
    [Fact]
    public async Task Successful_action_exits_zero()
    {
        var exit = await CliRunner.RunAsync(_ => Task.CompletedTask, TestContext.Current.CancellationToken);

        exit.Should().Be(0);
    }

    [Fact]
    public async Task Cancelled_action_exits_130_and_reports_on_stderr()
    {
        var (exit, stderr) = await RunCapturingStderrAsync(
            _ => throw new OperationCanceledException());

        exit.Should().Be(130);
        stderr.Should().Contain("operation canceled");
    }

    [Fact]
    public async Task Failing_action_exits_one_and_prints_only_the_message()
    {
        var (exit, stderr) = await RunCapturingStderrAsync(
            _ => throw new InvalidOperationException("boom"));

        exit.Should().Be(1);
        stderr.Should().Contain("featly: boom");
        // A friendly one-liner, never a stack trace.
        stderr.Should().NotContain("   at ");
    }

    private static async Task<(int Exit, string Stderr)> RunCapturingStderrAsync(Func<CancellationToken, Task> action)
    {
        var original = Console.Error;
        using var writer = new StringWriter();
        Console.SetError(writer);
        try
        {
            var exit = await CliRunner.RunAsync(action, TestContext.Current.CancellationToken);
            return (exit, writer.ToString());
        }
        finally
        {
            Console.SetError(original);
        }
    }
}
