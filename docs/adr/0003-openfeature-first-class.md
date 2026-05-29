# ADR-0003: OpenFeature provider as a first-class, day-one feature

- **Status:** Accepted
- **Date:** 2026-05-25
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

[OpenFeature](https://openfeature.dev) is the vendor-neutral standard for flag evaluation. Treating it as an afterthought tends to produce an awkward adapter that leaks the vendor's API shape. ARCHITECTURE.md §1 lists OpenFeature as a first-class citizen so adopters keep their options open.

## Decision

`Featly.OpenFeature.Provider` ships from day one and implements the OpenFeature spec by delegating to `IFeatlyClient`/`IFlagClient`. The internal API is designed so the provider stays a thin, faithful mapping (per-type resolve, spec-compliant reason/error mapping, context translation). The provider covers flags only; dynamic configuration stays on `IConfigClient` (a separate keyspace).

## Alternatives considered

### Alternative 1 — no OpenFeature, Featly-native API only

Rejected: locks adopters into Featly's API and ignores an emerging industry standard.

### Alternative 2 — OpenFeature as the *only* API

Rejected: OpenFeature is a flag spec; Featly's configs, experiments, and admin surface need a native API. We offer both — native richness plus a neutral escape hatch.

## Consequences

### Positive

- Adopters can write to the OpenFeature API and swap providers without touching call sites.
- Forces the internal API to stay clean enough to map cleanly.

### Negative

- A second public surface to keep in sync as the engine evolves.
- OpenFeature's value model (int = 32-bit, no native config concept) constrains what the provider can express.

### Neutral

- Configs are explicitly out of scope for the provider.

## References

- [docs/OPENFEATURE.md](../OPENFEATURE.md)
- ARCHITECTURE.md §18 — OpenFeature provider
- [OpenFeature .NET SDK](https://openfeature.dev/docs/reference/sdks/server/dotnet/)
