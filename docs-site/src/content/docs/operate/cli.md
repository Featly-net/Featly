---
title: CLI
description: The featly global tool — offline schema management and online admin operations against a running server.
---

`Featly.Cli` is a .NET global tool that operates the Featly store and server from
the command line. It is **hybrid** ([ADR-0022](https://github.com/Featly-net/Featly/blob/main/docs/adr/0022-cli-hybrid-online-offline.md)):
`db` commands run **offline** against the database file (the server cannot start
before its schema exists); every other command runs **online** against the
server's admin API, reusing its permission checks, audit log, and webhooks.

## Install

```bash
dotnet tool install -g Featly.Cli
featly --help
```

The command is `featly`.

## Connection and credentials

Most options fall back to environment variables (see also
[Configuration](/Featly/operate/configuration/#cli-environment-variables)):

| Option | Env fallback | Default |
|---|---|---|
| `--connection-string` / `-c` | `FEATLY_SQLITE` | `Data Source=featly.db` |
| `--server-url` / `-s` | `FEATLY_SERVER_URL` | `http://localhost:5080` |
| `--api-key` / `-k` | `FEATLY_API_KEY` | — (required for online admin commands) |

## `featly db` — schema (offline)

Operates directly on the SQLite database file. Run these from your release
pipeline before the new server version starts.

```bash
featly db status      # show applied and pending migrations
featly db migrate     # apply all pending migrations
featly db rollback <target>   # revert down to a target migration (destructive)
featly db rollback 0          # revert every migration (the initial empty schema)
featly db drop        # delete the entire database (irreversible)
```

`rollback` and `drop` prompt for confirmation; pass `--yes` / `-y` to skip the
prompt in scripts.

## `featly bootstrap-admin` — first admin (online, no key)

Provisions the very first administrator on a fresh server. The endpoint is
unauthenticated **by necessity** but self-guards — it refuses once any user
exists — so this command needs only a server URL, no API key.

```bash
featly bootstrap-admin --identifier you@example.com --server-url http://localhost:5080
```

It prints an admin token **once**. Store it.

## `featly apikey` — keys (online)

```bash
featly apikey generate \
  --name ci \
  --user you@example.com \
  --scope AdminWrite \
  --environment production
```

Mints a key and prints the plaintext token **once**. `--scope` is `AdminWrite`
(default) or `SdkRead`. Binding `--user` makes the key act as that person in RBAC,
the audit log, and approvals ([ADR-0023](https://github.com/Featly-net/Featly/blob/main/docs/adr/0023-user-bound-api-keys.md)).
Add `--expires-in <days>` to mint a key that stops authenticating after that many
days; omit it for a key that never expires.

```bash
featly apikey rotate <id> --expires-in 90
```

Rotates a key: mints a replacement with the same name, scope, environment, and
user binding, then revokes the old key. The replacement's token is printed
**once**. Without `--expires-in` the replacement inherits the old key's expiry.
Find the `<id>` in the dashboard's API keys screen or via `GET /api/admin/apikeys`.

## `featly env` — environment lock (online)

```bash
featly env lock production      # freeze: reject all mutations
featly env unlock production    # unfreeze
```

Use the lock to freeze an environment during an incident. See
[Governance](/Featly/concepts/governance/#readonly-environment-lock).

## `featly flags` — list and toggle (online)

```bash
featly flags list --environment production
featly flags enable checkout-v2 --environment production
featly flags disable checkout-v2 --environment production
```

`list` prints a table (key, enabled, type, variant count, name). `enable` /
`disable` flip a flag's master switch — everything else about the flag
(variants, rules, tags) is read back from the server and re-sent untouched, so
this is safe to script without knowing the flag's full definition. For
anything beyond the master switch (rules, variants), use the dashboard or the
admin API directly.

## `featly export` / `featly import` — definitions (online)

Move flag/config/segment **definitions** (never users, keys, or audit) between
environments as a JSON bundle.

```bash
# Export an environment to a file (or stdout if --output is omitted):
featly export --environment production --output prod.json

# Import a bundle into an environment (upserts definitions by key):
featly import prod.json --environment staging
```

## Exit codes

| Code | Meaning |
|---|---|
| `0` | success |
| `1` | error (the message is printed as a friendly one-liner, not a stack trace) |
| `130` | cancelled (Ctrl-C) |

The CLI is built on **System.CommandLine 2.0**; run any command with `--help` to
see its full options.
