// Batch barcodes (23 Nov 2026): a pasted list of barcodes is looked up, checked in "Check before saving", and saved as Pending with
// leading zeros kept and every value marked Detected from the barcode look-up. A barcode nobody knows is reported, not invented.
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

        // a plain sentence is refused without any look-up
        await page.evaluate(() => { document.querySelector(".import-batch").open = true; });
        await page.fill("#inventoryBatchText", "just some words");
        await page.click("#inventoryBatchBtn");
        await page.waitForTimeout(300);
        ok(asked.length === 0, `${width}px: words are not looked up as barcodes`);

        await page.fill("#inventoryBatchText", "Barcode\n0041554239416\n3600531652294\n0041554239416\n8001234567893");
        await page.click("#inventoryBatchBtn");
        await page.waitForSelector(".import-cover.ready", { timeout: 15000 });
        ok(asked.filter((c) => c === "0041554239416").length === 1, `${width}px: a repeated barcode is looked up once`);
        ok(asked.includes("3600531652294") && asked.includes("8001234567893"), `${width}px: every different barcode is looked up (${asked.join(",")})`);
        await page.click(".import-cover .btn.primary");
        await page.waitForSelector(".import-check");
        const checkText = await page.textContent(".import-check");
        ok(/Batch Great Lash Mascara/.test(checkText) && /Batch Matte Lip Tint/.test(checkText), `${width}px: the found products are listed to check before saving`);
        await page.click(".import-check [data-save]");
        await page.waitForSelector(".import-check", { state: "detached", timeout: 8000 });
        await page.waitForTimeout(500);
        const saved = await page.evaluate(() => {
            const out = [];
            for (const [k, v] of window.__STORE) if (k.startsWith("products/") && /^Batch /.test(v.name || "")) out.push(JSON.parse(JSON.stringify(v)));
            return out;
        });
        ok(saved.length === 2, `${width}px: two products were saved (${saved.length})`);
        const lash = saved.find((p) => /Great Lash/.test(p.name));
        ok(lash && lash.barcode === "0041554239416", `${width}px: the barcode keeps its leading zero (${lash && lash.barcode})`);
        ok(lash && lash.status === "Pending" && lash.reviewed === false, `${width}px: saved products wait for review`);
        ok(lash && lash.fieldOrigins && lash.fieldOrigins.ingredients && lash.fieldOrigins.ingredients.state === "detected" && /Barcode look-up/.test(lash.fieldOrigins.ingredients.from), `${width}px: values are Detected, from the barcode look-up (${lash && lash.fieldOrigins && lash.fieldOrigins.ingredients && lash.fieldOrigins.ingredients.from})`);
        ok(lash && lash.brandName === "Batchbrand", `${width}px: the brand came from the look-up`);
        ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), `${width}px: no sideways scroll`);
        ok(errors.length === 0, `${width}px: no page errors: ` + errors.slice(0, 3).join(" | "));
        await ctx.close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
