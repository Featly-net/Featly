using System.Text.Json;
using Featly.Engine;

namespace Featly.Sdk.Internal;

/// <summary>
/// SDK implementation of <see cref="IFlagClient"/>. Pure local evaluation
/// against the cached snapshot maintained by <see cref="FeatlySnapshotCache"/>.
/// </summary>
/// <remarks>
/// Pulls the segment lookup from the cache so <c>InSegment</c> conditions
/// resolve locally without a server round-trip. When the caller does not
/// pass an explicit <see cref="EvaluationContext"/>, the ambient one is
/// requested from <see cref="IFeatlyContextAccessor"/> (default
/// implementation returns null, so no context).
/// <para>
/// When an active experiment covers the evaluated flag and the context carries
/// a subject (<see cref="EvaluationContext.TargetingKey"/>), the client emits an
/// exposure event and — for sticky experiments — pins the subject to the first
/// variant it saw, overriding the freshly bucketed value when a mid-flight
/// weight change would otherwise migrate it.
/// </para>
/// </remarks>
internal sealed class FlagClient(
    FeatlySnapshotCache cache,
    IFeatlyContextAccessor contextAccessor,
    ExperimentExposureProcessor? exposures = null) : IFlagClient
{
    public ValueTask<bool> IsEnabledAsync(string key, EvaluationContext? context = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ct.ThrowIfCancellationRequested();

        var resolved = ResolveContext(context);
        var flag = cache.TryGetFlag(key);
        var fallback = JsonSerializer.SerializeToElement(false);
        var raw = Evaluator.EvaluateFlag(flag, resolved, fallback, cache.Segments, cache.Flags);
        raw = ApplyExperiment(key, resolved, raw);
        return ValueTask.FromResult(raw.As(false));
    }

    public ValueTask<T> GetVariantAsync<T>(string key, T defaultValue, EvaluationContext? context = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ct.ThrowIfCancellationRequested();

        var resolved = ResolveContext(context);
        var flag = cache.TryGetFlag(key);
        var fallback = JsonSerializer.SerializeToElement(defaultValue);
        var raw = Evaluator.EvaluateFlag(flag, resolved, fallback, cache.Segments, cache.Flags);
        raw = ApplyExperiment(key, resolved, raw);
        return ValueTask.FromResult(raw.As(defaultValue));
    }

    public ValueTask<EvaluationResult<T>> EvaluateAsync<T>(string key, T defaultValue, EvaluationContext? context = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ct.ThrowIfCancellationRequested();

        var resolved = ResolveContext(context);
        var flag = cache.TryGetFlag(key);
        var fallback = JsonSerializer.SerializeToElement(defaultValue);
        var raw = Evaluator.EvaluateFlag(flag, resolved, fallback, cache.Segments, cache.Flags);
        raw = ApplyExperiment(key, resolved, raw);

        var typed = new EvaluationResult<T>(
            Key: raw.Key,
            Value: raw.As(defaultValue),
            VariantKey: raw.VariantKey,
            Reason: raw.Reason,
            RuleMatched: raw.RuleMatched,
            Error: raw.Error);

        return ValueTask.FromResult(typed);
    }

    private EvaluationContext? ResolveContext(EvaluationContext? explicitContext)
        => explicitContext ?? contextAccessor.Current;

    /// <summary>
    /// Emits an exposure (and applies sticky pinning) when an active experiment
    /// covers the flag. Returns the original result untouched on the common path
    /// where no experiment applies — an O(1) dictionary miss, no allocations.
    /// </summary>
    private EvaluationResult<JsonElement> ApplyExperiment(
        string flagKey,
        EvaluationContext? context,
        EvaluationResult<JsonElement> raw)
    {
        if (exposures is null)
        {
            return raw;
        }

        var experiment = cache.TryGetActiveExperimentForFlag(flagKey);
        if (experiment is null)
        {
            return raw;
        }

        var subject = context?.TargetingKey;
        if (string.IsNullOrEmpty(subject))
        {
            // No subject to attribute the exposure to — skip silently.
            return raw;
        }

        var pinnedVariant = exposures.Process(experiment, subject, raw.VariantKey);
        if (string.Equals(pinnedVariant, raw.VariantKey, StringComparison.Ordinal))
        {
            return raw;
        }

        // Sticky pinned a different variant than this evaluation produced (a
        // weight change moved the bucket). Honour the pinned variant's value.
        var flag = cache.TryGetFlag(flagKey);
        var variant = flag?.Variants.FirstOrDefault(v => string.Equals(v.Key, pinnedVariant, StringComparison.Ordinal));
        return variant is null
            ? raw
            : raw with { VariantKey = pinnedVariant, Value = variant.Value };
    }
}
