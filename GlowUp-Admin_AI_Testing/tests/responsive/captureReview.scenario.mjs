// Product capture and review (automation brief, 23 Nov 2026), in the browser with the mock database:
// barcode with leading zeros, Detected values, the "not checked yet" question on an Active save, Pending = waiting for review,
// variant hint, Send back with a note, Reject with a reason. Open Beauty Facts is answered by the test (no real network).
import { launch, openPage } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };

const CODE = "0012345678905";
const OFF = { status: 1, product: { code: CODE, product_name: "Superstay Matte Ink Liquid Lipstick Longwear Extra Long Product Name Edition 0 Mini", brands: "Maybelline New York",
    ingredients_text_en: "Aqua, Dimethicone, Talc, Glycerin, Mica", categories_tags: ["en:foundations"] } };

async function answerNetwork(ctx) {
    await ctx.route("https://world.openbeautyfacts.org/**", (route) => {
        const url = route.request().url();
        const known = url.includes(`/api/v2/product/${CODE}`);
        return route.fulfill({ status: known ? 200 : 404, contentType: "application/json", body: JSON.stringify(known ? OFF : {}), headers: { "access-control-allow-origin": "*" } });
    });
}
const stored = (page, match) => page.evaluate((m) => {
    for (const [key, value] of window.__STORE) if (key.startsWith("products/") && (key === "products/" + m || value.name === m)) return { id: key.slice(9), ...JSON.parse(JSON.stringify(value)) };
    return null;
}, match);

