# ADR-0006: EF Core for relational storage; DbContext internal

- **Status:** Accepted
- **Date:** 2026-05-25
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

Featly needs relational persistence with migrations across multiple providers (SQLite now; SQL Server / Postgres later). It must not leak storage-engine types into application code, or the server and dashboard would depend on EF Core and a provider swap would ripple everywhere (ARCHITECTURE.md §1: "Storage is an interface").

## Decision

Relational providers use **EF Core**, but the `DbContext` and all EF types are **`internal` to the storage assembly** (e.g. `Featly.Storage.Sqlite`). Application code depends only on `IFeatlyStore` and its sub-stores ([ADR-0015](0015-storage-facade.md)). The CLI's `db` commands operate through a public migration runner facade, never an EF type ([ADR-0022](0022-cli-hybrid-online-offline.md)).

## Alternatives considered

### Alternative 1 — Dapper / raw SQL

Rejected: hand-rolled migrations and mapping for a rich relational model is more error-prone than EF Core's tooling.

### Alternative 2 — EF Core with a public DbContext

Rejected: leaks EF types into consumers and couples the public surface to a storage engine; a provider swap becomes a breaking change.

## Consequences

### Positive

- First-class migrations and LINQ mapping; provider swap is contained in one assembly.
- Public surface stays storage-agnostic.

### Negative

- Tooling that needs the context (migrations, the CLI) must go through an `internal`-friendly path (design-time factory, public facade).
- `InternalsVisibleTo` is needed for the storage assembly's own tests.

### Neutral

- Each new provider re-implements the sub-stores against its own internal context.

## References

- [ADR-0015](0015-storage-facade.md) — `IFeatlyStore` facade
- ARCHITECTURE.md §3, §8 — Solution structure / storage
