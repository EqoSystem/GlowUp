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

        await page.evaluate(() => document.querySelector('[data-list-edit="conditions"]').click());
        await page.waitForSelector(".lists-box .lists-row", { timeout: 6000 });
        ok(await page.locator(".lists-box .lists-row").count() === 30, `${width}px: with no saved list, the official 30 are shown`);
        await page.evaluate(() => Array.from(document.querySelectorAll(".lists-box button")).find((b) => b.textContent.trim() === "Use the official list").click());
        await page.waitForSelector(".modal-confirm, [role=alertdialog], .confirm-box", { timeout: 4000 }).catch(() => {});
        await page.evaluate(() => { const b = Array.from(document.querySelectorAll("button")).find((x) => x.textContent.trim() === "Use official list"); if (b) b.click(); });
        await page.waitForTimeout(400);
        const rows = await page.locator(".lists-box .lists-row").count();
        const ticked = await page.locator(".lists-box .lists-pick input:checked").count();
        ok(rows === 30, `${width}px: the official list has 30 conditions (${rows})`);
        ok(ticked === 15, `${width}px: 15 of them are marked "Users can pick it" (${ticked})`);
        const labels = await page.evaluate(() => Array.from(document.querySelectorAll(".lists-box .lists-label")).map((i) => i.value));
        ["Atopic Dermatitis", "Hidradenitis Suppurativa", "Cutaneous Lupus Erythematosus", "Ichthyosis", "Cold Sores", "Keratosis Pilaris"].forEach((l) => ok(labels.includes(l), `${width}px: "${l}" is in the list`));
        await page.click(".lists-box .save-btn");
        await page.waitForTimeout(900);
        const saved = await page.evaluate(() => { const d = window.__STORE.get("config/lists"); return d ? JSON.parse(JSON.stringify(d)) : null; });
        ok(saved && saved.conditions.length === 30 && saved.conditions.filter((c) => c.chronic === true).length === 15, `${width}px: the saved document has 30 conditions, 15 with chronic: true`);
        ok(saved && saved.conditions.find((c) => c.id === "eczema" && c.label === "Eczema"), `${width}px: the old id "eczema" is kept (ingredient rules still match)`);
        ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), `${width}px: no sideways scroll`);
        ok(errors.length === 0, `${width}px: no page errors: ` + errors.slice(0, 3).join(" | "));
        await ctx.close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
