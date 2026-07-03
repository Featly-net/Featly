# ADR-0027: Flag prerequisites — AND-only, write-time cycle rejection, opt-in evaluation cost

- **Status:** Accepted
- **Date:** 2026-07-03
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

`docs/DEFERRED.md`'s post-1.0 list carries "flag prerequisites (one flag depends on another)" — the common pattern of gating a feature flag behind an infrastructure flag ("new-checkout-ui" only evaluates its own rules once "checkout-api-v2" is on), tracked as [issue #158](https://github.com/Featly-net/Featly/issues/158). This is a **new top-level concept** (CLAUDE.md requires an ADR before implementation) because it changes the evaluator's contract: today `Evaluator.EvaluateFlag(Flag?, EvaluationContext?, JsonElement, ISegmentLookup?)` (ARCHITECTURE.md §5) evaluates one flag in isolation; a prerequisite means one flag's result can depend on another flag's result.

`Featly.Engine` is shared by the SDK's hot local-evaluation path and the server's preview path (ARCHITECTURE.md §5), with a documented target of p99 < 10μs and **zero allocations for boolean flag evaluation** (CLAUDE.md "Code conventions"). Whatever prerequisites cost, it must be paid only by flags that actually declare one — the ~99% of flags with no prerequisite must stay exactly as fast as today.

Three things need deciding: the prerequisite shape (single dependency vs. a list, AND vs. OR), how cycles are prevented (a graph of flags referencing each other can trivially loop), and how the evaluator gets access to *other* flags' results without breaking the "no server round-trip on the hot path" principle (ARCHITECTURE.md principle 1).

## Decision

`Flag.Prerequisites` is an **ordered list of `(FlagKey, RequiredVariantKeys)` pairs, joined with AND** — every listed prerequisite must currently resolve to one of its required variant keys, or the flag being evaluated short-circuits to its own default with a new `EvaluationReason.PrerequisiteNotMet`, before its own rules are walked at all. This mirrors the existing "conditions within a rule are ANDed" precedent (ADR-0005) rather than inventing a second boolean-combination scheme; a caller who wants OR-like behavior expresses it as two separate flags with a shared downstream dependency, exactly like ADR-0005 already asks callers to flatten OR-of-rules into separate rule entries. `RequiredVariantKeys` (plural) covers both the common boolean case (`["on"]`) and multi-variant gating (`["treatment-a", "treatern-b"]` — "on if the prerequisite chose either treatment") without a separate mechanism.

**Cycle rejection happens at write time**, not evaluation time: `POST`/`PUT /api/admin/flags` runs a DFS over the environment's full prerequisite graph (including the flag being written) and rejects with `409 Conflict` if the new edge would create a cycle. This keeps the evaluator itself free of cycle-guard bookkeeping on every call — the invariant "the prerequisite graph is acyclic" is established once, at the boundary, and the evaluator can assume it holds (same shape as `ChangeStaleness` establishing an invariant at the approval boundary rather than re-checking it inside `Evaluator`).

