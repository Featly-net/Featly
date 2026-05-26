# Current status

> Refreshed by maintainers whenever the active milestone changes.
> The full plan lives in [PLAN.md](PLAN.md). The design lives in [ARCHITECTURE.md](ARCHITECTURE.md).

## Active milestone

**M3 — Multi-variant flags with targeting rules** (in progress; PR 3A landed)

### Goal (M3)

Rules, conditions, segments, and bucketing — the full evaluation engine for flags. Done in four sequenced PRs (3A domain+storage, 3B engine+benchmarks, 3C server, 3D SDK+e2e).

### Goal (M2 — complete)

A boolean flag, evaluated locally by the SDK, served by the server, persisted in storage. Proves the architecture end-to-end.

### Done — M3 PR 3A (domain + storage)

- **New domain types** in `Featly.Abstractions`: `ConditionOperator` (enum, 16 members), `Condition`, `Split`, `RuleOutcome`, `Rule`, `Segment`, `IFeatlyContextAccessor` (interface; implementation lands in 3D)
- `Flag.Rules` ordered list added
- **`ISegmentStore`** contract in `Featly.Storage.Abstractions`, plus `IFeatlyStore.Segments` on the facade
- **`Featly.Storage.InMemory`**: `InMemorySegmentStore` wired into the facade
- **`Featly.Storage.Sqlite`**:
  - `SegmentConfiguration` + `Segments` table (unique `(EnvironmentId, Key)`, conditions as owned JSON)
  - `FlagConfiguration` extended with `Rules` owned JSON (rules → conditions, outcome → splits all in one document)
  - `ConditionValueParser` helper round-trips `JsonElement` through raw JSON text
  - Migration `AddRulesAndSegments` adds the `Rules` column to `Flags` and creates `Segments`
  - `SqliteSegmentStore` follows the per-operation context pattern
- **Tests** (37 passing total, +6 in this PR): segment round-trip, list ordering, upsert overwrite, idempotent delete, most-recent-update tracking, and a Flag-with-Rules round-trip exercising nested conditions and weighted splits

### Coming next — M3 PRs 3B → 3D

- **3B** — `Featly.Engine.Evaluator` extended with rule iteration, all 16 condition operators, segment resolution, MurmurHash3 bucketing; unit-test sweep; BenchmarkDotNet baseline targeting p99 < 10μs
- **3C** — Server endpoints for segments (CRUD), flag rule editor, snapshot endpoint includes segments
- **3D** — `IFeatlyContextAccessor` wired in DI; `HttpContextFeatlyContextAccessor` in `Featly.AspNetCore`; end-to-end test of a multi-variant flag with targeting

### Done — M2 (complete)

- **Domain entities** in `Featly.Abstractions`: `Project`, `Environment`, `Flag` (with `Variants`), `ConfigSnapshot`
- **Storage contracts** in `Featly.Storage.Abstractions`: `IFeatlyStore` facade + `IFlagStore`, `IProjectStore`, `IEnvironmentStore`, `IChangeNotifier`
- **`Featly.Storage.InMemory`**: thread-safe sub-stores backed by `ConcurrentDictionary`; in-process `IChangeNotifier`
- **`Featly.Engine.Evaluator`**: boolean-minimum flag evaluation (kill switch, archived short-circuit, default-variant resolution); rules in M3
- **`Featly.Server`**:
  - `FeatlyServerOptions` bound from `Featly:Server` configuration
  - Static-token bearer auth with `FeatlyAdmin` + `FeatlySdk` schemes (real Argon2 API keys land in M6)
  - `DefaultProjectBootstrapHostedService` auto-creates a default Project + Environment on first boot
  - Admin API: `GET|POST|PUT /api/admin/flags` with optional `?env=` query parameter
  - SDK API: `GET /api/sdk/config` with ETag/`If-None-Match`, `GET /api/sdk/stream` with SSE notifications
- **`Featly.Sdk`**:
  - `IFeatlyClient` + `IFlagClient` real implementations
  - `FeatlySnapshotCache` (thread-safe `ImmutableDictionary` lookup)
  - `FeatlyConfigSyncService` BackgroundService: initial fetch, long-lived SSE connection, polling fallback with ETag
  - `services.AddFeatly().UseServer(url, apiKey).UseEnvironment(...)` fluent DI surface
- **Samples**: `SelfHosted.Sample` boots server + dashboard + InMemory; `WebApi.Sample` consumes via the SDK
- **`Featly.Storage.Sqlite`**: `FeatlyDbContext` (internal) with EF Core configurations; `Project`, `Environment`, `Flag` tables; `Variants` and `Tags` as JSON columns; initial migration `InitialCreate`; pooled `IDbContextFactory<FeatlyDbContext>`; SQLite-backed sub-stores; `SqliteAutoMigrationHostedService` applies pending migrations at boot when `AutoMigrate=true`; `services.AddFeatlySqliteStore(opts => ...)` DI extension
- **`samples/SelfHosted.Sample`** now uses `Featly.Storage.Sqlite` by default (`Data Source=featly.db`), giving the Hangfire-style quickstart real persistence
- **Tests** (31 passing): Engine, SDK FlagClient, Server admin/SDK endpoints with auth, E2E boolean flag round-trip via TestServer, SQLite store round-trips (Flag with variants and tags, Project unique key, Environment scoped uniqueness, ChangeNotifier pub/sub, migrations apply)

### Done-when criteria (PLAN.md M2)

- [x] End-to-end test passes consistently (Featly.E2E.Tests.BooleanFlagEndToEndTests)
- [x] A developer can create a boolean flag via HTTP and the sample app's `IsEnabledAsync` reflects it within a polling interval
- [x] Persisted in SQLite (default for `samples/SelfHosted.Sample`; covered by `Featly.Storage.Sqlite.Tests`)

### Not in scope for M2 (deferred to later milestones)

- Targeting rules, conditions, segments, bucketing — M3
- Dynamic configs (`IConfigClient`) — M4
- Dashboard UI for flags — M5
- Real auth pipeline + RBAC + Project entity scoping — M6, M7
- Approval workflows — M8
- Experiments — M9
- Webhooks — M10
- OpenFeature provider implementation — M11
- CLI commands and first release — M12

## Open follow-ups

- **Reserve the `Featly` and `Featly.*` package names on NuGet.org** — before M3 we should publish minimal `0.0.1-preview.1` placeholders for at least `Featly`, `Featly.Sdk`, and `Featly.Abstractions`, and explore Verified Publisher for the `Featly.*` prefix. Avoids squatting while the rest of the milestones land.
- Persist `Flag.UpdatedAt` (and other `DateTimeOffset` columns) as UTC ticks (`long`) in the SQLite provider so `MAX`/`ORDER BY` can run in SQL. Today `GetMostRecentUpdateAsync` pulls timestamps client-side as a workaround.
- `CODE_OF_CONDUCT.md` referenced by `CONTRIBUTING.md` but not yet added
- ADR on testing library choice (FluentAssertions 7.x → migrate to Shouldly or AwesomeAssertions before bumping past 8.x)
- ADR on database-overrides-config settings provider (M2 introduces server options bound from config only; DB-overrides logic lands once `ISystemSettingsStore` exists in M6+)
- Extract the in-process `IChangeNotifier` from the InMemory and SQLite providers into a shared helper in `Featly.Storage.Abstractions` so future single-instance providers don't duplicate the code (currently identical implementations in both packages)
