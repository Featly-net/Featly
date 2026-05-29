# Configuration

Featly is configured through the standard ASP.NET Core configuration pipeline
(`appsettings.json`, environment variables, user secrets) plus, for
runtime-editable settings, the database.

## Three-layer precedence

Settings resolve in this order, highest wins ([ADR-0016](adr/0016-database-overrides-appsettings.md)):

1. **Hardcoded defaults** — sensible defaults baked into the code.
2. **`appsettings.json` / environment variables** — the bootstrap baseline.
3. **Database** — overrides both, for settings an operator edits in the dashboard.

Some settings are **bootstrap-only** and cannot live in the database, because they
are read before the database connection exists: the connection string, the
`AutoMigrate` flag, Kestrel URLs, and the bootstrap admin identifier.

## `Featly:Server`

Bound from configuration into `FeatlyServerOptions`.

| Key | Default | Notes |
|---|---|---|
| `AdminApiKey` | `""` | Static bearer key for the admin API + dashboard login. A bootstrap shortcut — prefer minted, user-bound keys (see below). Treat as a secret. |
| `SdkApiKey` | `""` | Static bearer key for the SDK API (`/api/sdk/*`). Treat as a secret. |
| `AutoCreateDefaultProject` | `true` | Create a default project + environment on first boot. |
| `DefaultProjectKey` | `default` | Key of the auto-created project. |
| `DefaultEnvironmentKey` | `development` | Key of the auto-created environment. |

## `Featly:Storage:Sqlite`

Bound into `SqliteFeatlyStoreOptions`.

| Key | Default | Notes |
|---|---|---|
| `ConnectionString` | `Data Source=featly.db` | Any Microsoft.Data.Sqlite connection string. Bootstrap-only. |
| `AutoMigrate` | `true` | Apply pending EF Core migrations at startup. Set `false` in production and run `featly db migrate` from your release pipeline. Bootstrap-only. |

## `Featly:Authorization`

Bound into `FeatlyAuthorizationOptions`.

| Key | Default | Notes |
|---|---|---|
| `BootstrapAdminIdentifier` | `""` | When set, the identifier (email / OIDC sub) is treated as admin on first boot and the user row is seeded. Bootstrap-only. An alternative to `featly bootstrap-admin`. |
| `AutoProvisionMode` | `Open` | `Open`: an authenticated user with no role assignment gets the viewer floor. `Closed`: deny unless an explicit assignment grants access. |

## `Featly:Webhooks`

Bound into `WebhookOptions` — the delivery worker's tuning. DB-overridable.

| Key | Default | Notes |
|---|---|---|
| `PollInterval` | `00:00:05` | How often the worker drains the delivery queue. |
| `BatchSize` | `50` | Max deliveries claimed per poll. |
| `MaxAttempts` | `6` | Attempts before a delivery is dead-lettered. |
| `BaseRetryDelay` | `00:00:10` | Exponential backoff base (`base · 2^(n-1)`). |
| `MaxRetryDelay` | `00:30:00` | Backoff cap. |
| `RequestTimeout` | `00:00:10` | Per-delivery HTTP timeout. |

## Example `appsettings.json`

```json
{
  "Featly": {
    "Server": {
      "AdminApiKey": "set-via-secret",
      "SdkApiKey": "set-via-secret",
      "DefaultEnvironmentKey": "production"
    },
    "Storage": { "Sqlite": { "ConnectionString": "Data Source=/var/lib/featly/featly.db", "AutoMigrate": false } },
    "Authorization": { "AutoProvisionMode": "Closed" },
    "Webhooks": { "MaxAttempts": 8 }
  }
}
```

Keep secrets out of source control: use environment variables
(`Featly__Server__AdminApiKey=...`) or a secret store, not committed JSON.

## CLI environment variables

The [`featly` CLI](#) reads these when an explicit option is not passed:

| Variable | Used by | Falls back to |
|---|---|---|
| `FEATLY_SQLITE` | `featly db *` (offline) | `Data Source=featly.db` |
| `FEATLY_SERVER_URL` | `featly apikey` / `env` / `export` / `import` / `bootstrap-admin` | `http://localhost:5080` |
| `FEATLY_API_KEY` | the online admin commands (not `bootstrap-admin`) | — (required) |

## API keys

The static `AdminApiKey` / `SdkApiKey` are bootstrap shortcuts. For real,
auditable identities, mint keys bound to a user:

```bash
featly apikey generate --name ci --user you@example.com --scope AdminWrite
```

A user-bound key authenticates over `Authorization: Bearer` and acts as that
user, so RBAC, the audit log, and approvals attribute the action to a real
person. See [ADR-0023](adr/0023-user-bound-api-keys.md).
