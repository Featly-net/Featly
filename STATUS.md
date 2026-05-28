# Current status

> Refreshed by maintainers whenever the active milestone changes.
> The full plan lives in [PLAN.md](PLAN.md). The design lives in [ARCHITECTURE.md](ARCHITECTURE.md).

## Active milestone

**M10 — Webhooks + audit polish** (in progress)

Outbound webhooks (persisted delivery queue with retry/backoff + HMAC-SHA256 signing) and a richer audit log, both fed by a shared internal domain-event publisher covering flag/config/segment/experiment mutations, M8 approval decisions, and M7 RBAC changes. Four sequenced PRs:

- [x] **PR 10A — domain + storage**: `WebhookEndpoint`, `WebhookDelivery` (+ status enum), `AuditEntry`, `FeatlyDomainEvent` (+ `FeatlyEventTypes` constants); `IWebhookStore` / `IWebhookDeliveryStore` / `IAuditStore` on the facade with InMemory + SQLite + migration `AddWebhooksAndAudit`. 3 new SQLite round-trips, 281 passing total.
- [ ] **PR 10B — event backbone + audit**: `IFeatlyEventPublisher` + audit recorder; wire mutation endpoints (flags/configs/segments/experiments + `ChangeApplicationService` + RBAC) to publish; `GET /api/admin/audit` with filters (`AuditRead`).
- [ ] **PR 10C — webhook engine**: `/api/admin/webhooks` CRUD; `WebhookDispatcher` enqueues to matching endpoints; background worker drains with exponential backoff → dead-letter, signing with HMAC-SHA256 (`X-Featly-Signature`); send-test-event endpoint.
- [ ] **PR 10D — dashboard**: Webhooks management (list/create/edit/delete + delivery status + redeliver) and Audit log screen with filters.

## Previous milestone

**M9 — Experiments / A-B testing** (complete; published as `v0.0.7-preview.1` on NuGet)

Layered experiments on flags, automatic exposure events, custom event tracking, and basic per-variant analytics. Four sequenced PRs:

- [x] **PR 9A — Experiment/Event/Assignment domain + storage**: 3 entities (`Experiment` layered on a flag by `FlagKey` with a metric-keys list, sticky toggle, and a started/stopped window + computed `IsActive`; append-only `Event` with Exposure/Custom type and a JsonElement properties bag; first-write-wins `Assignment`); `IExperimentStore` + `IEventStore` + `IAssignmentStore` on the facade with InMemory + SQLite + migration `AddExperiments`. Analytics aggregate over raw event rows on read. 6 new SQLite store round-trips, 256 passing total.
- [x] **PR 9B — server**: `/api/admin/experiments` CRUD + start/stop (per-route `Experiment*` permissions, flag-existence validation, lifecycle conflicts); `/api/sdk/events` batch ingest; `/api/admin/experiments/{key}/analytics` (pure `ExperimentAnalyticsAggregator`: first-exposure variant attribution + per-variant/per-metric conversion rates on read); active experiments in the SDK `ConfigSnapshot` + experiment most-recent-update folded into the snapshot ETag. 13 new tests, 269 passing total.
- [x] **PR 9C — SDK**: `IEventClient` (`IFeatlyClient.Events`) + `TrackAsync`; non-blocking `IEventSink` (bounded channel, drop-on-full) drained by a background `FeatlyEventFlushService` batching to `/api/sdk/events`; automatic first-exposure emission from the flag client (O(1) coverage check, allocation-free when no experiment); process-local sticky pinning that overrides the value after a weight change. No-op until `UseServer()`. 9 new tests, 278 passing total.
- [x] **PR 9D — dashboard**: Experiments nav screen (list), detail view with Start/Stop/Restart and a CSS-only analytics panel — exposures-by-variant bar chart + per-metric conversion-rate bars from `GET /api/admin/experiments/{key}/analytics`. Closes M9.

## Milestone before that

**M8 — Approval workflow** (complete; published as `v0.0.6-preview.1` on NuGet)

Four sequenced PRs:

