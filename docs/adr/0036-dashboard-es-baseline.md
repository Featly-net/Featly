# ADR-0036: Dashboard JavaScript baseline is ES2020 syntax on evergreen browsers

- **Status:** Accepted
- **Date:** 2026-08-18
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

The embedded dashboard is a single ~4 000-line `app.js`, served verbatim with
no build step (a deliberate choice: the package ships static resources, and
"two DI calls plus a mount" must stay the whole story). Its banner comment
and house style say **ES5** — `var`, function expressions, string
concatenation — and issue #229 asks to modularise it and to bring it under
lint.

The lint half landed (ADR-free: ESLint 9 with a `no-unsanitized` XSS gate,
#313; CSS and nested-ternary sweeps, #314/#315). What is left in SonarCloud
for `app.js` is ~180 findings, and reading them by rule shows they are all
one thing — *use the modern construct*:

| Rule | Ask | Count |
|---|---|---|
| S6582 | optional chaining `?.` (ES2020) | 41 |
| S7761 | `el.dataset.x` over `getAttribute("data-x")` | 48 |
| S7773 | `Number.isNaN` over `isNaN` (ES2015) | 22 |
| S7765 | `.includes()` over `.indexOf() !== -1` (ES2016) | 12 |
| S7781 | `replaceAll` over `replace` (ES2021) | 3 |
| S2004 | nesting deeper than 5 (structural — the modularisation itself) | 28 |

None of them can be addressed without first answering *what may this file
assume of the browser?* — and the modularisation the issue is actually about
needs the same answer, because splitting into modules means
`<script type="module">`, which is itself an ES2015+ contract.

Two facts settle it:

1. **The file is not ES5 today.** It already uses `Promise` (14×), `fetch`
   (5×), `String#startsWith`/`endsWith`/`padStart`, `Object.entries` (ES2017,
   9×), template literals (5×), and one `let`. No ES5-only browser has been
   able to run the dashboard for a long time. "Stay ES5" therefore preserves
   no compatibility; it only forbids the syntax the rest of the file already
   presumes.
2. **Nothing promises an old browser.** README, GETTING_STARTED, DEPLOYMENT,
   ARCHITECTURE and the docs site make no browser-support statement at all,
   and the dashboard is an *admin* surface for operators, not a
   consumer-facing page.

## Decision

The dashboard's JavaScript baseline is **ES2020 syntax, on evergreen
browsers** (current Chrome, Edge, Firefox, Safari — anything that gets
`?.`/`??`, `Object.entries`, `Array#includes`, `String#replaceAll`, and
`<script type="module">` for free). Concretely:

- `app.js` may use `const`/`let`, arrow functions, template literals,
  destructuring, default/rest/spread, optional chaining and nullish
  coalescing, `for…of`, `class` where it reads better, and the modern
  standard-library methods above. Sonar's modernisation findings are to be
  fixed, not suppressed.
- The **no-build-step constraint stands unchanged**: source is served as-is;
  no transpiler, bundler, or minifier enters the package. Modularisation
  (the rest of #229) is done with native ES modules —
  `<script type="module" src="…/app.js">` importing sibling files under
  `wwwroot/` — which the existing `script-src 'self'` CSP already permits.
- The ESLint config's `ecmaVersion` moves to `2022` (to cover `replaceAll`),
  and `no-var` / `prefer-const` flip from `off` to `error` once the file has
  been converted, so the baseline is enforced rather than aspirational.
- The banner comment in `app.js` is updated to say so; the "ES5" note in
  the ESLint config goes.

The conversion is done in **mechanical, reviewable slices** — one rule
family per PR (e.g. "all `indexOf` → `includes`", "all `var` → `const`/`let`"),
each validated the way #314/#315 were: `node --check`, `npm run lint`, the
Playwright smoke, and where behaviour could plausibly change, a rendered-DOM
diff against `main`. Modularisation follows once the syntax is modern,
because moving code and rewriting it in the same diff is unreviewable.

## Alternatives considered

### Alternative 1 — stay ES5 and suppress the ~180 findings

Rejected. It would suppress real signal (the findings *are* the modernisation
work), preserve a compatibility target the file already violates, and block
`<script type="module">` — i.e. it forecloses the modularisation #229 exists
for. The only thing "ES5" still buys is a house-style label.

### Alternative 2 — ES2020 with a build step (esbuild/Vite) so source can be freer than the served output

Rejected. It reverses a first-principles decision of the project (static
resources, no toolchain, principle 6 "predictable, not magical"), adds a
Node build to the .NET package's CI, and gains nothing on evergreen browsers,
where the source *is* a valid served artefact.

### Alternative 3 — pin a specific browserslist (e.g. "last 2 versions")

Rejected as over-specification. The dashboard has no analytics, no support
matrix, and no user base that would let a browserslist be maintained
honestly; "evergreen" is the truthful statement of intent, and ES2020 is a
concrete floor that every evergreen browser cleared years ago.

## Consequences

### Positive

- The Sonar JS backlog becomes fixable, rule family by rule family, with the
  same evidence-based validation the previous sweeps used.
- Modularisation of `app.js` has a defined shape (native ESM) and no
  toolchain question left open.
- The ESLint config stops carving out exceptions for a style the file
  doesn't actually follow.

### Negative

- Anyone still opening the dashboard in a non-evergreen browser (none is
  known) will get a syntax error instead of a partially working page — a
  clearer failure than today's, but a failure. If that ever surfaces it is a
  bug report, and the answer would be a documented support statement, not a
  reversal.
- The conversion is a series of large-diff PRs on a 4 000-line file; the
  slice-per-rule discipline and the DOM-diff validation are what keep them
  reviewable, and both are non-negotiable for these PRs.

### Neutral

- CSP is untouched: `script-src 'self'` covers module scripts. `index.html`
  gets `type="module"` on the one script tag when modularisation lands, and
  the mount-path substitution keeps working.

## References

- Issue #229 — the modularise + lint proposal this ADR unblocks
- PR #313 (ESLint + `no-unsanitized`), #314 (CSS duplicates), #315
  (nested ternaries) — the baseline-agnostic slices already merged
- ADR-0024 — modular feature areas (the DI-side modularity this complements)
- ARCHITECTURE.md §1 principle 6 — "predictable, not magical"
