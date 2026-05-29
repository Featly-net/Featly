# ADR-0002: Dashboard served as embedded static resources, no Blazor

- **Status:** Accepted
- **Date:** 2026-05-25
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

Featly embeds inside the consumer's ASP.NET Core process (the Hangfire model). The dashboard must mount with a single `MapFeatlyDashboard(...)` call and add no heavy runtime, framework version constraint, or build step to the host. A SPA framework (Blazor, React) would drag a runtime and a toolchain into a library that is supposed to be a drop-in.

## Decision

The dashboard ships as **static resources (HTML/CSS/vanilla JS) embedded in the `Featly.Dashboard` assembly** and served by a middleware mount. No Blazor, no Node build step, no client framework. Everything the dashboard does is reachable through the same HTTP API the dashboard itself calls — the UI is a thin client over the admin API.

## Alternatives considered

### Alternative 1 — Blazor (Server or WASM)

Rejected: Blazor Server adds a stateful circuit and SignalR dependency; Blazor WASM adds a multi-MB runtime download. Both impose framework/runtime coupling on the host.

### Alternative 2 — a React/Vue SPA with a bundler

Rejected: introduces a Node build step into a .NET library's release pipeline and ships a large JS bundle for a primarily form-driven admin UI.

## Consequences

### Positive

- Zero framework/runtime coupling; the dashboard is just static files + the API.
- Small footprint, instant mount, no build step in the consumer.
- Forces the "everything in the UI is in the API" discipline (a project rule).

### Negative

- Richer interactions are more work in vanilla JS than in a component framework.
- No component ecosystem; charts are CSS-only.

### Neutral

- The dashboard's capabilities are bounded by the admin API surface — by design.

## References

- [ARCHITECTURE.md §1 — Architectural principles](../../ARCHITECTURE.md) — "Embedded like Hangfire"
- ARCHITECTURE.md §14 — Dashboard
