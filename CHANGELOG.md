# Changelog

All notable changes to Featly will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Until version `1.0.0`, the public API is unstable and minor versions may introduce breaking changes. Breaking changes will be called out explicitly in the notes for each release.

## [Unreleased]

### Added

- **M5 PR 5C — dashboard detail screens + visual rule editor.** Clicking a row in any list view opens a detail page (`/featly/flags/{key}`, `/featly/configs/{key}`, `/featly/segments/{key}`) where the entity is editable: name, description, enabled toggle (flag), default variant (flag), default value (config), variants, tags, and a shared visual editor for rules + conditions. The rule editor handles flag Rules (single-variant outcome or weighted splits) and Config rules (typed value) and is reused for Segment conditions. Save round-trips to `PUT /api/admin/{resource}/{key}` with `Saving… / Saved. / inline error` feedback. The dashboard router now matches dynamic paths and falls back to the shell for deep links. Refresh-on-window-focus pulls fresh state when the tab regains focus (SSE live updates ship in a follow-up alongside an admin stream endpoint). The middleware was generalized to serve any asset under `wwwroot/` by extension, so future CSS/JS splits don't need code changes.

### Fixed

- **`SqliteFlagStore.UpsertAsync` was discarding `Rules` on update.** The update path copied every mutable field except `Rules`, so saving a flag with targeting rules returned the new rules in the response but never persisted them — the next GET would come back empty. The bug surfaced through the M5C rule editor; the underlying store has been wrong since M3 PR 3A. Fix includes the missing `Include(f => f.Rules)` and a parallel `Clear()` + add of the rules collection. New regression test `Upsert_replaces_rules_on_update` covers replace, partial change, and full wipe.

### Added (continued)

- **M5 PR 5B — dashboard listing screens.** The Flags, Configs, and Segments tabs now render live data from the admin API. New `GET /api/admin/environments` endpoint feeds the environment selector in the header. The dashboard prompts for the admin token on first load (stored in `localStorage`, sent as `Authorization: Bearer` on every request), exposes a "Sign out" button that clears it, and surfaces auth / network errors inline. Tables show key, name, type, enabled state, variant/rule counts, and a localized "Updated" timestamp. Pre-M6 bridge — M6's real auth pipeline replaces the token paste flow. 3 new server tests for the environments endpoint (auth gates + bootstrap default). 134 tests passing total.
- **M5 PR 5A — dashboard skeleton.** `Featly.Dashboard` now ships a navigable shell instead of the M1 "coming soon" placeholder. Three embedded assets (`wwwroot/index.html`, `wwwroot/app.css`, `wwwroot/app.js`) are served by `MapFeatlyDashboard`: the root and any sub-path return `index.html` (so deep links like `/featly/flags` work with browser refresh), and `app.css` / `app.js` get their own routes. The client is a vanilla-JS path-based SPA with a tiny router, design-token CSS (light/dark via `prefers-color-scheme`), explicit hover/focus states on every interactive element, and placeholder views for Flags / Configs / Segments / Settings. M5B replaces the placeholders with the real list screens. 2 new E2E tests cover SPA fallback + embedded asset serving (131 tests passing total).

## [0.0.2-preview.1] - 2026-05-26

First substantive preview. v0.0.1 reserved the package names with empty placeholders; v0.0.2-preview.1 ships the full Featly stack through M4 (multi-variant flag evaluation engine + dynamic configs + segments + targeting rules end-to-end).

### Changed

