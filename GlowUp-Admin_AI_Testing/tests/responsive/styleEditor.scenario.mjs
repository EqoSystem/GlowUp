// Browser check of "Style and text" (Layout & Style, 9 November 2026), in the rig of harness.mjs (pretend Firebase).
//   Run: node tests/responsive/styleEditor.scenario.mjs    (screenshots go to tests/screenshots/style-*.png)
import { launch, openPage, check } from "./harness.mjs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const SHOTS = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../screenshots");
let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };

const browser = await launch();
try {
    const { page, errors, settle } = await openPage(browser, "dash", { width: 1440, height: 900 });
    await settle();
    // a layout of your own first (Style asks for one): answer the name question
    await page.evaluate(() => document.querySelector('.board .panel[data-panel="activity"] .panel-menu-btn').click());
    await page.locator(".menu .menu-item", { hasText: "Style and text" }).click();
    const ask = page.locator(".modal-overlay input[type=text]").first();
    await ask.fill("Tester");
    await page.keyboard.press("Enter");
    await page.waitForSelector(".style-dialog", { timeout: 5000 });
    ok(await page.locator(".panel.style-target").count() === 1, "the panel being styled is marked");

    await page.fill("#sdSubtitle", "Newest first");
    await page.fill("#sdNote", "Check harmful ones first.");
    await page.selectOption("#sdNotePlace", "bottom");
    await page.selectOption("#sdAnim", "zoom");
    await page.selectOption("#sdEdge", "always");
    await page.fill("#sdTitleText", "Latest scans");
    const live = await page.evaluate(() => {
        const p = document.querySelector('.panel[data-panel="activity"]');
        return { title: p.querySelector(".panel-title").textContent, sub: (p.querySelector(".panel-subtitle-text") || {}).textContent, noteLast: p.querySelector(":scope > .panel-inner").lastElementChild.className, edge: p.dataset.edge };
    });
    ok(live.title === "Latest scans" && live.sub === "Newest first" && live.noteLast === "panel-note" && live.edge === "always", "changes show on the panel at once " + JSON.stringify(live));
    await page.screenshot({ path: path.join(SHOTS, "style-dialog.png") });
    await page.click(".style-dialog .save-btn");
    await page.waitForSelector(".style-dialog", { state: "detached" });

    const saved = await page.evaluate(() => JSON.parse(localStorage.getItem("glowup.layouts.v1")).profiles[0].pages.dashboard);
    ok(saved.texts.activity.title === "Latest scans" && saved.styles.activity.anim === "zoom", "the style is kept in the layout");

    // the page after a reload still has it
    await page.reload(); await settle();
    ok(await page.evaluate(() => document.querySelector('.panel[data-panel="activity"] .panel-title').textContent) === "Latest scans", "it is there after a reload");

    // Undo puts the page's own title back (a fresh visit has no undo, so change once more and undo that)
    await page.evaluate(() => document.querySelector('.board .panel[data-panel="activity"] .panel-menu-btn').click());
    await page.locator(".menu .menu-item", { hasText: "Style and text" }).click();
    await page.fill("#sdTitleText", "");
    await page.click(".style-dialog .save-btn");
    await page.waitForSelector(".style-dialog", { state: "detached" });
    const own = await page.evaluate(() => document.querySelector('.panel[data-panel="activity"] .panel-title').textContent);
    await page.keyboard.press("Control+z");
    const back = await page.evaluate(() => document.querySelector('.panel[data-panel="activity"] .panel-title').textContent);
    ok(own !== "Latest scans" && back === "Latest scans", `Ctrl+Z takes the last change back (${own} -> ${back})`);
    await page.keyboard.press("Control+y");
    ok(await page.evaluate(() => document.querySelector('.panel[data-panel="activity"] .panel-title').textContent) === own, "Ctrl+Y does it again");

    // Cancel puts back what was there
    await page.evaluate(() => document.querySelector('.board .panel[data-panel="activity"] .panel-menu-btn').click());
    await page.locator(".menu .menu-item", { hasText: "Style and text" }).click();
    await page.fill("#sdTitleText", "Will be cancelled");
    await page.click(".style-dialog [data-close]");
    ok(await page.evaluate(() => document.querySelector('.panel[data-panel="activity"] .panel-title').textContent) === own, "Cancel leaves the panel as it was");

    // the Numbers tiles one under the other, and the sizes of the window: no overlap, no sideways scroll
    await page.evaluate(() => {
        const store = JSON.parse(localStorage.getItem("glowup.layouts.v1"));
        const d = store.profiles[0].pages.dashboard;
        d.styles = Object.assign(d.styles || {}, { numbers: { orient: "row", scale: 1.25 }, results: { colors: { light: { bg: "#fde8ef", text: "#3a1020" } } } });
        d.texts = Object.assign(d.texts || {}, { numbers: { labels: { 0: "Scans since midnight with a long label" } }, results: { note: "Only today.", notePlace: "top" } });
        localStorage.setItem("glowup.layouts.v1", JSON.stringify(store));
    });
    for (const width of [1440, 1024, 800, 600, 375]) {
        await page.setViewportSize({ width, height: 900 });
        await page.reload(); await settle();
        const report = await check(page);
        const overlap = await page.evaluate(() => {
            const boxes = Array.from(document.querySelectorAll(".board > .panel:not(.is-hidden)")).map((p) => p.getBoundingClientRect());
            for (let i = 0; i < boxes.length; i++) for (let j = i + 1; j < boxes.length; j++) {
                const a = boxes[i], b = boxes[j];
                if (a.left < b.right - 1 && b.left < a.right - 1 && a.top < b.bottom - 1 && b.top < a.bottom - 1) return true;
            }
            return false;
        });
        const sideways = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1);
        const tilesFit = await page.evaluate(() => Array.from(document.querySelectorAll(".kpi")).every((k) => k.scrollWidth <= k.clientWidth + 2 || getComputedStyle(k).overflow !== "visible"));
        ok(!overlap, `${width}px: no panels overlap`);
        ok(!sideways, `${width}px: no sideways scroll`);
        ok(tilesFit, `${width}px: number tiles hold their text`);
        if (report && report.problems && report.problems.length) console.log(`${width}px rig notes:`, report.problems.slice(0, 5));
        await page.screenshot({ path: path.join(SHOTS, `style-${width}.png`), fullPage: false });
    }
    const real = errors.filter((e) => !/shared default layout/i.test(e));
    ok(real.length === 0, "no page errors: " + real.slice(0, 3).join(" | "));
} finally {
    await browser.close();
}
console.log(`checks made: ${checks}`);
if (failed) { console.log(`${failed} FAILED`); process.exit(1); }
console.log("ALL PASSED");
