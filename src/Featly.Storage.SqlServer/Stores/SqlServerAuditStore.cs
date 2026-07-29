using Featly.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.SqlServer.Stores;

internal sealed class SqlServerAuditStore(IDbContextFactory<FeatlyDbContext> contextFactory)
    : EfAuditStore<FeatlyDbContext>(contextFactory);
