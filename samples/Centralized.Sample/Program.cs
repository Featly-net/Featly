using Featly.Dashboard;
using Featly.Server;
using Featly.Storage.Sqlite;

// Centralized deployment pattern: a standalone Featly server that other
// application processes point their SDK at over HTTP. Unlike SelfHosted.Sample
// (which embeds the server inside the consuming app), this host runs ONLY the
// server, dashboard, and storage — there is no SDK in this process.
//
// Point a consumer at it by setting, in the consumer's config:
//   Featly:Sdk:ServerUrl = http://localhost:5085
// The WebApi.Sample is a ready-made consumer.

var builder = WebApplication.CreateBuilder(args);

// Persistent SQLite store. In production a DBA may own the schema — set
// Featly:Storage:Sqlite:AutoMigrate=false and run `featly db migrate` from CI.
builder.Services.AddFeatlySqliteStore();

// Featly server-side services (admin + SDK HTTP APIs, auth, approval, webhooks).
builder.Services.AddFeatlyServer();

var app = builder.Build();

// Embedded dashboard UI at /featly.
app.MapFeatlyDashboard("/featly");

// Featly HTTP API: /health/live (no auth) + admin/SDK endpoints (token auth).
app.MapFeatlyApi();

// A small landing page so the root URL explains what this host is.
app.MapGet("/", () => Results.Ok(new
{
    service = "Featly.Samples.Centralized",
    role = "standalone Featly server (no SDK in this process)",
    dashboard = "/featly",
    health = "/health/live",
    firstRun = "bootstrap the first admin: `featly bootstrap-admin --identifier you@example.com --server-url http://localhost:5085`",
    consumers = "point an SDK app (e.g. WebApi.Sample) at Featly:Sdk:ServerUrl=http://localhost:5085",
}));

app.Run();

/// <summary>Marker type so test factories can find this entry point.</summary>
public partial class Program;
