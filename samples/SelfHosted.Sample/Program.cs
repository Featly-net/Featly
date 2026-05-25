using Featly.Dashboard;
using Featly.Server;
using Featly.Storage.Sqlite;

var builder = WebApplication.CreateBuilder(args);

// Storage. SQLite is the Hangfire-style quickstart default: a single .db file
// in the host's content root, schema applied automatically on first boot.
// Options can also be bound from appsettings under "Featly:Storage:Sqlite".
//
// To run with no persistence (tests, demos, ephemeral hosts), swap with:
//   using Featly.Storage.InMemory;
//   builder.Services.AddFeatlyInMemoryStore();
builder.Services.AddFeatlySqliteStore();

// Featly server-side services.
builder.Services.AddFeatlyServer();

var app = builder.Build();

// Dashboard UI at /featly. Placeholder until M5.
app.MapFeatlyDashboard("/featly");

// Featly HTTP API: /health/live (no auth) + admin/SDK endpoints (token auth).
app.MapFeatlyApi();

app.Run();

/// <summary>
/// Marker type so the WebApplicationFactory in E2E tests can find this entry point.
/// </summary>
public partial class Program;
