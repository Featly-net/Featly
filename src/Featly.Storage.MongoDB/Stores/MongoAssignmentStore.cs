using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Stores;

internal sealed class MongoAssignmentStore(MongoFeatlyDatabase database) : IAssignmentStore
{
    public async Task<Assignment?> GetAsync(Guid experimentId, string subjectKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectKey);
        return await database.Assignments
            .Find(a => a.ExperimentId == experimentId && a.SubjectKey == subjectKey)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task UpsertAsync(Assignment assignment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        // First write wins — a subject's assignment never changes once
        // recorded. The unique (ExperimentId, SubjectKey) index rejects a
        // concurrent writer's duplicate; the already-persisted assignment
        // stands.
        try
        {
            await database.Assignments.InsertOneAsync(assignment, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
        }
    }

    public async Task<IReadOnlyList<Assignment>> ListByExperimentAsync(Guid experimentId, CancellationToken ct) =>
        await database.Assignments
            .Find(a => a.ExperimentId == experimentId)
            .SortBy(a => a.AssignedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
}
