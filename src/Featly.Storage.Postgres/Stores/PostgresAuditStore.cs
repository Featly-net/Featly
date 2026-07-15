using Featly.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Postgres.Stores;

internal sealed class PostgresAuditStore(IDbContextFactory<FeatlyDbContext> contextFactory)
    : EfAuditStore<FeatlyDbContext>(contextFactory);
