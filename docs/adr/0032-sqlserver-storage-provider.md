# ADR-0032: SQL Server storage provider — own DbContext, polling-only `IChangeNotifier`

- **Status:** Accepted
- **Date:** 2026-07-29
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

`IFeatlyStore` is a facade with per-entity sub-stores (ADR-0015). SQLite (ADR-0008)
and PostgreSQL (ADR-0026) are the two shipped providers. ARCHITECTURE.md §7's
"Provider roadmap" table already earmarks `Featly.Storage.SqlServer` as
"Planned" for "Enterprise self-hosted, multi-node" deployments — the most
commonly requested database in .NET shops that are not already on Postgres,
and the provider issue #157 (Postgres) and `docs/DEFERRED.md` both flagged as
the next relational provider once Postgres shipped.

Three questions, mirroring ADR-0026's structure:

1. Does `Featly.Storage.SqlServer` reuse `Featly.Storage.Postgres`'s
   `FeatlyDbContext`, or define its own?
2. What does `IChangeNotifier` do on SQL Server? Postgres has `LISTEN`/`NOTIFY`
   as a first-class primitive; SQL Server's closest equivalents are
   `SqlDependency`/Query Notifications and Service Broker, both built on
   Service Broker plumbing that must be explicitly enabled per database, has a
   well-documented history of connection-pool interaction problems, and is
   unsupported on several Azure SQL Database tiers. Is that complexity and
   operational risk worth taking on for a v1 provider?
3. How are provider-specific column mappings (JSON storage, `DateTimeOffset`
   representation) handled without duplicating every
   `IEntityTypeConfiguration<T>` verbatim across three relational providers now?

## Decision

