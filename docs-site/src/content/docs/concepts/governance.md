---
title: Governance
description: Custom RBAC, approval workflows, the audit log, environment locks, and dry-run — the enterprise controls that set Featly apart.
---

Governance is Featly's differentiator in the open-source .NET space. Flags are
powerful and dangerous; Featly is built so teams can delegate flag access without
losing control.

## Custom RBAC

Featly ships **four immutable system roles** and lets you define your own.

| Role | Can… |
|---|---|
| **Viewer** | read flags, configs, segments, audit |
| **Editor** | the above + create/edit flags, configs, segments |
| **Approver** | the above + approve/reject pending changes |
| **Admin** | everything, including RBAC, settings, environments, keys |

The system roles are immutable ([ADR-0012](https://github.com/Featly-net/Featly/blob/main/docs/adr/0012-system-roles-immutable.md));
custom roles compose from ~45 **granular permissions** named `<Entity><Action>`
(`FlagCreate`, `ChangeApprove`, `EnvironmentLock`, …). Permissions are
**cumulative — there is no deny rule** ([ADR-0014](https://github.com/Featly-net/Featly/blob/main/docs/adr/0014-cumulative-permissions-no-deny.md)):
your effective permissions are the union of all your role assignments, which
keeps "why can this person do that?" answerable by inspection.

Role assignments are **polymorphic** — a role can be granted to a user, and the
model is designed to extend to groups ([ADR-0013](https://github.com/Featly-net/Featly/blob/main/docs/adr/0013-polymorphic-role-assignment.md)).

**Auto-provision mode** decides what an authenticated user with no assignment
gets: `Open` grants the Viewer floor, `Closed` denies until granted explicitly.
Lock this down in production (see [Configuration](/Featly/operate/configuration/)).

## Approval workflows

A protected environment can require **approval** before a mutation applies.
Instead of taking effect immediately, the change becomes a **pending change**
([ADR-0017](https://github.com/Featly-net/Featly/blob/main/docs/adr/0017-approval-pending-change-entity.md)):

1. An editor proposes a change (toggle a flag, edit a config).
2. The change is captured as a current → proposed **diff** and parked.
3. A required reviewer (a specific user, role, or group) **approves or rejects**
   it — with comments.
4. On approval the change applies; the whole exchange is in the audit log.

Policies are **per environment** with sensible **defaults** (the prod/non-prod
approval templates, themselves DB-overridable — see
[Configuration](/Featly/operate/configuration/)). Emergency **bypass** is supported
and always audited, so a break-glass action is possible but never invisible.

The [dashboard's](/Featly/dashboard/) **Inbox** surfaces what is waiting on you.

## Audit log

**Every** mutation, approval, and settings change is recorded with actor,
timestamp, and a before/after diff. The audit log is append-only and visible in
the dashboard; clicking an entry opens the diff. Settings changes write a
`setting.changed` entry, imports a `configuration.imported` entry, and so on.

## ReadOnly environment lock

A **lock** freezes an environment: all mutations are rejected until it is
unlocked. Use it to freeze `production` during an incident. Toggle it in the UI
or with `featly env lock production` / `featly env unlock production` from the
[CLI](/Featly/operate/cli/).

## Dry-run

Any mutation endpoint accepts a **dry-run** flag: it computes and returns the
effect (the diff, the validation result) **without applying it**. Use it to
preview a change, or to validate an [import](/Featly/operate/cli/) before committing.

## See it in the dashboard

The [Dashboard tour](/Featly/dashboard/) shows the Inbox, a change review with a
line-level diff, and the audit log's before/after view.
