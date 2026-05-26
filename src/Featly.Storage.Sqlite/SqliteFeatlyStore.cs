namespace Featly.Storage.Sqlite;

/// <summary>
/// SQLite-backed implementation of <see cref="IFeatlyStore"/>. Aggregates
/// per-entity sub-stores. Created and disposed by the DI container; sub-stores
/// open per-operation <see cref="FeatlyDbContext"/> instances via the
/// registered <c>IDbContextFactory&lt;FeatlyDbContext&gt;</c>, so the facade
/// itself is safe to use as a singleton.
/// </summary>
internal sealed class SqliteFeatlyStore(
    IFlagStore flags,
    IProjectStore projects,
    IEnvironmentStore environments,
    ISegmentStore segments,
    IConfigStore configs,
    IChangeNotifier changes) : IFeatlyStore
{
    public IFlagStore Flags { get; } = flags;

    public IProjectStore Projects { get; } = projects;

    public IEnvironmentStore Environments { get; } = environments;

    public ISegmentStore Segments { get; } = segments;

    public IConfigStore Configs { get; } = configs;

    public IChangeNotifier Changes { get; } = changes;
}
