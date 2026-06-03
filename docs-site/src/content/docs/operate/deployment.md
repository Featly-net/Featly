---
title: Deployment
description: Featly's three deployment patterns and a production checklist.
---

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

```bash
dotnet tool install -g Featly.Cli

# Before deploying a new version (offline, against the DB file / connection string):
featly db status        --connection-string "Data Source=/var/lib/featly/featly.db"
featly db migrate       --connection-string "Data Source=/var/lib/featly/featly.db"

# Operational tasks against the running server (online):
featly env lock production    --server-url https://featly.internal --api-key "$FEATLY_API_KEY"
featly export --environment production --output prod-backup.json --server-url https://featly.internal --api-key "$FEATLY_API_KEY"
```

`db` commands run **offline** (direct on the database, before the server starts);
`apikey` / `env` / `export` / `import` run **online** against the server's admin
API, reusing its permission checks, audit log, and webhooks
([ADR-0022](https://github.com/Featly-net/Featly/blob/main/docs/adr/0022-cli-hybrid-online-offline.md)).
See the [CLI reference](/Featly/operate/cli/) for the full command tree.

## Storage providers

SQLite is the built-in default (`Featly.Storage.Sqlite`), ideal for the embedded
quickstart and small/medium centralized deployments. `IFeatlyStore` is an
interface ([ADR-0015](https://github.com/Featly-net/Featly/blob/main/docs/adr/0015-storage-facade.md));
SQL Server, Postgres, and Redis providers are designed but deferred until after
v0.1.0.

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
