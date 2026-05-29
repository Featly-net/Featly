# ADR-0023: API keys may bind to a real user; persisted keys authenticate over Bearer

- **Status:** Accepted
- **Date:** 2026-05-29
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

Through M11, an API key authenticated as an anonymous pseudo-identity (`api-key:AdminWrite`). The approval workflow ([ADR-0017](0017-approval-pending-change-entity.md)) needs a *real* approver, so an action taken with a key could not be attributed to a person, and a key could not be a real approver — a known limitation. Separately, persisted `ApiKey` rows ([ADR-0009](0009-api-keys-per-environment-scoped.md)) authenticated only for the dashboard cookie login, not over `Authorization: Bearer`, so a minted key could not be used by the CLI or SDK.

## Decision

An `ApiKey` gains an optional **`UserId` binding**. When set, a request authenticated with that key resolves to the bound user's identifier, so RBAC, audit, and approvals attribute the action to a real person — the key's effective permissions are the bound user's role assignments, never a blanket grant. The **Bearer authentication handler now validates persisted keys** (prefix lookup + Argon2id verify, filtered to the scheme's scope) in addition to the static appsettings key, so minted keys authenticate over `Authorization: Bearer`. New endpoints mint keys (`POST /api/admin/apikeys`) and provision the first admin without a credential (guarded `POST /api/admin/bootstrap`, valid only while zero users exist).

## Alternatives considered

### Alternative 1 — keys carry their own permission set (no user link)

Rejected: re-implements RBAC on the key and still leaves no real identity for approvals.

### Alternative 2 — a permanent superadmin key

Rejected: standing god-mode credential; the bootstrap is meant to be transitional and identity-backed.

## Consequences

### Positive

- Closes the M8 limitation: actions via a key attribute to a real user; a key can act as an approver.
- A minted key works over Bearer for the CLI and SDK; permissions follow the bound user's RBAC.
- A fresh server can mint its first admin credential with no prior secret.

### Negative

- The Bearer path may do a DB lookup + Argon2 verify when the static key doesn't match (only for persisted-key requests; cookie/static-key requests are unaffected).
- Migration `AddApiKeyUserBinding` adds a nullable `UserId` column.

### Neutral

- An unbound key remains a service principal; binding is optional.

## References

- ARCHITECTURE.md §10, §11 — Auth / RBAC
- [ADR-0009](0009-api-keys-per-environment-scoped.md), [ADR-0017](0017-approval-pending-change-entity.md), [ADR-0020](0020-bootstrap-admin-appsettings-db-override.md)
- `FeatlyApiKeyAuthenticationHandler`, `AdminApiKeysEndpoints`, `BootstrapEndpoints`
