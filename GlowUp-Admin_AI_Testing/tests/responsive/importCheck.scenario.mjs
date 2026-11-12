

import { launch, openPage } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };
const FILE = ["=== BRAND ===", "BRAND_NAME: Checkbrand", "=== PRODUCT ===", "BRAND_NAME: Checkbrand", "PRODUCT_NAME: Velvet Tint", "COSMETIC_TYPE: Lip Tint", "INGREDIENTS: Aqua | Mica",
    "=== PRODUCT ===", "BRAND_NAME: Checkbrand", "PRODUCT_NAME: Skip Me Balm", "COSMETIC_TYPE: Lipstick", "INGREDIENTS: Cera Alba"].join("\n");
const browser = await launch();
try {
    for (const width of [1440, 390]) {
        const { page, errors, settle } = await openPage(browser, "inv", { width, height: 900 });
        await settle(800);
        const dropBefore = await page.locator("#inventoryDropBox").boundingBox();
        await page.setInputFiles("#inventoryFileInput", { name: "check.txt", mimeType: "text/plain", buffer: Buffer.from(FILE) });
        await page.waitForSelector(".import-cover.ready", { timeout: 8000 });
        const cover = await page.locator(".import-cover").boundingBox();
        const dropNow = await page.locator("#inventoryDropBox").boundingBox();
        ok(Math.abs(cover.width - dropNow.width) < 2 && Math.abs(cover.height - dropNow.height) < 2, `${width}px: the cover is exactly the drop box, nothing else is covered`);
        ok(Math.abs(dropBefore.height - dropNow.height) < 2, `${width}px: the drop box did not change size`);
        ok(await page.locator(".toast", { hasText: "to check" }).count() >= 1, `${width}px: a toast says the file is read`);
        ok(/2 products and 1 brand/.test(await page.textContent(".import-cover-text")), `${width}px: the cover says what was found`);

        await page.click(".import-cover .btn.primary");
        await page.waitForSelector(".import-check");
        ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), `${width}px: no sideways scroll with the check window open`);
        ok(await page.evaluate(() => { const b = document.querySelector(".import-check").getBoundingClientRect(); return b.left >= -1 && b.right <= window.innerWidth + 1; }), `${width}px: the check window fits the screen`);

        await page.keyboard.press("Escape");
        ok(await page.locator(".import-check").count() === 0 && await page.locator(".import-cover.ready").count() === 1, `${width}px: Escape closes the window and keeps the records waiting`);
        await page.click(".import-cover .btn.primary");
        await page.waitForSelector(".import-check");

        const name = page.locator(".ic-row").nth(0).locator("input[type=text]").nth(1);
        await name.fill("Velvet Tint Edited");
        await page.locator(".ic-row").nth(1).locator(".ic-row-head input[type=checkbox]").uncheck();
        ok(/Save 1 as Pending|Save 2 as Pending/.test(await page.textContent(".import-check [data-save]")), `${width}px: the Save button counts the ticked records`);

        await page.locator(".ic-tabs .seg", { hasText: "Text from the file" }).click();
        ok((await page.inputValue(".ic-text")).includes("Velvet Tint"), `${width}px: the text read from the file is shown`);
        await page.locator(".ic-tabs .seg").first().click();
        await page.click(".import-check [data-save]");
        await page.waitForSelector(".import-check", { state: "detached", timeout: 8000 });
        const status = await page.textContent("#inventoryImportStatus");
        ok(/Saved 1 product/.test(status), `${width}px: only the ticked product was saved: ${status.slice(0, 120)}`);
        ok(await page.locator(".import-cover").count() === 0, `${width}px: the cover is gone after saving`);
        ok(errors.length === 0, `${width}px: no page errors: ` + errors.slice(0, 3).join(" | "));
        await page.context().close();
    }
} finally { await browser.close(); }
console.log(`checks made: ${checks}`);
if (failed) { console.log(`${failed} FAILED`); process.exit(1); }
console.log("ALL PASSED");
