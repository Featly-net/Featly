# ADR-0018: Webhooks as the single external notification channel; HMAC-signed

- **Status:** Accepted
- **Date:** 2026-05-28
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

Operators want Featly to notify external systems (Slack, Teams, CI, custom services) when consequential things happen — flag changes, approvals, RBAC edits. Building a native integration per destination is unbounded work and couples Featly to third-party APIs. Notifications must also be authenticated so a receiver can trust them.

## Decision

**Outbound webhooks are the single external notification channel.** A shared domain-event publisher fans every consequential action out to a dispatcher that enqueues a persisted, restart-surviving `WebhookDelivery` for each matching endpoint; a background worker drains the queue with exponential backoff to a dead-letter, **signing each body with the endpoint's secret via HMAC-SHA256** (`X-Featly-Signature: sha256=…`). Native integrations (Slack/Teams) and other channels (SMTP) are deferred — they can all sit behind the generic webhook.

## Alternatives considered

### Alternative 1 — native per-destination integrations

Rejected for v1: unbounded surface; each destination's API and auth is bespoke. A generic signed webhook reaches all of them via a small adapter on the receiver side.

### Alternative 2 — fire-and-forget, no persistence

Rejected: a transient receiver outage would silently drop notifications. The persisted queue with retry/dead-letter survives restarts and outages.

## Consequences

### Positive

- One channel reaches any destination; receivers verify authenticity via HMAC.
- Delivery is durable: retries, backoff, dead-letter, survives restart.

### Negative

- Receivers must implement signature verification and idempotency.
- No built-in pretty Slack formatting — that lives in the receiver/adapter.

### Neutral

- Worker tuning (interval, attempts, backoff) is configurable under `Featly:Webhooks`.

## References

- ARCHITECTURE.md §17 — Webhooks + audit
- `WebhookDispatcher`, `WebhookDeliveryWorker`, `WebhookSignature`
