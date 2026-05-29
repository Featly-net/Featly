# ADR-0019: Auto-create a default Project and Environment on first boot

- **Status:** Accepted
- **Date:** 2026-05-28
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

`Project` is a first-class grouping above `Environment` ([ADR-0011](0011-project-top-level-grouping.md)), and flags require an environment to live in. For the single-application quickstart, forcing the developer to create a project and an environment before they can make their first flag is friction that conflicts with the Hangfire "it just works" goal.

## Decision

On first boot, a hosted service **auto-creates a default `Project` and a default `Environment`** (keys configurable via `Featly:Server:DefaultProjectKey` / `DefaultEnvironmentKey`, gated by `AutoCreateDefaultProject`). Admin write endpoints resolve the default environment when no `?env=` is supplied, so the single-app developer never has to think about projects or environments. The seeding is idempotent.

## Alternatives considered

### Alternative 1 — require explicit project/environment creation

Rejected: adds setup steps before the first flag; hurts the quickstart.

### Alternative 2 — implicit "default" with no real rows

Rejected: a real default row keeps the model uniform (everything is scoped to a real environment) and lets RBAC/scoping work without special cases.

## Consequences

### Positive

- Zero-setup quickstart; first flag works immediately against `default`/`development`.
- The model stays uniform — there's always a real project + environment.

### Negative

- An install that wants only custom-named environments gets an unused default unless `AutoCreateDefaultProject=false`.

### Neutral

- Default keys are bootstrap settings under `Featly:Server`.

## References

- ARCHITECTURE.md §5 — Domain model
- [ADR-0011](0011-project-top-level-grouping.md); `DefaultProjectBootstrapHostedService`
