using Featly.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Sqlite.Stores;

internal sealed class SqliteWebhookDeliveryStore(IDbContextFactory<FeatlyDbContext> contextFactory)
    : EfWebhookDeliveryStore<FeatlyDbContext>(contextFactory);
