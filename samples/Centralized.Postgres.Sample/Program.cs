using Featly.Dashboard;
using Featly.Server;
using Featly.Server.Telemetry;
using Featly.Storage.Postgres;

// Centralized deployment pattern, PostgreSQL-backed: unlike Centralized.Sample
// (SQLite, single writable replica), this host is designed to run as several
// replicas sharing one Postgres database — see docker-compose.yml, which starts
// two of these behind a load balancer. A change written through one replica is
// pushed to every replica's SSE clients via Postgres LISTEN/NOTIFY (ADR-0026,
// issue #258), not just the replica that made it.
//
// Point a consumer at either replica (or the load balancer) by setting, in the
// consumer's config:
//   Featly:Sdk:ServerUrl = http://localhost:5086
// The WebApi.Sample is a ready-made consumer.

var builder = WebApplication.CreateBuilder(args);

// PostgreSQL store shared by every replica. AutoMigrate is off here — see
// docker-compose.yml's dedicated one-shot "migrate" service, which applies the
// schema once before any replica starts, per docs/DEPLOYMENT.md's "Turn
// AutoMigrate off when you run more than one replica" guidance.
builder.Services.AddFeatlyPostgresStore();

// Featly server-side services (admin + SDK HTTP APIs, auth, approval, webhooks).
builder.Services.AddFeatlyServer();

// OpenTelemetry traces + metrics for the centralized server. Off unless
// Featly:Telemetry:Enabled=true; exports HTTP spans and Featly's custom counters
// over OTLP when enabled. See appsettings.json for the (disabled) block.
builder.Services.AddFeatlyServerTelemetry(builder.Configuration);

var app = builder.Build();

// Embedded dashboard UI at /featly.
app.MapFeatlyDashboard("/featly");

// Featly HTTP API: /health/live (no auth) + admin/SDK endpoints (token auth).
app.MapFeatlyApi();

// A small landing page so the root URL explains what this host (and which
// replica, per REPLICA_NAME) is.
var replicaName = builder.Configuration["REPLICA_NAME"] ?? Environment.MachineName;
app.MapGet("/", () => Results.Ok(new
{
    service = "Featly.Samples.CentralizedPostgres",
    replica = replicaName,
    role = "standalone Featly server, one of several replicas sharing one Postgres database",
    dashboard = "/featly",
    health = "/health/live",
    firstRun = "bootstrap the first admin: `featly bootstrap-admin --identifier you@example.com --server-url http://localhost:5086`",
    consumers = "point an SDK app (e.g. WebApi.Sample) at Featly:Sdk:ServerUrl=http://localhost:5086",
}));

app.Run();

/// <summary>Marker type so test factories can find this entry point.</summary>
public partial class Program;
