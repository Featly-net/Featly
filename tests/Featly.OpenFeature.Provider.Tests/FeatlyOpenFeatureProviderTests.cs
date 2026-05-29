using System.Text.Json;
using FluentAssertions;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;
using Xunit;
using FeatlyContext = Featly.EvaluationContext;

namespace Featly.OpenFeature.Tests;

/// <summary>
/// Covers <see cref="FeatlyOpenFeatureProvider"/>: per-type resolution, the
/// reason/error mapping, context translation, structure conversion, and one
/// end-to-end pass through the OpenFeature client API.
/// </summary>
public class FeatlyOpenFeatureProviderTests
{
    /// <summary>A hand-rolled <see cref="IFlagClient"/> that returns canned results.</summary>
    private sealed class FakeFlagClient : IFlagClient
    {
        private readonly Dictionary<string, (JsonElement Value, string Variant, EvaluationReason Reason, string? Error)> _flags
            = new(StringComparer.Ordinal);

        public FeatlyContext? LastContext { get; private set; }

        public FakeFlagClient Set(string key, object? value, string variant = "treatment", EvaluationReason reason = EvaluationReason.TargetingMatch, string? error = null)
        {
            _flags[key] = (JsonSerializer.SerializeToElement(value), variant, reason, error);
            return this;
        }

        public ValueTask<EvaluationResult<T>> EvaluateAsync<T>(string key, T defaultValue, FeatlyContext? context = null, CancellationToken ct = default)
        {
            LastContext = context;
            if (!_flags.TryGetValue(key, out var entry))
            {
                return ValueTask.FromResult(new EvaluationResult<T>(key, defaultValue, "", EvaluationReason.NotFound));
            }

            T value;
            try
            { value = entry.Value.Deserialize<T>() ?? defaultValue; }
            catch (JsonException) { value = defaultValue; }
            return ValueTask.FromResult(new EvaluationResult<T>(key, value, entry.Variant, entry.Reason, null, entry.Error));
        }

        public ValueTask<bool> IsEnabledAsync(string key, FeatlyContext? context = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public ValueTask<T> GetVariantAsync<T>(string key, T defaultValue, FeatlyContext? context = null, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    [Fact]
    public void Metadata_names_the_provider_Featly()
    {
        var provider = new FeatlyOpenFeatureProvider(new FakeFlagClient());
        provider.GetMetadata()!.Name.Should().Be("Featly");
    }

    [Fact]
    public async Task Boolean_targeting_match_maps_value_reason_and_variant()
    {
        var flags = new FakeFlagClient().Set("checkout", true, variant: "on", reason: EvaluationReason.TargetingMatch);
        var provider = new FeatlyOpenFeatureProvider(flags);

        var details = await provider.ResolveBooleanValueAsync("checkout", false, cancellationToken: TestContext.Current.CancellationToken);

        details.Value.Should().BeTrue();
        details.Reason.Should().Be(Reason.TargetingMatch);
        details.Variant.Should().Be("on");
        details.ErrorType.Should().Be(ErrorType.None);
    }

    [Fact]
    public async Task Missing_flag_returns_default_with_flag_not_found()
    {
        var provider = new FeatlyOpenFeatureProvider(new FakeFlagClient());

        var details = await provider.ResolveStringValueAsync("ghost", "fallback", cancellationToken: TestContext.Current.CancellationToken);

        details.Value.Should().Be("fallback");
        details.ErrorType.Should().Be(ErrorType.FlagNotFound);
        details.Reason.Should().Be(Reason.Error);
    }

    [Fact]
    public async Task Integer_and_double_resolve_typed_values()
    {
        var flags = new FakeFlagClient()
            .Set("max-items", 42, reason: EvaluationReason.Static)
            .Set("ratio", 1.5, reason: EvaluationReason.Split);
        var provider = new FeatlyOpenFeatureProvider(flags);
        var ct = TestContext.Current.CancellationToken;

        (await provider.ResolveIntegerValueAsync("max-items", 0, cancellationToken: ct)).Value.Should().Be(42);
        var dbl = await provider.ResolveDoubleValueAsync("ratio", 0d, cancellationToken: ct);
        dbl.Value.Should().Be(1.5);
        dbl.Reason.Should().Be(Reason.Split);
    }

    [Fact]
    public async Task Disabled_flag_maps_to_disabled_reason_without_error()
    {
        var flags = new FakeFlagClient().Set("beta", false, variant: "off", reason: EvaluationReason.Disabled);
        var provider = new FeatlyOpenFeatureProvider(flags);

        var details = await provider.ResolveBooleanValueAsync("beta", true, cancellationToken: TestContext.Current.CancellationToken);

        details.Value.Should().BeFalse();
        details.Reason.Should().Be(Reason.Disabled);
        details.ErrorType.Should().Be(ErrorType.None);
    }

    [Fact]
    public async Task Error_reason_maps_to_general_error_and_echoes_default()
    {
        var flags = new FakeFlagClient().Set("broken", "x", reason: EvaluationReason.Error, error: "boom");
        var provider = new FeatlyOpenFeatureProvider(flags);

        var details = await provider.ResolveStringValueAsync("broken", "safe", cancellationToken: TestContext.Current.CancellationToken);

        details.Value.Should().Be("safe");
        details.ErrorType.Should().Be(ErrorType.General);
        details.ErrorMessage.Should().Be("boom");
    }

    [Fact]
    public async Task Structure_resolves_a_json_object_into_an_openfeature_value()
    {
        var flags = new FakeFlagClient().Set("theme", new { color = "green", size = 3 }, reason: EvaluationReason.TargetingMatch);
        var provider = new FeatlyOpenFeatureProvider(flags);

        var details = await provider.ResolveStructureValueAsync("theme", new Value("default"), cancellationToken: TestContext.Current.CancellationToken);

        details.ErrorType.Should().Be(ErrorType.None);
        var structure = details.Value.AsStructure;
        structure.Should().NotBeNull();
        structure!.GetValue("color").AsString.Should().Be("green");
        structure.GetValue("size").AsDouble.Should().Be(3);
    }

    [Fact]
    public async Task Context_targeting_key_and_attributes_map_to_featly()
    {
        var flags = new FakeFlagClient().Set("x", true);
        var provider = new FeatlyOpenFeatureProvider(flags);
        var ofContext = global::OpenFeature.Model.EvaluationContext.Builder()
            .SetTargetingKey("user-42")
            .Set("country", "BR")
            .Set("beta", true)
            .Build();

        await provider.ResolveBooleanValueAsync("x", false, ofContext, TestContext.Current.CancellationToken);

        flags.LastContext.Should().NotBeNull();
        flags.LastContext!.TargetingKey.Should().Be("user-42");
        flags.LastContext.Attributes.Should().NotBeNull();
        flags.LastContext.Attributes!["country"].Should().Be("BR");
        flags.LastContext.Attributes["beta"].Should().Be(true);
    }

    [Fact]
    public async Task End_to_end_through_the_openfeature_client()
    {
        var flags = new FakeFlagClient().Set("new-checkout", true, variant: "on");
        await Api.Instance.SetProviderAsync(new FeatlyOpenFeatureProvider(flags));
        try
        {
            var client = Api.Instance.GetClient();
            var value = await client.GetBooleanValueAsync("new-checkout", false, cancellationToken: TestContext.Current.CancellationToken);
            value.Should().BeTrue();
        }
        finally
        {
            await Api.Instance.ShutdownAsync();
        }
    }
}
