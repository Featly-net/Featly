namespace Featly;

/// <summary>
/// Client surface for dynamic configuration values. Mirrors
/// <see cref="IFlagClient"/> but returns typed values directly — there are
/// no variants for configs.
/// </summary>
/// <remarks>
/// All methods evaluate locally against the cached snapshot — no network
/// call on the hot path.
/// </remarks>
public interface IConfigClient
{
    /// <summary>
    /// Returns the configured value for <paramref name="key"/>, falling back
    /// to <paramref name="defaultValue"/> when the config is missing,
    /// archived, or the value cannot be coerced to <typeparamref name="T"/>.
    /// </summary>
    ValueTask<T> GetAsync<T>(
        string key,
        T defaultValue,
        EvaluationContext? context = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the full <see cref="EvaluationResult{T}"/> including reason
    /// and matched rule for diagnostics.
    /// </summary>
    ValueTask<EvaluationResult<T>> EvaluateAsync<T>(
        string key,
        T defaultValue,
        EvaluationContext? context = null,
        CancellationToken ct = default);
}
