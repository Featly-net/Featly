---
title: Flags and configuration
description: How Featly models feature flags and dynamic configuration — both resolved through one targeting engine.
---

Flags and configs are the two things Featly resolves. They share one evaluation
engine and one targeting model; the difference is intent.

## Feature flags

A **flag** is a named decision your code asks Featly to make.

- **Boolean flags** answer yes/no: `await featly.Flags.IsEnabledAsync("new-checkout")`.
- **Multivariate flags** return one of several typed **variants**. A variant has
  a key, a name, and a typed value (`Boolean`, `String`, `Int`, `Double`, or
  `Json`).

Every flag declares a **default variant** that is served when the flag is
disabled or no rule matches. A flag also has an `enabled` switch — when off, the
default variant always wins, regardless of rules.

```csharp
bool useV2 = await featly.Flags.IsEnabledAsync("new-checkout", ctx);

// Multivariate, typed:
string theme = await featly.Flags.GetStringValueAsync("checkout-theme", "classic", ctx);
```

Evaluation returns an `EvaluationResult<T>` carrying the value, the winning
variant key, and a **reason** (`Static`, `TargetingMatch`, `Split`, `Default`,
`Disabled`, `NotFound`, `Error`) — never an exception for control flow.

## Dynamic configuration

A **config** is a typed value resolved through the *same* targeting engine, for
things that should change without a deploy: timeouts, limits, copy, tuning
parameters.

```csharp
int timeout = await featly.Configs.GetAsync<int>("checkout.timeout", 30, ctx);
```

Config values can be `String`, `Int`, `Decimal`, `Bool`, or `Json`. A `Json`
config is the escape hatch for structured settings — a whole object you can
target per environment or audience.

Because configs run through the same rules engine, everything you can do with
flag targeting ([rules](/Featly/concepts/targeting/), [segments](/Featly/concepts/segments-and-experiments/),
weighted splits) applies to configs too.

## Flags vs configs — which to use

| Use a flag when… | Use a config when… |
|---|---|
| The decision is on/off or one-of-N behaviour | You need a *value* (number, string, object) |
| You'll remove it after rollout | It's a long-lived tunable |
| You ask "is this on for this user?" | You ask "what value applies to this context?" |

## The value type

Both flags and configs store their payload as a `JsonElement` internally, so a
single model covers every value kind. The SDK's typed accessors
(`GetAsync<int>`, `GetStringValueAsync`, …) convert on read; a type mismatch
surfaces as an `Error` reason and the default value, not an exception.

## Next

- [Targeting and rules](/Featly/concepts/targeting/) — how a context picks a variant.
- [Dashboard tour](/Featly/dashboard/) — create and edit flags and configs in the UI.
