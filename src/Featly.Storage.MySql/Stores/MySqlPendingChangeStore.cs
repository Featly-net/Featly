using Featly.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.MySql.Stores;

internal sealed class MySqlPendingChangeStore(IDbContextFactory<FeatlyDbContext> contextFactory)
    : EfPendingChangeStore<FeatlyDbContext>(contextFactory);
