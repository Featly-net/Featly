namespace Featly.Cli.Infrastructure;

/// <summary>
/// Single error boundary for command handlers. Runs the supplied action, maps
/// the outcome to a process exit code, and turns any failure into a friendly
/// one-line message instead of a raw stack trace.
/// </summary>
internal static class CliRunner
{
    /// <summary>
    /// Executes <paramref name="action"/> with the invocation's cancellation
    /// token and returns the process exit code: <c>0</c> on success, <c>130</c>
    /// on cancellation, <c>1</c> on any error. Wire it into a command action with
    /// <c>command.SetAction((parseResult, ct) =&gt; CliRunner.RunAsync(..., ct))</c>.
    /// </summary>
    public static async Task<int> RunAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await action(cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("featly: operation canceled.");
            return 130;
        }
#pragma warning disable CA1031 // CLI boundary: any failure is surfaced as a friendly one-line error, never a stack trace.
        catch (Exception ex)
        {
            Console.Error.WriteLine($"featly: {ex.Message}");
            return 1;
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Asks the user to confirm a destructive operation. Returns <c>true</c>
    /// immediately when <paramref name="autoYes"/> is set (the <c>--yes</c> flag),
    /// otherwise reads a line from stdin and accepts only <c>y</c>/<c>yes</c>.
    /// </summary>
    public static bool Confirm(string prompt, bool autoYes)
    {
        if (autoYes)
        {
            return true;
        }

        Console.Write($"{prompt} [y/N] ");
        var line = Console.ReadLine()?.Trim();
        return string.Equals(line, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(line, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
