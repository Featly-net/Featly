# ADR-0004: MurmurHash3 (32-bit) for deterministic bucketing

- **Status:** Accepted
- **Date:** 2026-05-25
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

Percentage rollouts and experiment splits need a stable mapping from a subject key to a bucket in `[0, 100)` (or `[0, 10000)`), such that the same subject always lands in the same bucket, the distribution is uniform, and the SDK and server agree without coordination. A cryptographic hash is overkill and slow; `string.GetHashCode()` is randomized per process and unstable across runtimes.

## Decision

Bucketing uses **MurmurHash3 (32-bit), implemented in-house** in `Featly.Engine`, over a canonical UTF-8 string (`subjectKey:flagKey:salt`). The same implementation runs in the SDK and the server, so bucket assignment is deterministic and identical on both sides without a shared service.

## Alternatives considered

### Alternative 1 — `string.GetHashCode()`

Rejected: randomized per process since .NET Core; not stable across runs or runtimes.

### Alternative 2 — a cryptographic hash (SHA-256)

Rejected: far slower than needed for a non-security mapping; MurmurHash3 gives uniform distribution at a fraction of the cost.

## Consequences

### Positive

- Deterministic, uniform, fast (sub-microsecond), no allocations beyond the key encode.
- SDK and server agree by construction — no central bucketing service.

### Negative

- An in-house hash is code we own and must test for distribution and cross-platform stability (covered by engine tests).

### Neutral

- The salt and key composition are part of the wire contract — changing them re-buckets everyone, so they are fixed.

## References

- ARCHITECTURE.md §6 — Evaluation engine / bucketing
- [PERFORMANCE.md](../PERFORMANCE.md)