- [x] **PR 8A — domain + storage + policy engine**: 5 entities (`PendingChange`, `ChangeApproval`, `ChangeComment`, `ApprovalPolicy`, `ApproverRule`) + enums; `IPendingChangeStore` + `IApprovalPolicyStore` with InMemory + SQLite + migration `AddApprovalWorkflow`; pure `ApprovalPolicyEvaluator` (MinApprovals + mandatory rules + self-approval, membership injected as a delegate). 16 new tests.
- [x] **PR 8B — change lifecycle endpoints + policy CRUD**: `/api/admin/changes` (propose/list/get/comment/approve/apply/bypass) with per-route permissions; `ChangeApplicationService` (deserializes proposed state → Flag/Config/Segment store) + `ApproverMatcher` (role/group membership for the evaluator); `/api/admin/approval-policies/{env}` GET/PUT/DELETE. 7 new tests.
- [x] **PR 8C — gate integration + stale + dryRun + emergency**: `ChangeGate` intercepts flag/config/segment POST/PUT — returns `202` + `PendingChange` when the env policy requires approval; `?dryRun` reports without mutating; `?emergency=true&reason=` applies immediately with an audit record; `ChangeStaleness` refuses stale applies (`409`) and marks sibling open changes stale. 6 new tests.
- [x] **PR 8D — dashboard Inbox + CR detail + policy editor**: unified Inbox (pending changes + role upgrade requests); change-detail with current/proposed JSON diff, comments, approvals list, and Approve/Reject/Apply/Bypass buttons; per-environment Approvals policy editor. Smoke-validated via the embedded dashboard.

## Milestone before that

**M7 — Custom RBAC + Projects** (complete; published as `v0.0.5-preview.1` on NuGet)

Four sequenced PRs:

- [x] **PR 7A — RoleAssignment domain + storage + real resolution**: `RoleAssignment` polymorphic entity (User/Group, project + optional-environment wildcard) + `AssigneeType` enum; `IRoleAssignmentStore` with InMemory + SQLite + migration `AddRoleAssignments`; `DefaultFeatlyPermissionChecker` rewritten to union matching-assignment roles. Legacy api-keys + bootstrap admin keep a hardcoded shortcut; unassigned users fall back per `AutoProvisionMode` (Open → viewer floor, Closed → deny). 15 new tests.
- [x] **PR 7B — UserGroup + group-based resolution**: `UserGroup` entity + inline membership + `IUserGroupStore` (InMemory + SQLite + migration `AddUserGroups`, membership as JSON array via `PrimitiveCollection`); checker expands a user into its groups and unions direct + group assignments. 6 new tests.
- [x] **PR 7C — Custom roles + admin endpoints**: four admin endpoint groups (`users`, `roles`, `groups`, `role-assignments`) with per-route permission enforcement; custom roles via `cloneFromSystemRole` (union of template + explicit perms); system roles immutable (409 on reserved-key create, 403 on update/delete). 11 new tests.
- [x] **PR 7D — RoleUpgradeRequest + Effective Access + dashboard**: `RoleUpgradeRequest` entity + store (InMemory + SQLite + migration `AddRoleUpgradeRequests`); file/list/approve/reject endpoints (approve mints the `RoleAssignment`); `GET /api/admin/users/{id}/effective-access` resolves the permission union + contributing roles; dashboard Users + Roles screens with an effective-access view. 12 new tests. 222 passing total.

## Milestone before that

**M6 — Authentication pipeline + basic RBAC** (complete; published as `v0.0.4-preview.1` on NuGet)

Four sequenced PRs:

