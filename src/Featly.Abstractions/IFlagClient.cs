namespace Featly;

/// <summary>
/// Client-side surface for evaluating feature flags. Implemented by
/// <c>Featly.Sdk</c> on top of a locally-cached <see cref="ConfigSnapshot"/>.
/// </summary>
public interface IFlagClient
{
    /// <summary>
    /// Returns whether the flag is enabled for the supplied context.
    /// </summary>
    /// <param name="key">The flag key.</param>
    /// <param name="context">Optional explicit evaluation context. When omitted,
    /// the ambient context from <c>IFeatlyContextAccessor</c> is used.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The variant value cast to <c>bool</c>; <c>false</c> if absent or non-boolean.</returns>
    ValueTask<bool> IsEnabledAsync(string key, EvaluationContext? context = null, CancellationToken ct = default);

    /// <summary>
    /// Returns the variant value as <typeparamref name="T"/>, falling back to
    /// <paramref name="defaultValue"/> when the flag is missing, disabled, or
    /// cannot be deserialized to <typeparamref name="T"/>.
    /// </summary>
    ValueTask<T> GetVariantAsync<T>(string key, T defaultValue, EvaluationContext? context = null, CancellationToken ct = default);

    /// <summary>
    /// Returns the full evaluation result, including variant key and reason.
    /// </summary>
    ValueTask<EvaluationResult<T>> EvaluateAsync<T>(string key, T defaultValue, EvaluationContext? context = null, CancellationToken ct = default);
}
