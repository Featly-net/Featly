using Featly.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.MySql.Stores;

internal sealed class MySqlAuditStore(IDbContextFactory<FeatlyDbContext> contextFactory)
    : EfAuditStore<FeatlyDbContext>(contextFactory);
