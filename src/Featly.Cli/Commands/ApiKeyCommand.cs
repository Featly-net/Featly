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
        apikey.Subcommands.Add(BuildRotate());
        return apikey;
    }

    private static Command BuildGenerate()
    {
        var name = new Option<string>("--name", "-n") { Description = "Human-readable label for the key.", Required = true };
        var user = new Option<string?>("--user", "-u") { Description = "Bind the key to this user identifier (the key then acts as that user)." };
        var scope = new Option<string?>("--scope") { Description = "Key scope: AdminWrite (default) or SdkRead." };
        var environment = new Option<string?>("--environment", "-e") { Description = "Environment key the key is scoped to (defaults to the server's default environment)." };
        var expiresIn = new Option<int?>("--expires-in") { Description = "Days until the key expires (omit for a key that never expires)." };
        var server = CliOptions.ServerUrl();
        var apiKey = CliOptions.ApiKey();

        var command = new Command("generate", "Mint a new API key. The plaintext token is printed once.");
        command.Options.Add(name);
        command.Options.Add(user);
        command.Options.Add(scope);
        command.Options.Add(environment);
        command.Options.Add(expiresIn);
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
                ResolveExpiry(parseResult.GetValue(expiresIn)),
                ct).ConfigureAwait(false);

            Console.WriteLine("API key created.");
            PrintKey(minted);
        }, cancellationToken));

        return command;
    }

    private static Command BuildRotate()
    {
        var id = new Argument<Guid>("id") { Description = "Row id of the key to rotate (see the dashboard or GET /api/admin/apikeys)." };
        var expiresIn = new Option<int?>("--expires-in") { Description = "Days until the replacement expires (omit to inherit the old key's expiry)." };
        var server = CliOptions.ServerUrl();
        var apiKey = CliOptions.ApiKey();

        var command = new Command("rotate", "Rotate an API key: mint a replacement and revoke the old key. The new token is printed once.");
        command.Arguments.Add(id);
        command.Options.Add(expiresIn);
        command.Options.Add(server);
        command.Options.Add(apiKey);

        command.SetAction((parseResult, cancellationToken) => CliRunner.RunAsync(async ct =>
        {
            var serverUrl = ServerConnection.ResolveServerUrl(parseResult.GetValue(server));
            var key = ServerConnection.ResolveApiKey(parseResult.GetValue(apiKey))
                ?? throw new InvalidOperationException($"an admin API key is required (--api-key or {ServerConnection.ApiKeyEnv}).");

            using var http = ServerConnection.CreateClient(serverUrl, key);
            var client = new AdminApiClient(http);
            var minted = await client.RotateApiKeyAsync(
                parseResult.GetValue(id),
                ResolveExpiry(parseResult.GetValue(expiresIn)),
                ct).ConfigureAwait(false);

            Console.WriteLine("API key rotated — the old key is revoked.");
            PrintKey(minted);
        }, cancellationToken));

        return command;
    }

    private static DateTimeOffset? ResolveExpiry(int? expiresInDays)
    {
        if (expiresInDays is not { } days)
        {
            return null;
        }
        if (days <= 0)
        {
            throw new InvalidOperationException("--expires-in must be a positive number of days.");
        }
        return DateTimeOffset.UtcNow.AddDays(days);
    }

    private static void PrintKey(MintedKey minted)
    {
        Console.WriteLine($"  id:      {minted.Id}");
        Console.WriteLine($"  name:    {minted.Name}");
        Console.WriteLine($"  scope:   {minted.Scope}");
        Console.WriteLine($"  user:    {(minted.UserId is { } u ? u.ToString() : "(service principal)")}");
        Console.WriteLine($"  expires: {(minted.ExpiresAt is { } e ? e.ToString("u", System.Globalization.CultureInfo.InvariantCulture) : "never")}");
        Console.WriteLine();
        Console.WriteLine("Token (shown once — store it now):");
        Console.WriteLine($"  {minted.Token}");
    }
}
