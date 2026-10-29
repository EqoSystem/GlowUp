// THE PAGE FRAME, DRAWN AT ONCE.
//
// This small script (no imports, nothing to wait for) is the first thing inside <main> of every admin page. It builds
// the real sidebar and the real top bar straight away, so the menu, the page's title and every fixed text of the
// page are on screen with the first picture. Nothing fixed waits for the sign-in check or for data any more: only the
// places where real data will appear show a loading shape (they are part of each page's own HTML).
//
//   - EVERY visit gets the frame at once (html.frame-first): the static shell (menu, title, panel headings, fixed
//     labels) never waits for the sign-in check or for data. Only the data containers (counts, lists, tables) show a
//     loading shape. The page cannot be used until the sign-in check has passed, and no data is asked for before that:
//     the check itself is unchanged, and somebody who is not an admin is sent to the login page as before (the frame
//     holds no data, only fixed text).
//   - When the check does not answer within 10 seconds, a message with a "Try again" button shows instead of an
//     endless loading screen.
//   - Resting the pointer on a menu link fetches that page ahead, so it opens without a wait.
//
// adminShell.js (mountShell) takes this frame over: it adds the tools of the top bar and the movement of the menu.
(function () {
    var script = document.currentScript;
    var main = script && script.parentElement && script.parentElement.tagName === "MAIN" ? script.parentElement : document.querySelector("main.main-content");
    var root = document.documentElement;
    var path = location.pathname;
    var page = path.indexOf("Inventory") >= 0 ? "inventory" : path.indexOf("Curator") >= 0 ? "curator" : path.indexOf("Recycle") >= 0 ? "bin" : "dashboard";

    // the same drawings as in adminShell.js (ICONS): the frame cannot wait for that file
    var ICON = {
        logo: '<path d="M12 3l2.2 6.8L21 12l-6.8 2.2L12 21l-2.2-6.8L3 12l6.8-2.2z"/>',
        dashboard: '<rect x="3" y="3" width="7" height="9" rx="1.5"/><rect x="14" y="3" width="7" height="5" rx="1.5"/><rect x="14" y="12" width="7" height="9" rx="1.5"/><rect x="3" y="16" width="7" height="5" rx="1.5"/>',
        box: '<path d="M21 8l-9-5-9 5 9 5 9-5z"/><path d="M3 8v8l9 5 9-5V8"/><path d="M12 13v8"/>',
        flask: '<path d="M9 3h6"/><path d="M10 3v6l-5 9a2 2 0 0 0 1.8 3h10.4a2 2 0 0 0 1.8-3l-5-9V3"/><path d="M7.5 15h9"/>',
        bin: '<path d="M4 7h16"/><path d="M9 7V4h6v3"/><path d="M6 7l1 13h10l1-13"/><path d="M10 11v6M14 11v6"/>',
        camera: '<path d="M4 8h3.2l1.8-2.5h6l1.8 2.5H20v11H4z"/><circle cx="12" cy="13.2" r="3.6"/>',
        chevron: '<path d="M15 6l-6 6 6 6"/>',
        down: '<path d="M6 9l6 6 6-6"/>'
    };
    var NAV = [
        { id: "dashboard", label: "Dashboard", icon: "dashboard", href: "../_Admin_Dashboard/adminDashboard.html" },
        { id: "inventory", label: "Brand & Product Inventory", icon: "box", href: "../_Admin_Brand_Inventory/adminBrandInventory.html" },
        { id: "curator", label: "Ingredient Curator", icon: "flask", href: "../_Admin_Ingredient_Curator/adminIngredientCurator.html" }
        // (Pending Snapshot Reviews is a window inside Brand & Product Inventory since 22 October 2026, not a page of its own)
        // (no menu entry for the Recycle Bin since 12 October 2026: the bin is opened INSIDE Brand & Product Inventory and
        //  inside Ingredient Curator, each with its own "Recycle bin" chip; both read the same database)
    ];
    // the menu can be folded to its icons; the choice is kept in this browser (and applied before the menu is drawn)
    var FOLD_KEY = "glowup.sidebar.folded";
    try { if (localStorage.getItem(FOLD_KEY) === "1") root.classList.add("sb-collapsed"); } catch (error) { /* a private window */ }
    var esc = function (value) { return String(value == null ? "" : value).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;"); };
    var svg = function (name) { return '<svg class="ic" viewBox="0 0 24 24" aria-hidden="true">' + (ICON[name] || ICON.logo) + "</svg>"; };

    /** Builds the sidebar and the top bar (once). */
    function build() {
        if (!main || document.querySelector("aside.sidebar")) return;
        document.body.classList.add("app");
        var sidebar = document.createElement("aside");
        sidebar.className = "sidebar";
        // the logo and the name are ONE button: it opens the settings menu (adminSettingsMenu.js). Folded, the logo alone opens it.
        // (no "Admin Portal" line, no e-mail and no Sign Out at the bottom since 29 October 2026: both are in that menu)
        sidebar.innerHTML =
            '<div class="sidebar-brand"><button type="button" class="brand-menu" id="brandMenuBtn" aria-haspopup="menu" aria-label="Settings" data-tip="Settings">' +
            '<span class="brand-logo"><img src="../_Shared/assets/glowup-icon-128.png" width="34" height="34" alt="" decoding="async"><span class="brand-badge" id="brandBinBadge" hidden></span></span>' +
            '<span class="brand-text"><span class="brand-title">GlowUp</span></span>' + svg("down").replace('class="ic"', 'class="ic brand-chevron"') + "</button>" +
            '<button type="button" class="sidebar-toggle" id="sidebarToggle" aria-controls="sidebarNav">' + svg("chevron") + '</button></div>' +
            '<ul class="nav-links" id="sidebarNav" aria-label="Pages"><li class="nav-drop" aria-hidden="true"></li>' +
            NAV.map(function (n) {
                var here = n.id === page;
                // the page you are on is marked on the link itself (and for screen readers), not by where the pointer is
                return '<li><a href="' + n.href + '"' + (here ? ' class="active" aria-current="page"' : "") + ' title="' + esc(n.label) + '">' + svg(n.icon) + '<span class="label">' + esc(n.label) + "</span></a></li>";
            }).join("") +
            "</ul>";
        document.body.insertBefore(sidebar, main);
        var toggle = sidebar.querySelector("#sidebarToggle");
        var paintToggle = function () {
            var folded = root.classList.contains("sb-collapsed");
            var text = folded ? "Expand the menu" : "Collapse the menu";
            toggle.setAttribute("aria-expanded", folded ? "false" : "true");
            toggle.setAttribute("aria-label", text);
            toggle.title = text;
        };
        paintToggle();
        toggle.addEventListener("click", function () {
            var folded = root.classList.toggle("sb-collapsed");
            try { localStorage.setItem(FOLD_KEY, folded ? "1" : "0"); } catch (error) { /* not kept */ }
            paintToggle();
        });

        var topbar = document.createElement("header");
        topbar.className = "topbar";
        topbar.innerHTML =
            '<div class="topbar-titles"><h2 class="header-title">' + esc(main.getAttribute("data-title") || "") + "</h2></div>" +
            '<div class="topbar-tools"></div>';
        main.insertBefore(topbar, main.firstChild);

        // a page the pointer rests on is fetched ahead (its HTML and its script), so opening it does not wait for them
        var fetched = {};
        var ahead = function (link) {
            var href = link.getAttribute("href");
            if (!href || fetched[href] || link.classList.contains("active")) return;
            fetched[href] = true;
            [href, href.replace(/\.html$/, ".js")].forEach(function (address) {
                var hint = document.createElement("link");
                hint.rel = "prefetch"; hint.href = address;
                document.head.appendChild(hint);
            });
        };
        Array.prototype.forEach.call(sidebar.querySelectorAll(".nav-links a"), function (link) {
            link.addEventListener("pointerenter", function () { ahead(link); });
            link.addEventListener("focus", function () { ahead(link); });
            link.addEventListener("touchstart", function () { ahead(link); }, { passive: true });
        });
    }

    /** GlowUp's own loading mark, for the sign-in check of somebody this browser does not know yet. */
    function brandLoader() {
        var holder = document.createElement("div");
        holder.className = "brand-loader";
        holder.setAttribute("role", "status");
        holder.innerHTML = '<div class="brand-loader-mark">' + svg("logo") + '</div><p class="brand-loader-name">GlowUp</p><p class="brand-loader-text">Admin Portal</p>';
        document.body.insertBefore(holder, document.body.firstChild);
    }

    /** The sign-in check did not answer: say so, and offer to try again (never an endless loading screen). */
    function stuck() {
        if (!root.classList.contains("auth-pending") || document.querySelector(".auth-stuck-box")) return;
        root.classList.add("auth-stuck");
        var box = document.createElement("div");
        box.className = "auth-stuck-box";
        box.setAttribute("role", "alert");
        box.innerHTML = "<b>The sign-in check is not answering.</b><span>Check the internet connection, then try again.</span>";
        var again = document.createElement("button");
        again.type = "button"; again.className = "btn primary"; again.textContent = "Try again";
        again.addEventListener("click", function () { location.reload(); });
        box.appendChild(again);
        document.body.appendChild(box);
    }

    // the static shell is drawn at once on every visit; only a page without a <main> (none today) falls back to the loading mark
    if (main) { root.classList.add("frame-first"); build(); }
    else brandLoader();
    setTimeout(stuck, 10000);

    window.GlowUpFrame = { page: page, NAV: NAV, build: build };
})();
