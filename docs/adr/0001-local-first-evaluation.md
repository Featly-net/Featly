# ADR-0001: Local-first evaluation in the SDK; server provides configuration only

- **Status:** Accepted
- **Date:** 2026-05-25
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

Feature-flag evaluation sits on the application's hot path — sometimes per request, sometimes per loop iteration. A design that calls the server on each evaluation couples application latency and availability to the flag service, the failure mode that makes teams distrust flag systems. ARCHITECTURE.md §1 makes "local-first evaluation" and "resilient by default" core principles.

## Decision

The SDK evaluates flags and configs **locally**, against a cached, fresh-by-default snapshot of the environment's configuration. The server's only job is to serve that snapshot (`GET /api/sdk/config`) and to push change notifications (SSE) so the cache stays fresh. There is **no network call on the evaluation path**. The same `Featly.Engine` code evaluates on both the SDK and the server, so results are consistent by construction.

## Alternatives considered

### Alternative 1 — server-side evaluation (evaluate-on-request)

The SDK sends the context to the server, which evaluates and returns the result. Rejected: every evaluation pays a network round-trip and fails when the server is down — exactly the latency/availability coupling we want to avoid.

### Alternative 2 — local evaluation, but block first use until the snapshot loads

Rejected as the only option: the SDK additionally supports bootstrapping from a static JSON file and serving last-known-good config, so a cold or offline start still evaluates.

## Consequences

### Positive

- Evaluation is in-process and sub-microsecond (see [PERFORMANCE.md](../PERFORMANCE.md)).
- The app keeps working (last-known-good) when the server is unreachable.
- Consistent results across SDK and server — one engine.

### Negative

- A change is visible only after the snapshot refreshes (polling interval or SSE push), not instantly everywhere.
- The SDK carries a cache and a sync background service — more moving parts than a thin HTTP client.

### Neutral

- The server must expose a snapshot shape (`ConfigSnapshot`) and an ETag/SSE invalidation path.

## References

- [ARCHITECTURE.md §1 — Architectural principles](../../ARCHITECTURE.md)
- ARCHITECTURE.md §19 — Performance targets
