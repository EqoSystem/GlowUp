// Official lists (23 Nov 2026): the skin types are Normal, Dry, Oily, Combination, Sensitive; the skin conditions list can be replaced
// by the official 15 chronic + 15 general conditions with one button, and "Users can pick it" marks the chronic ones.
import { launch, openPage } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };

const browser = await launch();
try {
    for (const width of [1440, 390]) {
        const { page, ctx, errors, settle } = await openPage(browser, "cur", { width, height: 900 });
        await settle(900);
        const types = await page.evaluate(() => Array.from(document.querySelectorAll("#skinTypesFit [data-fit-option], #skinTypesFit label, #skinTypesFit .fit-row")).map((n) => n.textContent.trim()).join("|"));
        ok(/Combination/.test(types) && /Sensitive/.test(types), `${width}px: the ingredient form offers Combination and Sensitive skin (${types.slice(0, 120)})`);
        ok(!/Mix \(combination\)/.test(types), `${width}px: the old "Mix (combination)" label is gone`);

        // the app no longer asks chronic skin conditions (27 Nov 2026): their list editor is gone from the form
        ok(await page.locator('[data-list-edit="conditions"]').count() === 0, `${width}px: no skin condition list editor`);
        ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), `${width}px: no sideways scroll`);
        ok(errors.length === 0, `${width}px: no page errors: ` + errors.slice(0, 3).join(" | "));
        await ctx.close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
