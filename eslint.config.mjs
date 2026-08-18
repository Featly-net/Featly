// ESLint for the embedded dashboard (issue #229, PR 1).
//
// Two things live here on purpose:
//   1. The dashboard itself: src/Featly.Dashboard/wwwroot/app.js. A single
//      no-build ES5 file executed in the browser (see the banner comment at the
//      top of that file), so it lints as a script with browser globals.
//   2. This directory's own smoke.mjs (Node ESM).
//
// The one rule that matters for the XSS surface the issue calls out is
// no-unsanitized: every innerHTML / outerHTML / insertAdjacentHTML /
// document.write sink must be fed either a literal or a value routed through
// an escaping helper. In app.js that helper is esc() (and its code()/badge()
// wrappers), so those are whitelisted as sanitizers below; anything else
// reaching a sink is an error, not a warning.
//
// Lives at the repo root (ESLint 9 flat config only lints files under the
// config's own directory) but its dependencies are installed in
// tests/dashboard-smoke/node_modules -- the one package.json in the repo, and
// the directory whose CI workflow already sets up Node. Run it via
// `npm run lint` from tests/dashboard-smoke. This is deliberately NOT a build
// step for the dashboard: app.js is still served verbatim.

import js from "./tests/dashboard-smoke/node_modules/@eslint/js/src/index.js";
import globals from "./tests/dashboard-smoke/node_modules/globals/index.js";
import noUnsanitized from "./tests/dashboard-smoke/node_modules/eslint-plugin-no-unsanitized/index.js";

// Helpers in app.js whose *return value* is safe to inject as HTML because
// they escape their inputs. Keep this list short and honest -- adding a
// name here is a security-review decision, not a lint-silencing shortcut.
const dashboardSanitizers = [
    // Actual escapers: they take arbitrary text and return HTML-safe text.
    "esc", "code", "badge", "icon", "formatDate", "highlightJson", "prettyJson", "jsonPretty",
    "encodeURIComponent",
    // html(): the identity marker for a template assembled above the sink out
    // of literals + the escapers/builders below (the [ ... ].join("") pattern
    // the rule cannot follow on its own). Wrapping a sink's RHS in html() is
    // an explicit "I audited this" -- it must never wrap raw server data.
    "html",
    // Markup builders that only ever interpolate their inputs through esc()
    // internally (each one read and confirmed for issue #229 PR 1). They
    // return trusted HTML by construction; a NEW builder must be reviewed and
    // added here on purpose -- that is the point of the list being explicit.
    "field", "jsonField", "listPageShell", "listEmptyEnv", "detailLoadingShell",
    "flagListMarkup", "configListMarkup", "segmentListMarkup", "memberPickerMarkup",
    "archivedPill", "crStatusBadge", "apiKeyScopeBadge", "deliveryStatusBadge",
    "experimentStatus", "roleAssignmentsCard", "rolePermMatrix", "memberChip", "userDisplay",
    "approvalTemplateFields", "webhookEventPicker",
    "renderRulesEditor", "renderRuleCard", "renderConditionRow", "renderVariantRow",
    "renderPrerequisiteRow", "renderPrereqVariantChecks", "renderPreviewPanel",
    "renderPreviewAttrRow", "renderPreviewResult", "renderExperimentAnalytics",
    "renderDiff", "renderApprovalsEditor",
];

export default [
    {
        ignores: ["**/node_modules/**"],
    },

    // --- The dashboard: browser, ES5 script, IIFE ---------------------------
    {
        files: ["src/Featly.Dashboard/wwwroot/**/*.js"],
        ...js.configs.recommended,
        languageOptions: {
            ecmaVersion: 2020,
            sourceType: "script",
            globals: {
                ...globals.browser,
            },
        },
        plugins: {
            "no-unsanitized": noUnsanitized,
        },
        rules: {
            ...js.configs.recommended.rules,

            // The point of this config. Both rules cover the DOM XSS sinks;
            // "property" is innerHTML/outerHTML assignment, "method" is
            // insertAdjacentHTML / document.write / etc.
            "no-unsanitized/property": ["error", {}, {
                innerHTML: { objectMatches: [".*"], escape: { methods: dashboardSanitizers } },
                outerHTML: { objectMatches: [".*"], escape: { methods: dashboardSanitizers } },
            }],
            "no-unsanitized/method": ["error", {}, {
                insertAdjacentHTML: { properties: [1], escape: { methods: dashboardSanitizers } },
            }],

            // ES5 house style in app.js: `var` and function expressions are
            // the norm there, not something to lint away in this PR.
            "no-var": "off",
            "prefer-const": "off",
            "no-redeclare": ["error", { builtinGlobals: false }],
            // `_` is the file's convention for a deliberately-ignored argument
            // (catch (_) {...}, .then(function (_) {...})), and an empty catch
            // is how it expresses best-effort localStorage/JSON access.
            "no-unused-vars": ["error", { argsIgnorePattern: "^_$", caughtErrorsIgnorePattern: "^_$" }],
            "no-empty": ["error", { allowEmptyCatch: true }],
        },
    },

    // --- This directory's own scripts: Node ESM -----------------------------
    {
        files: ["tests/dashboard-smoke/*.mjs"],
        ...js.configs.recommended,
        languageOptions: {
            ecmaVersion: 2022,
            sourceType: "module",
            globals: {
                ...globals.node,
            },
        },
    },
];
