---
title: Performance
description: Featly's evaluation performance baseline — targets, measured numbers, and how to reproduce them.
---

:::note[Targets]
From the architecture: **p50 < 1 μs, p99 < 10 μs** for `IsEnabledAsync` with a
warm cache.
:::

The numbers below come from `tests/Featly.Engine.Benchmarks`, run on the
maintainer's workstation. They are not authoritative — CI doesn't gate on them —
but they pin the order of magnitude and surface regressions when re-run manually
before each release.

## How to reproduce

```bash
dotnet run -c Release --project tests/Featly.Engine.Benchmarks -- --filter '*'
```

[BenchmarkDotNet](https://benchmarkdotnet.org/) handles warmup, statistical
noise, and outlier removal. The `[MemoryDiagnoser]` attribute also tracks managed
allocations per operation.

## Baseline — first complete engine cut

- **Runtime**: .NET 10.0.8, X64 RyuJIT (x86-64-v4)
- **Host**: AMD Ryzen 9 9950X (16 logical / 8 physical cores), virtualised (Hyper-V)

| Scenario | Mean | Allocations | Notes |
|---|---:|---:|---|
| Boolean flag, no rules (fast path) | **37 ns** | 72 B | The `EvaluationResult<JsonElement>` record is the entire allocation. |
| 1 rule, 1 `Equals` condition (matches) | **134 ns** | 104 B | Single attribute lookup + scalar comparison + `RuleMatched` string copy. |
| 5 rules × 3 conditions, last matches | **1 176 ns** | 280 B | 4 rule iterations that fail + 1 full match. Worst realistic targeting depth. |
| 50/50 split bucketing | **390 ns** | 232 B | UTF-8 encode of `key:flag:salt` + MurmurHash3 → bucket → cumulative-weight walk. |
| `InSegment` lookup (nested condition) | **286 ns** | 168 B | Dictionary lookup + recurses into the segment's conditions. |

Every scenario is at least an order of magnitude below the **10 μs p99 target**.
The boolean fast path is roughly **270×** under target.

## Re-run before the first public release

The engine's hot path was untouched by the configs, RBAC, approvals,
experiments, webhooks, OpenFeature, and CLI work — all of which sit outside it —
and the numbers held within run-to-run noise:

| Scenario | Mean | Allocations |
|---|---:|---:|
| Boolean flag, no rules (fast path) | **37 ns** | 72 B |
| 1 rule, 1 `Equals` condition (matches) | **151 ns** | 104 B |
| 5 rules × 3 conditions, last matches | **734 ns** | 280 B |
| 50/50 split bucketing | **315 ns** | 232 B |
| `InSegment` lookup (nested condition) | **261 ns** | 168 B |

No scenario regressed; the deepest realistic targeting path stays well under
1 μs — ~13× under the 10 μs p99 target.

## Reading the numbers

- **Mean** is the arithmetic mean across iterations after outlier removal.
- **Allocations** is `MemoryDiagnoser` accounting: managed bytes per single
  operation, inclusive of returned objects.
- Allocations are dominated by the `EvaluationResult<JsonElement>` record (~72 B)
  plus, in rule-matching paths, the `RuleMatched` and `VariantKey` strings. A
  future `ValueResult` struct overload could make boolean evaluation
  zero-allocation, but the public SDK contract already returns `EvaluationResult<T>`,
  so that would be an internal optimisation.

## Tracking regressions

Re-run the benchmark suite before each tagged release and compare against the
table above; if any scenario regresses by more than ~30% mean or allocations,
investigate before publishing the tag.

## Caveats

- BenchmarkDotNet warnings about *minimum observed iteration time* are expected
  at this granularity — invocation count is set high enough to amortise overhead.
- The virtualised host adds a little jitter; re-run on bare metal for sharper
  numbers.
- Very large rule sets (hundreds of rules) are not yet benchmarked; the suite
  targets realistic SDK usage (a handful of rules per flag).