- **Extract `InProcessChangeNotifier` into `Featly.Storage.Abstractions`.** The InMemory and SQLite providers previously each shipped a near-identical `IChangeNotifier` implementation (concurrent dictionary of handlers, subscriber-isolation `catch`, dispose-once subscription). Replaced by a single public `Featly.Storage.InProcessChangeNotifier` that both providers (and any future single-instance provider) consume. `InMemoryChangeNotifier` and `SqliteChangeNotifier` are gone. No public-API change for the storage facade: `IChangeNotifier`-typed consumers see the same contract.
- **Persist `DateTimeOffset` audit timestamps as 64-bit UTC ticks in SQLite.** New `DateTimeOffsetTicksConverter` value converter, applied to `CreatedAt` / `UpdatedAt` on Project, Environment, Flag, Segment, and Config. `MAX` and `ORDER BY` now run server-side instead of pulling N timestamps and aggregating client-side; `GetMostRecentUpdateAsync` on `SqliteFlagStore` / `SqliteSegmentStore` / `SqliteConfigStore` uses `.MaxAsync()`. EF migration `ConvertTimestampsToUtcTicks` alters the columns from `TEXT` to `INTEGER`. **Breaking for any dev database that holds existing rows**: SQLite's implicit `CAST(text AS INTEGER)` produces garbage values for the migrated rows (each ISO-8601 string collapses to the leading year integer). Delete `featly.db` and let the bootstrap recreate it. Production has no real users at v0.0.x, so the breaking change is intentionally tolerated.

### Added

