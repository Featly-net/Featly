---
title: Modularity
description: Run only the feature areas you need — flags-only, configs-only, or any combination — to shrink Featly's footprint.
---

Featly is a broad platform, but not every deployment needs every part. **Feature
areas are opt-out toggles** ([ADR-0024](https://github.com/Featly-net/Featly/blob/main/docs/adr/0024-modular-feature-areas.md)):
disable the ones you don't use and their HTTP endpoints disappear and they vanish
from the dashboard nav. Defaults stay **everything on**, so this is never a
breaking change — you opt *out*.

## What you can toggle

Under `Featly:Server:Features` (all default `true`):

| Toggle | Turns off |
|---|---|
| `Flags` | feature flags |
| `Configs` | dynamic configuration |
| `Segments` | reusable audiences |
| `Experiments` | A/B testing + exposure events |
| `Approvals` | approval workflows / pending changes |
| `Webhooks` | outbound webhook delivery |
| `Audit` | the audit log |
| `Rbac` | role-management UI/API (permission *checks* always run) |

A small set of endpoints is **always on** — authentication, bootstrap, meta,
environments, projects, API keys, settings, export, and the SDK API — because the
rest of the system depends on them.

## How to configure it

Set the toggles in `appsettings.json`:

```json
{
  "Featly": {
    "Server": {
      "Features": {
        "Experiments": false,
        "Webhooks": false,
        "Segments": false
      }
    }
  }
}
```

Or in code when you register the server:

```csharp
builder.Services.AddFeatlyServer(options =>
{
    options.Features.Experiments = false;
    options.Features.Webhooks = false;
});
```

### A flags-only deployment

```json
{
  "Featly": {
    "Server": {
      "Features": {
        "Configs": false,
        "Segments": false,
        "Experiments": false,
        "Approvals": false,
        "Webhooks": false
      }
    }
  }
}
```

This leaves the always-on core plus flags. The dashboard shows only the Flags
area, and the disabled endpoints return `404` — there is no dead surface.

## How it works

The dashboard discovers what's enabled at runtime from an anonymous
`GET /api/meta` endpoint that returns the active feature flags. The nav and the
command palette filter themselves to the enabled areas; the server gates the
corresponding endpoint groups at mount time. Toggling an area is a restart, not a
migration — the data model is unchanged, so you can turn an area back on later
without losing anything.

:::note[Future direction]
ADR-0024 chose a **hybrid** approach: DI toggles now, with a possible package
split (separate NuGet packages per area) deferred. Today you reference the full
server package and toggle areas off; a finer-grained package split may come later
if there's demand.
:::
