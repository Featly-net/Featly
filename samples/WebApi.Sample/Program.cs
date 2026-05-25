// WebApi.Sample — placeholder for M1.
// Real SDK consumer wiring (AddFeatly().UseServer(...).UseContextAccessor<...>())
// arrives in M2 when the SDK client surface comes online.

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    message = "Featly WebApi.Sample placeholder. Full SDK wiring lands in M2.",
    docs = "https://github.com/Featly-net/Featly/blob/main/PLAN.md"
}));

app.Run();

/// <summary>
/// Marker type so test factories can find this entry point.
/// </summary>
public partial class Program;
