---
title: Segments and experiments
description: Reusable audience definitions (segments) and A/B testing (experiments) built on Featly's targeting engine.
---

## Segments

A **segment** is a named, reusable audience: a set of conditions, defined once,
referenced by many flags and configs.

Instead of repeating `user.plan == "pro" AND user.country IN [...]` across a
dozen flags, define a `pro-users` segment and target it with a single
`InSegment` condition. Edit the segment once and every flag that references it
follows.

```text
Segment "beta-testers":
  user.email EndsWith "@ourcompany.com"
  OR user.betaOptIn Equals true

Flag rule:
  IF context InSegment "beta-testers"  ->  variant "on"
```

Segments are first-class entities with their own screen in the
[dashboard](/Featly/dashboard/) and their own API. A rule references a segment by
key, and segment membership is evaluated as part of the same first-match-wins
[rules](/Featly/concepts/targeting/) walk.

## Experiments

An **experiment** is an A/B test built on the flag engine. It combines:

- **Deterministic bucketing** — the same `targetingKey` is assigned to the same
  variant for the life of the experiment (the
  [MurmurHash3 split](/Featly/concepts/targeting/#weighted-splits-and-deterministic-bucketing)),
  so a user's experience is consistent.
- **Exposure events** — when a user is bucketed into a variant, Featly can emit
  an **exposure** event, and your code can emit **custom** events
  (`featly.Events.TrackAsync("checkout.completed")`). Together these let you
  measure the effect of a variant on a metric.

```csharp
if (await featly.Flags.IsEnabledAsync("new-checkout", ctx))
{
    await featly.Events.TrackAsync("checkout.started");
}
```

Bucketing being deterministic and shared between the SDK and the server means
exposure attribution is consistent no matter where evaluation happens.

:::note
Experiments are an opt-in feature area. If you only need flags and configs, you
can disable experiments to shrink the footprint — see
[Modularity](/Featly/operate/modularity/).
:::

## Next

- [Projects and environments](/Featly/concepts/projects-and-environments/) — how flags are
  isolated and promoted.
- [Governance](/Featly/concepts/governance/) — controlling who can change what.
