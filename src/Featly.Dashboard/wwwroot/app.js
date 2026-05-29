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
    var navLinks = Array.prototype.slice.call(document.querySelectorAll(".nav__link"));
    var headerEl = document.querySelector(".app-header");

    // Tracks whether /api/auth/me has confirmed an active session this load.
    // The cookie itself is HttpOnly so JS can't read it directly — we infer
    // session state by hitting /me and remembering the result.
    var session = null;

    var sessionLabel = document.createElement("span");
    sessionLabel.className = "session-label muted";
    sessionLabel.hidden = true;
    if (headerEl) { headerEl.appendChild(sessionLabel); }

    var signOutBtn = document.createElement("button");
    signOutBtn.type = "button";
    signOutBtn.className = "btn-link";
    signOutBtn.textContent = "Sign out";
    signOutBtn.hidden = true;
    signOutBtn.addEventListener("click", function () {
        // POST /logout clears the cookie server-side; we then reload so the
        // login screen shows. credentials: 'include' is mandatory — without
        // it the browser would skip the cookie and the server can't know
        // which session to expire.
        fetch("/api/auth/logout", { method: "POST", credentials: "include" })
            .finally(function () {
                session = null;
                location.reload();
            });
    });
    if (headerEl) { headerEl.appendChild(signOutBtn); }

    // ============================================================
    // Auth
    // ============================================================
    function isAuthenticated() { return session !== null; }

    function showAuthPrompt(errorText) {
        envSelect.disabled = true;
        signOutBtn.hidden = true;
        sessionLabel.hidden = true;
        viewEl.innerHTML = [
            '<h1>Sign in to Featly</h1>',
            '<div class="card auth-card">',
            '  <p>Enter an admin API key to start a dashboard session. Look up <code>Featly:Server:AdminApiKey</code> in <code>appsettings.json</code>, or use a key minted via the admin keys endpoint.</p>',
            '  <form id="auth-form" class="auth-form">',
            '    <label for="auth-token">Admin API key</label>',
            '    <input id="auth-token" type="password" autocomplete="off" required spellcheck="false" />',
            '    <button type="submit" class="btn-primary">Sign in</button>',
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
    }

    envSelect.addEventListener("change", function () {
        var picked = environments.find(function (e) { return e.key === envSelect.value; });
        if (!picked) { return; }
        currentEnv = picked;
        try { localStorage.setItem(STORAGE_ENV_KEY, picked.key); } catch (_) {}
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
    // Views
    // ============================================================
    function render() {
        if (!isAuthenticated()) { showAuthPrompt(); return; }
        var route = currentRoute();
        var view = views[route.key];
        if (!view) { views.overview(); return; }
        navLinks.forEach(function (link) {
            if (link.getAttribute("data-route") === route.navRoute) {
                link.setAttribute("aria-current", "page");
            } else {
                link.removeAttribute("aria-current");
            }
        });
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
        flagList:    function () { renderList("flags", "Flags", flagCols); },
        configList:  function () { renderList("configs", "Configs", configCols); },
        segmentList: function () { renderList("segments", "Segments", segmentCols); },
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
        settings: function () {
            viewEl.innerHTML = '<h1>Settings</h1><div class="placeholder"><p>Environment defaults, approval policy, webhook configuration, audit retention.</p><p class="muted">M6+ unlocks the editor.</p></div>';
        },
    };

    // ---------- List views ----------
    var flagCols = [
        { label: "Key", get: function (f) { return '<a data-link="/flags/' + encodeURIComponent(f.key) + '">' + code(f.key) + '</a>'; } },
        { label: "Name", get: function (f) { return esc(f.name); } },
        { label: "Type", get: function (f) { return badge(f.type); } },
        { label: "Enabled", get: function (f) { return f.enabled ? '<span class="dot dot--on"></span> on' : '<span class="dot dot--off"></span> off'; } },
        { label: "Variants", get: function (f) { return (f.variants || []).length; } },
        { label: "Rules", get: function (f) { return (f.rules || []).length; } },
        { label: "Updated", get: function (f) { return formatDate(f.updatedAt); } },
    ];
    var configCols = [
        { label: "Key", get: function (c) { return '<a data-link="/configs/' + encodeURIComponent(c.key) + '">' + code(c.key) + '</a>'; } },
        { label: "Name", get: function (c) { return esc(c.name); } },
        { label: "Type", get: function (c) { return badge(c.type); } },
        { label: "Default", get: function (c) { return code(truncate(JSON.stringify(c.defaultValue), 40)); } },
        { label: "Rules", get: function (c) { return (c.rules || []).length; } },
        { label: "Updated", get: function (c) { return formatDate(c.updatedAt); } },
    ];
    var segmentCols = [
        { label: "Key", get: function (s) { return '<a data-link="/segments/' + encodeURIComponent(s.key) + '">' + code(s.key) + '</a>'; } },
        { label: "Name", get: function (s) { return esc(s.name); } },
        { label: "Conditions", get: function (s) { return (s.conditions || []).length; } },
        { label: "Updated", get: function (s) { return formatDate(s.updatedAt); } },
    ];

    function renderList(resource, title, columns) {
        if (!currentEnv) {
            viewEl.innerHTML = '<h1>' + title + '</h1><div class="placeholder"><p>No environment selected yet.</p></div>';
            return;
        }
        viewEl.innerHTML = '<h1>' + title + ' <span class="muted">/ ' + esc(currentEnv.key) + '</span></h1><div class="card"><p class="muted">Loading…</p></div>';
        api("GET", "/admin/" + resource + "?env=" + encodeURIComponent(currentEnv.key))
            .then(function (rows) { renderTable(title, columns, rows); })
            .catch(handleErrOnView(title));
    }

    function renderTable(title, columns, rows) {
        var heading = '<h1>' + title + ' <span class="muted">/ ' + esc(currentEnv.key) + '</span></h1>';
        if (!rows || rows.length === 0) {
            viewEl.innerHTML = heading + '<div class="placeholder"><p>No ' + title.toLowerCase() + ' in this environment yet.</p><p class="muted">POST via the admin API to create one.</p></div>';
            return;
        }
        var thead = '<thead><tr>' + columns.map(function (c) { return '<th>' + esc(c.label) + '</th>'; }).join("") + '</tr></thead>';
        var tbody = '<tbody>' + rows.map(function (row) {
            return '<tr>' + columns.map(function (c) { return '<td>' + c.get(row) + '</td>'; }).join("") + '</tr>';
        }).join("") + '</tbody>';
        viewEl.innerHTML = heading + '<div class="table-wrap"><table class="table">' + thead + tbody + '</table></div>';
    }

    function handleErrOnView(title) {
        return function (err) {
            if (err.kind === "auth") { showAuthPrompt(); return; }
            viewEl.innerHTML = '<h1>' + title + '</h1><div class="error">' + esc(err.message) + '</div>';
        };
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
        return '<a data-link="/' + kind.toLowerCase() + 's" class="back-link">← ' + kind + 's</a>'
            + '<h1>' + esc(key) + '</h1>'
            + '<div class="card"><p class="muted">Loading…</p></div>';
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

        viewEl.innerHTML = [
            '<a data-link="/flags" class="back-link">← Flags</a>',
            '<h1>' + code(flag.key) + ' <span class="muted">/ ' + esc(currentEnv.key) + '</span></h1>',
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
            '<button type="button" class="btn-ghost" data-action="add-variant">+ Add variant</button>',
            '<h2>Rules</h2>',
            renderRulesEditor(flag.rules || [], { kind: "flag", variants: flag.variants || [] }),
            '<button type="button" class="btn-ghost" data-action="add-rule">+ Add rule</button>',
            '<div class="editor__footer">',
            '  <button type="submit" class="btn-primary">Save flag</button>',
            '  <span class="save-msg" id="save-msg"></span>',
            '</div>',
            '</form>',
            renderPreviewPanel("flag", flag.key),
            auditFooter(flag),
        ].join("\n");

        wireFlagEditor(flag);
        wirePreviewPanel("flag", flag.key);
    }

    function renderVariantRow(v) {
        return '<div class="variant-row">'
            + '<input class="v-key" placeholder="key" value="' + esc(v.key) + '" />'
            + '<input class="v-name" placeholder="name" value="' + esc(v.name) + '" />'
            + '<input class="v-value" placeholder="value (JSON)" value="' + esc(JSON.stringify(v.value)) + '" />'
            + '<button type="button" class="btn-icon" data-action="remove-variant" aria-label="Remove">×</button>'
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
            '<a data-link="/configs" class="back-link">← Configs</a>',
            '<h1>' + code(config.key) + ' <span class="muted">/ ' + esc(currentEnv.key) + '</span></h1>',
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
            '<button type="button" class="btn-ghost" data-action="add-rule">+ Add rule</button>',
            '<div class="editor__footer">',
            '  <button type="submit" class="btn-primary">Save config</button>',
            '  <span class="save-msg" id="save-msg"></span>',
            '</div>',
            '</form>',
            renderPreviewPanel("config", config.key),
            auditFooter(config),
        ].join("\n");

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
            '<a data-link="/segments" class="back-link">← Segments</a>',
            '<h1>' + code(segment.key) + ' <span class="muted">/ ' + esc(currentEnv.key) + '</span></h1>',
            '<form id="segment-form" class="editor">',
            field("Name", '<input name="name" required value="' + esc(segment.name) + '" />'),
            field("Description", '<textarea name="description" rows="2">' + esc(segment.description || "") + '</textarea>'),
            '<h2>Conditions</h2>',
            '<div class="conditions-list">' + (segment.conditions || []).map(renderConditionRow).join("") + '</div>',
            '<button type="button" class="btn-ghost" data-action="add-condition">+ Add condition</button>',
            '<div class="editor__footer">',
            '  <button type="submit" class="btn-primary">Save segment</button>',
            '  <span class="save-msg" id="save-msg"></span>',
            '</div>',
            '</form>',
            auditFooter(segment),
        ].join("\n");

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
    function renderUserList() {
        viewEl.innerHTML = '<h1>Users</h1><div class="card"><p class="muted">Loading…</p></div>';
        api("GET", "/admin/users")
            .then(function (users) {
                users = Array.isArray(users) ? users : [];
                if (users.length === 0) {
                    viewEl.innerHTML = '<h1>Users</h1><div class="placeholder"><p>No users yet.</p><p class="muted">Users are auto-provisioned on first sign-in, or created via the admin API.</p></div>';
                    return;
                }
                var rows = users.map(function (u) {
                    return '<tr>'
                        + '<td><a data-link="/users/' + encodeURIComponent(u.identifier) + '">' + esc(u.displayName || u.identifier) + '</a></td>'
                        + '<td>' + code(u.identifier) + '</td>'
                        + '<td>' + esc(u.email || "—") + '</td>'
                        + '<td>' + (u.disabled ? '<span class="dot dot--off"></span> disabled' : '<span class="dot dot--on"></span> active') + '</td>'
                        + '<td>' + formatDate(u.createdAt) + '</td>'
                        + '</tr>';
                }).join("");
                viewEl.innerHTML = '<h1>Users</h1><div class="table-wrap"><table class="table">'
                    + '<thead><tr><th>Name</th><th>Identifier</th><th>Email</th><th>Status</th><th>Created</th></tr></thead>'
                    + '<tbody>' + rows + '</tbody></table></div>';
            })
            .catch(handleErrOnView("Users"));
    }

    function renderUserDetail(identifier) {
        viewEl.innerHTML = '<a data-link="/users" class="back-link">← Users</a><h1>' + esc(identifier) + '</h1><div class="card"><p class="muted">Loading…</p></div>';
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
                '<a data-link="/users" class="back-link">← Users</a>',
                '<h1>' + esc(user.displayName || user.identifier) + '</h1>',
                '<div class="card">',
                '  <dl class="preview-result__dl">',
                '    <dt>Identifier</dt><dd>' + code(user.identifier) + '</dd>',
                '    <dt>Email</dt><dd>' + esc(user.email || "—") + '</dd>',
                '    <dt>Status</dt><dd>' + (user.disabled ? "disabled" : "active") + '</dd>',
                '  </dl>',
                '</div>',
                '<h2>Effective access</h2>',
                '<p class="muted">Why this user has the access they do — the union of every role granted by a matching assignment (direct or via a group), in the default project.</p>',
                roleRows
                    ? '<div class="table-wrap"><table class="table"><thead><tr><th>Role</th><th>Name</th><th>Via</th><th>Environment</th></tr></thead><tbody>' + roleRows + '</tbody></table></div>'
                    : '<div class="placeholder"><p>No role assignments in this project.</p></div>',
                '<h2>Permissions <span class="muted">(' + perms.length + ')</span></h2>',
                perms.length
                    ? '<div class="card"><p>' + perms.map(function (p) { return badge(p); }).join(" ") + '</p></div>'
                    : '<div class="placeholder"><p>No effective permissions in this scope.</p></div>',
            ].join("\n");
        }).catch(handleErrOnView("User: " + identifier));
    }

    function renderRoleList() {
        viewEl.innerHTML = '<h1>Roles</h1><div class="card"><p class="muted">Loading…</p></div>';
        api("GET", "/admin/roles")
            .then(function (roles) {
                roles = Array.isArray(roles) ? roles : [];
                var rows = roles.map(function (r) {
                    return '<tr>'
                        + '<td>' + code(r.key) + '</td>'
                        + '<td>' + esc(r.name) + '</td>'
                        + '<td>' + (r.isSystem ? badge("system") : badge("custom")) + '</td>'
                        + '<td>' + (r.permissions || []).length + '</td>'
                        + '<td class="muted">' + esc(truncate((r.permissions || []).join(", "), 80)) + '</td>'
                        + '</tr>';
                }).join("");
                viewEl.innerHTML = '<h1>Roles</h1>'
                    + '<p class="muted">System roles are immutable. Create a custom role by cloning a system template via <code>POST /api/admin/roles</code>.</p>'
                    + '<div class="table-wrap"><table class="table">'
                    + '<thead><tr><th>Key</th><th>Name</th><th>Kind</th><th>Permissions</th><th>Sample</th></tr></thead>'
                    + '<tbody>' + rows + '</tbody></table></div>';
            })
            .catch(handleErrOnView("Roles"));
    }

    // ============================================================
    // Experiments (M9): list, detail with analytics + bar charts
    // ============================================================
    function experimentStatus(exp) {
        if (exp.isActive) { return '<span class="preview-reason preview-reason--TargetingMatch">running</span>'; }
        if (exp.stoppedAt) { return '<span class="preview-reason preview-reason--Disabled">stopped</span>'; }
        return '<span class="preview-reason preview-reason--Default">draft</span>';
    }

    function renderExperimentList() {
        if (!currentEnv) {
            viewEl.innerHTML = '<h1>Experiments</h1><div class="placeholder"><p>No environment selected yet.</p></div>';
            return;
        }
        viewEl.innerHTML = '<h1>Experiments <span class="muted">/ ' + esc(currentEnv.key) + '</span></h1><div class="card"><p class="muted">Loading…</p></div>';
        api("GET", "/admin/experiments?env=" + encodeURIComponent(currentEnv.key))
            .then(function (rows) {
                rows = Array.isArray(rows) ? rows : [];
                var heading = '<h1>Experiments <span class="muted">/ ' + esc(currentEnv.key) + '</span></h1>'
                    + '<p class="muted">A/B experiments layered on a flag. Start one to begin collecting exposures; track conversions via <code>IEventClient.TrackAsync</code>.</p>';
                if (rows.length === 0) {
                    viewEl.innerHTML = heading + '<div class="placeholder"><p>No experiments in this environment yet.</p><p class="muted">Create one via <code>POST /api/admin/experiments</code>.</p></div>';
                    return;
                }
                var body = rows.map(function (e) {
                    return '<tr>'
                        + '<td><a data-link="/experiments/' + encodeURIComponent(e.key) + '">' + code(e.key) + '</a></td>'
                        + '<td>' + esc(e.name) + '</td>'
                        + '<td>' + code(e.flagKey) + '</td>'
                        + '<td>' + experimentStatus(e) + '</td>'
                        + '<td>' + (e.metricKeys || []).length + '</td>'
                        + '<td>' + (e.startedAt ? formatDate(e.startedAt) : '<span class="muted">—</span>') + '</td>'
                        + '</tr>';
                }).join("");
                viewEl.innerHTML = heading
                    + '<div class="table-wrap"><table class="table">'
                    + '<thead><tr><th>Key</th><th>Name</th><th>Flag</th><th>Status</th><th>Metrics</th><th>Started</th></tr></thead>'
                    + '<tbody>' + body + '</tbody></table></div>';
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
                ? '<button type="button" class="btn-ghost" data-exp="stop">Stop experiment</button>'
                : '<button type="button" class="btn-primary" data-exp="start">' + (exp.stoppedAt ? "Restart" : "Start") + ' experiment</button>';

            viewEl.innerHTML = [
                '<a data-link="/experiments" class="back-link">← Experiments</a>',
                '<h1>' + code(exp.key) + ' <span class="muted">/ ' + esc(currentEnv.key) + '</span></h1>',
                '<div class="card"><dl class="preview-result__dl">',
                '  <dt>Name</dt><dd>' + esc(exp.name) + '</dd>',
                exp.hypothesis ? '  <dt>Hypothesis</dt><dd>' + esc(exp.hypothesis) + '</dd>' : '',
                '  <dt>Flag</dt><dd><a data-link="/flags/' + encodeURIComponent(exp.flagKey) + '">' + code(exp.flagKey) + '</a></dd>',
                '  <dt>Status</dt><dd>' + experimentStatus(exp) + '</dd>',
                '  <dt>Sticky</dt><dd>' + (exp.stickyAssignments ? "yes — subjects keep their first variant" : "no") + '</dd>',
                '  <dt>Metric keys</dt><dd>' + ((exp.metricKeys || []).length ? (exp.metricKeys).map(function (m) { return code(m); }).join(" ") : '<span class="muted">none</span>') + '</dd>',
                '  <dt>Started</dt><dd>' + (exp.startedAt ? formatDate(exp.startedAt) : '<span class="muted">—</span>') + '</dd>',
                '  <dt>Stopped</dt><dd>' + (exp.stoppedAt ? formatDate(exp.stoppedAt) : '<span class="muted">—</span>') + '</dd>',
                '</dl>',
                '<div class="cr-actions">' + startStop + '<span class="save-msg" id="exp-msg"></span></div>',
                '</div>',
                '<h2>Analytics</h2>',
                renderExperimentAnalytics(exp, analytics),
            ].join("\n");

            wireExperimentDetail(exp);
        }).catch(handleErrOnView("Experiment: " + key));
    }

    function renderExperimentAnalytics(exp, a) {
        if (!a || !a.variants || a.variants.length === 0) {
            return '<div class="placeholder"><p>No exposures recorded yet.</p>'
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
    function renderInbox() {
        viewEl.innerHTML = '<h1>Inbox</h1><div class="card"><p class="muted">Loading…</p></div>';
        Promise.all([
            api("GET", "/admin/changes?status=Pending").catch(function () { return []; }),
            api("GET", "/admin/role-upgrade-requests?status=Pending").catch(function () { return []; }),
        ]).then(function (res) {
            var changes = Array.isArray(res[0]) ? res[0] : [];
            var upgrades = Array.isArray(res[1]) ? res[1] : [];

            var changeRows = changes.map(function (c) {
                return '<tr>'
                    + '<td><a data-link="/inbox/' + encodeURIComponent(c.id) + '">' + badge(c.entityType) + ' ' + code(c.entityKey) + '</a></td>'
                    + '<td>' + esc(c.action) + '</td>'
                    + '<td>' + (c.approvals || []).length + ' approval(s)</td>'
                    + '<td>' + formatDate(c.createdAt) + '</td>'
                    + '</tr>';
            }).join("");

            var upgradeRows = upgrades.map(function (u) {
                return '<tr><td>' + code(truncate(u.userId, 8)) + '</td><td>' + esc(u.justification || "—") + '</td><td>' + formatDate(u.createdAt) + '</td></tr>';
            }).join("");

            viewEl.innerHTML = [
                '<h1>Inbox</h1>',
                '<h2>Pending changes <span class="muted">(' + changes.length + ')</span></h2>',
                changeRows
                    ? '<div class="table-wrap"><table class="table"><thead><tr><th>Entity</th><th>Action</th><th>Approvals</th><th>Proposed</th></tr></thead><tbody>' + changeRows + '</tbody></table></div>'
                    : '<div class="placeholder"><p>No pending changes.</p></div>',
                '<h2>Role upgrade requests <span class="muted">(' + upgrades.length + ')</span></h2>',
                upgradeRows
                    ? '<div class="table-wrap"><table class="table"><thead><tr><th>User</th><th>Justification</th><th>Filed</th></tr></thead><tbody>' + upgradeRows + '</tbody></table></div>'
                    : '<div class="placeholder"><p>No pending role upgrade requests.</p></div>',
            ].join("\n");
        }).catch(handleErrOnView("Inbox"));
    }

    function renderChangeDetail(id) {
        viewEl.innerHTML = '<a data-link="/inbox" class="back-link">← Inbox</a><h1>Change</h1><div class="card"><p class="muted">Loading…</p></div>';
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
                    '<a data-link="/inbox" class="back-link">← Inbox</a>',
                    '<h1>' + badge(c.entityType) + ' ' + code(c.entityKey) + '</h1>',
                    '<div class="card"><dl class="preview-result__dl">',
                    '  <dt>Action</dt><dd>' + esc(c.action) + '</dd>',
                    '  <dt>Status</dt><dd>' + '<span class="preview-reason preview-reason--' + esc(c.status) + '">' + esc(c.status) + '</span>' + (c.wasEmergencyBypass ? ' ' + badge("emergency") : '') + '</dd>',
                    '  <dt>Author</dt><dd>' + code(truncate(c.authorUserId, 8)) + '</dd>',
                    c.authorMessage ? '  <dt>Message</dt><dd>' + esc(c.authorMessage) + '</dd>' : '',
                    '</dl></div>',
                    '<h2>Diff</h2>',
                    '<div class="cr-diff">',
                    '  <div><h3 class="preview-h3">Current</h3><pre class="cr-json">' + esc(prettyJson(c.currentState)) + '</pre></div>',
                    '  <div><h3 class="preview-h3">Proposed</h3><pre class="cr-json">' + esc(prettyJson(c.proposedState)) + '</pre></div>',
                    '</div>',
                    approvals ? '<h2>Approvals</h2><ul class="cr-approvals">' + approvals + '</ul>' : '',
                    '<h2>Comments</h2>',
                    '<div class="cr-comments">' + (comments || '<p class="muted">No comments yet.</p>') + '</div>',
                    '<form id="cr-comment-form" class="cr-actions"><input id="cr-comment" placeholder="Add a comment…" /><button type="submit" class="btn-ghost btn-small">Comment</button></form>',
                    '<h2>Decision</h2>',
                    '<div class="cr-actions">' + actions + '<span class="save-msg" id="cr-msg"></span></div>',
                ].join("\n");
                wireChangeDetail(c);
            })
            .catch(handleErrOnView("Change"));
    }

    function changeActionButtons(c) {
        var buttons = [];
        if (c.status === "Pending") {
            buttons.push('<button type="button" class="btn-primary" data-cr="approve">Approve</button>');
            buttons.push('<button type="button" class="btn-ghost" data-cr="reject">Reject</button>');
        }
        if (c.status === "Approved") {
            buttons.push('<button type="button" class="btn-primary" data-cr="apply">Apply</button>');
        }
        if (c.status === "Pending" || c.status === "Approved") {
            buttons.push('<button type="button" class="btn-ghost" data-cr="bypass">Emergency bypass</button>');
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

    function renderApprovalsEditor() {
        if (!currentEnv) {
            viewEl.innerHTML = '<h1>Approvals</h1><div class="placeholder"><p>No environment selected.</p></div>';
            return;
        }
        var envKey = currentEnv.key;
        viewEl.innerHTML = '<h1>Approvals <span class="muted">/ ' + esc(envKey) + '</span></h1><div class="card"><p class="muted">Loading…</p></div>';
        api("GET", "/admin/approval-policies/" + encodeURIComponent(envKey))
            .then(function (p) {
                var rules = (p.approverRules || []).map(function (r) {
                    return '<li>' + badge(r.type) + (r.mandatory ? ' ' + badge("mandatory") : '') + ' min ' + (r.minFromThisRule || 1) + '</li>';
                }).join("");
                viewEl.innerHTML = [
                    '<h1>Approvals <span class="muted">/ ' + esc(envKey) + '</span></h1>',
                    '<p class="muted">When approval is required, mutations to this environment become pending changes that need sign-off before they apply.</p>',
                    '<form id="policy-form" class="editor">',
                    field("Require approval", '<label class="check"><input type="checkbox" name="required"' + (p.required ? " checked" : "") + ' /> Mutations require approval</label>'),
                    field("Minimum approvals", '<input name="minApprovals" type="number" min="1" value="' + esc(p.minApprovals || 1) + '" />'),
                    field("Self-approval", '<label class="check"><input type="checkbox" name="self"' + (p.authorCanApproveOwnChange ? " checked" : "") + ' /> Author may approve their own change</label>'),
                    field("Emergency bypass", '<label class="check"><input type="checkbox" name="bypass"' + (p.allowEmergencyBypass ? " checked" : "") + ' /> Allow emergency bypass</label>'),
                    '<div class="editor__footer"><button type="submit" class="btn-primary">Save policy</button><span class="save-msg" id="policy-msg"></span></div>',
                    '</form>',
                    rules ? '<h2>Approver rules</h2><ul class="cr-approvals">' + rules + '</ul>' : '<p class="muted">No structured approver rules — a flat minimum-approvals count applies.</p>',
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
        var cls = status === "Succeeded" ? "TargetingMatch" : (status === "Dead" ? "NotFound" : "Default");
        return '<span class="preview-reason preview-reason--' + cls + '">' + esc(status) + '</span>';
    }

    function renderWebhookList() {
        viewEl.innerHTML = '<h1>Webhooks</h1><div class="card"><p class="muted">Loading…</p></div>';
        api("GET", "/admin/webhooks")
            .then(function (hooks) {
                hooks = Array.isArray(hooks) ? hooks : [];
                var rows = hooks.map(function (w) {
                    var types = (w.eventTypes || []).length ? (w.eventTypes).map(code).join(" ") : '<span class="muted">all events</span>';
                    return '<tr>'
                        + '<td><a data-link="/webhooks/' + encodeURIComponent(w.id) + '">' + esc(w.name) + '</a></td>'
                        + '<td>' + code(truncate(w.url, 48)) + '</td>'
                        + '<td>' + (w.enabled ? '<span class="dot dot--on"></span> on' : '<span class="dot dot--off"></span> off') + '</td>'
                        + '<td>' + types + '</td>'
                        + '</tr>';
                }).join("");
                viewEl.innerHTML = [
                    '<h1>Webhooks</h1>',
                    '<p class="muted">Outbound HTTP notifications. Each delivery is signed with the endpoint secret (<code>X-Featly-Signature: sha256=…</code>, HMAC-SHA256) and retried with backoff.</p>',
                    hooks.length
                        ? '<div class="table-wrap"><table class="table"><thead><tr><th>Name</th><th>URL</th><th>Enabled</th><th>Events</th></tr></thead><tbody>' + rows + '</tbody></table></div>'
                        : '<div class="placeholder"><p>No webhooks yet.</p></div>',
                    '<h2>New webhook</h2>',
                    '<form id="wh-form" class="editor">',
                    field("Name", '<input name="name" required placeholder="Slack relay" />'),
                    field("URL", '<input name="url" required placeholder="https://example.com/hook" />'),
                    field("Event types", '<input name="eventTypes" placeholder="flag.updated, change.applied (blank = all)" />'),
                    field("Secret", '<input name="secret" placeholder="(blank = auto-generate)" />'),
                    '<div class="editor__footer"><button type="submit" class="btn-primary">Create webhook</button><span class="save-msg" id="wh-msg"></span></div>',
                    '</form>',
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
        viewEl.innerHTML = '<a data-link="/webhooks" class="back-link">← Webhooks</a><h1>Webhook</h1><div class="card"><p class="muted">Loading…</p></div>';
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
                    + '<td>' + (d.attemptCount || 0) + '</td>'
                    + '<td>' + (d.lastStatusCode != null ? d.lastStatusCode : '—') + '</td>'
                    + '<td class="muted">' + esc(truncate(d.lastError || "", 48)) + '</td>'
                    + '<td>' + formatDate(d.createdAt) + '</td>'
                    + '</tr>';
            }).join("");

            viewEl.innerHTML = [
                '<a data-link="/webhooks" class="back-link">← Webhooks</a>',
                '<h1>' + esc(w.name) + '</h1>',
                '<form id="wh-edit" class="editor">',
                field("Name", '<input name="name" required value="' + esc(w.name) + '" />'),
                field("URL", '<input name="url" required value="' + esc(w.url) + '" />'),
                field("Enabled", '<label class="check"><input type="checkbox" name="enabled"' + (w.enabled ? " checked" : "") + ' /> Deliver events</label>'),
                field("Event types", '<input name="eventTypes" value="' + esc((w.eventTypes || []).join(", ")) + '" placeholder="(blank = all)" />'),
                field("Secret", '<input name="secret" value="' + esc(w.secret || "") + '" />'),
                '<div class="editor__footer">',
                '  <button type="submit" class="btn-primary">Save</button>',
                '  <button type="button" class="btn-ghost" data-wh="test">Send test event</button>',
                '  <button type="button" class="btn-ghost btn-danger" data-wh="delete">Delete</button>',
                '  <span class="save-msg" id="wh-msg"></span>',
                '</div>',
                '</form>',
                '<h2>Recent deliveries</h2>',
                deliveryRows
                    ? '<div class="table-wrap"><table class="table"><thead><tr><th>Event</th><th>Status</th><th>Attempts</th><th>Code</th><th>Last error</th><th>Created</th></tr></thead><tbody>' + deliveryRows + '</tbody></table></div>'
                    : '<div class="placeholder"><p>No deliveries yet. Use “Send test event” to enqueue one.</p></div>',
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

    function renderAuditLog() {
        viewEl.innerHTML = [
            '<h1>Audit log</h1>',
            '<form id="audit-filter" class="audit-filter">',
            '  <input name="entityType" placeholder="Entity type (e.g. Flag)" />',
            '  <input name="actor" placeholder="Actor" />',
            '  <button type="submit" class="btn-ghost btn-small">Filter</button>',
            '</form>',
            '<div id="audit-results"><div class="card"><p class="muted">Loading…</p></div></div>',
        ].join("\n");

        function load(entityType, actor) {
            var qs = [];
            if (entityType) { qs.push("entityType=" + encodeURIComponent(entityType)); }
            if (actor) { qs.push("actor=" + encodeURIComponent(actor)); }
            var path = "/admin/audit" + (qs.length ? "?" + qs.join("&") : "");
            api("GET", path).then(function (entries) {
                entries = Array.isArray(entries) ? entries : [];
                var resultsEl = document.getElementById("audit-results");
                if (entries.length === 0) {
                    resultsEl.innerHTML = '<div class="placeholder"><p>No audit entries match.</p></div>';
                    return;
                }
                var rows = entries.map(function (a) {
                    return '<tr>'
                        + '<td>' + formatDate(a.at) + '</td>'
                        + '<td>' + code(a.action) + '</td>'
                        + '<td>' + badge(a.entityType) + ' ' + (a.entityKey ? code(truncate(a.entityKey, 24)) : '') + '</td>'
                        + '<td>' + esc(a.actorIdentifier || "—") + '</td>'
                        + '</tr>';
                }).join("");
                resultsEl.innerHTML = '<div class="table-wrap"><table class="table"><thead><tr><th>When</th><th>Action</th><th>Entity</th><th>Actor</th></tr></thead><tbody>' + rows + '</tbody></table></div>';
            }).catch(handleErrOnView("Audit log"));
        }

        document.getElementById("audit-filter").addEventListener("submit", function (e) {
            e.preventDefault();
            load(e.target.entityType.value.trim(), e.target.actor.value.trim());
        });
        load("", "");
    }

    function parseCsv(value) {
        return String(value || "").split(",").map(function (s) { return s.trim(); }).filter(function (s) { return s.length > 0; });
    }

    // ============================================================
    // Rule editor (shared between Flag rules and Config rules)
    // ============================================================
    function renderRulesEditor(rules, context) {
        return '<div class="rules-list">' + rules.map(function (r) { return renderRuleCard(r, context); }).join("") + '</div>';
    }

    function renderRuleCard(rule, context) {
        var outcomeHtml;
        if (context.kind === "flag") {
            var hasSplits = !!(rule.outcome && rule.outcome.splits && rule.outcome.splits.length);
            var variantOpts = (context.variants || []).map(function (v) {
                return '<option value="' + esc(v.key) + '"' + (rule.outcome && rule.outcome.variantKey === v.key ? " selected" : "") + '>' + esc(v.key) + '</option>';
            }).join("");
            var splitsHtml = '<div class="splits' + (hasSplits ? "" : " hidden") + '">'
                + ((rule.outcome && rule.outcome.splits) || []).map(renderSplitRow).join("")
                + '<button type="button" class="btn-ghost btn-small" data-action="add-split">+ Add split</button>'
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

        return '<div class="rule-card" data-rule-id="' + esc(rule.id || cryptoId()) + '">'
            + '<div class="rule-card__head">'
            + '  <input class="r-name" placeholder="rule name" value="' + esc(rule.name || "") + '" />'
            + '  <label class="check"><input type="checkbox" class="r-enabled"' + (rule.enabled === false ? "" : " checked") + ' /> enabled</label>'
            + '  <div class="rule-card__buttons">'
            + '    <button type="button" class="btn-icon" data-action="rule-up" aria-label="Move up">↑</button>'
            + '    <button type="button" class="btn-icon" data-action="rule-down" aria-label="Move down">↓</button>'
            + '    <button type="button" class="btn-icon" data-action="rule-remove" aria-label="Remove">×</button>'
            + '  </div>'
            + '</div>'
            + '<div class="rule-card__conditions">'
            + '  <div class="conditions-list">' + (rule.conditions || []).map(renderConditionRow).join("") + '</div>'
            + '  <button type="button" class="btn-ghost btn-small" data-action="add-condition">+ Add condition</button>'
            + '</div>'
            + outcomeHtml
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
            + '<button type="button" class="btn-icon" data-action="remove-condition" aria-label="Remove">×</button>'
            + '</div>';
    }

    function renderSplitRow(s) {
        return '<div class="split-row">'
            + '<input class="s-variant" placeholder="variant" value="' + esc(s.variantKey || "") + '" />'
            + '<input class="s-weight" type="number" min="0" max="10000" placeholder="weight" value="' + esc(s.weight || 0) + '" />'
            + '<button type="button" class="btn-icon" data-action="remove-split" aria-label="Remove">×</button>'
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
            + '<button type="button" class="btn-ghost btn-small" data-action="preview-add-attr">+ Add attribute</button>'
            + '<div class="preview-actions">'
            + '  <button type="button" class="btn-primary" data-action="preview-eval">Evaluate</button>'
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
            + '<button type="button" class="btn-icon" data-action="preview-remove-attr" aria-label="Remove">×</button>'
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
        signOutBtn.hidden = false;
        sessionLabel.hidden = false;
        sessionLabel.textContent = "Signed in as " + (session.displayName || session.identifier);
        loadEnvironments().then(render).catch(function (err) {
            if (err.kind === "auth") { showAuthPrompt(); }
            else { viewEl.innerHTML = '<h1>Boot error</h1><div class="error">' + esc(err.message) + '</div>'; }
        });
    }
    // Probe for an existing session before showing the login screen — a page
    // refresh shouldn't make the user re-paste their key.
    probeSession().then(function (ok) { ok ? boot() : showAuthPrompt(); });
})();
