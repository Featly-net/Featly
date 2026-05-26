# ADR-0016: Database overrides `appsettings` for runtime-editable settings

- **Status:** Accepted
- **Date:** 2026-05-26
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

Featly needs settings that operators can change while the system is running — approval defaults, webhook retry policy, authorization mode, bootstrap admin identifier, rate limits — and settings that must be present before the application can even boot — connection strings, Kestrel URLs, auto-migrate flag.

Two conflicting forces:

1. **Boot needs static config.** The host must read a connection string from `appsettings.json` (or environment variables) before the database is reachable. Anything required to open the database itself cannot live in the database.
2. **Operators need live config.** Once Featly is running, operators editing approval policy or webhook retry behavior expect the change to take effect without redeploying. Forcing them to redeploy for a UI-editable setting is a poor experience and conflicts with ARCHITECTURE.md §1 principle "Predictable, not magical".

A naive "everything in `appsettings`" model fails the second case. A naive "everything in the database" model fails the first. We need an explicit precedence rule.

## Decision

Settings resolve through a three-layer precedence:

1. **Hardcoded defaults** — the lowest layer. Every setting has a sensible default baked into the code.
2. **`appsettings.json` / environment variables** — overrides hardcoded defaults at startup. The standard ASP.NET Core configuration pipeline.
3. **Database** — overrides both prior layers for settings the operator is allowed to edit at runtime. The highest layer.

The boundary between "appsettings-only" and "DB-overridable" is explicit:

- **Bootstrap-only settings** (cannot live in DB): connection strings, `AutoMigrate` flag, Kestrel URLs, bootstrap admin identifier. These are read before the DB connection exists.
- **DB-overridable settings**: every setting that appears in the dashboard's Settings screen, plus anything described as "settings" in ARCHITECTURE.md (approval defaults, webhook retry, authorization mode, audit policy, etc).

The mechanism: an `ISystemSettingsStore` (lands in M6 alongside the auth pipeline) reads from a `SystemSettings` table that mirrors the strongly-typed options objects. A composite `IOptionsMonitor<T>` wrapper consults the store first, falls back to the standard configuration pipeline.

`appsettings` therefore acts as the **bootstrap baseline** — what the system uses on first boot, before any operator has touched the dashboard. Once a setting is edited in the UI, the DB row wins until it's explicitly deleted.

## Alternatives considered

### Alternative 1 — `appsettings` only

Keep everything in `appsettings.json`. Force operators to redeploy for every settings change.

Rejected. The product positions itself against vendors like LaunchDarkly and Flagsmith where settings *are* runtime-editable. Forcing redeploys for approval policy edits is a non-starter.

### Alternative 2 — Database only, with a separate bootstrap file

Move every setting to the database. Read a tiny `featly-bootstrap.json` only for the connection string and `AutoMigrate`.

Rejected. ASP.NET Core developers expect `appsettings.json` + `IConfiguration` + environment variables as the standard config surface. Inventing a parallel bootstrap file for two values is more friction than letting the standard pipeline carry both bootstrap and pre-database defaults.

### Alternative 3 — `appsettings` wins; DB used for "operator overrides" via a separate API

Treat `appsettings` as authoritative; expose a separate "operator overrides" API that consumers explicitly opt into.

Rejected. Two parallel settings stores with merge semantics confuses operators ("did I edit the right one?"). The single, last-writer-wins-DB model is easier to reason about.

## Consequences

### Positive

- Operators edit settings in the dashboard and see them apply without redeploying.
- Bootstrap is preserved: the system can always boot from `appsettings` even with an empty database.
- `appsettings.json` documents the full surface of overridable settings as a side effect — developers reading the file see what's configurable.
- Reset-to-default is trivial (delete the DB row).

### Negative

- Two sources of truth for the same logical setting. Operators must understand "DB wins, but I can see the baseline in `appsettings`".
- The `IOptionsMonitor<T>` wrapper adds complexity to the options pipeline. Refreshing values when a DB row changes requires either polling or a change-notification subscription.
- Restoring a setting to its `appsettings` value requires deleting the DB row, not editing it. The dashboard must surface a "reset to default" affordance.

### Neutral

- Settings that should never be edited at runtime (connection strings) must be explicitly excluded from the `ISystemSettingsStore`. The exclusion lives in code, not config, so adding a new bootstrap-only setting requires a code change.

## Implementation notes

- Lands in **M6** alongside the auth pipeline and `ISystemSettingsStore`.
- Until M6, server options bind only from `appsettings` (no DB layer yet). The composite `IOptionsMonitor<T>` wrapper is the M6 deliverable that unlocks the third layer.
- Each settings object documents whether it is "bootstrap-only" or "DB-overridable" in its XML doc.
- The DB schema uses one row per top-level options class (`OptionsKey` PK), value stored as JSON. Strongly-typed binding happens on read.

## References

- [ARCHITECTURE.md §1 — Architectural principles](../../ARCHITECTURE.md#1-architectural-principles) — "DB beats config" principle
- ARCHITECTURE.md §10 — Settings precedence
- [.NET options pattern](https://learn.microsoft.com/en-us/dotnet/core/extensions/options)
- ADR-019 — Auto-create default Project and Environment on first boot (depends on bootstrap-only settings)
- ADR-020 — Bootstrap admin via `appsettings` with DB override (same precedence model applied to a single setting)