- [x] **PR 6A — auth domain + contracts**: `Permission` enum (60 values), `User` + `Role` entities, `SystemRoles` templates (Viewer / Editor / Approver / Admin), `IFeatlyUserResolver` (in `Featly.AspNetCore`) / `IFeatlyPermissionChecker` / `ResolvedUser` contracts, `IUserStore` + `IRoleStore` with InMemory + SQLite implementations + migration. `Role.Permissions` persists as JSON array of enum names (stable across enum re-orderings).
- [x] **PR 6B — ApiKey + Argon2id + auth filters**: `ApiKey` entity (Argon2id hash + 12-char Prefix for indexed lookup + Scope), `IApiKeyStore` with InMemory + SQLite + migration, `ApiKeyHasher` in `Featly.Server.Authentication` (mints / hashes / verifies via Konscious), `IFeatlyDashboardAuthorizationFilter` + built-in `FeatlyBasicAuthFilter` + `FeatlyLoopbackAuthFilter`. Legacy `AdminApiKey` / `SdkApiKey` keep working untouched until v0.1.0.
- [x] **PR 6C — Permission enforcement + bootstrap admin**: every admin endpoint flows through a per-route `Permission` filter; `AuthBootstrapHostedService` seeds the four system roles on every boot and provisions a bootstrap admin user when `Featly:Authorization:BootstrapAdminIdentifier` is configured; `DefaultFeatlyPermissionChecker` maps bootstrap admin / legacy admin api-key to the `admin` role, everyone else gets `viewer` (full `RoleAssignment`-driven resolution lands in M7). Auto-provision writes a `User` row on first request. Legacy keys keep working — no breaking change.
- [x] **PR 6D — Dashboard cookie session**: new `/api/auth/login|logout|me` endpoints mint an `HttpOnly; SameSite=Strict` cookie session (7-day sliding expiration). The `Admin` policy accepts both Bearer (SDK / scripts) and the dashboard cookie — same endpoints, two surfaces, no breaking change. The dashboard `app.js` now probes `/me` on boot, shows a real sign-in screen, and uses `credentials: 'include'` everywhere. SDK keys are intentionally rejected for dashboard sessions even though they remain valid against `/api/sdk/*`.

## Milestone before that

**M5 — Embedded dashboard UI** (complete; published as `v0.0.3-preview.1` on NuGet)

### Goal (M4)

Dynamic configuration as a parallel entity, sharing the targeting engine with flags. Delivered as two sequenced PRs (4A domain+storage, 4B engine+server+SDK+sample).

### Done — M4 PR 4A (domain + storage)

- **`ConfigRule`** new in `Featly.Abstractions`: `Order`, optional `Name`, list of `Condition` (reused from flag rules), `Value` (`JsonElement` — direct typed value, no variant indirection), `Enabled`
- **`Config.Rules`** ordered list added
- **`IConfigStore`** contract in `Featly.Storage.Abstractions`, plus `IFeatlyStore.Configs` on the facade
- **`Featly.Storage.InMemory.InMemoryConfigStore`** wired into the facade
- **`Featly.Storage.Sqlite`**:
  - `ConfigConfiguration` + `Configs` table (unique `(EnvironmentId, Key)`)
  - `DefaultValue` and `ConfigRule.Value` stored as raw JSON text via the shared `ConditionValueParser`
  - `Rules` and nested `Conditions` persisted as owned JSON inside the row
  - Migration `AddConfigs`: new table only (no schema changes to existing entities)
  - `SqliteConfigStore` follows the per-operation `IDbContextFactory<FeatlyDbContext>` pattern
- **Tests** (99 passing total, +10 new in `SqliteConfigStoreTests`): upsert with rules, overwrite preserves id, list filters archived + scopes per-env, **theory covering all 6 representative `ConfigType` values** (String/Int/Long/Double/Bool/Json) round-tripping cleanly, most-recent-update tracking

### Done — M4 PR 4B (engine + server + SDK + sample)

- **`Evaluator.EvaluateConfig`** walks `Config.Rules` by `ConfigRule.Order` asc, AND inside a rule, first-match wins, returns the rule's typed `Value` with reason `TargetingMatch`. Falls back to `Config.DefaultValue` with reason `Default`. Reuses the same `ISegmentLookup` plumbing flags use, so `InSegment` conditions resolve locally
- **`ConfigSnapshot`** carries `Configs` alongside `Flags` and `Segments`. The SDK `/api/sdk/config` ETag now folds the most-recent timestamps of all three buckets
- **Admin endpoints** under `/api/admin/configs`: `GET /`, `GET /{key}`, `POST /`, `PUT /{key}`. Same admin auth policy; ReadOnly environments rejected with 403; every mutation emits a `ChangeNotification(EntityType: "Config")`
- **`IConfigClient`** in the SDK with `GetAsync<T>` / `EvaluateAsync<T>`, mirroring `IFlagClient`. Picks up the ambient `IFeatlyContextAccessor` context when callers don't pass one
- **`IFeatlyClient.Configs`** property exposes the config client; `FeatlyClient` takes both `IFlagClient` and `IConfigClient` via primary constructor
- **`FeatlySnapshotCache`** indexes configs by key (`ConfigsByKey` `ImmutableDictionary`) alongside flags and segments
- **`samples/WebApi.Sample`** demonstrates `checkout.timeout` via a new `/checkout/timeout` endpoint: returns the default (30s) when no context matches and a per-country value when a rule targets `user.country=BR`
- **Tests** (129 passing total, +30 since 4A): 10 engine tests for `EvaluateConfig` (null/archived/default/rule match/rule order/AND/disabled/segments/JSON payloads), 8 SDK `ConfigClientTests` (cache empty default, typed deserialization, ambient pickup, explicit override, NotFound, blank key), 8 server `AdminConfigsEndpointTests` (full CRUD + auth gating + 4xx semantics), 2 SDK config endpoint tests (configs in snapshot + ETag invalidation), 2 E2E config tests (admin-creates-config-SDK-observes round-trip + NotFound semantics)

