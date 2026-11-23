// File import report (automation brief, 23 Nov 2026): every row that is not saved, or saved without a value, is named with its
// row number; a barcode already on another product is not copied; leading zeros are kept; imported values are Detected.
import { launch, openPage } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };
const product = (brand, name, type, extra = []) => ["=== PRODUCT ===", `BRAND_NAME: ${brand}`, `PRODUCT_NAME: ${name}`, `COSMETIC_TYPE: ${type}`, ...extra, "INGREDIENTS: Aqua | Mica | Talc | Dimethicone"];
const FILE = [
    ...product("Reportbrand", "Zero Barcode Tint", "Lip Tint", ["BARCODE: 0048000123456"]),
    ...product("Reportbrand", "Clashing Barcode Blush", "Blush", ["BARCODE: 480002"]),
    ...product("Reportbrand", "Short Barcode Liner", "Eyeliner", ["BARCODE: 12"]),
    ...product("Reportbrand", "Zero Barcode Tint", "Lip Tint")
].join("\n");

const browser = await launch();
try {
    for (const width of [1440, 390]) {
        const { page, errors, settle } = await openPage(browser, "inv", { width, height: 900 });
        await settle(800);
        await page.setInputFiles("#inventoryFileInput", { name: "report.txt", mimeType: "text/plain", buffer: Buffer.from(FILE) });
        await page.waitForSelector(".import-cover.ready", { timeout: 8000 });
        await page.click(".import-cover .btn.primary");
        await page.waitForSelector(".import-check");
        const checkText = await page.textContent(".import-check");
        await page.click(".import-check [data-save]");
        await page.waitForSelector(".import-check", { state: "detached", timeout: 8000 });
        await page.waitForTimeout(500);
        const report = await page.evaluate(() => { const b = document.getElementById("inventoryImportFlags"); return b && !b.hidden ? b.textContent : ""; });
        ok(/Row 2: "Clashing Barcode Blush".*WITHOUT its barcode 480002/s.test(report), `${width}px: a barcode already used is not copied and the row says so`);
        ok(/Row 3: "Short Barcode Liner".*not 6 to 14 digits/s.test(report), `${width}px: a too-short barcode is reported`);
        ok(/Zero Barcode Tint" of Reportbrand appears twice/.test(checkText) || /Zero Barcode Tint" of Reportbrand appears twice/.test(await page.textContent("body")), `${width}px: a row repeated in the file is reported before saving`);
        const saved = await page.evaluate(() => {
            const out = {};
            for (const [k, v] of window.__STORE) if (k.startsWith("products/") && /Barcode|Liner/.test(v.name || "")) out[v.name] = (out[v.name] || []).concat([JSON.parse(JSON.stringify(v))]);
            return out;
        });
        const zero = (saved["Zero Barcode Tint"] || []);
        ok(zero.length === 1, `${width}px: the repeated row was saved once (${zero.length})`);
        ok(zero[0] && zero[0].barcode === "0048000123456", `${width}px: the imported barcode keeps its leading zeros (${zero[0] && zero[0].barcode})`);
        ok(zero[0] && zero[0].status === "Pending" && zero[0].reviewed === false, `${width}px: imported products wait for review`);
        ok(zero[0] && zero[0].fieldOrigins && zero[0].fieldOrigins.ingredients && zero[0].fieldOrigins.ingredients.state === "detected" && /File: report\.txt/.test(zero[0].fieldOrigins.ingredients.from), `${width}px: imported values are Detected, from the file`);
        const clash = (saved["Clashing Barcode Blush"] || [])[0];
        ok(clash && !clash.barcode, `${width}px: the clashing product is saved without the barcode`);
        ok(errors.length === 0, `${width}px: no page errors: ` + errors.slice(0, 3).join(" | "));
        await page.context().close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
