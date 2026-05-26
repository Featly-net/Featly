# Changelog

All notable changes to Featly will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Until version `1.0.0`, the public API is unstable and minor versions may introduce breaking changes. Breaking changes will be called out explicitly in the notes for each release.

## [Unreleased]

### Added

- **M3 PR 3A — targeting domain + storage.** New types in `Featly.Abstractions`: `ConditionOperator` (16 members), `Condition`, `Split`, `RuleOutcome`, `Rule`, `Segment`, plus the `IFeatlyContextAccessor` interface (the ASP.NET Core implementation lands in 3D). `Flag.Rules` ordered list. `ISegmentStore` contract on the storage facade. `Featly.Storage.InMemory` and `Featly.Storage.Sqlite` both ship segment stores. SQLite migration `AddRulesAndSegments` adds the `Rules` JSON column to `Flags` and the new `Segments` table. The shared `ConditionValueParser` helper round-trips `JsonElement` through raw JSON text inside owned JSON documents.
- 6 new tests in `Featly.Storage.Sqlite.Tests` covering segment round-trip, list ordering, upsert overwrite, idempotent delete, most-recent-update tracking, and a Flag-with-Rules round-trip exercising nested conditions and weighted splits (37 passing total).
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

[Unreleased]: https://github.com/Featly-net/Featly/commits/main