### Goal (M2 — complete)

A boolean flag, evaluated locally by the SDK, served by the server, persisted in storage. Proves the architecture end-to-end.

### Done — M3 PR 3A (domain + storage)

- **New domain types** in `Featly.Abstractions`: `ConditionOperator` (enum, 16 members), `Condition`, `Split`, `RuleOutcome`, `Rule`, `Segment`, `IFeatlyContextAccessor` (interface; implementation lands in 3D)
- `Flag.Rules` ordered list added
- **`ISegmentStore`** contract in `Featly.Storage.Abstractions`, plus `IFeatlyStore.Segments` on the facade
- **`Featly.Storage.InMemory`**: `InMemorySegmentStore` wired into the facade
- **`Featly.Storage.Sqlite`**:
  - `SegmentConfiguration` + `Segments` table (unique `(EnvironmentId, Key)`, conditions as owned JSON)
  - `FlagConfiguration` extended with `Rules` owned JSON (rules → conditions, outcome → splits all in one document)
  - `ConditionValueParser` helper round-trips `JsonElement` through raw JSON text
  - Migration `AddRulesAndSegments` adds the `Rules` column to `Flags` and creates `Segments`
  - `SqliteSegmentStore` follows the per-operation context pattern
- **Tests** (37 passing total, +6 in this PR): segment round-trip, list ordering, upsert overwrite, idempotent delete, most-recent-update tracking, and a Flag-with-Rules round-trip exercising nested conditions and weighted splits

### Done — M3 PR 3B (engine + benchmarks)

- **`Featly.Engine.Evaluator` is now feature-complete** per ARCHITECTURE.md §5: walks rules by `Order` asc, AND inside a rule, first-match wins, single-variant outcome → `TargetingMatch`, weighted-split outcome → `Split` via MurmurHash3 bucketing
- All **16 condition operators** in `Internal/ConditionEvaluator` (Equals/NotEquals, In/NotIn, GreaterThan/GreaterThanOrEqual/LessThan/LessThanOrEqual, Contains/StartsWith/EndsWith, regex Matches with 50ms timeout against ReDoS, Semver Gt/Lt/Eq, InSegment with recursive segment-condition matching). `Negate` flag inverts the predicate
- **`Internal/MurmurHash3`**: 32-bit in-house implementation, `BucketOf10000` returns the 0..9999 bucket the engine uses
- **`Internal/Bucketer`**: composes `targetingKey + flagKey + salt`, hashes, walks the cumulative weights
- **`Internal/AttributeResolver`**: flat-key lookup into `EvaluationContext.Attributes` plus the `targetingKey` shortcut
- **`Internal/SemverComparer`**: in-house semver 2.0.0 (no external dependency)
- **`ISegmentLookup`** public contract + `DictionarySegmentLookup` default. M3C/3D populates the lookup from the SDK snapshot
- **40 new engine tests** (46 total): every operator positive + negative, `Negate`, first-match-wins, AND, disabled-rule skip, segment matched + missing-from-lookup, bucketing determinism, distribution within ±5% on 5 000 subjects (50/50 and 90/10)
- **`tests/Featly.Engine.Benchmarks`** new project. `docs/PERFORMANCE.md` carries the baseline. Every scenario is below the **10 μs p99 target**: boolean fast path 37 ns, 1 rule 134 ns, 5 rules × 3 conditions 1.2 μs, split bucketing 390 ns, InSegment lookup 286 ns

### Done — M3 PR 3C (server)

