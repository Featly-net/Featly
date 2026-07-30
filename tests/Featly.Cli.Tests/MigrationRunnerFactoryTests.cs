using AwesomeAssertions;
using Featly.Cli.Infrastructure;
using Xunit;

namespace Featly.Cli.Tests;

/// <summary>
/// Covers <see cref="MigrationRunnerFactory"/>'s MongoDB dispatch. A
/// connection string with no database name makes every
/// <c>MongoMigrationRunner</c> method fail its own validation before any
/// network call — exercising the wrapper wiring without needing a live
/// MongoDB replica set.
/// </summary>
public sealed class MigrationRunnerFactoryTests
{
    [Fact]
    public async Task Create_for_mongodb_dispatches_every_operation_to_MongoMigrationRunner()
    {
        var runner = MigrationRunnerFactory.Create(MigrationRunnerFactory.MongoDb, "mongodb://localhost:27017/");
        var ct = TestContext.Current.CancellationToken;

        var getStatus = async () => await runner.GetStatusAsync(ct);
        var migrate = async () => await runner.MigrateAsync(ct);
        var drop = async () => await runner.DropAsync(ct);
        var rollback = async () => await runner.RollbackAsync(MigrationRunnerFactory.InitialDatabaseTarget, ct);

        await getStatus.Should().ThrowAsync<ArgumentException>().WithMessage("*database name*");
        await migrate.Should().ThrowAsync<ArgumentException>().WithMessage("*database name*");
        await drop.Should().ThrowAsync<ArgumentException>().WithMessage("*database name*");
        await rollback.Should().ThrowAsync<NotSupportedException>().WithMessage("*not supported*");
    }

    [Fact]
    public void Create_with_an_unknown_provider_throws()
    {
        var create = () => MigrationRunnerFactory.Create("oracle", null);

        create.Should().Throw<InvalidOperationException>().WithMessage("*Unknown --provider*");
    }
}
