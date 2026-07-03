# ADR-0028: Scheduled releases — a field on PendingChange, drained by a staleness-aware worker

- **Status:** Accepted
- **Date:** 2026-07-03
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

`docs/DEFERRED.md`'s post-1.0 list carries "approval scheduling and time-windowed releases" — an approved `PendingChange` (ARCHITECTURE.md §12) applies automatically at a future time instead of requiring a human to click Apply the instant the policy clears, tracked as [issue #159](https://github.com/Featly-net/Featly/issues/159). The headline use case is a marketing-coordinated launch: "this is approved, ship it at exactly 9am Monday" — a moment nobody wants to be awake for, but that must not depend on someone being awake for it.

This extends an existing entity (`PendingChange`) rather than introducing a brand-new domain concept, but it does add a new execution path (something applies a change with **no human triggering the request**), so the design still needs to answer: where the schedule lives, who is allowed to set it, what executes it, and — critically — how it interacts with `ChangeStaleness` (ARCHITECTURE.md §12 step 5: "if the underlying entity changes between Approved and Apply → Stale"), since a scheduled apply is exactly the scenario where the gap between approval and apply is largest and most likely to let something else land first.

## Decision

The schedule is a **field on `PendingChange`**, not a new entity: `PendingChange.ScheduledApplyAt` (nullable `DateTimeOffset`, UTC). A `PendingChange` already carries everything an apply needs (`ProposedState`, `EntityType`/`EntityKey`/`EnvironmentId`, the approval trail); wrapping it in a separate `ScheduledRelease` entity would duplicate that state or force a join on every apply path (manual and scheduled) for no benefit — the schedule is a property of *when* an already-modeled change applies, not a different kind of change.

**Who can schedule:** the same actor who can `Apply` today (`ChangeApply` permission) — scheduling is "apply, but later," not a distinct capability. Setting `ScheduledApplyAt` is only accepted once `Status == Approved` (the same precondition manual Apply already enforces), via a new `PATCH /api/admin/changes/{id}/schedule` endpoint (`{ scheduledApplyAt }`, `null` to cancel).

**Execution is a background worker**, `ScheduledApplyWorker`, shaped exactly like the existing `WebhookDeliveryWorker` (ARCHITECTURE.md §17): a `BackgroundService` polling on an interval, claiming `PendingChange` rows where `Status == Approved && ScheduledApplyAt <= now`, and driving each one through the **existing** `ChangeApplicationService` + `ChangeStaleness` check — the exact same code path manual Apply already uses. This is the load-bearing decision: a scheduled apply is not a second, parallel apply implementation that happens to skip the staleness check for expedience. If the underlying entity changed since approval, the scheduled apply is **skipped, not forced**, `PendingChange.Status` moves to `Stale` (already the existing terminal state for this situation), and a new `change.schedule_skipped_stale` domain event fires so the author is notified through the same audit-log + webhook backbone every other consequential action already uses (ARCHITECTURE.md §17) — silently forcing a stale change through purely because a clock fired would be worse than not scheduling at all.

**Cancellation** is `PATCH .../schedule` with `scheduledApplyAt: null` — idempotent, works any time before the worker claims the row. **Time zone**: the API and storage are UTC-only (`DateTimeOffset`, consistent with every other timestamp in the domain model); the dashboard's date/time picker converts from the operator's local browser time zone to UTC before sending, same as every other datetime input already on the Change Request screen.

## Alternatives considered

### Alternative 1 — a separate `ScheduledRelease` entity wrapping one or more changes

Model scheduling as its own entity that references one or more `PendingChange` rows, enabling a "release train" (batch several approved changes to apply together at one instant). Rejected for v1 of this feature: it is a real capability but a different, larger feature (release-train orchestration, partial-failure semantics across a batch) than "apply this one approved change later," and `docs/DEFERRED.md`'s original ask is single-change scheduling. A `ScheduledRelease` batch entity can be layered on top of the `ScheduledApplyAt` field later without revisiting this decision — it would reference `PendingChange` rows exactly as they exist today.

### Alternative 2 — force-apply at the scheduled time, ignoring staleness

Skip the staleness check for scheduled applies on the reasoning that "it was approved, ship it." Rejected: this is precisely the failure mode `ChangeStaleness` exists to prevent (ARCHITECTURE.md §12 step 5), and a scheduled apply — by construction, the delay between approval and apply is often hours or days — is the scenario *most* likely to hit it, not least likely. Silently forcing a stale change through on a timer is worse than the status quo of a human noticing staleness at manual-apply time.

### Alternative 3 — client-side scheduling (a cron job or CI step calls Apply later)

Don't build scheduling into the server at all; document that operators can call `POST .../apply` from their own scheduler. Rejected: this pushes the staleness-vs-force decision, the audit trail, and the "what happened to my scheduled apply" visibility onto every operator individually, defeating the point of the feature — and Featly already owns exactly this kind of persisted-queue-plus-worker pattern for webhooks, so the marginal cost of doing it once, correctly, in the server is low.

## Consequences

### Positive

- Reuses `ChangeApplicationService` and `ChangeStaleness` verbatim — no second apply code path to keep in sync with the manual one, and every future improvement to apply semantics (e.g. a new precondition) applies to scheduled applies automatically.
- Reuses the existing audit-log + webhook event backbone for scheduled-apply outcomes — no new notification mechanism.
- Zero schema impact on `PendingChange` rows that never schedule anything (`ScheduledApplyAt` stays `null`).

### Negative

- A new background worker to operate and monitor (poll interval tuning, similar to `Featly:Webhooks`'s knobs) — one more `BackgroundService` in the server process.
- The Inbox needs a new visual state ("scheduled for 9am Monday") distinct from "awaiting approval" and "approved, awaiting apply," which is a dashboard change even though the domain model addition is small.
- A scheduled apply that gets skipped as stale needs a **clear** notification path (not just a log line) or operators will be confused why their 9am launch didn't ship — this is real UX work, not just backend plumbing.

### Neutral

- `PATCH .../schedule` is a new endpoint rather than overloading the existing `Apply` endpoint with an optional future-timestamp body parameter — kept separate so "apply now" and "schedule an apply" stay unambiguous in the API surface and in permission-gating (both still `ChangeApply`, but as distinct actions in the audit log).

## Implementation notes

Sliced into PRs in [issue #159](https://github.com/Featly-net/Featly/issues/159):

1. Domain (`PendingChange.ScheduledApplyAt`) + storage migration.
2. `ScheduledApplyWorker` (mirrors `WebhookDeliveryWorker`'s shape) driving the existing `ChangeApplicationService` + `ChangeStaleness`; new `change.schedule_skipped_stale` domain event.
3. `PATCH /api/admin/changes/{id}/schedule` (schedule / reschedule / cancel).
4. Dashboard: date/time picker on the Change Request screen; a distinct "scheduled" Inbox state.

## References

- ARCHITECTURE.md §12 (Approval workflow) — `PendingChange` lifecycle, `ChangeStaleness`
- ARCHITECTURE.md §17 (webhook delivery worker shape this ADR's worker mirrors)
- [ADR-0017](0017-approval-pending-change-entity.md) — approval workflow as a separate `PendingChange` entity
- [ADR-0018](0018-webhooks-single-notification-channel.md) — the notification backbone scheduled-apply outcomes reuse
- [GitHub issue #159](https://github.com/Featly-net/Featly/issues/159)
