import { launch, openPage, PAGES } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };
const browser = await launch();
try {
    for (const key of Object.keys(PAGES).filter((page) => page !== "login")) {
        const { page, ctx, errors } = await openPage(browser, key, { width: 1440, height: 900 });
        for (const state of ["collapsed", "hover", "pinned"]) {
            await page.evaluate((state) => {
                const root = document.documentElement;
                root.classList.remove("sb-mobile-open", "sb-hover", "sb-pinned", "sb-collapsed");
                if (state === "collapsed") root.classList.add("sb-collapsed");
                if (state === "hover") root.classList.add("sb-collapsed", "sb-hover");
                if (state === "pinned") root.classList.add("sb-pinned");
            }, state);
            await page.waitForTimeout(240);
            const m = await page.evaluate(() => {
                const rect = (element) => {
                    if (!element) return null;
                    const r = element.getBoundingClientRect();
                    return { left: r.left, right: r.right, width: r.width, height: r.height, centerX: r.left + r.width / 2, centerY: r.top + r.height / 2 };
                };
                const header = document.querySelector(".topbar");
                const controls = header.querySelector(".topbar-controls");
                const gear = header.querySelector(".quick-gear");
                const account = header.querySelector("#accountBtn") || gear;      // the profile button is the rightmost header control (20 Nov 2026)
                const first = controls.querySelector(":scope > :not(.topbar-tools)") || controls.querySelector(".topbar-tools > :not(.quick-gear)");
                const board = document.querySelector(".board");
                const card = board.querySelector(":scope > .panel:not([hidden])");
                const boardRect = board.getBoundingClientRect();
                const styles = getComputedStyle(board);
                const labels = Array.from(document.querySelectorAll("#sidebarNav .label"));
                const longestLabel = labels.find((label) => label.textContent.trim() === "Brand & Product Inventory");
                const footer = document.querySelector(".sidebar-footer");
                const navLinks = document.querySelector("#sidebarNav");
                const signOutLabel = footer.querySelector(".label");
                const footerRect = rect(footer);
                const navRect = rect(navLinks);
                const active = rect(navLinks.querySelector("a.active"));
                const navIcons = Array.from(document.querySelectorAll(".sidebar .nav-icon")).map(rect);
                const navRows = Array.from(navLinks.querySelectorAll("a")).map((link) => rect(link).height);
                const iconStyle = getComputedStyle(document.querySelector("#sidebarNav .nav-icon"));
                const menuStyle = getComputedStyle(header.querySelector("#menuBtn"));
                const longStyle = getComputedStyle(longestLabel);
                return {
                    menu: rect(header.querySelector("#menuBtn")),
                    navIcon: rect(document.querySelector("#sidebarNav .nav-icon")),
                    brand: rect(header.querySelector(".brand-home")),
                    logo: rect(header.querySelector(".brand-logo")),
                    brandTitle: rect(header.querySelector(".brand-title")),
                    navLabel: rect(labels.find((label) => label.textContent.trim() === "Dashboard")),
                    signOutLabel: rect(signOutLabel),
                    footer: footerRect,
                    nav: navRect,
                    active,
                    navIcons,
                    navRows,
                    navIconRadius: iconStyle.borderRadius,
                    menuRadius: menuStyle.borderRadius,
                    longestLabel: { clientWidth: longestLabel.clientWidth, scrollWidth: longestLabel.scrollWidth, whiteSpace: longStyle.whiteSpace },
                    signOut: rect(footer.querySelector(".sign-out")),
                    firstControl: rect(first), firstControlIsPagePart: !!controls.querySelector(":scope > :not(.topbar-tools)"),
                    card: rect(card),
                    gear: rect(gear),
                    account: rect(account),
                    contentRight: boardRect.right - parseFloat(styles.paddingRight),
                    h1Count: document.querySelectorAll("main.main-content > h1.sr-only").length,
                    menuIdCount: document.querySelectorAll("#menuBtn").length,
                    navIdCount: document.querySelectorAll("#sidebarNav").length,
                    visibleTitle: !!header.querySelector(".header-title, .topbar-titles"),
                    headerCenterY: rect(header).centerY
                };
            });
            const label = `${key} ${state}`;
            ok(Math.abs(m.menu.centerX - m.navIcon.centerX) <= 1, `${label}: hamburger and nav icon share an x center`);
            ok(m.navIcons.every((icon) => Math.abs(icon.centerX - m.menu.centerX) <= 1), `${label}: every nav icon shares the hamburger axis`);
            ok(Math.abs(m.logo.left - m.navLabel.left) <= 1, `${label}: nav labels align to the G logo`);
            ok(Math.abs(m.logo.left - m.signOutLabel.left) <= 1, `${label}: Sign Out label aligns to the G logo`);
            ok(Math.abs(m.nav.left - m.footer.left) <= 1 && Math.abs(m.nav.right - m.footer.right) <= 1, `${label}: nav and Sign Out divider share equal insets`);
            if (m.active) ok(Math.abs(m.active.left - m.nav.left - (m.nav.right - m.active.right)) <= 1, `${label}: active highlight has equal side insets`);
            ok(Math.abs(m.menu.centerY - m.brand.centerY) <= 1, `${label}: hamburger and brand share a y center`);
            ok(Math.abs(m.menu.centerY - m.logo.centerY) <= 1 && Math.abs(m.menu.centerY - m.brandTitle.centerY) <= 1, `${label}: logo and Glow Up text share the header centerline`);
            ok(Math.abs(m.menu.centerY - m.headerCenterY) <= 1, `${label}: header controls share the header centerline`);
            ok(m.h1Count === 1 && !m.visibleTitle, `${label}: one screen-reader heading and no visible page title`);
            ok(m.menuIdCount === 1 && m.navIdCount === 1, `${label}: header and nav IDs are unique`);
            ok(Math.abs(m.menu.width - m.navIcon.width) <= 1 && Math.abs(m.menu.height - m.navIcon.height) <= 1, `${label}: hamburger and nav icon boxes match`);
            ok(m.menuRadius === m.navIconRadius, `${label}: hamburger and nav icon radii match`);
            ok(m.navRows.every((height) => Math.abs(height - 38) <= 1), `${label}: nav rows share a 38px height`);
            ok(Math.abs(m.signOut.height - m.navIcon.height - 2) <= 1, `${label}: Sign Out row matches the nav row height`);
            if (m.firstControl) ok(Math.abs(m.firstControl.centerY - m.menu.centerY) <= 1 && Math.abs(m.firstControl.height - 36) <= 1, `${label}: header control has a 36px aligned box`);
            if (key !== "dash" && m.firstControlIsPagePart) {   // 22 Nov 2026: Bin, Archive and Users keep their tabs and search inside the list
                ok(Math.abs(m.firstControl.left - Math.max(m.card.left, m.brand.right + 16)) <= 1, `${label}: first control follows content and clears the brand`);
            }
            if (state !== "collapsed") {
                ok(m.longestLabel.whiteSpace === "nowrap" && m.longestLabel.scrollWidth <= m.longestLabel.clientWidth, `${label}: longest nav label fits without wrapping or truncation`);
            }
            ok(Math.abs(m.account.right - m.contentRight) <= 1, `${label}: the rightmost header control (profile) aligns to the content right edge`);
            ok(Math.abs(m.gear.centerY - m.menu.centerY) <= 1, `${label}: gear shares the header centerline`);
        }
        ok(errors.length === 0, `${key}: no page errors ${errors.join("; ")}`);
        await ctx.close();
    }

    const { page, ctx } = await openPage(browser, "inv", { width: 375, height: 812 });
    const mobile = await page.evaluate(() => {
        const r = (e) => e.getBoundingClientRect();
        const brand = r(document.querySelector(".brand-home"));
        const controls = r(document.querySelector(".topbar-controls"));
        return {
            brandRight: brand.right,
            brandBottom: brand.bottom,
            controlsLeft: controls.left,
            controlsTop: controls.top,
            controlsRight: controls.right,
            clientWidth: document.querySelector(".topbar-controls").clientWidth,
            scrollWidth: document.querySelector(".topbar-controls").scrollWidth
        };
    });
    // 17 Nov 2026: on a phone the search and the buttons have their own row under the brand (no sideways scrolling any more)
    ok(mobile.controlsLeft >= mobile.brandRight + 15 || mobile.controlsTop >= mobile.brandBottom - 1, "mobile: controls do not overlap the brand");
    ok(mobile.controlsRight <= 375 && mobile.scrollWidth <= mobile.clientWidth + 1, "mobile: every header control fits in view, nothing scrolls sideways");
    await page.click("#menuBtn");
    await page.waitForTimeout(220);
    const mobileOpen = await page.evaluate(() => ({
        open: document.documentElement.classList.contains("sb-mobile-open"),
        sidebarVisible: getComputedStyle(document.querySelector(".sidebar")).visibility === "visible",
        labelVisible: getComputedStyle(document.querySelector("#sidebarNav .label")).visibility === "visible"
    }));
    ok(mobileOpen.open && mobileOpen.sidebarVisible && mobileOpen.labelVisible, "mobile: opening the menu reveals the labeled off-canvas navigation");
    await page.mouse.click(350, 400);                // on the dimmed page beside the menu (y 100 is the header's second row now)
    await page.waitForTimeout(20);
    ok(!await page.evaluate(() => document.documentElement.classList.contains("sb-mobile-open")), "mobile: tapping the backdrop closes the off-canvas navigation");
    await page.click("#menuBtn");
    await page.keyboard.press("Escape");
    await page.waitForTimeout(20);
    ok(!await page.evaluate(() => document.documentElement.classList.contains("sb-mobile-open")), "mobile: Escape closes the off-canvas navigation");
    await ctx.close();
} finally {
    await browser.close();
}

console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
