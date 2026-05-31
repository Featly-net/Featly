# Project context for AI assistants

This file is read by AI coding assistants (Claude Code, Cowork, Cursor, etc.) when they work on this repository. It captures the project's principles, conventions, and current state so the assistant can be immediately useful without re-discovering them.

## What is Featly

Featly is an open-source feature management platform for .NET. It combines feature flags, dynamic configuration, segments, experiments, custom RBAC, and approval workflows into a single product that can be embedded inside the consumer's ASP.NET Core process (like Hangfire) or hosted centrally.

The complete architectural design is in [ARCHITECTURE.md](ARCHITECTURE.md). When in doubt about how a component is supposed to work, **read that document first**. It is the source of truth.

## Project status

Early development. The architecture is documented; the implementation is starting. Public API is unstable. There are no releases yet.

## Architectural principles to respect

1. **Local-first evaluation.** The SDK evaluates flags and configs locally against a cached, fresh-by-default snapshot. There is no network call on the hot path. Never introduce server round-trips for evaluation.
2. **Embedded like Hangfire.** The server, dashboard, and SDK can all run inside the consumer's process. Two DI calls plus a middleware mount must be enough to be operational.
3. **OpenFeature is a first-class citizen.** The `Featly.OpenFeature.Provider` package implements the OpenFeature spec by delegating to `IFeatlyClient`. Never let internal API drift make the OpenFeature provider awkward.
4. **Storage is an interface.** All persistence goes through `IFeatlyStore` and its sub-stores. Application code (server, dashboard) never imports EF Core or any storage-specific type. The DbContext is `internal` to `Featly.Storage.Sqlite`.
5. **DB beats config.** Settings have three-layer precedence: hardcoded default → `appsettings.json` → database. Anything editable in the UI lives in the DB and overrides `appsettings`.
6. **Predictable, not magical.** Small APIs, explicit contracts. No required source generators. No compile-time reflection tricks.
7. **Resilient by default.** SDK serves last-known-good config if the server is unreachable. Bootstrap from a static JSON file is supported.

## Solution structure

