using Featly.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Postgres.Stores;

internal sealed class PostgresPendingChangeStore(IDbContextFactory<FeatlyDbContext> contextFactory)
    : EfPendingChangeStore<FeatlyDbContext>(contextFactory);
