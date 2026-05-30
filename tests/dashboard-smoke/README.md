# Dashboard smoke

A headless [Playwright](https://playwright.dev) check that logs into the embedded
Featly dashboard and visits every routed list screen, failing on a screen that
does not render or that emits an uncaught page error / console error.

It exists because the visual regressions found during the dashboard redesign
(overlays not closing, clipped tables, overflowing rows, a screen that threw)
all slipped through manual review. This catches the "a screen throws" class in CI.

## Run locally

Start the sample, then run the smoke against it:

```bash
# terminal 1 — the server under test
dotnet run --project samples/SelfHosted.Sample
# (serves the dashboard at http://localhost:5080/featly)

# terminal 2 — the smoke
cd tests/dashboard-smoke
npm install
npx playwright install chromium
npm run smoke
```

## Configuration

| Env var            | Default                    | Purpose                          |
| ------------------ | -------------------------- | -------------------------------- |
| `FEATLY_BASE_URL`  | `http://localhost:5080`    | Base URL of the running sample   |
| `FEATLY_MOUNT`     | `/featly`                  | Dashboard mount path             |
| `FEATLY_ADMIN_KEY` | `dev-admin-replace-me`     | Admin key used to mint a session |

## CI

`.github/workflows/dashboard-smoke.yml` boots the sample and runs this on every
PR. It is intentionally a **separate, non-blocking** workflow (not a required
status check) so a browser/runner hiccup never blocks a merge while the check
proves itself.
