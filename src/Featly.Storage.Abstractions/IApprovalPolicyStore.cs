namespace Featly.Storage;

/// <summary>
/// Persistence operations for <see cref="ApprovalPolicy"/> entities. At most one
/// policy exists per environment; <see cref="ApproverRule"/> rows are stored
/// inline (owned collection).
/// </summary>
public interface IApprovalPolicyStore
{
    /// <summary>Returns the policy governing the given environment, or <c>null</c> if none is configured.</summary>
    Task<ApprovalPolicy?> GetByEnvironmentAsync(Guid environmentId, CancellationToken ct);

    /// <summary>
    /// Inserts or replaces the policy for its <see cref="ApprovalPolicy.EnvironmentId"/>.
    /// Environment is the natural key — the row id stays stable across updates.
    /// </summary>
    Task UpsertAsync(ApprovalPolicy policy, CancellationToken ct);

    /// <summary>Removes the policy for an environment, if any. Idempotent.</summary>
    Task DeleteByEnvironmentAsync(Guid environmentId, CancellationToken ct);
}