**The evaluator gets an `IFlagLookup` delegate**, a sibling to the existing `ISegmentLookup` (ARCHITECTURE.md §5's `InSegment` resolution): `EvaluateFlag` grows an optional `IFlagLookup? prerequisites` parameter, called only when `flag.Prerequisites` is non-empty. A flag with no prerequisites takes the exact code path it takes today — the `IFlagLookup` parameter is never touched, so the zero-allocation boolean fast path is untouched. When prerequisites exist, the evaluator resolves each one by looking up the referenced flag in the same snapshot and evaluating *it* (recursively, bounded by the write-time acyclic guarantee so no runtime depth counter is needed for correctness — a conservative depth cap is still added as a defense-in-depth backstop against a future bug in the write-time check, not as the primary safety mechanism). On the SDK side, `FeatlySnapshotCache` already indexes flags by key (mirroring how it indexes segments for `ISegmentLookup`) — implementing `IFlagLookup` from that cache is a small addition, not a new indexing pass, and resolution stays entirely local: **no server round-trip**, preserving ARCHITECTURE.md principle 1.

Prerequisites are scoped to the **same environment only** — a `FlagKey` reference resolves against the same `Guid environmentId` / `ConfigSnapshot` the flag being evaluated came from. Cross-environment dependencies are out of scope: environments are meant to be independently configurable (that is the entire reason they exist), and a cross-environment prerequisite would silently break the moment someone reconfigures one environment differently from another.

## Alternatives considered

### Alternative 1 — single prerequisite, not a list

Simplify to `Flag.PrerequisiteFlagKey` + `Flag.PrerequisiteVariantKey` (one dependency, one required variant). Rejected: real usage chains more than one gate ("new-checkout-ui" needs both "checkout-api-v2" *and* "payments-v3"), and a single-dependency model would force that into a synthetic wrapper flag — exactly the kind of workaround the feature exists to avoid. The list costs nothing extra on the fast path (empty list, same as no dependency).

### Alternative 2 — evaluation-time cycle detection (a runtime "visiting" set)

Thread a `HashSet<string>` of in-progress flag keys through `EvaluateFlag` to detect a cycle live and return an error reason instead of looping forever. Rejected as the *primary* mechanism: it adds an allocation (the set) to every prerequisite-bearing evaluation, forever, to guard against a state that write-time validation should never allow to exist. Write-time rejection is strictly cheaper at the only place that matters (the hot path) and gives the operator an immediate, actionable `409` instead of a confusing runtime reason months later. (A depth cap remains as cheap defense-in-depth — see Decision.)

### Alternative 3 — `IFlagLookup` resolves a pre-computed boolean, not a recursive evaluation

Have the *server* pre-compute each flag's currently-effective variant once per config-change and ship a flat `Dictionary<FlagKey, VariantKey>` snapshot instead of recursive evaluation. Rejected: a flag's effective variant is context-dependent (a rule can target `user.country=BR`), so there is no single "currently-effective variant" to precompute server-side — the prerequisite's own targeting rules must be evaluated against the *same* `EvaluationContext` as the dependent flag, which is exactly what recursive evaluation through the snapshot already gives for free.

## Consequences

### Positive

- Flags without prerequisites pay zero cost — same allocation profile and same code path as today, preserving the p99 < 10μs / zero-allocation boolean fast-path targets.
- Cycle rejection at write time gives immediate, clear feedback (`409` on the offending `PUT`/`POST`) instead of a confusing runtime failure mode.
- No server round-trip: prerequisite resolution is local, through the same `ConfigSnapshot` / `FeatlySnapshotCache` the SDK already holds.

### Negative

- A flag *with* prerequisites now costs at least one extra flag lookup + recursive evaluation per prerequisite per call — acceptable (still local, still sub-microsecond per hop) but real, and must be confirmed against `docs/PERFORMANCE.md`'s benchmark suite before this ships.
- The write-time DFS must scan the environment's full prerequisite graph on every flag write, not just the flag being changed — a cost proportional to how many flags declare prerequisites, paid only on mutation (not evaluation), which is the right place to pay it but is a new query pattern the `IFlagStore` write path doesn't have today.
- `EvaluateFlag`'s signature grows a fourth parameter; every existing call site (SDK, server preview, tests) needs a (defaultable) update.

### Neutral

- The dashboard's flag rule editor gains a prerequisite picker and a "gated by an unmet prerequisite" indicator — pure additive UI, no change to existing screens' behavior for flags that don't use the feature.

## Implementation notes

Sliced into PRs in [issue #158](https://github.com/Featly-net/Featly/issues/158):

1. Domain (`Flag.Prerequisites`) + storage (migration) + write-time cycle rejection.
2. Engine: `IFlagLookup`, the new `EvaluationReason.PrerequisiteNotMet`, recursive resolution with the depth-cap backstop; benchmark confirmation that the no-prerequisite path is unaffected.
3. Server (admin API surface) + SDK (`FeatlySnapshotCache` implementing `IFlagLookup`).
4. Dashboard: prerequisite picker in the rule editor + unmet-prerequisite indicator.

## References

- ARCHITECTURE.md §5 (Evaluation engine)
- ARCHITECTURE.md principle 1 (local-first evaluation, no server round-trip)
- [ADR-0004](0004-murmurhash3-bucketing.md) — deterministic bucketing (same evaluator this ADR extends)
- [ADR-0005](0005-first-match-wins-rules.md) — first-match-wins, AND within a rule (the precedent this ADR's AND-only decision follows)
- [GitHub issue #158](https://github.com/Featly-net/Featly/issues/158)
