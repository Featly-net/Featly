# ADR-0026: PostgreSQL storage provider — Npgsql, own DbContext, LISTEN/NOTIFY

- **Status:** Accepted
- **Date:** 2026-07-03
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

`IFeatlyStore` is a facade with per-entity sub-stores (ADR-0015). SQLite (ADR-0008) is the only shipped provider. ARCHITECTURE.md §7's "Provider roadmap" table already earmarks `Featly.Storage.Postgres` for "multi-node with LISTEN/NOTIFY for `IChangeNotifier`" — this ADR turns that one-line intent into a concrete, buildable design, tracked as [issue #157](https://github.com/Featly-net/Featly/issues/157).

SQLite's single-writer model is the practical ceiling for the centralized deployment pattern once it runs more than one replica (see docs/DEPLOYMENT.md's "Scaling out" note, itself a consequence of the in-process `IChangeNotifier` limitation). Postgres is the natural next storage provider: it is the most commonly requested production database for self-hosted .NET tooling, and its `LISTEN`/`NOTIFY` primitive gives `IChangeNotifier` a real cross-replica implementation instead of the SQLite provider's in-process-only one.

Three questions need answers before implementation can start:

1. Does `Featly.Storage.Postgres` reuse the existing `FeatlyDbContext` (the class in `Featly.Storage.Sqlite`, currently `internal` to that project) or define its own?
2. What does `IChangeNotifier` do on Postgres — `LISTEN`/`NOTIFY`, or the same in-process notifier the SQLite provider uses (deferring the cross-replica win)?
3. How are provider-specific column mappings (JSON storage, `DateTimeOffset` ticks vs. `timestamptz`) handled without duplicating every `IEntityTypeConfiguration<T>` verbatim?

## Decision

