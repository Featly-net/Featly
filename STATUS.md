# Current status

> Refreshed by maintainers whenever the active milestone changes.
> The full plan lives in [PLAN.md](PLAN.md). The design lives in [ARCHITECTURE.md](ARCHITECTURE.md).

## Active milestone

**M4 — Configs with the same engine** (in progress; PR 4A landed)

### Goal (M4)

Dynamic configuration as a parallel entity, sharing the targeting engine with flags. Done in two sequenced PRs (4A domain+storage, 4B engine+server+SDK+sample).

### Done — M4 PR 4A (domain + storage)

- **`ConfigRule`** new in `Featly.Abstractions`: `Order`, optional `Name`, list of `Condition` (reused from flag rules), `Value` (`JsonElement` — direct typed value, no variant indirection), `Enabled`
- **`Config.Rules`** ordered list added
- **`IConfigStore`** contract in `Featly.Storage.Abstractions`, plus `IFeatlyStore.Configs` on the facade
- **`Featly.Storage.InMemory.InMemoryConfigStore`** wired into the facade
- **`Featly.Storage.Sqlite`**:
  - `ConfigConfiguration` + `Configs` table (unique `(EnvironmentId, Key)`)
  - `DefaultValue` and `ConfigRule.Value` stored as raw JSON text via the shared `ConditionValueParser`
  - `Rules` and nested `Conditions` persisted as owned JSON inside the row
  - Migration `AddConfigs`: new table only (no schema changes to existing entities)
  - `SqliteConfigStore` follows the per-operation `IDbContextFactory<FeatlyDbContext>` pattern
- **Tests** (99 passing total, +10 new in `SqliteConfigStoreTests`): upsert with rules, overwrite preserves id, list filters archived + scopes per-env, **theory covering all 6 representative `ConfigType` values** (String/Int/Long/Double/Bool/Json) round-tripping cleanly, most-recent-update tracking

### Coming next — M4 PR 4B (engine + server + SDK)

- `Evaluator.EvaluateConfig<T>` walking `ConfigRule.Order` with the same AND-conditions semantics
- `ConfigSnapshot` extended with `Configs`; SDK config endpoint returns them
- Admin endpoints `/api/admin/configs` (CRUD + rules)
- `IConfigClient` in the SDK with `GetAsync<T>` / `EvaluateAsync<T>`
- `IFeatlyClient.Configs` property
- `FeatlySnapshotCache` indexes configs
- WebApi sample demonstrates `checkout.timeout` returning 30/60 based on `user.plan`
- E2E test of a config with a targeting rule

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

### Done — M3 PR 3B (engine + benchmarks)

- **`Featly.Engine.Evaluator` is now feature-complete** per ARCHITECTURE.md §5: walks rules by `Order` asc, AND inside a rule, first-match wins, single-variant outcome → `TargetingMatch`, weighted-split outcome → `Split` via MurmurHash3 bucketing
- All **16 condition operators** in `Internal/ConditionEvaluator` (Equals/NotEquals, In/NotIn, GreaterThan/GreaterThanOrEqual/LessThan/LessThanOrEqual, Contains/StartsWith/EndsWith, regex Matches with 50ms timeout against ReDoS, Semver Gt/Lt/Eq, InSegment with recursive segment-condition matching). `Negate` flag inverts the predicate
- **`Internal/MurmurHash3`**: 32-bit in-house implementation, `BucketOf10000` returns the 0..9999 bucket the engine uses
- **`Internal/Bucketer`**: composes `targetingKey + flagKey + salt`, hashes, walks the cumulative weights
- **`Internal/AttributeResolver`**: flat-key lookup into `EvaluationContext.Attributes` plus the `targetingKey` shortcut
- **`Internal/SemverComparer`**: in-house semver 2.0.0 (no external dependency)
- **`ISegmentLookup`** public contract + `DictionarySegmentLookup` default. M3C/3D populates the lookup from the SDK snapshot
- **40 new engine tests** (46 total): every operator positive + negative, `Negate`, first-match-wins, AND, disabled-rule skip, segment matched + missing-from-lookup, bucketing determinism, distribution within ±5% on 5 000 subjects (50/50 and 90/10)
- **`tests/Featly.Engine.Benchmarks`** new project. `docs/PERFORMANCE.md` carries the baseline. Every scenario is below the **10 μs p99 target**: boolean fast path 37 ns, 1 rule 134 ns, 5 rules × 3 conditions 1.2 μs, split bucketing 390 ns, InSegment lookup 286 ns

### Done — M3 PR 3C (server)

- **`ConfigSnapshot`** now carries `Segments` alongside `Flags`; the SDK config endpoint returns both. The ETag folds the most-recent `Flag.UpdatedAt` *and* the most-recent `Segment.UpdatedAt` so edits in either bucket invalidate cached snapshots
- **`FlagWriteRequest`** accepts an optional `Rules` array. `POST` and `PUT /api/admin/flags` persist targeting rules end-to-end (already round-tripped through SQLite by 3A)
- **`AdminSegmentsEndpoints`** new — full CRUD under `/api/admin/segments`:
  - `GET /` list, `GET /{key}`, `POST /`, `PUT /{key}`, `DELETE /{key}`
  - Auth: same admin policy as flags. Sdk-scoped keys get 401/403
  - ReadOnly environment rejected with 403
  - Every mutation emits a `ChangeNotification(EntityType: "Segment")` so SSE clients re-fetch
- **8 new server tests** (15 total in `Featly.Server.Tests`): 5 covering segment CRUD + auth gating, 1 PUT-flag-with-rules round-trip, 2 SDK snapshot showing segments and ETag invalidation on segment change

### Done — M3 PR 3D (SDK ambient context + AspNetCore + sample)

- **`FeatlySnapshotCache`** indexes `Segments` and exposes an `ISegmentLookup`; `FlagClient` hands it to the engine on every call, so `InSegment` resolves locally without a server round-trip
- **`FlagClient`** picks up the ambient context from `IFeatlyContextAccessor` when callers don't pass one. Explicit context always wins
- **`NoOpFeatlyContextAccessor`** is the SDK default; `builder.UseContextAccessor<TAccessor>()` swaps it
- **`Featly.AspNetCore.HttpContextFeatlyContextAccessor`** maps `HttpContext.User` claims (NameIdentifier/Sub/email/name) into an `EvaluationContext`. Wired via `builder.UseHttpContextAccessor()`
- **`samples/WebApi.Sample`** with targeting demo: `/checkout?country=BR&plan=pro` exercises rule matching, `?targetingKey=...` drives split bucketing; response includes matched rule, variant, and reason
- **4 new SDK tests** (`AmbientContextAccessorTests`): ambient pickup, explicit override, no-op fallthrough, segments resolved locally
- **89 tests pass** overall (was 85)

### M3 — "Done when" criteria from PLAN.md

- [x] Configure a flag like "100% for `user.country=BR`, 10% rollout for `user.plan=pro`, off for everyone else" via the API
- [x] SDK evaluates the rule locally for any subject and context
- [x] Performance: p99 < 10μs hit cleanly per `docs/PERFORMANCE.md`

## Coming next — M4 (PLAN.md)

Dynamic configuration (`Config` + `ConfigRule`) sharing the targeting engine with flags. `IConfigClient.GetAsync<T>` in the SDK. Admin API for configs.

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
