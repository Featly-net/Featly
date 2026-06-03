---
title: Targeting and rules
description: How Featly decides which variant a given context receives — rules, conditions, operators, and deterministic weighted splits.
---

Targeting is how a flag or config turns an **evaluation context** into a value.
It is the heart of the engine, and it runs identically in the SDK and on the
server.

## The evaluation context

You pass a context to every evaluation. It carries a **targeting key** (the
stable identity used for bucketing — a user id, account id, device id) and an
open set of **attributes** used by rules.

```csharp
var ctx = new Featly.EvaluationContext { TargetingKey = "alice@example.com" };
ctx.Attributes["user.country"] = "BR";
ctx.Attributes["user.plan"] = "pro";
```

## Rules

A flag or config has an **ordered list of rules**. Each rule has:

- a set of **conditions**, all of which must hold (logical AND), and
- an **outcome**: a single variant, or a **weighted split** across variants.

Evaluation walks the rules in order and the **first matching rule wins**
([ADR-0005](https://github.com/Featly-net/Featly/blob/main/docs/adr/0005-first-match-wins-rules.md)).
If no rule matches, the **default variant** is served with reason `Default`. No
ambiguity, no priority math — order is the priority.

## Conditions and operators

A condition is `(attribute, operator, value)`. Operators include equality,
comparison, set membership, string matching, and segment membership:

| Operator | Meaning |
|---|---|
| `Equals` / `NotEquals` | exact match |
| `GreaterThan` / `LessThan` (and `OrEqual` variants) | numeric / ordinal comparison |
| `In` / `NotIn` | membership in a list |
| `Contains` / `StartsWith` / `EndsWith` | string matching |
| `InSegment` | the context matches a named [segment](/Featly/concepts/segments-and-experiments/) |

A missing attribute simply fails the condition (it does not throw).

## Weighted splits and deterministic bucketing

A rule's outcome can be a **weighted split** — e.g. 50% `on`, 50% `off`, or a
canary 95/5. Bucketing is **deterministic**: Featly hashes
`targetingKey : flagKey : salt` with **MurmurHash3**
([ADR-0004](https://github.com/Featly-net/Featly/blob/main/docs/adr/0004-murmurhash3-bucketing.md))
into a stable bucket, then walks the cumulative weights. The same targeting key
always lands in the same bucket for a given flag, so a user's experience is
stable across requests and processes — and consistent between the SDK and the
server, because they share the engine.

```text
hash("alice@example.com:new-checkout:<salt>") % 10000  ->  bucket
   bucket 0..4999   -> variant "on"   (50%)
   bucket 5000..9999 -> variant "off"  (50%)
```

Optionally, assignments can be made **sticky** so a user keeps a variant even if
weights later change ([ADR-0010](https://github.com/Featly-net/Featly/blob/main/docs/adr/0010-sticky-assignments-opt-in.md)).

## Why local evaluation is safe

All of this runs **in-process against a cached snapshot** — there is no network
call to decide a flag. The SDK keeps the snapshot fresh in the background and
serves last-known-good if the server is unreachable, so evaluation stays fast
and available even during a server blip.

## Next

- [Segments and experiments](/Featly/concepts/segments-and-experiments/) — reusable
  audiences and A/B tests.
- [Performance](/Featly/reference/performance/) — the measured cost of evaluation.
