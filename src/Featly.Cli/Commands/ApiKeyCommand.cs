using System.CommandLine;
using Featly.Cli.Infrastructure;

namespace Featly.Cli.Commands;

/// <summary>
/// Builds the <c>featly apikey</c> command group. Online: talks to a running
/// server's admin API over HTTP (reusing its permission checks, audit, and
/// webhooks), so it needs a server URL and an admin API key.
/// </summary>
internal static class ApiKeyCommand
{
    public static Command Build()
    {
        var apikey = new Command("apikey", "Manage API keys against a running Featly server (online).");
        apikey.Subcommands.Add(BuildGenerate());
        return apikey;
    }

    private static Command BuildGenerate()
    {
        var name = new Option<string>("--name", "-n") { Description = "Human-readable label for the key.", Required = true };
        var user = new Option<string?>("--user", "-u") { Description = "Bind the key to this user identifier (the key then acts as that user)." };
        var scope = new Option<string?>("--scope") { Description = "Key scope: AdminWrite (default) or SdkRead." };
        var environment = new Option<string?>("--environment", "-e") { Description = "Environment key the key is scoped to (defaults to the server's default environment)." };
        var server = CliOptions.ServerUrl();
        var apiKey = CliOptions.ApiKey();

        var command = new Command("generate", "Mint a new API key. The plaintext token is printed once.");
        command.Options.Add(name);
        command.Options.Add(user);
        command.Options.Add(scope);
        command.Options.Add(environment);
        command.Options.Add(server);
        command.Options.Add(apiKey);

        command.SetAction((parseResult, cancellationToken) => CliRunner.RunAsync(async ct =>
        {
            var serverUrl = ServerConnection.ResolveServerUrl(parseResult.GetValue(server));
            var key = ServerConnection.ResolveApiKey(parseResult.GetValue(apiKey))
                ?? throw new InvalidOperationException($"an admin API key is required (--api-key or {ServerConnection.ApiKeyEnv}).");

            using var http = ServerConnection.CreateClient(serverUrl, key);
            var client = new AdminApiClient(http);
            var minted = await client.MintApiKeyAsync(
                parseResult.GetValue(name)!,
                parseResult.GetValue(scope),
                parseResult.GetValue(user),
                parseResult.GetValue(environment),
                ct).ConfigureAwait(false);

            Console.WriteLine("API key created.");
            Console.WriteLine($"  id:    {minted.Id}");
            Console.WriteLine($"  name:  {minted.Name}");
            Console.WriteLine($"  scope: {minted.Scope}");
            Console.WriteLine($"  user:  {(minted.UserId is { } u ? u.ToString() : "(service principal)")}");
            Console.WriteLine();
            Console.WriteLine("Token (shown once — store it now):");
            Console.WriteLine($"  {minted.Token}");
        }, cancellationToken));

        return command;
    }
}
