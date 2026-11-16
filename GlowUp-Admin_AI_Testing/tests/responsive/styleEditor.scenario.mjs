

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

    await page.evaluate(() => document.querySelector('.board .panel[data-panel="activity"] .panel-menu-btn').click());
    await page.locator(".menu .menu-item", { hasText: "Style and text" }).click();
    const ask = page.locator(".modal-overlay input[type=text]").first();
    await ask.fill("Tester");
    await page.keyboard.press("Enter");
    await page.waitForSelector(".style-dialog", { timeout: 5000 });
    ok(await page.locator(".panel.style-target").count() === 1, "the panel being styled is marked");

    ok(await page.locator("#sdSubtitle, #sdNote").count() === 0, "the panel editor has no box that would ADD text (subtitle, note)");
    ok(await page.locator("#sdWidth").count() === 0 && await page.locator("#sdWidthPx").count() === 1 && await page.locator("#sdHeight").count() === 1, "only Width (pixels) and Height (pixels) are offered");
    ok(await page.locator(".style-dialog .form-section > .field-hint:not(.contrast-note):not(#sdMotionHint)").count() === 0, "long helper text sits in the section titles' i, not under them");
    const tipCount = await page.locator(".style-dialog .sd-section-head .info-badge").count();
    ok(tipCount >= 5, "the section titles carry an i (" + tipCount + ")");
    await page.locator(".style-dialog .sd-section-head .info-badge").nth(1).click();
    await page.waitForTimeout(250);
    const tipBox = await page.evaluate(() => { const p = document.getElementById("infoPop"); if (!p || getComputedStyle(p).visibility === "hidden" || getComputedStyle(p).display === "none") return null; const r = p.getBoundingClientRect(); return { l: r.left, r: r.right, t: r.top, b: r.bottom, w: innerWidth, h: innerHeight }; });
    ok(tipBox && tipBox.l >= 0 && tipBox.r <= tipBox.w && tipBox.t >= 0 && tipBox.b <= tipBox.h, "a tapped i opens inside the window " + JSON.stringify(tipBox));
    await page.keyboard.press("Escape");
    await page.selectOption("#sdAnim", "zoom");
    await page.selectOption("#sdEdge", "always");
    await page.fill("#sdTitleText", "Latest scans");
    await page.waitForTimeout(400);
    const live = await page.evaluate(() => {
        const p = document.querySelector('.panel[data-panel="activity"]');
        return { title: p.querySelector(".panel-title").textContent, extra: p.querySelectorAll(".panel-subtitle-text, .panel-note").length, edge: p.dataset.edge };
    });
    ok(live.title === "Latest scans" && live.extra === 0 && live.edge === "always", "changes show on the panel at once " + JSON.stringify(live));
    await page.screenshot({ path: path.join(SHOTS, "style-dialog.png") });
    await page.click(".style-dialog .save-btn");
    await page.waitForSelector(".style-dialog", { state: "detached" });

    const saved = await page.evaluate(() => JSON.parse(localStorage.getItem("glowup.layouts.v1")).profiles[0].pages.dashboard);
    ok(saved.texts.activity.title === "Latest scans" && saved.styles.activity.anim === "zoom", "the style is kept in the layout");


    await page.reload(); await settle();
    ok(await page.evaluate(() => document.querySelector('.panel[data-panel="activity"] .panel-title').textContent) === "Latest scans", "it is there after a reload");


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


    await page.evaluate(() => document.querySelector('.board .panel[data-panel="activity"] .panel-menu-btn').click());
    await page.locator(".menu .menu-item", { hasText: "Style and text" }).click();
    await page.fill("#sdTitleText", "Will be cancelled");
    // "Leave site?" shows only for a real window close with unsaved changes, never for the app's own redirect (session end, sign-out)
    const asks = () => page.evaluate(() => { const e = new Event("beforeunload", { cancelable: true }); window.dispatchEvent(e); return e.defaultPrevented; });
    ok(await asks() === true, "closing the window with unsaved changes asks");
    await page.evaluate(() => { window.__glowupLeaving = true; });
    ok(await asks() === false, "the app's own redirect (session end) does not ask");
    await page.evaluate(() => { window.__glowupLeaving = false; });
    await page.click(".style-dialog [data-close]");

    ok(await page.locator(".modal-content", { hasText: "Discard changes?" }).count() === 1, "Cancel with unsaved changes asks Discard changes?");
    await page.keyboard.press("Escape");
    ok(await page.locator(".style-dialog").count() === 1 && await page.inputValue("#sdTitleText") === "Will be cancelled", "Escape on that question keeps editing");
    await page.click(".style-dialog [data-close]");
    await page.locator(".modal-content button", { hasText: "Discard" }).click();
    await page.waitForSelector(".style-dialog", { state: "detached" });
    ok(await page.evaluate(() => document.querySelector('.panel[data-panel="activity"] .panel-title').textContent) === own, "Cancel leaves the panel as it was");


    await page.evaluate(() => {
        const store = JSON.parse(localStorage.getItem("glowup.layouts.v1"));
        const d = store.profiles[0].pages.dashboard;
        d.styles = Object.assign(d.styles || {}, { numbers: { orient: "row", scale: 1.25 }, results: { colors: { light: { bg: "#fde8ef", text: "#3a1020" } } } });
        d.texts = Object.assign(d.texts || {}, { numbers: { labels: { 0: "Scans since midnight with a long label" } }, results: { title: "Results" } });
        localStorage.setItem("glowup.layouts.v1", JSON.stringify(store));
    });
    // pixel size and card-label look on the "numbers" panel: bad values are named, good ones apply, nothing overlaps
    await page.evaluate(() => document.querySelector('.board .panel[data-panel="numbers"] .panel-menu-btn').click());
    await page.locator(".menu .menu-item", { hasText: "Style and text" }).click();
    await page.waitForSelector(".style-dialog", { timeout: 5000 });
    await page.fill("#sdWidthPx", "50");
    await page.dispatchEvent("#sdWidthPx", "change");
    ok(await page.locator("#sdWidthPxErr").isVisible(), "a width below the allowed range is named under its box");
    await page.click(".style-dialog .save-btn");
    ok(await page.locator(".style-dialog").count() === 1, "Save is refused while the size is wrong");
    await page.fill("#sdWidthPx", "420"); await page.dispatchEvent("#sdWidthPx", "change");
    await page.fill("#sdHeight", "240"); await page.dispatchEvent("#sdHeight", "change");
    await page.fill("#sdLabel0", "Scans since midnight with a very long label to wrap");
    await page.fill("#sdLabelSize0", "20");
    await page.selectOption("#sdLabelStyle0", "bold");
    await page.selectOption("#sdLabelAlign0", "center");
    await page.waitForTimeout(120);
    const look = await page.evaluate(() => {
        const p = document.querySelector('.panel[data-panel="numbers"]');
        const l = p.querySelector(".kpi-title, .stat-title");
        return { w: p.hasAttribute("data-fixed-w"), px: p.style.getPropertyValue("--wpx"), h: p.style.height, size: l.style.fontSize, align: l.style.textAlign, weight: l.style.fontWeight, text: l.textContent.length };
    });
    ok(look.w && look.px === "420px" && look.h === "240px" && look.size === "20px" && look.align === "center" && look.weight === "700" && look.text > 20, "pixel size and label look show at once " + JSON.stringify(look));
    await page.click(".style-dialog .save-btn");
    await page.waitForSelector(".style-dialog", { state: "detached" });
    const kept = await page.evaluate(() => { const d = JSON.parse(localStorage.getItem("glowup.layouts.v1")).profiles[0].pages.dashboard; return { size: d.size && d.size.numbers, ls: d.texts.numbers.labelStyles }; });
    ok(kept.size && kept.size.wpx === 420 && kept.size.h === 240 && kept.ls && kept.ls[0].align === "center", "size and label look are kept " + JSON.stringify(kept));
    ok(kept.ls && Object.keys(kept.ls).length > 1 && Object.values(kept.ls).every((l) => l.align === "center" && l.size === 20 && l.style === "bold"), "the look went to every card of the panel " + JSON.stringify(kept.ls));
    ok(kept.size && Number(kept.size.w) >= 2, "the panel keeps its column span (the Width list is gone, nothing moved) " + JSON.stringify(kept.size));
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
