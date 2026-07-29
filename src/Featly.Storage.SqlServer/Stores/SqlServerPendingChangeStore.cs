using Featly.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.SqlServer.Stores;

internal sealed class SqlServerPendingChangeStore(IDbContextFactory<FeatlyDbContext> contextFactory)
    : EfPendingChangeStore<FeatlyDbContext>(contextFactory);
