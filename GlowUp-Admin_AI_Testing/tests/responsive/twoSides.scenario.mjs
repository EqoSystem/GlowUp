// 22 Nov 2026: on a wide board the wide panels stay on the left and the narrow ones on the right, folded or not, with a thin
// divider between the two sides (adminLayouts.js placeSides). Replaces the New22 fold placement. Dashboard, Inventory, Curator.
// Run: node tests/responsive/twoSides.scenario.mjs
import { launch, openPage } from "./harness.mjs";
let checks = 0, failed = 0;
const ok = (cond, what) => { checks++; if (!cond) { failed++; console.log("FAILED: " + what); } };
const browser = await launch();
try {
    for (const key of ["dash", "inv", "cur"]) {
        const { page, ctx } = await openPage(browser, key, { width: 1440, height: 900 });
        await page.waitForTimeout(700);
        ok(await page.evaluate(() => document.querySelector(".board").classList.contains("two-sides")), `${key}: the board has two sides and a divider`);
        const moved = await page.evaluate(async () => {
            const bd = document.querySelector(".board"), out = [];
            for (const side of ["main", "side"]) {
                for (const p of bd.querySelectorAll(`:scope > .panel[data-side=${side}]`)) {
                    const btn = p.querySelector(".panel-fold-btn"); if (!btn || p.offsetParent === null) continue;
                    const x0 = Math.round(p.getBoundingClientRect().left);
                    btn.click(); await new Promise((r) => setTimeout(r, 450)); const x1 = Math.round(p.getBoundingClientRect().left);
                    btn.click(); await new Promise((r) => setTimeout(r, 450)); const x2 = Math.round(p.getBoundingClientRect().left);
                    out.push({ id: p.dataset.panel, side, x0, x1, x2 });
                }
            }
            return out;
        });
        ok(moved.length >= 1 && moved.every((m) => m.x0 === m.x1 && m.x1 === m.x2), `${key}: folding and unfolding never moves a panel to the other side ${JSON.stringify(moved)}`);
        const sides = await page.evaluate(() => { const bd = document.querySelector(".board"); const l = (s) => [...bd.querySelectorAll(`:scope > .panel[data-side=${s}]`)].filter((p) => p.offsetParent).map((p) => Math.round(p.getBoundingClientRect().left)); return { main: l("main"), side: l("side") }; });
        ok(new Set(sides.main).size === 1 && new Set(sides.side).size === 1 && sides.side[0] > sides.main[0], `${key}: every left panel starts on the left edge, every right panel on the right column ${JSON.stringify(sides)}`);
        await ctx.close();
        // narrow screens: no sides (one column)
        const narrow = await openPage(browser, key, { width: 900, height: 900 });
        ok(!(await narrow.page.evaluate(() => document.querySelector(".board").classList.contains("two-sides"))), `${key}: a narrow board has no sides`);
        await narrow.ctx.close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
