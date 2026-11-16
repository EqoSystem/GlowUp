
import { launch, openPage } from "./harness.mjs";
let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };
const browser = await launch();
try {
    for (const [width, height] of [[1440, 900], [1024, 800], [768, 900]]) {
        const { page, errors, settle } = await openPage(browser, "dash", { width, height });
        await settle(300);
        const mobile = width <= 768;
        const sidebarWidth = () => page.evaluate(() => Math.round(document.querySelector("aside.sidebar").getBoundingClientRect().width));
        const sidebarLeft = () => page.evaluate(() => Math.round(document.querySelector("aside.sidebar").getBoundingClientRect().left));
        const mainLeft = () => page.evaluate(() => Math.round(document.querySelector(".main-content").getBoundingClientRect().left));
        const header = () => page.evaluate(() => {
            const r = document.querySelector(".topbar").getBoundingClientRect();
            return { left: Math.round(r.left), right: Math.round(r.right), top: Math.round(r.top), bottom: Math.round(r.bottom), height: Math.round(r.height) };
        });
        const bar = await header();
        ok(bar.left === 0 && bar.right === width && bar.top === 0 && bar.height >= 64, `${width}px: full-width sticky header ${JSON.stringify(bar)}`);
        ok(await page.locator(".topbar #menuBtn").count() === 1 && await page.locator(".topbar .brand-home").count() === 1, `${width}px: menu and brand are in the header`);
        ok(await page.locator(".sidebar .brand-home, .sidebar-brand, #sidebarToggle, #sbMenuBtn").count() === 0, `${width}px: no old sidebar header or edge toggle remains`);
        ok(await page.locator("#menuBtn").getAttribute("aria-controls") === "sidebarNav", `${width}px: menu control points at the nav`);

        if (mobile) {
            ok(await sidebarLeft() < 0 && await mainLeft() === 0, `${width}px: nav starts off-canvas and content uses full width`);
            await page.click("#menuBtn");
            await settle(250);
            ok(await sidebarLeft() === 0 && await page.locator("#menuBtn").getAttribute("aria-expanded") === "true", `${width}px: hamburger opens off-canvas nav`);
            ok(await page.evaluate(() => getComputedStyle(document.body, "::after").content !== "none"), `${width}px: backdrop is present`);
            await page.keyboard.press("Escape");
            await settle(250);
            ok(await sidebarLeft() < 0 && await page.locator("#menuBtn").getAttribute("aria-expanded") === "false", `${width}px: Escape closes nav`);
        } else {
            const rail = await sidebarWidth(), left = await mainLeft();
            ok(rail === 72 && left === 72, `${width}px: collapsed 72px rail and matching content offset`);
            await page.mouse.move(30, bar.bottom + 50);
            await settle(240);
            ok(await sidebarWidth() >= 250 && await mainLeft() === left, `${width}px: hover expands over content without shifting it`);
            await page.keyboard.press("Escape");
            await settle(100);
            ok(await sidebarWidth() === 72, `${width}px: Escape closes the hover overlay`);
            await page.click("#menuBtn");
            await settle(250);
            { const w = await sidebarWidth(), m = await mainLeft(); ok(w === 284 && m === 284, `${width}px: hamburger pins the nav and offsets content (--nav-w 284px; ${w}, ${m})`); }
            ok(await page.locator("#menuBtn").getAttribute("aria-expanded") === "true", `${width}px: pinned state is reflected accessibly`);
            ok(await page.evaluate(() => localStorage.getItem("glowup.sidebar.folded")) === "0", `${width}px: pin state persists`);
            await page.mouse.move(width - 30, height - 20);
            await settle(240);
            ok(await sidebarWidth() === 284, `${width}px: pinned nav remains open after pointer leaves`);
            await page.click("#menuBtn");
            await settle(250);
            ok(await sidebarWidth() === 72 && await mainLeft() === 72, `${width}px: second click unpins and returns to the rail`);
        }
        const faults = await page.evaluate(() => window.__audit());
        ok(Object.values(faults).every((list) => list.length === 0), `${width}px: no layout audit faults`);
        ok(errors.length === 0, `${width}px: no page errors ${errors.join("; ")}`);
        await page.context().close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
