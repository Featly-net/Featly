using Featly.Storage.Postgres.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AbstractionsMarker = Featly.IFeatlyStore;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Storage.Postgres;

/// <summary>
/// DI extensions for the PostgreSQL-backed Featly store — the provider for the
/// centralized deployment pattern, where several server replicas share one
/// database (ADR-0026).
/// </summary>
public static class PostgresStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PostgreSQL Featly store. Options are bound from
    /// <c>Featly:Storage:Postgres</c> first, then any inline
    /// <paramref name="configure"/> callback is applied on top. When
    /// <see cref="PostgresFeatlyStoreOptions.AutoMigrate"/> is <c>true</c>
    /// (default), pending EF Core migrations are applied at startup via a hosted
    /// service.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mirrors <c>AddFeatlySqliteStore()</c>: swapping providers is a one-line
    /// change, because everything above the storage layer depends only on
    /// <see cref="IFeatlyStore"/>.
    /// </para>
    /// <para>
    /// <b>Change notifications are in-process for now.</b> This registration
    /// reuses <see cref="InProcessChangeNotifier"/>, so an SSE client connected to
    /// replica A will not be pushed a change made through replica B — it catches
    /// up on its next poll instead. The Postgres-native <c>LISTEN</c>/<c>NOTIFY</c>
    /// notifier ADR-0026 calls for is tracked separately in issue #179.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddFeatlyPostgresStore(
        this IServiceCollection services,
        Action<PostgresFeatlyStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services
            .AddOptions<PostgresFeatlyStoreOptions>()
            .BindConfiguration(PostgresFeatlyStoreOptions.SectionName)
            // There is no sensible default server, so fail at startup with a
            // clear message rather than at the first query with an Npgsql error.
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.ConnectionString),
                $"Featly: a PostgreSQL connection string is required. Set '{PostgresFeatlyStoreOptions.SectionName}:ConnectionString' " +
                "or pass AddFeatlyPostgresStore(o => o.ConnectionString = ...).")
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
            var opts = sp.GetRequiredService<IOptions<PostgresFeatlyStoreOptions>>().Value;
            builder.UseNpgsql(opts.ConnectionString);
        });

        services.TryAddSingleton<IChangeNotifier, InProcessChangeNotifier>();
        services.TryAddSingleton<IFlagStore, PostgresFlagStore>();
        services.TryAddSingleton<IProjectStore, PostgresProjectStore>();
        services.TryAddSingleton<IEnvironmentStore, PostgresEnvironmentStore>();
        services.TryAddSingleton<ISegmentStore, PostgresSegmentStore>();
        services.TryAddSingleton<IConfigStore, PostgresConfigStore>();
        services.TryAddSingleton<IUserStore, PostgresUserStore>();
        services.TryAddSingleton<IRoleStore, PostgresRoleStore>();
        services.TryAddSingleton<IRoleAssignmentStore, PostgresRoleAssignmentStore>();
        services.TryAddSingleton<IUserGroupStore, PostgresUserGroupStore>();
        services.TryAddSingleton<IRoleUpgradeRequestStore, PostgresRoleUpgradeRequestStore>();
        services.TryAddSingleton<IPendingChangeStore, PostgresPendingChangeStore>();
        services.TryAddSingleton<IApprovalPolicyStore, PostgresApprovalPolicyStore>();
        services.TryAddSingleton<IExperimentStore, PostgresExperimentStore>();
        services.TryAddSingleton<IEventStore, PostgresEventStore>();
        services.TryAddSingleton<IAssignmentStore, PostgresAssignmentStore>();
        services.TryAddSingleton<IWebhookStore, PostgresWebhookStore>();
        services.TryAddSingleton<IWebhookDeliveryStore, PostgresWebhookDeliveryStore>();
        services.TryAddSingleton<IAuditStore, PostgresAuditStore>();
        services.TryAddSingleton<IApiKeyStore, PostgresApiKeyStore>();
        services.TryAddSingleton<ISystemSettingsStore, PostgresSystemSettingsStore>();

        services.TryAddSingleton<PostgresFeatlyStore>();
        services.TryAddSingleton<StorageFacade>(sp => sp.GetRequiredService<PostgresFeatlyStore>());
        services.TryAddSingleton<AbstractionsMarker>(sp => sp.GetRequiredService<PostgresFeatlyStore>());

        services.AddHostedService<PostgresAutoMigrationHostedService>();

        return services;
    }
}

/// <summary>
/// Applies EF Core migrations at startup when
/// <see cref="PostgresFeatlyStoreOptions.AutoMigrate"/> is enabled. Logs the
/// outcome at <c>Information</c>; failures bubble up and stop the host so the
/// operator notices.
/// </summary>
internal sealed partial class PostgresAutoMigrationHostedService(
    IDbContextFactory<FeatlyDbContext> contextFactory,
    IOptions<PostgresFeatlyStoreOptions> options,
    ILogger<PostgresAutoMigrationHostedService> logger) : IHostedService
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

    [LoggerMessage(EventId = 3101, Level = LogLevel.Information,
        Message = "Featly PostgreSQL AutoMigrate disabled; skipping schema migration at startup.")]
    private static partial void LogAutoMigrateDisabled(ILogger logger);

    // The connection string is deliberately not logged — it carries credentials.
    [LoggerMessage(EventId = 3102, Level = LogLevel.Information,
        Message = "Applying Featly PostgreSQL migrations.")]
    private static partial void LogApplyingMigrations(ILogger logger);

    [LoggerMessage(EventId = 3103, Level = LogLevel.Information,
        Message = "Featly PostgreSQL schema is up to date.")]
    private static partial void LogSchemaUpToDate(ILogger logger);
}
