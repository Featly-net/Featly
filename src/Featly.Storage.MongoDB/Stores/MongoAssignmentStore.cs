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
        // recorded. $setOnInsert + upsert is atomic: a concurrent duplicate
        // call is a no-op against the already-persisted row rather than a
        // race to catch, unlike a plain InsertOneAsync against the unique
        // (ExperimentId, SubjectKey) index.
        var filter = Builders<Assignment>.Filter.Where(a =>
            a.ExperimentId == assignment.ExperimentId && a.SubjectKey == assignment.SubjectKey);
        var update = Builders<Assignment>.Update
            .SetOnInsert(a => a.Id, assignment.Id)
            .SetOnInsert(a => a.ExperimentId, assignment.ExperimentId)
            .SetOnInsert(a => a.SubjectKey, assignment.SubjectKey)
            .SetOnInsert(a => a.VariantKey, assignment.VariantKey)
            .SetOnInsert(a => a.AssignedAt, assignment.AssignedAt);

        await database.Assignments
            .UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Assignment>> ListByExperimentAsync(Guid experimentId, CancellationToken ct) =>
        await database.Assignments
            .Find(a => a.ExperimentId == experimentId)
            .SortBy(a => a.AssignedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
}
