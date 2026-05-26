# Performance baseline

> Goals from [ARCHITECTURE.md](../ARCHITECTURE.md) §19:
> **p50 < 1 μs, p99 < 10 μs** for `IsEnabledAsync` with a warm cache.

The numbers below come from `tests/Featly.Engine.Benchmarks`, run on the maintainer's workstation. They are not authoritative — CI doesn't gate on them — but they pin the order of magnitude and surface regressions when we rerun manually before each release.

## How to reproduce

```bash
dotnet run -c Release --project tests/Featly.Engine.Benchmarks -- --filter '*'
```

[BenchmarkDotNet](https://benchmarkdotnet.org/) handles warmup, statistical noise, and outlier removal. The `[MemoryDiagnoser]` attribute also tracks managed allocations per operation.

## Baseline (M3B — engine first complete cut)

- **Date**: 2026-05-26
- **Runtime**: .NET 10.0.8, X64 RyuJIT (x86-64-v4)
- **Host**: AMD Ryzen 9 9950X (1 CPU, 16 logical / 8 physical cores), virtualised (Hyper-V)

| Scenario | Mean | Allocations | Notes |
|---|---:|---:|---|
| Boolean flag, no rules (fast path) | **37 ns** | 72 B | The `EvaluationResult<JsonElement>` record is the entire allocation. |
| 1 rule, 1 `Equals` condition (matches) | **134 ns** | 104 B | Single attribute lookup + scalar comparison + `RuleMatched` string copy. |
| 5 rules × 3 conditions, last matches | **1 176 ns** | 280 B | 4 rule iterations that fail + 1 full match. Worst realistic targeting depth in current benchmarks. |
| 50/50 split bucketing | **390 ns** | 232 B | UTF-8 encode of `key:flag:salt` + MurmurHash3 → bucket → cumulative-weight walk. |
| `InSegment` lookup (nested condition) | **286 ns** | 168 B | Dictionary lookup + recurses into the segment's conditions. |

Every scenario is at least an order of magnitude below the **10 μs p99 target**. Boolean flag fast path is roughly **270×** under target.

## Reading the numbers

- **Mean** is the arithmetic mean across iterations after outlier removal.
- **Allocations** is `MemoryDiagnoser` accounting: managed bytes per single operation, inclusive of returned objects.
- The current allocations are dominated by the `EvaluationResult<JsonElement>` record (~72 B) plus, in rule-matching paths, the `RuleMatched` and `VariantKey` strings. Future iterations may introduce a `ValueResult` struct overload for zero-allocation boolean flag evaluation, but **the SDK API contract already returns `EvaluationResult<T>`** as the public type — going to zero-alloc would be an internal optimisation that bypasses the record.

## Tracking regressions

Re-run the benchmark suite before each tagged release. Compare the table above against the new run; if any scenario regresses by more than ~30% mean or allocations, investigate before publishing the tag.

A future CI job may auto-run the benchmarks on `main` and post diff comments on PRs that touch `Featly.Engine.*` (out of scope for M3B).

## Notes / caveats

- The BenchmarkDotNet warnings about *minimum observed iteration time* are expected at this granularity — invocation count is set high enough to amortise the overhead. Raising `invocationCount` further would tighten error bars at the cost of run time.
- The virtualised host adds a small amount of jitter (visible in the standard deviation column). Re-run on bare metal for sharper numbers.
- Targeting workloads with very large rule sets (hundreds of rules) are not yet covered; the existing benchmarks aim at realistic SDK usage (a handful of rules per flag).
