using System.CommandLine;
using System.CommandLine.Parsing;
using Featly.Cli.Infrastructure;

namespace Featly.Cli.Commands;

/// <summary>
/// Builds the <c>featly export</c> and <c>featly import</c> commands: move an
/// environment's flag / config / segment definitions to and from a JSON file,
/// against a running server (online).
/// </summary>
internal static class ExportImportCommand
{
    public static Command BuildExport()
    {
        var environment = new Option<string?>(["--environment", "-e"], "Environment key to export (defaults to the server's default environment).");
        var output = new Option<string?>(["--output", "-o"], "Write the bundle to this file. Defaults to standard output.");
        var server = CliOptions.ServerUrl();
        var apiKey = CliOptions.ApiKey();

        var command = new Command("export", "Export an environment's flag/config/segment definitions as a JSON bundle.");
        command.AddOption(environment);
        command.AddOption(output);
        command.AddOption(server);
        command.AddOption(apiKey);

        command.SetHandler(context => CliRunner.RunAsync(context, async ct =>
        {
            var parsed = context.ParseResult;
            using var http = CreateClient(parsed, server, apiKey);
            var client = new AdminApiClient(http);

            var json = await client.ExportAsync(parsed.GetValueForOption(environment), ct).ConfigureAwait(false);

            var path = parsed.GetValueForOption(output);
            if (string.IsNullOrWhiteSpace(path))
            {
                Console.WriteLine(json);
            }
            else
            {
                await System.IO.File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
                Console.WriteLine($"Exported bundle to {path}.");
            }
        }));

        return command;
    }

    public static Command BuildImport()
    {
        var file = new Argument<string>("file", "Path to a JSON bundle previously produced by 'featly export'.");
        var environment = new Option<string?>(["--environment", "-e"], "Target environment key (defaults to the server's default environment).");
        var server = CliOptions.ServerUrl();
        var apiKey = CliOptions.ApiKey();

        var command = new Command("import", "Import a JSON bundle into an environment (upserts definitions by key).");
        command.AddArgument(file);
        command.AddOption(environment);
        command.AddOption(server);
        command.AddOption(apiKey);

        command.SetHandler(context => CliRunner.RunAsync(context, async ct =>
        {
            var parsed = context.ParseResult;
            var path = parsed.GetValueForArgument(file);
            if (!System.IO.File.Exists(path))
            {
                throw new InvalidOperationException($"file not found: {path}");
            }

            var json = await System.IO.File.ReadAllTextAsync(path, ct).ConfigureAwait(false);

            using var http = CreateClient(parsed, server, apiKey);
            var client = new AdminApiClient(http);
            var result = await client.ImportAsync(parsed.GetValueForOption(environment), json, ct).ConfigureAwait(false);

            Console.WriteLine($"Imported into '{result.EnvironmentKey}': {result.Flags} flag(s), {result.Configs} config(s), {result.Segments} segment(s).");
        }));

        return command;
    }

    private static HttpClient CreateClient(ParseResult parsed, Option<string?> server, Option<string?> apiKey)
    {
        var serverUrl = ServerConnection.ResolveServerUrl(parsed.GetValueForOption(server));
        var key = ServerConnection.ResolveApiKey(parsed.GetValueForOption(apiKey))
            ?? throw new InvalidOperationException($"an admin API key is required (--api-key or {ServerConnection.ApiKeyEnv}).");
        return ServerConnection.CreateClient(serverUrl, key);
    }
}
