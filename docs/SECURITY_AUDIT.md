# Security audit — v0.1.0

A pre-release review of Featly's security-sensitive surfaces. Status legend:
**OK** (meets intent), **NOTE** (acceptable, documented), **FOLLOW-UP** (tracked
for a later release in [DEFERRED.md](DEFERRED.md)).

> Scope: the four areas called out in PLAN.md M12 — Argon2id usage, HMAC signing,
> CSRF defenses, secret handling — plus authn/authz boundaries and the M12
> additions (user-bound keys, the bootstrap endpoint, export/import).

## 1. API key hashing — Argon2id — **OK**

`ApiKeyHasher` (`Featly.Server.Authentication`) hashes every API key with
**Argon2id** (`Konscious.Security.Cryptography.Argon2`, RFC 9106):

- Parameters: 16-byte random salt, 32-byte output, **iterations = 3, memory = 64 MiB, parallelism = 2** (~100 ms/hash) — a deliberate work factor against offline cracking.
- Storage keeps **only the hash** in the documented `argon2id$v=19$m=,t=,p=$salt$hash` format; the plaintext token is shown **once** at creation and never persisted or logged.
- Verification is **constant-time** (`Verify`), and an indexed 12-char `Prefix` narrows candidates so the ~100 ms Argon2 cost runs on at most a handful of rows per request, not the whole table.
- The static bootstrap keys (`appsettings`) are compared **constant-time** too (`FixedTimeEquals`), avoiding a timing oracle.

**NOTE:** key rotation and automatic expiry are not built in (revoke is manual). Tracked as a follow-up.

## 2. Webhook signing — HMAC-SHA256 — **OK**

`WebhookSignature` signs every delivery body with the endpoint's per-endpoint
secret using **HMAC-SHA256**, sent as `X-Featly-Signature: sha256=<hex>` (plus
`X-Featly-Event` / `X-Featly-Delivery`). Receivers recompute and compare to
authenticate the sender. The secret is auto-generated if the operator doesn't
supply one. Validated end-to-end in M10 against a Python receiver that recomputed
the HMAC (`sig_ok=True`).

**NOTE:** receivers are responsible for constant-time comparison and idempotency
(documented in [ADR-0018](adr/0018-webhooks-single-notification-channel.md)).

## 3. CSRF / session defenses — **OK**

- The dashboard session cookie is **`HttpOnly`** (an XSS in the host can't read the token), **`SameSite=Strict`** (a cross-site request can't ride along — the primary CSRF defense), `IsEssential`, and **`Secure` under TLS** (`SecurePolicy=SameAsRequest` keeps local-dev HTTP working).
- The API is **Bearer-token** based; token auth is not ambiently sent by browsers, so the admin/SDK HTTP surface is not CSRF-exposed.
- Auth failures return **401/403 as JSON**, never a redirect to a login URL, keeping the API shape clean and avoiding open-redirect vectors.

**NOTE:** state-changing dashboard calls rely on `SameSite=Strict` rather than a
per-request anti-forgery token. For the embedded/admin audience this is adequate;
a synchronizer-token layer is a possible future hardening (FOLLOW-UP).

## 4. Secret handling — **OK / NOTE**

- Minted tokens and bootstrap tokens are returned **exactly once**; the API key list endpoint exposes **metadata only** (never the hash or token) — covered by a test asserting the hash/`token` never appear in the list response.
- **NOTE:** the sample `appsettings.json` files ship `dev-*-replace-me` placeholders. [DEPLOYMENT.md](DEPLOYMENT.md) requires real secrets via environment variables / a secret store and explicitly calls out replacing them. The static `AdminApiKey`/`SdkApiKey` are bootstrap shortcuts; production should prefer user-bound minted keys.

## 5. Authentication & authorization boundaries — **OK**

- Two Bearer schemes (`AdminWrite`, `SdkRead`) + a cookie scheme; the admin policy accepts admin-Bearer or cookie, the SDK policy accepts SDK-Bearer only. Per-route `RequirePermission(...)` enforces RBAC ([ADR-0014](adr/0014-cumulative-permissions-no-deny.md)).
- API keys are environment-scoped ([ADR-0009](adr/0009-api-keys-per-environment-scoped.md)); a leaked SDK key reads one environment and cannot mutate.
- **M12:** a user-bound key resolves to the bound user's identity, so its effective permissions are that user's role assignments — **never a blanket admin** (a dedicated test proves a bound key with no admin role is forbidden from admin writes). The Bearer DB lookup runs only when a token is present and the static key didn't match, so the common paths pay no extra cost.

## 6. The bootstrap endpoint — **OK**

`POST /api/admin/bootstrap` is unauthenticated **by necessity** (it creates the
first credential) but **self-guards**: it succeeds only while the instance has
**zero users**, and returns `409` forever after. It creates one admin user, an
admin role assignment, and a bound key (returned once). Reviewed for escalation:
once any user exists — including a config-seeded bootstrap admin — the endpoint
is closed. See [ADR-0020](adr/0020-bootstrap-admin-appsettings-db-override.md) /
[ADR-0023](adr/0023-user-bound-api-keys.md).

## 7. Export / import — **NOTE**

`export`/`import` move **definitions only** (flags, configs, segments) — never
users, keys, role assignments, webhooks, or audit. Export is gated by the
dedicated `BackupExport` permission and import by `BackupImport`; import emits a
`configuration.imported` audit event.

**RESOLVED (was a follow-up):** the routes originally piggybacked on
`FlagRead`/`FlagCreate`, which let a flag-only role move entity kinds it could
not touch individually. Both routes now carry dedicated permissions held only by
the system Admin role by default; grant them to a custom role for backup tooling.

## Dependency posture

- `System.Security.Cryptography.Xml` is pinned to `10.0.8` to stay off the
  vulnerable `9.0.0` that EF Core Design pulls transitively (GHSA-37gx-xxp4-5rgx,
  GHSA-w3x6-4m5h-cxqf) — see `Directory.Packages.props`.
- CodeQL runs on every PR and on `main`; the only standing dismissals are
  intentional resilience boundaries (broad `catch` in the event publisher, the
  webhook worker, and the CLI error boundary), each documented.

## Follow-ups (tracked)

Recorded in [DEFERRED.md](DEFERRED.md): API-key rotation/expiry (**shipped**),
request rate limiting (**shipped** — opt-in `Featly:RateLimiting`, DB-overridable),
a synchronizer-token CSRF layer, and a dedicated backup/import permission
(**shipped**). None block v0.1.0.
