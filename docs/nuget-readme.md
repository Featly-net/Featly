# Featly

**Feature management for .NET.** Feature flags, dynamic configuration, segments, experiments, and enterprise governance. Embed the server, dashboard, and SDK inside your ASP.NET Core process like Hangfire, or host it centrally for many consumers. Bring your own database.

## Status

**Preview.** Milestones M1–M12 are complete: flags, configs, segments, experiments, projects and environments, custom RBAC, approval workflows, audit log, webhooks, an embedded dashboard, an OpenFeature provider, and a `featly` CLI all ship and are covered by the test suite.

The public API is **pre-1.0 and may still change between previews**, so pin an exact version. It is not a placeholder — it works end to end; it is simply not yet promised to be source-compatible with 1.0.

## Install

Embed the server + dashboard in your own app:

```shell
dotnet add package Featly.Server
dotnet add package Featly.Dashboard
dotnet add package Featly.Storage.Sqlite
```

Consume flags from any .NET app:

```shell
dotnet add package Featly.Sdk
dotnet add package Featly.AspNetCore
```

## Quick start

Host Featly inside your ASP.NET Core app:

```csharp
using Featly.Dashboard;
using Featly.Server;
using Featly.Storage.Sqlite;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFeatlySqliteStore();   // schema applied on first boot
builder.Services.AddFeatlyServer();

var app = builder.Build();

app.MapFeatlyDashboard("/featly");         // UI at /featly
app.MapFeatlyApi();                        // SDK + admin endpoints

app.Run();
```

Consume it from any .NET app:

```csharp
using Featly;
using Featly.AspNetCore;
using Featly.Sdk;

builder.Services.AddFeatly()
    .UseServer(serverUrl: "https://features.example.com", apiKey: "...")
    .UseHttpContextAccessor();
```

```csharp
app.MapGet("/checkout", async (IFeatlyClient featly, CancellationToken ct) =>
{
    if (await featly.Flags.IsEnabledAsync("new-checkout-flow", ct: ct))
    {
        var timeout = await featly.Configs.GetAsync("checkout.timeout", 30, ct: ct);
        return Results.Ok(new { ui = "v2", timeout });
    }

    return Results.Ok(new { ui = "v1" });
});
```

Evaluation is **local**: the SDK evaluates against a cached, fresh-by-default snapshot, so there is no network call on the hot path.

## Learn more

- **Docs:** <https://featly-net.github.io/Featly/>
- **Getting started:** <https://github.com/Featly-net/Featly/blob/main/docs/GETTING_STARTED.md>
- **Architecture:** <https://github.com/Featly-net/Featly/blob/main/ARCHITECTURE.md>
- **Repository / issues:** <https://github.com/Featly-net/Featly>
- **Changelog:** <https://github.com/Featly-net/Featly/blob/main/CHANGELOG.md>

## License

[MIT](https://github.com/Featly-net/Featly/blob/main/LICENSE)
