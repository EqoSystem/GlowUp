

import { launch, openPage } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };
const browser = await launch();
try {
    const { page, errors, settle } = await openPage(browser, "cur");
    await settle(800);
    const before = await page.locator(".ingredient-card").count();
    const firstName = await page.locator('.ingredient-card [data-act="bin"]').first().evaluate((b) => b.closest(".ingredient-card").dataset.id || b.dataset.id || "");
    const binButton = page.locator('.ingredient-card [data-act="bin"]').first();
    await page.locator(".ingredient-card").first().hover();
    await binButton.click({ force: true });
    const asked = page.locator(".modal-content", { hasText: "Recycle bin" });
    ok(await asked.count() >= 1, "moving an ingredient to the Recycle bin asks first");
    await page.keyboard.press("Escape");
    await settle(400);
    ok(await page.locator(".ingredient-card").count() === before, "Escape on that question keeps the ingredient");
    await page.locator(".ingredient-card").first().hover();
    await binButton.click({ force: true });
    await page.locator(".modal-content button", { hasText: "Move to Recycle bin" }).click();
    await settle(600);
    ok(await page.locator(`.ingredient-card[data-id="${firstName}"]`).count() === 0 && await page.locator(".toast", { hasText: "Recycle bin" }).count() >= 1, "confirming moves it (" + firstName + ")");

    const inv = await openPage(browser, "inv");
    await inv.settle(800);
    const MIXED = "=== BRAND ===\nBRAND_NAME: Glow\n=== PRODUCT ===\nBRAND_NAME: Glow\nPRODUCT_NAME: Tint\nCOSMETIC_TYPE: Lipstick\n=== INGREDIENT ===\nNAME: Aqua\nDESCRIPTION: Water.";
    await inv.page.setInputFiles("#inventoryFileInput", { name: "mixed.txt", mimeType: "text/plain", buffer: Buffer.from(MIXED) });
    await inv.page.waitForFunction(() => /cannot be extracted here/.test(document.getElementById("inventoryImportStatus").textContent), null, { timeout: 8000 }).catch(() => {});
    const text = await inv.page.textContent("#inventoryImportStatus");
    ok(/nothing was imported/.test(text) && /1 ingredient record/.test(text) && /Ingredient Curator/.test(text), "a mixed file is refused with the reason: " + text.slice(0, 160));

    await inv.page.click(".bin-chip");
    await inv.settle(400);
    ok(await inv.page.locator(".shelf-back").count() === 1, "the Recycle bin view shows a Back button");
    await inv.page.click(".shelf-back");
    await inv.settle(400);
    ok(await inv.page.locator(".shelf-back").count() === 0 && await inv.page.locator(".bin-chip.on").count() === 0, "Back returns to the whole list");
    await inv.page.click(".bin-chip");
    await inv.settle(400);
    await inv.page.goBack();
    await inv.settle(500);
    ok(await inv.page.locator(".bin-chip.on").count() === 0 && /adminBrandInventory/.test(inv.page.url()), "the browser's Back leaves the bin and stays on the page");
    ok(errors.concat(inv.errors).filter((e) => !/shared default layout/i.test(e)).length === 0, "no page errors: " + errors.concat(inv.errors).slice(0, 3).join(" | "));
} finally { await browser.close(); }
console.log(`checks made: ${checks}`);
if (failed) { console.log(`${failed} FAILED`); process.exit(1); }
console.log("ALL PASSED");
