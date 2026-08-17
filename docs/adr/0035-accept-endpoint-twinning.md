# ADR-0035: Accept admin-endpoint twinning over a generic `EntityWriteFlow`

- **Status:** Accepted
- **Date:** 2026-08-17
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

The admin write endpoints for flags, configs, and segments
(`AdminFlagsEndpoints`, `AdminConfigsEndpoints`, `AdminSegmentsEndpoints`)
repeat the same linear shape: resolve the environment, guard `ReadOnly`, run
the approval gate, upsert, announce the snapshot change, publish the domain
event. Issue #224 proposed consolidating them, floating an
`EntityWriteFlow<TRequest, TEntity>` abstraction.

Three attempts established the actual shape of the problem:

- The pieces genuinely worth sharing were already extracted along the way:
  `EnvironmentResolver.ResolveWritableAsync` (the env-resolve + `ReadOnly`
  preamble, #248) and `SnapshotChange.AnnounceAsync` (the notify + version
  bump, #252).
- What remains duplicated is *structural twinning*, concentrated in
  `AdminConfigsEndpoints` / `AdminSegmentsEndpoints`: same handler shape,
  differing only in sub-store and event names, plus byte-identical
  `ResolveActor` copies (six across the endpoint files) and three identical
  `SetArchivedAsync` bodies. (`AdminFlagsEndpoints` carries extra
  prerequisite logic that breaks the symmetry, so it largely drops out of
  duplication reports — the tell that the twinning itself is the finding.)
- PR #249 built the obvious mid-size extraction (an `AdminWrite` helper for
  actor / gate / archive). It was green on everything **except SonarCloud's
  new-code duplication gate, which extraction cannot clear**: every
  extraction leaves per-entity wrappers whose signatures are themselves
  identical across the twins, so the gate re-flags them; the attempt moved
  the duplicated-line count from 8 to 12. Because the gate scores *new*
  lines, merely touching the twins is what trips it.

The only change that removes the twinning is the full
`EntityWriteFlow<TRequest, TEntity>` — threading get / upsert / map /
validate / key-of through roughly nine delegates.

## Decision

We accept the twinning and will not build `EntityWriteFlow`. The endpoint
files stay as parallel, linear handlers. Issue #224 is closed by this ADR.

This is the same trade ADR-0026 already accepted for the storage layer
(per-provider duplication over a shared, branching abstraction), applied one
layer up: ARCHITECTURE.md principle 6, "predictable, not magical," outweighs
the duplication metric. A handler that reads top-to-bottom — resolve, guard,
gate, upsert, announce, publish — is worth more than removing ~30 duplicated
lines behind delegate indirection, and this issue itself flagged the
regression risk of wrapping the approval gate in an abstraction.

Explicitly rejected alongside:

- **Merging a cleanup with the duplication gate red** ("the metric penalises
  a legacy touch"): the project's own merge policy is all gates green, no
  exceptions carved out case-by-case.
- **Adding the endpoint files to `sonar.cpd.exclusions`**: the storage
  providers earned that exclusion via an ADR covering code that is
  *by-design* near-identical across packages. Application endpoint code is
  exactly where duplication scrutiny should stay live.

## Consequences

### Positive

- Each endpoint file remains independently readable and independently
  editable — flag-specific logic (prerequisites) already diverges, and the
  twins are free to diverge the same way without un-threading a shared flow.
- The approval gate stays inline and visible at every call site that applies
  it, instead of behind a delegate parameter.

### Negative

- The duplication is real and stays: six `ResolveActor` copies, the twinned
  handler shapes, three `SetArchivedAsync` bodies. A behavioral change to the
  approval-gate block must be applied in each place (the gate's *logic*
  lives in shared services; what is duplicated is the call-site
  choreography).
- **Anyone editing the twinned regions may trip SonarCloud's new-code
  duplication gate through no new fault of their own** — the gate rescores
  touched lines, and PR #249 demonstrated extraction makes the score worse,
  not better. When that happens, cite this ADR in the PR rather than
  attempting another extraction; a genuinely new approach should supersede
  this ADR instead.

### Neutral

- If the endpoint count ever grows past the current three-way twinning
  (e.g. a fourth entity with the same write shape), the calculus shifts and
  this ADR should be revisited — the generic flow's readability cost
  amortises over more twins.

## References

- Issue #224 — the consolidation proposal and the three documented attempts
- PR #249 (closed) — the empirical result: extraction cannot clear the gate
- PR #248 (`EnvironmentResolver.ResolveWritableAsync`), PR #252
  (`SnapshotChange.AnnounceAsync`) — the pieces that *were* worth sharing
- [ADR-0026](0026-postgres-storage-provider.md) — the accepted-duplication
  precedent this decision mirrors
- ARCHITECTURE.md §1, principle 6 — "predictable, not magical"