`Featly.Storage.Postgres` is a new project, independent from `Featly.Storage.Sqlite`, targeting `net10.0` only (matching every other storage/server/CLI project — ADR-0007). It defines **its own internal `FeatlyDbContext`**, not a shared/reused one: despite ARCHITECTURE.md §7's "The `FeatlyDbContext` is shared" phrasing (written before any second provider existed), a shared context class would force one of the two providers to carry column-mapping compromises it doesn't need (SQLite's `DateTimeOffsetTicksConverter` has no reason to exist against a native `timestamptz` column; Postgres's native `jsonb` has no reason to be serialized through the SQLite provider's raw-JSON-text converter). Each provider owns a `DbContext` scoped to its own assembly and `internal`, exactly like `Featly.Storage.Sqlite` today — this ADR corrects the aspirational §7 text to match that reality.

**What *is* shared** to avoid duplicating the domain-to-column mapping logic twice: the entity *shape* (which properties exist, what's nullable, what's owned/JSON) is expressed once, in prose, by the domain types in `Featly.Abstractions` — both providers' `IEntityTypeConfiguration<T>` implementations independently encode that same shape using provider-native column types. This is intentional duplication (each configuration file is ~20-40 lines, and provider-native types are exactly the reason to write them separately) rather than a shared base class fighting both providers' idioms.

`IChangeNotifier` for Postgres uses **`LISTEN`/`NOTIFY`**: `NotifyAsync` issues `NOTIFY featly_changes, '<payload>'`, and a background listener (a dedicated, long-lived `NpgsqlConnection` executing `LISTEN featly_changes`) fans incoming notifications out to the same in-process subscriber list the SQLite provider's notifier already maintains. This gives every replica of a centralized Postgres-backed deployment the "instant push" that a SQLite deployment can only get within one process — closing the exact gap docs/DEPLOYMENT.md's "Scaling out" note calls out. The 7,999-byte payload limit `NOTIFY` imposes is not a constraint here: `ChangeNotification` already carries only `(EntityType, EntityKey, EnvironmentId, UpdatedAt)` — small fixed fields — never a full entity body.

Migrations follow the standard EF Core multi-provider pattern: `Featly.Storage.Postgres/Migrations/` is a separate migrations history from `Featly.Storage.Sqlite/Migrations/`, generated against the Postgres-specific `FeatlyDbContext`, with its own `SqliteMigrationRunner`-equivalent facade (`PostgresMigrationRunner`) exposed for `Featly.Cli`'s `db` command group.

## Alternatives considered

### Alternative 1 — one shared `FeatlyDbContext` + `OnModelCreating` provider branching

A single `DbContext` in a new `Featly.Storage.Relational` (or similar) project, with `IEntityTypeConfiguration<T>` implementations that branch on `Database.ProviderName` for column-type differences (e.g. `HasConversion` only when SQLite). Rejected: it makes every future entity change a two-provider-aware edit in one file, defeats the "storage providers are independent packages, consumers pay only for what they reference" principle (ARCHITECTURE.md §1/Dependency rules — a shared project would make `Featly.Storage.Sqlite` and `Featly.Storage.Postgres` co-versioned and co-deployed), and the branching logic itself becomes exactly the kind of "compile-time reflection trick" principle 6 warns against.

### Alternative 2 — polling-only `IChangeNotifier` for Postgres (defer `LISTEN`/`NOTIFY`)

Ship the Postgres provider with the same in-process notifier the SQLite provider uses, leaving `LISTEN`/`NOTIFY` for a follow-up. Rejected as the *default*: the entire point of choosing Postgres over SQLite for a production deployment is multi-replica, and shipping a multi-replica-capable storage engine with a notifier that still doesn't fan out across replicas would repeat the exact gap this ADR exists to close. `LISTEN`/`NOTIFY` is a well-understood, low-risk primitive (single dedicated connection, small payload) — there is no correctness reason to defer it, only an effort one, and the effort is modest.

### Alternative 3 — Dapper or hand-rolled SQL instead of EF Core

Rejected for consistency with ADR-0006 (EF Core for relational storage) — the whole point of that decision was to keep `FeatlyDbContext` swappable without consumer-visible change; introducing a different data-access technology for a second provider would violate the reasoning ADR-0006 already established, for no benefit specific to Postgres.

## Consequences

### Positive

- `services.AddFeatlyPostgresStore(...)` is a drop-in replacement for `AddFeatlySqliteStore(...)` — `Featly.Server` and `Featly.Dashboard` never reference `Npgsql` or any Postgres-specific type (ARCHITECTURE.md principle 4).
- Multi-replica centralized deployments get real cross-replica live updates via `LISTEN`/`NOTIFY`, closing the docs/DEPLOYMENT.md "Scaling out" gap for operators who choose Postgres.
- Each provider's migrations, `DbContext`, and configurations evolve independently — a Postgres-specific optimization never risks a SQLite regression and vice versa.

### Negative

- Real duplication: every entity gets two `IEntityTypeConfiguration<T>` files (one per provider) instead of one. Accepted as the cost of provider-native column types (see Decision).
- A second migrations history to keep in sync feature-for-feature with the SQLite one; a PR that adds a column must remember to touch both providers (mitigated by the `Featly.Storage.Sqlite.Tests`-mirroring test suite planned in issue #157's PR 5 — a missing Postgres migration shows up as a failing round-trip test, not a silent gap).
- `LISTEN`/`NOTIFY` needs a dedicated, always-open `NpgsqlConnection` per server instance (outside the pooled `IDbContextFactory` connections used for everything else) — one more thing to reason about in connection-limit sizing for constrained Postgres plans.

### Neutral

- CI needs a real Postgres to test against (a service container in `ci.yml`, per issue #157's PR 5) — this is new CI surface, not a design cost, but worth calling out since every other provider's tests run against a throwaway file or in-memory dictionary.

## Implementation notes

Sliced into PRs in [issue #157](https://github.com/Featly-net/Featly/issues/157) (entity porting) and [issue #179](https://github.com/Featly-net/Featly/issues/179) (making the provider usable):

1. Project scaffold + core entities (`Project`, `Environment`, `Flag`) + initial migration. **Shipped.**
2. Remaining entities (configs, segments, experiments, RBAC, approvals, webhooks, audit, settings). **Shipped.**
3. `IFeatlyStore` facade + `AddFeatlyPostgresStore()` DI. **Shipped** (#256).
4. `Featly.Cli` `db --provider postgres` support. **Shipped** (#259).
5. `LISTEN`/`NOTIFY` `IChangeNotifier` implementation, exactly as designed above: `PostgresChangeNotifier` (publish via `pg_notify`, local fan-out through the same `InProcessChangeNotifier` every provider uses) + `PostgresChangeListenerHostedService` (the dedicated `NpgsqlConnection`, reconnect with exponential backoff). **Shipped** (#258). One refinement over the original design: `NotifyAsync` does not fan out to local subscribers directly — delivery happens exclusively through the `LISTEN` round-trip, so the replica that raised a change hears it back the same symmetric way every other replica does, with nothing to deduplicate.
6. Test suite mirroring `Featly.Storage.Sqlite.Tests`, running against a real Postgres in CI. **Shipped.**

## References

- ARCHITECTURE.md §7 (Storage layer, "Provider roadmap")
- ARCHITECTURE.md §14 (Notifications)
- [ADR-0006](0006-ef-core-internal-dbcontext.md) — EF Core for relational storage; DbContext internal
- [ADR-0008](0008-sqlite-default-storage.md) — SQLite as default storage
- [ADR-0015](0015-storage-facade.md) — `IFeatlyStore` as facade
- [PostgreSQL: `NOTIFY`](https://www.postgresql.org/docs/current/sql-notify.html)
- [Npgsql: Listen/Notify](https://www.npgsql.org/doc/features/listen-notify.html)
- [GitHub issue #157](https://github.com/Featly-net/Featly/issues/157)
