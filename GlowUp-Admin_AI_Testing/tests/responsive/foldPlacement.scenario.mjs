// New22 fold placement: Inventory's "Needs your attention" (tools) takes the Import box's space when Import is folded and gives
// it back when it opens; on the dashboard the folded report panels line up in the right-hand column under Most scanned.
// Run: node tests/responsive/foldPlacement.scenario.mjs   (SHOT_DIR=<folder> saves pictures)
import { launch, openPage, check } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (cond, what) => { checks++; if (!cond) { failed++; console.log("FAILED: " + what); } };
const shot = async (page, name) => { if (process.env.SHOT_DIR) await page.screenshot({ path: `${process.env.SHOT_DIR}/${name}.png`, fullPage: true }); };
const rect = (page, id) => page.evaluate((id) => { const r = document.querySelector(`.board > .panel[data-panel="${id}"]`).getBoundingClientRect(); return { x: r.left, y: r.top, r: r.right, b: r.bottom, w: r.width }; }, id);
const board = (page) => page.evaluate(() => { const b = document.querySelector(".board"), s = getComputedStyle(b), r = b.getBoundingClientRect(); return { l: r.left + parseFloat(s.paddingLeft), r: r.right - parseFloat(s.paddingRight) }; });
const fold = async (page, id) => { await page.click(`.board > .panel[data-panel="${id}"] .panel-fold-btn`); await page.waitForTimeout(250); await page.evaluate(() => document.getAnimations().forEach((a) => { try { a.finish(); } catch (e) { } })); await page.waitForTimeout(80); };

const browser = await launch();
try {
    for (const width of [1440, 1280]) {
        // ---- inventory ----
        {
            const { page, ctx, errors, settle } = await openPage(browser, "inv", { width, height: 900 });
            await settle(400);
            // the owner's layout: Needs your attention (8 wide) beside the Import box (4 wide)
            await page.evaluate(() => document.querySelector('.board > .panel[data-panel="notes"]').style.setProperty("--w", "8"));
            await settle(400);
            const B = await board(page);
            const tools0 = await rect(page, "notes"), imp0 = await rect(page, "import");
            ok(imp0.x > tools0.r - 1 && Math.abs(imp0.y - tools0.y) < 4, `${width} inv: Import starts beside Needs your attention`);
            await fold(page, "import");
            const tools1 = await rect(page, "notes"), imp1 = await rect(page, "import");
            ok(Math.abs(tools1.r - B.r) < 2 && tools1.w > tools0.w + 50, `${width} inv: folding Import lets Needs your attention take its space (${Math.round(tools0.w)} -> ${Math.round(tools1.w)})`);
            ok(Math.abs(imp1.r - B.r) < 2 && imp1.y >= tools1.b - 2, `${width} inv: the folded Import bar moves below, in the right-hand column`);
            const f1 = await check(page);
            ok(f1.length === 0, `${width} inv folded: layout faults ${f1.join(" | ")}`);
            await shot(page, `fold_inv_${width}`);
            await fold(page, "import");
            const tools2 = await rect(page, "notes"), imp2 = await rect(page, "import");
            ok(Math.abs(tools2.w - tools0.w) < 2 && Math.abs(imp2.x - imp0.x) < 2, `${width} inv: opening Import gives its space back`);
            ok(errors.length === 0, `${width} inv: no page errors ${errors.join("; ")}`);
            await ctx.close();
        }
        // ---- dashboard ----
        {
            const { page, ctx, errors, settle } = await openPage(browser, "dash", { width, height: 900 });
            await settle(400);
            const B = await board(page);
            for (const id of ["support", "supportOther", "suggestions", "supportWrong"]) await fold(page, id);
            const brands = await rect(page, "brands");
            const bars = [];
            for (const id of ["suggestions", "support", "supportWrong", "supportOther"]) bars.push(await rect(page, id));
            ok(bars.every((b) => Math.abs(b.r - B.r) < 2 && Math.abs(b.x - bars[0].x) < 2), `${width} dash: the folded report panels share the right-hand column`);
            ok(bars.every((b) => b.y >= brands.b - 2), `${width} dash: they sit under the right-hand panels (Most scanned)`);
            const ys = bars.map((b) => b.y).sort((a, b) => a - b);
            ok(ys.every((y, i) => i === 0 || y > ys[i - 1] + 10), `${width} dash: one under the other, no overlap`);
            const f = await check(page);
            ok(f.length === 0, `${width} dash folded: layout faults ${f.join(" | ")}`);
            await shot(page, `fold_dash_${width}`);
            for (const id of ["support", "supportOther", "suggestions", "supportWrong"]) await fold(page, id);
            const open = await rect(page, "support");
            ok(open.w > bars[0].w + 50, `${width} dash: unfolded, a report panel has its own width again`);
            ok(errors.length === 0, `${width} dash: no page errors ${errors.join("; ")}`);
            await ctx.close();
        }
    }
    // phones keep one column: nothing moves sideways
    {
        const { page, ctx, settle } = await openPage(browser, "inv", { width: 390, height: 844, touch: true });
        await settle(400);
        await fold(page, "import");
        const f = await check(page);
        ok(f.length === 0, `390 inv folded: layout faults ${f.join(" | ")}`);
        await ctx.close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