const browser = await launch();
try {
    for (const [label, width, height] of [["desktop", 1280, 860], ["phone", 390, 800]]) {
        const { page, ctx, errors, settle } = await openPage(browser, "inv", { width, height, touch: label === "phone" });
        await answerNetwork(ctx);
        await settle(500);

        // ---- add a product: brand first, then Quick fill by a barcode that starts with zeros ----
        await page.evaluate(() => document.getElementById("addMenuBtn").click());
        await page.waitForTimeout(200);
        await page.locator(".menu-item, [role=menuitem]").filter({ hasText: "Product" }).first().click();
        await page.waitForSelector("#productForm", { state: "visible" });
        await page.selectOption("#productBrandInput", "maybelline");
        await page.fill("#productQuickInput", CODE);
        await page.click("#productQuickFind");
        await page.waitForFunction(() => document.getElementById("productNameInput").value !== "", null, { timeout: 8000 }).catch(() => {});
        const form = await page.evaluate(() => ({ name: document.getElementById("productNameInput").value, barcode: document.getElementById("productBarcodeInput").value,
            similar: document.getElementById("productSimilar").hidden ? "" : document.getElementById("productSimilar").textContent }));
        ok(form.barcode === CODE, `${label}: the barcode keeps its leading zeros in the form (${form.barcode})`);
        ok(/Superstay Matte Ink/.test(form.similar), `${label}: a close product of the same brand is shown as a possible variant`);

        // ---- Active with values nobody checked: the admin is asked; "Save as Pending" puts it in the review queue ----
        await page.selectOption("#productStatusInput", "Active");
        await page.click("#saveProductBtn");
        await page.waitForSelector(".modal-content:has-text('Values not checked yet')", { timeout: 4000 }).catch(() => {});
        ok(await page.locator(".modal-content:has-text('Values not checked yet')").isVisible(), `${label}: an Active save with Detected values asks first`);
        await page.locator("button", { hasText: "Save as Pending for review" }).click();
        await page.waitForFunction(() => document.getElementById("productModal").style.display === "none" || !document.getElementById("productForm").offsetParent, null, { timeout: 6000 }).catch(() => {});
        await page.waitForTimeout(400);
        const saved = await stored(page, OFF.product.product_name);
        ok(!!saved, `${label}: the product was saved`);
        if (saved) {
            ok(saved.status === "Pending" && saved.reviewed === false, `${label}: it is Pending and waits for review (${saved.status}, reviewed ${saved.reviewed})`);
            ok(saved.barcode === CODE, `${label}: the stored barcode keeps its zeros (${saved.barcode})`);
            ok(saved.fieldOrigins && saved.fieldOrigins.ingredients && saved.fieldOrigins.ingredients.state === "detected" && /Open Beauty Facts/.test(saved.fieldOrigins.ingredients.from), `${label}: the list is stored as Detected, from Open Beauty Facts`);
            ok(saved.fieldOrigins && saved.fieldOrigins.brand && saved.fieldOrigins.brand.state === "typed", `${label}: the brand chosen by the admin is stored as Typed`);
            ok(/Aqua, Dimethicone, Talc, Glycerin, Mica/.test(saved.ingredientsRaw || ""), `${label}: the ingredient text as the source gave it is stored`);
        }

        // ---- a second product with the same barcode (written without zeros) is refused ----
        await page.evaluate(() => document.getElementById("addMenuBtn").click());
        await page.waitForTimeout(200);
        await page.locator(".menu-item, [role=menuitem]").filter({ hasText: "Product" }).first().click();
        await page.waitForSelector("#productForm", { state: "visible" });
        await page.fill("#productNameInput", "Another Lipstick");
        await page.selectOption("#productBrandInput", "loreal");
        await page.selectOption("#productTypeInput", "Lipstick");
        await page.fill("#productIngredientsInput", "Dimethicone, Mica, Talc, Glycerin");
        await page.fill("#productBarcodeInput", "12345678905");
        await page.click("#saveProductBtn");
        await page.waitForTimeout(300);
        ok(/already on "Superstay Matte Ink.*Mini"/.test(await page.textContent("#productForm .form-error")), `${label}: the same barcode without its zeros is a duplicate`);
        await page.click("#productModal [data-close]");
        await page.waitForTimeout(300);

        // ---- the review queue: Send back with a note, then Reject with a reason ----
        await page.evaluate(() => { const s = document.getElementById("reviewFilterSelect"); s.value = "review"; s.dispatchEvent(new Event("change", { bubbles: true })); });
        await page.waitForTimeout(400);
        const sendBack = page.locator('[data-act="send-back-product"][data-id="p5"]').first();
        await sendBack.scrollIntoViewIfNeeded().catch(() => {});
        ok(await sendBack.count() > 0, `${label}: a product waiting for review has Send back`);
        if (await sendBack.count()) {
            await page.evaluate(() => document.querySelector('[data-act="send-back-product"][data-id="p5"]').click());
            await page.waitForSelector("#reviewReasonInput", { timeout: 3000 }).catch(() => {});
            await page.locator('.modal-content:has(#reviewReasonInput) [data-act="ok"]').click();
            ok(/Write the reason/.test(await page.locator(".modal-content:has(#reviewReasonInput) [role=alert]").textContent()), `${label}: Send back needs a note`);
            await page.fill("#reviewReasonInput", "The ingredient list is from the 30 ml size.");
            await page.locator('.modal-content:has(#reviewReasonInput) [data-act="ok"]').click();
            await page.waitForTimeout(500);
            const p5 = await stored(page, "p5");
            ok(p5 && p5.reviewState === "changes" && p5.status === "Pending" && p5.reviewed === false && /30 ml/.test(p5.reviewNote), `${label}: sent back, still Pending, note stored`);
        }
        await page.waitForTimeout(300);
        const row = page.locator('tr.product-row[data-product="p5"]').first();
        if (await row.count()) {
            ok(/Sent back/.test(await row.textContent()), `${label}: the row says Sent back`);
            await page.evaluate(() => document.querySelector('tr.product-row[data-product="p5"] .product-name').click());
            await page.waitForTimeout(400);
        }
        const reject = page.locator('[data-act="reject-product"][data-id="p5"]').first();
        ok(await reject.count() > 0, `${label}: the details of a waiting product offer Reject`);
        if (await reject.count()) {
            await page.evaluate(() => document.querySelector('[data-act="reject-product"][data-id="p5"]').click());
            await page.waitForSelector("#reviewReasonInput", { timeout: 3000 }).catch(() => {});
            await page.fill("#reviewReasonInput", "Not a real product: test entry.");
            await page.locator('.modal-content:has(#reviewReasonInput) [data-act="ok"]').click();
            await page.waitForTimeout(500);
            const p5 = await stored(page, "p5");
            ok(p5 && p5.binned === true && p5.reviewState === "rejected" && /^Rejected: Not a real product/.test(p5.binnedReason), `${label}: rejected into the Recycle bin with the reason`);
        }
        const audit = await page.evaluate(() => Array.from(window.__STORE.keys()).filter((k) => k.startsWith("auditLog/")).length);
        ok(audit >= 4, `${label}: every write left an activity-log record (${audit})`);
        ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), `${label}: the page does not scroll sideways`);

        const real = errors.filter((e) => !/status of 404/.test(e));
        ok(real.length === 0, `${label}: no page errors (${real.join(" | ")})`);
        await ctx.close();
    }
} finally {
    await browser.close();
}
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
