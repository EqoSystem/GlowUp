
















(function () {
    var script = document.currentScript;
    var main = script && script.parentElement && script.parentElement.tagName === "MAIN" ? script.parentElement : document.querySelector("main.main-content");
    var root = document.documentElement;
    var path = location.pathname;
    var page = path.indexOf("Inventory") >= 0 ? "inventory" : path.indexOf("Curator") >= 0 ? "curator" : path.indexOf("Recycle") >= 0 ? "bin" : path.indexOf("_Admin_Archive") >= 0 ? "archive" : path.indexOf("_Admin_User_Management") >= 0 ? "users" : path.indexOf("_Admin_Admin_Management") >= 0 ? "admins" : "dashboard";


    var ICON = {
        logo: '<path d="M12 3l2.2 6.8L21 12l-6.8 2.2L12 21l-2.2-6.8L3 12l6.8-2.2z"/>',
        dashboard: '<rect x="3" y="3" width="7" height="9" rx="1.5"/><rect x="14" y="3" width="7" height="5" rx="1.5"/><rect x="14" y="12" width="7" height="9" rx="1.5"/><rect x="3" y="16" width="7" height="5" rx="1.5"/>',
        box: '<path d="M21 8l-9-5-9 5 9 5 9-5z"/><path d="M3 8v8l9 5 9-5V8"/><path d="M12 13v8"/>',
        flask: '<path d="M9 3h6"/><path d="M10 3v6l-5 9a2 2 0 0 0 1.8 3h10.4a2 2 0 0 0 1.8-3l-5-9V3"/><path d="M7.5 15h9"/>',
        bin: '<path d="M4 7h16"/><path d="M9 7V4h6v3"/><path d="M6 7l1 13h10l1-13"/><path d="M10 11v6M14 11v6"/>',
        camera: '<path d="M4 8h3.2l1.8-2.5h6l1.8 2.5H20v11H4z"/><circle cx="12" cy="13.2" r="3.6"/>',
        chevron: '<path d="M15 6l-6 6 6 6"/>',
        shield: '<path d="M12 3l8 3v6c0 4.5-3.2 7.8-8 9-4.8-1.2-8-4.5-8-9V6l8-3z"/><path d="M9 12l2.2 2.2L15 10.5"/>',
        users: '<circle cx="9" cy="8" r="3.6"/><path d="M2.5 20c1.2-3.6 3.8-5.5 6.5-5.5s5.3 1.9 6.5 5.5"/><path d="M16 4.6a3.6 3.6 0 0 1 0 6.8M18.5 14.8c1.7.8 2.9 2.6 3.5 5.2"/>',
        menu: '<path d="M4 7h16M4 12h16M4 17h16"/>',
        logout: '<path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><path d="M16 17l5-5-5-5"/><path d="M21 12H9"/>'
    };
    var NAV = [
        { id: "dashboard", label: "Dashboard", icon: "dashboard", href: "../_Admin_Dashboard/adminDashboard.html" },
        { id: "users", label: "User Management", icon: "users", href: "../_Admin_User_Management/adminUserManagement.html" },
        { id: "admins", label: "Admin Management", icon: "shield", href: "../_Admin_Admin_Management/adminAdminManagement.html" },
        { id: "inventory", label: "Brand & Product Inventory", icon: "box", href: "../_Admin_Brand_Inventory/adminBrandInventory.html" },
        { id: "curator", label: "Ingredient Curator", icon: "flask", href: "../_Admin_Ingredient_Curator/adminIngredientCurator.html" }



    ];

    var FOLD_KEY = "glowup.sidebar.folded";
    var pinnedOpen = false;
    try { pinnedOpen = localStorage.getItem(FOLD_KEY) === "0"; } catch (error) {                        }
    root.classList.toggle("sb-pinned", pinnedOpen);
    root.classList.toggle("sb-collapsed", !pinnedOpen);
    requestAnimationFrame(function () { requestAnimationFrame(function () { root.classList.add("sb-ready"); }); });
    var esc = function (value) { return String(value == null ? "" : value).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;"); };
    var svg = function (name) { return '<svg class="ic" viewBox="0 0 24 24" aria-hidden="true">' + (ICON[name] || ICON.logo) + "</svg>"; };


    function build() {
        if (!main || document.querySelector("aside.sidebar")) return;
        document.body.classList.add("app");
        var sidebar = document.createElement("aside");
        sidebar.className = "sidebar";
        sidebar.innerHTML =
            '<ul class="nav-links" id="sidebarNav" aria-label="Pages"><li class="nav-drop" aria-hidden="true"></li>' +
            NAV.map(function (n) {
                var here = n.id === page;

                return '<li><a href="' + n.href + '"' + (here ? ' class="active" aria-current="page"' : "") + ' aria-label="' + esc(n.label) + '"><span class="nav-icon">' + svg(n.icon) + '</span><span class="label">' + esc(n.label) + "</span></a></li>";
            }).join("") +
            "</ul>" +
            '<div class="sidebar-footer"><button type="button" class="sign-out" aria-label="Sign Out"><span class="nav-icon">' + svg("logout") + '</span><span class="label">Sign Out</span></button></div>';
        document.body.insertBefore(sidebar, main);

        var links = sidebar.querySelectorAll(".nav-links a");
        var heading = document.createElement("h1");
        heading.className = "sr-only";
        heading.textContent = main.getAttribute("data-title") || "";
        main.insertBefore(heading, main.firstChild);
        var topbar = document.createElement("header");
        topbar.className = "topbar";
        topbar.innerHTML =
            '<div class="topbar-leading"><button type="button" class="icon-btn menu-btn" id="menuBtn" aria-controls="sidebarNav" aria-expanded="' + (pinnedOpen ? "true" : "false") + '" aria-label="' + (pinnedOpen ? "Close menu" : "Open menu") + '">' + svg("menu") + "</button></div>" +
            '<a class="brand-home" href="' + NAV[0].href + '" aria-label="GlowUp – go to dashboard"><span class="brand-mark"><span class="brand-logo"><img src="../_Shared/assets/glowup-icon-128.png" width="34" height="34" alt="" decoding="async"></span></span><span class="brand-title">Glow Up</span></a>' +
            '<div class="topbar-controls"><div class="topbar-tools"></div></div>';
        main.insertBefore(topbar, main.firstChild);
        var menuBtn = topbar.querySelector("#menuBtn"), brand = topbar.querySelector(".brand-home"), hoverTimer = 0;
        var mobileMenu = window.matchMedia("(max-width: 768px), (hover: none)");
        var expanded = function () {
            return mobileMenu.matches ? root.classList.contains("sb-mobile-open") : root.classList.contains("sb-pinned") || root.classList.contains("sb-hover");
        };
        var paintMenuBtn = function () {
            var open = expanded();
            menuBtn.setAttribute("aria-expanded", open ? "true" : "false");
            menuBtn.setAttribute("aria-label", open ? "Close menu" : "Open menu");
        };
        paintMenuBtn();
        var closeMobileMenu = function (focusBack) {
            if (!root.classList.contains("sb-mobile-open")) return;
            root.classList.remove("sb-mobile-open");
            paintMenuBtn();
            if (focusBack) menuBtn.focus({ preventScroll: true });
        };
        var updateHeaderLayout = function () {
            root.style.setProperty("--header-h", Math.ceil(topbar.getBoundingClientRect().height) + "px");
            root.style.setProperty("--brand-right", brand.getBoundingClientRect().right + "px");
        };
        updateHeaderLayout();
        if (window.ResizeObserver) {
            var headerObserver = new ResizeObserver(updateHeaderLayout);
            headerObserver.observe(topbar);
            headerObserver.observe(brand);
        } else window.addEventListener("resize", updateHeaderLayout);
        menuBtn.addEventListener("click", function () {
            if (mobileMenu.matches) {
                root.classList.toggle("sb-mobile-open");
                paintMenuBtn();
                if (root.classList.contains("sb-mobile-open")) {
                    var here = sidebar.querySelector(".nav-links a.active") || links[0];
                    if (here) here.focus({ preventScroll: true });
                }
                return;
            }
            pinnedOpen = !root.classList.contains("sb-pinned");
            root.classList.toggle("sb-pinned", pinnedOpen);
            root.classList.toggle("sb-collapsed", !pinnedOpen);
            root.classList.remove("sb-hover");
            try { localStorage.setItem(FOLD_KEY, pinnedOpen ? "0" : "1"); } catch (error) {                }
            paintMenuBtn();
        });
        var hoverOpen = function (event) {
            if (mobileMenu.matches || !window.matchMedia("(hover: hover)").matches || event && event.pointerType === "touch" || root.classList.contains("sb-pinned")) return;
            clearTimeout(hoverTimer);
            root.classList.add("sb-hover");
            paintMenuBtn();
        };
        var hoverClose = function (delay) {
            clearTimeout(hoverTimer);
            hoverTimer = setTimeout(function () {
                if (sidebar.matches(":hover") || sidebar.matches(":focus-within") || root.classList.contains("sb-pinned")) return;
                root.classList.remove("sb-hover");
                paintMenuBtn();
            }, delay);
        };
        sidebar.addEventListener("pointerenter", hoverOpen);
        sidebar.addEventListener("pointerleave", function (event) { if (event.pointerType !== "touch") hoverClose(190); });
        sidebar.addEventListener("focusin", hoverOpen);
        sidebar.addEventListener("focusout", function () { hoverClose(0); });
        links.forEach(function (link) {
            link.addEventListener("click", function () { if (mobileMenu.matches) closeMobileMenu(false); });
        });
        document.addEventListener("keydown", function (event) {
            if (event.key !== "Escape") return;
            if (root.classList.contains("sb-mobile-open")) { closeMobileMenu(true); return; }
            if (!root.classList.contains("sb-pinned") && root.classList.contains("sb-hover")) {
                root.classList.remove("sb-hover");
                paintMenuBtn();
                menuBtn.focus({ preventScroll: true });
            }
        });
        document.addEventListener("pointerdown", function (event) {
            if (root.classList.contains("sb-mobile-open") && !sidebar.contains(event.target) && !menuBtn.contains(event.target)) closeMobileMenu(false);
        });
        mobileMenu.addEventListener("change", function () {
            root.classList.remove("sb-mobile-open", "sb-hover");
            root.classList.toggle("sb-pinned", !mobileMenu.matches && pinnedOpen);
            root.classList.toggle("sb-collapsed", mobileMenu.matches || !pinnedOpen);
            paintMenuBtn();
        });


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


    function brandLoader() {
        var holder = document.createElement("div");
        holder.className = "brand-loader";
        holder.setAttribute("role", "status");
        holder.innerHTML = '<div class="brand-loader-mark">' + svg("logo") + '</div><p class="brand-loader-name">GlowUp</p><p class="brand-loader-text">Admin Portal</p>';
        document.body.insertBefore(holder, document.body.firstChild);
    }


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


    if (main) { root.classList.add("frame-first"); build(); }
    else brandLoader();

    setTimeout(function () { root.classList.add("board-ready"); }, 8000);
    setTimeout(stuck, 10000);

    window.GlowUpFrame = { page: page, NAV: NAV, build: build };
})();
