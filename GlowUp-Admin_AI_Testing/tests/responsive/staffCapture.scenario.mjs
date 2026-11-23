// Mobile staff capture -> website (23 Nov 2026): a submission written by the phone appears on the open inventory page by
// itself (live listener), is in Waiting for review, says how it was captured, and its photos open for the reviewer.
import { launch, openPage } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };
const PIXEL = "/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////wAALCAABAAEBAREA/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AKp//2Q==";

const browser = await launch();
try {
    for (const [label, width, height] of [["desktop", 1280, 860], ["phone", 390, 800]]) {
        const { page, ctx, errors, settle } = await openPage(browser, "inv", { width, height, touch: label === "phone" });
        await settle(700);
        // the phone's write, as another device would make it
        await page.evaluate((jpeg) => {
            window.__STORE.set("products/cap_admin1_abc_1", { name: "Phone Captured Lip Tint", brandId: "maybelline", brandName: "Maybelline New York", cosmeticType: "Lip Tint",
                ingredients: ["Aqua", "Mica"], shades: [], sources: [], status: "Pending", reviewed: false, source: "staff-capture", addedBy: "admin1", addedAtUnix: Math.floor(Date.now() / 1000), scans: 0,
                captureMethod: "Barcode", evidenceCount: 1, barcode: "0012345678905", ingredientsRaw: "INGREDIENTS: Aqua, Mica",
                fieldOrigins: { name: { state: "detected", from: "Open Beauty Facts", method: "barcode", atUnix: 1 } } });
            window.__STORE.set("captureEvidence/cap_admin1_abc_1", { by: "admin1", atUnix: 1, productId: "cap_admin1_abc_1", photos: [{ role: "Front", jpeg }] });
            window.__NOTIFY();
        }, PIXEL);
        await page.waitForTimeout(1200);
        await page.evaluate(() => { const s = document.getElementById("reviewFilterSelect"); s.value = "review"; s.dispatchEvent(new Event("change", { bubbles: true })); });
        await page.waitForTimeout(500);
        const row = page.locator('tr.product-row[data-product="cap_admin1_abc_1"]');
        ok(await row.count() === 1, `${label}: the phone's submission appears without reloading the page, in Waiting for review`);
        if (await row.count()) {
            await page.evaluate(() => document.querySelector('tr.product-row[data-product="cap_admin1_abc_1"] .product-name').click());
            await page.waitForTimeout(500);
            const details = await page.textContent('tr.detail-row[data-for="cap_admin1_abc_1"]');
            ok(/Submitted from the mobile app/.test(details) && /Captured by barcode scan/.test(details), `${label}: the details say it came from the mobile app, by barcode scan`);
            ok(/Not checked/.test(details), `${label}: the values the phone detected are listed as not checked`);
            await page.evaluate(() => document.querySelector('[data-act="capture-photos"][data-id="cap_admin1_abc_1"]').click());
            await page.waitForSelector(".capture-photos img", { timeout: 4000 }).catch(() => {});
            const photos = await page.evaluate(() => Array.from(document.querySelectorAll(".capture-photos img")).map((i) => i.alt));
            ok(photos.length === 1 && /Front photo/.test(photos[0]), `${label}: the reviewer sees the capture photo (${photos.join(", ")})`);
        }
        ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), `${label}: no sideways scroll`);
        ok(errors.length === 0, `${label}: no page errors (${errors.join(" | ")})`);
        await ctx.close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
