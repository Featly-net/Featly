using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Featly.Storage.Sqlite;

/// <summary>
/// Design-time factory used by the EF Core tooling
/// (<c>dotnet ef migrations add</c>, etc.) to instantiate the context outside
/// of a running host. The actual production wiring lives in
/// <c>AddFeatlySqliteStore</c>; this factory is only ever called by the
/// command-line tools.
/// </summary>
internal sealed class FeatlyDbContextDesignTimeFactory : IDesignTimeDbContextFactory<FeatlyDbContext>
{
    public FeatlyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FeatlyDbContext>()
            // Point at a throwaway path; the connection is only used to
            // resolve the SQLite provider, not to actually open a database.
            .UseSqlite("Data Source=design-time.db")
            .Options;
        return new FeatlyDbContext(options);
    }
}
