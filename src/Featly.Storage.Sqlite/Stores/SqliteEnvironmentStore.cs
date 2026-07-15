using Featly.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Sqlite.Stores;

internal sealed class SqliteEnvironmentStore(IDbContextFactory<FeatlyDbContext> contextFactory)
    : EfEnvironmentStore<FeatlyDbContext>(contextFactory);
