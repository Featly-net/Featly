# ADR-0010: Sticky assignments opt-in per experiment

- **Status:** Accepted
- **Date:** 2026-05-25
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

When an experiment's variant weights change mid-flight, deterministic bucketing ([ADR-0004](0004-murmurhash3-bucketing.md)) will re-bucket some subjects, migrating them to a different variant. For pure rollouts that is fine; for experiments measuring conversion it corrupts the analysis, because a subject's exposure must stay on one variant. But stickiness costs persistence (an `Assignment` row per subject), so it should not be forced on every flag.

## Decision

Stickiness is **opt-in per experiment** via a flag on the `Experiment`. When enabled, a subject is **pinned to the first variant it was exposed to** (first-write-wins): the SDK applies a process-local pin and the server persists the authoritative `Assignment` on event ingest, so a later weight change does not migrate already-exposed subjects. Non-sticky flags keep the cheap, stateless bucketing path.

## Alternatives considered

### Alternative 1 — always sticky

Rejected: forces an assignment store and write per subject on every flag, even simple rollouts that don't need it.

### Alternative 2 — never sticky (pure bucketing)

Rejected: weight changes during a running experiment would silently corrupt conversion analysis.

## Consequences

### Positive

- Experiments get correct, stable attribution; plain flags pay nothing.
- The decision is explicit and visible on the experiment.

### Negative

- Two code paths (pinned vs stateless) to maintain and test.
- Sticky experiments accumulate assignment rows.

### Neutral

- The SDK pin is process-local; the server holds the durable assignment.

## References

- ARCHITECTURE.md §16 — Experiments
- [ADR-0004](0004-murmurhash3-bucketing.md) — bucketing
