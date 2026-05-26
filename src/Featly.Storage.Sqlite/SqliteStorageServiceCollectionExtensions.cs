using Featly.Storage;
using Featly.Storage.Sqlite.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AbstractionsMarker = Featly.IFeatlyStore;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Storage.Sqlite;

/// <summary>
/// DI extensions for the SQLite-backed Featly store.
/// </summary>
public static class SqliteStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQLite Featly store. Options are bound from
    /// <c>Featly:Storage:Sqlite</c> first, then any inline <paramref name="configure"/>
    /// callback is applied on top. When <see cref="SqliteFeatlyStoreOptions.AutoMigrate"/>
    /// is <c>true</c> (default), pending EF Core migrations are applied at startup
    /// via a hosted service.
    /// </summary>
    public static IServiceCollection AddFeatlySqliteStore(
        this IServiceCollection services,
        Action<SqliteFeatlyStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services
            .AddOptions<SqliteFeatlyStoreOptions>()
            .BindConfiguration(SqliteFeatlyStoreOptions.SectionName);

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        // Pooled factory so each per-operation context comes from a small pool
        // instead of allocating from scratch. Singleton sub-stores create
        // contexts on demand.
        services.AddPooledDbContextFactory<FeatlyDbContext>((sp, builder) =>
        {
            var opts = sp.GetRequiredService<IOptions<SqliteFeatlyStoreOptions>>().Value;
            builder.UseSqlite(opts.ConnectionString);
        });

        services.TryAddSingleton<IChangeNotifier, InProcessChangeNotifier>();
        services.TryAddSingleton<IFlagStore, SqliteFlagStore>();
        services.TryAddSingleton<IProjectStore, SqliteProjectStore>();
        services.TryAddSingleton<IEnvironmentStore, SqliteEnvironmentStore>();
        services.TryAddSingleton<ISegmentStore, SqliteSegmentStore>();
        services.TryAddSingleton<IConfigStore, SqliteConfigStore>();

        services.TryAddSingleton<SqliteFeatlyStore>();
        services.TryAddSingleton<StorageFacade>(sp => sp.GetRequiredService<SqliteFeatlyStore>());
        services.TryAddSingleton<AbstractionsMarker>(sp => sp.GetRequiredService<SqliteFeatlyStore>());

        services.AddHostedService<SqliteAutoMigrationHostedService>();

        return services;
    }
}

/// <summary>
/// Applies EF Core migrations at startup when
/// <see cref="SqliteFeatlyStoreOptions.AutoMigrate"/> is enabled. Logs the
/// outcome at <c>Information</c>; failures bubble up and stop the host so the
/// operator notices.
/// </summary>
internal sealed partial class SqliteAutoMigrationHostedService(
    IDbContextFactory<FeatlyDbContext> contextFactory,
    IOptions<SqliteFeatlyStoreOptions> options,
    ILogger<SqliteAutoMigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;
        if (!opts.AutoMigrate)
        {
            LogAutoMigrateDisabled(logger);
            return;
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        LogApplyingMigrations(logger, opts.ConnectionString);
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        LogSchemaUpToDate(logger);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information,
        Message = "Featly SQLite AutoMigrate disabled; skipping schema migration at startup.")]
    private static partial void LogAutoMigrateDisabled(ILogger logger);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information,
        Message = "Applying Featly SQLite migrations against {ConnectionString}.")]
    private static partial void LogApplyingMigrations(ILogger logger, string connectionString);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Information,
        Message = "Featly SQLite schema is up to date.")]
    private static partial void LogSchemaUpToDate(ILogger logger);
}
