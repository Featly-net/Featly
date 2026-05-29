# ADR-0020: Bootstrap admin via appsettings with DB override

- **Status:** Accepted
- **Date:** 2026-05-28
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

RBAC has a chicken-and-egg problem: you need an admin to grant the first admin role, but a fresh install has no admins. There must be a way to designate the first administrator without already being one, and it must not leave a permanent god-mode backdoor in normal operation.

## Decision

A **bootstrap admin** is designated by `Featly:Authorization:BootstrapAdminIdentifier` (a bootstrap-only setting, [ADR-0016](0016-database-overrides-appsettings.md)). On boot, if set and the user doesn't exist, the user row is seeded; the permission checker treats that identifier (and the legacy static admin key) as admin via a hardcoded shortcut, so it works before any role assignment exists. Once real users and assignments exist, normal RBAC resolution applies. M12 adds an alternative, credential-free path — the guarded `POST /api/admin/bootstrap` endpoint and `featly bootstrap-admin`, available only while zero users exist ([ADR-0023](0023-user-bound-api-keys.md)).

## Alternatives considered

### Alternative 1 — a permanent superadmin account

Rejected: a standing god-mode account is a security liability; the bootstrap is meant to be transitional.

### Alternative 2 — manual SQL seed

Rejected: requires DB access and knowledge of the schema; poor onboarding.

## Consequences

### Positive

- A fresh install can designate its first admin from config alone, no prior credential.
- The shortcut is narrow (one configured identifier + the legacy key) and disappears behind normal RBAC once users exist.

### Negative

- The bootstrap identifier is effectively admin as long as it's configured; operators should promote a real user and can then drop it.

### Neutral

- Two bootstrap paths now exist (config identifier; the zero-users endpoint) for different operational styles.

## References

- ARCHITECTURE.md §10, §11 — Auth / RBAC
- [ADR-0016](0016-database-overrides-appsettings.md), [ADR-0023](0023-user-bound-api-keys.md)
- `AuthBootstrapHostedService`, `BootstrapEndpoints`
