using Featly.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Sqlite.Stores;

internal sealed class SqliteAuditStore(IDbContextFactory<FeatlyDbContext> contextFactory)
    : EfAuditStore<FeatlyDbContext>(contextFactory);
