using Featly.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Sqlite.Stores;

internal sealed class SqliteWebhookStore(IDbContextFactory<FeatlyDbContext> contextFactory)
    : EfWebhookStore<FeatlyDbContext>(contextFactory);
