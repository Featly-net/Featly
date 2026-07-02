---
title: Security
description: Featly's pre-release security review — API-key hashing, webhook signing, CSRF defenses, secret handling, and authz boundaries.
---

A pre-release review of Featly's security-sensitive surfaces. Status legend:
**OK** (meets intent), **NOTE** (acceptable, documented), **FOLLOW-UP** (tracked
for a later release).

## API key hashing — Argon2id — OK

`ApiKeyHasher` hashes every API key with **Argon2id**
(`Konscious.Security.Cryptography.Argon2`, RFC 9106):

- Parameters: 16-byte random salt, 32-byte output, **iterations = 3, memory =
  64 MiB, parallelism = 2** (~100 ms/hash) — a deliberate work factor against
  offline cracking.
- Storage keeps **only the hash** in the documented
  `argon2id$v=19$m=,t=,p=$salt$hash` format; the plaintext token is shown
  **once** at creation and never persisted or logged.
- Verification is **constant-time**, and an indexed 12-char `Prefix` narrows
  candidates so the ~100 ms Argon2 cost runs on at most a handful of rows per
  request, not the whole table.
- The static bootstrap keys are compared **constant-time** too (`FixedTimeEquals`),
  avoiding a timing oracle.

**NOTE:** key rotation and automatic expiry are not built in (revoke is manual).
Tracked as a follow-up.

## Webhook signing — HMAC-SHA256 — OK

Every delivery body is signed with the endpoint's per-endpoint secret using
**HMAC-SHA256**, sent as `X-Featly-Signature: sha256=<hex>` (plus
`X-Featly-Event` / `X-Featly-Delivery`). Receivers recompute and compare to
authenticate the sender. The secret is auto-generated if the operator doesn't
supply one.

**NOTE:** receivers are responsible for constant-time comparison and idempotency
([ADR-0018](https://github.com/Featly-net/Featly/blob/main/docs/adr/0018-webhooks-single-notification-channel.md)).

## CSRF / session defenses — OK

- The dashboard session cookie is **`HttpOnly`** (an XSS in the host can't read
  the token), **`SameSite=Strict`** (the primary CSRF defense), `IsEssential`,
  and **`Secure` under TLS** (`SecurePolicy=SameAsRequest` keeps local-dev HTTP
  working).
- The API is **Bearer-token** based; token auth is not ambiently sent by
  browsers, so the admin/SDK HTTP surface is not CSRF-exposed.
- Auth failures return **401/403 as JSON**, never a redirect to a login URL,
  keeping the API shape clean and avoiding open-redirect vectors.

A **synchronizer-token layer** sits on top of `SameSite=Strict`: login mints a
random per-session token stored as a claim inside the HttpOnly cookie and echoed
in the login/`/me` JSON; every cookie-authenticated mutation must present it in
the `X-Featly-Csrf` header (constant-time compare). Bearer requests are exempt —
a header credential is not ambiently attached by browsers.

## Secret handling — OK / NOTE

- Minted tokens and bootstrap tokens are returned **exactly once**; the API key
  list endpoint exposes **metadata only** (never the hash or token) — covered by
  a test asserting the hash/`token` never appear in the list response.
- **NOTE:** the sample `appsettings.json` files ship `dev-*-replace-me`
  placeholders. [Deployment](/Featly/operate/deployment/) requires real secrets via
  environment variables / a secret store and explicitly calls out replacing them.

## Authentication & authorization boundaries — OK

- Two Bearer schemes (`AdminWrite`, `SdkRead`) + a cookie scheme; the admin
  policy accepts admin-Bearer or cookie, the SDK policy accepts SDK-Bearer only.
  Per-route `RequirePermission(...)` enforces RBAC ([ADR-0014](https://github.com/Featly-net/Featly/blob/main/docs/adr/0014-cumulative-permissions-no-deny.md)).
- API keys are environment-scoped ([ADR-0009](https://github.com/Featly-net/Featly/blob/main/docs/adr/0009-api-keys-per-environment-scoped.md));
  a leaked SDK key reads one environment and cannot mutate.
- A user-bound key resolves to the bound user's identity, so its effective
  permissions are that user's role assignments — **never a blanket admin** (a
  dedicated test proves a bound key with no admin role is forbidden from admin
  writes).

## The bootstrap endpoint — OK

`POST /api/admin/bootstrap` is unauthenticated **by necessity** (it creates the
first credential) but **self-guards**: it succeeds only while the instance has
**zero users**, and returns `409` forever after. See [ADR-0020](https://github.com/Featly-net/Featly/blob/main/docs/adr/0020-bootstrap-admin-appsettings-db-override.md)
/ [ADR-0023](https://github.com/Featly-net/Featly/blob/main/docs/adr/0023-user-bound-api-keys.md).

## Export / import — NOTE

`export`/`import` move **definitions only** (flags, configs, segments) — never
users, keys, role assignments, webhooks, or audit. Export is gated by the
dedicated `BackupExport` permission and import by `BackupImport` (held only by
the system Admin role by default; grant them to a custom role for backup
tooling); import emits a `configuration.imported` audit event.

## Dependency posture

- `System.Security.Cryptography.Xml` is pinned to a non-vulnerable version to
  stay off the transitively-pulled vulnerable release (GHSA-37gx-xxp4-5rgx,
  GHSA-w3x6-4m5h-cxqf).
- CodeQL runs on every PR and on `main`; the only standing dismissals are
  intentional resilience boundaries (a broad `catch` in the event publisher, the
  webhook worker, and the CLI error boundary), each documented.

## Follow-ups (tracked)

API-key rotation/expiry, request rate limiting, a synchronizer-token CSRF layer,
and a dedicated backup/import permission. None block the first release.

:::note
To report a vulnerability, see the repository's
[SECURITY.md](https://github.com/Featly-net/Featly/blob/main/SECURITY.md).
:::