- **`ConfigSnapshot`** now carries `Segments` alongside `Flags`; the SDK config endpoint returns both. The ETag folds the most-recent `Flag.UpdatedAt` *and* the most-recent `Segment.UpdatedAt` so edits in either bucket invalidate cached snapshots
- **`FlagWriteRequest`** accepts an optional `Rules` array. `POST` and `PUT /api/admin/flags` persist targeting rules end-to-end (already round-tripped through SQLite by 3A)
- **`AdminSegmentsEndpoints`** new — full CRUD under `/api/admin/segments`:
  - `GET /` list, `GET /{key}`, `POST /`, `PUT /{key}`, `DELETE /{key}`
  - Auth: same admin policy as flags. Sdk-scoped keys get 401/403
  - ReadOnly environment rejected with 403
  - Every mutation emits a `ChangeNotification(EntityType: "Segment")` so SSE clients re-fetch
- **8 new server tests** (15 total in `Featly.Server.Tests`): 5 covering segment CRUD + auth gating, 1 PUT-flag-with-rules round-trip, 2 SDK snapshot showing segments and ETag invalidation on segment change

### Done — M3 PR 3D (SDK ambient context + AspNetCore + sample)

- **`FeatlySnapshotCache`** indexes `Segments` and exposes an `ISegmentLookup`; `FlagClient` hands it to the engine on every call, so `InSegment` resolves locally without a server round-trip
- **`FlagClient`** picks up the ambient context from `IFeatlyContextAccessor` when callers don't pass one. Explicit context always wins
- **`NoOpFeatlyContextAccessor`** is the SDK default; `builder.UseContextAccessor<TAccessor>()` swaps it
- **`Featly.AspNetCore.HttpContextFeatlyContextAccessor`** maps `HttpContext.User` claims (NameIdentifier/Sub/email/name) into an `EvaluationContext`. Wired via `builder.UseHttpContextAccessor()`
- **`samples/WebApi.Sample`** with targeting demo: `/checkout?country=BR&plan=pro` exercises rule matching, `?targetingKey=...` drives split bucketing; response includes matched rule, variant, and reason
- **4 new SDK tests** (`AmbientContextAccessorTests`): ambient pickup, explicit override, no-op fallthrough, segments resolved locally
- **89 tests pass** overall (was 85)

### M3 — "Done when" criteria from PLAN.md

- [x] Configure a flag like "100% for `user.country=BR`, 10% rollout for `user.plan=pro`, off for everyone else" via the API
- [x] SDK evaluates the rule locally for any subject and context
- [x] Performance: p99 < 10μs hit cleanly per `docs/PERFORMANCE.md`

## Coming next — M5 (in progress)

Dashboard UI in four sequenced PRs:

- [x] **PR 5A — skeleton**: asset pipeline + middleware serving real `index.html` / `app.css` / `app.js`, layout + navigation, light/dark tokens, explicit hover states.
- [x] **PR 5B — listings**: Flags / Configs / Segments tables backed by `/api/admin/*`, environment selector populated from `GET /api/admin/environments`, admin-token paste flow with `localStorage` + "Sign out" (pre-M6 bridge).
- [x] **PR 5C — detail + rule editor**: dynamic routing for `/flags/{key}` etc., editable detail screens (name, description, enabled, variants, tags, default value/variant), shared visual rule editor for Flag rules / Config rules / Segment conditions, save via PUT with feedback, refresh-on-focus for light live updates. Generalised the dashboard middleware to serve any `wwwroot/` asset by extension. Fixed a pre-existing bug in `SqliteFlagStore.UpsertAsync` where `Rules` were being dropped on update. SSE live updates land alongside an admin stream endpoint in a follow-up.
- [x] **PR 5D — "test this context" preview**: new `POST /api/admin/preview/{flags|configs}/{key}` server-side dry-run endpoint that runs `Evaluator.EvaluateFlag` / `EvaluateConfig` against a candidate context (no persistence). Flag and config detail screens grow a "Test this context" panel with targeting-key + attribute inputs and a result card showing the reason badge, value, matched rule.

