# ADR-0008: SQLite as the default storage in the embedded quickstart

- **Status:** Accepted
- **Date:** 2026-05-25
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

The embedded quickstart must be operational with near-zero setup — no separate database server to provision before a developer can create their first flag (the Hangfire "it just works" feel). At the same time, larger deployments need a real server-class database.

## Decision

**SQLite is the default storage provider** (`Featly.Storage.Sqlite`): a single `featly.db` file in the host's content root, schema applied automatically on first boot. It is the one-line default in `AddFeatlySqliteStore()`. Server-class providers (SQL Server, Postgres) are designed behind the same `IFeatlyStore` facade and deferred to post-1.0; an in-memory provider exists for tests.

## Alternatives considered

### Alternative 1 — require SQL Server / Postgres from day one

Rejected: forces infrastructure setup before the quickstart works, defeating the embedded experience.

### Alternative 2 — in-memory by default

Rejected: data vanishes on restart; surprising for anyone who creates a flag and expects it to persist.

## Consequences

### Positive

- Zero-infrastructure quickstart; a file is the database.
- Same facade means upgrading to a server DB later is a one-line provider swap.

### Negative

- SQLite's write concurrency suits small/medium deployments, not high-write central servers — hence the planned SQL Server/Postgres providers.

### Neutral

- The connection string and `AutoMigrate` are bootstrap-only settings ([ADR-0016](0016-database-overrides-appsettings.md)).

## References

- ARCHITECTURE.md §8 — Storage; §1 — "Embedded like Hangfire"
- [DEFERRED.md](../DEFERRED.md) — post-1.0 storage providers
