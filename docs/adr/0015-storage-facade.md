# ADR-0015: IFeatlyStore as a facade with per-entity sub-stores

- **Status:** Accepted
- **Date:** 2026-05-25
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

All persistence must go through an interface so application code never depends on a storage engine ([ADR-0006](0006-ef-core-internal-dbcontext.md)). A single fat `IFeatlyStore` with every method for every entity would grow unwieldy and force each provider to implement one enormous interface; scattering many top-level interfaces with no relationship makes DI registration and discovery harder.

## Decision

`IFeatlyStore` is a **facade exposing focused per-entity sub-stores** — `IFlagStore`, `IConfigStore`, `ISegmentStore`, `IUserStore`, `IRoleStore`, `IApiKeyStore`, `IWebhookStore`, `IAuditStore`, etc. Consumers reach `store.Flags.UpsertAsync(...)`, `store.Audit.AppendAsync(...)`. Each sub-store is a small contract a provider implements independently; the facade composes them.

## Alternatives considered

### Alternative 1 — one fat IFeatlyStore interface

Rejected: dozens of unrelated methods on one type; painful to implement and read.

### Alternative 2 — many independent top-level stores, no facade

Rejected: every consumer injects a long list of stores; harder to discover and register.

## Consequences

### Positive

- Focused contracts (interface-segregation); the facade is the single injection point.
- A provider implements small, testable sub-stores.

### Negative

- Adding an entity adds a sub-store interface, an implementation per provider, and a facade property.

### Neutral

- The facade and sub-stores live in `Featly.Storage.Abstractions`; providers implement them.

## References

- ARCHITECTURE.md §3, §8 — Solution structure / storage
- [ADR-0006](0006-ef-core-internal-dbcontext.md) — DbContext internal
