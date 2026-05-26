// Featly dashboard — M5 skeleton.
//
// Vanilla JS, no build step. Path-based router that uses history.pushState
// and intercepts internal nav clicks. The middleware serves index.html for
// any /featly/* path so deep links work (refresh on /featly/flags renders
// the same shell, then the router decides which view to mount).

(function () {
    "use strict";

    var meta = document.querySelector('meta[name="featly-mount-path"]');
    var mountPath = (meta && meta.getAttribute("content")) || "/featly";
    if (mountPath.endsWith("/")) {
        mountPath = mountPath.slice(0, -1);
    }

    var viewEl = document.getElementById("view");
    var navLinks = Array.prototype.slice.call(document.querySelectorAll(".nav__link"));

    // View renderers. Each returns the HTML to drop into #view.
    // M5B replaces the placeholders with real list screens.
    var views = {
        "/": function () {
            return [
                '<h1>Overview</h1>',
                '<div class="card">',
                '  <p>Welcome to Featly. The dashboard skeleton is up; real screens land in <strong>M5B</strong> (listings) and <strong>M5C</strong> (detail + rule editor).</p>',
                '  <p class="muted">Until then, every section below shows a placeholder. The HTTP API behind them is already complete — see <code>/api/admin</code>.</p>',
                '</div>',
            ].join("\n");
        },
        "/flags": function () {
            return placeholderView("Flags", "List of flags in the selected environment. Click a row to open the detail + rule editor.");
        },
        "/configs": function () {
            return placeholderView("Configs", "Dynamic configuration values with the same targeting engine as flags.");
        },
        "/segments": function () {
            return placeholderView("Segments", "Reusable predicate sets. Reference them from flag and config rules with the <code>InSegment</code> operator.");
        },
        "/settings": function () {
            return placeholderView("Settings", "Environment defaults, approval policy, webhook configuration, audit retention. M6+ unlocks the editor.");
        },
    };

    function placeholderView(title, body) {
        return [
            '<h1>' + escapeHtml(title) + '</h1>',
            '<div class="placeholder">',
            '  <p>' + body + '</p>',
            '  <p class="muted">Coming in <strong>M5B</strong>.</p>',
            '</div>',
        ].join("\n");
    }

    function escapeHtml(s) {
        return String(s).replace(/[&<>"']/g, function (c) {
            return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
        });
    }

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
        viewEl.innerHTML = views[route]();
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
        // Honour modifier keys and middle-click — let the browser do its thing.
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

    render();
})();
