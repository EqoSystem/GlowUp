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
                const first = controls.querySelector(":scope > :not(.topbar-tools)") || controls.querySelector(".topbar-tools > *");
                const board = document.querySelector(".board");
                const card = board.querySelector(":scope > .panel:not([hidden])");
                const boardRect = board.getBoundingClientRect();
                const styles = getComputedStyle(board);
                return {
                    menu: rect(header.querySelector("#menuBtn")),
                    navIcon: rect(document.querySelector("#sidebarNav .nav-icon")),
                    brand: rect(header.querySelector(".brand-home")),
                    firstControl: rect(first),
                    card: rect(card),
                    gear: rect(gear),
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
            ok(Math.abs(m.menu.centerY - m.brand.centerY) <= 1, `${label}: hamburger and brand share a y center`);
            ok(Math.abs(m.menu.centerY - m.headerCenterY) <= 1, `${label}: header controls share the header centerline`);
            ok(m.h1Count === 1 && !m.visibleTitle, `${label}: one screen-reader heading and no visible page title`);
            ok(m.menuIdCount === 1 && m.navIdCount === 1, `${label}: header and nav IDs are unique`);
            ok(Math.abs(m.firstControl.centerY - m.menu.centerY) <= 1 && Math.abs(m.firstControl.height - 36) <= 1, `${label}: header control has a 36px aligned box`);
            if (key !== "dash") {
                ok(Math.abs(m.firstControl.left - Math.max(m.card.left, m.brand.right + 16)) <= 1, `${label}: first control follows content and clears the brand`);
            }
            ok(Math.abs(m.gear.right - m.contentRight) <= 1, `${label}: gear aligns to the content right edge`);
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
            controlsLeft: controls.left,
            controlsRight: controls.right,
            clientWidth: document.querySelector(".topbar-controls").clientWidth,
            scrollWidth: document.querySelector(".topbar-controls").scrollWidth
        };
    });
    ok(mobile.controlsLeft >= mobile.brandRight + 15, "mobile: controls do not overlap the brand");
    ok(mobile.controlsRight <= 375 && mobile.scrollWidth >= mobile.clientWidth, "mobile: controls remain within a horizontally scrollable header group");
    await ctx.close();
} finally {
    await browser.close();
}

console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
