using Featly.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.MySql.Stores;

internal sealed class MySqlWebhookStore(IDbContextFactory<FeatlyDbContext> contextFactory)
    : EfWebhookStore<FeatlyDbContext>(contextFactory);
