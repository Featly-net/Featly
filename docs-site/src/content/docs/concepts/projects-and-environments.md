---
title: Projects and environments
description: How Featly isolates flags by application (projects) and deployment target (environments), and how API keys are scoped.
---

## Projects

A **project** is the top-level grouping that isolates one application's flags,
configs, and segments ([ADR-0011](https://github.com/Featly-net/Featly/blob/main/docs/adr/0011-project-top-level-grouping.md)).
When several services share one Featly database (the
[shared-database deployment pattern](/Featly/operate/deployment/)), each service's
definitions stay in their own project and never collide.

On first boot Featly auto-creates a default project so the quickstart needs zero
setup ([ADR-0019](https://github.com/Featly-net/Featly/blob/main/docs/adr/0019-auto-create-default-project-environment.md)).
You can disable that (`Featly:Server:AutoCreateDefaultProject=false`) and manage
projects explicitly.

## Environments

An **environment** is a deployment target — `development`, `staging`,
`production`, and whatever else you define. A flag has **per-environment values
and rules**: the same `new-checkout` flag can be `on` for everyone in
`development`, a 5% canary in `staging`, and `off` in `production`, all at once.

Environments also carry **governance state**:

- **Approval policy** — whether mutations require review (see
  [Governance](/Featly/concepts/governance/)).
- **ReadOnly lock** — a freeze that rejects all mutations during an incident,
  toggled from the dashboard or `featly env lock <key>` from the
  [CLI](/Featly/operate/cli/).

## API keys are environment-scoped

An API key is **scoped to a single environment** ([ADR-0009](https://github.com/Featly-net/Featly/blob/main/docs/adr/0009-api-keys-per-environment-scoped.md)).
A leaked `SdkRead` key for `staging` can read `staging` and nothing else — it
cannot read `production` and cannot mutate anything. Keys also carry a **scope**
(`SdkRead` for the SDK API, `AdminWrite` for the admin API) and can be **bound to
a user** so that actions attribute to a real person in the audit log and
approvals ([ADR-0023](https://github.com/Featly-net/Featly/blob/main/docs/adr/0023-user-bound-api-keys.md)).

```bash
featly apikey generate --name web-prod --scope SdkRead --environment production \
  --user ci@example.com
```

## Promoting changes between environments

Because each environment holds its own values, promoting a configuration is an
explicit act. The [`featly export` / `featly import`](/Featly/operate/cli/) commands
move flag/config/segment **definitions** between environments as a JSON bundle —
definitions only, never users, keys, or audit history.

## Next

- [Governance](/Featly/concepts/governance/) — RBAC, approvals, and the audit log.
- [Deployment](/Featly/operate/deployment/) — the three deployment patterns.
