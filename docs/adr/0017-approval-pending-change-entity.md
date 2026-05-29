# ADR-0017: Approval workflow as a separate PendingChange entity, not entity versioning

- **Status:** Accepted
- **Date:** 2026-05-28
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

For protected environments, a mutation must be reviewed before it takes effect. Two ways to model "a proposed-but-not-yet-applied change": version every entity (each flag carries draft + published revisions), or capture the proposed change as a separate record awaiting approval. Versioning every entity touches every table and every write path and complicates the read model for the common, unprotected case.

## Decision

An approval is a **separate `PendingChange` entity** that captures the proposed new state (serialized), the target entity/environment, the proposer, approvals/comments, and a status. When an `ApprovalPolicy` requires review, the normal POST/PUT mutation **transparently creates a `PendingChange` and returns `202`** instead of applying; on approval, a `ChangeApplicationService` deserializes the proposed state and writes it through the normal store path. Unprotected environments are unaffected — the entities themselves are never versioned.

## Alternatives considered

### Alternative 1 — version every entity (draft/published)

Rejected: invasive across all tables and writes; complicates evaluation/read for the majority case that needs no approval.

### Alternative 2 — external workflow tool

Rejected: breaks the embedded, self-contained model; approvals belong in Featly's own store and dashboard.

## Consequences

### Positive

- Gating is transparent: the same endpoint either applies or returns a `PendingChange`.
- Entities stay single-state; only protected environments pay the workflow cost.

### Negative

- The proposed state is serialized and re-applied later, so it must round-trip faithfully and detect staleness if the underlying entity changed meanwhile.

### Neutral

- Emergency bypass and dry-run are modeled as flags on the change-create path.

## References

- ARCHITECTURE.md §12 — Approval workflow
- `PendingChange`, `ChangeApplicationService`, `ChangeGate`