`Featly.Storage.SqlServer` is a new project, independent from
`Featly.Storage.Sqlite` and `Featly.Storage.Postgres`, targeting `net10.0` only
(ADR-0007), using `Microsoft.EntityFrameworkCore.SqlServer` (which brings in
`Microsoft.Data.SqlClient`) — consistent with ADR-0006's EF-Core-for-relational-
storage decision. It defines **its own internal `FeatlyDbContext`**, for the
same reason ADR-0026 gave for Postgres not reusing SQLite's: a shared context
would force column-mapping compromises no single provider needs (SQL Server's
native `datetimeoffset` has no reason to route through SQLite's ticks
converter or share a JSON-column strategy with Postgres's native `jsonb`).
Entity *shape* stays the single source of truth in `Featly.Abstractions`; each
provider's `IEntityTypeConfiguration<T>` independently encodes that shape with
provider-native column types — the same intentional duplication ADR-0026
accepted, now paid a third time.

`IChangeNotifier` for SQL Server ships **polling-only** (the same in-process
notifier `Featly.Storage.Sqlite` and `Featly.Storage.InMemory` already use, via
`InProcessChangeNotifier`) — **not** Service Broker / `SqlDependency`. This is
the opposite call from ADR-0026, which treated `LISTEN`/`NOTIFY` as low-risk
and non-optional. Service Broker is a different order of complexity: it
requires `ALTER DATABASE ... SET ENABLE_BROKER`, is unavailable or restricted
on some Azure SQL Database service tiers, needs its own queue/service/contract
objects provisioned per deployment, and `SqlDependency`'s connection-affinity
model has a long history of silent failure modes in pooled-connection ASP.NET
Core apps. Taking that on for a v1 provider fails ARCHITECTURE.md principle 6
("predictable, not magical") for a win that is only the "instant push" case —
correctness does not depend on it: every SDK already converges via the ETag
poll (`FeatlyConfigSyncService`), exactly as documented for SQLite's
in-process-only notifier in `docs/DEPLOYMENT.md`'s "Scaling out" section. SQL
Server joins SQLite there: a multi-replica SQL-Server-backed deployment gets
correct, eventually-consistent updates, just not the instant per-replica SSE
push Postgres gets. This is called out explicitly in `docs/DEPLOYMENT.md`
rather than left as a silent gap — operators who need cross-replica instant
push should choose the Postgres provider, matching that document's existing
recommendation for the same reason.

Migrations follow the established multi-provider pattern: a separate
`Featly.Storage.SqlServer/Migrations/` history generated against this
provider's own `FeatlyDbContext`, with a `SqlServerMigrationRunner` facade for
`Featly.Cli`'s `db` command group (`--provider sqlserver`).

## Alternatives considered

### Alternative 1 — Service Broker / `SqlDependency`-backed `IChangeNotifier`

Considered, to match Postgres's cross-replica push. Rejected for v1: the
operational cost (explicit Service Broker enablement, queue/service/contract
provisioning, restricted availability on Azure SQL Database, documented
`SqlDependency` reliability issues under connection pooling) is disproportionate
to the benefit (push latency only — not correctness) for a first release of
this provider. Nothing here rules it out later as a follow-up ADR if real
deployments ask for it; `docs/DEPLOYMENT.md` documents the limitation plainly
so that demand is visible rather than assumed away.

### Alternative 2 — one shared relational `FeatlyDbContext` across all three providers

Same alternative ADR-0026 rejected for two providers, now with a third data
point: SQL Server's `datetimeoffset`, Postgres's `timestamptz`, and SQLite's
ticks-converter-over-`INTEGER` are three genuinely different column strategies.
A shared context's `OnModelCreating` would need to branch on
`Database.ProviderName` for every mapped property, which is exactly the
"compile-time reflection trick" principle 6 warns against, and would
co-version three otherwise-independent packages.

### Alternative 3 — Dapper or hand-rolled SQL

Rejected for the same reason ADR-0026 rejected it: consistency with ADR-0006.

## Consequences

### Positive

- `services.AddFeatlySqlServerStore(...)` is a drop-in replacement for
  `AddFeatlySqliteStore(...)`/`AddFeatlyPostgresStore(...)` — same `IFeatlyStore`
  contract, no application-code changes (ARCHITECTURE.md principle 4).
- No new *notifier* operational surface (Service Broker, queue objects) for
  operators to provision, understand, or debug — pub/sub-wise the provider is
  as "boring" to run as SQLite (see Negative below for a different, real
  operational cost this provider does add).
- Each provider's migrations, `DbContext`, and configurations continue to
  evolve independently, as established by ADR-0026.

### Negative

- A SQL-Server-backed multi-replica deployment does not get instant
  cross-replica SSE push — only Postgres does. This is a real capability gap
  between the two "production" providers, not just a documentation footnote;
  operators who need instant push must choose Postgres.
- A third migrations history and a third `IEntityTypeConfiguration<T>` set per
  entity to keep in sync feature-for-feature with the other two providers.
- **Discovered during PR 1 implementation, not anticipated at Decision time:**
  `Microsoft.Data.SqlClient` requires ICU and throws
  `NotSupportedException: Globalization Invariant Mode is not supported` when
  opening a connection under the repo-wide `InvariantGlobalization=true`
  default (`Directory.Build.props`) — Postgres's Npgsql driver has no such
  requirement, so this is genuinely new to this provider, not something
  ADR-0026 already had to account for. `Featly.Storage.SqlServer.csproj` and
  its test project override the setting back to `false`, but this is a
  library-level override only; any consuming host (`Featly.Cli`, a server
  deployment referencing this provider) must set the same override itself,
  since `InvariantGlobalization` is a final-executable `runtimeconfig.json`
  switch that does not flow transitively through a `ProjectReference`. In
  practice this means choosing SQL Server costs a larger deployment (ICU data)
  compared to Postgres/SQLite, which work fine invariant. Tracked for PR 4
  (CLI + docs) to document prominently in `docs/DEPLOYMENT.md`.

### Neutral

- CI needs a real SQL Server to test against — a `mcr.microsoft.com/mssql/server`
  service container in `ci.yml`, mirroring the existing `postgres-tests` job
  (`sqlserver-tests`).

## Implementation notes

Sliced into PRs mirroring issue #157/#179's Postgres slicing, tracked in
[issue #274](https://github.com/Featly-net/Featly/issues/274):

1. Project scaffold + core entities (`Project`, `Environment`, `Flag`) + initial migration. **Shipped.**
2. Remaining entities (configs, segments, experiments, RBAC, approvals, webhooks, audit, settings).
3. `SqlServerFeatlyStore` facade + `AddFeatlySqlServerStore()` DI.
4. `Featly.Cli` `db --provider sqlserver` support + `docs/DEPLOYMENT.md` section.
5. Test suite mirroring `Featly.Storage.Postgres.Tests`, running against a real
   SQL Server in CI (`sqlserver-tests` service-container job).

No `LISTEN`/`NOTIFY`-equivalent PR — see Decision above; the in-process
notifier is used as-is, no new notifier class needed.

## References

- ARCHITECTURE.md §7 (Storage layer, "Provider roadmap")
- [ADR-0006](0006-ef-core-internal-dbcontext.md) — EF Core for relational storage; DbContext internal
- [ADR-0008](0008-sqlite-default-storage.md) — SQLite as default storage
- [ADR-0015](0015-storage-facade.md) — `IFeatlyStore` as facade
- [ADR-0026](0026-postgres-storage-provider.md) — PostgreSQL provider; the precedent this ADR mirrors and partly diverges from
- [Microsoft.Data.SqlClient](https://learn.microsoft.com/en-us/sql/connect/ado-net/microsoft-data-sqlclient)
- [SQL Server Service Broker](https://learn.microsoft.com/en-us/sql/database-engine/service-broker/service-broker) — the rejected alternative
