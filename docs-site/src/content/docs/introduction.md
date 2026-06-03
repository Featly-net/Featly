---
title: Introduction
description: The core concepts behind Featly — flags, configuration, segments, experiments, environments, and governance — in five minutes.
---

Featly is an open-source **feature-management platform for .NET**. It combines
feature flags, dynamic configuration, segments, experiments, custom RBAC, and
approval workflows into a single product that can be **embedded inside your
ASP.NET Core process** (like Hangfire) or **hosted centrally** for many
consumers.

This page is the five-minute mental model. Follow [Getting started](/Featly/getting-started/)
to run it, or jump to the [Dashboard tour](/Featly/dashboard/) to see it.

## The architectural principles

Everything in Featly follows from a handful of decisions:

- **Local-first evaluation.** The SDK evaluates flags and configs locally
  against a cached, fresh-by-default snapshot. There is **no network call on the
  hot path**; a boolean flag resolves in tens of nanoseconds.
- **Embedded like Hangfire.** The server, dashboard, and SDK can all run inside
  the consumer's process. Two DI calls plus a middleware mount and you are
  operational.
- **The same engine everywhere.** `Featly.Engine` evaluates flags identically in
  the SDK and on the server — consistency by construction.
- **Resilient by default.** The SDK serves last-known-good config if the server
  is unreachable, and can bootstrap from a static JSON file.
- **Storage is an interface.** All persistence goes through `IFeatlyStore`.
  SQLite ships in the box; other providers are pluggable.
- **DB beats config.** Runtime-editable settings have three-layer precedence:
  hardcoded default → `appsettings.json` → database.

## The building blocks

### Flags

A **flag** is a named decision. Boolean flags answer yes/no; multivariate flags
return one of several typed variants (string, int, double, JSON). Flags carry
**targeting rules** that decide which variant a given context receives.

### Configuration

A **config** is a typed value (string, int, decimal, JSON, …) resolved through
the *same* targeting engine as flags. Use it for values that change without a
deploy — timeouts, limits, copy, feature parameters.

### Targeting and rules

A flag or config has an ordered list of **rules**. Each rule is a set of AND-ed
**conditions** (attribute, operator, value) that resolve to a variant or a
**weighted split**. The first matching rule wins; if none match, the default
variant is served. Splits bucket deterministically, so the same targeting key
always lands in the same bucket.

### Segments

A **segment** is a named, reusable audience definition (a set of conditions). A
rule can target a segment by reference, so "beta users" is defined once and used
across many flags.

### Experiments

An **experiment** is an A/B test built on flags: deterministic bucketing plus
exposure events, so you can measure the effect of a variant.

### Projects and environments

A **project** isolates one application's flags. An **environment**
(`development`, `staging`, `production`, …) is a deployment target with its own
values and its own governance. API keys are scoped to an environment.

### Governance

Featly is built for teams who need control:

- **Custom RBAC** — four immutable system roles (Viewer, Editor, Approver,
  Admin) plus user-defined roles, with ~45 granular permissions.
- **Approval workflows** — protected environments turn mutations into **pending
  changes** that require review before they apply.
- **Audit log** — every mutation, approval, and settings change is recorded with
  a before/after diff.
- **ReadOnly environment lock** — freeze an environment during an incident.
- **Dry-run** — preview the effect of any mutation without applying it.

## How the pieces fit

```text
   Your ASP.NET Core app
   ┌─────────────────────────────────────────────┐
   │  Featly.Server   ── admin + SDK HTTP APIs     │
   │  Featly.Dashboard ── embedded UI at /featly   │
   │  Featly.Engine   ── evaluation (shared)       │
   │  IFeatlyStore    ── SQLite / your database    │
   └─────────────────────────────────────────────┘
            ▲                         ▲
            │ HTTP (sync snapshot)    │ local evaluation
            │                         │
   ┌────────┴────────┐       ┌────────┴────────────┐
   │ Featly.Sdk      │       │ same process (opt.) │
   │ (other services)│       │                     │
   └─────────────────┘       └─────────────────────┘
```

Read on:

- [Flags and configuration](/Featly/concepts/flags-and-configs/)
- [Targeting and rules](/Featly/concepts/targeting/)
- [Segments and experiments](/Featly/concepts/segments-and-experiments/)
- [Projects and environments](/Featly/concepts/projects-and-environments/)
- [Governance](/Featly/concepts/governance/)
