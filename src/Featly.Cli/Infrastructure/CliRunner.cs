using System.CommandLine.Invocation;

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
    /// token and sets <see cref="InvocationContext.ExitCode"/> accordingly:
    /// <c>0</c> on success, <c>130</c> on cancellation, <c>1</c> on any error.
    /// </summary>
    public static async Task RunAsync(InvocationContext context, Func<CancellationToken, Task> action)
    {
        try
        {
            await action(context.GetCancellationToken()).ConfigureAwait(false);
            context.ExitCode = 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("featly: operation canceled.");
            context.ExitCode = 130;
        }
#pragma warning disable CA1031 // CLI boundary: any failure is surfaced as a friendly one-line error, never a stack trace.
        catch (Exception ex)
        {
            Console.Error.WriteLine($"featly: {ex.Message}");
            context.ExitCode = 1;
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
