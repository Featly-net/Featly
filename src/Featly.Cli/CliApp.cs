using System.CommandLine;
using Featly.Cli.Commands;

namespace Featly.Cli;

/// <summary>
/// Composition root for the <c>featly</c> global tool. Exposed publicly so tests
/// can drive the command tree in-process.
/// </summary>
public static class CliApp
{
    /// <summary>
    /// Builds the root command and invokes it with the supplied arguments.
    /// </summary>
    /// <param name="args">Raw command-line arguments.</param>
    /// <returns>The process exit code.</returns>
    public static Task<int> RunAsync(string[] args)
    {
        return BuildRootCommand().Parse(args).InvokeAsync();
    }

    /// <summary>
    /// Builds the full <c>featly</c> command tree. Separate from
    /// <see cref="RunAsync"/> so tests can inspect or invoke it directly.
    /// </summary>
    public static RootCommand BuildRootCommand()
    {
        var root = new RootCommand(
            "Featly CLI — operate the Featly SQLite store and server from the command line.");
        root.Subcommands.Add(DbCommand.Build());
        root.Subcommands.Add(ApiKeyCommand.Build());
        root.Subcommands.Add(BootstrapAdminCommand.Build());
        root.Subcommands.Add(EnvCommand.Build());
        root.Subcommands.Add(FlagsCommand.Build());
        root.Subcommands.Add(ExportImportCommand.BuildExport());
        root.Subcommands.Add(ExportImportCommand.BuildImport());
        return root;
    }
}
