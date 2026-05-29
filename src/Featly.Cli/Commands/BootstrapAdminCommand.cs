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
        var identifier = new Option<string>(["--identifier", "-i"], "Identifier for the first admin (email / OIDC subject / username).") { IsRequired = true };
        var display = new Option<string?>(["--display", "-d"], "Display name for the admin (defaults to the identifier).");
        var server = CliOptions.ServerUrl();

        var command = new Command("bootstrap-admin", "Provision the first admin on a fresh server. Prints an admin token once.");
        command.AddOption(identifier);
        command.AddOption(display);
        command.AddOption(server);

        command.SetHandler(context => CliRunner.RunAsync(context, async ct =>
        {
            var parsed = context.ParseResult;
            var serverUrl = ServerConnection.ResolveServerUrl(parsed.GetValueForOption(server));

            // No API key: the bootstrap endpoint is unauthenticated and self-guarded.
            using var http = ServerConnection.CreateClient(serverUrl, apiKey: null);
            var client = new AdminApiClient(http);
            var admin = await client.BootstrapAsync(
                parsed.GetValueForOption(identifier)!,
                parsed.GetValueForOption(display),
                ct).ConfigureAwait(false);

            Console.WriteLine("Bootstrap admin created.");
            Console.WriteLine($"  identifier: {admin.Identifier}");
            Console.WriteLine($"  user id:    {admin.UserId}");
            Console.WriteLine($"  key id:     {admin.ApiKeyId}");
            Console.WriteLine();
            Console.WriteLine("Admin API key (shown once — store it now):");
            Console.WriteLine($"  {admin.Token}");
        }));

        return command;
    }
}
