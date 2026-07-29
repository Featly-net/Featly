# ADR-0034: MongoDB storage provider — MongoDB.Driver (not EF Core), replica-set requirement, Change Streams `IChangeNotifier`

- **Status:** Accepted
- **Date:** 2026-07-29
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

`IFeatlyStore` is a facade with per-entity sub-stores (ADR-0015), implemented so
far by four relational providers (SQLite, PostgreSQL, SQL Server, MySQL — ADR-
0008/0026/0032/0033) that all share the same shape: EF Core, an internal
`FeatlyDbContext`, and a `Migrations/` folder. MongoDB is architecturally
different in every one of those dimensions — it is a document store, not
relational, and ADR-0006's "EF Core for relational storage" decision does not
apply to it (EF Core's third-party Mongo providers are thin, lag the official
driver, and would fight the document model more than help it). This ADR makes
the equivalent set of decisions for a genuinely different storage shape, not a
restatement of ADR-0006.

Four questions:

1. Data-access technology: the official `MongoDB.Driver`, or an EF Core
   provider for Mongo?
2. Document shape: how do `Featly.Abstractions` domain types (which must stay
   dependency-free, per ARCHITECTURE.md principle 4) map to BSON documents
   without leaking `MongoDB.Bson` attributes onto them?
3. "Migrations" and multi-document atomicity: Mongo has no schema to migrate
   and, on a standalone node, no multi-document transactions. Does Featly's
   approval/pending-change workflow (which relies on atomic multi-entity
   writes) work at all on Mongo?
4. `IChangeNotifier`: does this provider get real cross-replica push, like
   Postgres, or polling-only, like SQL Server and MySQL (ADR-0032/0033)?

## Decision

`Featly.Storage.MongoDB` is a new project, independent from the relational
providers, targeting `net10.0` only (ADR-0007), using the **official
`MongoDB.Driver`** directly — no EF Core. Domain entities in
`Featly.Abstractions` stay untouched (zero `MongoDB.Bson` attributes, same as
they carry zero EF Core attributes today); each entity's document shape is
registered via a `BsonClassMap` configuration class at startup (one per
entity, e.g. `FlagClassMap`), the document-model equivalent of the relational
providers' `IEntityTypeConfiguration<T>` — same intentional per-provider
duplication ADR-0026 already accepted, now in a different shape. One
collection per entity (`flags`, `projects`, `environments`, ...), scoped by
`ProjectId`/`EnvironmentId` fields mirroring the relational foreign keys,
indexed accordingly.

**This provider requires MongoDB to run as a replica set** (a single-node
replica set is sufic­ient for local dev and is what the docker-compose sample
configures) — not a standalone instance. This one operational requirement
buys two things at once: multi-document ACID transactions (needed for the
approval workflow's atomic pending-change writes, and for audit hash-chain
appends that must not interleave) and Change Streams (see below). Both
features are unavailable on a standalone `mongod`, so there is no meaningful
"standalone" tier for this provider to support — `docs/DEPLOYMENT.md` states
the replica-set requirement plainly as a prerequisite, not a footnote.

There is no traditional schema to migrate, so "migrations" become an ordered
list of idempotent steps (index creation, one-off document transforms)
tracked by a version marker document in a `__migrations` collection —
`MongoMigrationRunner` (the `Featly.Cli` `db --provider mongodb` facade) walks
pending steps in order and records each as applied, mirroring the *behavior*
of EF Core migrations (`db status` / `db migrate`) without pretending Mongo
has a schema.

`IChangeNotifier` for MongoDB uses **Change Streams** — a `watch()` cursor per
replica against the relevant collections, fanning notifications out to the
same in-process subscriber list every provider's notifier maintains. Unlike
SQL Server's Service Broker or MySQL's binlog streaming (both rejected as
disproportionately heavy for a polling-equivalent win — ADR-0032/0033), Change
Streams are a first-class, well-supported driver feature with no separate
enablement step beyond the replica set this provider already requires for
transactions. That makes MongoDB the second provider (after Postgres) to get
real cross-replica push in `docs/DEPLOYMENT.md`'s "Scaling out" section — the
deciding factor across every provider ADR so far has consistently been "is the
push primitive cheap and operationally boring, or does it need its own
enablement/config surface," not database popularity.

## Alternatives considered

### Alternative 1 — an EF Core provider for MongoDB

Rejected: third-party EF Core Mongo providers are thin wrappers that fight the
document model (they encourage a relational mental model — foreign keys,
joins — Mongo does not have) and lag official EF Core releases. The official
`MongoDB.Driver` is actively maintained, async-first, and is what the wider
.NET-on-Mongo ecosystem actually uses.

### Alternative 2 — support standalone (non-replica-set) MongoDB, with polling-only `IChangeNotifier` and no transactions

