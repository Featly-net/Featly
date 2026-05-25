using Featly;
using Featly.Sdk;

var builder = WebApplication.CreateBuilder(args);

// Wire the SDK. ServerUrl + ApiKey come from appsettings (Featly:Sdk section)
// so the sample is honest about how production wiring will look.
builder.Services.AddFeatly()
    .UseServer(
        serverUrl: builder.Configuration["Featly:Sdk:ServerUrl"] ?? "http://localhost:5080",
        apiKey: builder.Configuration["Featly:Sdk:ApiKey"] ?? "dev-sdk-replace-me");

var app = builder.Build();

// Health probe.
app.MapGet("/", () => Results.Ok(new
{
    sample = "Featly.Samples.WebApi",
    docs = "https://github.com/Featly-net/Featly"
}));

// Example flag-driven endpoint.
app.MapGet("/checkout", async (IFeatlyClient featly, CancellationToken ct) =>
{
    var enabled = await featly.Flags.IsEnabledAsync("new-checkout-flow", ct: ct);
    return Results.Ok(new
    {
        flag = "new-checkout-flow",
        ui = enabled ? "v2" : "v1",
        message = enabled
            ? "New checkout served by Featly."
            : "Legacy checkout (default when the flag is off or missing).",
    });
});

app.Run();

/// <summary>Marker type so test factories can find this entry point.</summary>
public partial class Program;
