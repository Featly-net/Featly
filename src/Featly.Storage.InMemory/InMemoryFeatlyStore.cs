namespace Featly.Storage.InMemory;

/// <summary>
/// In-memory implementation of <see cref="IFeatlyStore"/>. State is process-local,
/// not persisted, and reset whenever the host restarts. Intended for tests,
/// demos, and ephemeral environments.
/// </summary>
/// <remarks>
/// No-op placeholder for M1. Sub-store implementations land alongside the
/// corresponding feature milestones (M2 flags, M4 configs, ...).
/// </remarks>
public sealed class InMemoryFeatlyStore : IFeatlyStore
{
}
