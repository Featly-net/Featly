# ADR-0005: First-match-wins on ordered rules; AND within a rule

- **Status:** Accepted
- **Date:** 2026-05-25
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

A flag has an ordered list of targeting rules, each with one or more conditions. The evaluation semantics must be obvious to an operator reading the rule list top to bottom, and cheap to evaluate. Ambiguous semantics (e.g. "most specific wins", scoring) make outcomes hard to predict.

## Decision

The engine walks rules in `Order` and the **first rule whose conditions all match wins** (first-match-wins). **Within a rule, conditions are AND-ed**; OR is expressed by adding another rule. When no rule matches, the engine returns the flag's `DefaultVariantKey`. This mirrors how an operator reads the list and keeps evaluation a single linear pass.

## Alternatives considered

### Alternative 1 — most-specific-wins / scoring

Rejected: requires a specificity metric that is non-obvious and order-independent in confusing ways; operators can't predict outcomes by reading top to bottom.

### Alternative 2 — all-matching-rules combine

Rejected: combining multiple matched outcomes needs conflict-resolution rules, reintroducing ambiguity.

## Consequences

### Positive

- Predictable, readable: the rule list is the evaluation order.
- O(rules × conditions) worst case, single pass, early-exit on first match.

### Negative

- OR across conditions requires duplicating into multiple rules.

### Neutral

- Rule `Order` is significant and must be maintained by the admin UI/API.

## References

- ARCHITECTURE.md §6 — Evaluation engine
- ADR-0014 — Cumulative permissions (the analogous "more is more, no deny" choice in RBAC)