Considered, to lower the deployment bar. Rejected: without transactions, the
approval workflow's atomic pending-change application and the audit hash-
chain's strictly-ordered appends would need compensating application-level
logic to approximate atomicity Mongo already gives for free on a replica set —
real complexity, for an operational simplification (skip `rs.initiate()`) that
is a single command and standard practice for any production Mongo deployment
in 2026. Every real Mongo deployment guide already recommends a replica set
regardless of Featly's needs.

### Alternative 3 — polling-only `IChangeNotifier` for consistency with SQL Server/MySQL

Rejected as the default: this provider already requires a replica set for
transactions, so Change Streams add no new operational requirement — unlike
Service Broker or binlog streaming, which would be new asks on top of an
otherwise plain deployment. Paying the replica-set cost once and getting both
transactions and real push is a strictly better trade than paying it and
settling for polling anyway.

## Consequences

### Positive

- `services.AddFeatlyMongoStore(...)` is a drop-in replacement for the other
  four providers' DI extensions — same `IFeatlyStore` contract
  (ARCHITECTURE.md principle 4).
- Real cross-replica push, same tier as Postgres — two of five providers now
  give operators the "instant" SSE experience without a documented gap.
- `Featly.Abstractions` stays exactly as storage-agnostic as it is today; no
  BSON attributes creep into domain types.

### Negative

- A genuinely different code shape from the other four providers (no EF Core,
  no `DbContext`, no generated migrations) — the storage layer's internal
  consistency ends at the `IFeatlyStore` boundary, not one level below it.
  Contributors touching storage need to understand two patterns, not one.
- Requires a replica set even for a single-node local/dev deployment — a
  heavier local setup than any of the other four providers (`docker compose up`
  for the sample must run `rs.initiate()` on first boot).
- `MongoMigrationRunner`'s ordered-idempotent-step model is hand-rolled, not
  EF Core tooling — index/transform steps are reviewed by hand for
  idempotency instead of relying on a generator.

### Neutral

- CI needs a real MongoDB replica set to test against — a `mongo:7` service
  container in `ci.yml` with a startup step running `rs.initiate()`
  (`mongodb-tests` job, mirroring `postgres-tests`).

## Implementation notes

Sliced into PRs, tracked in [issue #277](https://github.com/Featly-net/Featly/issues/277):

1. Project scaffold + `BsonClassMap` registration pattern + core entities
   (`Project`, `Environment`, `Flag`) + `MongoMigrationRunner` skeleton with
   the first index-creation step. **Shipped.**
2. Remaining entities' class maps and stores (configs, segments, experiments,
   RBAC, approvals, webhooks, audit, settings), transactional writes for the
   approval/pending-change and audit hash-chain paths.
3. `MongoFeatlyStore` facade + `AddFeatlyMongoStore()` DI.
4. Change-Streams-backed `IChangeNotifier` + hosted listener service (shape
   mirrors `PostgresChangeListenerHostedService`, watching collections instead
   of a channel).
5. `Featly.Cli` `db --provider mongodb` support + `docs/DEPLOYMENT.md` section
   (including the replica-set prerequisite) + docker-compose sample with
   `rs.initiate()`.
6. Test suite mirroring `Featly.Storage.Postgres.Tests`, running against a
   real single-node Mongo replica set in CI.

**Discovered during PR 1 implementation:** GitHub Actions' `services:` block
has no `command:` key to pass extra CLI args to a service container — every
other provider's service entry only needed environment variables and a health
check, which `services:` supports directly, but this provider needs
`mongod --replSet rs0` specifically. Confirmed by checking the documented
service-container schema (image/credentials/env/ports/volumes/options only),
not assumed. Worked around by starting and initiating MongoDB as a plain
`docker run` + `docker exec ... rs.initiate(...)` job step instead of a
`services:` entry, in both `ci.yml`'s `mongodb-tests` job and
`sonarcloud.yml`'s coverage job — the only provider whose CI wiring deviates
from the `services:` pattern the other four use.

## References

- ARCHITECTURE.md §7 (Storage layer, "Provider roadmap"), §14 (Notifications)
- [ADR-0015](0015-storage-facade.md) — `IFeatlyStore` as facade
- [ADR-0026](0026-postgres-storage-provider.md) — the LISTEN/NOTIFY precedent this ADR's Change Streams decision follows
- [ADR-0032](0032-sqlserver-storage-provider.md) / [ADR-0033](0033-mysql-storage-provider.md) — the polling-only precedent this ADR explicitly diverges from, and why
- [MongoDB Change Streams](https://www.mongodb.com/docs/manual/changeStreams/)
- [MongoDB transactions](https://www.mongodb.com/docs/manual/core/transactions/) — replica-set/sharded-cluster requirement
- [MongoDB.Driver](https://www.mongodb.com/docs/drivers/csharp/current/)
