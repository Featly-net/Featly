# ADR-0022: CLI is hybrid — `db` commands offline, admin commands online over HTTP

- **Status:** Accepted
- **Date:** 2026-05-29
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

The `featly` global tool exposes two kinds of operation: schema management (`db migrate/status/rollback/drop`) and administrative actions (`apikey generate`, `bootstrap-admin`, `env lock/unlock`, `export`, `import`). Schema management must run *before* the server can start (the server needs a migrated schema). Administrative actions operate on a live system where the server already enforces permissions, writes audit entries, and fires webhooks. Routing schema commands through a running server is impossible (chicken-and-egg); routing admin commands directly at the database would bypass all of the server's safeguards.

## Decision

The CLI is **hybrid**:

- **`db` commands run OFFLINE** — directly against the database through a public per-provider migration runner (`SqliteMigrationRunner`, `PostgresMigrationRunner`) keeping each `FeatlyDbContext` internal ([ADR-0006](0006-ef-core-internal-dbcontext.md)). `--provider sqlite|postgres` (default `sqlite`) selects which; connection string from `--connection-string` / a per-provider environment variable (`FEATLY_SQLITE` / `FEATLY_POSTGRES`) / a default for SQLite only — Postgres has none and fails fast, mirroring `AddFeatlyPostgresStore()` (issue #257).
- **All other admin commands run ONLINE** — against the server's admin HTTP API (`--server-url` / `FEATLY_SERVER_URL`, `--api-key` / `FEATLY_API_KEY`). They therefore reuse the server's permission checks, audit log, and webhook backbone. The one exception is `bootstrap-admin`, which hits an unauthenticated, self-guarded endpoint (it exists precisely to create the first credential).

## Alternatives considered

### Alternative 1 — everything offline (direct DB)

Rejected: admin mutations would bypass permission checks, audit, and webhooks, and would require the CLI to reimplement Argon2 hashing and identity logic that lives server-side.

### Alternative 2 — everything online

Rejected: `db migrate` cannot go through a server that can't start without a migrated schema.

## Consequences

### Positive

- Each command runs where it must: schema before boot, admin actions through the live server's safeguards.
- The CLI never reimplements server logic (hashing, RBAC); it calls the API.

### Negative

- Two connection models (a DB connection string vs a server URL + key) the operator must understand.
- Admin commands need a running server and a credential.

### Neutral

- `db` is the only offline group; everything else is HTTP.

## References

- [DEPLOYMENT.md](../DEPLOYMENT.md), [CONFIGURATION.md](../CONFIGURATION.md)
- `SqliteMigrationRunner`, `AdminApiClient`
