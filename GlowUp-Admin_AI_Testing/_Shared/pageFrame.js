// THE PAGE FRAME, DRAWN AT ONCE.
//
// This small script (no imports, nothing to wait for) is the first thing inside <main> of every admin page. It builds
// the real sidebar and the real top bar straight away, so the menu, the page's title and every fixed text of the
// page are on screen with the first picture. Nothing fixed waits for the sign-in check or for data any more: only the
// places where real data will appear show a loading shape (they are part of each page's own HTML).
//
//   - An admin who was signed in on this browser before gets the frame at once (html.frame-first). The page cannot be
//     used until the sign-in check has passed, and no data is asked for before that: the check itself is unchanged.
//   - Somebody this browser does not know sees GlowUp's own loading mark until the check has answered, and is then
//     sent to the login page as before.
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
        logout: '<path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><path d="M16 17l5-5-5-5"/><path d="M21 12H9"/>'
    };
    var NAV = [
        { id: "dashboard", label: "Dashboard", icon: "dashboard", href: "../_Admin_Dashboard/adminDashboard.html" },
        { id: "inventory", label: "Brand & Product Inventory", icon: "box", href: "../_Admin_Brand_Inventory/adminBrandInventory.html" },
        { id: "curator", label: "Ingredient Curator", icon: "flask", href: "../_Admin_Ingredient_Curator/adminIngredientCurator.html" }
    ];
    var esc = function (value) { return String(value == null ? "" : value).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;"); };
    var svg = function (name) { return '<svg class="ic" viewBox="0 0 24 24" aria-hidden="true">' + (ICON[name] || ICON.logo) + "</svg>"; };

    /** Builds the sidebar and the top bar (once). */
    function build() {
        if (!main || document.querySelector("aside.sidebar")) return;
        document.body.classList.add("app");
        var sidebar = document.createElement("aside");
        sidebar.className = "sidebar";
        sidebar.innerHTML =
            '<div class="sidebar-brand"><div class="brand-logo">' + svg("logo") + '</div>' +
            '<div class="brand-text"><h1 class="brand-title">GlowUp</h1><p class="brand-subtitle">Admin Portal</p></div></div>' +
            '<ul class="nav-links" aria-label="Pages"><li class="nav-drop" aria-hidden="true"></li>' +
            NAV.map(function (n) {
                var here = n.id === page;
                // the page you are on is marked on the link itself (and for screen readers), not by where the pointer is
                return '<li><a href="' + n.href + '"' + (here ? ' class="active" aria-current="page"' : "") + ' title="' + esc(n.label) + '">' + svg(n.icon) + '<span class="label">' + esc(n.label) + "</span></a></li>";
            }).join("") +
            "</ul>" +
            '<div class="sidebar-footer"><p class="signed-in-as" aria-live="polite">&nbsp;</p>' +
            '<a href="../_Index/index.html" class="sign-out" title="Sign out">' + svg("logout") + '<span class="label">Sign Out</span></a></div>';
        document.body.insertBefore(sidebar, main);

        var topbar = document.createElement("header");
        topbar.className = "topbar";
        topbar.innerHTML =
            '<div class="topbar-titles"><h2 class="header-title">' + esc(main.getAttribute("data-title") || "") + "</h2>" +
            '<p class="header-subtitle">' + esc(main.getAttribute("data-subtitle") || "") + "</p></div>" +
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

    var known = false;
    try { known = localStorage.getItem("glowup.admin.known") === "1"; } catch (error) { /* a private window */ }
    if (known && main) { root.classList.add("frame-first"); build(); }
    else brandLoader();
    setTimeout(stuck, 10000);

    window.GlowUpFrame = { page: page, NAV: NAV, build: build };
})();