**M5 complete.** Coming next per PLAN.md: **M6 — Authentication pipeline + basic RBAC** (replaces the dashboard's localStorage token paste flow with real API keys + system roles).

### Done — M2 (complete)

- **Domain entities** in `Featly.Abstractions`: `Project`, `Environment`, `Flag` (with `Variants`), `ConfigSnapshot`
- **Storage contracts** in `Featly.Storage.Abstractions`: `IFeatlyStore` facade + `IFlagStore`, `IProjectStore`, `IEnvironmentStore`, `IChangeNotifier`
- **`Featly.Storage.InMemory`**: thread-safe sub-stores backed by `ConcurrentDictionary`; in-process `IChangeNotifier`
- **`Featly.Engine.Evaluator`**: boolean-minimum flag evaluation (kill switch, archived short-circuit, default-variant resolution); rules in M3
- **`Featly.Server`**:
  - `FeatlyServerOptions` bound from `Featly:Server` configuration
  - Static-token bearer auth with `FeatlyAdmin` + `FeatlySdk` schemes (real Argon2 API keys land in M6)
  - `DefaultProjectBootstrapHostedService` auto-creates a default Project + Environment on first boot
  - Admin API: `GET|POST|PUT /api/admin/flags` with optional `?env=` query parameter
  - SDK API: `GET /api/sdk/config` with ETag/`If-None-Match`, `GET /api/sdk/stream` with SSE notifications
- **`Featly.Sdk`**:
  - `IFeatlyClient` + `IFlagClient` real implementations
  - `FeatlySnapshotCache` (thread-safe `ImmutableDictionary` lookup)
  - `FeatlyConfigSyncService` BackgroundService: initial fetch, long-lived SSE connection, polling fallback with ETag
  - `services.AddFeatly().UseServer(url, apiKey).UseEnvironment(...)` fluent DI surface
- **Samples**: `SelfHosted.Sample` boots server + dashboard + InMemory; `WebApi.Sample` consumes via the SDK
- **`Featly.Storage.Sqlite`**: `FeatlyDbContext` (internal) with EF Core configurations; `Project`, `Environment`, `Flag` tables; `Variants` and `Tags` as JSON columns; initial migration `InitialCreate`; pooled `IDbContextFactory<FeatlyDbContext>`; SQLite-backed sub-stores; `SqliteAutoMigrationHostedService` applies pending migrations at boot when `AutoMigrate=true`; `services.AddFeatlySqliteStore(opts => ...)` DI extension
- **`samples/SelfHosted.Sample`** now uses `Featly.Storage.Sqlite` by default (`Data Source=featly.db`), giving the Hangfire-style quickstart real persistence
- **Tests** (31 passing): Engine, SDK FlagClient, Server admin/SDK endpoints with auth, E2E boolean flag round-trip via TestServer, SQLite store round-trips (Flag with variants and tags, Project unique key, Environment scoped uniqueness, ChangeNotifier pub/sub, migrations apply)

### Done-when criteria (PLAN.md M2)

- [x] End-to-end test passes consistently (Featly.E2E.Tests.BooleanFlagEndToEndTests)
- [x] A developer can create a boolean flag via HTTP and the sample app's `IsEnabledAsync` reflects it within a polling interval
- [x] Persisted in SQLite (default for `samples/SelfHosted.Sample`; covered by `Featly.Storage.Sqlite.Tests`)

### Not in scope for M2 (deferred to later milestones)

- Targeting rules, conditions, segments, bucketing — M3
- Dynamic configs (`IConfigClient`) — M4
- Dashboard UI for flags — M5
- Real auth pipeline + RBAC + Project entity scoping — M6, M7
- Approval workflows — M8
- Experiments — M9
- Webhooks — M10
- OpenFeature provider implementation — M11
- CLI commands and first release — M12

## Open follow-ups

- **Reserve the `Featly` and `Featly.*` package names on NuGet.org** — before M3 we should publish minimal `0.0.1-preview.1` placeholders for at least `Featly`, `Featly.Sdk`, and `Featly.Abstractions`, and explore Verified Publisher for the `Featly.*` prefix. Avoids squatting while the rest of the milestones land.
- `CODE_OF_CONDUCT.md` referenced by `CONTRIBUTING.md` but not yet added
- Apply ADR-021 at v0.1.0: migrate test projects from `FluentAssertions 7.2.0` to `AwesomeAssertions` (mechanical rename of `using` directives + `Directory.Packages.props` bump)
