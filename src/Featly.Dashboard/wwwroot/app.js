// Featly dashboard.
//
// M6 PR 6D replaces the localStorage token-paste with a real cookie session:
//   - POST /api/auth/login mints an HttpOnly cookie.
//   - GET  /api/auth/me detects an existing session on boot.
//   - POST /api/auth/logout clears the cookie.
//   - All admin/SDK requests use credentials: 'include' so the cookie rides
//     along automatically.
//
// Earlier milestones still apply:
//   - Dynamic routing for /flags/:key, /configs/:key, /segments/:key (M5C).
//   - Detail views with edit-in-place: name, description, enabled, variants,
//     tags, default value (config), default variant (flag) (M5C).
//   - Visual rule editor (shared between Flag rules and Config rules) and a
//     condition editor (also used for Segments) (M5C).
//   - PUT to the admin API on save with success / error feedback (M5C).
//   - Refresh-on-focus so a second operator's edits show up when the tab
//     regains focus (M5C).
//   - "Test this context" dry-run panel against the saved entity (M5D).
//
// Single-file, no build step, no framework. Sections are marked with banner
// comments so navigation stays manageable.

(function () {
    "use strict";

    // ============================================================
    // Config + DOM
    // ============================================================
    var meta = document.querySelector('meta[name="featly-mount-path"]');
    var mountPath = (meta && meta.getAttribute("content")) || "/featly";
    if (mountPath.endsWith("/")) { mountPath = mountPath.slice(0, -1); }

    var STORAGE_ENV_KEY = "featly.envKey";

    var viewEl = document.getElementById("view");
    var envSelect = document.getElementById("env-select");
    var envPill = document.getElementById("env-pill");
    var envLock = document.getElementById("env-lock");
    var crumbsEl = document.getElementById("crumbs");
    var navLinks = Array.prototype.slice.call(document.querySelectorAll(".sb-item"));

    // Sidebar account row reflects the session.
    var sbUserName = document.getElementById("sb-user-name");
    var sbUserRole = document.getElementById("sb-user-role");
    var sbAvatar = document.getElementById("sb-avatar");

    // Tracks whether /api/auth/me has confirmed an active session this load.
    // The cookie itself is HttpOnly so JS can't read it directly — we infer
    // session state by hitting /me and remembering the result.
    var session = null;

    // POST /logout clears the cookie server-side; we then reload so the login
    // screen shows. credentials:'include' is mandatory — without it the browser
    // would skip the cookie and the server can't know which session to expire.
    // (The topbar avatar and the sidebar user row both sign out for now; the My
    // Account screen lands in a later step.)
    function signOut() {
        fetch("/api/auth/logout", { method: "POST", credentials: "include" })
            .finally(function () { session = null; location.reload(); });
    }
    var accountBtn = document.getElementById("account-btn");
    if (accountBtn) { accountBtn.addEventListener("click", signOut); }
    var sbUserEl = document.getElementById("sb-user");
    if (sbUserEl) {
        sbUserEl.addEventListener("click", signOut);
        sbUserEl.addEventListener("keydown", function (e) {
            if (e.key === "Enter" || e.key === " ") { e.preventDefault(); signOut(); }
        });
    }

    // ============================================================
    // Auth
    // ============================================================
    function isAuthenticated() { return session !== null; }

    function showAuthPrompt(errorText) {
        envSelect.disabled = true;
        if (sbUserName) { sbUserName.textContent = "Not signed in"; }
        if (sbUserRole) { sbUserRole.textContent = "—"; }
        viewEl.innerHTML = [
            '<h1>Sign in to Featly</h1>',
            '<div class="card auth-card">',
            '  <p>Enter an admin API key to start a dashboard session. Look up <code>Featly:Server:AdminApiKey</code> in <code>appsettings.json</code>, or use a key minted via the admin keys endpoint.</p>',
            '  <form id="auth-form" class="auth-form">',
            '    <label for="auth-token">Admin API key</label>',
            '    <input id="auth-token" type="password" autocomplete="off" required spellcheck="false" />',
            '    <button type="submit" class="btn primary">Sign in</button>',
            errorText ? '    <p class="error" id="auth-error">' + esc(errorText) + '</p>' : '',
            '  </form>',
            '  <p class="muted">Featly stores your session as an <code>HttpOnly; SameSite=Strict</code> cookie. Nothing is written to <code>localStorage</code>.</p>',
            '</div>',
        ].join("\n");
        document.getElementById("auth-form").addEventListener("submit", function (e) {
            e.preventDefault();
            var v = document.getElementById("auth-token").value.trim();
            if (!v) { return; }
            login(v);
        });
    }

    function login(apiKey) {
        fetch("/api/auth/login", {
            method: "POST",
            credentials: "include",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ apiKey: apiKey }),
        }).then(function (res) {
            if (res.status === 401) {
                showAuthPrompt("That key didn't match any admin credential.");
                return;
            }
            if (!res.ok) {
                return res.text().then(function (t) {
                    showAuthPrompt(t || ("Login failed (" + res.status + ")."));
                });
            }
            return res.json().then(function (me) {
                session = me;
                boot();
            });
        }).catch(function (err) {
            showAuthPrompt(err && err.message ? err.message : "Network error.");
        });
    }

    function probeSession() {
        // GET /me returns 200 + identity if the cookie is valid, 401 if not.
        return fetch("/api/auth/me", { credentials: "include" }).then(function (res) {
            if (res.status === 200) {
                return res.json().then(function (me) { session = me; return true; });
            }
            session = null;
            return false;
        }).catch(function () { session = null; return false; });
    }

    // ============================================================
    // HTTP
    // ============================================================
    function api(method, path, body) {
        var url = path.startsWith("http") ? path : "/api" + path;
        var headers = {};
        var init = { method: method, headers: headers, credentials: "include" };
        if (body !== undefined) {
            headers["Content-Type"] = "application/json";
            init.body = JSON.stringify(body);
        }
        return fetch(url, init).then(function (res) {
            if (res.status === 401 || res.status === 403) {
                session = null;
                var err = new Error("Session expired — please sign in again.");
                err.kind = "auth";
                throw err;
            }
            if (!res.ok) {
                return res.text().then(function (b) {
                    var err = new Error(b || ("Request failed (" + res.status + ")"));
                    err.status = res.status;
                    throw err;
                });
            }
            if (res.status === 204) { return null; }
            var ct = res.headers.get("Content-Type") || "";
            return ct.indexOf("application/json") >= 0 ? res.json() : res.text();
        });
    }

    // ============================================================
    // Environments
    // ============================================================
    var environments = [];
    var currentEnv = null;

    function loadEnvironments() {
        return api("GET", "/admin/environments").then(function (list) {
            environments = Array.isArray(list) ? list : [];
            var saved = null;
            try { saved = localStorage.getItem(STORAGE_ENV_KEY); } catch (_) {}
            var pick = environments.find(function (e) { return e.key === saved; })
                || environments.find(function (e) { return e.isDefault; })
                || environments[0];
            currentEnv = pick || null;
            renderEnvSelect();
        });
    }

    function renderEnvSelect() {
        envSelect.innerHTML = environments.map(function (e) {
            var sel = currentEnv && e.id === currentEnv.id ? " selected" : "";
            return '<option value="' + esc(e.key) + '"' + sel + '>' + esc(e.name || e.key) + (e.readOnly ? " (read-only)" : "") + '</option>';
        }).join("");
        envSelect.disabled = environments.length === 0;
        updateEnvPill();
    }

    envSelect.addEventListener("change", function () {
        var picked = environments.find(function (e) { return e.key === envSelect.value; });
        if (!picked) { return; }
        currentEnv = picked;
        try { localStorage.setItem(STORAGE_ENV_KEY, picked.key); } catch (_) {}
        updateEnvPill();
        render();
    });

    // ============================================================
    // Helpers
    // ============================================================
    function esc(s) {
        return String(s == null ? "" : s).replace(/[&<>"']/g, function (c) {
            return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
        });
    }
    function code(s) { return '<code>' + esc(s) + '</code>'; }
    function badge(s) { return '<span class="badge">' + esc(s) + '</span>'; }
    function truncate(s, n) { s = String(s || ""); return s.length <= n ? s : s.slice(0, n - 1) + "…"; }
    function formatDate(iso) {
        if (!iso) { return ""; }
        var d = new Date(iso);
        if (isNaN(d.getTime())) { return esc(iso); }
        var fmt = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" });
        return '<time datetime="' + esc(iso) + '" title="' + esc(iso) + '">' + esc(fmt.format(d)) + '</time>';
    }
    function readJsonField(el) {
        var raw = (el.value || "").trim();
        if (!raw) { return null; }
        try { return JSON.parse(raw); }
        catch (_) {
            // Plain strings are convenient — wrap them in quotes.
            return raw;
        }
    }
    function setMessage(kind, text) {
        var slot = document.getElementById("save-msg");
        if (!slot) { return; }
        slot.className = "save-msg save-msg--" + kind;
        slot.textContent = text;
    }

    // ============================================================
    // Shell: icons, theme, breadcrumbs, env pill
    // ============================================================
    // Inline-SVG icons (Lucide path data). No webfont — see README §6. This is
    // the shell subset; more icons are added with the per-screen rewrites.
    var ICONS = {
        "home": '<path d="m3 9 9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/><polyline points="9 22 9 12 15 12 15 22"/>',
        "inbox": '<polyline points="22 12 16 12 14 15 10 15 8 12 2 12"/><path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z"/>',
        "flag": '<path d="M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1z"/><line x1="4" x2="4" y1="22" y2="15"/>',
        "sliders": '<line x1="4" x2="4" y1="21" y2="14"/><line x1="4" x2="4" y1="10" y2="3"/><line x1="12" x2="12" y1="21" y2="12"/><line x1="12" x2="12" y1="8" y2="3"/><line x1="20" x2="20" y1="21" y2="16"/><line x1="20" x2="20" y1="12" y2="3"/><line x1="2" x2="6" y1="14" y2="14"/><line x1="10" x2="14" y1="8" y2="8"/><line x1="18" x2="22" y1="16" y2="16"/>',
        "segment": '<circle cx="12" cy="12" r="10"/><circle cx="12" cy="12" r="6"/><circle cx="12" cy="12" r="2"/>',
        "flask": '<path d="M10 2v7.31"/><path d="M14 9.3V1.99"/><path d="M8.5 2h7"/><path d="M14 9.3a6.5 6.5 0 1 1-4 0"/><path d="M5.52 16h12.96"/>',
        "users": '<path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/>',
        "shield": '<path d="M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z"/>',
        "webhook": '<path d="M18 16.98h-5.99c-1.1 0-1.95.94-2.48 1.9A4 4 0 0 1 2 17c.01-.7.2-1.4.57-2"/><path d="m6 17 3.13-5.78c.53-.97.1-2.18-.5-3.1a4 4 0 1 1 6.89-4.06"/><path d="m12 6 3.13 5.73C15.66 12.7 16.9 13 18 13a4 4 0 0 1 0 8"/>',
        "git-pull-request": '<circle cx="18" cy="18" r="3"/><circle cx="6" cy="6" r="3"/><path d="M13 6h3a2 2 0 0 1 2 2v7"/><line x1="6" x2="6" y1="9" y2="21"/>',
        "scroll": '<path d="M19 17V5a2 2 0 0 0-2-2H4"/><path d="M8 21h12a2 2 0 0 0 2-2v-1a1 1 0 0 0-1-1H11a1 1 0 0 0-1 1v1a2 2 0 1 1-4 0V5a2 2 0 1 0-4 0v2a1 1 0 0 0 1 1h3"/>',
        "settings": '<path d="M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z"/><circle cx="12" cy="12" r="3"/>',
        "chevron-down": '<path d="m6 9 6 6 6-6"/>',
        "chevron-up": '<path d="m18 15-6-6-6 6"/>',
        "lock": '<rect width="18" height="11" x="3" y="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/>',
        "search": '<circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/>',
        "sun": '<circle cx="12" cy="12" r="4"/><path d="M12 2v2"/><path d="M12 20v2"/><path d="m4.93 4.93 1.41 1.41"/><path d="m17.66 17.66 1.41 1.41"/><path d="M2 12h2"/><path d="M20 12h2"/><path d="m6.34 17.66-1.41 1.41"/><path d="m19.07 4.93-1.41 1.41"/>',
        "moon": '<path d="M12 3a6 6 0 0 0 9 9 9 9 0 1 1-9-9Z"/>',
        "bell": '<path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9"/><path d="M10.3 21a1.94 1.94 0 0 0 3.4 0"/>',
        "user": '<path d="M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/>',
        "dots": '<circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/><circle cx="5" cy="12" r="1"/>',
        "plus": '<path d="M5 12h14"/><path d="M12 5v14"/>',
        "check": '<path d="M20 6 9 17l-5-5"/>',
        "clock": '<circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/>',
        "user-shield": '<path d="M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z"/>',
        "layers": '<path d="M12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83z"/><path d="m22 17.65-9.17 4.16a2 2 0 0 1-1.66 0L2 17.65"/><path d="m22 12.65-9.17 4.16a2 2 0 0 1-1.66 0L2 12.65"/>',
        "key": '<circle cx="7.5" cy="15.5" r="5.5"/><path d="m21 2-9.6 9.6"/><path d="m15.5 7.5 3 3L22 7l-3-3"/>',
        "x": '<path d="M18 6 6 18"/><path d="m6 6 12 12"/>',
        "folder": '<path d="M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z"/>',
        "grip": '<circle cx="9" cy="6" r="1"/><circle cx="9" cy="12" r="1"/><circle cx="9" cy="18" r="1"/><circle cx="15" cy="6" r="1"/><circle cx="15" cy="12" r="1"/><circle cx="15" cy="18" r="1"/>'
    };
    function icon(name, size) {
        size = size || 16;
        return '<svg class="ti" width="' + size + '" height="' + size + '" viewBox="0 0 24 24" '
            + 'fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" '
            + 'stroke-linejoin="round" aria-hidden="true">' + (ICONS[name] || "") + '</svg>';
    }
    function hydrateIcons(root) {
        var slots = (root || document).querySelectorAll("[data-ti]");
        Array.prototype.forEach.call(slots, function (el) {
            if (el.getAttribute("data-ti-done") === "1") { return; }
            el.innerHTML = icon(el.getAttribute("data-ti"));
            el.setAttribute("data-ti-done", "1");
        });
    }

    // Theme: OS default, overridable by a stored choice; the toggle swaps the icon.
    var THEME_KEY = "featly.theme";
    function effectiveTheme() {
        return document.documentElement.getAttribute("data-theme")
            || (window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light");
    }
    function applyThemeIcon() {
        var slot = document.querySelector("#theme-toggle .ti-slot");
        if (slot) { slot.innerHTML = icon(effectiveTheme() === "dark" ? "sun" : "moon"); }
    }
    function initTheme() {
        var saved = null;
        try { saved = localStorage.getItem(THEME_KEY); } catch (_) {}
        if (saved === "dark" || saved === "light") { document.documentElement.setAttribute("data-theme", saved); }
        var btn = document.getElementById("theme-toggle");
        if (btn) {
            btn.addEventListener("click", function () {
                var next = effectiveTheme() === "dark" ? "light" : "dark";
                document.documentElement.setAttribute("data-theme", next);
                try { localStorage.setItem(THEME_KEY, next); } catch (_) {}
                applyThemeIcon();
            });
        }
        applyThemeIcon();
    }

    // Env pill: colored pip (prod/staging/dev) + LOCKED badge from currentEnv.
    function updateEnvPill() {
        if (!envPill) { return; }
        var key = ((currentEnv && currentEnv.key) || "").toLowerCase();
        var tone = /prod/.test(key) ? "prod" : (/stag|stg|uat|pre/.test(key) ? "staging" : "dev");
        envPill.className = "env-pill " + tone;
        if (envLock) { envLock.hidden = !(currentEnv && currentEnv.readOnly); }
    }

    // Breadcrumbs: workspace / section [ / detail key ].
    var SECTION_LABELS = {
        overview: "Overview", inbox: "Inbox", changeDetail: "Inbox",
        flagList: "Flags", flagDetail: "Flags", configList: "Configs", configDetail: "Configs",
        segmentList: "Segments", segmentDetail: "Segments",
        experimentList: "Experiments", experimentDetail: "Experiments",
        userList: "Users", userDetail: "Users", roleList: "Roles",
        groupList: "Groups", groupDetail: "Groups", apiKeyList: "API keys",
        projectList: "Projects", projectDetail: "Projects",
        approvals: "Approval policies", webhookList: "Webhooks", webhookDetail: "Webhooks",
        auditLog: "Audit log", settings: "Settings"
    };
    function renderCrumbs(route) {
        if (!crumbsEl) { return; }
        var section = SECTION_LABELS[route.key] || "Overview";
        var detail = route.params && (route.params.key || route.params.id);
        var parts = ['<span class="crumb">Featly</span>', '<span class="sep">/</span>'];
        if (detail) {
            parts.push('<a class="crumb" data-link="' + esc(route.navRoute) + '" href="' + esc(mountPath + route.navRoute) + '">' + esc(section) + '</a>');
            parts.push('<span class="sep">/</span>');
            parts.push('<span class="crumb current">' + esc(detail) + '</span>');
        } else {
            parts.push('<span class="crumb current">' + esc(section) + '</span>');
        }
        crumbsEl.innerHTML = parts.join("");
    }

    var OPERATORS = [
        "Equals", "NotEquals", "In", "NotIn",
        "GreaterThan", "GreaterThanOrEqual", "LessThan", "LessThanOrEqual",
        "Contains", "StartsWith", "EndsWith", "Matches",
        "SemverGt", "SemverLt", "SemverEq", "InSegment",
    ];

    // ============================================================
    // Router
    // ============================================================
    var routes = [
        { match: /^\/?$/,                key: "overview",     params: function () { return {}; } },
        { match: /^\/flags\/(.+)$/,      key: "flagDetail",   params: function (m) { return { key: decodeURIComponent(m[1]) }; } },
        { match: /^\/flags\/?$/,         key: "flagList",     params: function () { return {}; } },
        { match: /^\/configs\/(.+)$/,    key: "configDetail", params: function (m) { return { key: decodeURIComponent(m[1]) }; } },
        { match: /^\/configs\/?$/,       key: "configList",   params: function () { return {}; } },
        { match: /^\/segments\/(.+)$/,   key: "segmentDetail",params: function (m) { return { key: decodeURIComponent(m[1]) }; } },
        { match: /^\/segments\/?$/,      key: "segmentList",  params: function () { return {}; } },
        { match: /^\/experiments\/(.+)$/, key: "experimentDetail", params: function (m) { return { key: decodeURIComponent(m[1]) }; } },
        { match: /^\/experiments\/?$/,   key: "experimentList", params: function () { return {}; } },
        { match: /^\/users\/(.+)$/,      key: "userDetail",   params: function (m) { return { id: decodeURIComponent(m[1]) }; } },
        { match: /^\/users\/?$/,         key: "userList",     params: function () { return {}; } },
        { match: /^\/roles\/?$/,         key: "roleList",     params: function () { return {}; } },
        { match: /^\/groups\/(.+)$/,     key: "groupDetail",  params: function (m) { return { key: decodeURIComponent(m[1]) }; } },
        { match: /^\/groups\/?$/,        key: "groupList",    params: function () { return {}; } },
        { match: /^\/apikeys\/?$/,       key: "apiKeyList",   params: function () { return {}; } },
        { match: /^\/projects\/(.+)$/,   key: "projectDetail", params: function (m) { return { key: decodeURIComponent(m[1]) }; } },
        { match: /^\/projects\/?$/,      key: "projectList",  params: function () { return {}; } },
        { match: /^\/inbox\/(.+)$/,      key: "changeDetail", params: function (m) { return { id: decodeURIComponent(m[1]) }; } },
        { match: /^\/inbox\/?$/,         key: "inbox",        params: function () { return {}; } },
        { match: /^\/approvals\/?$/,     key: "approvals",    params: function () { return {}; } },
        { match: /^\/webhooks\/(.+)$/,   key: "webhookDetail",params: function (m) { return { id: decodeURIComponent(m[1]) }; } },
        { match: /^\/webhooks\/?$/,      key: "webhookList",  params: function () { return {}; } },
        { match: /^\/audit\/?$/,         key: "auditLog",     params: function () { return {}; } },
        { match: /^\/settings\/?$/,      key: "settings",     params: function () { return {}; } },
    ];

    function currentRoute() {
        var path = window.location.pathname;
        if (!path.startsWith(mountPath)) { return { key: "overview", params: {}, navRoute: "/" }; }
        var sub = path.slice(mountPath.length) || "/";
        for (var i = 0; i < routes.length; i++) {
            var m = sub.match(routes[i].match);
            if (m) {
                return {
                    key: routes[i].key,
                    params: routes[i].params(m),
                    navRoute: navRouteFor(routes[i].key),
                };
            }
        }
        return { key: "overview", params: {}, navRoute: "/" };
    }

    function navRouteFor(key) {
        if (key === "overview") { return "/"; }
        if (key.indexOf("flag") === 0) { return "/flags"; }
        if (key.indexOf("config") === 0) { return "/configs"; }
        if (key.indexOf("segment") === 0) { return "/segments"; }
        if (key.indexOf("experiment") === 0) { return "/experiments"; }
        if (key.indexOf("user") === 0) { return "/users"; }
        if (key.indexOf("role") === 0) { return "/roles"; }
        if (key.indexOf("group") === 0) { return "/groups"; }
        if (key.indexOf("apiKey") === 0) { return "/apikeys"; }
        if (key.indexOf("project") === 0) { return "/projects"; }
        if (key === "inbox" || key === "changeDetail") { return "/inbox"; }
        if (key === "approvals") { return "/approvals"; }
        if (key.indexOf("webhook") === 0) { return "/webhooks"; }
        if (key === "auditLog") { return "/audit"; }
        if (key === "settings") { return "/settings"; }
        return "/";
    }

    function navigate(path) {
        window.history.pushState({}, "", mountPath + path);
        render();
        document.getElementById("main").focus({ preventScroll: false });
    }

    function onNavClick(event) {
        var anchor = event.target.closest("a[data-link]");
        if (!anchor) { return; }
        if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0) { return; }
        event.preventDefault();
        navigate(anchor.getAttribute("data-link"));
    }
    document.addEventListener("click", onNavClick);
    navLinks.forEach(function (link) { link.setAttribute("data-link", link.getAttribute("data-route")); });

    window.addEventListener("popstate", render);
    window.addEventListener("focus", function () {
        // Refresh-on-focus: re-render the current view so a second operator's
        // edits show up. Cheap, no SSE plumbing yet.
        if (isAuthenticated() && currentEnv) { render(); }
    });

    // ============================================================
    // Command palette (Cmd/Ctrl-K): jump to any screen, or open a
    // flag/config/segment in the current environment by key or name.
    // No new endpoints — reuses the admin list APIs + the router.
    // ============================================================
    var paletteEl = null, paletteOpen = false, paletteItems = [], paletteActive = 0;
    var paletteEntityCache = { env: null, items: null };
    var NAV_COMMANDS = [
        { label: "Overview", route: "/", ti: "home" },
        { label: "Inbox", route: "/inbox", ti: "inbox" },
        { label: "Flags", route: "/flags", ti: "flag" },
        { label: "Configs", route: "/configs", ti: "sliders" },
        { label: "Segments", route: "/segments", ti: "segment" },
        { label: "Experiments", route: "/experiments", ti: "flask" },
        { label: "Users", route: "/users", ti: "users" },
        { label: "Groups", route: "/groups", ti: "layers" },
        { label: "Roles", route: "/roles", ti: "shield" },
        { label: "API keys", route: "/apikeys", ti: "key" },
        { label: "Webhooks", route: "/webhooks", ti: "webhook" },
        { label: "Approval policies", route: "/approvals", ti: "git-pull-request" },
        { label: "Audit log", route: "/audit", ti: "scroll" },
        { label: "Settings", route: "/settings", ti: "settings" },
    ];

    function ensurePalette() {
        if (paletteEl) { return paletteEl; }
        paletteEl = document.createElement("div");
        paletteEl.className = "palette-backdrop";
        paletteEl.hidden = true;
        paletteEl.innerHTML = [
            '<div class="palette" role="dialog" aria-modal="true" aria-label="Command palette">',
            '  <div class="palette-input"><span class="ti-slot" data-ti="search"></span>',
            '    <input id="palette-q" type="text" autocomplete="off" spellcheck="false" aria-label="Search" placeholder="Search flags, configs, segments — or jump to a screen…" />',
            '  </div>',
            '  <div class="palette-list" id="palette-list" role="listbox"></div>',
            '  <div class="palette-foot"><span class="ent"><span class="kp">↑↓</span> navigate</span><span class="ent"><span class="kp">↵</span> open</span><span class="ent"><span class="kp">esc</span> close</span></div>',
            '</div>',
        ].join("");
        document.body.appendChild(paletteEl);
        hydrateIcons(paletteEl);
        paletteEl.addEventListener("mousedown", function (e) { if (e.target === paletteEl) { closePalette(); } });
        var input = paletteEl.querySelector("#palette-q");
        input.addEventListener("input", function () { paletteFilter(input.value); });
        input.addEventListener("keydown", paletteKeydown);
        return paletteEl;
    }

    function openPalette() {
        if (!isAuthenticated()) { return; }
        ensurePalette();
        paletteEl.hidden = false;
        paletteOpen = true;
        var input = paletteEl.querySelector("#palette-q");
        input.value = "";
        paletteFilter("");
        input.focus();
        paletteLoadEntities().then(function () { if (paletteOpen) { paletteFilter(input.value); } });
    }

    function closePalette() {
        if (paletteEl) { paletteEl.hidden = true; }
        paletteOpen = false;
    }

    function paletteLoadEntities() {
        var envKey = currentEnv && currentEnv.key;
        if (!envKey) { paletteEntityCache = { env: null, items: [] }; return Promise.resolve(); }
        if (paletteEntityCache.env === envKey && paletteEntityCache.items) { return Promise.resolve(); }
        var q = "?env=" + encodeURIComponent(envKey);
        return Promise.all([
            api("GET", "/admin/flags" + q).catch(function () { return []; }),
            api("GET", "/admin/configs" + q).catch(function () { return []; }),
            api("GET", "/admin/segments" + q).catch(function () { return []; }),
        ]).then(function (res) {
            var items = [];
            (res[0] || []).forEach(function (f) { items.push({ label: f.key, sub: f.name || "", kind: "Flag", route: "/flags/" + encodeURIComponent(f.key), ti: "flag" }); });
            (res[1] || []).forEach(function (c) { items.push({ label: c.key, sub: c.name || "", kind: "Config", route: "/configs/" + encodeURIComponent(c.key), ti: "sliders" }); });
            (res[2] || []).forEach(function (s) { items.push({ label: s.key, sub: s.name || "", kind: "Segment", route: "/segments/" + encodeURIComponent(s.key), ti: "segment" }); });
            paletteEntityCache = { env: envKey, items: items };
        }).catch(function () { paletteEntityCache = { env: envKey, items: [] }; });
    }

    function paletteFilter(q) {
        q = (q || "").trim().toLowerCase();
        var nav = NAV_COMMANDS.filter(function (c) { return !q || c.label.toLowerCase().indexOf(q) >= 0; })
            .map(function (c) { return { label: c.label, sub: "", kind: "Go to", route: c.route, ti: c.ti, group: "Navigate" }; });
        var ents = (paletteEntityCache.items || []).filter(function (e) {
            return !q || e.label.toLowerCase().indexOf(q) >= 0 || (e.sub && e.sub.toLowerCase().indexOf(q) >= 0);
        });
        ents.forEach(function (e) { e.group = "Entities"; });
        var ordered = q ? ents.concat(nav) : nav.concat(ents.slice(0, 8));
        paletteItems = ordered.slice(0, 40);
        paletteActive = 0;
        paletteRender();
    }

    function paletteRender() {
        var list = paletteEl.querySelector("#palette-list");
        if (!paletteItems.length) {
            list.innerHTML = '<div class="palette-section-label">No matches</div>';
            return;
        }
        var html = "", lastGroup = null;
        paletteItems.forEach(function (it, i) {
            if (it.group !== lastGroup) { html += '<div class="palette-section-label">' + esc(it.group) + '</div>'; lastGroup = it.group; }
            html += '<div class="palette-item' + (i === paletteActive ? " active" : "") + '" data-i="' + i + '" role="option">'
                + '<span class="ti-slot" data-ti="' + esc(it.ti) + '"></span>'
                + '<span><span class="mono">' + esc(it.label) + '</span>' + (it.sub ? ' <span class="sub">' + esc(it.sub) + '</span>' : '') + '</span>'
                + '<span class="kp">' + esc(it.kind) + '</span>'
                + '</div>';
        });
        list.innerHTML = html;
        hydrateIcons(list);
        Array.prototype.slice.call(list.querySelectorAll(".palette-item")).forEach(function (el) {
            el.addEventListener("mousemove", function () { paletteActive = parseInt(el.getAttribute("data-i"), 10); paletteHighlight(); });
            el.addEventListener("click", function () { paletteSelect(parseInt(el.getAttribute("data-i"), 10)); });
        });
    }

    function paletteHighlight() {
        var els = paletteEl.querySelectorAll(".palette-item");
        Array.prototype.forEach.call(els, function (el) {
            el.classList.toggle("active", parseInt(el.getAttribute("data-i"), 10) === paletteActive);
        });
        var active = paletteEl.querySelector(".palette-item.active");
        if (active && active.scrollIntoView) { active.scrollIntoView({ block: "nearest" }); }
    }

    function paletteSelect(i) {
        var it = paletteItems[i];
        if (!it) { return; }
        closePalette();
        navigate(it.route);
    }

    function paletteKeydown(e) {
        if (e.key === "ArrowDown") { e.preventDefault(); paletteActive = Math.min(paletteActive + 1, paletteItems.length - 1); paletteHighlight(); }
        else if (e.key === "ArrowUp") { e.preventDefault(); paletteActive = Math.max(paletteActive - 1, 0); paletteHighlight(); }
        else if (e.key === "Enter") { e.preventDefault(); paletteSelect(paletteActive); }
        else if (e.key === "Escape") { e.preventDefault(); closePalette(); }
    }

    document.addEventListener("keydown", function (e) {
        if ((e.metaKey || e.ctrlKey) && (e.key === "k" || e.key === "K")) {
            e.preventDefault();
            if (paletteOpen) { closePalette(); } else { openPalette(); }
        } else if (e.key === "Escape" && paletteOpen) { closePalette(); }
    });
    var globalSearchBtn = document.getElementById("global-search");
    if (globalSearchBtn) { globalSearchBtn.addEventListener("click", openPalette); }

    // ============================================================
    // Notifications popover (bell): pending approvals + role-upgrade
    // requests, reusing the same admin endpoints as the Inbox.
    // ============================================================
    var notifPop = null, notifOpen = false;
    function ensureNotifPop() {
        if (notifPop) { return notifPop; }
        notifPop = document.createElement("div");
        notifPop.className = "notif-pop";
        document.body.appendChild(notifPop);
        document.addEventListener("mousedown", function (e) {
            if (!notifOpen) { return; }
            var bell = document.getElementById("notif-btn");
            if (notifPop.contains(e.target) || (bell && bell.contains(e.target))) { return; }
            closeNotif();
        });
        document.addEventListener("keydown", function (e) { if (e.key === "Escape" && notifOpen) { closeNotif(); } });
        return notifPop;
    }
    function closeNotif() { if (notifPop) { notifPop.classList.remove("open"); } notifOpen = false; }
    function openNotif() {
        if (!isAuthenticated()) { return; }
        ensureNotifPop();
        notifPop.innerHTML = '<div class="notif-head">Notifications</div><div class="notif-list"><div class="notif-empty">Loading…</div></div>';
        notifPop.classList.add("open");
        notifOpen = true;
        fetchNotifications().then(function (d) { if (notifOpen) { renderNotifPop(d.changes, d.upgrades); } });
    }
    function toggleNotif() { if (notifOpen) { closeNotif(); } else { openNotif(); } }
    function fetchNotifications() {
        return Promise.all([
            api("GET", "/admin/changes?status=Pending").catch(function () { return []; }),
            api("GET", "/admin/role-upgrade-requests?status=Pending").catch(function () { return []; }),
        ]).then(function (res) {
            return { changes: Array.isArray(res[0]) ? res[0] : [], upgrades: Array.isArray(res[1]) ? res[1] : [] };
        }).catch(function () { return { changes: [], upgrades: [] }; });
    }
    function renderNotifPop(changes, upgrades) {
        var total = changes.length + upgrades.length;
        var parts = ['<div class="notif-head">Notifications <span class="count">' + total + '</span></div>', '<div class="notif-list">'];
        if (!total) {
            parts.push('<div class="notif-empty">You’re all caught up.</div>');
        } else {
            if (changes.length) {
                parts.push('<div class="notif-section">Awaiting approval</div>');
                changes.forEach(function (c) {
                    parts.push('<div class="notif-item" data-go="/inbox/' + encodeURIComponent(c.id) + '">'
                        + '<span class="ic"><span class="ti-slot" data-ti="git-pull-request"></span></span>'
                        + '<span class="t"><span class="mono">' + esc(c.entityKey || c.action || "change") + '</span>'
                        + '<span class="sub">' + esc((c.action || "") + " · " + truncate(c.authorUserId || "", 8)) + '</span></span>'
                        + '</div>');
                });
            }
            if (upgrades.length) {
                parts.push('<div class="notif-section">Role upgrade requests</div>');
                upgrades.forEach(function (u) {
                    parts.push('<div class="notif-item" data-go="/inbox">'
                        + '<span class="ic"><span class="ti-slot" data-ti="user-shield"></span></span>'
                        + '<span class="t"><span class="mono">' + esc(truncate(u.userId || "", 12)) + '</span>'
                        + '<span class="sub">' + esc(truncate(u.justification || "no justification", 48)) + '</span></span>'
                        + '</div>');
                });
            }
        }
        parts.push('</div><div class="notif-foot"><a class="btn outline xs" data-go="/inbox" href="' + esc(mountPath) + '/inbox">Open Inbox</a></div>');
        notifPop.innerHTML = parts.join("");
        hydrateIcons(notifPop);
        Array.prototype.slice.call(notifPop.querySelectorAll("[data-go]")).forEach(function (el) {
            el.addEventListener("click", function (e) { e.preventDefault(); closeNotif(); navigate(el.getAttribute("data-go")); });
        });
        updateNotifBadge(total);
    }
    function updateNotifBadge(total) {
        var bell = document.getElementById("notif-btn");
        if (!bell) { return; }
        var dot = bell.querySelector(".dot-count");
        if (total > 0) {
            if (!dot) { dot = document.createElement("span"); dot.className = "dot-count"; bell.appendChild(dot); }
            dot.textContent = total > 9 ? "9+" : String(total);
        } else if (dot) { dot.remove(); }
    }
    function refreshNotifBadge() {
        if (!isAuthenticated()) { return; }
        fetchNotifications().then(function (d) { updateNotifBadge(d.changes.length + d.upgrades.length); });
    }
    var notifBtn = document.getElementById("notif-btn");
    if (notifBtn) { notifBtn.addEventListener("click", function (e) { e.stopPropagation(); toggleNotif(); }); }

    // ============================================================
    // Views
    // ============================================================
    function render() {
        if (!isAuthenticated()) { showAuthPrompt(); return; }
        var route = currentRoute();
        var view = views[route.key];
        if (!view) { views.overview(); return; }
        navLinks.forEach(function (link) {
            var active = link.getAttribute("data-route") === route.navRoute;
            link.classList.toggle("active", active);
            if (active) { link.setAttribute("aria-current", "page"); } else { link.removeAttribute("aria-current"); }
        });
        renderCrumbs(route);
        document.title = route.key === "overview" ? "Featly" : "Featly — " + route.key.replace(/([A-Z])/g, " $1").trim();
        view(route.params);
    }

    var views = {
        overview: function () {
            viewEl.innerHTML = [
                '<h1>Overview</h1>',
                '<div class="card">',
                '  <p>Welcome to Featly. M5C lights up the detail screens and visual rule editor.</p>',
                '  <p class="muted">Click any row in the list views to edit it. Saved changes round-trip through the admin API and propagate to connected SDKs through the existing change-notification path.</p>',
                '</div>',
            ].join("\n");
        },
        flagList:    function () { renderFlagList(); },
        configList:  function () { renderConfigList(); },
        segmentList: function () { renderSegmentList(); },
        flagDetail:    function (p) { renderFlagDetail(p.key); },
        configDetail:  function (p) { renderConfigDetail(p.key); },
        segmentDetail: function (p) { renderSegmentDetail(p.key); },
        experimentList:   function () { renderExperimentList(); },
        experimentDetail: function (p) { renderExperimentDetail(p.key); },
        userList:    function () { renderUserList(); },
        userDetail:  function (p) { renderUserDetail(p.id); },
        inbox:        function () { renderInbox(); },
        changeDetail: function (p) { renderChangeDetail(p.id); },
        approvals:    function () { renderApprovalsEditor(); },
        webhookList:   function () { renderWebhookList(); },
        webhookDetail: function (p) { renderWebhookDetail(p.id); },
        auditLog:      function () { renderAuditLog(); },
        roleList:    function () { renderRoleList(); },
        groupList:   function () { renderGroupList(); },
        groupDetail: function (p) { renderGroupDetail(p.key); },
        apiKeyList:  function () { renderApiKeyList(); },
        projectList:  function () { renderProjectList(); },
        projectDetail: function (p) { renderProjectDetail(p.key); },
        settings: function () { renderSettings(); },
    };

    function handleErrOnView(title) {
        return function (err) {
            if (err.kind === "auth") { showAuthPrompt(); return; }
            viewEl.innerHTML = '<h1>' + title + '</h1><div class="error">' + esc(err.message) + '</div>';
        };
    }

    // ---------- Flags list (redesigned, step 5) ----------
    // Tab + filter state survives re-renders within a session.
    var flagListTab = "all";

    function flagTypeBadge(type) {
        var t = String(type == null ? "" : type);
        var variant = t === "Boolean" ? " info" : (t === "Json" ? " accent" : "");
        return '<span class="badge sq' + variant + '">' + esc(t || "—") + '</span>';
    }
    function flagStatusBadge(f) {
        return f.enabled
            ? '<span class="badge success"><span class="dot"></span>on</span>'
            : '<span class="badge"><span class="dot"></span>off</span>';
    }
    function flagTagsCell(f) {
        var tags = f.tags || [];
        if (!tags.length) { return '<span class="muted" style="font-size:11px">—</span>'; }
        return '<div class="tag-list">' + tags.map(function (t) {
            return '<span class="tag muted">' + esc(t) + '</span>';
        }).join("") + '</div>';
    }
    function flagRow(f) {
        return '<tr data-key="' + esc(f.key) + '">'
            + '<td class="col-check"><input type="checkbox" class="row-check" aria-label="Select ' + esc(f.key) + '" /></td>'
            + '<td>' + flagStatusBadge(f) + '</td>'
            + '<td><div class="cell-keyname"><span class="mono cell-key">' + esc(f.key) + '</span>'
            + '<span class="cell-name">' + esc(f.name || "") + '</span></div></td>'
            + '<td>' + flagTypeBadge(f.type) + '</td>'
            + '<td class="num">' + (f.variants || []).length + '</td>'
            + '<td>' + flagTagsCell(f) + '</td>'
            + '<td class="mono cell-modified">' + (formatDate(f.updatedAt) || "—") + '</td>'
            + '<td class="col-actions"><button class="icon-btn" type="button" tabindex="-1" aria-label="More for ' + esc(f.key) + '"><span class="ti-slot" data-ti="dots"></span></button></td>'
            + '</tr>';
    }

    function renderFlagList() {
        if (!currentEnv) {
            viewEl.innerHTML = '<div class="page"><div class="page-head"><div class="title-wrap"><h1>Flags</h1></div></div>'
                + '<div class="page-body"><div class="empty"><p>No environment selected yet.</p></div></div></div>';
            return;
        }
        viewEl.innerHTML = flagListMarkup([], true);
        hydrateIcons(viewEl);
        api("GET", "/admin/flags?env=" + encodeURIComponent(currentEnv.key))
            .then(function (rows) {
                rows = rows || [];
                viewEl.innerHTML = flagListMarkup(rows, false);
                hydrateIcons(viewEl);
                wireFlagList(rows);
            })
            .catch(handleErrOnView("Flags"));
    }

    function flagListMarkup(all, loading) {
        var enabled = all.filter(function (f) { return f.enabled; }).length;
        var tabs = [
            { k: "all", label: "All flags", n: all.length },
            { k: "enabled", label: "Enabled", n: enabled },
            { k: "disabled", label: "Disabled", n: all.length - enabled },
        ];
        var tabsHtml = tabs.map(function (t) {
            return '<button class="tab' + (t.k === flagListTab ? " active" : "") + '" type="button" data-tab="' + t.k + '">'
                + esc(t.label) + ' <span class="count">' + t.n + '</span></button>';
        }).join("");
        var body = loading
            ? '<div class="empty"><p class="muted">Loading…</p></div>'
            : [
                '<div class="filter-bar">',
                '  <div class="search-input"><span class="ti-slot" data-ti="search"></span>',
                '    <input id="flag-filter" type="search" placeholder="Filter by key, name, or tag" spellcheck="false" autocomplete="off" />',
                '    <span class="clear-shortcut">/</span></div>',
                '</div>',
                '<div class="tbl-wrap"><table class="tbl"><thead><tr>',
                '  <th style="width:28px"><input type="checkbox" id="flag-check-all" aria-label="Select all" /></th>',
                '  <th style="width:54px">Status</th><th>Key / Name</th><th style="width:120px">Type</th>',
                '  <th style="width:84px">Variants</th><th>Tags</th><th style="width:140px">Last modified</th><th style="width:48px"></th>',
                '</tr></thead><tbody id="flag-tbody">' + all.map(flagRow).join("") + '</tbody></table></div>',
                '<div class="list-foot"><span id="flag-count">Showing ' + all.length + ' of ' + all.length + ' flags</span>',
                '<span class="mono">Click a row to open · <span class="kbd-key">/</span> to filter</span></div>',
            ].join("");
        return [
            '<div class="page">',
            '  <div class="page-head"><div class="title-wrap"><h1>Flags</h1>',
            '    <span class="sub">Boolean and multivariate feature flags evaluated in <code>' + esc(currentEnv.key) + '</code>.</span>',
            '  </div></div>',
            '  <div class="tabs">' + tabsHtml + '</div>',
            '  <div class="page-body tight">' + body + '</div>',
            '</div>',
        ].join("");
    }

    function wireFlagList(all) {
        var tbody = document.getElementById("flag-tbody");
        var countEl = document.getElementById("flag-count");
        var filterInput = document.getElementById("flag-filter");
        var checkAll = document.getElementById("flag-check-all");
        if (!tbody) { return; }

        function visibleRows() {
            var q = (filterInput && filterInput.value ? filterInput.value : "").trim().toLowerCase();
            return all.filter(function (f) {
                if (flagListTab === "enabled" && !f.enabled) { return false; }
                if (flagListTab === "disabled" && f.enabled) { return false; }
                if (q) {
                    var hay = (f.key + " " + (f.name || "") + " " + (f.tags || []).join(" ")).toLowerCase();
                    if (hay.indexOf(q) < 0) { return false; }
                }
                return true;
            });
        }
        function repaint() {
            var rows = visibleRows();
            tbody.innerHTML = rows.length
                ? rows.map(flagRow).join("")
                : '<tr><td colspan="8" class="muted" style="padding:18px;text-align:center">No flags match this filter.</td></tr>';
            hydrateIcons(tbody);
            if (countEl) { countEl.textContent = "Showing " + rows.length + " of " + all.length + " flags"; }
            if (checkAll) { checkAll.checked = false; }
        }

        Array.prototype.forEach.call(viewEl.querySelectorAll(".tab[data-tab]"), function (btn) {
            btn.addEventListener("click", function () {
                flagListTab = btn.getAttribute("data-tab");
                Array.prototype.forEach.call(viewEl.querySelectorAll(".tab[data-tab]"), function (b) {
                    b.classList.toggle("active", b === btn);
                });
                repaint();
            });
        });
        if (filterInput) { filterInput.addEventListener("input", repaint); }
        tbody.addEventListener("click", function (e) {
            if (e.target.closest("input, button, a")) { return; }
            var tr = e.target.closest("tr[data-key]");
            if (tr) { navigate("/flags/" + encodeURIComponent(tr.getAttribute("data-key"))); }
        });
        if (checkAll) {
            checkAll.addEventListener("change", function () {
                Array.prototype.forEach.call(tbody.querySelectorAll(".row-check"), function (c) { c.checked = checkAll.checked; });
            });
        }
    }

    // ---------- Shared list helpers (redesign, step 5) ----------
    function listPageShell(title, subHtml, bodyHtml) {
        return [
            '<div class="page">',
            '  <div class="page-head"><div class="title-wrap"><h1>' + esc(title) + '</h1>',
            subHtml ? '    <span class="sub">' + subHtml + '</span>' : '',
            '  </div></div>',
            '  <div class="page-body tight">' + bodyHtml + '</div>',
            '</div>',
        ].join("");
    }
    function listFilterBar(placeholder) {
        return '<div class="filter-bar"><div class="search-input"><span class="ti-slot" data-ti="search"></span>'
            + '<input class="list-filter" type="search" placeholder="' + esc(placeholder) + '" spellcheck="false" autocomplete="off" />'
            + '<span class="clear-shortcut">/</span></div></div>';
    }
    function listFoot(n, noun) {
        return '<div class="list-foot"><span class="list-count">Showing ' + n + ' of ' + n + ' ' + esc(noun) + '</span>'
            + '<span class="mono">Click a row to open · <span class="kbd-key">/</span> to filter</span></div>';
    }
    function listEmptyEnv(title) {
        return listPageShell(title, "", '<div class="empty"><p>No environment selected yet.</p></div>');
    }
    function tagsCell(row) {
        var tags = row.tags || [];
        if (!tags.length) { return '<span class="muted" style="font-size:11px">—</span>'; }
        return '<div class="tag-list">' + tags.map(function (t) { return '<span class="tag muted">' + esc(t) + '</span>'; }).join("") + '</div>';
    }
    // Wires filter + row-click + select-all for a list whose tbody is .list-tbody,
    // count is .list-count, filter input is .list-filter. `fields` are the row
    // properties searched; `rowFn` re-renders one row; navBase + key → detail route.
    function wireSimpleList(all, navBase, noun, fields, rowFn, colspan) {
        var tbody = viewEl.querySelector(".list-tbody");
        var countEl = viewEl.querySelector(".list-count");
        var filterInput = viewEl.querySelector(".list-filter");
        var checkAll = viewEl.querySelector(".check-all");
        if (!tbody) { return; }
        function repaint() {
            var q = (filterInput && filterInput.value ? filterInput.value : "").trim().toLowerCase();
            var rows = all.filter(function (r) {
                if (!q) { return true; }
                var hay = fields.map(function (f) { var v = r[f]; return Array.isArray(v) ? v.join(" ") : (v == null ? "" : v); }).join(" ").toLowerCase();
                return hay.indexOf(q) >= 0;
            });
            tbody.innerHTML = rows.length
                ? rows.map(rowFn).join("")
                : '<tr><td colspan="' + (colspan || 8) + '" class="muted" style="padding:18px;text-align:center">No ' + esc(noun) + ' match this filter.</td></tr>';
            hydrateIcons(tbody);
            if (countEl) { countEl.textContent = "Showing " + rows.length + " of " + all.length + " " + noun; }
            if (checkAll) { checkAll.checked = false; }
        }
        if (filterInput) { filterInput.addEventListener("input", repaint); }
        tbody.addEventListener("click", function (e) {
            if (e.target.closest("input, button, a")) { return; }
            var tr = e.target.closest("tr[data-key]");
            if (tr) { navigate(navBase + "/" + encodeURIComponent(tr.getAttribute("data-key"))); }
        });
        if (checkAll) {
            checkAll.addEventListener("change", function () {
                Array.prototype.forEach.call(tbody.querySelectorAll(".row-check"), function (c) { c.checked = checkAll.checked; });
            });
        }
    }

    // ---------- Configs list (redesign) ----------
    function configTypeBadge(type) {
        var t = String(type == null ? "" : type);
        var variant = t === "Json" ? " accent" : (t === "Bool" ? " info" : "");
        return '<span class="badge sq' + variant + '">' + esc(t || "—") + '</span>';
    }
    function configRow(c) {
        return '<tr data-key="' + esc(c.key) + '">'
            + '<td class="col-check"><input type="checkbox" class="row-check" aria-label="Select ' + esc(c.key) + '" /></td>'
            + '<td>' + configTypeBadge(c.type) + '</td>'
            + '<td><div class="cell-keyname"><span class="mono cell-key">' + esc(c.key) + '</span>'
            + '<span class="cell-name">' + esc(c.name || "") + '</span></div></td>'
            + '<td>' + code(truncate(JSON.stringify(c.defaultValue), 48)) + '</td>'
            + '<td>' + tagsCell(c) + '</td>'
            + '<td class="mono cell-modified">' + (formatDate(c.updatedAt) || "—") + '</td>'
            + '<td class="col-actions"><button class="icon-btn" type="button" tabindex="-1" aria-label="More for ' + esc(c.key) + '"><span class="ti-slot" data-ti="dots"></span></button></td>'
            + '</tr>';
    }
    function renderConfigList() {
        if (!currentEnv) { viewEl.innerHTML = listEmptyEnv("Configs"); return; }
        viewEl.innerHTML = configListMarkup([], true);
        hydrateIcons(viewEl);
        api("GET", "/admin/configs?env=" + encodeURIComponent(currentEnv.key))
            .then(function (rows) {
                rows = rows || [];
                viewEl.innerHTML = configListMarkup(rows, false);
                hydrateIcons(viewEl);
                wireSimpleList(rows, "/configs", "configs", ["key", "name", "tags"], configRow, 7);
            })
            .catch(handleErrOnView("Configs"));
    }
    function configListMarkup(all, loading) {
        var body = loading ? '<div class="empty"><p class="muted">Loading…</p></div>' : [
            listFilterBar("Filter by key, name, or tag"),
            '<div class="tbl-wrap"><table class="tbl"><thead><tr>',
            '  <th style="width:28px"><input type="checkbox" class="check-all" aria-label="Select all" /></th>',
            '  <th style="width:120px">Type</th><th>Key / Name</th><th>Value</th><th>Tags</th>',
            '  <th style="width:140px">Last modified</th><th style="width:48px"></th>',
            '</tr></thead><tbody class="list-tbody">' + all.map(configRow).join("") + '</tbody></table></div>',
            listFoot(all.length, "configs"),
        ].join("");
        return listPageShell("Configs", "Typed configuration values evaluated in <code>" + esc(currentEnv.key) + "</code>.", body);
    }

    // ---------- Segments list (redesign) ----------
    function segmentRow(s) {
        return '<tr data-key="' + esc(s.key) + '">'
            + '<td><div class="cell-keyname"><span class="mono cell-key">' + esc(s.key) + '</span>'
            + '<span class="cell-name">' + esc(s.name || "") + '</span></div></td>'
            + '<td class="num">' + (s.conditions || []).length + '</td>'
            + '<td class="mono cell-modified">' + (formatDate(s.updatedAt) || "—") + '</td>'
            + '<td class="col-actions"><button class="icon-btn" type="button" tabindex="-1" aria-label="More for ' + esc(s.key) + '"><span class="ti-slot" data-ti="dots"></span></button></td>'
            + '</tr>';
    }
    function renderSegmentList() {
        if (!currentEnv) { viewEl.innerHTML = listEmptyEnv("Segments"); return; }
        viewEl.innerHTML = segmentListMarkup([], true);
        hydrateIcons(viewEl);
        api("GET", "/admin/segments?env=" + encodeURIComponent(currentEnv.key))
            .then(function (rows) {
                rows = rows || [];
                viewEl.innerHTML = segmentListMarkup(rows, false);
                hydrateIcons(viewEl);
                wireSimpleList(rows, "/segments", "segments", ["key", "name"], segmentRow, 4);
            })
            .catch(handleErrOnView("Segments"));
    }
    function segmentListMarkup(all, loading) {
        var body = loading ? '<div class="empty"><p class="muted">Loading…</p></div>' : [
            listFilterBar("Filter by key or name"),
            '<div class="tbl-wrap"><table class="tbl"><thead><tr>',
            '  <th>Key / Name</th><th style="width:120px">Conditions</th>',
            '  <th style="width:140px">Last modified</th><th style="width:48px"></th>',
            '</tr></thead><tbody class="list-tbody">' + all.map(segmentRow).join("") + '</tbody></table></div>',
            listFoot(all.length, "segments"),
        ].join("");
        return listPageShell("Segments", "Reusable user groups referenced from flag and config rules via the <code>InSegment</code> operator.", body);
    }

    // ============================================================
    // Detail views (read + edit)
    // ============================================================
    function renderFlagDetail(key) {
        if (!currentEnv) { return; }
        viewEl.innerHTML = detailLoadingShell("Flag", key);
        api("GET", "/admin/flags/" + encodeURIComponent(key) + "?env=" + encodeURIComponent(currentEnv.key))
            .then(function (flag) { renderFlagEditor(flag); })
            .catch(handleErrOnView("Flag: " + key));
    }

    function renderConfigDetail(key) {
        if (!currentEnv) { return; }
        viewEl.innerHTML = detailLoadingShell("Config", key);
        api("GET", "/admin/configs/" + encodeURIComponent(key) + "?env=" + encodeURIComponent(currentEnv.key))
            .then(function (config) { renderConfigEditor(config); })
            .catch(handleErrOnView("Config: " + key));
    }

    function renderSegmentDetail(key) {
        if (!currentEnv) { return; }
        viewEl.innerHTML = detailLoadingShell("Segment", key);
        api("GET", "/admin/segments/" + encodeURIComponent(key) + "?env=" + encodeURIComponent(currentEnv.key))
            .then(function (segment) { renderSegmentEditor(segment); })
            .catch(handleErrOnView("Segment: " + key));
    }

    function detailLoadingShell(kind, key) {
        return listPageShell(kind, key ? esc(key) : "Loading…", '<div class="empty"><p class="muted">Loading…</p></div>');
    }

    function auditFooter(entity) {
        return '<div class="audit muted">'
            + 'Created ' + formatDate(entity.createdAt) + ' by ' + esc(entity.createdBy || "—") + '<br/>'
            + 'Updated ' + formatDate(entity.updatedAt) + ' by ' + esc(entity.updatedBy || "—")
            + '</div>';
    }

    // ---------- Flag editor ----------
    function renderFlagEditor(flag) {
        var variantOpts = (flag.variants || []).map(function (v) {
            return '<option value="' + esc(v.key) + '"' + (v.key === flag.defaultVariantKey ? " selected" : "") + '>' + esc(v.key) + '</option>';
        }).join("");

        var statusBadge = flag.enabled
            ? '<span class="badge success"><span class="dot"></span>on</span>'
            : '<span class="badge"><span class="dot"></span>off</span>';
        viewEl.innerHTML = [
            '<div class="page"><div class="page-head">',
            '  <div class="title-wrap"><h1 class="mono">' + esc(flag.key) + '</h1>',
            '    <span class="sub">' + esc(flag.name || "") + ' · ' + esc(flag.type) + ' · evaluated in <code>' + esc(currentEnv.key) + '</code></span>',
            '  </div><div class="actions">' + statusBadge + '</div>',
            '</div><div class="page-body"><div class="detail-grid"><div class="detail-main">',
            '<form id="flag-form" class="editor">',
            field("Name", '<input name="name" required value="' + esc(flag.name) + '" />'),
            field("Description", '<textarea name="description" rows="2">' + esc(flag.description || "") + '</textarea>'),
            '<div class="grid-2">',
            field("Type", '<input value="' + esc(flag.type) + '" readonly disabled />'),
            field("Enabled", '<label class="check"><input type="checkbox" name="enabled"' + (flag.enabled ? " checked" : "") + ' /> Master switch</label>'),
            '</div>',
            field("Default variant", '<select name="defaultVariantKey">' + variantOpts + '</select>'),
            field("Tags", '<input name="tags" value="' + esc((flag.tags || []).join(", ")) + '" placeholder="comma,separated" />'),
            '<h2>Variants</h2>',
            '<div class="variant-list">' + (flag.variants || []).map(renderVariantRow).join("") + '</div>',
            '<button type="button" class="btn outline xs" data-action="add-variant"><span class="ti-slot" data-ti="plus"></span> Add variant</button>',
            '<h2>Rules</h2>',
            renderRulesEditor(flag.rules || [], { kind: "flag", variants: flag.variants || [] }),
            '<button type="button" class="btn outline xs" data-action="add-rule"><span class="ti-slot" data-ti="plus"></span> Add rule</button>',
            '<div class="editor__footer">',
            '  <button type="submit" class="btn primary">Save flag</button>',
            '  <span class="save-msg" id="save-msg"></span>',
            '</div>',
            '</form>',
            renderPreviewPanel("flag", flag.key),
            '</div><aside class="detail-side">',
            '  <div class="side-card"><h3 class="side-h">Details</h3><dl class="side-dl">',
            '    <dt>Status</dt><dd>' + statusBadge + '</dd>',
            '    <dt>Type</dt><dd>' + esc(flag.type) + '</dd>',
            '    <dt>Default</dt><dd class="mono">' + esc(flag.defaultVariantKey || "—") + '</dd>',
            '    <dt>Variants</dt><dd>' + (flag.variants || []).length + '</dd>',
            '    <dt>Rules</dt><dd>' + (flag.rules || []).length + '</dd>',
            '    <dt>Created</dt><dd>' + (formatDate(flag.createdAt) || "—") + '</dd>',
            '    <dt>Updated</dt><dd>' + (formatDate(flag.updatedAt) || "—") + '</dd>',
            '  </dl></div>',
            '</aside></div></div></div>',
        ].join("\n");

        wireFlagEditor(flag);
        wirePreviewPanel("flag", flag.key);
        hydrateIcons(viewEl);
    }

    function renderVariantRow(v) {
        return '<div class="variant-row">'
            + '<input class="v-key" placeholder="key" value="' + esc(v.key) + '" />'
            + '<input class="v-name" placeholder="name" value="' + esc(v.name) + '" />'
            + '<input class="v-value" placeholder="value (JSON)" value="' + esc(JSON.stringify(v.value)) + '" />'
            + '<button type="button" class="icon-btn" data-action="remove-variant" aria-label="Remove">' + icon("x") + '</button>'
            + '</div>';
    }

    function wireFlagEditor(flag) {
        var form = document.getElementById("flag-form");
        // Wire any pre-existing split toggles (rules loaded with splits).
        Array.prototype.slice.call(form.querySelectorAll(".rule-card")).forEach(function (card) {
            var toggle = card.querySelector(".split-toggle");
            if (toggle) {
                toggle.dataset.wired = "1";
                toggle.addEventListener("change", function () {
                    card.querySelector(".single").classList.toggle("hidden", toggle.checked);
                    card.querySelector(".splits").classList.toggle("hidden", !toggle.checked);
                });
            }
        });
        form.addEventListener("click", function (e) {
            var action = e.target.closest("[data-action]")?.getAttribute("data-action");
            if (action === "add-variant") {
                var list = form.querySelector(".variant-list");
                list.insertAdjacentHTML("beforeend", renderVariantRow({ key: "", name: "", value: false }));
            } else if (action === "remove-variant") {
                e.target.closest(".variant-row").remove();
            } else if (action === "add-rule") {
                var rulesEl = form.querySelector(".rules-list");
                rulesEl.insertAdjacentHTML("beforeend", renderRuleCard({ id: cryptoId(), order: rulesEl.children.length, name: "", enabled: true, conditions: [], outcome: { variantKey: flag.defaultVariantKey } }, { kind: "flag", variants: flag.variants || [] }));
            } else {
                handleRuleAction(e, form, { kind: "flag", variants: flag.variants || [] });
            }
        });
        form.addEventListener("submit", function (e) {
            e.preventDefault();
            try {
                var body = collectFlagBody(form, flag);
                setMessage("loading", "Saving…");
                api("PUT", "/admin/flags/" + encodeURIComponent(flag.key) + "?env=" + encodeURIComponent(currentEnv.key), body)
                    .then(function (updated) { setMessage("success", "Saved."); renderFlagEditor(updated || body); })
                    .catch(function (err) {
                        if (err.kind === "auth") { showAuthPrompt(); return; }
                        setMessage("error", err.message);
                    });
            } catch (err) {
                setMessage("error", err.message);
            }
        });
    }

    function collectFlagBody(form, flag) {
        var body = {
            key: flag.key,
            name: form.elements["name"].value.trim(),
            description: form.elements["description"].value.trim() || null,
            type: flag.type,
            enabled: form.elements["enabled"].checked,
            defaultVariantKey: form.elements["defaultVariantKey"].value,
            variants: Array.prototype.slice.call(form.querySelectorAll(".variant-row")).map(function (row, idx) {
                var key = row.querySelector(".v-key").value.trim();
                var name = row.querySelector(".v-name").value.trim();
                var valueRaw = row.querySelector(".v-value").value;
                if (!key) { throw new Error("Variant #" + (idx + 1) + ": key is required."); }
                var value;
                try { value = JSON.parse(valueRaw); } catch (_) { throw new Error("Variant '" + key + "': value must be valid JSON."); }
                return { key: key, name: name || key, description: null, value: value };
            }),
            tags: parseCsv(form.elements["tags"].value),
            rules: collectRules(form, { kind: "flag" }),
        };
        if (!body.name) { throw new Error("Name is required."); }
        if (!body.variants.some(function (v) { return v.key === body.defaultVariantKey; })) {
            throw new Error("Default variant '" + body.defaultVariantKey + "' is not in the variants list.");
        }
        return body;
    }

    // ---------- Config editor ----------
    function renderConfigEditor(config) {
        viewEl.innerHTML = [
            '<div class="page"><div class="page-head">',
            '  <div class="title-wrap"><h1 class="mono">' + esc(config.key) + '</h1>',
            '    <span class="sub">' + esc(config.name || "") + ' · ' + esc(config.type) + ' · evaluated in <code>' + esc(currentEnv.key) + '</code></span>',
            '  </div><div class="actions"><span class="badge sq' + (config.type === "Json" ? " accent" : (config.type === "Bool" ? " info" : "")) + '">' + esc(config.type) + '</span></div>',
            '</div><div class="page-body"><div class="detail-grid"><div class="detail-main">',
            '<form id="config-form" class="editor">',
            field("Name", '<input name="name" required value="' + esc(config.name) + '" />'),
            field("Description", '<textarea name="description" rows="2">' + esc(config.description || "") + '</textarea>'),
            '<div class="grid-2">',
            field("Type", '<input value="' + esc(config.type) + '" readonly disabled />'),
            field("Default value (JSON)", '<input name="defaultValue" required value="' + esc(JSON.stringify(config.defaultValue)) + '" />'),
            '</div>',
            field("Tags", '<input name="tags" value="' + esc((config.tags || []).join(", ")) + '" placeholder="comma,separated" />'),
            '<h2>Rules</h2>',
            renderRulesEditor(config.rules || [], { kind: "config" }),
            '<button type="button" class="btn outline xs" data-action="add-rule"><span class="ti-slot" data-ti="plus"></span> Add rule</button>',
            '<div class="editor__footer">',
            '  <button type="submit" class="btn primary">Save config</button>',
            '  <span class="save-msg" id="save-msg"></span>',
            '</div>',
            '</form>',
            renderPreviewPanel("config", config.key),
            '</div><aside class="detail-side"><div class="side-card"><h3 class="side-h">Details</h3><dl class="side-dl">',
            '  <dt>Type</dt><dd>' + esc(config.type) + '</dd>',
            '  <dt>Default</dt><dd class="mono">' + esc(truncate(JSON.stringify(config.defaultValue), 24)) + '</dd>',
            '  <dt>Rules</dt><dd>' + (config.rules || []).length + '</dd>',
            '  <dt>Created</dt><dd>' + (formatDate(config.createdAt) || "—") + '</dd>',
            '  <dt>Updated</dt><dd>' + (formatDate(config.updatedAt) || "—") + '</dd>',
            '</dl></div></aside></div></div></div>',
        ].join("\n");

        hydrateIcons(viewEl);
        wirePreviewPanel("config", config.key);
        var form = document.getElementById("config-form");
        form.addEventListener("click", function (e) {
            var action = e.target.closest("[data-action]")?.getAttribute("data-action");
            if (action === "add-rule") {
                var rulesEl = form.querySelector(".rules-list");
                rulesEl.insertAdjacentHTML("beforeend", renderRuleCard({ id: cryptoId(), order: rulesEl.children.length, name: "", enabled: true, conditions: [], value: config.defaultValue }, { kind: "config" }));
            } else {
                handleRuleAction(e, form, { kind: "config" });
            }
        });
        form.addEventListener("submit", function (e) {
            e.preventDefault();
            try {
                var body = collectConfigBody(form, config);
                setMessage("loading", "Saving…");
                api("PUT", "/admin/configs/" + encodeURIComponent(config.key) + "?env=" + encodeURIComponent(currentEnv.key), body)
                    .then(function (updated) { setMessage("success", "Saved."); renderConfigEditor(updated || body); })
                    .catch(function (err) {
                        if (err.kind === "auth") { showAuthPrompt(); return; }
                        setMessage("error", err.message);
                    });
            } catch (err) {
                setMessage("error", err.message);
            }
        });
    }

    function collectConfigBody(form, config) {
        var defaultRaw = form.elements["defaultValue"].value;
        var defaultValue;
        try { defaultValue = JSON.parse(defaultRaw); }
        catch (_) { throw new Error("Default value must be valid JSON."); }
        return {
            key: config.key,
            name: form.elements["name"].value.trim(),
            description: form.elements["description"].value.trim() || null,
            type: config.type,
            defaultValue: defaultValue,
            tags: parseCsv(form.elements["tags"].value),
            rules: collectRules(form, { kind: "config" }),
        };
    }

    // ---------- Segment editor ----------
    function renderSegmentEditor(segment) {
        viewEl.innerHTML = [
            '<div class="page"><div class="page-head">',
            '  <div class="title-wrap"><h1 class="mono">' + esc(segment.key) + '</h1>',
            '    <span class="sub">' + esc(segment.name || "") + ' · reusable group in <code>' + esc(currentEnv.key) + '</code></span>',
            '  </div></div><div class="page-body"><div class="detail-grid"><div class="detail-main">',
            '<form id="segment-form" class="editor">',
            field("Name", '<input name="name" required value="' + esc(segment.name) + '" />'),
            field("Description", '<textarea name="description" rows="2">' + esc(segment.description || "") + '</textarea>'),
            '<h2>Conditions</h2>',
            '<div class="conditions-list">' + (segment.conditions || []).map(renderConditionRow).join("") + '</div>',
            '<button type="button" class="btn outline xs" data-action="add-condition"><span class="ti-slot" data-ti="plus"></span> Add condition</button>',
            '<div class="editor__footer">',
            '  <button type="submit" class="btn primary">Save segment</button>',
            '  <span class="save-msg" id="save-msg"></span>',
            '</div>',
            '</form>',
            '</div><aside class="detail-side"><div class="side-card"><h3 class="side-h">Details</h3><dl class="side-dl">',
            '  <dt>Conditions</dt><dd>' + (segment.conditions || []).length + '</dd>',
            '  <dt>Created</dt><dd>' + (formatDate(segment.createdAt) || "—") + '</dd>',
            '  <dt>Updated</dt><dd>' + (formatDate(segment.updatedAt) || "—") + '</dd>',
            '</dl><p class="muted" style="font-size:11px;margin:10px 0 0">Referenced from flag &amp; config rules via the <code>InSegment</code> operator.</p></div></aside></div></div></div>',
        ].join("\n");

        hydrateIcons(viewEl);
        var form = document.getElementById("segment-form");
        form.addEventListener("click", function (e) {
            var action = e.target.closest("[data-action]")?.getAttribute("data-action");
            if (action === "add-condition") {
                form.querySelector(".conditions-list").insertAdjacentHTML("beforeend", renderConditionRow({ attribute: "", operator: "Equals", value: "", negate: false }));
            } else if (action === "remove-condition") {
                e.target.closest(".condition-row").remove();
            }
        });
        form.addEventListener("submit", function (e) {
            e.preventDefault();
            try {
                var body = {
                    key: segment.key,
                    name: form.elements["name"].value.trim(),
                    description: form.elements["description"].value.trim() || null,
                    conditions: collectConditions(form.querySelector(".conditions-list")),
                };
                setMessage("loading", "Saving…");
                api("PUT", "/admin/segments/" + encodeURIComponent(segment.key) + "?env=" + encodeURIComponent(currentEnv.key), body)
                    .then(function (updated) { setMessage("success", "Saved."); renderSegmentEditor(updated || body); })
                    .catch(function (err) {
                        if (err.kind === "auth") { showAuthPrompt(); return; }
                        setMessage("error", err.message);
                    });
            } catch (err) {
                setMessage("error", err.message);
            }
        });
    }

    // ============================================================
    // Users + Roles (RBAC, M7) — not environment-scoped
    // ============================================================
    function userRow(u) {
        return '<tr data-key="' + esc(u.identifier) + '">'
            + '<td><div class="cell-keyname"><span class="cell-key" style="font-weight:500">' + esc(u.displayName || u.identifier) + '</span>'
            + '<span class="cell-name mono">' + esc(u.identifier) + '</span></div></td>'
            + '<td>' + esc(u.email || "—") + '</td>'
            + '<td>' + (u.disabled
                ? '<span class="badge"><span class="dot"></span>disabled</span>'
                : '<span class="badge success"><span class="dot"></span>active</span>') + '</td>'
            + '<td class="mono cell-modified">' + (formatDate(u.createdAt) || "—") + '</td>'
            + '</tr>';
    }
    function renderUserList() {
        var sub = "People with access — auto-provisioned on first sign-in, or created via the admin API.";
        viewEl.innerHTML = listPageShell("Users", sub, '<div class="empty"><p class="muted">Loading…</p></div>');
        api("GET", "/admin/users")
            .then(function (users) {
                users = Array.isArray(users) ? users : [];
                var body = users.length
                    ? '<div class="tbl-wrap"><table class="tbl"><thead><tr><th>Name / Identifier</th><th>Email</th>'
                        + '<th style="width:90px">Status</th><th style="width:140px">Created</th></tr></thead>'
                        + '<tbody class="list-tbody">' + users.map(userRow).join("") + '</tbody></table></div>'
                    : '<div class="empty"><p>No users yet.</p></div>';
                viewEl.innerHTML = listPageShell("Users", sub, body);
                hydrateIcons(viewEl);
                var tb = viewEl.querySelector(".list-tbody");
                if (tb) {
                    tb.addEventListener("click", function (e) {
                        if (e.target.closest("a, button, input")) { return; }
                        var tr = e.target.closest("tr[data-key]");
                        if (tr) { navigate("/users/" + encodeURIComponent(tr.getAttribute("data-key"))); }
                    });
                }
            })
            .catch(handleErrOnView("Users"));
    }

    function renderUserDetail(identifier) {
        viewEl.innerHTML = listPageShell("User", esc(identifier), '<div class="empty"><p class="muted">Loading…</p></div>');
        Promise.all([
            api("GET", "/admin/users/" + encodeURIComponent(identifier)),
            api("GET", "/admin/users/" + encodeURIComponent(identifier) + "/effective-access"),
        ]).then(function (res) {
            var user = res[0];
            var access = res[1];
            var roleRows = (access.roles || []).map(function (r) {
                return '<tr><td>' + code(r.key) + '</td><td>' + esc(r.name) + '</td><td>' + esc(r.via) + '</td><td>' + (r.environmentId ? code(truncate(r.environmentId, 8)) : '<span class="muted">all envs</span>') + '</td></tr>';
            }).join("");
            var perms = (access.permissions || []);
            viewEl.innerHTML = [
                '<div class="page"><div class="page-head"><div class="title-wrap">',
                '  <h1>' + esc(user.displayName || user.identifier) + '</h1>',
                '  <span class="sub mono">' + esc(user.identifier) + '</span>',
                '</div><div class="actions">' + (user.disabled
                    ? '<span class="badge"><span class="dot"></span>disabled</span>'
                    : '<span class="badge success"><span class="dot"></span>active</span>') + '</div></div>',
                '<div class="page-body"><div class="detail-grid"><div class="detail-main">',
                '  <div class="card-pad"><h2>Effective access</h2>',
                '  <p class="muted" style="font-size:12px">The union of every role granted by a matching assignment (direct or via a group), in the default project.</p>',
                roleRows
                    ? '  <div class="tbl-wrap"><table class="tbl"><thead><tr><th>Role</th><th>Name</th><th>Via</th><th>Environment</th></tr></thead><tbody>' + roleRows + '</tbody></table></div>'
                    : '  <div class="empty"><p>No role assignments in this project.</p></div>',
                '  </div>',
                '  <div class="card-pad"><h2>Permissions <span class="muted">(' + perms.length + ')</span></h2>',
                perms.length
                    ? '  <div class="tag-list">' + perms.map(function (p) { return '<span class="tag muted">' + esc(p) + '</span>'; }).join("") + '</div>'
                    : '  <div class="empty"><p>No effective permissions in this scope.</p></div>',
                '  </div>',
                '</div><aside class="detail-side"><div class="side-card"><h3 class="side-h">Identity</h3><dl class="side-dl">',
                '  <dt>Identifier</dt><dd class="mono">' + esc(truncate(user.identifier, 22)) + '</dd>',
                '  <dt>Email</dt><dd>' + esc(user.email || "—") + '</dd>',
                '  <dt>Status</dt><dd>' + (user.disabled ? "disabled" : "active") + '</dd>',
                '  <dt>Roles</dt><dd>' + (access.roles || []).length + '</dd>',
                '</dl></div></aside></div></div></div>',
            ].join("\n");
            hydrateIcons(viewEl);
        }).catch(handleErrOnView("User: " + identifier));
    }

    function renderRoleList() {
        var sub = "Permission sets. System roles are immutable; clone one to make an editable custom role.";
        viewEl.innerHTML = listPageShell("Roles", sub, '<div class="empty"><p class="muted">Loading…</p></div>');
        api("GET", "/admin/roles")
            .then(function (roles) {
                roles = Array.isArray(roles) ? roles : [];
                var rows = roles.map(function (r) {
                    return '<tr>'
                        + '<td class="mono cell-key">' + esc(r.key) + '</td>'
                        + '<td>' + esc(r.name) + '</td>'
                        + '<td>' + (r.isSystem ? '<span class="badge info">system</span>' : '<span class="badge purple">custom</span>') + '</td>'
                        + '<td class="num">' + (r.permissions || []).length + '</td>'
                        + '<td><span class="muted" style="font-size:11px">' + esc(truncate((r.permissions || []).join(", "), 70)) + '</span></td>'
                        + '</tr>';
                }).join("");
                var body = roles.length
                    ? '<div class="tbl-wrap"><table class="tbl"><thead><tr><th style="width:120px">Key</th><th>Name</th>'
                        + '<th style="width:90px">Kind</th><th style="width:110px">Permissions</th><th>Sample</th></tr></thead>'
                        + '<tbody>' + rows + '</tbody></table></div>'
                    : '<div class="empty"><p>No roles.</p></div>';
                viewEl.innerHTML = listPageShell("Roles", sub, body);
                hydrateIcons(viewEl);
            })
            .catch(handleErrOnView("Roles"));
    }

    // ============================================================
    // Groups (Access): bundle users for a single role assignment
    // ============================================================
    function renderGroupList() {
        viewEl.innerHTML = listPageShell("Groups", "Loading…", '<div class="empty"><p class="muted">Loading…</p></div>');
        api("GET", "/admin/groups")
            .then(function (groups) {
                groups = Array.isArray(groups) ? groups : [];
                var rows = groups.map(function (g) {
                    return '<tr data-key="' + esc(g.key) + '">'
                        + '<td><div class="cell-keyname"><span class="mono cell-key">' + esc(g.key) + '</span>'
                        + '<span class="cell-name">' + esc(g.name || "") + '</span></div></td>'
                        + '<td class="num">' + ((g.memberUserIds || []).length) + '</td>'
                        + '<td><span class="muted" style="font-size:11px">' + esc(truncate(g.description || "", 60)) + '</span></td>'
                        + '<td class="mono cell-modified">' + (formatDate(g.updatedAt) || "—") + '</td>'
                        + '</tr>';
                }).join("");
                viewEl.innerHTML = [
                    '<div class="page"><div class="page-head"><div class="title-wrap"><h1>Groups</h1>',
                    '  <span class="sub">Bundle users so a single role assignment grants a role to every member at once.</span>',
                    '</div></div><div class="page-body"><div class="detail-grid"><div class="detail-main">',
                    groups.length
                        ? '<div class="tbl-wrap"><table class="tbl"><thead><tr><th>Group</th><th>Members</th><th>Description</th><th>Modified</th></tr></thead><tbody class="grp-tbody">' + rows + '</tbody></table></div>'
                        : '<div class="empty"><p>No groups yet. Create one on the right.</p></div>',
                    '</div><aside class="detail-side"><div class="side-card"><h3 class="side-h">New group</h3>',
                    '  <form id="grp-form" class="editor">',
                    field("Key", '<input name="key" required placeholder="platform-team" />'),
                    field("Name", '<input name="name" placeholder="Platform Team" />'),
                    field("Description", '<input name="description" placeholder="optional" />'),
                    '  <div class="editor__footer"><button type="submit" class="btn primary">Create group</button><span class="save-msg" id="grp-msg"></span></div>',
                    '  </form>',
                    '</div></aside></div></div></div>',
                ].join("\n");

                var tbody = viewEl.querySelector(".grp-tbody");
                if (tbody) {
                    tbody.addEventListener("click", function (e) {
                        if (e.target.closest("input, button, a")) { return; }
                        var tr = e.target.closest("tr[data-key]");
                        if (tr) { navigate("/groups/" + encodeURIComponent(tr.getAttribute("data-key"))); }
                    });
                }
                document.getElementById("grp-form").addEventListener("submit", function (e) {
                    e.preventDefault();
                    var f = e.target;
                    var msg = document.getElementById("grp-msg");
                    setMessageOn(msg, "loading", "Creating…");
                    api("POST", "/admin/groups", {
                        key: f.key.value.trim(),
                        name: f.name.value.trim() || null,
                        description: f.description.value.trim() || null,
                    }).then(function (g) { navigate("/groups/" + encodeURIComponent(g.key)); })
                      .catch(function (err) { if (err.kind === "auth") { showAuthPrompt(); return; } setMessageOn(msg, "error", err.message); });
                });
            })
            .catch(handleErrOnView("Groups"));
    }

    function renderGroupDetail(key) {
        viewEl.innerHTML = listPageShell("Group", esc(key), '<div class="empty"><p class="muted">Loading…</p></div>');
        api("GET", "/admin/groups/" + encodeURIComponent(key))
            .then(function (g) {
                var members = (g.memberUserIds || []).join("\n");
                viewEl.innerHTML = [
                    '<div class="page"><div class="page-head"><div class="title-wrap"><h1>' + esc(g.name || g.key) + '</h1>',
                    '  <span class="sub">group <code>' + esc(g.key) + '</code> · ' + ((g.memberUserIds || []).length) + ' member(s)</span>',
                    '</div><div class="actions"><button type="button" class="btn outline xs danger" data-grp="delete">Delete</button><span class="save-msg" id="grp-msg"></span></div></div>',
                    '<div class="page-body"><div class="detail-grid"><div class="detail-main">',
                    '  <form id="grp-edit" class="editor card-pad">',
                    field("Name", '<input name="name" value="' + esc(g.name || "") + '" />'),
                    field("Description", '<input name="description" value="' + esc(g.description || "") + '" />'),
                    field("Member user IDs", '<textarea name="members" rows="6" spellcheck="false" placeholder="one user GUID per line">' + esc(members) + '</textarea>'),
                    '  <div class="editor__footer"><button type="submit" class="btn primary">Save</button></div>',
                    '  </form>',
                    '</div><aside class="detail-side"><div class="side-card"><h3 class="side-h">Details</h3><dl class="side-dl">',
                    '  <dt>Key</dt><dd class="mono">' + esc(g.key) + '</dd>',
                    '  <dt>Members</dt><dd>' + ((g.memberUserIds || []).length) + '</dd>',
                    '  <dt>Created</dt><dd>' + (formatDate(g.createdAt) || "—") + '</dd>',
                    '  <dt>Updated</dt><dd>' + (formatDate(g.updatedAt) || "—") + '</dd>',
                    '</dl></div></aside></div></div></div>',
                ].join("\n");

                var msg = document.getElementById("grp-msg");
                document.getElementById("grp-edit").addEventListener("submit", function (e) {
                    e.preventDefault();
                    var f = e.target;
                    var ids = f.members.value.split(/[\s,]+/).map(function (s) { return s.trim(); }).filter(Boolean);
                    setMessageOn(msg, "loading", "Saving…");
                    api("PUT", "/admin/groups/" + encodeURIComponent(key), {
                        key: key,
                        name: f.name.value.trim() || null,
                        description: f.description.value.trim() || null,
                        memberUserIds: ids,
                    }).then(function () { setMessageOn(msg, "success", "Saved."); })
                      .catch(function (err) { if (err.kind === "auth") { showAuthPrompt(); return; } setMessageOn(msg, "error", err.message); });
                });
                viewEl.querySelector('[data-grp="delete"]').addEventListener("click", function () {
                    if (!window.confirm("Delete group '" + key + "'? Role assignments targeting this group will stop granting access.")) { return; }
                    api("DELETE", "/admin/groups/" + encodeURIComponent(key))
                        .then(function () { navigate("/groups"); })
                        .catch(function (err) { if (err.kind === "auth") { showAuthPrompt(); return; } setMessageOn(msg, "error", err.message); });
                });
            })
            .catch(handleErrOnView("Group: " + key));
    }

    // ============================================================
    // API keys (Access): mint (token shown once) / list / revoke
    // ============================================================
    function apiKeyScopeBadge(scope) {
        var v = scope === "AdminWrite" ? " purple" : " info";
        return '<span class="badge' + v + '">' + esc(scope) + '</span>';
    }

    function renderApiKeyList() {
        if (!currentEnv) { viewEl.innerHTML = listEmptyEnv("API keys"); return; }
        var envKey = currentEnv.key;
        viewEl.innerHTML = [
            '<div class="page"><div class="page-head"><div class="title-wrap"><h1>API keys</h1>',
            '  <span class="sub">Bearer tokens scoped to <code>' + esc(envKey) + '</code>. The plaintext token is shown exactly once, at creation; only an Argon2id hash is stored.</span>',
            '</div></div><div class="page-body">',
            '  <div id="apikey-reveal"></div>',
            '  <div class="card-pad"><h2>Mint key</h2>',
            '  <form id="apikey-form" class="row-form">',
            '    <label class="field"><span class="field__label">Name</span><input name="name" required placeholder="ci-pipeline" /></label>',
            '    <label class="field"><span class="field__label">Scope</span><select name="scope"><option value="AdminWrite">AdminWrite</option><option value="SdkRead">SdkRead</option></select></label>',
            '    <label class="field"><span class="field__label">Bind to user</span><input name="user" placeholder="optional identifier, e.g. alice@example.com" /></label>',
            '    <div class="row-form__action"><button type="submit" class="btn primary">Mint key</button><span class="save-msg" id="apikey-msg"></span></div>',
            '  </form></div>',
            '  <div class="tbl-wrap"><table class="tbl"><thead><tr><th>Name</th><th>Prefix</th><th>Scope</th><th>User</th><th>Status</th><th>Created</th><th>Last used</th><th></th></tr></thead><tbody id="apikey-tbody"><tr><td colspan="8" class="muted" style="padding:18px;text-align:center">Loading…</td></tr></tbody></table></div>',
            '</div></div>',
        ].join("\n");
        hydrateIcons(viewEl);

        var tbody = document.getElementById("apikey-tbody");
        function paint(keys) {
            keys = Array.isArray(keys) ? keys : [];
            if (!keys.length) {
                tbody.innerHTML = '<tr><td colspan="8" class="muted" style="padding:18px;text-align:center">No API keys in this environment yet. Mint one on the right.</td></tr>';
                return;
            }
            tbody.innerHTML = keys.map(function (k) {
                var status = k.revoked
                    ? '<span class="badge danger">revoked</span>'
                    : '<span class="badge success"><span class="dot"></span>active</span>';
                return '<tr>'
                    + '<td>' + esc(k.name) + '</td>'
                    + '<td>' + code(k.prefix) + '</td>'
                    + '<td>' + apiKeyScopeBadge(k.scope) + '</td>'
                    + '<td class="mono">' + (k.userId ? esc(truncate(k.userId, 8)) : '<span class="muted">—</span>') + '</td>'
                    + '<td>' + status + '</td>'
                    + '<td class="mono cell-modified">' + (formatDate(k.createdAt) || "—") + '</td>'
                    + '<td class="mono cell-modified">' + (k.lastUsedAt ? formatDate(k.lastUsedAt) : '<span class="muted">never</span>') + '</td>'
                    + '<td class="col-actions">' + (k.revoked ? "" : '<button type="button" class="btn outline xs danger" data-revoke="' + esc(k.id) + '">Revoke</button>') + '</td>'
                    + '</tr>';
            }).join("");
            Array.prototype.slice.call(tbody.querySelectorAll("[data-revoke]")).forEach(function (btn) {
                btn.addEventListener("click", function () {
                    var id = btn.getAttribute("data-revoke");
                    if (!window.confirm("Revoke this API key? Requests using it start failing immediately.")) { return; }
                    api("POST", "/admin/apikeys/" + encodeURIComponent(id) + "/revoke")
                        .then(refresh)
                        .catch(function (err) { if (err.kind === "auth") { showAuthPrompt(); return; } window.alert(err.message); });
                });
            });
        }
        function refresh() {
            return api("GET", "/admin/apikeys?environmentKey=" + encodeURIComponent(envKey))
                .then(paint)
                .catch(handleErrOnView("API keys"));
        }

        refresh();
        document.getElementById("apikey-form").addEventListener("submit", function (e) {
            e.preventDefault();
            var f = e.target;
            var msg = document.getElementById("apikey-msg");
            setMessageOn(msg, "loading", "Minting…");
            api("POST", "/admin/apikeys", {
                name: f.name.value.trim(),
                scope: f.scope.value,
                userIdentifier: f.user.value.trim() || null,
                environmentKey: envKey,
            }).then(function (res) {
                var reveal = document.getElementById("apikey-reveal");
                if (reveal) {
                    reveal.innerHTML = '<div class="card-pad" style="border-color:var(--warn-border);background:var(--warn-bg);margin-bottom:12px">'
                        + '<strong>Copy this token now — it will not be shown again.</strong>'
                        + '<pre class="cr-json" style="margin:8px 0 0">' + esc(res.token) + '</pre></div>';
                }
                setMessageOn(msg, "success", "Minted.");
                f.reset();
                return refresh();
            }).catch(function (err) { if (err.kind === "auth") { showAuthPrompt(); return; } setMessageOn(msg, "error", err.message); });
        });
    }

    // ============================================================
    // Projects (System): top-level scope grouping environments
    // ============================================================
    function renderProjectList() {
        viewEl.innerHTML = listPageShell("Projects", "Loading…", '<div class="empty"><p class="muted">Loading…</p></div>');
        api("GET", "/admin/projects")
            .then(function (projects) {
                projects = Array.isArray(projects) ? projects : [];
                var rows = projects.map(function (p) {
                    return '<tr data-key="' + esc(p.key) + '">'
                        + '<td><div class="cell-keyname"><span class="mono cell-key">' + esc(p.key) + '</span>'
                        + '<span class="cell-name">' + esc(p.name || "") + '</span></div></td>'
                        + '<td>' + (p.isDefault ? '<span class="badge info">default</span>' : '<span class="muted" style="font-size:11px">—</span>') + '</td>'
                        + '<td><span class="muted" style="font-size:11px">' + esc(truncate(p.description || "", 60)) + '</span></td>'
                        + '<td class="mono cell-modified">' + (formatDate(p.createdAt) || "—") + '</td>'
                        + '</tr>';
                }).join("");
                viewEl.innerHTML = [
                    '<div class="page"><div class="page-head"><div class="title-wrap"><h1>Projects</h1>',
                    '  <span class="sub">A project is the top-level scope that groups a set of environments.</span>',
                    '</div></div><div class="page-body"><div class="detail-grid"><div class="detail-main">',
                    projects.length
                        ? '<div class="tbl-wrap"><table class="tbl"><thead><tr><th>Project</th><th>Default</th><th>Description</th><th>Created</th></tr></thead><tbody class="prj-tbody">' + rows + '</tbody></table></div>'
                        : '<div class="empty"><p>No projects yet. Create one on the right.</p></div>',
                    '</div><aside class="detail-side"><div class="side-card"><h3 class="side-h">New project</h3>',
                    '  <form id="prj-form" class="editor">',
                    field("Key", '<input name="key" required placeholder="mobile" />'),
                    field("Name", '<input name="name" placeholder="Mobile" />'),
                    field("Description", '<input name="description" placeholder="optional" />'),
                    '  <div class="editor__footer"><button type="submit" class="btn primary">Create project</button><span class="save-msg" id="prj-msg"></span></div>',
                    '  </form>',
                    '</div></aside></div></div></div>',
                ].join("\n");

                var tbody = viewEl.querySelector(".prj-tbody");
                if (tbody) {
                    tbody.addEventListener("click", function (e) {
                        if (e.target.closest("input, button, a")) { return; }
                        var tr = e.target.closest("tr[data-key]");
                        if (tr) { navigate("/projects/" + encodeURIComponent(tr.getAttribute("data-key"))); }
                    });
                }
                document.getElementById("prj-form").addEventListener("submit", function (e) {
                    e.preventDefault();
                    var f = e.target;
                    var msg = document.getElementById("prj-msg");
                    setMessageOn(msg, "loading", "Creating…");
                    api("POST", "/admin/projects", {
                        key: f.key.value.trim(),
                        name: f.name.value.trim() || null,
                        description: f.description.value.trim() || null,
                    }).then(function (p) { navigate("/projects/" + encodeURIComponent(p.key)); })
                      .catch(function (err) { if (err.kind === "auth") { showAuthPrompt(); return; } setMessageOn(msg, "error", err.message); });
                });
            })
            .catch(handleErrOnView("Projects"));
    }

    function renderProjectDetail(key) {
        viewEl.innerHTML = listPageShell("Project", esc(key), '<div class="empty"><p class="muted">Loading…</p></div>');
        api("GET", "/admin/projects/" + encodeURIComponent(key))
            .then(function (p) {
                viewEl.innerHTML = [
                    '<div class="page"><div class="page-head"><div class="title-wrap"><h1>' + esc(p.name || p.key) + '</h1>',
                    '  <span class="sub">project <code>' + esc(p.key) + '</code>' + (p.isDefault ? ' · default' : '') + '</span>',
                    '</div><div class="actions"><span class="save-msg" id="prj-msg"></span></div></div>',
                    '<div class="page-body"><div class="detail-grid"><div class="detail-main">',
                    '  <form id="prj-edit" class="editor card-pad">',
                    field("Name", '<input name="name" value="' + esc(p.name || "") + '" />'),
                    field("Description", '<input name="description" value="' + esc(p.description || "") + '" />'),
                    '  <div class="editor__footer"><button type="submit" class="btn primary">Save</button></div>',
                    '  </form>',
                    '</div><aside class="detail-side"><div class="side-card"><h3 class="side-h">Details</h3><dl class="side-dl">',
                    '  <dt>Key</dt><dd class="mono">' + esc(p.key) + '</dd>',
                    '  <dt>Default</dt><dd>' + (p.isDefault ? "yes" : "no") + '</dd>',
                    '  <dt>Created</dt><dd>' + (formatDate(p.createdAt) || "—") + '</dd>',
                    '</dl></div></aside></div></div></div>',
                ].join("\n");

                var msg = document.getElementById("prj-msg");
                document.getElementById("prj-edit").addEventListener("submit", function (e) {
                    e.preventDefault();
                    var f = e.target;
                    setMessageOn(msg, "loading", "Saving…");
                    api("PUT", "/admin/projects/" + encodeURIComponent(key), {
                        key: key,
                        name: f.name.value.trim() || null,
                        description: f.description.value.trim() || null,
                    }).then(function () { setMessageOn(msg, "success", "Saved."); })
                      .catch(function (err) { if (err.kind === "auth") { showAuthPrompt(); return; } setMessageOn(msg, "error", err.message); });
                });
            })
            .catch(handleErrOnView("Project: " + key));
    }

    // ============================================================
    // Experiments (M9): list, detail with analytics + bar charts
    // ============================================================
    function experimentStatus(exp) {
        if (exp.isActive) { return '<span class="badge success"><span class="dot"></span>running</span>'; }
        if (exp.stoppedAt) { return '<span class="badge">stopped</span>'; }
        return '<span class="badge warn">draft</span>';
    }

    function renderExperimentList() {
        var sub = "A/B experiments layered on a flag. Start one to collect exposures; track conversions via <code>IEventClient.TrackAsync</code>.";
        if (!currentEnv) { viewEl.innerHTML = listEmptyEnv("Experiments"); return; }
        viewEl.innerHTML = listPageShell("Experiments", sub, '<div class="empty"><p class="muted">Loading…</p></div>');
        api("GET", "/admin/experiments?env=" + encodeURIComponent(currentEnv.key))
            .then(function (rows) {
                rows = Array.isArray(rows) ? rows : [];
                var body;
                if (rows.length === 0) {
                    body = '<div class="empty"><p>No experiments in this environment yet.</p></div>';
                } else {
                    var trs = rows.map(function (e) {
                        return '<tr data-key="' + esc(e.key) + '">'
                            + '<td><div class="cell-keyname"><span class="mono cell-key">' + esc(e.key) + '</span><span class="cell-name">' + esc(e.name || "") + '</span></div></td>'
                            + '<td class="mono">' + esc(e.flagKey) + '</td>'
                            + '<td>' + experimentStatus(e) + '</td>'
                            + '<td class="num">' + (e.metricKeys || []).length + '</td>'
                            + '<td class="mono cell-modified">' + (e.startedAt ? formatDate(e.startedAt) : "—") + '</td>'
                            + '</tr>';
                    }).join("");
                    body = '<div class="tbl-wrap"><table class="tbl"><thead><tr><th>Key / Name</th><th>Flag</th>'
                        + '<th style="width:90px">Status</th><th style="width:80px">Metrics</th><th style="width:140px">Started</th></tr></thead>'
                        + '<tbody class="list-tbody">' + trs + '</tbody></table></div>';
                }
                viewEl.innerHTML = listPageShell("Experiments", sub, body);
                hydrateIcons(viewEl);
                var tb = viewEl.querySelector(".list-tbody");
                if (tb) {
                    tb.addEventListener("click", function (e) {
                        if (e.target.closest("a, button, input")) { return; }
                        var tr = e.target.closest("tr[data-key]");
                        if (tr) { navigate("/experiments/" + encodeURIComponent(tr.getAttribute("data-key"))); }
                    });
                }
            })
            .catch(handleErrOnView("Experiments"));
    }

    function renderExperimentDetail(key) {
        if (!currentEnv) { return; }
        viewEl.innerHTML = detailLoadingShell("Experiment", key);
        var envQ = "?env=" + encodeURIComponent(currentEnv.key);
        Promise.all([
            api("GET", "/admin/experiments/" + encodeURIComponent(key) + envQ),
            api("GET", "/admin/experiments/" + encodeURIComponent(key) + "/analytics" + envQ).catch(function () { return null; }),
        ]).then(function (res) {
            var exp = res[0];
            var analytics = res[1];
            var startStop = exp.isActive
                ? '<button type="button" class="btn outline xs" data-exp="stop">Stop</button>'
                : '<button type="button" class="btn primary xs" data-exp="start">' + (exp.stoppedAt ? "Restart" : "Start") + '</button>';

            viewEl.innerHTML = [
                '<div class="page"><div class="page-head"><div class="title-wrap"><h1 class="mono">' + esc(exp.key) + '</h1>',
                '  <span class="sub">' + esc(exp.name || "") + ' · flag <code>' + esc(exp.flagKey) + '</code> · ' + esc(currentEnv.key) + '</span>',
                '</div><div class="actions">' + experimentStatus(exp) + startStop + '<span class="save-msg" id="exp-msg"></span></div></div>',
                '<div class="page-body"><div class="detail-grid"><div class="detail-main">',
                '  <div class="card-pad"><h2>Analytics</h2>' + renderExperimentAnalytics(exp, analytics) + '</div>',
                '</div><aside class="detail-side"><div class="side-card"><h3 class="side-h">Setup</h3><dl class="side-dl">',
                '  <dt>Flag</dt><dd><a class="mono" data-link="/flags/' + encodeURIComponent(exp.flagKey) + '" href="' + esc(mountPath) + '/flags/' + encodeURIComponent(exp.flagKey) + '">' + esc(exp.flagKey) + '</a></dd>',
                '  <dt>Status</dt><dd>' + experimentStatus(exp) + '</dd>',
                '  <dt>Sticky</dt><dd>' + (exp.stickyAssignments ? "yes" : "no") + '</dd>',
                '  <dt>Metrics</dt><dd>' + (exp.metricKeys || []).length + '</dd>',
                '  <dt>Started</dt><dd>' + (exp.startedAt ? formatDate(exp.startedAt) : "—") + '</dd>',
                '  <dt>Stopped</dt><dd>' + (exp.stoppedAt ? formatDate(exp.stoppedAt) : "—") + '</dd>',
                '</dl>',
                exp.hypothesis ? '<p class="muted" style="font-size:11px;margin:10px 0 0">' + esc(exp.hypothesis) + '</p>' : '',
                '</div></aside></div></div></div>',
            ].join("\n");

            wireExperimentDetail(exp);
            hydrateIcons(viewEl);
        }).catch(handleErrOnView("Experiment: " + key));
    }

    function renderExperimentAnalytics(exp, a) {
        if (!a || !a.variants || a.variants.length === 0) {
            return '<div class="empty"><p>No exposures recorded yet.</p>'
                + '<p class="muted">' + (exp.isActive ? "Evaluate the flag from an SDK client to start collecting data." : "Start the experiment to begin collecting exposures.") + '</p></div>';
        }

        var metricKeys = exp.metricKeys || [];
        var maxExposed = a.variants.reduce(function (mx, v) { return Math.max(mx, v.exposedSubjects || 0); }, 0) || 1;

        // Exposures by variant.
        var exposureBars = a.variants.map(function (v) {
            var pct = Math.round(((v.exposedSubjects || 0) / maxExposed) * 100);
            return barRow(v.variantKey, pct, (v.exposedSubjects || 0) + " subject(s)", "bar--exposure");
        }).join("");

        var sections = [
            '<div class="card">',
            '<div class="muted exp-total">' + a.totalExposedSubjects + ' subject(s) · ' + a.totalExposureEvents + ' exposure event(s)</div>',
            '<h3 class="preview-h3">Exposures by variant</h3>',
            '<div class="bar-chart">' + exposureBars + '</div>',
            '</div>',
        ];

        // One conversion-rate chart per metric.
        metricKeys.forEach(function (metric) {
            var rows = a.variants.map(function (v) {
                var m = (v.metrics || []).find(function (x) { return x.metricKey === metric; });
                var rate = m ? m.conversionRate : 0;
                var pct = Math.round(rate * 1000) / 10; // one decimal
                var convs = m ? m.conversions : 0;
                return barRow(v.variantKey, Math.min(pct, 100), pct + "% (" + convs + "/" + (v.exposedSubjects || 0) + ")", "bar--conversion");
            }).join("");
            sections.push(
                '<div class="card">'
                + '<h3 class="preview-h3">Conversion rate — ' + code(metric) + '</h3>'
                + '<div class="bar-chart">' + rows + '</div>'
                + '</div>');
        });

        return sections.join("\n");
    }

    function barRow(label, pct, valueText, modifier) {
        var width = Math.max(0, Math.min(100, pct));
        return '<div class="bar-row">'
            + '<div class="bar-row__label">' + esc(label) + '</div>'
            + '<div class="bar-row__track"><div class="bar ' + modifier + '" style="width:' + width + '%" role="img" aria-label="' + esc(label + ": " + valueText) + '"></div></div>'
            + '<div class="bar-row__value">' + esc(valueText) + '</div>'
            + '</div>';
    }

    function wireExperimentDetail(exp) {
        var msg = document.getElementById("exp-msg");
        Array.prototype.slice.call(document.querySelectorAll("[data-exp]")).forEach(function (btn) {
            btn.addEventListener("click", function () {
                var action = btn.getAttribute("data-exp");
                btn.disabled = true;
                setMessageOn(msg, "loading", action === "start" ? "Starting…" : "Stopping…");
                api("POST", "/admin/experiments/" + encodeURIComponent(exp.key) + "/" + action + "?env=" + encodeURIComponent(currentEnv.key))
                    .then(function () { renderExperimentDetail(exp.key); })
                    .catch(function (err) {
                        btn.disabled = false;
                        if (err.kind === "auth") { showAuthPrompt(); return; }
                        setMessageOn(msg, "error", err.message);
                    });
            });
        });
    }

    // ============================================================
    // Approval workflow (M8): Inbox, change detail, policy editor
    // ============================================================
    function inboxChangeCard(c) {
        var approvals = (c.approvals || []).length;
        var statusLine = approvals + ' approval' + (approvals === 1 ? '' : 's') + ' so far';
        return '<div class="inbox-card urgent" data-cr="' + esc(c.id) + '">'
            + '<span class="ic info"><span class="ti-slot" data-ti="git-pull-request"></span></span>'
            + '<div class="main">'
            + '  <div class="row1"><span class="title">' + esc(c.action || "Change") + '<span class="key">' + esc(c.entityKey || "") + '</span></span>'
            + (c.entityType ? '<span class="badge sq">' + esc(c.entityType) + '</span>' : '') + '</div>'
            + '  <div class="row2"><span>' + code(truncate(c.authorUserId || "—", 8)) + '</span><span class="dot-sep"></span>'
            + '<span>' + (formatDate(c.createdAt) || "—") + '</span></div>'
            + '  <div class="status-line"><span class="ti-slot" data-ti="clock"></span>' + esc(statusLine) + '</div>'
            + '</div>'
            + '<div class="actions"><a class="btn outline xs" data-link="/inbox/' + encodeURIComponent(c.id) + '">Review</a></div>'
            + '</div>';
    }
    function inboxUpgradeCard(u) {
        return '<div class="inbox-card mine">'
            + '<span class="ic purple"><span class="ti-slot" data-ti="user-shield"></span></span>'
            + '<div class="main">'
            + '  <div class="row1"><span class="title">Role upgrade<span class="key">' + esc(truncate(u.userId || "", 12)) + '</span></span>'
            + '<span class="badge warn">pending</span></div>'
            + '  <div class="row2"><span>' + esc(truncate(u.justification || "No justification", 80)) + '</span><span class="dot-sep"></span>'
            + '<span>' + (formatDate(u.createdAt) || "—") + '</span></div>'
            + '</div></div>';
    }
    function inboxGroup(label, count, cardsHtml, emptyText) {
        return '<div class="inbox-group">'
            + '<div class="inbox-group-head">' + esc(label) + ' <span class="count">' + count + '</span></div>'
            + (cardsHtml || '<div class="empty" style="padding:18px"><p class="muted">' + esc(emptyText) + '</p></div>')
            + '</div>';
    }
    function renderInbox() {
        viewEl.innerHTML = listPageShell("Inbox", "Everything that needs your attention — approvals and role-upgrade requests.",
            '<div class="empty"><p class="muted">Loading…</p></div>');
        Promise.all([
            api("GET", "/admin/changes?status=Pending").catch(function () { return []; }),
            api("GET", "/admin/role-upgrade-requests?status=Pending").catch(function () { return []; }),
        ]).then(function (res) {
            var changes = Array.isArray(res[0]) ? res[0] : [];
            var upgrades = Array.isArray(res[1]) ? res[1] : [];
            var body = [
                inboxGroup("Awaiting your approval", changes.length, changes.map(inboxChangeCard).join(""), "Nothing awaiting approval."),
                inboxGroup("Role upgrade requests", upgrades.length, upgrades.map(inboxUpgradeCard).join(""), "No pending role-upgrade requests."),
            ].join("");
            viewEl.innerHTML = listPageShell("Inbox", "Everything that needs your attention — approvals and role-upgrade requests.",
                '<div class="inbox-list">' + body + '</div>');
            hydrateIcons(viewEl);
            var list = viewEl.querySelector(".inbox-list");
            if (list) {
                list.addEventListener("click", function (e) {
                    if (e.target.closest("a, button")) { return; }
                    var card = e.target.closest(".inbox-card[data-cr]");
                    if (card) { navigate("/inbox/" + encodeURIComponent(card.getAttribute("data-cr"))); }
                });
            }
        }).catch(handleErrOnView("Inbox"));
    }

    function crStatusBadge(status) {
        var s = String(status == null ? "" : status);
        var v = (s === "Approved" || s === "Applied") ? " success" : (s === "Rejected" ? " danger" : (s === "Pending" ? " warn" : ""));
        return '<span class="badge' + v + '">' + esc(s || "—") + '</span>';
    }

    function renderChangeDetail(id) {
        viewEl.innerHTML = listPageShell("Change request", "Loading…", '<div class="empty"><p class="muted">Loading…</p></div>');
        api("GET", "/admin/changes/" + encodeURIComponent(id))
            .then(function (c) {
                var actions = changeActionButtons(c);
                var comments = (c.comments || []).map(function (m) {
                    return '<div class="cr-comment"><div class="muted">' + code(truncate(m.authorUserId, 8)) + ' · ' + formatDate(m.at) + '</div><div>' + esc(m.body) + '</div></div>';
                }).join("");
                var approvals = (c.approvals || []).map(function (a) {
                    return '<li>' + badge(a.decision) + ' ' + code(truncate(a.approverUserId, 8)) + (a.comment ? ' — ' + esc(a.comment) : '') + '</li>';
                }).join("");
                viewEl.innerHTML = [
                    '<div class="page"><div class="page-head"><div class="title-wrap"><h1>' + badge(c.entityType) + ' <span class="mono">' + esc(c.entityKey) + '</span></h1>',
                    '  <span class="sub">' + esc(c.action) + ' · authored by <code>' + esc(truncate(c.authorUserId, 8)) + '</code></span>',
                    '</div><div class="actions">' + crStatusBadge(c.status) + (c.wasEmergencyBypass ? ' ' + badge("emergency") : '') + '</div></div>',
                    '<div class="page-body"><div class="detail-grid"><div class="detail-main">',
                    '  <div class="card-pad"><h2>Diff</h2>'
                    + renderDiff(prettyJson(c.currentState), prettyJson(c.proposedState), esc(c.entityType) + " " + esc(c.entityKey))
                    + '</div>',
                    '  <div class="card-pad"><h2>Comments</h2>',
                    '    <div class="cr-comments">' + (comments || '<p class="muted">No comments yet.</p>') + '</div>',
                    '    <form id="cr-comment-form" class="cr-actions"><input id="cr-comment" placeholder="Add a comment…" /><button type="submit" class="btn outline xs">Comment</button></form>',
                    '  </div>',
                    '</div><aside class="detail-side">',
                    '  <div class="side-card"><h3 class="side-h">Decision</h3>',
                    '    <div class="cr-actions">' + actions + '</div><span class="save-msg" id="cr-msg"></span>',
                    '  </div>',
                    '  <div class="side-card"><h3 class="side-h">Details</h3><dl class="side-dl">',
                    '    <dt>Status</dt><dd>' + crStatusBadge(c.status) + '</dd>',
                    '    <dt>Action</dt><dd>' + esc(c.action) + '</dd>',
                    '    <dt>Entity</dt><dd>' + esc(c.entityType) + '</dd>',
                    '    <dt>Author</dt><dd><code>' + esc(truncate(c.authorUserId, 8)) + '</code></dd>',
                    c.authorMessage ? '    <dt>Message</dt><dd>' + esc(c.authorMessage) + '</dd>' : '',
                    '  </dl></div>',
                    approvals ? '  <div class="side-card"><h3 class="side-h">Approvals</h3><ul class="cr-approvals">' + approvals + '</ul></div>' : '',
                    '</aside></div></div></div>',
                ].join("\n");
                wireChangeDetail(c);
            })
            .catch(handleErrOnView("Change"));
    }

    function changeActionButtons(c) {
        var buttons = [];
        if (c.status === "Pending") {
            buttons.push('<button type="button" class="btn primary xs" data-cr="approve">Approve</button>');
            buttons.push('<button type="button" class="btn outline xs" data-cr="reject">Reject</button>');
        }
        if (c.status === "Approved") {
            buttons.push('<button type="button" class="btn primary xs" data-cr="apply">Apply</button>');
        }
        if (c.status === "Pending" || c.status === "Approved") {
            buttons.push('<button type="button" class="btn outline xs danger" data-cr="bypass">Emergency bypass</button>');
        }
        return buttons.join(" ") || '<span class="muted">No actions available (' + esc(c.status) + ').</span>';
    }

    function wireChangeDetail(c) {
        var msg = document.getElementById("cr-msg");
        document.getElementById("cr-comment-form").addEventListener("submit", function (e) {
            e.preventDefault();
            var body = document.getElementById("cr-comment").value.trim();
            if (!body) { return; }
            api("POST", "/admin/changes/" + encodeURIComponent(c.id) + "/comments", { body: body })
                .then(function () { renderChangeDetail(c.id); })
                .catch(crErr(msg));
        });
        Array.prototype.slice.call(document.querySelectorAll("[data-cr]")).forEach(function (btn) {
            btn.addEventListener("click", function () {
                var action = btn.getAttribute("data-cr");
                if (action === "approve") {
                    api("POST", "/admin/changes/" + encodeURIComponent(c.id) + "/approvals", { decision: "Approve" })
                        .then(function () { renderChangeDetail(c.id); }).catch(crErr(msg));
                } else if (action === "reject") {
                    var reason = window.prompt("Reason for rejection (optional):") || "";
                    api("POST", "/admin/changes/" + encodeURIComponent(c.id) + "/approvals", { decision: "Reject", comment: reason })
                        .then(function () { renderChangeDetail(c.id); }).catch(crErr(msg));
                } else if (action === "apply") {
                    api("POST", "/admin/changes/" + encodeURIComponent(c.id) + "/apply")
                        .then(function () { renderChangeDetail(c.id); }).catch(crErr(msg));
                } else if (action === "bypass") {
                    var why = window.prompt("Emergency bypass reason (required):");
                    if (!why) { return; }
                    api("POST", "/admin/changes/" + encodeURIComponent(c.id) + "/bypass", { reason: why })
                        .then(function () { renderChangeDetail(c.id); }).catch(crErr(msg));
                }
            });
        });
    }

    function crErr(msg) {
        return function (err) {
            if (err.kind === "auth") { showAuthPrompt(); return; }
            if (msg) { msg.className = "save-msg save-msg--error"; msg.textContent = err.message; }
        };
    }

    function prettyJson(value) {
        if (value === null || value === undefined) { return "(none)"; }
        try { return JSON.stringify(value, null, 2); } catch (_) { return String(value); }
    }

    // ============================================================
    // Diff view: line-level LCS diff rendered with the .diff component.
    // Used by the change review and the audit details modal.
    // ============================================================
    function diffLines(a, b) {
        a = String(a == null ? "" : a).split("\n");
        b = String(b == null ? "" : b).split("\n");
        var n = a.length, m = b.length;
        // LCS length table (bottom-up), then walk it to emit add/del/context.
        var dp = [];
        for (var x = 0; x <= n; x++) { dp.push(new Array(m + 1).fill(0)); }
        for (var i = n - 1; i >= 0; i--) {
            for (var j = m - 1; j >= 0; j--) {
                dp[i][j] = a[i] === b[j] ? dp[i + 1][j + 1] + 1 : Math.max(dp[i + 1][j], dp[i][j + 1]);
            }
        }
        var out = [], p = 0, q = 0, oldN = 1, newN = 1;
        while (p < n && q < m) {
            if (a[p] === b[q]) { out.push({ t: "ctx", text: a[p], o: oldN++, nn: newN++ }); p++; q++; }
            else if (dp[p + 1][q] >= dp[p][q + 1]) { out.push({ t: "del", text: a[p], o: oldN++, nn: null }); p++; }
            else { out.push({ t: "add", text: b[q], o: null, nn: newN++ }); q++; }
        }
        while (p < n) { out.push({ t: "del", text: a[p], o: oldN++, nn: null }); p++; }
        while (q < m) { out.push({ t: "add", text: b[q], o: null, nn: newN++ }); q++; }
        return out;
    }

    function renderDiff(oldStr, newStr, title) {
        var lines = diffLines(oldStr, newStr);
        var adds = 0, dels = 0;
        var body = lines.map(function (l) {
            if (l.t === "add") { adds++; } else if (l.t === "del") { dels++; }
            var cls = l.t === "add" ? " add" : (l.t === "del" ? " del" : "");
            var sym = l.t === "add" ? "+" : (l.t === "del" ? "-" : " ");
            return '<div class="line' + cls + '">'
                + '<span class="ln">' + (l.o == null ? "" : l.o) + '</span>'
                + '<span class="ln">' + (l.nn == null ? "" : l.nn) + '</span>'
                + '<span class="sym">' + sym + '</span>'
                + '<span class="code">' + esc(l.text) + '</span></div>';
        }).join("");
        return '<div class="diff"><div class="file-head">'
            + (title ? '<span>' + esc(title) + '</span>' : '')
            + '<span class="stat-add">+' + adds + '</span><span class="stat-del">-' + dels + '</span></div>'
            + '<div class="body">' + body + '</div></div>';
    }

    // ============================================================
    // Modal: built once, appended to <body>. Esc / backdrop close.
    // ============================================================
    var modalEl = null;
    function ensureModal() {
        if (modalEl) { return modalEl; }
        modalEl = document.createElement("div");
        modalEl.className = "modal-backdrop";
        modalEl.hidden = true;
        document.body.appendChild(modalEl);
        modalEl.addEventListener("mousedown", function (e) { if (e.target === modalEl) { closeModal(); } });
        document.addEventListener("keydown", function (e) { if (e.key === "Escape" && modalEl && !modalEl.hidden) { closeModal(); } });
        return modalEl;
    }
    function openModal(title, bodyHtml, large) {
        ensureModal();
        modalEl.innerHTML = '<div class="modal' + (large ? " lg" : "") + '" role="dialog" aria-modal="true" aria-label="' + esc(title) + '">'
            + '<div class="modal-head"><span class="title">' + esc(title) + '</span>'
            + '<button class="icon-btn" type="button" data-modal-close aria-label="Close"><span class="ti-slot" data-ti="x"></span></button></div>'
            + '<div class="modal-body">' + bodyHtml + '</div></div>';
        modalEl.hidden = false;
        hydrateIcons(modalEl);
        modalEl.querySelector("[data-modal-close]").addEventListener("click", closeModal);
    }
    function closeModal() { if (modalEl) { modalEl.hidden = true; modalEl.innerHTML = ""; } }

    function renderApprovalsEditor() {
        if (!currentEnv) { viewEl.innerHTML = listEmptyEnv("Approval policy"); return; }
        var envKey = currentEnv.key;
        viewEl.innerHTML = listPageShell("Approval policy", "Loading…", '<div class="empty"><p class="muted">Loading…</p></div>');
        api("GET", "/admin/approval-policies/" + encodeURIComponent(envKey))
            .then(function (p) {
                var rules = (p.approverRules || []).map(function (r) {
                    return '<li>' + badge(r.type) + (r.mandatory ? ' ' + badge("mandatory") : '') + ' · min ' + (r.minFromThisRule || 1) + '</li>';
                }).join("");
                viewEl.innerHTML = [
                    '<div class="page"><div class="page-head"><div class="title-wrap"><h1>Approval policy</h1>',
                    '  <span class="sub">When approval is required, mutations to <code>' + esc(envKey) + '</code> become pending changes that need sign-off before they apply.</span>',
                    '</div></div><div class="page-body"><div class="detail-grid"><div class="detail-main">',
                    '  <form id="policy-form" class="editor card-pad">',
                    field("Require approval", '<label class="check"><input type="checkbox" name="required"' + (p.required ? " checked" : "") + ' /> Mutations require approval</label>'),
                    field("Minimum approvals", '<input name="minApprovals" type="number" min="1" value="' + esc(p.minApprovals || 1) + '" />'),
                    field("Self-approval", '<label class="check"><input type="checkbox" name="self"' + (p.authorCanApproveOwnChange ? " checked" : "") + ' /> Author may approve their own change</label>'),
                    field("Emergency bypass", '<label class="check"><input type="checkbox" name="bypass"' + (p.allowEmergencyBypass ? " checked" : "") + ' /> Allow emergency bypass</label>'),
                    '  <div class="editor__footer"><button type="submit" class="btn primary">Save policy</button><span class="save-msg" id="policy-msg"></span></div>',
                    '  </form>',
                    '</div><aside class="detail-side"><div class="side-card"><h3 class="side-h">Approver rules</h3>',
                    rules ? '<ul class="cr-approvals">' + rules + '</ul>' : '<p class="muted" style="font-size:12px">No structured approver rules — a flat minimum-approvals count applies.</p>',
                    '</div></aside></div></div></div>',
                ].join("\n");
                document.getElementById("policy-form").addEventListener("submit", function (e) {
                    e.preventDefault();
                    var f = e.target.elements;
                    var pmsg = document.getElementById("policy-msg");
                    setMessageOn(pmsg, "loading", "Saving…");
                    api("PUT", "/admin/approval-policies/" + encodeURIComponent(envKey), {
                        required: f["required"].checked,
                        minApprovals: parseInt(f["minApprovals"].value, 10) || 1,
                        authorCanApproveOwnChange: f["self"].checked,
                        allowEmergencyBypass: f["bypass"].checked,
                        approverRules: p.approverRules || [],
                    }).then(function () { setMessageOn(pmsg, "success", "Saved."); })
                      .catch(function (err) { if (err.kind === "auth") { showAuthPrompt(); return; } setMessageOn(pmsg, "error", err.message); });
                });
            })
            .catch(handleErrOnView("Approvals"));
    }

    function setMessageOn(slot, kind, text) {
        if (!slot) { return; }
        slot.className = "save-msg save-msg--" + kind;
        slot.textContent = text;
    }

    // ============================================================
    // Webhooks + Audit log (M10)
    // ============================================================
    function deliveryStatusBadge(status) {
        var v = status === "Succeeded" ? " success" : (status === "Dead" ? " danger" : " warn");
        return '<span class="badge' + v + '">' + esc(status) + '</span>';
    }

    function renderWebhookList() {
        viewEl.innerHTML = listPageShell("Webhooks", "Loading…", '<div class="empty"><p class="muted">Loading…</p></div>');
        api("GET", "/admin/webhooks")
            .then(function (hooks) {
                hooks = Array.isArray(hooks) ? hooks : [];
                var rows = hooks.map(function (w) {
                    var types = (w.eventTypes || []).length ? (w.eventTypes).map(code).join(" ") : '<span class="muted">all events</span>';
                    return '<tr>'
                        + '<td><a data-link="/webhooks/' + encodeURIComponent(w.id) + '">' + esc(w.name) + '</a></td>'
                        + '<td>' + code(truncate(w.url, 48)) + '</td>'
                        + '<td>' + (w.enabled ? '<span class="badge success"><span class="dot"></span>on</span>' : '<span class="badge">off</span>') + '</td>'
                        + '<td>' + types + '</td>'
                        + '</tr>';
                }).join("");
                viewEl.innerHTML = [
                    '<div class="page"><div class="page-head"><div class="title-wrap"><h1>Webhooks</h1>',
                    '  <span class="sub">Outbound HTTP notifications. Each delivery is signed with the endpoint secret (<code>X-Featly-Signature: sha256=…</code>, HMAC-SHA256) and retried with backoff.</span>',
                    '</div></div><div class="page-body"><div class="detail-grid"><div class="detail-main">',
                    hooks.length
                        ? '<div class="tbl-wrap"><table class="tbl"><thead><tr><th>Name</th><th>URL</th><th>Enabled</th><th>Events</th></tr></thead><tbody>' + rows + '</tbody></table></div>'
                        : '<div class="empty"><p>No webhooks yet. Create one on the right.</p></div>',
                    '</div><aside class="detail-side"><div class="side-card"><h3 class="side-h">New webhook</h3>',
                    '  <form id="wh-form" class="editor">',
                    field("Name", '<input name="name" required placeholder="Slack relay" />'),
                    field("URL", '<input name="url" required placeholder="https://example.com/hook" />'),
                    field("Event types", '<input name="eventTypes" placeholder="flag.updated, change.applied (blank = all)" />'),
                    field("Secret", '<input name="secret" placeholder="(blank = auto-generate)" />'),
                    '  <div class="editor__footer"><button type="submit" class="btn primary">Create webhook</button><span class="save-msg" id="wh-msg"></span></div>',
                    '  </form>',
                    '</div></aside></div></div></div>',
                ].join("\n");

                document.getElementById("wh-form").addEventListener("submit", function (e) {
                    e.preventDefault();
                    var f = e.target;
                    var msg = document.getElementById("wh-msg");
                    setMessageOn(msg, "loading", "Creating…");
                    api("POST", "/admin/webhooks", {
                        name: f.name.value.trim(),
                        url: f.url.value.trim(),
                        secret: f.secret.value.trim() || null,
                        eventTypes: parseCsv(f.eventTypes.value),
                    }).then(function (created) { navigate("/webhooks/" + encodeURIComponent(created.id)); })
                      .catch(function (err) { if (err.kind === "auth") { showAuthPrompt(); return; } setMessageOn(msg, "error", err.message); });
                });
            })
            .catch(handleErrOnView("Webhooks"));
    }

    function renderWebhookDetail(id) {
        viewEl.innerHTML = listPageShell("Webhook", "Loading…", '<div class="empty"><p class="muted">Loading…</p></div>');
        Promise.all([
            api("GET", "/admin/webhooks/" + encodeURIComponent(id)),
            api("GET", "/admin/webhooks/" + encodeURIComponent(id) + "/deliveries").catch(function () { return []; }),
        ]).then(function (res) {
            var w = res[0];
            var deliveries = Array.isArray(res[1]) ? res[1] : [];
            var deliveryRows = deliveries.map(function (d) {
                return '<tr>'
                    + '<td>' + code(d.eventType) + '</td>'
                    + '<td>' + deliveryStatusBadge(d.status) + '</td>'
                    + '<td class="num">' + (d.attemptCount || 0) + '</td>'
                    + '<td class="num">' + (d.lastStatusCode != null ? d.lastStatusCode : '—') + '</td>'
                    + '<td class="muted">' + esc(truncate(d.lastError || "", 48)) + '</td>'
                    + '<td>' + formatDate(d.createdAt) + '</td>'
                    + '</tr>';
            }).join("");

            viewEl.innerHTML = [
                '<div class="page"><div class="page-head"><div class="title-wrap"><h1>' + esc(w.name) + '</h1>',
                '  <span class="sub">' + (w.enabled ? '<span class="badge success"><span class="dot"></span>enabled</span>' : '<span class="badge">disabled</span>') + ' · ' + code(truncate(w.url, 56)) + '</span>',
                '</div><div class="actions">',
                '  <button type="button" class="btn outline xs" data-wh="test">Send test event</button>',
                '  <button type="button" class="btn outline xs danger" data-wh="delete">Delete</button>',
                '  <span class="save-msg" id="wh-msg"></span>',
                '</div></div>',
                '<div class="page-body"><div class="detail-grid"><div class="detail-main">',
                '  <form id="wh-edit" class="editor card-pad">',
                field("Name", '<input name="name" required value="' + esc(w.name) + '" />'),
                field("URL", '<input name="url" required value="' + esc(w.url) + '" />'),
                field("Enabled", '<label class="check"><input type="checkbox" name="enabled"' + (w.enabled ? " checked" : "") + ' /> Deliver events</label>'),
                field("Event types", '<input name="eventTypes" value="' + esc((w.eventTypes || []).join(", ")) + '" placeholder="(blank = all)" />'),
                field("Secret", '<input name="secret" value="' + esc(w.secret || "") + '" />'),
                '  <div class="editor__footer"><button type="submit" class="btn primary">Save</button></div>',
                '  </form>',
                '  <div class="card-pad"><h2>Recent deliveries</h2>',
                deliveryRows
                    ? '<div class="tbl-wrap"><table class="tbl"><thead><tr><th>Event</th><th>Status</th><th>Attempts</th><th>Code</th><th>Last error</th><th>Created</th></tr></thead><tbody>' + deliveryRows + '</tbody></table></div>'
                    : '<div class="empty"><p>No deliveries yet. Use “Send test event” to enqueue one.</p></div>',
                '  </div>',
                '</div><aside class="detail-side"><div class="side-card"><h3 class="side-h">Details</h3><dl class="side-dl">',
                '  <dt>Status</dt><dd>' + (w.enabled ? "enabled" : "disabled") + '</dd>',
                '  <dt>Events</dt><dd>' + ((w.eventTypes || []).length ? esc((w.eventTypes).join(", ")) : "all events") + '</dd>',
                '  <dt>Deliveries</dt><dd>' + deliveries.length + '</dd>',
                '  <dt>Created</dt><dd>' + (formatDate(w.createdAt) || "—") + '</dd>',
                '</dl></div></aside></div></div></div>',
            ].join("\n");

            var msg = document.getElementById("wh-msg");
            document.getElementById("wh-edit").addEventListener("submit", function (e) {
                e.preventDefault();
                var f = e.target;
                setMessageOn(msg, "loading", "Saving…");
                api("PUT", "/admin/webhooks/" + encodeURIComponent(id), {
                    name: f.name.value.trim(),
                    url: f.url.value.trim(),
                    enabled: f.enabled.checked,
                    eventTypes: parseCsv(f.eventTypes.value),
                    secret: f.secret.value.trim() || null,
                }).then(function () { setMessageOn(msg, "success", "Saved."); })
                  .catch(function (err) { if (err.kind === "auth") { showAuthPrompt(); return; } setMessageOn(msg, "error", err.message); });
            });
            Array.prototype.slice.call(document.querySelectorAll("[data-wh]")).forEach(function (btn) {
                btn.addEventListener("click", function () {
                    var action = btn.getAttribute("data-wh");
                    if (action === "test") {
                        setMessageOn(msg, "loading", "Enqueuing test…");
                        api("POST", "/admin/webhooks/" + encodeURIComponent(id) + "/test")
                            .then(function () { renderWebhookDetail(id); })
                            .catch(function (err) { if (err.kind === "auth") { showAuthPrompt(); return; } setMessageOn(msg, "error", err.message); });
                    } else if (action === "delete") {
                        api("DELETE", "/admin/webhooks/" + encodeURIComponent(id))
                            .then(function () { navigate("/webhooks"); })
                            .catch(function (err) { if (err.kind === "auth") { showAuthPrompt(); return; } setMessageOn(msg, "error", err.message); });
                    }
                });
            });
        }).catch(handleErrOnView("Webhook"));
    }

    function renderSettings() {
        viewEl.innerHTML = [
            '<div class="page"><div class="page-head"><div class="title-wrap"><h1>Settings</h1>',
            '  <span class="sub">Environment-level controls. A read-only environment rejects every mutation (flags, configs, segments) with <code>403</code> — a hard freeze for incidents and compliance windows.</span>',
            '</div></div><div class="page-body tight">',
            '  <h2>Environments</h2>',
            '  <div class="card-pad"><form id="env-new" class="row-form">',
            '    <label class="field"><span class="field__label">Key</span><input name="key" required placeholder="staging" /></label>',
            '    <label class="field"><span class="field__label">Name</span><input name="name" placeholder="Staging" /></label>',
            '    <div class="row-form__action"><button type="submit" class="btn primary">Add environment</button><span class="save-msg" id="env-new-msg"></span></div>',
            '  </form></div>',
            '  <div id="env-settings"><div class="empty"><p class="muted">Loading…</p></div></div>',
            '</div></div>',
        ].join("\n");

        function refreshSettings(msg) {
            loadEnvironments().then(function () { renderSettings(); })
                .catch(function (err) { if (err.kind === "auth") { showAuthPrompt(); return; } if (msg) { setMessageOn(msg, "error", err.message); } });
        }

        document.getElementById("env-new").addEventListener("submit", function (e) {
            e.preventDefault();
            var f = e.target;
            var msg = document.getElementById("env-new-msg");
            setMessageOn(msg, "loading", "Creating…");
            api("POST", "/admin/environments", { key: f.key.value.trim(), name: f.name.value.trim() || null })
                .then(function () { refreshSettings(msg); })
                .catch(function (err) { if (err.kind === "auth") { showAuthPrompt(); return; } setMessageOn(msg, "error", err.message); });
        });

        api("GET", "/admin/environments").then(function (envs) {
            envs = Array.isArray(envs) ? envs : [];
            var rows = envs.map(function (e) {
                var frozen = e.readOnly;
                var action = frozen ? "unlock" : "lock";
                var label = frozen ? "Unlock" : "Lock";
                var btnClass = frozen ? "btn primary xs" : "btn outline xs danger";
                return '<tr>'
                    + '<td>' + code(e.key) + (e.isDefault ? ' ' + badge("default") : '') + '</td>'
                    + '<td>' + esc(e.name) + '</td>'
                    + '<td>' + (frozen ? '<span class="badge danger">read-only</span>' : '<span class="badge success"><span class="dot"></span>writable</span>') + '</td>'
                    + '<td class="col-actions" style="white-space:nowrap">'
                    + '<button type="button" class="' + btnClass + '" data-env-action="' + action + '" data-env-key="' + esc(e.key) + '">' + label + '</button> '
                    + '<button type="button" class="btn outline xs" data-env-action="rename" data-env-key="' + esc(e.key) + '" data-env-name="' + esc(e.name) + '">Rename</button>'
                    + (e.isDefault ? '' : ' <button type="button" class="btn outline xs danger" data-env-action="delete" data-env-key="' + esc(e.key) + '">Delete</button>')
                    + '</td>'
                    + '</tr>';
            }).join("");
            document.getElementById("env-settings").innerHTML = [
                '<div class="tbl-wrap"><table class="tbl"><thead><tr><th>Key</th><th>Name</th><th>State</th><th></th></tr></thead><tbody>' + rows + '</tbody></table></div>',
                '<span class="save-msg" id="env-msg"></span>',
            ].join("\n");

            Array.prototype.slice.call(document.querySelectorAll("#env-settings [data-env-action]")).forEach(function (btn) {
                btn.addEventListener("click", function () {
                    var key = btn.getAttribute("data-env-key");
                    var action = btn.getAttribute("data-env-action");
                    var msg = document.getElementById("env-msg");
                    var p;
                    if (action === "lock" || action === "unlock") {
                        setMessageOn(msg, "loading", action === "lock" ? "Freezing…" : "Unfreezing…");
                        p = api("POST", "/admin/environments/" + encodeURIComponent(key) + "/" + action);
                    } else if (action === "rename") {
                        var name = window.prompt("New name for environment '" + key + "':", btn.getAttribute("data-env-name") || "");
                        if (name == null) { return; }
                        setMessageOn(msg, "loading", "Renaming…");
                        p = api("PUT", "/admin/environments/" + encodeURIComponent(key), { key: key, name: name.trim() || null });
                    } else if (action === "delete") {
                        if (!window.confirm("Delete environment '" + key + "'? It must have no flags, configs or segments.")) { return; }
                        setMessageOn(msg, "loading", "Deleting…");
                        p = api("DELETE", "/admin/environments/" + encodeURIComponent(key));
                    } else { return; }
                    p.then(function () { refreshSettings(msg); })
                     .catch(function (err) { if (err.kind === "auth") { showAuthPrompt(); return; } setMessageOn(msg, "error", err.message); });
                });
            });
        }).catch(handleErrOnView("Settings"));
    }

    function auditIcon(entityType) {
        switch (String(entityType == null ? "" : entityType)) {
            case "Flag": return "flag";
            case "Config": return "sliders";
            case "Segment": return "segment";
            case "Webhook": return "webhook";
            case "Role": return "shield";
            case "User": return "user";
            case "ApiKey": return "user-shield";
            default: return "scroll";
        }
    }
    function renderAuditLog() {
        viewEl.innerHTML = [
            '<div class="page"><div class="page-head"><div class="title-wrap"><h1>Audit log</h1>',
            '  <span class="sub">Every consequential action, newest first. Filter by entity, actor, or date.</span>',
            '</div></div><div class="page-body tight">',
            '<form id="audit-filter" class="audit-filter">',
            '  <input name="entityType" placeholder="Entity type (e.g. Flag)" />',
            '  <input name="entityKey" placeholder="Entity key" />',
            '  <input name="actor" placeholder="Actor" />',
            '  <input name="from" type="date" title="From date" />',
            '  <input name="to" type="date" title="To date" />',
            '  <button type="submit" class="btn outline xs">Filter</button>',
            '</form>',
            '<div id="audit-results"><div class="empty"><p class="muted">Loading…</p></div></div>',
            '</div></div>',
        ].join("\n");

        function load(filters) {
            var qs = [];
            if (filters.entityType) { qs.push("entityType=" + encodeURIComponent(filters.entityType)); }
            if (filters.entityKey) { qs.push("entityKey=" + encodeURIComponent(filters.entityKey)); }
            if (filters.actor) { qs.push("actor=" + encodeURIComponent(filters.actor)); }
            if (filters.from) { qs.push("from=" + encodeURIComponent(filters.from + "T00:00:00Z")); }
            if (filters.to) { qs.push("to=" + encodeURIComponent(filters.to + "T23:59:59Z")); }
            var path = "/admin/audit" + (qs.length ? "?" + qs.join("&") : "");
            api("GET", path).then(function (entries) {
                entries = Array.isArray(entries) ? entries : [];
                var resultsEl = document.getElementById("audit-results");
                if (entries.length === 0) {
                    resultsEl.innerHTML = '<div class="empty"><p class="muted">No audit entries match.</p></div>';
                    return;
                }
                var rows = entries.map(function (a, idx) {
                    var warn = /lock|bypass|drop|delete|revoke|bootstrap/i.test(a.action || "");
                    return '<div class="audit-row' + (warn ? ' warn' : '') + '" data-idx="' + idx + '" role="button" tabindex="0" title="View details" style="cursor:pointer">'
                        + '<span class="ts">' + (formatDate(a.at) || "—") + '</span>'
                        + '<span class="ic"><span class="ti-slot" data-ti="' + auditIcon(a.entityType) + '"></span></span>'
                        + '<span class="desc"><span class="who">' + esc(a.actorIdentifier || "—") + '</span> '
                        + code(a.action) + (a.entityKey ? ' <span class="entity">' + esc(truncate(a.entityKey, 32)) + '</span>' : '') + '</span>'
                        + (a.entityType ? '<span class="env-chip">' + esc(a.entityType) + '</span>' : '')
                        + '</div>';
                }).join("");
                resultsEl.innerHTML = '<div class="audit-list">' + rows + '</div>';
                hydrateIcons(resultsEl);
                function openAuditEntry(idx) {
                    var a = entries[idx];
                    if (!a) { return; }
                    var d = a.data;
                    var title = (a.action || "Audit entry") + (a.entityKey ? " · " + a.entityKey : "");
                    var body;
                    if (d && typeof d === "object" && !Array.isArray(d) &&
                        (d.before !== undefined || d.after !== undefined || d.currentState !== undefined || d.proposedState !== undefined)) {
                        var before = d.before !== undefined ? d.before : d.currentState;
                        var after = d.after !== undefined ? d.after : d.proposedState;
                        body = renderDiff(prettyJson(before), prettyJson(after), a.entityType ? esc(a.entityType) + " " + esc(a.entityKey || "") : "");
                    } else {
                        body = '<pre class="cr-json">' + esc(prettyJson(d === undefined ? null : d)) + '</pre>';
                    }
                    var meta = '<dl class="side-dl" style="margin-bottom:12px">'
                        + '<dt>Actor</dt><dd>' + esc(a.actorIdentifier || "—") + '</dd>'
                        + '<dt>Action</dt><dd class="mono">' + esc(a.action || "—") + '</dd>'
                        + '<dt>Entity</dt><dd>' + esc((a.entityType || "—") + (a.entityKey ? " / " + a.entityKey : "")) + '</dd>'
                        + '<dt>When</dt><dd>' + (formatDate(a.at) || "—") + '</dd>'
                        + '</dl>';
                    openModal(title, meta + body, true);
                }
                Array.prototype.slice.call(resultsEl.querySelectorAll(".audit-row")).forEach(function (row) {
                    row.addEventListener("click", function () { openAuditEntry(parseInt(row.getAttribute("data-idx"), 10)); });
                    row.addEventListener("keydown", function (e) {
                        if (e.key === "Enter" || e.key === " ") { e.preventDefault(); openAuditEntry(parseInt(row.getAttribute("data-idx"), 10)); }
                    });
                });
            }).catch(handleErrOnView("Audit log"));
        }

        document.getElementById("audit-filter").addEventListener("submit", function (e) {
            e.preventDefault();
            var f = e.target;
            load({
                entityType: f.entityType.value.trim(),
                entityKey: f.entityKey.value.trim(),
                actor: f.actor.value.trim(),
                from: f.from.value,
                to: f.to.value,
            });
        });
        load({});
    }

    function parseCsv(value) {
        return String(value || "").split(",").map(function (s) { return s.trim(); }).filter(function (s) { return s.length > 0; });
    }

    // ============================================================
    // Rule editor (shared between Flag rules and Config rules)
    // ============================================================
    function renderRulesEditor(rules, context) {
        return '<div class="rules-list">' + rules.map(function (r, i) { return renderRuleCard(r, context, i); }).join("") + '</div>';
    }

    // One-line glance of a rule for the collapsible header: first condition
    // (attr op value) + the outcome. Computed from the saved data; refreshes on
    // the next render. Editing happens in the body.
    function ruleSummary(rule, context) {
        var conds = rule.conditions || [];
        var head;
        if (!conds.length) {
            head = '<span class="op">any context</span>';
        } else {
            var c = conds[0];
            var val = typeof c.value === "string" ? c.value : JSON.stringify(c.value);
            head = '<span class="mono">' + esc(c.attribute || "?") + '</span>'
                + '<span class="op">' + esc(c.operator || "") + '</span>'
                + '<span class="val">' + esc(truncate(val == null ? "" : String(val), 28)) + '</span>'
                + (conds.length > 1 ? '<span class="op">+' + (conds.length - 1) + '</span>' : '');
        }
        var out = "";
        if (context.kind === "flag") {
            if (rule.outcome && rule.outcome.splits && rule.outcome.splits.length) {
                out = '<span class="op">&rarr; split</span>';
            } else if (rule.outcome && rule.outcome.variantKey) {
                out = '<span class="op">&rarr;</span><span class="val">' + esc(rule.outcome.variantKey) + '</span>';
            }
        } else {
            out = '<span class="op">&rarr;</span><span class="val">' + esc(truncate(JSON.stringify(rule.value), 24)) + '</span>';
        }
        return head + out;
    }

    function renderRuleCard(rule, context, index) {
        var outcomeHtml;
        if (context.kind === "flag") {
            var hasSplits = !!(rule.outcome && rule.outcome.splits && rule.outcome.splits.length);
            var variantOpts = (context.variants || []).map(function (v) {
                return '<option value="' + esc(v.key) + '"' + (rule.outcome && rule.outcome.variantKey === v.key ? " selected" : "") + '>' + esc(v.key) + '</option>';
            }).join("");
            var splitsHtml = '<div class="splits' + (hasSplits ? "" : " hidden") + '">'
                + ((rule.outcome && rule.outcome.splits) || []).map(renderSplitRow).join("")
                + '<button type="button" class="btn outline xs" data-action="add-split">+ Add split</button>'
                + '</div>';
            outcomeHtml =
                '<div class="outcome">'
                + '<label class="check"><input type="checkbox" class="split-toggle"' + (hasSplits ? " checked" : "") + ' /> Use weighted splits</label>'
                + '<div class="single' + (hasSplits ? " hidden" : "") + '"><label>Variant <select class="r-variant">' + variantOpts + '</select></label></div>'
                + splitsHtml
                + '</div>';
        } else {
            outcomeHtml =
                '<div class="outcome">'
                + '<label>Value (JSON) <input class="r-value" value="' + esc(JSON.stringify(rule.value)) + '" /></label>'
                + '</div>';
        }

        var num = (typeof index === "number" ? index + 1 : 0);
        return '<div class="rule rule-card" data-rule-id="' + esc(rule.id || cryptoId()) + '">'
            + '<div class="rule-head" data-action="rule-toggle">'
            + '  <span class="grip" aria-hidden="true">' + icon("grip") + '</span>'
            + '  <span class="rule-num">#' + num + '</span>'
            + '  <span class="summary">' + ruleSummary(rule, context) + '</span>'
            + '  <span class="meta">'
            + '    <label class="check"><input type="checkbox" class="r-enabled"' + (rule.enabled === false ? "" : " checked") + ' /> on</label>'
            + '    <button type="button" class="icon-btn" data-action="rule-up" aria-label="Move up">' + icon("chevron-up") + '</button>'
            + '    <button type="button" class="icon-btn" data-action="rule-down" aria-label="Move down">' + icon("chevron-down") + '</button>'
            + '    <button type="button" class="icon-btn" data-action="rule-remove" aria-label="Remove">' + icon("x") + '</button>'
            + '    <span class="chev" aria-hidden="true">' + icon("chevron-down") + '</span>'
            + '  </span>'
            + '</div>'
            + '<div class="rule-body">'
            + '  <label class="field"><span class="field__label">Rule name</span><input class="r-name" placeholder="optional label" value="' + esc(rule.name || "") + '" /></label>'
            + '  <div class="conditions-list">' + (rule.conditions || []).map(renderConditionRow).join("") + '</div>'
            + '  <button type="button" class="btn outline xs" data-action="add-condition">+ Add condition</button>'
            + '  ' + outcomeHtml
            + '</div>'
            + '</div>';
    }

    function renderConditionRow(c) {
        var opts = OPERATORS.map(function (o) {
            return '<option value="' + o + '"' + (c.operator === o ? " selected" : "") + '>' + o + '</option>';
        }).join("");
        return '<div class="condition-row">'
            + '<input class="c-attr" placeholder="attribute" value="' + esc(c.attribute || "") + '" />'
            + '<select class="c-op">' + opts + '</select>'
            + '<input class="c-value" placeholder="value (JSON or text)" value="' + esc(typeof c.value === "string" ? c.value : JSON.stringify(c.value || "")) + '" />'
            + '<label class="check"><input type="checkbox" class="c-negate"' + (c.negate ? " checked" : "") + ' /> negate</label>'
            + '<button type="button" class="icon-btn" data-action="remove-condition" aria-label="Remove">' + icon("x") + '</button>'
            + '</div>';
    }

    function renderSplitRow(s) {
        return '<div class="split-row">'
            + '<input class="s-variant" placeholder="variant" value="' + esc(s.variantKey || "") + '" />'
            + '<input class="s-weight" type="number" min="0" max="10000" placeholder="weight" value="' + esc(s.weight || 0) + '" />'
            + '<button type="button" class="icon-btn" data-action="remove-split" aria-label="Remove">' + icon("x") + '</button>'
            + '</div>';
    }

    function handleRuleAction(event, form, context) {
        var btn = event.target.closest("[data-action]");
        if (!btn) { return; }
        var action = btn.getAttribute("data-action");
        var card = btn.closest(".rule-card");
        if (action === "rule-remove" && card) { card.remove(); }
        else if (action === "rule-up" && card && card.previousElementSibling) { card.parentNode.insertBefore(card, card.previousElementSibling); }
        else if (action === "rule-down" && card && card.nextElementSibling) { card.parentNode.insertBefore(card.nextElementSibling, card); }
        else if (action === "add-condition") {
            card.querySelector(".conditions-list").insertAdjacentHTML("beforeend", renderConditionRow({ attribute: "", operator: "Equals", value: "", negate: false }));
        }
        else if (action === "remove-condition") { btn.closest(".condition-row").remove(); }
        else if (action === "add-split") { card.querySelector(".splits").insertBefore(htmlToElement(renderSplitRow({ variantKey: "", weight: 0 })), btn); }
        else if (action === "remove-split") { btn.closest(".split-row").remove(); }
        else if (action === "rule-toggle") {
            // Collapse / expand the rule body — but ignore clicks on the controls
            // that live in the header (enabled toggle, move/remove buttons).
            if (!event.target.closest("input, select, button, label") && card) { card.classList.toggle("collapsed"); }
            return;
        }
        else { return; }
        // Re-bind split toggle in case we added one.
        if (context.kind === "flag" && card) {
            var toggle = card.querySelector(".split-toggle");
            if (toggle && !toggle.dataset.wired) {
                toggle.dataset.wired = "1";
                toggle.addEventListener("change", function () {
                    card.querySelector(".single").classList.toggle("hidden", toggle.checked);
                    card.querySelector(".splits").classList.toggle("hidden", !toggle.checked);
                });
            }
        }
    }

    function collectRules(form, context) {
        return Array.prototype.slice.call(form.querySelectorAll(".rule-card")).map(function (card, idx) {
            var rule = {
                id: card.getAttribute("data-rule-id"),
                order: idx,
                name: card.querySelector(".r-name").value.trim() || null,
                enabled: card.querySelector(".r-enabled").checked,
                conditions: collectConditions(card.querySelector(".conditions-list")),
            };
            if (context.kind === "flag") {
                var useSplits = card.querySelector(".split-toggle").checked;
                if (useSplits) {
                    var splits = Array.prototype.slice.call(card.querySelectorAll(".split-row")).map(function (row) {
                        return {
                            variantKey: row.querySelector(".s-variant").value.trim(),
                            weight: parseInt(row.querySelector(".s-weight").value, 10) || 0,
                        };
                    }).filter(function (s) { return s.variantKey; });
                    var total = splits.reduce(function (a, s) { return a + s.weight; }, 0);
                    if (total !== 10000) { throw new Error("Rule '" + (rule.name || idx) + "': splits must sum to 10000 (got " + total + ")."); }
                    rule.outcome = { splits: splits };
                } else {
                    rule.outcome = { variantKey: card.querySelector(".r-variant").value };
                }
            } else {
                var raw = card.querySelector(".r-value").value;
                try { rule.value = JSON.parse(raw); }
                catch (_) { throw new Error("Rule '" + (rule.name || idx) + "': value must be valid JSON."); }
            }
            return rule;
        });
    }

    function collectConditions(listEl) {
        return Array.prototype.slice.call(listEl.querySelectorAll(".condition-row")).map(function (row) {
            var attr = row.querySelector(".c-attr").value.trim();
            var op = row.querySelector(".c-op").value;
            var raw = row.querySelector(".c-value").value;
            var value;
            try { value = JSON.parse(raw); } catch (_) { value = raw; }
            return {
                attribute: attr,
                operator: op,
                value: value,
                negate: row.querySelector(".c-negate").checked,
            };
        }).filter(function (c) { return c.attribute; });
    }

    // ============================================================
    // Preview ("test this context") panel
    // ============================================================
    function renderPreviewPanel(kind, entityKey) {
        var panelId = "preview-panel";
        var title = kind === "flag" ? "Test this context" : "Test this context";
        return '<section class="preview-panel" id="' + panelId + '">'
            + '<h2>' + esc(title) + '</h2>'
            + '<p class="muted">Server-side dry-run against the current saved ' + esc(kind) + ' &mdash; nothing is persisted.</p>'
            + '<div class="preview-fields">'
            + field("Targeting key", '<input class="preview-tkey" placeholder="alice@example.com" />')
            + '</div>'
            + '<h3 class="preview-h3">Attributes</h3>'
            + '<div class="preview-attrs"></div>'
            + '<button type="button" class="btn outline xs" data-action="preview-add-attr">+ Add attribute</button>'
            + '<div class="preview-actions">'
            + '  <button type="button" class="btn primary" data-action="preview-eval">Evaluate</button>'
            + '  <span class="preview-msg muted"></span>'
            + '</div>'
            + '<div class="preview-result hidden"></div>'
            + '</section>';
    }

    function renderPreviewAttrRow(attr) {
        attr = attr || { key: "", value: "" };
        return '<div class="preview-attr-row">'
            + '<input class="pa-key" placeholder="user.country" value="' + esc(attr.key) + '" />'
            + '<input class="pa-value" placeholder="value (JSON or text)" value="' + esc(attr.value) + '" />'
            + '<button type="button" class="icon-btn" data-action="preview-remove-attr" aria-label="Remove">' + icon("x") + '</button>'
            + '</div>';
    }

    function wirePreviewPanel(kind, entityKey) {
        var panel = document.getElementById("preview-panel");
        if (!panel) { return; }
        var attrsList = panel.querySelector(".preview-attrs");
        // Seed with one empty row so the user has something to type into.
        attrsList.insertAdjacentHTML("beforeend", renderPreviewAttrRow());

        panel.addEventListener("click", function (e) {
            var btn = e.target.closest("[data-action]");
            if (!btn) { return; }
            var action = btn.getAttribute("data-action");
            if (action === "preview-add-attr") {
                attrsList.insertAdjacentHTML("beforeend", renderPreviewAttrRow());
            } else if (action === "preview-remove-attr") {
                btn.closest(".preview-attr-row").remove();
            } else if (action === "preview-eval") {
                evaluatePreview(panel, kind, entityKey);
            }
        });
    }

    function evaluatePreview(panel, kind, entityKey) {
        var tkey = panel.querySelector(".preview-tkey").value.trim();
        var attrs = {};
        Array.prototype.slice.call(panel.querySelectorAll(".preview-attr-row")).forEach(function (row) {
            var k = row.querySelector(".pa-key").value.trim();
            if (!k) { return; }
            var raw = row.querySelector(".pa-value").value;
            var v;
            try { v = JSON.parse(raw); }
            catch (_) { v = raw; }
            attrs[k] = v;
        });
        var body = { targetingKey: tkey || null, attributes: attrs };
        var msg = panel.querySelector(".preview-msg");
        var resultEl = panel.querySelector(".preview-result");
        msg.textContent = "Evaluating…";
        resultEl.classList.add("hidden");

        var resource = kind === "flag" ? "flags" : "configs";
        api("POST", "/admin/preview/" + resource + "/" + encodeURIComponent(entityKey) + "?env=" + encodeURIComponent(currentEnv.key), body)
            .then(function (result) {
                msg.textContent = "";
                resultEl.classList.remove("hidden");
                resultEl.innerHTML = renderPreviewResult(result);
            })
            .catch(function (err) {
                if (err.kind === "auth") { showAuthPrompt(); return; }
                msg.textContent = err.message;
                msg.className = "preview-msg save-msg--error";
            });
    }

    function renderPreviewResult(result) {
        var reasonBadge = '<span class="preview-reason preview-reason--' + esc(result.reason || "Unknown") + '">' + esc(result.reason || "Unknown") + '</span>';
        var lines = [
            '<div class="preview-result__head">' + reasonBadge + (result.ruleMatched ? ' <span class="muted">rule: ' + esc(result.ruleMatched) + '</span>' : "") + '</div>',
            '<dl class="preview-result__dl">',
            '  <dt>Value</dt><dd>' + code(JSON.stringify(result.value)) + '</dd>',
        ];
        if (result.variantKey) {
            lines.push('  <dt>Variant</dt><dd>' + code(result.variantKey) + '</dd>');
        }
        if (result.error) {
            lines.push('  <dt>Error</dt><dd class="save-msg--error">' + esc(result.error) + '</dd>');
        }
        lines.push('</dl>');
        return lines.join("\n");
    }

    // ============================================================
    // Tiny utilities
    // ============================================================
    function field(label, html) {
        return '<label class="field"><span class="field__label">' + esc(label) + '</span>' + html + '</label>';
    }
    function htmlToElement(html) {
        var d = document.createElement("div");
        d.innerHTML = html.trim();
        return d.firstChild;
    }
    function parseCsv(s) {
        return String(s || "").split(",").map(function (x) { return x.trim(); }).filter(Boolean);
    }
    function cryptoId() {
        return (crypto && crypto.randomUUID) ? crypto.randomUUID() : "id-" + Math.random().toString(36).slice(2);
    }

    // ============================================================
    // Boot
    // ============================================================
    function boot() {
        if (!isAuthenticated()) { showAuthPrompt(); return; }
        var who = session.displayName || session.identifier || "Signed in";
        if (sbUserName) { sbUserName.textContent = who; }
        if (sbUserRole) { sbUserRole.textContent = session.identifier || "Admin"; }
        if (sbAvatar) { sbAvatar.textContent = (who.charAt(0) || "F"); }
        refreshNotifBadge();
        loadEnvironments().then(render).catch(function (err) {
            if (err.kind === "auth") { showAuthPrompt(); }
            else { viewEl.innerHTML = '<h1>Boot error</h1><div class="error">' + esc(err.message) + '</div>'; }
        });
    }
    // Fill the static shell icons and wire the theme toggle before first paint.
    hydrateIcons();
    initTheme();

    // Probe for an existing session before showing the login screen — a page
    // refresh shouldn't make the user re-paste their key.
    probeSession().then(function (ok) { ok ? boot() : showAuthPrompt(); });
})();
