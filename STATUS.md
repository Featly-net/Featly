# Current status

> Refreshed by maintainers whenever the active milestone changes.
> The full plan lives in [PLAN.md](PLAN.md). The design lives in [ARCHITECTURE.md](ARCHITECTURE.md).

## Active milestone

**M1 — Skeleton** (in progress)

### Goal

Solution structure exists, every project builds, CI runs, nothing useful happens yet.

### Done

- 11 `src/` projects (Abstractions, Engine, Sdk, AspNetCore, OpenFeature.Provider, Server, Dashboard, Storage.Abstractions, Storage.InMemory, Storage.Sqlite, Cli) with correct multi-targeting per `CLAUDE.md`
- 5 `tests/` projects (Engine.Tests, Sdk.Tests, Server.Tests, Storage.Sqlite.Tests, E2E.Tests) wired to xUnit v3 + FluentAssertions + NSubstitute
- 2 `samples/` projects (WebApi.Sample placeholder, SelfHosted.Sample functional)
- Build infrastructure: `global.json` (pin SDK 10.0.300), `.editorconfig`, `Directory.Build.props` (Nullable + TreatWarningsAsErrors + AnalysisLevel + SourceLink), `Directory.Packages.props` (CPM)
- Placeholder contract types: `Flag`, `Variant`, `FlagType`, `Config`, `ConfigType`, `EvaluationContext`, `EvaluationReason`, `EvaluationResult<T>`, `IFeatlyClient`, `IFeatlyStore`
- `Featly.Server` exposes `GET /health/live`
- `Featly.Dashboard` middleware serves an embedded "coming soon" page at `/featly`
- `Featly.Storage.InMemory` no-op store boots the server
- CI workflow (`.github/workflows/ci.yml`): matrix [ubuntu, windows] x [.NET 8 + 10 SDKs], restore/build/test/pack-preview

### Verified (Done-when criteria)

- `dotnet run --project samples/SelfHosted.Sample` starts the host
- `GET /health/live` returns 200 with `{"status":"live"}`
- `GET /featly` returns 200 with the placeholder HTML
- `dotnet test` passes (5 smoke tests)

### Not in scope for M1 (deferred to later milestones)

- Any real evaluation logic — comes in M2 (boolean flag end-to-end) and M3 (rules, conditions, segments, bucketing)
- Real SDK client surface (`IFlagClient`, `IConfigClient`, `IEventClient`) — M2 onward
- EF Core migrations and SQLite schema — M2
- Auth, RBAC, custom roles, approval workflows, webhooks, experiments, OpenFeature provider implementation — M6 through M11
- CLI commands (`db migrate`, `env lock`, ...) — M12

## Open follow-ups

- `CODE_OF_CONDUCT.md` referenced by `CONTRIBUTING.md` but not yet added
- ADR on testing library choice (FluentAssertions 7.x → migrate to Shouldly or AwesomeAssertions before bumping past 8.x)
