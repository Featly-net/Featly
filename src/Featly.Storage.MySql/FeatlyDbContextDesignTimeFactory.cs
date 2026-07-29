using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Featly.Storage.MySql;

/// <summary>
/// Design-time factory used by the EF Core tooling
/// (<c>dotnet ef migrations add</c>, etc.) to instantiate the context outside
/// of a running host. The actual production wiring lands in the DI extension
/// added once every sub-store exists (see <see cref="FeatlyDbContext"/>'s
/// remarks); this factory is only ever called by the command-line tools and
/// does not need a reachable database — the fixed
/// <see cref="MySqlServerVersionInfo.Version"/> means Pomelo never needs to
/// connect to resolve dialect features.
/// </summary>
internal sealed class FeatlyDbContextDesignTimeFactory : IDesignTimeDbContextFactory<FeatlyDbContext>
{
    public FeatlyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FeatlyDbContext>()
            .UseMySql("Server=localhost;Database=featly-design-time;User=featly", MySqlServerVersionInfo.Version)
            .Options;
        return new FeatlyDbContext(options);
    }
}
