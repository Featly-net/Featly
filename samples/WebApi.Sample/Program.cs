using Featly;
using Featly.AspNetCore;
using Featly.Sdk;

// CA1861 hoists these tiny demo hints into a static readonly so the closure
// captures the existing reference instead of re-allocating on every request.
string[] tryItHints =
[
    "/checkout                                 — flag defaults, no context",
    "/checkout?country=BR&plan=pro             — explicit query targeting",
    "/checkout?targetingKey=alice@x.io         — drives split bucketing",
    "/checkout/timeout                         — dynamic config, default value",
    "/checkout/timeout?country=BR              — config rule per country",
];

var builder = WebApplication.CreateBuilder(args);

// Wire the SDK. ServerUrl + ApiKey come from appsettings (Featly:Sdk section)
// so the sample is honest about how production wiring will look.
//
// UseHttpContextAccessor swaps the SDK's no-op context accessor for one that
// reads HttpContext.User claims — that is how SDK calls inside an endpoint
// automatically see the current request's targeting context.
builder.Services.AddFeatly()
    .UseServer(
        serverUrl: builder.Configuration["Featly:Sdk:ServerUrl"] ?? "http://localhost:5080",
        apiKey: builder.Configuration["Featly:Sdk:ApiKey"] ?? "dev-sdk-replace-me")
    .UseHttpContextAccessor();

var app = builder.Build();

// Health probe.
app.MapGet("/", () => Results.Ok(new
{
    sample = "Featly.Samples.WebApi",
    docs = "https://github.com/Featly-net/Featly",
    tryIt = tryItHints,
}));

// Flag-driven endpoint that demonstrates targeting.
//
// Build a context from query string (no auth in this demo) and call the SDK.
// In real apps the context comes from claims via UseHttpContextAccessor, but
// query-string lets you smoke-test rules from curl without setting up auth.
app.MapGet("/checkout", async (HttpContext http, IFeatlyClient featly, CancellationToken ct) =>
{
    var country = http.Request.Query["country"].ToString();
    var plan = http.Request.Query["plan"].ToString();
    var targetingKey = http.Request.Query["targetingKey"].ToString();

    EvaluationContext? ctx = null;
    if (!string.IsNullOrWhiteSpace(country) ||
        !string.IsNullOrWhiteSpace(plan) ||
        !string.IsNullOrWhiteSpace(targetingKey))
    {
        var attrs = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(country))
        { attrs["user.country"] = country; }
        if (!string.IsNullOrWhiteSpace(plan))
        { attrs["user.plan"] = plan; }
        ctx = new EvaluationContext(
            TargetingKey: string.IsNullOrWhiteSpace(targetingKey) ? null : targetingKey,
            Attributes: attrs);
    }

    var result = await featly.Flags.EvaluateAsync("new-checkout-flow", defaultValue: false, ctx, ct);

    return Results.Ok(new
    {
        flag = "new-checkout-flow",
        ui = result.Value ? "v2" : "v1",
        message = result.Value
            ? "New checkout served by Featly."
            : "Legacy checkout (default when the flag is off or no rule matched).",
        reason = result.Reason.ToString(),
        ruleMatched = result.RuleMatched,
        variant = result.VariantKey,
        contextUsed = ctx is null ? null : new
        {
            targetingKey = ctx.TargetingKey,
            attributes = ctx.Attributes,
        },
    });
});

// Config-driven endpoint. Dynamic configs share the targeting engine with flags
// but produce a typed value directly instead of selecting a variant. Operators
// edit `checkout.timeout` in the dashboard and the change propagates to every
// connected SDK without redeploying.
app.MapGet("/checkout/timeout", async (HttpContext http, IFeatlyClient featly, CancellationToken ct) =>
{
    var country = http.Request.Query["country"].ToString();
    var plan = http.Request.Query["plan"].ToString();

    EvaluationContext? ctx = null;
    if (!string.IsNullOrWhiteSpace(country) || !string.IsNullOrWhiteSpace(plan))
    {
        var attrs = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(country))
        { attrs["user.country"] = country; }
        if (!string.IsNullOrWhiteSpace(plan))
        { attrs["user.plan"] = plan; }
        ctx = new EvaluationContext(Attributes: attrs);
    }

    // 30s is the bootstrap fallback used while the SDK is still warming up the
    // snapshot — once Featly returns the config, this value is overridden by
    // whichever rule (or default) matches the context.
    var result = await featly.Configs.EvaluateAsync("checkout.timeout", defaultValue: 30, ctx, ct);

    return Results.Ok(new
    {
        config = "checkout.timeout",
        seconds = result.Value,
        reason = result.Reason.ToString(),
        ruleMatched = result.RuleMatched,
        contextUsed = ctx is null ? null : new { attributes = ctx.Attributes },
    });
});

app.Run();

/// <summary>Marker type so test factories can find this entry point.</summary>
public partial class Program;
