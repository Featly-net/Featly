using System.Text.Json;
using Featly.Engine;

namespace Featly.Sdk.Internal;

/// <summary>
/// SDK implementation of <see cref="IConfigClient"/>. Pure local evaluation
/// against the cached snapshot maintained by <see cref="FeatlySnapshotCache"/>.
/// Mirrors <see cref="FlagClient"/> but resolves config values directly.
/// </summary>
internal sealed class ConfigClient(
    FeatlySnapshotCache cache,
    IFeatlyContextAccessor contextAccessor) : IConfigClient
{
    public ValueTask<T> GetAsync<T>(string key, T defaultValue, EvaluationContext? context = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ct.ThrowIfCancellationRequested();

        var config = cache.TryGetConfig(key);
        var fallback = JsonSerializer.SerializeToElement(defaultValue);
        var result = Evaluator.EvaluateConfig(config, ResolveContext(context), fallback, cache.Segments);
        return ValueTask.FromResult(result.As(defaultValue));
    }

    public ValueTask<EvaluationResult<T>> EvaluateAsync<T>(string key, T defaultValue, EvaluationContext? context = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ct.ThrowIfCancellationRequested();

        var config = cache.TryGetConfig(key);
        var fallback = JsonSerializer.SerializeToElement(defaultValue);
        var raw = Evaluator.EvaluateConfig(config, ResolveContext(context), fallback, cache.Segments);

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
}
