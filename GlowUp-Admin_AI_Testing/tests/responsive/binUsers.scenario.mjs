// 22 Nov 2026: the Recycle bin's tabs and search sit inside the list; a Users tab lists accounts removed on User Management
// (live), restores them to the status they had, and deletes them for good after asking. The Archive's tabs moved the same way.
// Run: node tests/responsive/binUsers.scenario.mjs
import { launch, openPage } from "./harness.mjs";
let checks = 0, failed = 0;
const ok = (cond, what) => { checks++; if (!cond) { failed++; console.log("FAILED: " + what); } };
const doc = (page, path) => page.evaluate((p) => { const d = window.__STORE.get(p); return d ? JSON.parse(JSON.stringify(d)) : null; }, path);
const seed = () => { const t = Date.now(); window.__SEED.users = {
    d1: { fullName: "Dee Park", username: "dee", email: "dee@example.com", createdAt: { __ts: t - 9e8 }, status: "deleted", inBin: true, statusBefore: "archived", statusChangedAt: { __ts: t - 3e5 } },
    d2: { fullName: "Gus Lane", email: "gus@example.com", createdAt: { __ts: t - 9e8 }, status: "deleted", inBin: true, statusBefore: "active", statusChangedAt: { __ts: t - 6e5 } },
    a1: { fullName: "Ana Reyes", email: "ana@example.com", createdAt: { __ts: t - 9e8 } } }; };
const browser = await launch();
try {
    for (const [width, theme] of [[1440, "light"], [390, "dark"]]) {
        const tag = `${width} ${theme}`;
        const { page, ctx, url, errors } = await openPage(browser, "bin", { width, height: 860, theme, go: false, touch: width < 500 });
        await ctx.addInitScript(seed);
        await page.goto(url); await page.waitForTimeout(1500);
        ok(await page.evaluate(() => !!document.querySelector("[data-panel=list] .list-toolbar #binTabs") && !!document.querySelector("[data-panel=list] .list-toolbar #binSearchInput") && !document.querySelector(".topbar #binTabs")), `${tag}: tabs and search are inside the list`);
        ok(await page.textContent("#countUsers") === "2", `${tag}: the Users tab counts the removed accounts`);
        await page.click('#binTabs [data-kind="users"]'); await page.waitForTimeout(200);
        const rows = await page.evaluate(() => [...document.querySelectorAll("#binTableBody tr[data-id]")].map((r) => r.dataset.id));
        ok(JSON.stringify(rows) === JSON.stringify(["d1", "d2"]) && /Removed users/.test(await page.textContent("#binTitle")), `${tag}: removed users listed, newest first ${JSON.stringify(rows)}`);
        ok(await page.evaluate(() => !document.querySelector('#binTableBody [data-act="archive"]')), `${tag}: no Archive button for users`);
        const tabs = await page.locator("#binTabs").boundingBox(), search = await page.locator("[data-panel=list] .search-container").boundingBox();
        ok(tabs && search && tabs.x >= 0 && search.x + search.width <= width + 0.5 && (search.y >= tabs.y + tabs.height - 1 || search.x >= tabs.x + tabs.width - 1), `${tag}: tabs and search do not overlap`);
        // live: a user removed elsewhere shows by itself
        await page.evaluate(() => { const t = Date.now(); window.__STORE.set("users/a1", { fullName: "Ana Reyes", email: "ana@example.com", createdAt: { toMillis: () => t - 9e8 }, status: "deleted", inBin: true, statusBefore: "banned", statusChangedAt: { toMillis: () => t } }); window.__NOTIFY(); });
        await page.waitForTimeout(250);
        ok(await page.textContent("#countUsers") === "3", `${tag}: a user removed on another page shows at once`);
        // restore: back to the status before
        await page.click('#binTableBody tr[data-id="d1"] [data-act="restore"]'); await page.waitForTimeout(300);
        const d1 = await doc(page, "users/d1");
        ok(d1.status === "archived" && d1.inBin === false, `${tag}: Restore brings the user back as they were ${JSON.stringify([d1.status, d1.inBin])}`);
        ok(await page.textContent("#countUsers") === "2", `${tag}: the list updates by itself after Restore`);
        // delete for good: asks first
        await page.click('#binTableBody tr[data-id="d2"] [data-act="delete"]'); await page.waitForTimeout(200);
        ok(/Delete permanently: 1 user\?/.test(await page.evaluate(() => [...document.querySelectorAll(".modal-content h3")].pop()?.textContent || "")), `${tag}: Delete permanently asks first`);
        await page.keyboard.press("Escape"); await page.waitForTimeout(150);
        ok(!!(await doc(page, "users/d2")), `${tag}: Escape deletes nothing`);
        const real = errors.filter((e) => !/ViewTransition/.test(e));
        ok(real.length === 0, `${tag}: no page errors ${real.join("; ")}`);
        await ctx.close();
    }
    // the Archive: same toolbar
    const { page, ctx } = await openPage(browser, "arc", { width: 1280, height: 860 });
    ok(await page.evaluate(() => !!document.querySelector("[data-panel=list] .list-toolbar #archiveTabs") && !!document.querySelector("[data-panel=list] .list-toolbar #archiveSearchInput")), "archive: tabs and search are inside the list");
    ok(await page.evaluate(() => !!document.querySelector('#archiveTabs [data-kind="supportReports"]')), "archive: a Reports tab for deleted app reports");
    await ctx.close();
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
