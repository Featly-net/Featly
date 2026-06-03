using System.CommandLine;
using Featly.Cli.Infrastructure;
using Featly.Storage.Sqlite;

namespace Featly.Cli.Commands;

/// <summary>
/// Builds the <c>featly db</c> command group. These commands run OFFLINE: they
/// open the SQLite database directly through <see cref="SqliteMigrationRunner"/>
/// and never require a running server (the server cannot start before its schema
/// exists). All other admin commands go through the server's HTTP API instead.
/// </summary>
internal static class DbCommand
{
    public static Command Build()
    {
        var db = new Command(
            "db",
            "Manage the Featly SQLite schema offline (operates directly on the database file).");

        db.Subcommands.Add(BuildMigrate());
        db.Subcommands.Add(BuildStatus());
        db.Subcommands.Add(BuildRollback());
        db.Subcommands.Add(BuildDrop());
        return db;
    }

    private static Command BuildMigrate()
    {
        var connection = CliOptions.ConnectionString();
        var command = new Command("migrate", "Apply all pending migrations so the schema matches this build.");
        command.Options.Add(connection);

        command.SetAction((parseResult, cancellationToken) => CliRunner.RunAsync(async ct =>
        {
            var connectionString = ConnectionStringResolver.Resolve(parseResult.GetValue(connection));
            var status = await SqliteMigrationRunner.GetStatusAsync(connectionString, ct).ConfigureAwait(false);

            if (status.Pending.Count == 0)
            {
                Console.WriteLine("Schema is already up to date; nothing to apply.");
                return;
            }

            Console.WriteLine($"Applying {status.Pending.Count} pending migration(s):");
            foreach (var migration in status.Pending)
            {
                Console.WriteLine($"  + {migration}");
            }

            await SqliteMigrationRunner.MigrateAsync(connectionString, ct).ConfigureAwait(false);
            Console.WriteLine("Done. Schema is up to date.");
        }, cancellationToken));

        return command;
    }

    private static Command BuildStatus()
    {
        var connection = CliOptions.ConnectionString();
        var command = new Command("status", "Show applied and pending migrations.");
        command.Options.Add(connection);

        command.SetAction((parseResult, cancellationToken) => CliRunner.RunAsync(async ct =>
        {
            var connectionString = ConnectionStringResolver.Resolve(parseResult.GetValue(connection));
            var status = await SqliteMigrationRunner.GetStatusAsync(connectionString, ct).ConfigureAwait(false);

            Console.WriteLine($"Applied ({status.Applied.Count}):");
            foreach (var migration in status.Applied)
            {
                Console.WriteLine($"  * {migration}");
            }

            Console.WriteLine($"Pending ({status.Pending.Count}):");
            foreach (var migration in status.Pending)
            {
                Console.WriteLine($"  + {migration}");
            }

            Console.WriteLine(status.Pending.Count == 0
                ? "Schema is up to date."
                : "Run 'featly db migrate' to apply pending migrations.");
        }, cancellationToken));

        return command;
    }

    private static Command BuildRollback()
    {
        var target = new Argument<string>("target")
        {
            Description = $"Migration to roll back to. Use '{SqliteMigrationRunner.InitialDatabaseTarget}' to revert every migration.",
        };
        var connection = CliOptions.ConnectionString();
        var yes = CliOptions.Yes();
        var command = new Command("rollback", "Revert the schema down to a target migration. Destructive.");
        command.Arguments.Add(target);
        command.Options.Add(connection);
        command.Options.Add(yes);

        command.SetAction((parseResult, cancellationToken) => CliRunner.RunAsync(async ct =>
        {
            var connectionString = ConnectionStringResolver.Resolve(parseResult.GetValue(connection));
            var targetMigration = parseResult.GetValue(target)!;
            var autoYes = parseResult.GetValue(yes);

            var label = targetMigration == SqliteMigrationRunner.InitialDatabaseTarget
                ? "the initial (empty) schema"
                : $"migration '{targetMigration}'";

            if (!CliRunner.Confirm($"Roll back the schema down to {label}? This is destructive.", autoYes))
            {
                Console.WriteLine("Aborted.");
                return;
            }

            await SqliteMigrationRunner.RollbackAsync(connectionString, targetMigration, ct).ConfigureAwait(false);
            Console.WriteLine($"Rolled back to {label}.");
        }, cancellationToken));

        return command;
    }

    private static Command BuildDrop()
    {
        var connection = CliOptions.ConnectionString();
        var yes = CliOptions.Yes();
        var command = new Command("drop", "Delete the entire database (all tables and the migration history). Irreversible.");
        command.Options.Add(connection);
        command.Options.Add(yes);

        command.SetAction((parseResult, cancellationToken) => CliRunner.RunAsync(async ct =>
        {
            var connectionString = ConnectionStringResolver.Resolve(parseResult.GetValue(connection));
            var autoYes = parseResult.GetValue(yes);

            if (!CliRunner.Confirm("Drop the entire Featly database? This is irreversible.", autoYes))
            {
                Console.WriteLine("Aborted.");
                return;
            }

            var dropped = await SqliteMigrationRunner.DropAsync(connectionString, ct).ConfigureAwait(false);
            Console.WriteLine(dropped
                ? "Database dropped."
                : "No database existed; nothing to drop.");
        }, cancellationToken));

        return command;
    }
}
