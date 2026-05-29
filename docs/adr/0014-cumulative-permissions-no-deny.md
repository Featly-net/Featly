# ADR-0014: Cumulative permissions; no deny rules

- **Status:** Accepted
- **Date:** 2026-05-25
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

A user may hold several role assignments (directly and via groups, across scopes). The system must combine them into an effective permission set. Deny rules (an explicit "this role removes permission X") are powerful but make effective access hard to reason about: you can no longer answer "can this user do X?" by looking at what they were granted — you must also find everything that revokes it, with precedence rules.

## Decision

Effective permissions are the **union of all granted permissions** across every matching assignment — **more is more, with no deny rules**. If one assignment grants Viewer and another grants Admin, the user is effectively Admin. "Why does this user have access X?" is answered by listing the assignments that contributed it (the Effective Access view), with no hidden subtraction.

## Alternatives considered

### Alternative 1 — allow + deny with precedence

Rejected: deny rules make effective access non-obvious and order/precedence-dependent; hard to audit and explain.

### Alternative 2 — most-specific assignment wins

Rejected: introduces a specificity metric and discards other grants in confusing ways.

## Consequences

### Positive

- Effective access is explainable: it's the union, traceable to contributing assignments.
- The checker early-exits as soon as any matching role grants the asked-for permission.

### Negative

- You cannot carve out an exception ("Editor everywhere except can't delete in prod") with a deny; you model it by not granting, or by a narrower custom role.

### Neutral

- Pairs naturally with first-match-wins flag rules ([ADR-0005](0005-first-match-wins-rules.md)) — both favor predictability over expressive-but-opaque combination.

## References

- ARCHITECTURE.md §11 — RBAC
- `DefaultFeatlyPermissionChecker`; the Effective Access endpoint
