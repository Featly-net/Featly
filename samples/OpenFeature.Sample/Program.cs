using Featly.OpenFeature;
using Featly.Sdk;
using OpenFeature;
using OpenFeature.Model;

// This sample shows a vendor-neutral consumer: every flag read goes through the
// OpenFeature SDK (Api.Instance.GetClient()), and Featly is wired in once as the
// provider. The application code below never references a Featly type — swapping
// providers would not touch a single call site.

string[] tryItHints =
[
    "/checkout                          — flag defaults, no context",
    "/checkout?country=BR               — query targeting via OpenFeature context",
    "/checkout?targetingKey=alice@x.io  — drives split bucketing",
];

var builder = WebApplication.CreateBuilder(args);

// Wire the Featly SDK exactly like any other Featly consumer.
builder.Services.AddFeatly()
    .UseServer(
        serverUrl: builder.Configuration["Featly:Sdk:ServerUrl"] ?? "http://localhost:5080",
        apiKey: builder.Configuration["Featly:Sdk:ApiKey"] ?? "dev-sdk-replace-me");

var app = builder.Build();

// Register Featly as the OpenFeature provider once, at startup. From here on the
// app talks to OpenFeature, not Featly.
var featly = app.Services.GetRequiredService<Featly.IFeatlyClient>();
await Api.Instance.SetProviderAsync(new FeatlyOpenFeatureProvider(featly));

app.MapGet("/", () => Results.Ok(new
{
    sample = "Featly.Samples.OpenFeature",
    provider = Api.Instance.GetProviderMetadata()?.Name,
    docs = "https://github.com/Featly-net/Featly/blob/main/docs/OPENFEATURE.md",
    tryIt = tryItHints,
}));

// A flag read through the OpenFeature client. The provider delegates to Featly,
// but this endpoint is pure OpenFeature — note there is no Featly type in sight.
app.MapGet("/checkout", async (HttpContext http) =>
{
    var country = http.Request.Query["country"].ToString();
    var targetingKey = http.Request.Query["targetingKey"].ToString();

    EvaluationContext? context = null;
    if (!string.IsNullOrWhiteSpace(country) || !string.IsNullOrWhiteSpace(targetingKey))
    {
        var b = EvaluationContext.Builder();
        if (!string.IsNullOrWhiteSpace(targetingKey))
        {
            b.SetTargetingKey(targetingKey);
        }
        if (!string.IsNullOrWhiteSpace(country))
        {
            b.Set("user.country", country);
        }
        context = b.Build();
    }

    var client = Api.Instance.GetClient();
    var details = await client.GetBooleanDetailsAsync("new-checkout-flow", false, context);

    return Results.Ok(new
    {
        flag = details.FlagKey,
        ui = details.Value ? "v2" : "v1",
        reason = details.Reason,
        variant = details.Variant,
        errorType = details.ErrorType.ToString(),
    });
});

await app.RunAsync();

/// <summary>Marker type so test factories can find this entry point.</summary>
public partial class Program;
