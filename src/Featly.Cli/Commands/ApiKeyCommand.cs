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
        apikey.AddCommand(BuildGenerate());
        return apikey;
    }

    private static Command BuildGenerate()
    {
        var name = new Option<string>(["--name", "-n"], "Human-readable label for the key.") { IsRequired = true };
        var user = new Option<string?>(["--user", "-u"], "Bind the key to this user identifier (the key then acts as that user).");
        var scope = new Option<string?>("--scope", "Key scope: AdminWrite (default) or SdkRead.");
        var environment = new Option<string?>(["--environment", "-e"], "Environment key the key is scoped to (defaults to the server's default environment).");
        var server = CliOptions.ServerUrl();
        var apiKey = CliOptions.ApiKey();

        var command = new Command("generate", "Mint a new API key. The plaintext token is printed once.");
        command.AddOption(name);
        command.AddOption(user);
        command.AddOption(scope);
        command.AddOption(environment);
        command.AddOption(server);
        command.AddOption(apiKey);

        command.SetHandler(context => CliRunner.RunAsync(context, async ct =>
        {
            var parsed = context.ParseResult;
            var serverUrl = ServerConnection.ResolveServerUrl(parsed.GetValueForOption(server));
            var key = ServerConnection.ResolveApiKey(parsed.GetValueForOption(apiKey))
                ?? throw new InvalidOperationException($"an admin API key is required (--api-key or {ServerConnection.ApiKeyEnv}).");

            using var http = ServerConnection.CreateClient(serverUrl, key);
            var client = new AdminApiClient(http);
            var minted = await client.MintApiKeyAsync(
                parsed.GetValueForOption(name)!,
                parsed.GetValueForOption(scope),
                parsed.GetValueForOption(user),
                parsed.GetValueForOption(environment),
                ct).ConfigureAwait(false);

            Console.WriteLine("API key created.");
            Console.WriteLine($"  id:    {minted.Id}");
            Console.WriteLine($"  name:  {minted.Name}");
            Console.WriteLine($"  scope: {minted.Scope}");
            Console.WriteLine($"  user:  {(minted.UserId is { } u ? u.ToString() : "(service principal)")}");
            Console.WriteLine();
            Console.WriteLine("Token (shown once — store it now):");
            Console.WriteLine($"  {minted.Token}");
        }));

        return command;
    }
}
