using Featly.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Postgres.Stores;

internal sealed class PostgresEnvironmentStore(IDbContextFactory<FeatlyDbContext> contextFactory)
    : EfEnvironmentStore<FeatlyDbContext>(contextFactory);
