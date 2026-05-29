using System.Text.Json;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;

namespace Featly.OpenFeature;

/// <summary>
/// OpenFeature <see cref="FeatureProvider"/> backed by Featly (ARCHITECTURE.md
/// §18). It delegates every resolution to <see cref="IFlagClient"/>, so an
/// OpenFeature consumer evaluates Featly flags locally against the cached
/// snapshot with no change to its call sites:
/// <code>
/// await Api.Instance.SetProviderAsync(new FeatlyOpenFeatureProvider(featly));
/// var client = Api.Instance.GetClient();
/// bool on = await client.GetBooleanValueAsync("new-checkout", false);
/// </code>
/// Featly flags are multi-typed, so they cover all five OpenFeature value
/// kinds (boolean / string / integer / double / structure). Dynamic configs are
/// a separate concern reached through <c>IConfigClient</c>, not this provider.
/// </summary>
public sealed class FeatlyOpenFeatureProvider : FeatureProvider
{
    private const string ProviderName = "Featly";
    private static readonly Metadata s_metadata = new(ProviderName);

    // A JSON null used as the throwaway default when resolving a structure — the
    // result value is only read on success, so its content never surfaces.
    private static readonly JsonElement s_nullJson = JsonSerializer.SerializeToElement<object?>(null);

    private readonly IFlagClient _flags;

    /// <summary>Creates a provider over a full Featly client (uses its <see cref="IFeatlyClient.Flags"/>).</summary>
    public FeatlyOpenFeatureProvider(IFeatlyClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _flags = client.Flags;
    }

    /// <summary>Creates a provider over a flag client directly.</summary>
    public FeatlyOpenFeatureProvider(IFlagClient flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        _flags = flags;
    }

    /// <inheritdoc />
    public override Metadata GetMetadata() => s_metadata;

    /// <inheritdoc />
    public override async Task<ResolutionDetails<bool>> ResolveBooleanValueAsync(
        string flagKey, bool defaultValue, global::OpenFeature.Model.EvaluationContext? context = null, CancellationToken cancellationToken = default)
    {
        var result = await _flags.EvaluateAsync(flagKey, defaultValue, OpenFeatureContextMapper.ToFeatly(context), cancellationToken).ConfigureAwait(false);
        return Map(flagKey, defaultValue, result);
    }

    /// <inheritdoc />
    public override async Task<ResolutionDetails<string>> ResolveStringValueAsync(
        string flagKey, string defaultValue, global::OpenFeature.Model.EvaluationContext? context = null, CancellationToken cancellationToken = default)
    {
        var result = await _flags.EvaluateAsync(flagKey, defaultValue, OpenFeatureContextMapper.ToFeatly(context), cancellationToken).ConfigureAwait(false);
        return Map(flagKey, defaultValue, result);
    }

    /// <inheritdoc />
    public override async Task<ResolutionDetails<int>> ResolveIntegerValueAsync(
        string flagKey, int defaultValue, global::OpenFeature.Model.EvaluationContext? context = null, CancellationToken cancellationToken = default)
    {
        var result = await _flags.EvaluateAsync(flagKey, defaultValue, OpenFeatureContextMapper.ToFeatly(context), cancellationToken).ConfigureAwait(false);
        return Map(flagKey, defaultValue, result);
    }

    /// <inheritdoc />
    public override async Task<ResolutionDetails<double>> ResolveDoubleValueAsync(
        string flagKey, double defaultValue, global::OpenFeature.Model.EvaluationContext? context = null, CancellationToken cancellationToken = default)
    {
        var result = await _flags.EvaluateAsync(flagKey, defaultValue, OpenFeatureContextMapper.ToFeatly(context), cancellationToken).ConfigureAwait(false);
        return Map(flagKey, defaultValue, result);
    }

    /// <inheritdoc />
    public override async Task<ResolutionDetails<Value>> ResolveStructureValueAsync(
        string flagKey, Value defaultValue, global::OpenFeature.Model.EvaluationContext? context = null, CancellationToken cancellationToken = default)
    {
        var result = await _flags.EvaluateAsync(flagKey, s_nullJson, OpenFeatureContextMapper.ToFeatly(context), cancellationToken).ConfigureAwait(false);
        var (error, reason, message) = MapReason(result.Reason, result.Error);

        // On a miss or error, OpenFeature wants the caller's default echoed back.
        var value = error == ErrorType.None ? OpenFeatureContextMapper.ToValue(result.Value) : defaultValue;
        return new ResolutionDetails<Value>(
            flagKey, value, errorType: error, reason: reason,
            variant: NullIfEmpty(result.VariantKey), errorMessage: message);
    }

    private static ResolutionDetails<T> Map<T>(string flagKey, T defaultValue, EvaluationResult<T> result)
    {
        var (error, reason, message) = MapReason(result.Reason, result.Error);
        // OpenFeature contract: on abnormal execution the caller's default is returned.
        var value = error == ErrorType.None ? result.Value : defaultValue;
        return new ResolutionDetails<T>(
            flagKey, value, errorType: error, reason: reason,
            variant: NullIfEmpty(result.VariantKey), errorMessage: message);
    }

    /// <summary>Maps Featly's evaluation reason + error onto OpenFeature's reason string + error type.</summary>
    private static (ErrorType Error, string Reason, string? Message) MapReason(EvaluationReason reason, string? error) => reason switch
    {
        EvaluationReason.Static => (ErrorType.None, Reason.Static, null),
        EvaluationReason.TargetingMatch => (ErrorType.None, Reason.TargetingMatch, null),
        EvaluationReason.Split => (ErrorType.None, Reason.Split, null),
        EvaluationReason.Default => (ErrorType.None, Reason.Default, null),
        EvaluationReason.Disabled => (ErrorType.None, Reason.Disabled, null),
        EvaluationReason.NotFound => (ErrorType.FlagNotFound, Reason.Error, error ?? "Flag not found."),
        EvaluationReason.Error => (ErrorType.General, Reason.Error, error ?? "Evaluation error."),
        _ => (ErrorType.None, Reason.Unknown, null),
    };

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
