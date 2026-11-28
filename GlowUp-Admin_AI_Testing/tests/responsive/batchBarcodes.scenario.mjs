// Batch Lookup (23 Nov 2026): a toolbar button opens a drawer; pasted barcodes are counted, looked up, listed with a status,
// and only the selected, found ones are saved, as Pending, with leading zeros kept and every value marked Detected.
// Every outside answer is made up by the test.
import { launch, openPage } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };

const KNOWN = {
    "0041554239416": { code: "0041554239416", product_name: "Batch Great Lash Mascara", brands: "Batchbrand", categories_tags: ["en:mascaras"], ingredients_text_en: "Aqua, Glycerin, Cera Alba" },
    "3600531652294": { code: "3600531652294", product_name: "Batch Matte Lip Tint", brands: "Batchbrand", categories_tags: ["en:lipsticks"], ingredients_text_en: "Ricinus Communis Seed Oil, Mica" }
};

const browser = await launch();
try {
    for (const width of [1440, 390]) {
        const { page, ctx, errors, settle } = await openPage(browser, "inv", { width, height: 900 });
        const asked = [];
        await ctx.route("https://world.openbeautyfacts.org/**", (route) => {
            const url = route.request().url();
            const m = url.match(/\/product\/(\d+)\.json/);
            if (m) asked.push(m[1]);
            const hit = m && KNOWN[m[1]];
            return route.fulfill({ status: 200, contentType: "application/json", headers: { "access-control-allow-origin": "*" }, body: JSON.stringify(hit ? { status: 1, product: hit } : { status: 0 }) });
        });
        await ctx.route("**/api/upc-lookup", (route) => route.fulfill({ status: 404, contentType: "application/json", headers: { "access-control-allow-origin": "*" }, body: JSON.stringify({ found: 0, items: [] }) }));
        await settle(800);

        ok(await page.locator("[data-panel=batch]").count() === 0, `${width}px: no permanent Batch panel in the sidebar`);
        ok(await page.locator("#batchLookupBtn").count() === 1, `${width}px: Batch Lookup button is in the Inventory toolbar`);
        await page.evaluate(() => document.getElementById("batchLookupBtn").click());
        await page.waitForSelector(".batch-window");
        const box = await page.evaluate(() => { const r = document.querySelector(".batch-window").getBoundingClientRect(); return { l: r.left, w: r.width, h: r.height, vw: window.innerWidth, vh: window.innerHeight }; });
        ok(box.l + box.w <= box.vw + 1 && box.h <= box.vh + 1, `${width}px: the drawer fits the screen`);
        ok(width > 640 ? box.w < box.vw : box.w >= box.vw - 1, `${width}px: drawer on a wide screen, full width on a phone`);
        ok(await page.isDisabled(".batch-window .bl-actions .save-btn"), `${width}px: Look up is disabled with nothing entered`);

        await page.fill("#batchLookupText", "Barcode\n0041554239416\n3600531652294\n0041554239416\n8001234567893\n123");
        const counts = await page.textContent(".bl-counts");
        ok(/5 entered: 3 valid, 1 duplicate, 1 invalid/.test(counts), `${width}px: counts are live (${counts})`);
        ok(!(await page.isDisabled(".batch-window .bl-actions .save-btn")), `${width}px: Look up is enabled`);
        await page.click(".batch-window .bl-actions .save-btn");
        await page.waitForFunction(() => /Done:/.test(document.querySelector(".bl-progress").textContent), null, { timeout: 15000 });
        ok(asked.filter((c) => c === "0041554239416").length === 1, `${width}px: a repeated barcode is looked up once`);
        const statuses = await page.evaluate(() => Array.from(document.querySelectorAll(".bl-row")).map((r) => r.dataset.status).join(","));
        ok(statuses === "found,found,duplicate,notfound,invalid" || ["found", "duplicate", "notfound", "invalid"].every((s) => statuses.includes(s)), `${width}px: each row has a status (${statuses})`);
        ok(await page.locator(".bl-row[data-status=found] input:checked").count() === 2, `${width}px: found rows start selected`);
        ok(await page.locator(".bl-row .btn", { hasText: "Retry" }).count() === 1, `${width}px: a not-found row can be retried`);

        // nothing is saved before the import is confirmed
        const before = await page.evaluate(() => Array.from(window.__STORE.keys()).filter((k) => k.startsWith("products/")).length);
        await page.evaluate(() => Array.from(document.querySelectorAll(".bl-row[data-status=found] input")).slice(1).forEach((i) => i.click()));
        ok(await page.locator(".bl-summary .chip", { hasText: "Selected" }).textContent().then((t) => /^1 /.test(t.trim())), `${width}px: the summary counts the selection`);
        await page.click(".batch-window .bl-importbar .save-btn");
        await page.waitForSelector(".modal-overlay:last-of-type .modal-actions .save-btn");
        await page.click(".modal-overlay:last-of-type .modal-actions .save-btn");
        await page.waitForFunction((n) => Array.from(window.__STORE.keys()).filter((k) => k.startsWith("products/")).length > n, before, { timeout: 8000 });
        const saved = await page.evaluate(() => {
            const out = [];
            for (const [k, v] of window.__STORE) if (k.startsWith("products/") && /^Batch /.test(v.name || "")) out.push(JSON.parse(JSON.stringify(v)));
            return out;
        });
        ok(saved.length === 1, `${width}px: only the selected product was saved (${saved.length})`);
        const lash = saved[0];
        ok(lash && lash.barcode === "0041554239416", `${width}px: the barcode keeps its leading zero (${lash && lash.barcode})`);
        ok(lash && lash.status === "Pending" && lash.reviewed === false, `${width}px: saved products wait for review`);
        ok(lash && lash.fieldOrigins && lash.fieldOrigins.ingredients && lash.fieldOrigins.ingredients.state === "detected", `${width}px: values are Detected`);
        ok(await page.locator(".bl-row[data-status=found]").count() === 1, `${width}px: the unselected found row stays in the list`);
        ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), `${width}px: no sideways scroll`);

        // Escape asks before throwing results away; Discard closes
        await page.keyboard.press("Escape");
        await page.waitForSelector(".modal-overlay:last-of-type .modal-actions .cancel-btn");
        await page.click(".modal-overlay:last-of-type .modal-actions .save-btn");
        await page.waitForSelector(".batch-window", { state: "detached", timeout: 3000 });
        ok(true, `${width}px: Escape then Discard closes the drawer`);
        ok(errors.length === 0, `${width}px: no page errors: ` + errors.slice(0, 3).join(" | "));
        await ctx.close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
