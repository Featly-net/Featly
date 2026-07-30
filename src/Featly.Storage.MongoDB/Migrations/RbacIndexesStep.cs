using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Migrations;

/// <summary>
/// Third migration step (PR 3, issue #277): indexes for the RBAC entities.
/// </summary>
internal sealed class RbacIndexesStep : IMongoMigrationStep
{
    public string Name => "0003_rbac_indexes";

    public async Task ApplyAsync(IMongoDatabase database, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(database);

        var users = database.GetCollection<User>(MongoCollectionNames.Users);
        await users.Indexes.CreateOneAsync(
            new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(u => u.Identifier),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: ct).ConfigureAwait(false);

        var roles = database.GetCollection<Role>(MongoCollectionNames.Roles);
        await roles.Indexes.CreateOneAsync(
            new CreateIndexModel<Role>(
                Builders<Role>.IndexKeys.Ascending(r => r.Key),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: ct).ConfigureAwait(false);

        var userGroups = database.GetCollection<UserGroup>(MongoCollectionNames.UserGroups);
        await userGroups.Indexes.CreateOneAsync(
            new CreateIndexModel<UserGroup>(
                Builders<UserGroup>.IndexKeys.Ascending(g => g.Key),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: ct).ConfigureAwait(false);

        var roleAssignments = database.GetCollection<RoleAssignment>(MongoCollectionNames.RoleAssignments);
        await roleAssignments.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<RoleAssignment>(Builders<RoleAssignment>.IndexKeys.Ascending(a => a.AssigneeId)),
                new CreateIndexModel<RoleAssignment>(Builders<RoleAssignment>.IndexKeys.Ascending(a => a.ProjectId)),
            ],
            cancellationToken: ct).ConfigureAwait(false);

        var roleUpgradeRequests = database.GetCollection<RoleUpgradeRequest>(MongoCollectionNames.RoleUpgradeRequests);
        await roleUpgradeRequests.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<RoleUpgradeRequest>(Builders<RoleUpgradeRequest>.IndexKeys.Ascending(r => r.Status)),
                new CreateIndexModel<RoleUpgradeRequest>(Builders<RoleUpgradeRequest>.IndexKeys.Ascending(r => r.UserId)),
            ],
            cancellationToken: ct).ConfigureAwait(false);
    }
}
