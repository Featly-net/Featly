using Featly.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.SqlServer.Stores;

internal sealed class SqlServerWebhookStore(IDbContextFactory<FeatlyDbContext> contextFactory)
    : EfWebhookStore<FeatlyDbContext>(contextFactory);
