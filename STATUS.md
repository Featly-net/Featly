# Current status

> Refreshed by maintainers whenever the active milestone changes.
> The full plan lives in [PLAN.md](PLAN.md). The design lives in [ARCHITECTURE.md](ARCHITECTURE.md).

## Active milestone

**M2 — First vertical slice** (mostly complete; SQLite persistence to follow as a fast-follow PR)

### Goal

A boolean flag, evaluated locally by the SDK, served by the server, persisted in storage. Proves the architecture end-to-end.

### Done

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
- **Tests** (22 passing): Engine, SDK FlagClient, Server admin/SDK endpoints with auth, E2E boolean flag round-trip via TestServer

### Done-when criteria (PLAN.md M2)

- [x] End-to-end test passes consistently (Featly.E2E.Tests.BooleanFlagEndToEndTests)
- [x] A developer can create a boolean flag via HTTP and the sample app's `IsEnabledAsync` reflects it within a polling interval
- [ ] Persisted in SQLite — **pending; tracked as M2 follow-up PR**

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

- **M2 fast-follow PR**: `Featly.Storage.Sqlite` with `FeatlyDbContext` (internal), EF Core configurations, Initial migration, sub-stores; `AddFeatlySqliteStore(opts => opts.UseConnectionString(...))`
- `CODE_OF_CONDUCT.md` referenced by `CONTRIBUTING.md` but not yet added
- ADR on testing library choice (FluentAssertions 7.x → migrate to Shouldly or AwesomeAssertions before bumping past 8.x)
- ADR on database-overrides-config settings provider (M2 introduces server options bound from config only; DB-overrides logic lands once `ISystemSettingsStore` exists in M6+)
