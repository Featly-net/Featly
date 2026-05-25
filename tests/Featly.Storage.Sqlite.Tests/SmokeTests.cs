using FluentAssertions;
using Featly.Storage.Sqlite;
using Xunit;

namespace Featly.Storage.Sqlite.Tests;

public class SmokeTests
{
    [Fact]
    public void SqliteStorage_Assembly_Is_Reachable()
    {
        // Placeholder for M1. Real EF Core + migrations tests land in M2.
        var assembly = typeof(SqliteStorageMarker).Assembly;
        assembly.Should().NotBeNull();
    }
}
