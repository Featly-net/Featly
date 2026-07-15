using Featly.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Postgres.Stores;

internal sealed class PostgresWebhookDeliveryStore(IDbContextFactory<FeatlyDbContext> contextFactory)
    : EfWebhookDeliveryStore<FeatlyDbContext>(contextFactory);
