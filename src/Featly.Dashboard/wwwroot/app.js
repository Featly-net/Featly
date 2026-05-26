// Featly dashboard — M5B.
//
// Adds:
//   - Admin token auth (paste once, kept in localStorage, sent as Bearer
//     on every fetch). Pre-M6 bridge until the real auth pipeline lands.
//   - Environment selector populated from /api/admin/environments.
//   - Read-only list screens for Flags / Configs / Segments.
//   - Loading / error / empty / loaded states.
//
// Still vanilla JavaScript, no build step, no framework.

(function () {
    "use strict";

    // ---------- Config ----------
    var meta = document.querySelector('meta[name="featly-mount-path"]');
    var mountPath = (meta && meta.getAttribute("content")) || "/featly";
    if (mountPath.endsWith("/")) {
        mountPath = mountPath.slice(0, -1);
    }

    var STORAGE_TOKEN_KEY = "featly.adminToken";
    var STORAGE_ENV_KEY = "featly.envKey";

    // ---------- DOM ----------
    var viewEl = document.getElementById("view");
    var navLinks = Array.prototype.slice.call(document.querySelectorAll(".nav__link"));
    var envSelect = document.getElementById("env-select");

    // Add a "Sign out" affordance into the header.
    var brandRow = document.querySelector(".app-header");
    var signOutBtn = document.createElement("button");
    signOutBtn.type = "button";
    signOutBtn.className = "btn-link";
    signOutBtn.textContent = "Sign out";
    signOutBtn.hidden = true;
    signOutBtn.addEventListener("click", function () {
        localStorage.removeItem(STORAGE_TOKEN_KEY);
        location.reload();
    });
    if (brandRow) {
        brandRow.appendChild(signOutBtn);
    }

    // ---------- Auth ----------
    function getToken() {
        try { return localStorage.getItem(STORAGE_TOKEN_KEY); }
        catch (_) { return null; }
    }
    function setToken(value) {
        try { localStorage.setItem(STORAGE_TOKEN_KEY, value); }
        catch (_) { /* private mode */ }
    }
    function showAuthPrompt() {
        envSelect.disabled = true;
        signOutBtn.hidden = true;
        viewEl.innerHTML = [
            '<h1>Connect to Featly</h1>',
            '<div class="card auth-card">',
            '  <p>Paste your admin API key to use the dashboard. Look up <code>Featly:Server:AdminApiKey</code> in <code>appsettings.json</code>.</p>',
            '  <form id="auth-form" class="auth-form">',
            '    <label for="auth-token">Admin API key</label>',
            '    <input id="auth-token" type="password" autocomplete="off" required spellcheck="false" />',
            '    <button type="submit" class="btn-primary">Continue</button>',
            '  </form>',
            '  <p class="muted">Stored in <code>localStorage</code>. M6 replaces this with a real auth flow.</p>',
            '</div>',
        ].join("\n");
        document.getElementById("auth-form").addEventListener("submit", function (event) {
            event.preventDefault();
            var value = document.getElementById("auth-token").value.trim();
            if (!value) { return; }
            setToken(value);
            boot();
        });
    }

    // ---------- HTTP ----------
    function api(path) {
        var token = getToken();
        var url = path.startsWith("http") ? path : "/api" + path;
        return fetch(url, {
            headers: token ? { "Authorization": "Bearer " + token } : {},
        }).then(function (res) {
            if (res.status === 401 || res.status === 403) {
                var err = new Error("Unauthorized — check your admin token.");
                err.kind = "auth";
                throw err;
            }
            if (!res.ok) {
                return res.text().then(function (body) {
                    var err = new Error("Request failed (" + res.status + "): " + (body || res.statusText));
                    err.status = res.status;
                    throw err;
                });
            }
            return res.json();
        });
    }

    // ---------- Environments ----------
    var environments = [];
    var currentEnv = null;

    function loadEnvironments() {
        return api("/admin/environments").then(function (list) {
            environments = Array.isArray(list) ? list : [];
            var savedKey = null;
            try { savedKey = localStorage.getItem(STORAGE_ENV_KEY); } catch (_) {}
            var fromSaved = environments.find(function (e) { return e.key === savedKey; });
            var fromDefault = environments.find(function (e) { return e.isDefault; });
            currentEnv = fromSaved || fromDefault || environments[0] || null;
            renderEnvSelect();
        });
    }

    function renderEnvSelect() {
        envSelect.innerHTML = environments.map(function (e) {
            var selected = currentEnv && e.id === currentEnv.id ? " selected" : "";
            return '<option value="' + escapeAttr(e.key) + '"' + selected + '>' + escapeHtml(e.name || e.key) + (e.readOnly ? " (read-only)" : "") + '</option>';
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

    // ---------- Views ----------
    var views = {
        "/": function () {
            viewEl.innerHTML = [
                '<h1>Overview</h1>',
                '<div class="card">',
                '  <p>Welcome to Featly. M5B lights up the read-only listings; M5C brings the detail screens and rule editor.</p>',
                '  <p class="muted">All data here comes from <code>/api/admin/*</code> with your Bearer token. Targeting evaluation happens locally inside each SDK — the dashboard only reads and writes definitions.</p>',
                '</div>',
            ].join("\n");
        },
        "/flags": function () { renderList("flags", "Flags", flagColumns); },
        "/configs": function () { renderList("configs", "Configs", configColumns); },
        "/segments": function () { renderList("segments", "Segments", segmentColumns); },
        "/settings": function () {
            viewEl.innerHTML = [
                '<h1>Settings</h1>',
                '<div class="placeholder">',
                '  <p>Environment defaults, approval policy, webhook configuration, audit retention.</p>',
                '  <p class="muted">M6+ unlocks the editor.</p>',
                '</div>',
            ].join("\n");
        },
    };

    var flagColumns = [
        { label: "Key", get: function (f) { return code(f.key); } },
        { label: "Name", get: function (f) { return escapeHtml(f.name); } },
        { label: "Type", get: function (f) { return badge(f.type); } },
        { label: "Enabled", get: function (f) { return f.enabled ? '<span class="dot dot--on"></span> on' : '<span class="dot dot--off"></span> off'; } },
        { label: "Variants", get: function (f) { return (f.variants || []).length; } },
        { label: "Rules", get: function (f) { return (f.rules || []).length; } },
        { label: "Updated", get: function (f) { return formatDate(f.updatedAt); } },
    ];

    var configColumns = [
        { label: "Key", get: function (c) { return code(c.key); } },
        { label: "Name", get: function (c) { return escapeHtml(c.name); } },
        { label: "Type", get: function (c) { return badge(c.type); } },
        { label: "Default", get: function (c) { return code(truncate(JSON.stringify(c.defaultValue), 40)); } },
        { label: "Rules", get: function (c) { return (c.rules || []).length; } },
        { label: "Updated", get: function (c) { return formatDate(c.updatedAt); } },
    ];

    var segmentColumns = [
        { label: "Key", get: function (s) { return code(s.key); } },
        { label: "Name", get: function (s) { return escapeHtml(s.name); } },
        { label: "Conditions", get: function (s) { return (s.conditions || []).length; } },
        { label: "Updated", get: function (s) { return formatDate(s.updatedAt); } },
    ];

    function renderList(resource, title, columns) {
        if (!currentEnv) {
            viewEl.innerHTML = '<h1>' + title + '</h1><div class="placeholder"><p>No environment selected yet.</p></div>';
            return;
        }
        viewEl.innerHTML = '<h1>' + title + ' <span class="muted">/ ' + escapeHtml(currentEnv.key) + '</span></h1><div class="card"><p class="muted">Loading…</p></div>';

        api("/admin/" + resource + "?env=" + encodeURIComponent(currentEnv.key))
            .then(function (rows) {
                renderTable(title, columns, rows);
            })
            .catch(function (err) {
                if (err.kind === "auth") {
                    showAuthPrompt();
                    return;
                }
                viewEl.innerHTML = '<h1>' + title + '</h1><div class="error">' + escapeHtml(err.message) + '</div>';
            });
    }

    function renderTable(title, columns, rows) {
        var envSpan = '<h1>' + title + ' <span class="muted">/ ' + escapeHtml(currentEnv.key) + '</span></h1>';
        if (!rows || rows.length === 0) {
            viewEl.innerHTML = envSpan + '<div class="placeholder"><p>No ' + title.toLowerCase() + ' in this environment yet.</p><p class="muted">Create one via the admin API; the detail screen lands in M5C.</p></div>';
            return;
        }
        var head = '<thead><tr>' + columns.map(function (c) { return '<th>' + escapeHtml(c.label) + '</th>'; }).join("") + '</tr></thead>';
        var body = '<tbody>' + rows.map(function (row) {
            return '<tr>' + columns.map(function (c) { return '<td>' + c.get(row) + '</td>'; }).join("") + '</tr>';
        }).join("") + '</tbody>';
        viewEl.innerHTML = envSpan + '<div class="table-wrap"><table class="table">' + head + body + '</table></div>';
    }

    // ---------- Helpers ----------
    function escapeHtml(s) {
        return String(s == null ? "" : s).replace(/[&<>"']/g, function (c) {
            return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
        });
    }
    function escapeAttr(s) { return escapeHtml(s); }
    function code(s) { return '<code>' + escapeHtml(s) + '</code>'; }
    function badge(s) { return '<span class="badge">' + escapeHtml(s) + '</span>'; }
    function truncate(s, n) { s = String(s || ""); return s.length <= n ? s : s.slice(0, n - 1) + "…"; }
    function formatDate(iso) {
        if (!iso) { return ""; }
        var d = new Date(iso);
        if (isNaN(d.getTime())) { return escapeHtml(iso); }
        var fmt = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" });
        return '<time datetime="' + escapeAttr(iso) + '" title="' + escapeAttr(iso) + '">' + escapeHtml(fmt.format(d)) + '</time>';
    }

    // ---------- Router ----------
    function currentRoute() {
        var path = window.location.pathname;
        if (!path.startsWith(mountPath)) { return "/"; }
        var sub = path.slice(mountPath.length) || "/";
        if (!sub.startsWith("/")) { sub = "/" + sub; }
        if (sub.length > 1 && sub.endsWith("/")) { sub = sub.slice(0, -1); }
        return views[sub] ? sub : "/";
    }

    function render() {
        var route = currentRoute();
        views[route]();
        navLinks.forEach(function (link) {
            if (link.getAttribute("data-route") === route) {
                link.setAttribute("aria-current", "page");
            } else {
                link.removeAttribute("aria-current");
            }
        });
        document.title = route === "/" ? "Featly" : "Featly — " + route.slice(1);
    }

    function onNavClick(event) {
        var target = event.target.closest("a.nav__link");
        if (!target) { return; }
        if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0) { return; }
        event.preventDefault();
        var route = target.getAttribute("data-route");
        var newPath = route === "/" ? mountPath + "/" : mountPath + route;
        window.history.pushState({}, "", newPath);
        render();
        document.getElementById("main").focus({ preventScroll: false });
    }

    document.addEventListener("click", onNavClick);
    window.addEventListener("popstate", render);

    // ---------- Boot ----------
    function boot() {
        if (!getToken()) {
            showAuthPrompt();
            return;
        }
        signOutBtn.hidden = false;
        loadEnvironments()
            .then(render)
            .catch(function (err) {
                if (err.kind === "auth") {
                    showAuthPrompt();
                } else {
                    viewEl.innerHTML = '<h1>Boot error</h1><div class="error">' + escapeHtml(err.message) + '</div>';
                }
            });
    }

    boot();
})();
