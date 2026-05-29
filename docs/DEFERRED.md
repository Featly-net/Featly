# Deferred work

Scope intentionally pushed out of the milestone where it was first listed, with
the rationale. Tracked here per [PLAN.md](../PLAN.md) ("Scope creep across
milestones is recorded as deferred work in `docs/DEFERRED.md`").

## From M10 (Webhooks + audit polish)

M10 shipped the core: the domain-event backbone, the audit log (store + API +
dashboard), and the webhook engine (CRUD + dispatcher + persisted retry queue
with HMAC-SHA256 signing and exponential-backoff → dead-letter). The
`feat/m10-polish` follow-up added the ReadOnly environment lock/unlock (API +
dashboard) and the full audit-log filter UI. The items below were listed in the
M10 plan but deliberately left out:

- **Webhook circuit breaker.** The plan paired "exponential backoff retry +
  circuit breaker". We shipped backoff + dead-letter, which already throttles a
  consistently-failing endpoint (each failure pushes `NextAttemptAt` further out
  and the row is abandoned after the attempt budget). A first-class circuit
  breaker (open/half-open per endpoint, short-circuiting the queue) is a
  refinement, not a correctness gap. **Revisit if** high-volume deployments show
  the queue clogging on a dead endpoint.

- **DB-overridable webhook settings (`WebhookSettings`).** Retry tuning binds
  from `appsettings` (`Featly:Webhooks`) today, not the database. Making it
  DB-overridable per the "DB beats config" principle requires a generic
  settings-store subsystem that does not exist yet — the same subsystem
  `ApprovalDefaultsSettings` (M8) and audit-retention settings would use.
  **Deferred as one unit:** build the settings store once, then migrate the
  webhook/approval/audit knobs onto it (good candidate for early M12 or a
  dedicated settings milestone). Operational knobs like retry cadence are
  defensibly bootstrap-ish in the meantime.

- **Dry-run on *every* mutation endpoint.** `?dryRun=true` exists on the
  approval-gated flag/config/segment writes (M8), where its semantics are
  well-defined ("would this require approval, and what's the diff?"). Extending
  it to experiments / webhooks / RBAC would be a different feature ("validate
  without persisting"), of debatable value for those entities. **Revisit** if a
  concrete need surfaces; the gated-entity dry-run already covers the headline
  use case.

- **Audit-retention policy.** No automatic pruning/rollup of `AuditEntry` rows
  yet — the log grows unbounded. Needs the settings subsystem above plus a
  background trimmer. **Deferred** to the settings milestone / M12.

- **`env lock / unlock` via the CLI.** The ReadOnly freeze is reachable from the
  API and dashboard. The CLI verb waits on `Featly.Cli`, which is an **M12**
  deliverable — there is no CLI project yet. **Naturally lands in M12.**

## Post-1.0 (from PLAN.md "Post-1.0 extensions")

Designed in `ARCHITECTURE.md`, explicitly out of scope until after `v0.1.0`:

- `Featly.Storage.SqlServer` and `Featly.Storage.Postgres` providers
- `Featly.Storage.Redis` (cache + change pub/sub)
- Statistical significance for experiments (Welch's t-test, chi-square,
  sequential analysis)
- Email (SMTP) notification channel
- Multi-tenant cloud-hosted mode (same binary, tenant flag)
- Browser-side edge SDK (JavaScript/TypeScript)
- Approval scheduling and time-windowed releases
- Flag prerequisites (one flag depends on another)
- Native Slack / Teams integrations (currently via generic webhook)
