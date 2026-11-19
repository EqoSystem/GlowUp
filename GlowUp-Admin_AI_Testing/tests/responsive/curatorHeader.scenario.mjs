// Ingredient Curator, list panel header on a phone: the "..." button, the Recycle bin chip with its count and the pager
// must not overlap (19 November 2026: the "..." button sat on top of the bin chip and cut its count, "24", off).
// Run: node tests/responsive/curatorHeader.scenario.mjs
import { launch, openPage } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (cond, what) => { checks++; if (!cond) { failed++; console.log("FAILED: " + what); } };

const browser = await launch();
try {
    for (const w of [320, 360, 375, 390, 414, 480, 768, 1024, 1440]) {
        const { page, ctx, url, settle } = await openPage(browser, "cur", { width: w, height: 800, touch: w < 1000, go: false });
        // a catalogue the size of the live one: 650 ingredients, 24 of them in the Recycle bin
        await ctx.addInitScript(() => {
            const s = window.__SEED;
            for (let i = 0; i < 650; i++) {
                s.ingredients["big" + i] = {
                    name: "Ingredient " + i, classificationId: "", purpose: "", riskLevel: "auto", description: "x", aliases: [], sources: [], source: "ai", reviewed: true,
                    createdAt: { seconds: 1700000000 - i, nanoseconds: 0 }, updatedAt: { seconds: 1700000000 - i, nanoseconds: 0 },
                    ...(i < 24 ? { binned: true, binnedAt: { seconds: 1700000100, nanoseconds: 0 } } : {})
                };
            }
        });
        await page.goto(url);
        await settle(1200);
        const problems = await page.evaluate(() => {
            const out = [];
            const head = document.querySelector("[data-panel=list] .panel-head");
            const els = [...head.querySelectorAll("button, select, .pager, h3")].filter((e) => { const r = e.getBoundingClientRect(); return r.width > 0 && r.height > 0; });
            for (const e of els) { const r = e.getBoundingClientRect(); if (r.left < -0.5 || r.right > innerWidth + 0.5) out.push("off screen: " + e.className); }
            for (let i = 0; i < els.length; i++) {
                for (let j = i + 1; j < els.length; j++) {
                    const a = els[i], c = els[j];
                    if (a.contains(c) || c.contains(a)) continue;
                    const x = a.getBoundingClientRect(), y = c.getBoundingClientRect();
                    const ox = Math.min(x.right, y.right) - Math.max(x.left, y.left), oy = Math.min(x.bottom, y.bottom) - Math.max(x.top, y.top);
                    if (ox > 1 && oy > 1) out.push("overlap: " + String(a.className).slice(0, 24) + " x " + String(c.className).slice(0, 24));
                }
            }
            const chip = document.querySelector("[data-panel=list] .bin-chip .chip-count");
            if (!chip || !/24/.test(chip.textContent)) out.push("the bin count is not shown");
            return out;
        });
        ok(problems.length === 0, `${w}px: list header has no overlap or clipping (${problems.join("; ")})`);
        await ctx.close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
