using Featly.Dashboard;
using Featly.Server;
using Featly.Storage.InMemory;

var builder = WebApplication.CreateBuilder(args);

// Storage. M2 swaps this for SQLite in real deployments.
builder.Services.AddFeatlyInMemoryStore();

// Featly server-side services.
builder.Services.AddFeatlyServer();

var app = builder.Build();

// Dashboard UI at /featly. Placeholder until M5.
app.MapFeatlyDashboard("/featly");

// Featly HTTP API: only /health/live in M1; admin and SDK endpoints land in M2.
app.MapFeatlyApi();

app.Run();

/// <summary>
/// Marker type so the WebApplicationFactory in E2E tests can find this entry point.
/// </summary>
public partial class Program;
