# ADR-0013: Polymorphic RoleAssignment (User | Group) with wildcard environment

- **Status:** Accepted
- **Date:** 2026-05-25
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

A role can be granted to an individual user or to a group of users, and to a single environment or to every environment in a project. Modeling user-grants and group-grants as separate tables, or environment-scoped and project-scoped grants separately, multiplies the join logic the permission checker must run on the hot path.

## Decision

A single **`RoleAssignment`** joins a role to an assignee, with an **`AssigneeType` discriminator (`User` | `Group`)** and an `AssigneeId`. It scopes to a `ProjectId` and an **optional `EnvironmentId` (null = wildcard, all environments in the project)**. The permission checker expands a user into the set of assignee ids (itself + its groups), lists their assignments, and unions the roles whose scope matches the request — one uniform resolution path.

## Alternatives considered

### Alternative 1 — separate UserRoleAssignment and GroupRoleAssignment tables

Rejected: duplicates schema and resolution logic; the discriminator keeps it one table, one query.

### Alternative 2 — environment-required assignments (no wildcard)

Rejected: granting "admin across all environments" would require one row per environment and constant maintenance as environments are added.

## Consequences

### Positive

- One table, one resolution path for users and groups, single- and all-environment scopes.
- "Admin on this project everywhere" is one wildcard row.

### Negative

- A polymorphic `AssigneeId` has no FK to a single table; integrity is enforced in code.

### Neutral

- Cumulative union semantics ([ADR-0014](0014-cumulative-permissions-no-deny.md)) make overlapping assignments additive.

## References

- ARCHITECTURE.md §11 — RBAC
- `RoleAssignment` in `Featly.Abstractions`; `DefaultFeatlyPermissionChecker`
