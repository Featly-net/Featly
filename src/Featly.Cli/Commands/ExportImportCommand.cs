using System.CommandLine;
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
        var environment = new Option<string?>("--environment", "-e") { Description = "Environment key to export (defaults to the server's default environment)." };
        var output = new Option<string?>("--output", "-o") { Description = "Write the bundle to this file. Defaults to standard output." };
        var server = CliOptions.ServerUrl();
        var apiKey = CliOptions.ApiKey();

        var command = new Command("export", "Export an environment's flag/config/segment definitions as a JSON bundle.");
        command.Options.Add(environment);
        command.Options.Add(output);
        command.Options.Add(server);
        command.Options.Add(apiKey);

        command.SetAction((parseResult, cancellationToken) => CliRunner.RunAsync(async ct =>
        {
            using var http = CreateClient(parseResult, server, apiKey);
            var client = new AdminApiClient(http);

            var json = await client.ExportAsync(parseResult.GetValue(environment), ct).ConfigureAwait(false);

            var path = parseResult.GetValue(output);
            if (string.IsNullOrWhiteSpace(path))
            {
                Console.WriteLine(json);
            }
            else
            {
                await System.IO.File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
                Console.WriteLine($"Exported bundle to {path}.");
            }
        }, cancellationToken));

        return command;
    }

    public static Command BuildImport()
    {
        var file = new Argument<string>("file") { Description = "Path to a JSON bundle previously produced by 'featly export'." };
        var environment = new Option<string?>("--environment", "-e") { Description = "Target environment key (defaults to the server's default environment)." };
        var server = CliOptions.ServerUrl();
        var apiKey = CliOptions.ApiKey();

        var command = new Command("import", "Import a JSON bundle into an environment (upserts definitions by key).");
        command.Arguments.Add(file);
        command.Options.Add(environment);
        command.Options.Add(server);
        command.Options.Add(apiKey);

        command.SetAction((parseResult, cancellationToken) => CliRunner.RunAsync(async ct =>
        {
            var path = parseResult.GetValue(file);
            if (!System.IO.File.Exists(path))
            {
                throw new InvalidOperationException($"file not found: {path}");
            }

            var json = await System.IO.File.ReadAllTextAsync(path, ct).ConfigureAwait(false);

            using var http = CreateClient(parseResult, server, apiKey);
            var client = new AdminApiClient(http);
            var result = await client.ImportAsync(parseResult.GetValue(environment), json, ct).ConfigureAwait(false);

            Console.WriteLine($"Imported into '{result.EnvironmentKey}': {result.Flags} flag(s), {result.Configs} config(s), {result.Segments} segment(s).");
        }, cancellationToken));

        return command;
    }

    private static HttpClient CreateClient(ParseResult parsed, Option<string?> server, Option<string?> apiKey)
    {
        var serverUrl = ServerConnection.ResolveServerUrl(parsed.GetValue(server));
        var key = ServerConnection.ResolveApiKey(parsed.GetValue(apiKey))
            ?? throw new InvalidOperationException($"an admin API key is required (--api-key or {ServerConnection.ApiKeyEnv}).");
        return ServerConnection.CreateClient(serverUrl, key);
    }
}
