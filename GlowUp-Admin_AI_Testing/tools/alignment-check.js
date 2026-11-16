(() => {
    const root = document.documentElement;
    const sidebar = document.querySelector(".sidebar");
    const header = document.querySelector(".topbar");
    const controls = header && header.querySelector(".topbar-controls");
    const gear = header && header.querySelector(".quick-gear");
    const nav = document.querySelector("#sidebarNav");
    const menu = header && header.querySelector("#menuBtn");
    const logo = header && header.querySelector(".brand-logo");
    const title = header && header.querySelector(".brand-title");
    const firstNavIcon = nav && nav.querySelector(".nav-icon");
    const firstLabel = nav && Array.from(nav.querySelectorAll(".label")).find((item) => item.textContent.trim() === "Dashboard");
    const signOutLabel = document.querySelector(".sidebar-footer .label");
    const signOutRow = document.querySelector(".sidebar-footer .sign-out");
    const board = document.querySelector(".board");
    const card = board && board.querySelector(":scope > .panel:not([hidden])");
    const tools = controls && controls.querySelector(".topbar-tools");
    const firstControl = controls && (controls.querySelector(":scope > :not(.topbar-tools)") || tools && tools.querySelector(":scope > :not(.quick-gear)"));
    const state = root.classList.contains("sb-mobile-open") ? "mobile-open" :
        root.classList.contains("sb-pinned") ? "pinned" :
            root.classList.contains("sb-hover") ? "hover-expanded" : "collapsed";
    const box = (element) => {
        if (!element) return null;
        const rect = element.getBoundingClientRect();
        return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom, width: rect.width, height: rect.height, centerX: rect.left + rect.width / 2, centerY: rect.top + rect.height / 2 };
    };
    const menuBox = box(menu);
    const iconBox = box(firstNavIcon);
    const logoBox = box(logo);
    const labelBox = box(firstLabel);
    const signOutLabelBox = box(signOutLabel);
    const cardBox = box(card);
    const boardStyle = board && getComputedStyle(board);
    const contentRight = board && board.getBoundingClientRect().right - parseFloat(boardStyle.paddingRight);
    const longestLabel = nav && Array.from(nav.querySelectorAll(".label")).find((item) => item.textContent.trim() === "Brand & Product Inventory");
    const textWidth = () => {
        const range = document.createRange();
        range.selectNodeContents(longestLabel);
        return range.getBoundingClientRect().width;
    };
    let longestBox = box(longestLabel);
    let longestStyle = longestLabel && getComputedStyle(longestLabel);
    let longestTextWidth = longestLabel ? textWidth() : 0;
    let longestClientWidth = longestLabel ? longestLabel.clientWidth : 0;
    let longestScrollWidth = longestLabel ? longestLabel.scrollWidth : 0;
    const mobileClosed = matchMedia("(max-width: 768px), (hover: none)").matches && !root.classList.contains("sb-mobile-open");

    if (longestLabel && state === "collapsed" && !mobileClosed && sidebar) {
        const oldTransition = sidebar.style.transition;
        sidebar.style.transition = "none";
        root.classList.add("sb-hover");
        void sidebar.offsetWidth;
        longestBox = box(longestLabel);
        longestStyle = getComputedStyle(longestLabel);
        longestTextWidth = textWidth();
        longestClientWidth = longestLabel.clientWidth;
        longestScrollWidth = longestLabel.scrollWidth;
        root.classList.remove("sb-hover");
        void sidebar.offsetWidth;
        sidebar.style.transition = oldTransition;
    }

    const checks = [];
    const addCheck = (name, actual, expected, applicable = true) => {
        const delta = applicable && Number.isFinite(actual) && Number.isFinite(expected) ? Math.abs(actual - expected) : null;
        checks.push({ check: name, actual: Number.isFinite(actual) ? Number(actual.toFixed(1)) : "N/A", expected: Number.isFinite(expected) ? Number(expected.toFixed(1)) : "N/A", delta: delta === null ? "N/A" : Number(delta.toFixed(1)), result: !applicable ? "N/A" : delta !== null && delta <= 1 ? "PASS" : "FAIL" });
    };
    addCheck("Hamburger vs nav icon center", menuBox && menuBox.centerX, iconBox && iconBox.centerX);
    addCheck("Nav label vs G logo left", labelBox && labelBox.left, logoBox && logoBox.left, !mobileClosed);
    const expectedControlLeft = cardBox && logoBox && Math.max(cardBox.left, box(header.querySelector(".brand-home")).right + 16);
    addCheck("First header control vs card left", firstControl && box(firstControl).left, expectedControlLeft, !!firstControl);
    addCheck("Gear vs content right edge", box(gear) && box(gear).right, contentRight);
    const labelFits = !!longestLabel && !mobileClosed && longestStyle.whiteSpace === "nowrap" && longestTextWidth <= longestBox.width && longestScrollWidth <= longestClientWidth;
    checks.push({ check: "Brand & Product Inventory fits without wrap", actual: longestLabel && longestBox ? `${longestTextWidth.toFixed(1)}/${longestBox.width.toFixed(1)}px` : "N/A", expected: "nowrap, no overflow", delta: "N/A", result: !longestLabel || mobileClosed ? "N/A" : labelFits ? "PASS" : "FAIL" });

    const measurements = {
        state,
        hamburgerCenterX: menuBox && Number(menuBox.centerX.toFixed(1)),
        navIconCenterX: iconBox && Number(iconBox.centerX.toFixed(1)),
        logoLeft: logoBox && Number(logoBox.left.toFixed(1)),
        logoRight: logoBox && Number(logoBox.right.toFixed(1)),
        glowUpTextLeft: box(title) && Number(box(title).left.toFixed(1)),
        navLabelLeft: labelBox && Number(labelBox.left.toFixed(1)),
        signOutLabelLeft: signOutLabelBox && Number(signOutLabelBox.left.toFixed(1)),
        dividerLeft: box(document.querySelector(".sidebar-footer")) && Number(box(document.querySelector(".sidebar-footer")).left.toFixed(1)),
        signOutLeft: box(signOutRow) && Number(box(signOutRow).left.toFixed(1)),
        firstHeaderControlLeft: firstControl && Number(box(firstControl).left.toFixed(1)),
        firstCardLeft: cardBox && Number(cardBox.left.toFixed(1)),
        firstCardRight: cardBox && Number(cardBox.right.toFixed(1)),
        gearRight: box(gear) && Number(box(gear).right.toFixed(1)),
        contentRight: Number.isFinite(contentRight) ? Number(contentRight.toFixed(1)) : "N/A"
    };

    const tokenPattern = /--(?:rail-w|nav-w|icon-box|icon-center-x|label-start-x|gutter|header-h)\s*:/i;
    const overrides = [];
    document.querySelectorAll(".topbar[style], .topbar [style], .sidebar[style], .sidebar [style]").forEach((element) => {
        const style = element.getAttribute("style");
        if (!style || !style.trim()) return;
        const selector = `${element.tagName.toLowerCase()}${element.id ? `#${element.id}` : ""}${element.className && typeof element.className === "string" ? `.${element.className.trim().replace(/\s+/g, ".")}` : ""}`;
        overrides.push(`${tokenPattern.test(style) ? "inline layout-token override" : "inline style (non-token)"}: ${selector} — ${style}`);
    });
    const scanRules = (rules, source) => {
        if (!rules) return;
        Array.from(rules).forEach((rule) => {
            if (rule.cssRules) scanRules(rule.cssRules, source);
            if (rule.style && Array.from(rule.style).some((property) => tokenPattern.test(property + ":"))) {
                overrides.push(`${source} token override: ${rule.cssText}`);
            }
        });
    };
    Array.from(document.styleSheets).forEach((sheet) => {
        const source = sheet.href || "<inline style>";
        let isShared = false;
        try {
            const url = new URL(source, location.href);
            isShared = url.pathname.includes("/_Shared/adminCommon.css");
        } catch (_) {
            isShared = false;
        }
        if (isShared) return;
        try {
            scanRules(sheet.cssRules, source);
        } catch (error) {
            overrides.push(`${source}: unable to inspect stylesheet rules (${error.name})`);
        }
    });

    console.group(`GlowUp alignment check — ${state}`);
    console.table(checks);
    console.table([measurements]);
    console.log(overrides.length ? "Header/nav inline styles or per-page token overrides:" : "No inline header/nav styles or per-page layout-token overrides found.");
    if (overrides.length) console.table(overrides.map((override) => ({ override })));
    console.groupEnd();
})();