- **M4 PR 4B — config engine + server + SDK + sample.** `Evaluator.EvaluateConfig` walks `Config.Rules` by `ConfigRule.Order` asc, AND inside a rule, first-match wins, returns the rule's typed `Value` with reason `TargetingMatch` and the matched rule name; falls back to `Config.DefaultValue` with reason `Default`. Reuses the same `ISegmentLookup` plumbing flags use. `ConfigSnapshot` now carries `Configs` alongside `Flags` and `Segments`; the SDK config endpoint ETag folds the most-recent timestamps of all three buckets. New `/api/admin/configs` endpoints (list, get, create, update) with the same admin auth policy, ReadOnly-environment 403, and `Config`-typed `ChangeNotification` on every mutation. `IConfigClient` ships in the SDK with `GetAsync<T>` / `EvaluateAsync<T>`, picks up the ambient `IFeatlyContextAccessor` context, and is wired into `IFeatlyClient.Configs`. `FeatlySnapshotCache` indexes configs alongside flags and segments. `samples/WebApi.Sample` gains a `/checkout/timeout` endpoint demonstrating dynamic config consumption. 30 new tests across engine (10), SDK (8), server admin endpoints (8), SDK snapshot (2), and E2E (2) — 129 tests passing in total. **M4 is complete.**
- **M4 PR 4A — config domain + storage.** New `ConfigRule` type in `Featly.Abstractions` reusing `Condition` from flag rules; its outcome is a direct typed `Value` (no variant indirection). `Config.Rules` ordered list added. `IConfigStore` contract on the storage facade; `Featly.Storage.InMemory` and `Featly.Storage.Sqlite` both ship config stores. SQLite migration `AddConfigs` adds a `Configs` table (unique `(EnvironmentId, Key)`, `DefaultValue` and `ConfigRule.Value` as raw JSON text, `Rules` as owned JSON). 10 new tests covering round-trip with rules, overwrite preserves id, list filters archived + scopes per-env, plus a theory exercising every representative `ConfigType` (String/Int/Long/Double/Bool/Json).
- **M3 PR 3D — SDK ambient context + AspNetCore.** `FeatlySnapshotCache` now indexes `Segments` and exposes an `ISegmentLookup` that `FlagClient` hands to the engine on every call, so `InSegment` resolves locally without a server round-trip. `FlagClient` picks up the ambient context from `IFeatlyContextAccessor` when callers don't pass one explicitly; explicit context always wins. `NoOpFeatlyContextAccessor` is the SDK default, replaceable via the new `builder.UseContextAccessor<TAccessor>()` extension. `Featly.AspNetCore.HttpContextFeatlyContextAccessor` reads `HttpContext.User` claims (NameIdentifier/Sub/email/name) into an `EvaluationContext`; wired via `builder.UseHttpContextAccessor()`. `samples/WebApi.Sample` updated with a targeting demo (`/checkout?country=BR&plan=pro` for rule matching, `?targetingKey=...` for split bucketing). 4 new SDK tests in `AmbientContextAccessorTests`. **M3 is complete — all four sequenced PRs (3A→3D) merged.**
- **M3 PR 3C — server.** `ConfigSnapshot` now carries `Segments` alongside `Flags`; the SDK config endpoint (`GET /api/sdk/config`) returns both and the ETag folds the most-recent `Flag.UpdatedAt` and `Segment.UpdatedAt` so edits in either bucket invalidate cached snapshots. `FlagWriteRequest` accepts an optional `Rules` array — `POST` and `PUT /api/admin/flags` now persist targeting rules end-to-end (round-tripped through SQLite by 3A). New `AdminSegmentsEndpoints` exposes full CRUD under `/api/admin/segments` (list/get/post/put/delete) with the same admin auth policy, ReadOnly-environment 403, and `Segment`-typed `ChangeNotification` on every mutation. 8 new tests in `Featly.Server.Tests` cover segment CRUD, auth gating, a flag-with-rules PUT round-trip, and SDK snapshot showing segments plus ETag invalidation on segment change.
- **M3 PR 3B — engine + benchmarks.** `Featly.Engine.Evaluator` is now feature-complete per ARCHITECTURE.md §5. All 16 condition operators land in `Internal/ConditionEvaluator` (regex with a 50ms timeout against ReDoS; Semver 2.0.0 comparison in-house). `Internal/MurmurHash3` 32-bit + `Internal/Bucketer` implement deterministic split bucketing. `ISegmentLookup` + `DictionarySegmentLookup` give the engine a way to resolve `InSegment` conditions without taking a storage dependency. 40 new tests in `Featly.Engine.Tests` (46 total) cover every operator, rule order, AND within rules, segment matching, and bucketing distribution within ±5% on 5 000 subjects. New `tests/Featly.Engine.Benchmarks` project with BenchmarkDotNet; `docs/PERFORMANCE.md` documents the baseline (all scenarios below the 10 μs p99 target).
- **M3 PR 3A — targeting domain + storage.** New types in `Featly.Abstractions`: `ConditionOperator` (16 members), `Condition`, `Split`, `RuleOutcome`, `Rule`, `Segment`, plus the `IFeatlyContextAccessor` interface (the ASP.NET Core implementation lands in 3D). `Flag.Rules` ordered list. `ISegmentStore` contract on the storage facade. `Featly.Storage.InMemory` and `Featly.Storage.Sqlite` both ship segment stores. SQLite migration `AddRulesAndSegments` adds the `Rules` JSON column to `Flags` and the new `Segments` table. The shared `ConditionValueParser` helper round-trips `JsonElement` through raw JSON text inside owned JSON documents.
- 6 new tests in `Featly.Storage.Sqlite.Tests` covering segment round-trip, list ordering, upsert overwrite, idempotent delete, most-recent-update tracking, and a Flag-with-Rules round-trip exercising nested conditions and weighted splits.
- Initial project foundation: `ARCHITECTURE.md`, `PLAN.md`, `CONTRIBUTING.md`, `NOTICE.md`, `README.md`, `LICENSE` (MIT).
- Repository scaffolding: `SECURITY.md`, `CHANGELOG.md`, issue and pull request templates, CODEOWNERS, Dependabot configuration, ADR template.
- **M1 skeleton.** Solution structure with 11 `src/` projects, 5 `tests/` projects, 2 `samples/` projects. Build infrastructure: `global.json`, `.editorconfig`, `Directory.Build.props`, `Directory.Packages.props` (Central Package Management).
- Placeholder contract types in `Featly.Abstractions`: `Flag`, `Variant`, `FlagType`, `Config`, `ConfigType`, `EvaluationContext`, `EvaluationReason`, `EvaluationResult<T>`, `IFeatlyClient`, `IFeatlyStore`.
- `Featly.Server.MapFeatlyApi()` mounts a `GET /health/live` liveness probe.
- `Featly.Dashboard.MapFeatlyDashboard("/featly")` serves an embedded "coming soon" placeholder.
- `Featly.Storage.InMemory.AddFeatlyInMemoryStore()` registers a no-op `IFeatlyStore` so the server can boot.
- `samples/SelfHosted.Sample` runs the embedded host end-to-end.
- GitHub Actions CI workflow: matrix [ubuntu-latest, windows-latest] with .NET 8 + .NET 10 SDKs. Restore / build (warnings-as-errors) / test / pack preview.
- `STATUS.md` tracking the active milestone.
- **M2 vertical slice.** Boolean flag end-to-end: domain entities (`Project`, `Environment`, `Variants` on `Flag`, `ConfigSnapshot`); storage contracts (`IFlagStore`, `IProjectStore`, `IEnvironmentStore`, `IChangeNotifier`); real `InMemoryFeatlyStore`; minimal `Evaluator` (kill switch + default variant); server with static-token bearer auth, default-project bootstrap, admin CRUD endpoints, and SDK `GET /api/sdk/config` (ETag) plus `GET /api/sdk/stream` (SSE); SDK with local-evaluation cache, fluent `AddFeatly().UseServer(...)` API, and `BackgroundService` keeping the snapshot fresh via SSE + polling fallback.
- Sample apps updated: `SelfHosted.Sample` (server + dashboard + in-memory store) and `WebApi.Sample` (SDK consumer with `/checkout` endpoint).
- 22 tests covering Evaluator behavior, FlagClient eval paths, admin/SDK auth, ETag negotiation, and the end-to-end "create-flag-then-read-from-SDK" round-trip.
- **`Featly.Storage.Sqlite`**: EF Core-backed persistent storage. Internal `FeatlyDbContext` with `Project`, `Environment`, `Flag` tables. `Variants` stored as an owned JSON column; `Tags` as a JSON primitive collection. Initial migration `InitialCreate`. Pooled `IDbContextFactory<FeatlyDbContext>` so the singleton sub-stores allocate context per operation. `SqliteAutoMigrationHostedService` applies pending migrations at boot when `AutoMigrate=true`. `services.AddFeatlySqliteStore(opts => opts.ConnectionString = "...")` DI extension; options bind from `Featly:Storage:Sqlite`.
- 10 SQLite store tests covering schema migration, Project/Environment uniqueness, Flag round-trip with variants and tags, archive semantics, and ChangeNotifier pub/sub.

### Changed

- `samples/SelfHosted.Sample` now uses `AddFeatlySqliteStore` by default (`Data Source=featly.db`) — the Hangfire-style quickstart now has real persistence. `Featly.Storage.InMemory` is still referenced for the swap-in-one-line alternative.
- Centrally-managed NuGet versions: pinned `System.Security.Cryptography.Xml` to `10.0.8` to dodge the vulnerable `9.0.0` pulled in transitively by EF Core Design (GHSA-37gx-xxp4-5rgx, GHSA-w3x6-4m5h-cxqf).

### Deprecated

_(nothing yet)_

### Removed

_(nothing yet)_

### Fixed

_(nothing yet)_

### Security

_(nothing yet)_

---

<!--
Release entries follow this shape:

## [0.1.0] - YYYY-MM-DD

### Added
- ...

### Changed
- ...

[Unreleased]: https://github.com/Featly-net/Featly/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/Featly-net/Featly/releases/tag/v0.1.0
-->

[Unreleased]: https://github.com/Featly-net/Featly/compare/v0.0.2-preview.1...HEAD
[0.0.2-preview.1]: https://github.com/Featly-net/Featly/releases/tag/v0.0.2-preview.1
