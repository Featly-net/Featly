using System.Text.Json;
using Featly.Engine;

namespace Featly.Sdk.Internal;

/// <summary>
/// SDK implementation of <see cref="IFlagClient"/>. Pure local evaluation
/// against the cached snapshot maintained by <see cref="FeatlySnapshotCache"/>.
/// </summary>
internal sealed class FlagClient(FeatlySnapshotCache cache) : IFlagClient
{
    public ValueTask<bool> IsEnabledAsync(string key, EvaluationContext? context = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ct.ThrowIfCancellationRequested();

        var flag = cache.TryGetFlag(key);
        var fallback = JsonSerializer.SerializeToElement(false);
        var result = Evaluator.EvaluateFlag(flag, context, fallback);
        return ValueTask.FromResult(result.As(false));
    }

    public ValueTask<T> GetVariantAsync<T>(string key, T defaultValue, EvaluationContext? context = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ct.ThrowIfCancellationRequested();

        var flag = cache.TryGetFlag(key);
        var fallback = JsonSerializer.SerializeToElement(defaultValue);
        var result = Evaluator.EvaluateFlag(flag, context, fallback);
        return ValueTask.FromResult(result.As(defaultValue));
    }

    public ValueTask<EvaluationResult<T>> EvaluateAsync<T>(string key, T defaultValue, EvaluationContext? context = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ct.ThrowIfCancellationRequested();

        var flag = cache.TryGetFlag(key);
        var fallback = JsonSerializer.SerializeToElement(defaultValue);
        var raw = Evaluator.EvaluateFlag(flag, context, fallback);

        var typed = new EvaluationResult<T>(
            Key: raw.Key,
            Value: raw.As(defaultValue),
            VariantKey: raw.VariantKey,
            Reason: raw.Reason,
            RuleMatched: raw.RuleMatched,
            Error: raw.Error);

        return ValueTask.FromResult(typed);
    }
}
