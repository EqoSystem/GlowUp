// Browser check (12 November 2026): the menu is the icon rail by default, opens OVER the page on hover and closes when the pointer leaves;
// the menu button left of the logo pins it open / folds it back.   Run: node tests/responsive/sidebarGmail.scenario.mjs
import { launch, openPage } from "./harness.mjs";
let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };
const browser = await launch();
try {
    for (const [width, height] of [[1440, 900], [1100, 800], [900, 800]]) {
        const { page, errors, settle } = await openPage(browser, "dash", { width, height });
        await settle(800);
        const w = () => page.evaluate(() => Math.round(document.querySelector("aside.sidebar").getBoundingClientRect().width));
        const mainLeft = () => page.evaluate(() => Math.round(document.querySelector(".main-content").getBoundingClientRect().left));
        const visible = (sel) => page.evaluate((s) => { const e = document.querySelector(s); return !!e && getComputedStyle(e).display !== "none" && e.getBoundingClientRect().width > 0; }, sel);
        const rail = await w(), left0 = await mainLeft();
        ok(rail <= 80, `${width}px: the menu starts as the icon rail (${rail}px)`);
        ok(await visible("#sbMenuBtn"), `${width}px: the menu button shows`);
        ok(!(await visible(".sidebar-brand .brand-home")), `${width}px: the rail shows only the menu button, not the logo`);
        const logoBox = await page.evaluate(() => { const b = document.querySelector("#sbMenuBtn").getBoundingClientRect(); return b.left; });
        await page.mouse.move(30, 300);
        await page.waitForTimeout(300);
        ok(await w() >= 220, `${width}px: hovering the rail opens the menu (${await w()}px)`);
        ok(await mainLeft() === left0, `${width}px: the page does not move while the menu is open over it`);
        ok(await visible(".sidebar-brand .brand-home"), `${width}px: the logo shows beside the menu button when open`);
        const order = await page.evaluate(() => { const m = document.querySelector("#sbMenuBtn").getBoundingClientRect().left, l = document.querySelector(".brand-home").getBoundingClientRect().left; return m < l; });
        ok(order, `${width}px: the menu button is LEFT of the logo`);
        await page.mouse.move(width - 40, 400);
        await page.waitForTimeout(450);
        ok(await w() <= 80, `${width}px: leaving the menu closes it again (${await w()}px)`);
        // the menu button pins it open, and again folds it
        await page.click("#sbMenuBtn"); await page.waitForTimeout(450);
        await page.mouse.move(width - 40, 400); await page.waitForTimeout(450);
        ok(await w() >= 220, `${width}px: the menu button keeps the menu open after the pointer leaves (${await w()}px)`);
        await page.click("#sbMenuBtn"); await page.mouse.move(width - 40, 400); await page.waitForTimeout(450);
        ok(await w() <= 80, `${width}px: the menu button folds it back (${await w()}px)`);
        ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), `${width}px: no sideways scroll`);
        if (process.env.SHOT_DIR && width === 1440) {
            await page.mouse.move(30, 300); await page.waitForTimeout(300);
            await page.screenshot({ path: `${process.env.SHOT_DIR}/menu_hover_1440.png` });
        }
        ok(errors.length === 0, `${width}px: no page errors ${errors.join("; ")}`);
        await page.context().close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
