# Deployment

Featly supports three deployment patterns. They share one binary set — the
difference is which packages a process references and how they are wired.

## The three patterns

### 1. Embedded (everything in your app)

The server, dashboard, and SDK all run inside your ASP.NET Core process — the
Hangfire model. Simplest to operate; the flag UI and your app share a lifecycle.

- Packages: `Featly.Server` + `Featly.Dashboard` + a storage provider, and
  optionally `Featly.Sdk` for local evaluation in the same process.
- Sample: **`samples/SelfHosted.Sample`**.

### 2. Centralized (standalone server)

A dedicated Featly server process that owns flags, the dashboard, and storage.
Other application processes point their SDK at it over HTTP. Best when several
services share one set of flags or when you want the flag system deployed and
scaled independently.

- Server process packages: `Featly.Server` + `Featly.Dashboard` + a storage provider (no SDK).
- Consumer process packages: `Featly.Sdk` only, with `UseServer("https://featly.internal", "<sdk key>")`.
- Samples: **`samples/Centralized.Sample`** (the server) + **`samples/WebApi.Sample`** (a consumer).

#### Scaling out: one writable replica for now

The server's live-update backbone (`IChangeNotifier`) is **in-process**: a
mutation applied on one replica raises the SSE change notification only for the
SDK clients connected to *that* replica. Running several centralized-server
replicas behind a load balancer therefore weakens live updates — an SDK
connected to replica A does not get the SSE nudge for a change written through
replica B. It does **not** break correctness: every SDK also polls with an ETag
(`FeatlyConfigSyncService`), so all clients converge within one polling
interval; only the "instant" push is per-replica.

