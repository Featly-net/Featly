namespace Featly.Storage.InMemory;

/// <summary>
/// Process-local <see cref="IFeatlyStore"/> implementation. State is held in
/// thread-safe collections and is lost when the host restarts. Intended for
/// tests, demos, and ephemeral environments.
/// </summary>
public sealed class InMemoryFeatlyStore : IFeatlyStore
{
    /// <summary>Creates a fresh store with empty sub-stores.</summary>
    public InMemoryFeatlyStore()
    {
        Flags = new InMemoryFlagStore();
        Projects = new InMemoryProjectStore();
        Environments = new InMemoryEnvironmentStore();
        Segments = new InMemorySegmentStore();
        Configs = new InMemoryConfigStore();
        Changes = new InMemoryChangeNotifier();
    }

    /// <inheritdoc />
    public IFlagStore Flags { get; }

    /// <inheritdoc />
    public IProjectStore Projects { get; }

    /// <inheritdoc />
    public IEnvironmentStore Environments { get; }

    /// <inheritdoc />
    public ISegmentStore Segments { get; }

    /// <inheritdoc />
    public IConfigStore Configs { get; }

    /// <inheritdoc />
    public IChangeNotifier Changes { get; }
}
