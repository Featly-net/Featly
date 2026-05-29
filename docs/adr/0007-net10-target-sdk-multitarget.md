# ADR-0007: .NET 10 primary target; client packages multi-target net8.0 + net10.0

- **Status:** Accepted
- **Date:** 2026-05-25
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

The server and tooling can demand the latest runtime, but the SDK and the packages a consumer references must run in applications that are not yet on .NET 10. Forcing every consumer onto net10.0 would shrink the addressable audience for a library whose whole point is to drop into existing apps.

## Decision

`.NET 10` is the primary target. The **consumer-facing packages multi-target `net8.0;net10.0`**: `Featly.Abstractions`, `Featly.Engine`, `Featly.Sdk`, `Featly.AspNetCore`, `Featly.OpenFeature.Provider`. The **server, dashboard, storage providers, and CLI target `net10.0` only** — they are deployed, not embedded into someone else's older app.

## Alternatives considered

### Alternative 1 — net10.0 everywhere

Rejected: excludes apps still on net8.0 (the current LTS) from using the SDK.

### Alternative 2 — also target netstandard2.0

Rejected: netstandard2.0 lacks APIs the engine relies on and drags in polyfills; net8.0 is a low-enough floor for the supported audience.

## Consequences

### Positive

- net8.0 LTS apps can adopt the SDK today; net10.0 apps get the latest.
- Server/CLI use the newest runtime features freely.

### Negative

- Multi-targeted projects build and test against two TFMs (CI matrix cost).
- Code in multi-targeted projects avoids net10-only APIs or guards them with `#if`.

### Neutral

- `Microsoft.Extensions.*` are pinned at versions compatible with both TFMs (see `Directory.Packages.props`).

## References

- `Directory.Packages.props`, `Directory.Build.props`
- ARCHITECTURE.md §2 — Multi-targeting
