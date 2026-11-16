// New20: the four report panels (Suggestions, Bug reports, Wrong scan results, Other reports) must not draw
// anything on top of anything else: the helper strip stays above the list, and inside each row the kind chip,
// date, message, sender and the two buttons never cross. Checked at desktop, tablet and phone widths, with the
// panels at their natural height AND pinned to a short fixed height (the case seen on the live site).
// Run: node tests/responsive/reportPanels.scenario.mjs   (SHOT_DIR=<folder> to keep pictures)
import { launch, openPage } from "./harness.mjs";
let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };
const PANELS = ["suggestions", "support", "supportWrong", "supportOther"];
const LONG = "The app froze on the result screen after I scanned a lipstick and then I pressed back twice, the screen went white and I had to close the app. Pneumonoultramicroscopicsilicovolcanoconiosis-style-very-long-word-without-spaces-to-test-wrapping.";

const browser = await launch();
try {
    for (const [width, height] of [[1440, 900], [1024, 768], [768, 1024], [390, 844], [320, 640]]) {
        const { page, errors, settle, url } = await openPage(browser, "dash", { width, height, go: false });
        await page.addInitScript((long) => {
            const now = Math.floor(Date.now() / 1000), r = window.__SEED.supportReports;
            const who = { uid: "QK9x5pn3abcdef", userName: "Maria Clara Dela Cruz-Santos de los Reyes", appVersion: "0.44.0", status: "new", device: "Pixel 7, Android 14" };
            r.b2 = { ...who, category: "Bug", message: long, createdAtUnix: now - 4 * 86400 };
            r.w2 = { ...who, category: "Wrong scan result", message: "wrong scan Result", createdAtUnix: now - 4 * 86400 };
            r.o2 = { ...who, category: "Other", message: long, createdAtUnix: now - 4 * 86400 };
            r.s2 = { ...who, category: "Suggestion", message: "Suggestion test", createdAtUnix: now - 4 * 86400 };
        }, LONG);
        await page.goto(url, { waitUntil: "load" });
        await page.waitForTimeout(900); await settle(500);
        await page.waitForSelector("#supportList li", { timeout: 6000 });

        for (const pinned of [false, true]) {
            if (pinned) {
                await page.evaluate((ids) => ids.forEach((id) => { const p = document.querySelector(`.board .panel[data-panel="${id}"]`); if (p) { p.style.height = "240px"; p.setAttribute("data-fixed-h", "1"); } }), PANELS);
                await settle(250);
            }
            const faults = await page.evaluate((ids) => {
                const out = [], box = (e) => e.getBoundingClientRect();
                const shown = (e) => { const r = box(e), s = getComputedStyle(e); return r.width > 0 && r.height > 0 && s.visibility !== "hidden" && s.display !== "none"; };
                const cross = (a, b) => { const r = box(a), q = box(b); return r.left < q.right - 1 && q.left < r.right - 1 && r.top < q.bottom - 1 && q.top < r.bottom - 1; };
                for (const id of ids) {
                    const panel = document.querySelector(`.board .panel[data-panel="${id}"]`);
                    if (!panel) { out.push(id + ": panel missing"); continue; }
                    const inner = panel.querySelector(":scope > .panel-inner"), strip = inner.querySelector(":scope > .assist"), list = inner.querySelector(":scope > ul");
                    if (strip && list && shown(strip) && shown(list) && box(strip).bottom > box(list).top + 1) out.push(`${id}: helper strip bottom ${Math.round(box(strip).bottom)} runs into the list top ${Math.round(box(list).top)}`);
                    if (strip) strip.querySelectorAll("button").forEach((b) => { if (shown(b) && box(b).bottom > box(strip).bottom + 1) out.push(`${id}: helper button "${b.textContent.trim()}" spills out of its strip`); });
                    const rows = [...inner.querySelectorAll("li.report-item")];
                    if (rows.length === 0) out.push(id + ": no rows drawn");
                    rows.forEach((li, n) => {
                        const parts = [...li.querySelectorAll(".report-head .chip, .report-when, .report-text, .report-from, .suggestion-actions .row-btn")].filter(shown);
                        for (let i = 0; i < parts.length; i++) for (let j = i + 1; j < parts.length; j++)
                            if (cross(parts[i], parts[j])) out.push(`${id} row ${n}: ${parts[i].className.split(" ")[0]} overlaps ${parts[j].className.split(" ")[0]}`);
                        parts.forEach((p) => { if (box(p).right > box(li).right + 1 || box(p).left < box(li).left - 1) out.push(`${id} row ${n}: ${p.className.split(" ")[0]} sticks out of its row`); });
                        li.querySelectorAll(".suggestion-actions").forEach((a) => { if (getComputedStyle(a).opacity !== "1") out.push(`${id} row ${n}: actions hidden until hover`); });
                    });
                }
                return [...new Set(out)].slice(0, 15);
            }, PANELS);
            faults.forEach((f) => ok(false, `${width}px${pinned ? " pinned" : ""}: ${f}`));
            ok(true, `${width}px${pinned ? " pinned" : ""}: measured`);
            ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), `${width}px: no sideways scroll`);
            if (process.env.SHOT_DIR) for (const id of PANELS) await page.locator(`.board .panel[data-panel="${id}"]`).screenshot({ path: `${process.env.SHOT_DIR}/report_${id}_${width}${pinned ? "_pinned" : ""}.png` });
        }
        ok(errors.length === 0, `${width}px: no page errors ${errors.join("; ")}`);
        await page.context().close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
