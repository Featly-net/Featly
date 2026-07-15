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

- **Webhook circuit breaker.** ~~Deferred.~~ **Shipped** in [ADR-0029](adr/0029-webhook-circuit-breaker.md)
  (issue #207): a first-class per-endpoint open/half-open breaker now sits on top
  of the backoff + dead-letter policy, short-circuiting a consistently-failing
  endpoint's due deliveries so it cannot clog the queue.

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

- **`env lock / unlock` via the CLI.** ~~The ReadOnly freeze is reachable from the
  API and dashboard. The CLI verb waits on `Featly.Cli`.~~ **Shipped in M12 (12C):
  `featly env lock|unlock <key>`.**

## Security follow-ups (from the v0.1.0 audit)

Recorded by [SECURITY_AUDIT.md](SECURITY_AUDIT.md); none block `v0.1.0`:

- **API-key rotation / expiry.** ~~Keys are minted and manually revoked; there is
  no built-in rotation or automatic expiry.~~ **Shipped post-preview.2:** keys
  accept an optional `expiresAt` (enforced by the auth pipeline), and
  `POST /api/admin/apikeys/{id}/rotate` / `featly apikey rotate` mint a
  replacement and revoke the old key.
- **Request rate limiting.** ~~No built-in throttling on the admin/SDK APIs;
  operators rely on a reverse proxy for now.~~ **Shipped post-preview.2:**
  opt-in fixed-window throttling per surface (auth/admin/SDK) and per client,
  bound from `Featly:RateLimiting` and DB-overridable via the settings API.
- **Synchronizer-token CSRF layer.** ~~Dashboard state-changing calls rely on the
  `SameSite=Strict` cookie; a per-request anti-forgery token is possible future
  hardening.~~ **Shipped post-preview.2:** login mints a per-session token
  (claim inside the HttpOnly cookie, echoed by login/`/me`); cookie-authenticated
  mutations must present it in `X-Featly-Csrf`; Bearer requests are exempt.
- **Dedicated backup/import permission.** ~~`export`/`import` are gated by
  `FlagRead` / `FlagCreate`; a dedicated `BackupImport` permission would tighten
  the gate for bundles carrying configs/segments.~~ **Shipped post-preview.2:**
  `BackupExport` / `BackupImport` permissions gate the two routes; only the
  system Admin role holds them by default.

## Post-1.0 (from PLAN.md "Post-1.0 extensions")

Designed in `ARCHITECTURE.md`, explicitly out of scope until after `v0.1.0`:

- `Featly.Storage.SqlServer` and `Featly.Storage.Postgres` providers
- `Featly.Storage.Redis` (cache + change pub/sub)
- ~~Statistical significance for experiments (Welch's t-test, chi-square,
  sequential analysis)~~ **Shipped post-preview.2:** a two-proportion z-test
  (the standard large-sample equivalent of the 2x2 chi-square test) against a
  baseline variant, with a per-metric winner. **Sequential analysis** (safe to
  peek at a running experiment without inflating the false-positive rate)
  remains deferred — the shipped test is fixed-horizon.
- Email (SMTP) notification channel
- Multi-tenant cloud-hosted mode (same binary, tenant flag)
- Browser-side edge SDK (JavaScript/TypeScript)
- Approval scheduling and time-windowed releases
- Flag prerequisites (one flag depends on another)
- Native Slack / Teams integrations (currently via generic webhook)
