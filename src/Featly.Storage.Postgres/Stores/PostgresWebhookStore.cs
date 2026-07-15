using Featly.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Postgres.Stores;

internal sealed class PostgresWebhookStore(IDbContextFactory<FeatlyDbContext> contextFactory)
    : EfWebhookStore<FeatlyDbContext>(contextFactory);
