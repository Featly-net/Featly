using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Featly;
using Featly.Engine;

namespace Featly.Engine.Benchmarks;

/// <summary>
/// Hot-path microbenchmarks for the evaluator. Goal per ARCHITECTURE.md §19:
/// p50 &lt; 1 μs, p99 &lt; 10 μs for boolean evaluation with a warm cache.
/// </summary>
/// <remarks>
/// Run from the repo root with:
///   dotnet run -c Release --project tests/Featly.Engine.Benchmarks -- --filter '*'
/// Baseline numbers are checked into docs/PERFORMANCE.md and updated on every
/// engine release.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5, invocationCount: 16384)]
public class EvaluatorBenchmarks
{
    private static readonly Guid EnvId = Guid.NewGuid();
    private static readonly JsonElement Fallback = JsonSerializer.SerializeToElement(false);
    private static readonly string[] EnterprisePlanArray = ["enterprise", "pro"];

    private Flag _booleanNoRules = null!;
    private Flag _singleConditionRule = null!;
    private Flag _fiveRulesThreeConditions = null!;
    private Flag _splitFlag = null!;
    private Flag _segmentFlag = null!;
    private Flag _prerequisiteFlag = null!;

    private EvaluationContext _ctx = null!;
    private DictionarySegmentLookup _segmentLookup = null!;
    private DictionaryFlagLookup _flagLookup = null!;

    [GlobalSetup]
    public void Setup()
    {
        _ctx = new EvaluationContext(
            TargetingKey: "user-12345",
            Attributes: new Dictionary<string, object?>
            {
                ["user.country"] = "BR",
                ["user.plan"] = "enterprise",
                ["user.age"] = 30,
                ["user.email"] = "alice@example.com",
                ["user.version"] = "2.5.1",
            });

        _booleanNoRules = BuildBoolFlag([]);

        _singleConditionRule = BuildBoolFlag(
        [
            new Rule
            {
                Order = 0,
                Conditions =
                [
                    new Condition
                    {
                        Attribute = "user.country",
                        Operator = ConditionOperator.Equals,
                        Value = JsonSerializer.SerializeToElement("BR"),
                    },
                ],
                Outcome = new RuleOutcome { VariantKey = "on" },
            },
        ]);

        _fiveRulesThreeConditions = BuildBoolFlag(
        [
            BuildComplexRule(0, country: "US"),
            BuildComplexRule(1, country: "UK"),
            BuildComplexRule(2, country: "DE"),
            BuildComplexRule(3, country: "FR"),
            BuildComplexRule(4, country: "BR"), // this one matches our context
        ]);

        _splitFlag = BuildBoolFlag(
        [
            new Rule
            {
                Order = 0,
                Outcome = new RuleOutcome
                {
                    Splits =
                    [
                        new Split { VariantKey = "on",  Weight = 50 },
                        new Split { VariantKey = "off", Weight = 50 },
                    ],
                },
            },
        ]);

        var enterprise = new Segment
        {
            Id = Guid.NewGuid(),
            Key = "enterprise",
            Name = "Enterprise",
            EnvironmentId = EnvId,
            Conditions =
            [
                new Condition
                {
                    Attribute = "user.plan",
                    Operator = ConditionOperator.Equals,
                    Value = JsonSerializer.SerializeToElement("enterprise"),
                },
            ],
        };
        _segmentLookup = new DictionarySegmentLookup(new Dictionary<string, Segment> { ["enterprise"] = enterprise });

        _segmentFlag = BuildBoolFlag(
        [
            new Rule
            {
                Order = 0,
                Conditions =
                [
                    new Condition
                    {
                        Attribute = "ignored",
                        Operator = ConditionOperator.InSegment,
                        Value = JsonSerializer.SerializeToElement("enterprise"),
                    },
                ],
                Outcome = new RuleOutcome { VariantKey = "on" },
            },
        ]);

        var infraFlag = BuildBoolFlag([], key: "infra", defaultVariantKey: "on");
        _flagLookup = new DictionaryFlagLookup(new Dictionary<string, Flag> { ["infra"] = infraFlag });

        _prerequisiteFlag = BuildBoolFlag([]);
        _prerequisiteFlag.Prerequisites = [new Prerequisite { FlagKey = "infra", RequiredVariantKeys = ["on"] }];
    }

    [Benchmark(Description = "Boolean flag, no rules (degenerate fast path)", Baseline = true)]
    public EvaluationResult<JsonElement> Boolean_NoRules()
        => Evaluator.EvaluateFlag(_booleanNoRules, _ctx, Fallback);

    [Benchmark(Description = "1 rule, 1 Equals condition (matches)")]
    public EvaluationResult<JsonElement> SingleConditionRule()
        => Evaluator.EvaluateFlag(_singleConditionRule, _ctx, Fallback);

    [Benchmark(Description = "5 rules x 3 conditions, last rule matches")]
    public EvaluationResult<JsonElement> FiveRulesThreeConditions()
        => Evaluator.EvaluateFlag(_fiveRulesThreeConditions, _ctx, Fallback);

    [Benchmark(Description = "50/50 split bucketing")]
    public EvaluationResult<JsonElement> SplitBucketing()
        => Evaluator.EvaluateFlag(_splitFlag, _ctx, Fallback);

    [Benchmark(Description = "InSegment lookup + nested condition")]
    public EvaluationResult<JsonElement> SegmentLookup()
        => Evaluator.EvaluateFlag(_segmentFlag, _ctx, Fallback, _segmentLookup);

    [Benchmark(Description = "1 prerequisite, met (no rules of its own)")]
    public EvaluationResult<JsonElement> PrerequisiteMet()
        => Evaluator.EvaluateFlag(_prerequisiteFlag, _ctx, Fallback, flags: _flagLookup);

    // ---- helpers ----

    private static Flag BuildBoolFlag(Rule[] rules, string key = "demo", string defaultVariantKey = "off") => new()
    {
        Id = Guid.NewGuid(),
        Key = key,
        Name = "Demo",
        Type = FlagType.Boolean,
        Enabled = true,
        DefaultVariantKey = defaultVariantKey,
        EnvironmentId = EnvId,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        Variants =
        [
            new Variant { Key = "on",  Name = "On",  Value = JsonSerializer.SerializeToElement(true) },
            new Variant { Key = "off", Name = "Off", Value = JsonSerializer.SerializeToElement(false) },
        ],
        Rules = [.. rules],
    };

    private static Rule BuildComplexRule(int order, string country) => new()
    {
        Order = order,
        Name = $"rule-{order}",
        Conditions =
        [
            new Condition
            {
                Attribute = "user.country",
                Operator = ConditionOperator.Equals,
                Value = JsonSerializer.SerializeToElement(country),
            },
            new Condition
            {
                Attribute = "user.plan",
                Operator = ConditionOperator.In,
                Value = JsonSerializer.SerializeToElement(EnterprisePlanArray),
            },
            new Condition
            {
                Attribute = "user.age",
                Operator = ConditionOperator.GreaterThanOrEqual,
                Value = JsonSerializer.SerializeToElement(18),
            },
        ],
        Outcome = new RuleOutcome { VariantKey = "on" },
    };
}
