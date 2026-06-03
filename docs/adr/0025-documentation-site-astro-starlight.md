# ADR-0025: Documentation site on Astro Starlight, hosted on GitHub Pages

- **Status:** Accepted
- **Date:** 2026-06-03
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

The in-repo Markdown (`README.md`, `docs/*.md`, `docs/adr/*`) covers the
project well for contributors, but issue #92 asks for a dedicated,
**image-rich documentation site** that shows the product in action — install,
concepts, a dashboard tour, configuration, deployment, OpenFeature,
performance, and security — so that prospective users can evaluate Featly
without cloning the repo.

The forces at play:

- **Reuse, don't fork.** We already maintain accurate Markdown. The site must
  consume that content (or content very close to it) so the two do not drift.
  Authoring docs twice is the failure mode to avoid.
- **Image-first.** The acceptance criteria emphasise screenshots of every major
  screen. The generator must make images, captions, and visual callouts easy.
- **Low ceremony hosting.** Featly is an open-source GitHub project. Publishing
  should be a push-to-`main` GitHub Action, not a third-party service to
  administer.
- **Predictable, not magical** (ARCHITECTURE.md §1). The toolchain should be a
  well-trodden path with a small config surface, not a bespoke pipeline.
- **Isolation from the product build.** The site is documentation, not a
  shipped artifact. Its toolchain (Node) must not leak into the .NET solution,
  CI for the libraries, or what consumers restore.

## Decision

We will build the documentation site with **Astro + Starlight**, sources under
`docs-site/`, published to **GitHub Pages** by a dedicated GitHub Actions
workflow on push to `main`. Content is authored as Markdown/MDX in
`docs-site/src/content/docs/`; screenshots live in `docs-site/src/assets/` and
are referenced with relative Markdown image syntax so Astro fingerprints and
optimises them. The site is a self-contained Node project: it has its own
`package.json` and is **excluded from the .NET solution** and from the
library/test/pack CI jobs. The README keeps its existing inline screenshots and
links out to the published site.

## Alternatives considered

### Alternative 1 — MkDocs Material

Mature, hugely popular for technical docs, excellent image/admonition support,
minimal YAML config. Rejected primarily because it introduces a **Python**
toolchain into an otherwise .NET + Node (dashboard tooling) repository, adding a
third language runtime to set up in CI and locally. Starlight reaches the same
visual quality while keeping us on Node, which the project already uses for the
Playwright dashboard-smoke tests.

### Alternative 2 — DocFX

Native .NET (`dotnet tool`), no Node/Python, and can generate API reference from
XML doc comments. Rejected because #92 is about a **conceptual, image-rich
product tour**, not API reference; DocFX's strength (API docs) is not the goal,
and its conceptual-site output is less modern and less image-oriented than
Starlight. The public API is also still unstable (pre-release), so committing to
generated API reference now would be premature.

### Alternative 3 — VitePress

Lightweight, fast, Markdown-first. A close runner-up. Starlight was chosen for
its batteries-included docs features (built-in pagefind search, sidebar
autogeneration, light/dark theming, social-card and SEO defaults) that VitePress
leaves to assembly, lowering the maintenance surface for an image-heavy docs
site.

## Consequences

### Positive

- Existing Markdown ports with minimal changes (frontmatter + relative-link
  fixes); the site and the repo docs share prose, reducing drift.
- Built-in full-text search, responsive nav, and dark mode with near-zero
  config — strong fit for an image-rich tour.
- Publishing is `git push` + a GitHub Action; no external service to operate.
- The Node toolchain is fully isolated under `docs-site/`; the .NET solution,
  the package CI, and consumers are untouched.

### Negative

- Adds a Node build to the repository's surface (a `docs-site/package.json` and
  a deploy workflow) that maintainers must keep current (Astro/Starlight move
  fast).
- Screenshots must be re-captured when the UI changes — the standing cost #92
  itself calls out. This is mitigated by the existing Playwright smoke harness,
  which can re-capture the canonical screens.
- A second place where prose can live (site vs `docs/`). We accept this by
  treating the site as the published surface and keeping `docs/` as the
  contributor-facing source; long-term we may converge by sourcing the site
  directly from `docs/`.

### Neutral

- The site deploys to the GitHub Pages project URL
  (`https://featly-net.github.io/Featly/`), so Astro is configured with
  `site` + `base` accordingly. Switching to a custom domain later is a
  config-only change.

## Implementation notes

- Site root: `docs-site/` — `package.json`, `astro.config.mjs`, `tsconfig.json`,
  `src/content/docs/*`, `src/assets/*`.
- Deploy: `.github/workflows/docs-site.yml` builds with the official
  `withastro/action` and publishes via `actions/deploy-pages`. Triggers on push
  to `main` affecting `docs-site/**` (plus manual `workflow_dispatch`).
- The site is intentionally **not** added to `Featly.sln`.

## References

- Issue #92 — Docs: overhaul GitHub docs + build an image-rich documentation site.
- ARCHITECTURE.md §1 (Predictable, not magical), §22 (ADR index).
- ADR-0002 — embedded dashboard without Blazor (vanilla-JS dashboard; the docs
  site is a separate, build-time-only Node project and does not change that).
- Astro Starlight — https://starlight.astro.build
