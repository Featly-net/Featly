using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Stores;

internal sealed class MongoProjectStore(MongoFeatlyDatabase database) : IProjectStore
{
    public async Task<Project?> GetByKeyAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return await database.Projects
            .Find(p => p.Key == key)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Project?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await database.Projects
            .Find(p => p.Id == id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    public async Task<Project?> GetDefaultAsync(CancellationToken ct) =>
        await database.Projects
            .Find(p => p.IsDefault)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken ct) =>
        await database.Projects
            .Find(Builders<Project>.Filter.Empty)
            .SortBy(p => p.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task CreateAsync(Project project, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(project);

        var keyTaken = await database.Projects.Find(p => p.Key == project.Key).AnyAsync(ct).ConfigureAwait(false);
        if (keyTaken)
        {
            throw new InvalidOperationException($"A project with key '{project.Key}' already exists.");
        }

        await database.Projects.InsertOneAsync(project, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(Project project, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(project);

        var update = Builders<Project>.Update
            .Set(p => p.Name, project.Name)
            .Set(p => p.Description, project.Description);

        var result = await database.Projects
            .UpdateOneAsync(p => p.Id == project.Id, update, cancellationToken: ct)
            .ConfigureAwait(false);

        if (result.MatchedCount == 0)
        {
            throw new InvalidOperationException($"Project '{project.Key}' not found.");
        }
    }
}
