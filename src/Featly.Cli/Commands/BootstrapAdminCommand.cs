using System.CommandLine;
using Featly.Cli.Infrastructure;

namespace Featly.Cli.Commands;

/// <summary>
/// Builds the <c>featly bootstrap-admin</c> command: provisions the very first
/// administrator on a running server that has no users yet. The server endpoint
/// is unauthenticated but self-guards (it refuses once any user exists), so this
/// command needs only a server URL — no API key.
/// </summary>
internal static class BootstrapAdminCommand
{
    public static Command Build()
    {
        var identifier = new Option<string>("--identifier", "-i") { Description = "Identifier for the first admin (email / OIDC subject / username).", Required = true };
        var display = new Option<string?>("--display", "-d") { Description = "Display name for the admin (defaults to the identifier)." };
        var server = CliOptions.ServerUrl();

        var command = new Command("bootstrap-admin", "Provision the first admin on a fresh server. Prints an admin token once.");
        command.Options.Add(identifier);
        command.Options.Add(display);
        command.Options.Add(server);

        command.SetAction((parseResult, cancellationToken) => CliRunner.RunAsync(async ct =>
        {
            var serverUrl = ServerConnection.ResolveServerUrl(parseResult.GetValue(server));

            // No API key: the bootstrap endpoint is unauthenticated and self-guarded.
            using var http = ServerConnection.CreateClient(serverUrl, apiKey: null);
            var client = new AdminApiClient(http);
            var admin = await client.BootstrapAsync(
                parseResult.GetValue(identifier)!,
                parseResult.GetValue(display),
                ct).ConfigureAwait(false);

            Console.WriteLine("Bootstrap admin created.");
            Console.WriteLine($"  identifier: {admin.Identifier}");
            Console.WriteLine($"  user id:    {admin.UserId}");
            Console.WriteLine($"  key id:     {admin.ApiKeyId}");
            Console.WriteLine();
            Console.WriteLine("Admin API key (shown once — store it now):");
            Console.WriteLine($"  {admin.Token}");
        }, cancellationToken));

        return command;
    }
}
