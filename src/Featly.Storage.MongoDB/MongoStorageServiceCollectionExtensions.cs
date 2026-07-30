using Featly.Storage.MongoDB.Stores;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using AbstractionsMarker = Featly.IFeatlyStore;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Storage.MongoDB;

/// <summary>
/// DI extensions for the MongoDB-backed Featly store (ADR-0034).
/// </summary>
public static class MongoStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the MongoDB Featly store. Options are bound from
    /// <c>Featly:Storage:MongoDB</c> first, then any inline
    /// <paramref name="configure"/> callback is applied on top. When
    /// <see cref="MongoFeatlyStoreOptions.AutoMigrate"/> is <c>true</c>
    /// (default), pending migration steps are applied at startup via a
    /// hosted service.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>AddFeatlyPostgresStore()</c>/<c>AddFeatlySqlServerStore()</c>/
    /// <c>AddFeatlyMySqlStore()</c>: swapping providers is a one-line change,
    /// because everything above the storage layer depends only on
    /// <see cref="IFeatlyStore"/>. Change notifications use
    /// <see cref="MongoChangeNotifier"/>, backed by a Change Stream
    /// (<see cref="MongoChangeListenerHostedService"/>, ADR-0034) — the
    /// second provider, after Postgres, to give real cross-replica push
    /// instead of the SQL Server/MySQL providers' polling-only fallback.
    /// </remarks>
    public static IServiceCollection AddFeatlyMongoStore(
        this IServiceCollection services,
        Action<MongoFeatlyStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services
            .AddOptions<MongoFeatlyStoreOptions>()
            .BindConfiguration(MongoFeatlyStoreOptions.SectionName)
            // There is no sensible default server, so fail at startup with a
            // clear message rather than at the first query with a MongoDB
            // driver exception.
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.ConnectionString),
                $"Featly: a MongoDB connection string is required. Set '{MongoFeatlyStoreOptions.SectionName}:ConnectionString' " +
                "or pass AddFeatlyMongoStore(o => o.ConnectionString = ...).")
            .ValidateOnStart();

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        services.TryAddSingleton<IMongoClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<MongoFeatlyStoreOptions>>().Value;
            MongoFeatlyDatabase.EnsureClassMapsRegistered();
            return new MongoClient(opts.ConnectionString);
        });

        services.TryAddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<MongoFeatlyStoreOptions>>().Value;
            var mongoUrl = MongoUrl.Create(opts.ConnectionString);
            var client = sp.GetRequiredService<IMongoClient>();
            return new MongoFeatlyDatabase(client.GetDatabase(mongoUrl.DatabaseName));
        });

        services.TryAddSingleton<MongoChangeNotifier>();
        services.TryAddSingleton<IChangeNotifier>(sp => sp.GetRequiredService<MongoChangeNotifier>());
        services.TryAddSingleton<IFlagStore, MongoFlagStore>();
        services.TryAddSingleton<IProjectStore, MongoProjectStore>();
        services.TryAddSingleton<IEnvironmentStore, MongoEnvironmentStore>();
        services.TryAddSingleton<ISegmentStore, MongoSegmentStore>();
        services.TryAddSingleton<IConfigStore, MongoConfigStore>();
        services.TryAddSingleton<IUserStore, MongoUserStore>();
        services.TryAddSingleton<IRoleStore, MongoRoleStore>();
        services.TryAddSingleton<IRoleAssignmentStore, MongoRoleAssignmentStore>();
        services.TryAddSingleton<IUserGroupStore, MongoUserGroupStore>();
        services.TryAddSingleton<IRoleUpgradeRequestStore, MongoRoleUpgradeRequestStore>();
        services.TryAddSingleton<IPendingChangeStore, MongoPendingChangeStore>();
        services.TryAddSingleton<IApprovalPolicyStore, MongoApprovalPolicyStore>();
        services.TryAddSingleton<IExperimentStore, MongoExperimentStore>();
        services.TryAddSingleton<IEventStore, MongoEventStore>();
        services.TryAddSingleton<IAssignmentStore, MongoAssignmentStore>();
        services.TryAddSingleton<IWebhookStore, MongoWebhookStore>();
        services.TryAddSingleton<IWebhookDeliveryStore, MongoWebhookDeliveryStore>();
        services.TryAddSingleton<IAuditStore, MongoAuditStore>();
        services.TryAddSingleton<IApiKeyStore, MongoApiKeyStore>();
        services.TryAddSingleton<ISystemSettingsStore, MongoSystemSettingsStore>();

        services.TryAddSingleton<MongoFeatlyStore>();
        services.TryAddSingleton<StorageFacade>(sp => sp.GetRequiredService<MongoFeatlyStore>());
        services.TryAddSingleton<AbstractionsMarker>(sp => sp.GetRequiredService<MongoFeatlyStore>());

        services.AddHostedService<MongoAutoMigrationHostedService>();
        services.AddHostedService<MongoChangeListenerHostedService>();

        return services;
    }
}

/// <summary>
/// Applies pending migration steps at startup when
/// <see cref="MongoFeatlyStoreOptions.AutoMigrate"/> is enabled. Logs the
/// outcome at <c>Information</c>; failures bubble up and stop the host so
/// the operator notices.
/// </summary>
internal sealed partial class MongoAutoMigrationHostedService(
    IOptions<MongoFeatlyStoreOptions> options,
    ILogger<MongoAutoMigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.AutoMigrate)
        {
            LogAutoMigrateDisabled(logger);
            return;
        }

        LogApplyingMigrations(logger);
        await MongoMigrationRunner.MigrateAsync(options.Value.ConnectionString, cancellationToken).ConfigureAwait(false);
        LogSchemaUpToDate(logger);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 3401, Level = LogLevel.Information,
        Message = "Featly MongoDB AutoMigrate disabled; skipping migration at startup.")]
    private static partial void LogAutoMigrateDisabled(ILogger logger);

    // The connection string is deliberately not logged — it carries credentials.
    [LoggerMessage(EventId = 3402, Level = LogLevel.Information,
        Message = "Applying Featly MongoDB migrations.")]
    private static partial void LogApplyingMigrations(ILogger logger);

    [LoggerMessage(EventId = 3403, Level = LogLevel.Information,
        Message = "Featly MongoDB schema is up to date.")]
    private static partial void LogSchemaUpToDate(ILogger logger);
}
