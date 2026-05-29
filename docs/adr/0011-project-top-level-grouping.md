# ADR-0011: Project as the first-class top-level grouping above Environment

- **Status:** Accepted
- **Date:** 2026-05-25
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

Flags live in environments (`development`, `production`). But an organization runs more than one application, each with its own set of environments, and RBAC needs a scope above the environment to grant "admin on app X across all its environments". Without a top-level grouping, environments become a flat namespace and cross-environment role scoping has nothing to attach to.

## Decision

**`Project` is a first-class entity above `Environment`.** A project owns a set of environments; flags/configs/segments are scoped to an environment within a project. Role assignments scope to a project and optionally to one environment (wildcard = all environments in the project — see [ADR-0013](0013-polymorphic-role-assignment.md)). A default project + environment is auto-created on first boot ([ADR-0019](0019-auto-create-default-project-environment.md)) so the single-app case needs no setup.

## Alternatives considered

### Alternative 1 — flat environments, no project

Rejected: no scope for "admin across all environments of one app"; multi-app installs collide in a flat namespace.

### Alternative 2 — tenant above project

Deferred: multi-tenant cloud mode is post-1.0; project is the right granularity for the embedded/centralized cases now.

## Consequences

### Positive

- RBAC can scope to a project (all its environments) or a single environment.
- Multiple applications coexist cleanly.

### Negative

- One more level of hierarchy to model, migrate, and surface in the UI/API.

### Neutral

- The single-app user mostly ignores projects thanks to the auto-created default.

## References

- ARCHITECTURE.md §5, §11 — Domain model / RBAC
- [ADR-0013](0013-polymorphic-role-assignment.md), [ADR-0019](0019-auto-create-default-project-environment.md)
