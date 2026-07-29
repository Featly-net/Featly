using Featly.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.SqlServer.Stores;

internal sealed class SqlServerWebhookDeliveryStore(IDbContextFactory<FeatlyDbContext> contextFactory)
    : EfWebhookDeliveryStore<FeatlyDbContext>(contextFactory);
