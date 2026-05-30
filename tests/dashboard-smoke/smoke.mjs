// Headless smoke test for the embedded Featly dashboard.
//
// Boots a real browser against a running SelfHosted sample, authenticates with
// the admin key, then visits every routed list screen. A screen "passes" when
// it renders a non-empty <h1> inside #view and produces no uncaught page errors
// or console errors. The whole run exits non-zero on the first failing screen,
// so a render that throws (the class of regression that slipped through manual
// review during the redesign) breaks CI.
//
// Config via env:
//   FEATLY_BASE_URL   default http://localhost:5080
//   FEATLY_MOUNT      default /featly
//   FEATLY_ADMIN_KEY  default dev-admin-replace-me

import { chromium } from "playwright";

const BASE = process.env.FEATLY_BASE_URL || "http://localhost:5080";
const MOUNT = process.env.FEATLY_MOUNT || "/featly";
const ADMIN_KEY = process.env.FEATLY_ADMIN_KEY || "dev-admin-replace-me";

// List-level routes only (detail screens need a real entity id). Mirrors the
// `routes` table in src/Featly.Dashboard/wwwroot/app.js.
const ROUTES = [
  "/",
  "/flags",
  "/configs",
  "/segments",
  "/experiments",
  "/users",
  "/roles",
  "/groups",
  "/apikeys",
  "/projects",
  "/inbox",
  "/approvals",
  "/webhooks",
  "/audit",
  "/settings",
];

// Console-error noise that is not a dashboard regression (resource 404s such as
// a missing favicon). Anything else counts as a failure.
const IGNORED_CONSOLE = /favicon|Failed to load resource/i;

async function main() {
  const browser = await chromium.launch();
  const context = await browser.newContext({ baseURL: BASE });

  // Mint the session cookie. context.request shares the cookie jar with page
  // navigations, so the SPA boots already authenticated.
  const login = await context.request.post("/api/auth/login", {
    data: { apiKey: ADMIN_KEY },
    headers: { "Content-Type": "application/json" },
  });
  if (!login.ok()) {
    console.error(`Login failed (${login.status()}). Is the sample running at ${BASE} with admin key '${ADMIN_KEY}'?`);
    await browser.close();
    process.exit(1);
  }

  const page = await context.newPage();
  const failures = [];

  for (const route of ROUTES) {
    const url = MOUNT + route;
    const consoleErrors = [];
    const pageErrors = [];
    const onConsole = (msg) => {
      if (msg.type() === "error" && !IGNORED_CONSOLE.test(msg.text())) {
        consoleErrors.push(msg.text());
      }
    };
    const onPageError = (err) => pageErrors.push(String(err && err.message ? err.message : err));
    page.on("console", onConsole);
    page.on("pageerror", onPageError);

    const issues = [];
    try {
      await page.goto(url, { waitUntil: "domcontentloaded", timeout: 20000 });
      await page.waitForSelector("#view h1", { timeout: 12000 });
      const h1 = (await page.textContent("#view h1")) || "";
      if (!h1.trim()) {
        issues.push("rendered an empty heading");
      }
      // The dashboard catches a render/boot/fetch failure and paints a
      // <div class="error"> fallback (e.g. "Boot error") instead of throwing,
      // so detecting that element is how we catch a screen that broke.
      const errorEl = await page.$("#view .error");
      if (errorEl) {
        const detail = ((await errorEl.textContent()) || "").trim();
        issues.push(`error screen: "${h1.trim()}" — ${detail.slice(0, 120)}`);
      }
    } catch (e) {
      issues.push(`did not render (${e.message.split("\n")[0]})`);
    }

    page.off("console", onConsole);
    page.off("pageerror", onPageError);
    if (pageErrors.length) {
      issues.push(`page errors: ${pageErrors.join(" | ")}`);
    }
    if (consoleErrors.length) {
      issues.push(`console errors: ${consoleErrors.join(" | ")}`);
    }

    if (issues.length) {
      failures.push(`${route} -> ${issues.join("; ")}`);
      console.log(`FAIL ${route}`);
    } else {
      console.log(`ok   ${route}`);
    }
  }

  await browser.close();

  if (failures.length) {
    console.error(`\nDashboard smoke FAILED (${failures.length}/${ROUTES.length} screens):`);
    for (const f of failures) {
      console.error("  - " + f);
    }
    process.exit(1);
  }

  console.log(`\nDashboard smoke passed: ${ROUTES.length} screens rendered with no page or console errors.`);
}

main().catch((e) => {
  console.error("Smoke runner crashed:", e);
  process.exit(1);
});
