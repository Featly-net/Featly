using Featly.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.MySql.Stores;

internal sealed class MySqlEnvironmentStore(IDbContextFactory<FeatlyDbContext> contextFactory)
    : EfEnvironmentStore<FeatlyDbContext>(contextFactory);