Eleven projects, three logical layers (contracts → engine → integration), plus storage providers and tooling. See [ARCHITECTURE.md §3](ARCHITECTURE.md#3-solution-structure) for the full layout. Quick reference:

```
src/
  Featly.Abstractions/            # interfaces, models, contracts. Zero deps.
  Featly.Engine/                  # evaluation engine. Shared by SDK and server.
  Featly.Sdk/                     # client SDK. Light. No EF Core, no server code.
  Featly.AspNetCore/              # DI extensions and middleware adapters.
  Featly.OpenFeature.Provider/    # provider adapter for the OpenFeature spec.
  Featly.Server/                  # admin + SDK HTTP APIs, approval engine.
  Featly.Dashboard/               # embedded UI as static resources.
  Featly.Storage.Abstractions/    # IFeatlyStore + sub-stores.
  Featly.Storage.InMemory/        # in-memory store for tests.
  Featly.Storage.Sqlite/          # SQLite via EF Core (DbContext internal).
  Featly.Cli/                     # dotnet tool: migrations, lock/unlock, import.
```

## Dependency rules

- `Featly.Sdk` is **light**. No EF Core, no server-side code. Only Abstractions + Engine + HTTP client + System.Text.Json.
- `Featly.Engine` is **shared** between SDK and Server. Same code evaluates on both sides; consistency by construction.
- `Featly.Server` does **not** depend on `Featly.Sdk`. The server is not a client of itself.
- Storage providers are **independent packages**. Consumers pay only for what they reference.

## Multi-targeting

- `Featly.Abstractions`, `Featly.Engine`, `Featly.Sdk`, `Featly.AspNetCore`, `Featly.OpenFeature.Provider` → multi-target `net8.0;net10.0`.
- Server, dashboard, storage providers, CLI → `net10.0` only.

## Code conventions

- C# 12+ idioms. `required` members, primary constructors, file-scoped namespaces, target-typed `new()`.
- `sealed` by default for non-inheritance types. Use `sealed record` for value-like types.
- `ValueTask<T>` on hot paths (SDK evaluation methods). `Task<T>` everywhere else.
- Nullable reference types enabled in every project (`<Nullable>enable</Nullable>`).
- No exceptions for control flow. Use `EvaluationResult<T>` with `Reason` to carry outcomes.
- No allocations on the hot path for boolean flag evaluation. Use `ImmutableDictionary` for cache lookups.
- Domain entities have `Id` as `Guid` (`init`), audit timestamps as `DateTimeOffset`, JSON payloads as `JsonElement`.

## Naming conventions

- Project: `Featly.<Component>`. Examples: `Featly.Sdk`, `Featly.OpenFeature.Provider`.
- NuGet package IDs match project names.
- Public namespaces: `Featly`, `Featly.Sdk`, `Featly.AspNetCore`, etc.
- DI extension methods: `AddFeatly()`, `AddFeatlyServer()`, `MapFeatlyDashboard()`, `MapFeatlyApi()`.
- Domain entities: singular noun (`Flag`, `Config`, `RoleAssignment`).
- Sub-store interfaces: `IFlagStore`, `IConfigStore`, etc. — focused contracts.
- Permission enum members: `<Entity><Action>` (`FlagCreate`, `ChangeApprove`, `EnvironmentLock`).

## Testing conventions

- xUnit + AwesomeAssertions + NSubstitute for unit tests. (AwesomeAssertions is the Apache-2.0 fork of FluentAssertions; same idiom, `using AwesomeAssertions;`. See ADR-0021.)
- `WebApplicationFactory<T>` for server integration tests.
- End-to-end tests instantiate both the server and the SDK in-process and verify sync correctness.
- BenchmarkDotNet for hot-path microbenchmarks; results published on every release.

## What to avoid

- **Do not** call the server during flag evaluation. Always evaluate from the cached snapshot.
- **Do not** make `Featly.Server` depend on `Featly.Sdk`. Server has its own internal evaluation path via `Featly.Engine`.
- **Do not** expose EF Core types (`DbContext`, `DbSet`) outside `Featly.Storage.Sqlite`.
- **Do not** introduce new top-level concepts without an ADR in `docs/adr/`.
- **Do not** add UI features that are not also exposed via the HTTP API. Everything reachable in the dashboard must be reachable via API.
- **Do not** use emojis in code, comments, or documentation unless the user explicitly asks.
- **Do not** add a setting to `appsettings` without also making it overridable via the database, unless it is genuinely bootstrap-only (connection string, auto-migrate flag, kestrel URLs).
- **Do not** add `Co-Authored-By: Claude <noreply@anthropic.com>` (or any equivalent AI co-author trailer) to commit messages. Commits authored by an AI assistant on behalf of the maintainer are committed under the maintainer's identity only.

## Before merging a PR

CI green is necessary but not sufficient. **Always** check both before merging:

1. **CI status** — `gh pr checks <num> --repo Featly-net/Featly`. Every required check must be green.
2. **PR comments and reviews** — `gh pr view <num> --repo Featly-net/Featly --comments` plus `gh api repos/Featly-net/Featly/pulls/<num>/comments` (inline review comments). Read every unresolved comment, address actionable feedback, and reply to the rest before merging. Never merge over open review threads without surfacing them to the maintainer first.

This applies to every PR, including ones the assistant authored.

## When making architectural decisions

Document them as an ADR in `docs/adr/`. Use the template at `docs/adr/0000-template.md`. Number sequentially. Status flow: Proposed → Accepted → (Deprecated | Superseded by ADR-N).

## When in doubt

1. Re-read [ARCHITECTURE.md](ARCHITECTURE.md) for design intent.
2. Re-read [PLAN.md](PLAN.md) for sequencing and current focus.
3. Check existing ADRs in `docs/adr/`.
4. Ask the user before assuming. Default to the principles above when no explicit guidance exists.
