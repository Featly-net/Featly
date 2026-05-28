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
        Users = new InMemoryUserStore();
        Roles = new InMemoryRoleStore();
        RoleAssignments = new InMemoryRoleAssignmentStore();
        Groups = new InMemoryUserGroupStore();
        ApiKeys = new InMemoryApiKeyStore();
        Changes = new InProcessChangeNotifier();
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
    public IUserStore Users { get; }

    /// <inheritdoc />
    public IRoleStore Roles { get; }

    /// <inheritdoc />
    public IRoleAssignmentStore RoleAssignments { get; }

    /// <inheritdoc />
    public IUserGroupStore Groups { get; }

    /// <inheritdoc />
    public IApiKeyStore ApiKeys { get; }

    /// <inheritdoc />
    public IChangeNotifier Changes { get; }
}
