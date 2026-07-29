using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Featly.Storage.SqlServer.Tests;

/// <summary>
/// Direct coverage for <c>SqlServerAutoMigrationHostedService</c> — not
/// exercised by <see cref="SqlServerStoreRegistrationTests"/>, since building a
/// <see cref="ServiceProvider"/> alone never starts <c>IHostedService</c>
/// instances. Mirrors how a real host would run it: <c>StartAsync</c> either
/// applies pending migrations or skips them per <c>AutoMigrate</c>.
/// </summary>
[Trait("Category", "RequiresSqlServer")]
public class SqlServerAutoMigrationHostedServiceTests
{
    [Fact]
    public async Task StartAsync_skips_migration_when_AutoMigrate_is_false()
    {
        // AutoMigrate=false must return before ever touching the database, so
        // an unreachable connection string proves the skip actually happened —
        // touching it would throw.
        var factory = CreateFactory("Server=unreachable-host;Database=featly;Trusted_Connection=True;Connect Timeout=1");
        var options = Options.Create(new SqlServerFeatlyStoreOptions { AutoMigrate = false });
        var service = new SqlServerAutoMigrationHostedService(factory, options, NullLogger<SqlServerAutoMigrationHostedService>.Instance);

        var start = async () => await service.StartAsync(TestContext.Current.CancellationToken);
        await start.Should().NotThrowAsync();

        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartAsync_applies_pending_migrations_when_AutoMigrate_is_true()
    {
        await using var host = await SqlServerTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        // Start from an empty schema — SqlServerTestHost.CreateAsync already
        // migrated it, so roll everything back first.
        await SqlServerMigrationRunner.RollbackAsync(host.ConnectionString, SqlServerMigrationRunner.InitialDatabaseTarget, ct);
        (await SqlServerMigrationRunner.GetStatusAsync(host.ConnectionString, ct)).Applied.Should().BeEmpty();

        var factory = CreateFactory(host.ConnectionString);
        var options = Options.Create(new SqlServerFeatlyStoreOptions { AutoMigrate = true });
        var service = new SqlServerAutoMigrationHostedService(factory, options, NullLogger<SqlServerAutoMigrationHostedService>.Instance);

        await service.StartAsync(ct);

        var status = await SqlServerMigrationRunner.GetStatusAsync(host.ConnectionString, ct);
        status.Pending.Should().BeEmpty();
        status.Applied.Should().NotBeEmpty();

        await service.StopAsync(ct);
    }

    private static IDbContextFactory<FeatlyDbContext> CreateFactory(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<FeatlyDbContext>(builder => builder.UseSqlServer(connectionString));
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<FeatlyDbContext>>();
    }
}
