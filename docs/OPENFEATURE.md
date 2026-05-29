# Using Featly with OpenFeature

[OpenFeature](https://openfeature.dev) is a vendor-neutral specification for
feature-flag evaluation. The `Featly.OpenFeature.Provider` package lets an
OpenFeature consumer evaluate Featly flags without referencing a single Featly
type at the call site — so you can adopt OpenFeature's API today and keep your
options open tomorrow.

## Install

```bash
dotnet add package Featly.Sdk
dotnet add package Featly.OpenFeature.Provider
dotnet add package OpenFeature
```

## Wire it up once

Register the Featly SDK as usual, then set `FeatlyOpenFeatureProvider` as the
OpenFeature provider at startup:

```csharp
using Featly.OpenFeature;
using Featly.Sdk;
using OpenFeature;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFeatly()
    .UseServer("https://featly.internal", "featly_sdk_key");

var app = builder.Build();

// One line: hand Featly to OpenFeature.
var featly = app.Services.GetRequiredService<Featly.IFeatlyClient>();
await Api.Instance.SetProviderAsync(new FeatlyOpenFeatureProvider(featly));
```

## Evaluate flags through OpenFeature

From here on your code talks to OpenFeature, not Featly:

```csharp
var client = Api.Instance.GetClient();

bool useV2 = await client.GetBooleanValueAsync("new-checkout-flow", false);

// With targeting context:
var ctx = EvaluationContext.Builder()
    .SetTargetingKey("alice@example.com")
    .Set("user.country", "BR")
    .Set("user.plan", "pro")
    .Build();

string theme = await client.GetStringValueAsync("checkout-theme", "classic", ctx);

// Details carry the reason, variant, and any error:
var details = await client.GetBooleanDetailsAsync("new-checkout-flow", false, ctx);
// details.Reason  -> "TARGETING_MATCH" / "SPLIT" / "DISABLED" / "DEFAULT" / ...
// details.Variant -> the Featly variant key that won
```

## How the mapping works

Featly flags are multi-typed, so a single flag covers whichever OpenFeature
value kind you ask for. The provider routes each OpenFeature resolve to
`IFlagClient.EvaluateAsync<T>` and maps the result back:

| OpenFeature call | Featly flag type | Notes |
|------------------|------------------|-------|
| `GetBooleanValue` | `Boolean` | |
| `GetStringValue` | `String` | |
| `GetIntegerValue` | `Int` | 32-bit, per the OpenFeature .NET SDK |
| `GetDoubleValue` | `Double` | |
| `GetObjectValue` | `Json` | the flag's JSON value becomes an OpenFeature `Value` (structures, lists, primitives) |

**Reason / error mapping** follows the spec:

| Featly `EvaluationReason` | OpenFeature `Reason` | `ErrorType` |
|---------------------------|----------------------|-------------|
| `Static` | `STATIC` | `None` |
| `TargetingMatch` | `TARGETING_MATCH` | `None` |
| `Split` | `SPLIT` | `None` |
| `Default` | `DEFAULT` | `None` |
| `Disabled` | `DISABLED` | `None` |
| `NotFound` | `ERROR` | `FlagNotFound` |
| `Error` | `ERROR` | `General` |

On any abnormal result (flag missing or an evaluation error) the provider
returns the **default value you passed**, as the spec requires.

**Context.** The OpenFeature `EvaluationContext` translates directly: its
targeting key becomes Featly's `TargetingKey`, and every other attribute is
flattened into Featly's targeting attributes (so a rule on `user.country`
matches `Set("user.country", "BR")`).

## What this provider does *not* cover

Featly's **dynamic configuration** is a separate concept from feature flags and
is intentionally out of scope here — OpenFeature is a flag specification. Read
configs through Featly's own `IConfigClient` (`featly.Configs.GetAsync<T>(...)`).
There is no flag/config fallback: an OpenFeature key resolves against Featly
flags only.

## Runnable sample

`samples/OpenFeature.Sample` is a minimal web API that wires the provider and
serves a flag read entirely through the OpenFeature client. Point it at a
running Featly server (the `SelfHosted.Sample` works) and hit
`/checkout?country=BR&targetingKey=alice@example.com`.
