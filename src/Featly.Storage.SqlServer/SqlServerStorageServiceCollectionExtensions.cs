using Featly.Storage.EntityFramework;
using Featly.Storage.SqlServer.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AbstractionsMarker = Featly.IFeatlyStore;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Storage.SqlServer;

/// <summary>
/// DI extensions for the SQL Server-backed Featly store — the provider for
/// enterprise self-hosted, multi-node deployments (ADR-0032).
/// </summary>
public static class SqlServerStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQL Server Featly store. Options are bound from
    /// <c>Featly:Storage:SqlServer</c> first, then any inline
    /// <paramref name="configure"/> callback is applied on top. When
    /// <see cref="SqlServerFeatlyStoreOptions.AutoMigrate"/> is <c>true</c>
    /// (default), pending EF Core migrations are applied at startup via a hosted
    /// service.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>AddFeatlySqliteStore()</c>/<c>AddFeatlyPostgresStore()</c>:
    /// swapping providers is a one-line change, because everything above the
    /// storage layer depends only on <see cref="IFeatlyStore"/>. Change
    /// notifications use the same in-process <see cref="InProcessChangeNotifier"/>
    /// the SQLite provider uses — SQL Server ships polling-only, not a real
    /// cross-replica notifier (ADR-0032's Service Broker rejection).
    /// </remarks>
    public static IServiceCollection AddFeatlySqlServerStore(
        this IServiceCollection services,
        Action<SqlServerFeatlyStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services
            .AddOptions<SqlServerFeatlyStoreOptions>()
            .BindConfiguration(SqlServerFeatlyStoreOptions.SectionName)
            // There is no sensible default server, so fail at startup with a
            // clear message rather than at the first query with a SqlException.
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.ConnectionString),
                $"Featly: a SQL Server connection string is required. Set '{SqlServerFeatlyStoreOptions.SectionName}:ConnectionString' " +
                "or pass AddFeatlySqlServerStore(o => o.ConnectionString = ...).")
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
            var opts = sp.GetRequiredService<IOptions<SqlServerFeatlyStoreOptions>>().Value;
            builder.UseSqlServer(opts.ConnectionString);
        });

        services.TryAddSingleton<IChangeNotifier, InProcessChangeNotifier>();
        services.TryAddSingleton<IFlagStore, SqlServerFlagStore>();
        services.TryAddSingleton<IProjectStore, SqlServerProjectStore>();
        services.TryAddSingleton<IEnvironmentStore, SqlServerEnvironmentStore>();
        services.TryAddSingleton<ISegmentStore, SqlServerSegmentStore>();
        services.TryAddSingleton<IConfigStore, SqlServerConfigStore>();
        services.TryAddSingleton<IUserStore, SqlServerUserStore>();
        services.TryAddSingleton<IRoleStore, SqlServerRoleStore>();
        services.TryAddSingleton<IRoleAssignmentStore, SqlServerRoleAssignmentStore>();
        services.TryAddSingleton<IUserGroupStore, SqlServerUserGroupStore>();
        services.TryAddSingleton<IRoleUpgradeRequestStore, SqlServerRoleUpgradeRequestStore>();
        services.TryAddSingleton<IPendingChangeStore, SqlServerPendingChangeStore>();
        services.TryAddSingleton<IApprovalPolicyStore, SqlServerApprovalPolicyStore>();
        services.TryAddSingleton<IExperimentStore, SqlServerExperimentStore>();
        services.TryAddSingleton<IEventStore, SqlServerEventStore>();
        services.TryAddSingleton<IAssignmentStore, SqlServerAssignmentStore>();
        services.TryAddSingleton<IWebhookStore, SqlServerWebhookStore>();
        services.TryAddSingleton<IWebhookDeliveryStore, SqlServerWebhookDeliveryStore>();
        services.TryAddSingleton<IAuditStore, SqlServerAuditStore>();
        services.TryAddSingleton<IApiKeyStore, SqlServerApiKeyStore>();
        services.TryAddSingleton<ISystemSettingsStore, SqlServerSystemSettingsStore>();

        services.TryAddSingleton<EfFeatlyStore>();
        services.TryAddSingleton<StorageFacade>(sp => sp.GetRequiredService<EfFeatlyStore>());
        services.TryAddSingleton<AbstractionsMarker>(sp => sp.GetRequiredService<EfFeatlyStore>());

        services.AddHostedService<SqlServerAutoMigrationHostedService>();

        return services;
    }
}

/// <summary>
/// Applies EF Core migrations at startup when
/// <see cref="SqlServerFeatlyStoreOptions.AutoMigrate"/> is enabled. Logs the
/// outcome at <c>Information</c>; failures bubble up and stop the host so the
/// operator notices.
/// </summary>
internal sealed partial class SqlServerAutoMigrationHostedService(
    IDbContextFactory<FeatlyDbContext> contextFactory,
    IOptions<SqlServerFeatlyStoreOptions> options,
    ILogger<SqlServerAutoMigrationHostedService> logger) : IHostedService
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

    [LoggerMessage(EventId = 3201, Level = LogLevel.Information,
        Message = "Featly SQL Server AutoMigrate disabled; skipping schema migration at startup.")]
    private static partial void LogAutoMigrateDisabled(ILogger logger);

    // The connection string is deliberately not logged — it carries credentials.
    [LoggerMessage(EventId = 3202, Level = LogLevel.Information,
        Message = "Applying Featly SQL Server migrations.")]
    private static partial void LogApplyingMigrations(ILogger logger);

    [LoggerMessage(EventId = 3203, Level = LogLevel.Information,
        Message = "Featly SQL Server schema is up to date.")]
    private static partial void LogSchemaUpToDate(ILogger logger);
}
