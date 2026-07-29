using Featly.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.SqlServer.Stores;

internal sealed class SqlServerEnvironmentStore(IDbContextFactory<FeatlyDbContext> contextFactory)
    : EfEnvironmentStore<FeatlyDbContext>(contextFactory);
