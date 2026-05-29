# Getting started

This guide takes you from nothing to a running Featly dashboard with a live
feature flag in a few minutes. Featly embeds inside your ASP.NET Core process
(like Hangfire): two DI calls plus a middleware mount and you are operational.

> New to the design? Read [ARCHITECTURE.md](../ARCHITECTURE.md) for the why.
> Looking for every knob? See [CONFIGURATION.md](CONFIGURATION.md). Shipping to
> production? See [DEPLOYMENT.md](DEPLOYMENT.md).

## 1. Install

For the embedded quickstart (server + dashboard + SQLite, all in your app):

```bash
dotnet add package Featly.Server
dotnet add package Featly.Dashboard
dotnet add package Featly.Storage.Sqlite
```

A consumer that only *reads* flags from a remote Featly server needs just the SDK:

```bash
dotnet add package Featly.Sdk
```

## 2. Wire it up (embedded)

```csharp
using Featly.Dashboard;
using Featly.Server;
using Featly.Storage.Sqlite;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFeatlySqliteStore(); // a featly.db file, schema auto-applied
builder.Services.AddFeatlyServer();      // admin + SDK HTTP APIs, auth, approvals, webhooks

var app = builder.Build();

app.MapFeatlyDashboard("/featly"); // dashboard UI
app.MapFeatlyApi();                // /health/live + /api/admin/* + /api/sdk/*

app.Run();
```

Set an admin key in `appsettings.json` so you can authenticate:

```json
{
  "Featly": {
    "Server": { "AdminApiKey": "replace-with-a-strong-secret", "SdkApiKey": "replace-too" }
  }
}
```

Run it, then open `http://localhost:<port>/featly`. The default project and a
`development` environment are created on first boot.

## 3. Get an admin identity

The `AdminApiKey` above is a bootstrap shortcut. For a real, auditable admin
identity (so approvals attribute to a person), use the CLI's first-run bootstrap
against the running server — it works only while no users exist yet:

```bash
dotnet tool install -g Featly.Cli
featly bootstrap-admin --identifier you@example.com --server-url http://localhost:5080
```

It prints an admin token **once**. Store it; it acts as your user. Mint more
keys later with `featly apikey generate --name ci --user you@example.com`.

## 4. Create a flag

Via the dashboard (log in with the admin key or token), or via the API:

```bash
curl -X POST http://localhost:5080/api/admin/flags \
  -H "Authorization: Bearer <admin token>" -H "Content-Type: application/json" \
  -d '{
    "key": "new-checkout",
    "name": "New checkout",
    "type": "Boolean",
    "enabled": true,
    "defaultVariantKey": "off",
    "variants": [
      { "key": "on",  "name": "On",  "value": true  },
      { "key": "off", "name": "Off", "value": false }
    ],
    "rules": [
      { "order": 1, "outcome": { "variantKey": "on" },
        "conditions": [ { "attribute": "user.country", "operator": "Equals", "value": "BR" } ] }
    ]
  }'
```

This flag is `on` for `user.country=BR`, `off` otherwise.

## 5. Evaluate it from your app

In the consuming application (or the same app), register the SDK and evaluate
locally — there is **no network call on the hot path**; the SDK serves a cached,
fresh-by-default snapshot:

```csharp
builder.Services.AddFeatly()
    .UseServer("http://localhost:5080", "replace-too"); // SDK key

// ...
var featly = app.Services.GetRequiredService<Featly.IFeatlyClient>();

var ctx = new Featly.EvaluationContext { TargetingKey = "alice" };
ctx.Attributes["user.country"] = "BR";

bool useV2 = await featly.Flags.IsEnabledAsync("new-checkout", ctx);
```

Prefer a vendor-neutral API? Use the
[OpenFeature provider](OPENFEATURE.md) — the same flag, read through
`Api.Instance.GetClient()`.

## Runnable samples

- **`samples/SelfHosted.Sample`** — embed everything in one app (this guide).
- **`samples/WebApi.Sample`** — an SDK consumer with targeting + a config demo.
- **`samples/Centralized.Sample`** — a standalone Featly server other apps point at.
- **`samples/OpenFeature.Sample`** — reads flags through the OpenFeature client.

## Next steps

- [CONFIGURATION.md](CONFIGURATION.md) — every setting, the three-layer precedence, environment variables, the CLI.
- [DEPLOYMENT.md](DEPLOYMENT.md) — the three deployment patterns and a production checklist.
- [OPENFEATURE.md](OPENFEATURE.md) — adopt the OpenFeature API.