Until a distributed notifier ships (Postgres `LISTEN`/`NOTIFY`, tracked in
[#179](https://github.com/Featly-net/Featly/issues/179)), run **one**
centralized-server instance, or accept polling-interval freshness across
replicas. If you do run several, use the PostgreSQL provider — SQLite's
single-writer model does not fit multiple replicas sharing one database.

The background workers are safe to run on several replicas against a shared
database: `WebhookDeliveryWorker` and `ScheduledApplyWorker` claim each due row
with a single conditional `UPDATE` before acting on it
([issue #237](https://github.com/Featly-net/Featly/issues/237)), so only one
instance wins each claim — a scheduled apply cannot race and a delivery is not
sent twice by two instances. This matters for a concurrent-writer store such as
the PostgreSQL provider; the bundled SQLite provider serializes writers anyway. (Freshness across replicas still depends on the distributed notifier
above; that is a separate concern from worker correctness.)

### 3. Consumer (SDK only)

An app that only reads flags from a remote Featly server. No server or dashboard
in-process. Evaluation is local against a cached snapshot — no network call on
the hot path; last-known-good config is served if the server is unreachable.

- Packages: `Featly.Sdk` (+ `Featly.OpenFeature.Provider` if you use OpenFeature).
- Sample: **`samples/WebApi.Sample`**.

## Production checklist

- [ ] **Secrets, not committed JSON.** Set `Featly:Server:AdminApiKey` and
      `SdkApiKey` via environment variables or a secret store. Never ship the
      `dev-*-replace-me` placeholders.
- [ ] **A real admin identity.** Run `featly bootstrap-admin --identifier you@example.com`
      once against the running server (or set `Featly:Authorization:BootstrapAdminIdentifier`).
      Then mint per-user keys with `featly apikey generate`. This makes approvals
      and the audit log attribute actions to real people.
- [ ] **Schema owned by the pipeline.** Set `Featly:Storage:Sqlite:AutoMigrate=false`
      and run `featly db migrate` from CI/CD before the new version starts.
      Check pending migrations with `featly db status`.
- [ ] **TLS.** Terminate HTTPS at the host or a reverse proxy. The dashboard
      cookie is `Secure` under TLS (`SameAsRequest` keeps local dev on HTTP working).
- [ ] **Lock down `AutoProvisionMode`.** Set `Featly:Authorization:AutoProvisionMode=Closed`
      so unknown authenticated users get no implicit access; grant roles explicitly.
- [ ] **Approval policies.** For protected environments (e.g. `production`),
      configure an `ApprovalPolicy` so mutations require review.
- [ ] **Webhook secrets.** Each webhook endpoint signs with HMAC-SHA256
      (`X-Featly-Signature`); store the per-endpoint secret and verify it on the receiver.
- [ ] **Back up the database.** `featly export` captures flag/config/segment
      definitions; the SQLite file (or your DB) captures everything.

## Schema lifecycle with the CLI

`featly db *` operates directly on the database, offline — no running server
needed (the server can't start before its schema exists). `--provider` selects
`sqlite` (default) or `postgres`; each takes its own connection string.

```bash
dotnet tool install -g Featly.Cli

# SQLite (offline, against the DB file):
featly db status        --connection-string "Data Source=/var/lib/featly/featly.db"
featly db migrate       --connection-string "Data Source=/var/lib/featly/featly.db"

# PostgreSQL (offline; --connection-string is required, there's no default server):
featly db status  --provider postgres --connection-string "Host=db;Database=featly;Username=featly;Password=..."
featly db migrate --provider postgres --connection-string "Host=db;Database=featly;Username=featly;Password=..."

# Operational tasks against the running server (online, same for either provider):
featly env lock production    --server-url https://featly.internal --api-key "$FEATLY_API_KEY"
featly export --environment production --output prod-backup.json --server-url https://featly.internal --api-key "$FEATLY_API_KEY"
```

`db` commands run **offline** (direct on the database, before the server starts);
`apikey` / `env` / `export` / `import` run **online** against the server's admin
API, reusing its permission checks, audit log, and webhooks
([ADR-0022](adr/0022-cli-hybrid-online-offline.md)).

## Storage providers

`IFeatlyStore` is an interface ([ADR-0015](adr/0015-storage-facade.md)), so the
provider is a one-line swap — nothing above the storage layer changes.

### SQLite (default)

`Featly.Storage.Sqlite` is the built-in default: ideal for the embedded
quickstart and for a single-instance centralized server.

```csharp
builder.Services.AddFeatlySqliteStore();   // Featly:Storage:Sqlite
```

### PostgreSQL

`Featly.Storage.Postgres` ([ADR-0026](adr/0026-postgres-storage-provider.md)) is
the provider for a centralized server with **several replicas sharing one
database** — SQLite's single-writer model does not fit that shape.

```csharp
builder.Services.AddFeatlyPostgresStore(o =>
    o.ConnectionString = "Host=db;Database=featly;Username=featly;Password=...");
```

Or bind it from configuration instead:

```json
{
  "Featly": {
    "Storage": {
      "Postgres": {
        "ConnectionString": "Host=db;Database=featly;Username=featly;Password=...",
        "AutoMigrate": true
      }
    }
  }
}
```

There is no default connection string — the host fails at startup with a clear
message rather than at the first query.

**Turn `AutoMigrate` off when you run more than one replica.** It defaults to
`true` (each host applies pending migrations on boot), which is what you want for
a single instance; with several replicas booting together they would each race to
migrate the same database. Apply the schema once as a deploy step with
`featly db migrate --provider postgres --connection-string "..."` (see
["Schema lifecycle with the CLI"](#schema-lifecycle-with-the-cli) below) and
start the replicas with `AutoMigrate: false`.

> **Change notifications are in-process for now.** An SSE client connected to
> replica A is not pushed a change made through replica B — it catches up on its
> next poll instead (see "Scaling out" above). The Postgres-native
> `LISTEN`/`NOTIFY` notifier that closes this is tracked in
> [#179](https://github.com/Featly-net/Featly/issues/179). Everything else —
> including the background workers, which claim rows atomically — is safe across
> replicas.

### Planned

SQL Server and a Redis cache / change pub-sub provider are designed but not
built; see [DEFERRED.md](DEFERRED.md) and [PLAN.md](../PLAN.md).

## Health checks

`GET /health/live` returns `200` with no auth when the host process can respond —
wire it to your orchestrator's liveness probe.

## Run the central server with Docker

`samples/Centralized.Sample` ships a multi-stage `Dockerfile` and a
`docker-compose.yml` so the centralized pattern is a one-command experience.
From `samples/Centralized.Sample/`:

```bash
docker compose up --build
```

This builds the image, starts the server + dashboard, and maps it to
`http://localhost:5085`:

- Dashboard: `http://localhost:5085/featly`
- Liveness: `http://localhost:5085/health/live`

SQLite is stored on a named volume (`featly-data`, mounted at `/data`), so flags
and config **persist across restarts**. `docker compose down` keeps the data;
`docker compose down -v` wipes it.

Configuration is supplied via environment variables (overriding `appsettings.json`).
Override the placeholders before exposing the server anywhere real — for example
in an `.env` file next to the compose file:

```dotenv
FEATLY_BOOTSTRAP_ADMIN=you@example.com
FEATLY_ADMIN_API_KEY=<a strong admin key>
FEATLY_SDK_API_KEY=<a strong sdk key>
```

The image runs as the non-root `app` user and publishes untrimmed (EF Core +
reflection-based JSON make trimming unsafe). To build the image directly, run
from the **repository root** (the build context the sample's project references
need):

```bash
docker build -f samples/Centralized.Sample/Dockerfile -t featly-centralized .
```
