using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.MySql;

/// <summary>
/// The minimum MySQL server version this provider's SQL generation targets.
/// Pomelo needs a <c>ServerVersion</c> to know which dialect/feature set to
/// generate (window functions, native <c>JSON</c>, etc.) — unlike
/// Npgsql/Microsoft.Data.SqlClient, it cannot assume this from the connection
/// string alone.
/// </summary>
/// <remarks>
/// A fixed version (rather than <c>ServerVersion.AutoDetect(connectionString)</c>)
/// is deliberate: auto-detection means an extra round-trip to the server on
/// every context creation (or a cached-but-still-first-request connection) and
/// makes generated SQL depend on whatever server happens to be running,
/// which fails ARCHITECTURE.md principle 6 ("predictable, not magical"). MySQL
/// 8.0 is the floor for the features this provider's mapping relies on
/// (native <c>JSON</c> columns, window functions used by EF Core's own
/// pagination translation); MariaDB users on a compatible version work fine
/// against the same MySQL-dialect SQL Pomelo generates for this version.
/// </remarks>
internal static class MySqlServerVersionInfo
{
    public static readonly ServerVersion Version = new MySqlServerVersion(new Version(8, 0, 21));
}
