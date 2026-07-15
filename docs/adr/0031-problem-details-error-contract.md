# ADR-0031: RFC 7807 ProblemDetails as the API error contract

- **Status:** Accepted
- **Date:** 2026-07-15
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

The HTTP API returned errors in four inconsistent shapes: `NotFound(new { error })`
(85 sites), ad-hoc `BadRequest(new { error })` (43), `Conflict(new { error })` (24),
and `Results.Problem(...)` (29, already RFC 7807). Consumers — the dashboard, the
CLI, and integrators — had to special-case the envelope, and validation failures
were scattered `string.IsNullOrWhiteSpace` checks with no uniform message format
(issues #226, #230). The API is pre-1.0, so a coordinated breaking change to the
error shape is acceptable now and cheap compared to doing it later.

## Decision

Every error response is RFC 7807 `application/problem+json`. A small
`Problems` helper (`Featly.Server.Endpoints`) produces the standard document:
`Problems.NotFound/BadRequest/Conflict/Forbidden/UnprocessableEntity(detail)`
wrap `Results.Problem(...)`, and `Problems.Validation(field, message)` /
`Problems.Validation(errors)` wrap `Results.ValidationProblem(...)` so field-level
failures return a `ValidationProblemDetails` with an `errors` map. The old
`{ error }` anonymous objects are gone. Consumers read `detail` (or the `errors`
map) — the dashboard's `api()` helper and the CLI's error reader parse the problem
document, and both still tolerate a raw-text body as a fallback.

## Alternatives considered

### Alternative 1 — keep `{ error }`, just make it consistent

Standardize on a single bespoke `{ error }` envelope instead of RFC 7807.
Rejected: ProblemDetails is the platform standard (ASP.NET Core produces it
natively via `Results.Problem` / `Results.ValidationProblem`), is already used in
29 sites, and interops with existing tooling. Inventing a bespoke envelope would
be strictly worse.

### Alternative 2 — full DataAnnotations validation framework (#230)

Annotate every request DTO and add a model-validation filter. Rejected as
disproportionate: it adds attributes and a filter across every endpoint for
marginal gain over a small `Problems.Validation` helper. The lightweight helper
already gives uniform, RFC 7807-shaped validation messages; a fuller framework can
layer on later if request validation grows.

## Consequences

### Positive

- One error shape across the whole API; clients read `detail` / `errors`.
- Validation failures carry a standard per-field `errors` map.
- Uses ASP.NET Core's native problem responses — no bespoke serialization.

### Negative

- **Breaking change** to the error JSON. Acceptable pre-1.0; the dashboard and CLI
  were updated in the same change.

### Neutral

- A `Problems` helper and the pre-existing `Results.Problem(...)` calls coexist;
  both emit identical `application/problem+json`, so the wire contract is uniform
  even though two call styles remain in the source.

## Implementation notes

- `Problems` helper; ~152 `{ error }` sites converted to `Problems.*`, 22
  field checks to `Problems.Validation`. Consumers: `app.js` `problemMessage()`,
  CLI `AdminApiClient` error reader (now also reads the `errors` map).
- Tests: `Errors_use_rfc7807_problem_json_shape`,
  `Field_validation_uses_rfc7807_errors_map`.

## References

- [RFC 7807 — Problem Details for HTTP APIs](https://www.rfc-editor.org/rfc/rfc7807)
- Issues #226, #230; ARCHITECTURE.md §8
