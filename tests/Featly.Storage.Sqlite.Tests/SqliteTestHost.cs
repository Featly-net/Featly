using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Featly.Storage.Sqlite;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Storage.Sqlite.Tests;

/// <summary>
/// Spins up a Featly SQLite store backed by a unique temp file, applies
/// migrations via the same hosted service the production wiring uses, and
/// exposes the storage facade. Disposing tears the host down and deletes the
/// database file.
/// </summary>
internal sealed class SqliteTestHost : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly string _dbPath;

    private SqliteTestHost(IHost host, string dbPath)
    {
        _host = host;
        _dbPath = dbPath;
    }

    public StorageFacade Store => _host.Services.GetRequiredService<StorageFacade>();

    public IServiceProvider Services => _host.Services;

    public string ConnectionString => $"Data Source={_dbPath}";

    public static async Task<SqliteTestHost> CreateAsync(CancellationToken ct = default)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"featly-sqlite-test-{Guid.NewGuid():N}.db");

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddFeatlySqliteStore(opts =>
                {
                    opts.ConnectionString = $"Data Source={dbPath}";
                    opts.AutoMigrate = true;
                });
            })
            .Build();

        await host.StartAsync(ct).ConfigureAwait(false);
        return new SqliteTestHost(host, dbPath);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync().ConfigureAwait(false);
        _host.Dispose();

        // The pooled DbContext factory may still hold connections briefly;
        // ignore deletion errors so tests don't fail on transient locks.
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }
}
