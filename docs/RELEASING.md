# Releasing Featly

How a version gets from `main` to nuget.org, and what to check before you
tag. The mechanics already live in `.github/workflows/release.yml`; this
page is the human-side checklist around them, written down so it doesn't
depend on remembering it.

## How versioning works

Package versions are **computed, not typed**. MinVer derives them from git
tags (`Directory.Build.props`, MinVer section):

- The latest reachable tag matching `v*` is the version. Commits *after* it
  get a pre-release suffix from the configured default identifier
  (`preview.0`) plus the commit height — e.g. 12 commits past
  `v0.1.0-preview.2` pack as `0.1.0-preview.3-preview.0.12`-style versions on
  the internal feed. Nothing sets a version number in a `.csproj`.
- Therefore **creating and pushing a tag is the release.** There is no
  version bump commit.

## Two publish paths, one workflow

`release.yml` runs the same `pack` job for both and gates the publish step on
the trigger, so a package is never pushed twice:

| Trigger | Publishes to | Who consumes it |
|---|---|---|
| every push to `main` | GitHub Packages (internal / preview feed) | maintainers testing the tip |
| a `v*` tag | **nuget.org** (public) | everyone |
| `workflow_dispatch` | your choice of the two | re-runs / recovery |

`--skip-duplicate` is set on both pushes, so re-running after a partial
failure is safe.

## Cutting a release

### 0. Decide the number

Pre-1.0, minor bumps are allowed to break the public API (see the
CHANGELOG preamble). Rough guide, by what the `[Unreleased]` section holds:

- **patch preview** (`0.1.0-preview.3`): fixes, docs, internal refactors.
- **minor preview** (`0.2.0-preview.1`): a new package, a new storage
  provider, a new top-level feature (approvals, webhooks, experiments,
  a scheduled-apply worker, ...), or anything with an ADR.
- **stable** (`0.1.0`): only on an explicit go-ahead; nothing in this
  document promotes a preview to stable on its own.

### 1. Pre-release checklist

Everything here is verifiable; do it on a clean `main` you have just pulled.

- [ ] **`main` is green.** `gh run list --branch main --limit 6` — CI,
      CodeQL, Release (the GitHub Packages leg), sonarcloud, dashboard-smoke
      all `success` on the tip commit.
- [ ] **No open Dependabot security alerts.** `gh api repos/Featly-net/Featly/dependabot/alerts --jq '[.[]|select(.state=="open")]|length'` → `0`.
- [ ] **CHANGELOG.md**: rename `## [Unreleased]` to `## [vX.Y.Z-preview.N] - YYYY-MM-DD`,
      add a fresh empty `## [Unreleased]` above it. Consolidate PR-sliced
      entries into what a *user* reads — "`Featly.Storage.MongoDB` (PR 1 of
      N — scaffold)" through "(PR 8 — CLI)" become one "MongoDB storage
      provider" bullet with the sub-points that matter (replica-set
      requirement, Change Streams push, no `rollback`), and the per-PR
      detail stays in git history and the ADR.
- [ ] **STATUS.md**: the "Active milestone" section names the version being
      cut and the test count is current (`grep -rhoE "\[(Fact|Theory)\]" tests/*/*.cs | wc -l`).
- [ ] **ARCHITECTURE.md §7 provider roadmap** and **docs/DEPLOYMENT.md**
      agree with what actually ships (every provider row says `Shipped`
      only if its `AddFeatly*Store()` exists).
- [ ] **docs/nuget-readme.md** still describes the packages accurately —
      this is what every package shows on nuget.org (packed via
      `Directory.Build.props`), so a stale sentence there is the most public
      stale sentence in the repo. Verify any code sample against `samples/`,
      which are E2E-tested.
- [ ] **New ADRs are `Accepted`** and listed in ARCHITECTURE.md §22.
- [ ] **Local dry-run pack** matches what CI will produce:
      `dotnet pack Featly.sln -c Release -o artifacts` and eyeball
      `artifacts/*.nupkg` versions (they will carry the height suffix until
      the tag exists — that is expected).

Commit the CHANGELOG/STATUS/docs edits as one `docs(release): prepare
vX.Y.Z-preview.N` PR, merge it, pull.

### 2. Tag and push

```bash
git checkout main && git pull
git tag -a v0.2.0-preview.1 -m "v0.2.0-preview.1"
git push origin v0.2.0-preview.1
```

The tag push triggers `release.yml` on the tag ref → `pack` → `Publish to
nuget.org (tag)`. **That job does not run unattended.** It targets the
`nuget-org` GitHub environment, which is configured with a *required
reviewer* (currently @thiagoluga): after `pack` succeeds the publish job
sits at "Waiting for review" in the Actions UI until the reviewer clicks
**Approve and deploy** — that click is the actual moment packages leave for
nuget.org. This is the deliberate guard-rail for an irreversible action;
if an automated agent or a script does the tagging, the human still gets
the last word. Approve from the run's page (`gh run view <id> --web`).

### 3. Verify

- [ ] `gh run list --workflow release.yml --limit 2` shows the tag run
      `success` (it will show `waiting` until the environment reviewer approves).
- [ ] `curl -s https://api.nuget.org/v3-flatcontainer/featly.sdk/index.json`
      lists the new version (nuget.org indexing can lag a few minutes).
- [ ] `gh release create vX.Y.Z-preview.N --notes-from-tag` (or paste the
      CHANGELOG section) so the GitHub Releases page matches nuget.org.
- [ ] Bump the "Active milestone" in STATUS.md to whatever comes next.

## If something goes wrong

- **Publish failed after `pack` succeeded** — re-run the workflow with
  `workflow_dispatch` → target `nuget-org`; `--skip-duplicate` makes it
  idempotent for the packages that did land.
- **Wrong version tagged** — a tag that has been *pushed and published*
  cannot be unpublished from nuget.org (only unlisted). Cut a corrected
  higher version rather than moving the tag; delete a tag only if it never
  reached nuget.org.
- **`NUGET_API_KEY` missing/expired** — the publish job fails fast with an
  explicit `::error::`; rotate the secret at Settings › Secrets and
  variables › Actions and re-run.
