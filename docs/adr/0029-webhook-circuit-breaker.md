# ADR-0029: Webhook delivery circuit breaker

- **Status:** Accepted
- **Date:** 2026-07-15
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

`WebhookDeliveryWorker` already retries failed deliveries with exponential
backoff and dead-letters them after the attempt budget (ARCHITECTURE.md §17).
That protects an individual delivery, but not the queue: a consistently-failing
endpoint keeps producing due rows (each new event enqueues another delivery),
and every one of them pays a full connection attempt + timeout before backing
off. Under high event volume a single dead endpoint can soak up worker time and
delay healthy deliveries. ARCHITECTURE.md §17 already described a per-endpoint
circuit breaker aspirationally (`ConsecutiveFailures` + cooldown), but it was
never implemented; it was tracked as deferred in `docs/DEFERRED.md` (issue #207).

## Decision

We add a per-endpoint circuit breaker to `WebhookDeliveryWorker`. Two fields on
`WebhookEndpoint` hold the state: `ConsecutiveFailures` (reset to zero on any
success) and `CircuitOpenUntil` (a nullable timestamp). After a delivery attempt
the worker records the outcome via `IWebhookStore.RecordCircuitStateAsync`, which
updates only those two columns with a single conditional `UPDATE` and never
touches admin-editable fields. When `ConsecutiveFailures` reaches the configured
threshold the circuit **opens**: `CircuitOpenUntil` is set to `now + cooldown`,
and while it is in the future the worker **short-circuits** that endpoint's due
deliveries — it reschedules each to `CircuitOpenUntil` without POSTing and
without spending the attempt budget. After the cooldown the next delivery is a
half-open probe: it is attempted normally; success closes the circuit, failure
re-opens it (and, because the drain reloads the endpoint per delivery, the
remaining due rows in that pass short-circuit again). The threshold and cooldown
are DB-overridable (`FeatlyWebhookSettings`, defaults on `WebhookOptions`); a
non-positive threshold disables the breaker, preserving the pre-#207 behavior.

## Alternatives considered

### Alternative 1 — in-memory circuit state

Track `ConsecutiveFailures` / open-until in a process-local dictionary instead of
persisting it. Rejected: the state would not survive a restart and, more
importantly, would diverge across instances in a multi-node deployment, so a dead
endpoint would still be hammered by every replica. Persisting it on the endpoint
keeps the breaker correct across restarts and (on a shared database) across
instances.

### Alternative 2 — separate probe state / explicit half-open flag

Model an explicit `HalfOpen` state with a single designated probe. Rejected as
over-engineered: letting the cooldown expiry naturally admit the next delivery as
the probe, and having the first failed probe immediately re-open the circuit
(which the per-delivery endpoint reload then enforces on the rest of the pass),
gives the same throttling with no extra state machine.

## Consequences

### Positive

- A consistently-failing endpoint stops clogging the queue: its deliveries are
  short-circuited cheaply instead of each paying a connection + timeout.
- State is durable and shared, so the breaker behaves correctly across restarts
  and (on a concurrent-writer store) across instances.
- Fully backward-compatible and opt-outable (threshold `<= 0`).

### Negative

- Two new columns on `WebhookEndpoints` and a migration per relational provider.
- One extra small `UPDATE` per delivery attempt when the breaker is enabled.

### Neutral

- `ConsecutiveFailures` is a read-modify-write from the worker's loaded endpoint,
  so under concurrent writers the count is approximate — acceptable for a
  heuristic breaker, and the bundled SQLite provider serializes writers anyway.

## Implementation notes

- `WebhookEndpoint.ConsecutiveFailures` / `CircuitOpenUntil`;
  `IWebhookStore.RecordCircuitStateAsync` (shared `EfWebhookStore<TContext>` +
  in-memory); worker logic in `WebhookDeliveryWorker.AttemptAsync` /
  `RecordCircuitOutcomeAsync`.
- Migrations: `AddWebhookCircuitBreaker` (SQLite + Postgres).

## References

- ARCHITECTURE.md §17 (Webhooks)
- Issue #207; `docs/DEFERRED.md`
- [ADR-0018](0018-webhooks-single-notification-channel.md)
