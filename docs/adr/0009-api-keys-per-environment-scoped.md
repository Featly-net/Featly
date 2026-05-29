# ADR-0009: API keys scoped per environment with SdkRead / AdminWrite scope

- **Status:** Accepted
- **Date:** 2026-05-25
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

Featly accepts API keys for two very different audiences: SDK clients that read a snapshot, and operators/CI that mutate definitions. A single all-powerful key conflates these and makes blast radius large. Keys also need to be scoped to an environment so a `development` SDK key can't read `production`.

## Decision

An `ApiKey` carries an **`ApiKeyScope` of `SdkRead` or `AdminWrite`** and is **scoped to a single environment** (`EnvironmentId`). The store keeps only an Argon2id hash plus an indexed prefix for O(log n) lookup; the plaintext is shown once at creation. The SDK auth scheme accepts `SdkRead`; the admin scheme accepts `AdminWrite`. (M12 extends keys with an optional user binding — see [ADR-0023](0023-user-bound-api-keys.md).)

## Alternatives considered

### Alternative 1 — one key, full access

Rejected: a leaked SDK key would grant mutation; no environment isolation.

### Alternative 2 — fine-grained per-permission keys

Rejected for v1: two coarse scopes cover the read/write split; finer control comes from binding a key to a user whose RBAC roles apply.

## Consequences

### Positive

- Small blast radius: an SDK key reads one environment and cannot mutate.
- Hash-only storage; constant-time verification.

### Negative

- A truly cross-environment admin tool needs a key per environment, or a user-bound admin key whose role assignments span environments.

### Neutral

- The two scopes map directly to the two ASP.NET Core authentication schemes.

## References

- ARCHITECTURE.md §10 — Auth / API keys
- [ADR-0023](0023-user-bound-api-keys.md) — user-bound API keys
