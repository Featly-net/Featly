using Featly.Storage.EntityFramework;
using Featly.Storage.MySql.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AbstractionsMarker = Featly.IFeatlyStore;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Storage.MySql;

/// <summary>
/// DI extensions for the MySQL/MariaDB-backed Featly store (ADR-0033).
/// </summary>
public static class MySqlStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the MySQL Featly store. Options are bound from
    /// <c>Featly:Storage:MySql</c> first, then any inline
    /// <paramref name="configure"/> callback is applied on top. When
    /// <see cref="MySqlFeatlyStoreOptions.AutoMigrate"/> is <c>true</c>
    /// (default), pending EF Core migrations are applied at startup via a hosted
    /// service.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>AddFeatlySqliteStore()</c>/<c>AddFeatlyPostgresStore()</c>/
    /// <c>AddFeatlySqlServerStore()</c>: swapping providers is a one-line
    /// change, because everything above the storage layer depends only on
    /// <see cref="IFeatlyStore"/>. Change notifications use the same
    /// in-process <see cref="InProcessChangeNotifier"/> the SQLite/SQL Server
    /// providers use — MySQL ships polling-only, not a real cross-replica
    /// notifier (ADR-0033).
    /// </remarks>
    public static IServiceCollection AddFeatlyMySqlStore(
        this IServiceCollection services,
        Action<MySqlFeatlyStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services
            .AddOptions<MySqlFeatlyStoreOptions>()
            .BindConfiguration(MySqlFeatlyStoreOptions.SectionName)
            // There is no sensible default server, so fail at startup with a
            // clear message rather than at the first query with a MySqlException.
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.ConnectionString),
                $"Featly: a MySQL connection string is required. Set '{MySqlFeatlyStoreOptions.SectionName}:ConnectionString' " +
                "or pass AddFeatlyMySqlStore(o => o.ConnectionString = ...).")
            .ValidateOnStart();

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        // Pooled factory so each per-operation context comes from a small pool
        // instead of allocating from scratch. Singleton sub-stores create
        // contexts on demand.
        services.AddPooledDbContextFactory<FeatlyDbContext>((sp, builder) =>
        {
            var opts = sp.GetRequiredService<IOptions<MySqlFeatlyStoreOptions>>().Value;
            builder.UseMySql(opts.ConnectionString, MySqlServerVersionInfo.Version);
        });

        services.TryAddSingleton<IChangeNotifier, InProcessChangeNotifier>();
        services.TryAddSingleton<IFlagStore, MySqlFlagStore>();
        services.TryAddSingleton<IProjectStore, MySqlProjectStore>();
        services.TryAddSingleton<IEnvironmentStore, MySqlEnvironmentStore>();
        services.TryAddSingleton<ISegmentStore, MySqlSegmentStore>();
        services.TryAddSingleton<IConfigStore, MySqlConfigStore>();
        services.TryAddSingleton<IUserStore, MySqlUserStore>();
        services.TryAddSingleton<IRoleStore, MySqlRoleStore>();
        services.TryAddSingleton<IRoleAssignmentStore, MySqlRoleAssignmentStore>();
        services.TryAddSingleton<IUserGroupStore, MySqlUserGroupStore>();
        services.TryAddSingleton<IRoleUpgradeRequestStore, MySqlRoleUpgradeRequestStore>();
        services.TryAddSingleton<IPendingChangeStore, MySqlPendingChangeStore>();
        services.TryAddSingleton<IApprovalPolicyStore, MySqlApprovalPolicyStore>();
        services.TryAddSingleton<IExperimentStore, MySqlExperimentStore>();
        services.TryAddSingleton<IEventStore, MySqlEventStore>();
        services.TryAddSingleton<IAssignmentStore, MySqlAssignmentStore>();
        services.TryAddSingleton<IWebhookStore, MySqlWebhookStore>();
        services.TryAddSingleton<IWebhookDeliveryStore, MySqlWebhookDeliveryStore>();
        services.TryAddSingleton<IAuditStore, MySqlAuditStore>();
        services.TryAddSingleton<IApiKeyStore, MySqlApiKeyStore>();
        services.TryAddSingleton<ISystemSettingsStore, MySqlSystemSettingsStore>();

        services.TryAddSingleton<EfFeatlyStore>();
        services.TryAddSingleton<StorageFacade>(sp => sp.GetRequiredService<EfFeatlyStore>());
        services.TryAddSingleton<AbstractionsMarker>(sp => sp.GetRequiredService<EfFeatlyStore>());

        services.AddHostedService<MySqlAutoMigrationHostedService>();

        return services;
    }
}

/// <summary>
/// Applies EF Core migrations at startup when
/// <see cref="MySqlFeatlyStoreOptions.AutoMigrate"/> is enabled. Logs the
/// outcome at <c>Information</c>; failures bubble up and stop the host so the
/// operator notices.
/// </summary>
internal sealed partial class MySqlAutoMigrationHostedService(
    IDbContextFactory<FeatlyDbContext> contextFactory,
    IOptions<MySqlFeatlyStoreOptions> options,
    ILogger<MySqlAutoMigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.AutoMigrate)
        {
            LogAutoMigrateDisabled(logger);
            return;
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        LogApplyingMigrations(logger);
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        LogSchemaUpToDate(logger);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 3301, Level = LogLevel.Information,
        Message = "Featly MySQL AutoMigrate disabled; skipping schema migration at startup.")]
    private static partial void LogAutoMigrateDisabled(ILogger logger);

    // The connection string is deliberately not logged — it carries credentials.
    [LoggerMessage(EventId = 3302, Level = LogLevel.Information,
        Message = "Applying Featly MySQL migrations.")]
    private static partial void LogApplyingMigrations(ILogger logger);

    [LoggerMessage(EventId = 3303, Level = LogLevel.Information,
        Message = "Featly MySQL schema is up to date.")]
    private static partial void LogSchemaUpToDate(ILogger logger);
}
