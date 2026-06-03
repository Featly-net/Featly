# ADR-0024: Modular feature areas via DI toggles

- **Status:** Accepted
- **Date:** 2026-06-03
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

Featly bundles a broad surface — flags, configs, segments, experiments, RBAC, approvals, webhooks, audit, settings. Many consumers only want a subset: feature flags only, or dynamic configs only, without the heavier governance pieces. Today `AddFeatlyServer()` + the dashboard light up everything, so a flags-only consumer still gets config/experiment/approval endpoints and dashboard screens they never use — more surface to reason about, secure, and document (issue #112).

We want the product to be **modular**: a consumer enables only the feature areas they need, keeping the footprint small. This must respect the project principles (ARCHITECTURE.md §1): *predictable, small APIs* and *embedded like Hangfire* (two DI calls stay enough). It also must not become a breaking change for existing consumers.

Forces:

- The evaluation **engine** and **storage facade** are shared and cheap; the cost we want to trim is the **HTTP surface** and the **dashboard surface**, plus the cognitive/security surface.
- The SDK and storage providers are **already** modular at the package level (`Featly.Sdk`, `Featly.Storage.*`). The gap is the server/dashboard feature areas.
- Splitting every area into its own NuGet package multiplies the maintenance and versioning burden for a pre-1.0 project.

## Decision

We will make feature areas **opt-out toggles on `FeatlyServerOptions.Features`**, gated at DI/registration time, in the existing single server package. `AddFeatlyServer(o => { o.Features.Configs = false; o.Features.Experiments = false; ... })` conditionally maps the corresponding endpoint groups and hides the matching dashboard nav entries, routes, and command-palette entries. **Defaults are everything-on**, so existing consumers and the samples are unaffected (no breaking change). A small set of always-on core areas (health, auth, projects/environments, settings) cannot be disabled because the rest depends on them.

Gating is **surface-level**: disabling an area removes its endpoints and screens; it does **not** drop the storage tables (the schema stays uniform across deployments, so enabling an area later needs no migration). The dashboard reads the enabled set from a tiny bootstrap endpoint (or an injected config blob) and renders only what is on. The OpenFeature provider is flags-centric and unaffected.

This is a **hybrid** stance (the chosen direction): DI toggles ship now; package-level modularity (separate `Featly.Server.Experiments` etc.) is **not** adopted now but can be layered on later for the heaviest areas if a real footprint need appears, without changing the toggle API.

## Alternatives considered

### Alternative 1 — Package-level split

Split each feature area into its own NuGet package so unused code is never referenced (smallest possible deploy).

Rejected for now. It multiplies project/versioning/maintenance overhead for a pre-1.0 codebase and creates a large breaking-change surface, for a benefit (a few MB of unreferenced IL) that rarely matters in practice. The DI-toggle approach delivers the real wins (smaller HTTP/UI/cognitive surface) with one package. A future split can build on top without changing the toggle API.

### Alternative 2 — Opt-in core (everything off by default)

Make consumers explicitly enable each area, starting from a minimal core.

Rejected. It is a breaking change for every existing consumer and the samples, and it contradicts the Hangfire-style "two calls and you are operational" quickstart. Defaults stay everything-on.

### Alternative 3 — Do nothing

Keep the full surface always on.

Rejected — it is exactly the gap #112 raises: flags-only / configs-only consumers carry endpoints and screens they never use.

## Consequences

### Positive

- Flags-only / configs-only (and granular) deployments expose only the endpoints and dashboard screens they use — smaller HTTP, UI, security, and docs surface.
- One package, one version, one mental model; the quickstart is unchanged.
- No schema divergence: the storage shape is identical regardless of toggles, so enabling an area later is free.

### Negative

- The server must thread an "is this area enabled" check through endpoint mapping, and the dashboard must gate nav/routes/palette — a cross-cutting concern to keep consistent as new areas are added.
- Disabled areas still ship their code (IL) in the assembly; the deploy size is unchanged (only the active surface shrinks).

### Neutral

- Storage tables for disabled areas remain in the schema, unused.
- A future package-level split remains possible on top of the toggle API.

## Implementation notes

Sliced as follow-ups to this ADR (issue #112): (1) `FeatlyServerOptions.Features` + gate the endpoint-group mapping in `MapFeatlyApi`; (2) expose the enabled set to the dashboard and gate nav/routes/command palette; (3) docs. Defaults remain everything-on throughout.

## References

- ARCHITECTURE.md §1 (principles: predictable/small APIs, embedded like Hangfire), §3 (package layout), §8 (HTTP API), §9 (dashboard)
- Issue #112
