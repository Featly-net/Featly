// Featly dashboard — M5C.
//
// Adds:
//   - Dynamic routing for /flags/:key, /configs/:key, /segments/:key.
//   - Detail views with edit-in-place: name, description, enabled, variants,
//     tags, default value (config), default variant (flag).
//   - Visual rule editor (shared between Flag rules and Config rules) and a
//     condition editor (also used for Segments).
//   - PUT to the admin API on save with success / error feedback.
//   - Refresh-on-focus so a second operator's edits show up when the tab
//     regains focus. (Full SSE live updates land alongside an admin stream
//     endpoint in a follow-up.)
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

    var STORAGE_TOKEN_KEY = "featly.adminToken";
    var STORAGE_ENV_KEY = "featly.envKey";

    var viewEl = document.getElementById("view");
    var envSelect = document.getElementById("env-select");
    var navLinks = Array.prototype.slice.call(document.querySelectorAll(".nav__link"));
    var headerEl = document.querySelector(".app-header");

    var signOutBtn = document.createElement("button");
    signOutBtn.type = "button";
    signOutBtn.className = "btn-link";
    signOutBtn.textContent = "Sign out";
    signOutBtn.hidden = true;
    signOutBtn.addEventListener("click", function () {
        localStorage.removeItem(STORAGE_TOKEN_KEY);
        location.reload();
    });
    if (headerEl) { headerEl.appendChild(signOutBtn); }

    // ============================================================
    // Auth
    // ============================================================
    function getToken() { try { return localStorage.getItem(STORAGE_TOKEN_KEY); } catch (_) { return null; } }
    function setToken(v) { try { localStorage.setItem(STORAGE_TOKEN_KEY, v); } catch (_) {} }

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
        document.getElementById("auth-form").addEventListener("submit", function (e) {
            e.preventDefault();
            var v = document.getElementById("auth-token").value.trim();
            if (!v) { return; }
            setToken(v);
            boot();
        });
    }

    // ============================================================
    // HTTP
    // ============================================================
    function api(method, path, body) {
        var token = getToken();
        var url = path.startsWith("http") ? path : "/api" + path;
        var headers = token ? { "Authorization": "Bearer " + token } : {};
        var init = { method: method, headers: headers };
        if (body !== undefined) {
            headers["Content-Type"] = "application/json";
            init.body = JSON.stringify(body);
        }
        return fetch(url, init).then(function (res) {
            if (res.status === 401 || res.status === 403) {
                var err = new Error("Unauthorized — check your admin token.");
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
        if (getToken() && currentEnv) { render(); }
    });

    // ============================================================
    // Views
    // ============================================================
    function render() {
        if (!getToken()) { showAuthPrompt(); return; }
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
        if (!getToken()) { showAuthPrompt(); return; }
        signOutBtn.hidden = false;
        loadEnvironments().then(render).catch(function (err) {
            if (err.kind === "auth") { showAuthPrompt(); }
            else { viewEl.innerHTML = '<h1>Boot error</h1><div class="error">' + esc(err.message) + '</div>'; }
        });
    }
    boot();
})();
