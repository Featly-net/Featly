using System.CommandLine;
using Featly.Cli.Infrastructure;

namespace Featly.Cli.Commands;

/// <summary>
/// Builds the <c>featly flags</c> command group: list flags and flip the
/// master switch against a running server (online; needs a server URL and an
/// admin API key). Everything else about a flag — variants, rules, tags — is
/// left to the dashboard or the admin API; this covers the two operations a
/// script or an on-call runbook actually needs.
/// </summary>
internal static class FlagsCommand
{
    public static Command Build()
    {
        var flags = new Command("flags", "List and toggle flags against a running Featly server (online).");
        flags.Subcommands.Add(BuildList());
        flags.Subcommands.Add(BuildToggle("enable", enabled: true, "Turn a flag's master switch on."));
        flags.Subcommands.Add(BuildToggle("disable", enabled: false, "Turn a flag's master switch off."));
        return flags;
    }

    private static Command BuildList()
    {
        var environment = new Option<string?>("--environment", "-e") { Description = "Environment key (defaults to the server's default environment)." };
        var server = CliOptions.ServerUrl();
        var apiKey = CliOptions.ApiKey();

        var command = new Command("list", "List every flag in an environment.");
        command.Options.Add(environment);
        command.Options.Add(server);
        command.Options.Add(apiKey);

        command.SetAction((parseResult, cancellationToken) => CliRunner.RunAsync(async ct =>
        {
            var serverUrl = ServerConnection.ResolveServerUrl(parseResult.GetValue(server));
            var credential = ServerConnection.ResolveApiKey(parseResult.GetValue(apiKey))
                ?? throw new InvalidOperationException($"an admin API key is required (--api-key or {ServerConnection.ApiKeyEnv}).");

            using var http = ServerConnection.CreateClient(serverUrl, credential);
            var client = new AdminApiClient(http);
            var flagList = await client.ListFlagsAsync(parseResult.GetValue(environment), ct).ConfigureAwait(false);

            if (flagList.Count == 0)
            {
                Console.WriteLine("No flags found.");
                return;
            }

            var keyWidth = Math.Max(3, flagList.Max(f => f.Key.Length));
            Console.WriteLine($"{"KEY".PadRight(keyWidth)}  {"ENABLED",-7}  {"TYPE",-8}  VARIANTS  NAME");
            foreach (var flag in flagList)
            {
                var variantCount = flag.Variants?.Count ?? 0;
                Console.WriteLine($"{flag.Key.PadRight(keyWidth)}  {(flag.Enabled ? "on" : "off"),-7}  {flag.Type,-8}  {variantCount,8}  {flag.Name}");
            }
        }, cancellationToken));

        return command;
    }

    private static Command BuildToggle(string verb, bool enabled, string description)
    {
        var key = new Argument<string>("key") { Description = "Flag key." };
        var environment = new Option<string?>("--environment", "-e") { Description = "Environment key (defaults to the server's default environment)." };
        var server = CliOptions.ServerUrl();
        var apiKey = CliOptions.ApiKey();

        var command = new Command(verb, description);
        command.Arguments.Add(key);
        command.Options.Add(environment);
        command.Options.Add(server);
        command.Options.Add(apiKey);

        command.SetAction((parseResult, cancellationToken) => CliRunner.RunAsync(async ct =>
        {
            var flagKey = parseResult.GetValue(key)!;
            var serverUrl = ServerConnection.ResolveServerUrl(parseResult.GetValue(server));
            var credential = ServerConnection.ResolveApiKey(parseResult.GetValue(apiKey))
                ?? throw new InvalidOperationException($"an admin API key is required (--api-key or {ServerConnection.ApiKeyEnv}).");

            using var http = ServerConnection.CreateClient(serverUrl, credential);
            var client = new AdminApiClient(http);
            await client.SetFlagEnabledAsync(flagKey, enabled, parseResult.GetValue(environment), ct).ConfigureAwait(false);

            Console.WriteLine($"Flag '{flagKey}' {(enabled ? "enabled" : "disabled")}.");
        }, cancellationToken));

        return command;
    }
}
