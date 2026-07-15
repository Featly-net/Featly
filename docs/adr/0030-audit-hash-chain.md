# ADR-0030: Tamper-evident audit hash chain

- **Status:** Accepted
- **Date:** 2026-07-15
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

The audit log records every consequential action (ARCHITECTURE.md §17). Entries
were appended but not protected: anyone with database access could edit or delete
a row and leave no trace. For compliance-sensitive deployments the log needs to
be **tamper-evident** — post-hoc modification, deletion, or reordering must be
detectable (issue #208). Retention pruning (`AuditRetentionWorker`) already
deletes old entries, so any integrity scheme must tolerate the oldest entries
disappearing.

## Decision

We link audit entries into a hash chain. Each `AuditEntry` gains a monotonic
`Sequence`, a `PreviousHash`, and a SHA-256 `Hash` (lowercase hex) computed over
its immutable content plus `PreviousHash` (`AuditHash.Compute`, length-prefixed
fields so values cannot be rearranged into the same byte stream). On append the
store reads the current tail, sets `Sequence = tail.Sequence + 1` and
`PreviousHash = tail.Hash`, computes `Hash`, and inserts — all under a
process-wide append gate so the read-tail → chain → insert step is atomic and the
chain stays linear. `IAuditStore.VerifyChainAsync` walks the hashed entries in
`Sequence` order and returns the first break: a content-hash mismatch (an entry
was modified) or a broken `PreviousHash` link (an entry was deleted or
reordered). The oldest surviving entry's link is exempt, so retention pruning of
the chain's head leaves the surviving suffix verifiable. `GET
/api/admin/audit/verify` exposes the check to admins (`AuditRead`).

## Alternatives considered

### Alternative 1 — per-entry signature instead of a chain

Sign each entry independently with a server key. Rejected: an independent
signature detects modification of a row but not **deletion** of whole rows, which
a chain catches via the broken link. A chain also needs no key management.

### Alternative 2 — external notarization / append-only WAL

Stream entries to an external immutable store (object-lock bucket, transparency
log). Rejected as out of scope for the embedded product: it adds an external
dependency and operational surface. The hash chain is a self-contained first step;
external anchoring can layer on later for stronger guarantees.

## Consequences

### Positive

- Casual tampering (editing or deleting a row directly in the database) is
  detectable by recomputation, with the offending `Sequence` reported.
- Self-contained: no keys, no external services; works on the bundled SQLite.
- Tolerates retention pruning of the chain head.

### Negative

- Three new columns on `AuditEntries` and a migration per relational provider.
- Appends are serialized by a process-wide gate, so audit-write throughput is
  capped at one append at a time (acceptable — auditing is off the evaluation hot
  path).
- **Not** proof against an attacker who can rewrite the entire chain (recompute
  every subsequent hash). Detecting that needs an external anchor, deliberately
  left as future work.

### Neutral

- The append gate is in-process, so correctness holds for the single-writer
  embedded deployment. Concurrent writers across instances (a future Postgres
  multi-node setup) would need DB-level serialization; tracked for that provider.

## Implementation notes

- `AuditEntry.Sequence` / `PreviousHash` / `Hash`; `AuditHash`,
  `AuditChainVerifier`, `AuditChainVerification` in `Featly.Storage.Abstractions`;
  chained append + verify in the shared `EfAuditStore<TContext>` and
  `InMemoryAuditStore`.
- Endpoint: `GET /api/admin/audit/verify`. Migration: `AddAuditHashChain`
  (SQLite + Postgres).

## References

- ARCHITECTURE.md §17 (Audit log)
- Issue #208
