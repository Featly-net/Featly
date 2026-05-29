# ADR-0012: System roles immutable; custom roles via clone-and-edit

- **Status:** Accepted
- **Date:** 2026-05-25
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

Featly ships four built-in roles (Viewer, Editor, Approver, Admin). If operators could edit these in place, an upgrade that adds a permission to a system role would conflict with local edits, and a mis-edit of Admin could lock everyone out. Yet operators legitimately need custom permission sets.

## Decision

The four **system roles are immutable**: they are seeded (and their permission set refreshed) on every boot, identified by stable keys, and cannot be edited or deleted via the API. Custom roles are created by **clone-and-edit** — copy a system role's permission set into a new, fully editable custom role. This keeps the built-ins authoritative and upgrade-safe while giving operators full flexibility.

## Alternatives considered

### Alternative 1 — fully editable system roles

Rejected: local edits collide with permission-set refreshes on upgrade; an Admin mis-edit is a lockout risk.

### Alternative 2 — system roles only (no custom roles)

Rejected: real orgs need bespoke permission sets the four built-ins don't cover.

## Consequences

### Positive

- Upgrades can add permissions to system roles safely; built-ins are a reliable baseline.
- Operators still get arbitrary custom roles.

### Negative

- "Edit Admin" is intentionally impossible — operators must clone first, which can surprise.

### Neutral

- System roles are re-seeded idempotently on boot; ids stay stable across restarts.

## References

- ARCHITECTURE.md §11 — RBAC
- `SystemRoles` in `Featly.Abstractions`
