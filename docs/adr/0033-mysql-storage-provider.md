# ADR-0033: MySQL storage provider — Pomelo EF Core provider, own DbContext, polling-only `IChangeNotifier`

- **Status:** Accepted
- **Date:** 2026-07-29
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

`IFeatlyStore` is a facade with per-entity sub-stores (ADR-0015). SQLite
(ADR-0008), PostgreSQL (ADR-0026), and SQL Server (ADR-0032) cover the
relational providers shipped or in progress. MySQL (and MariaDB, which is
wire-compatible for the driver's purposes) is the remaining widely-requested
relational database for self-hosted .NET tooling — common in shops that are
neither Postgres- nor SQL-Server-standardized.

Two questions specific to MySQL, on top of the ones ADR-0026/ADR-0032 already
settled for "own `DbContext`, EF Core, independent migrations":

1. Which EF Core provider package: Oracle's `MySql.EntityFrameworkCore`, or
   the community-maintained `Pomelo.EntityFrameworkCore.MySql`?
2. What does `IChangeNotifier` do? MySQL has no `LISTEN`/`NOTIFY` equivalent;
   the closest primitives are binlog-based change data capture (row-based
   replication read via a client like a binlog-streaming library) or polling.

## Decision

`Featly.Storage.MySql` is a new project, independent from the other three
relational providers, targeting `net10.0` only (ADR-0007), using
**`Pomelo.EntityFrameworkCore.MySql`** (backed by `MySqlConnector`) rather than
Oracle's official provider: Pomelo is MIT-licensed with no GPL-adjacent
licensing questions, tracks current EF Core versions promptly (a recurring
weak point for Oracle's provider historically), and `MySqlConnector` is the
async-first, actively maintained ADO.NET driver the wider OSS .NET ecosystem
has converged on for MySQL. It defines **its own internal `FeatlyDbContext`**,
for the same reasons ADR-0026 and ADR-0032 gave: MySQL's native `JSON` column
type and UTC-normalized `DATETIME(6)` convention (MySQL has no timezone-aware
datetime type — Pomelo stores UTC and the convention is enforced at the
application boundary, same as how every Featly domain timestamp is already
`DateTimeOffset` normalized to UTC) are distinct enough from Postgres's
`jsonb`/`timestamptz` and SQL Server's `datetimeoffset` that a shared context
would just reintroduce the branching ADR-0026's Alternative 1 rejected.

`IChangeNotifier` for MySQL ships **polling-only** (`InProcessChangeNotifier`),
for the same reasoning ADR-0032 gave for SQL Server: MySQL's real push
primitive (binlog streaming via row-based replication) requires the server to
run with `binlog_format=ROW` and `log_bin` enabled, a replication-capable user
grant, and a client library maintaining a persistent binlog position —
materially more operational surface than a dedicated `LISTEN` connection, for
a benefit (push latency) that is not a correctness requirement (the ETag poll
already guarantees convergence, per `docs/DEPLOYMENT.md`'s "Scaling out"
section, which SQL Server already joined SQLite in under ADR-0032). MySQL
joins them: three of the four relational providers ship without instant
cross-replica push; only Postgres's `LISTEN`/`NOTIFY` is cheap and reliable
enough to justify building the real thing for v1.

Migrations follow the established pattern: a separate
`Featly.Storage.MySql/Migrations/` history against this provider's own
`FeatlyDbContext`, with a `MySqlMigrationRunner` facade for `Featly.Cli`'s `db`
command group (`--provider mysql`).

## Alternatives considered

### Alternative 1 — Oracle's `MySql.EntityFrameworkCore`

Rejected: slower EF Core version tracking historically, and it depends on
Oracle's `MySql.Data` connector, which ships under a GPL-with-FOSS-exception
license that adds review overhead an MIT-licensed alternative avoids. Pomelo +
`MySqlConnector` is the de facto standard choice in the OSS .NET ecosystem.

### Alternative 2 — binlog-based `IChangeNotifier`

Considered, to match Postgres's cross-replica push. Rejected for v1 for the
same class of reason ADR-0032 rejected Service Broker: real operational cost
(binlog format/retention configuration, a replication grant, a persistent
streaming connection with its own resume-position bookkeeping) for a
push-latency-only win. `docs/DEPLOYMENT.md` documents the limitation
explicitly rather than leaving it implicit; nothing here rules it out later if
real deployments ask for it.

### Alternative 3 — one shared relational `FeatlyDbContext`

Rejected for the same reason as every prior relational provider ADR: four
different provider-native column strategies would turn `OnModelCreating` into
a `Database.ProviderName` branch-fest, which principle 6 ("predictable, not
magical") rules out.

## Consequences

### Positive

- `services.AddFeatlyMySqlStore(...)` is a drop-in replacement for the other
  three providers' DI extensions — same `IFeatlyStore` contract.
- No new operational surface (binlog config, replication grants) for operators.
- MIT-licensed dependency chain end to end (Pomelo + MySqlConnector).

### Negative

- No instant cross-replica push — same limitation as SQLite and SQL Server;
  only Postgres has it.
- A fourth migrations history and a fourth `IEntityTypeConfiguration<T>` set
  per entity to keep in sync feature-for-feature with the other three.
- **Discovered during PR 1 implementation, not anticipated at Decision time:**
  `Pomelo.EntityFrameworkCore.MySql` 9.0.0 (the latest available) targets EF
  Core 9 and throws `MissingMethodException` at runtime if forced onto this
  repo's EF Core 10.0.9 (confirmed empirically via `dotnet ef migrations add`,
  not assumed). `Featly.Storage.MySql`'s own `.csproj` pins its EF Core
  packages down to `9.0.18` via `VersionOverride` — this provider alone runs
  one EF Core minor line behind the other three until Pomelo ships an EF Core
  10 build. This is a real, if contained, maintenance cost: a future EF Core
  10.x security fix does not automatically reach this provider.
- **Also discovered during PR 1:** Pomelo does not yet implement EF Core's
  `OwnsMany(...).ToJson()` owned-entity JSON mapping that every other
  relational provider here uses for `Flag.Variants`/`Rules`/`Tags`/`Prerequisites`
  (tracked upstream as
  [PomeloFoundation/Pomelo.EntityFrameworkCore.MySql#1752](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql/issues/1752),
  targeted at Pomelo's own next major version). The fallback,
  `JsonCollectionConversion` (an internal helper in `Featly.Storage.MySql`),
  maps each such property as a plain scalar `List<T>` serialized via
  `System.Text.Json` into a native MySQL `json` column with a
  `ValueComparer` for change tracking — semantically equivalent for Featly's
  access pattern (always load/replace the whole list with the parent row) but
  a materially different mapping mechanism than the other three providers,
  not just a different column-type string.
- **Discovered during PR 2 implementation:** MySQL's native `json` column
  type stores an internal optimized representation and always returns a
  canonical text form on read (e.g. inserting a space after `:`/`,` in
  objects and arrays) — confirmed empirically via a round-trip test on
  `Config.DefaultValue` that failed only for the object-shaped `Json`
  `ConfigType` case, never for scalar values (which have no delimiters to
  reformat). Every other relational provider here maps `JsonElement`
  scalars as plain text, so `GetRawText()` byte-for-byte equality holds
  there; MySQL's tests instead assert structural equality
  (`JsonElement.DeepEquals`). Not a correctness bug — the same input always
  canonicalizes to the same output — but a real fidelity difference worth
  knowing about if a future feature ever needs to preserve a config value's
  exact original formatting (none does today).

### Neutral

- CI needs a real MySQL (or MariaDB) to test against — a `mysql:8` service
  container in `ci.yml`, mirroring `postgres-tests` (`mysql-tests`).

## Implementation notes

Sliced into PRs mirroring issue #157 (Postgres) / #274 (SQL Server)'s slicing,
tracked in [issue #276](https://github.com/Featly-net/Featly/issues/276):

1. Project scaffold + core entities (`Project`, `Environment`, `Flag`) + initial migration. **Shipped.**
2. Remaining entities (configs, segments, experiments, RBAC, approvals, webhooks, audit, settings).
3. `MySqlFeatlyStore` facade + `AddFeatlyMySqlStore()` DI.
4. `Featly.Cli` `db --provider mysql` support + `docs/DEPLOYMENT.md` section.
5. Test suite mirroring `Featly.Storage.Postgres.Tests`, running against a real
   MySQL in CI (`mysql-tests` service-container job).

No push-based notifier PR — see Decision above.

## References

- ARCHITECTURE.md §7 (Storage layer, "Provider roadmap")
- [ADR-0006](0006-ef-core-internal-dbcontext.md) — EF Core for relational storage; DbContext internal
- [ADR-0026](0026-postgres-storage-provider.md) — PostgreSQL provider
- [ADR-0032](0032-sqlserver-storage-provider.md) — SQL Server provider; the polling-only precedent this ADR follows
- [Pomelo.EntityFrameworkCore.MySql](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql)
- [MySqlConnector](https://mysqlconnector.net/)
