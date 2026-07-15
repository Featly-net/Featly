using Featly.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Sqlite.Stores;

internal sealed class SqlitePendingChangeStore(IDbContextFactory<FeatlyDbContext> contextFactory)
    : EfPendingChangeStore<FeatlyDbContext>(contextFactory);
