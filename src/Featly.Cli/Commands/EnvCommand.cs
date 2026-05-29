using System.CommandLine;
using Featly.Cli.Infrastructure;

namespace Featly.Cli.Commands;

/// <summary>
/// Builds the <c>featly env</c> command group: lock / unlock an environment's
/// ReadOnly freeze against a running server (online; needs a server URL and an
/// admin API key).
/// </summary>
internal static class EnvCommand
{
    public static Command Build()
    {
        var env = new Command("env", "Manage environments against a running Featly server (online).");
        env.AddCommand(BuildToggle("lock", readOnly: true, "Freeze an environment: reject all mutations until unlocked."));
        env.AddCommand(BuildToggle("unlock", readOnly: false, "Unfreeze an environment: allow mutations again."));
        return env;
    }

    private static Command BuildToggle(string verb, bool readOnly, string description)
    {
        var key = new Argument<string>("key", "Environment key (for example 'production').");
        var server = CliOptions.ServerUrl();
        var apiKey = CliOptions.ApiKey();

        var command = new Command(verb, description);
        command.AddArgument(key);
        command.AddOption(server);
        command.AddOption(apiKey);

        command.SetHandler(context => CliRunner.RunAsync(context, async ct =>
        {
            var parsed = context.ParseResult;
            var environmentKey = parsed.GetValueForArgument(key);
            var serverUrl = ServerConnection.ResolveServerUrl(parsed.GetValueForOption(server));
            var credential = ServerConnection.ResolveApiKey(parsed.GetValueForOption(apiKey))
                ?? throw new InvalidOperationException($"an admin API key is required (--api-key or {ServerConnection.ApiKeyEnv}).");

            using var http = ServerConnection.CreateClient(serverUrl, credential);
            var client = new AdminApiClient(http);
            await client.SetEnvironmentReadOnlyAsync(environmentKey, readOnly, ct).ConfigureAwait(false);

            Console.WriteLine($"Environment '{environmentKey}' {(readOnly ? "locked" : "unlocked")}.");
        }));

        return command;
    }
}
