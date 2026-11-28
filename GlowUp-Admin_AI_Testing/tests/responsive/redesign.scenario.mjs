// Redesign plan (27 Nov 2026): Search and import panel, Review queue (imports, app scans, user reports), Product review, User report,
// and the Curator's result counts. Runs against the mock Firebase of this rig: `node redesign.scenario.mjs`.
import { launch, openPage } from "./harness.mjs";
let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };
const browser = await launch();
try {
    for (const [width, height] of [[1440, 900], [390, 844]]) {
        const { page, errors, settle } = await openPage(browser, "inv", { width, height });
        await settle(1200);
        ok(await page.isVisible("#siPanel"), `${width}px: the Search and import panel shows`);
        ok((await page.textContent("#siPanel")).includes("Everything found is saved as Pending"), `${width}px: it says found products are Pending`);
        ok(await page.locator("[data-si-tab], #siPhone").count() === 0 && await page.isVisible("#siMany"), `${width}px: one search box and Add barcode list`);
        ok(await page.isHidden("#batchLookupBtn"), `${width}px: the separate Batch Lookup button is gone from the table`);
        await page.waitForSelector("#rqBody tr[data-rq]", { timeout: 8000 });
        const queue = await page.textContent("#rqBody");
        ok(/User report: new product/.test(queue) && /Sample Brand, Sample Lip Tint/.test(queue), `${width}px: the user report waits in the Review queue`);
        ok(/No list found/.test(queue), `${width}px: a product without a list says so`);
        ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), `${width}px: no sideways scroll`);

        await page.hover("#siMany");
        ok(/paste many barcodes/.test(await page.textContent("#siPanel .btn-hint")), `${width}px: pointing at a button explains it in the help line`);
        await page.click("#siMany");
        await page.waitForSelector(".batch-window", { timeout: 4000 });
        await page.goBack(); await page.waitForTimeout(300);
        ok(await page.locator(".batch-window").count() === 0, `${width}px: browser Back closes the pop-up`);

        // the user report
        await page.click('#rqBody [data-rq-open^="report:"]');
        await page.waitForSelector(".review-window", { timeout: 4000 });
        const report = await page.textContent(".review-window");
        ok(/4800000000000/.test(report) && /Aqua, Dimethicone, Parfum/.test(report) && /Not verified/.test(report), `${width}px: the report shows barcode, ingredient text and "Not verified"`);
        ok(await page.locator(".review-window >> text=Search and import this").count() === 1, `${width}px: the report offers Search and import this`);
        await page.keyboard.press("Escape");
        await page.waitForTimeout(200);
        ok(await page.locator(".review-window").count() === 0, `${width}px: Escape returns to the queue`);

        // a product review
        await page.click('#rqBody [data-rq-open^="product:"]');
        await page.waitForSelector(".review-window .rv-table", { timeout: 4000 });
        const review = await page.textContent(".review-window");
        ok(/All \d+/.test(review) && /Flagged \d+/.test(review) && /No rule matched \d+/.test(review) && /Not identified \d+/.test(review), `${width}px: the review filters by the three results`);
        ok(/Approve all/.test(review) && /Save for later/.test(review) && /Send to Recycle Bin/.test(review), `${width}px: the review has its three actions`);
        ok(!/\bSafe\b/.test(await page.textContent(".review-window .rv-table")), `${width}px: no ingredient is called safe`);
        await page.click('.review-window [data-rv="later"]');
        await page.waitForTimeout(400);
        ok(await page.locator(".review-window").count() === 0, `${width}px: Save for later returns to the queue`);
        ok(errors.length === 0, `${width}px: no page errors (${errors.join(" | ")})`);
        await page.close();
    }

    for (const width of [390]) {
        const dash = await openPage(browser, "dash", { width, height: 844 });
        await dash.settle(1200);
        ok(await dash.page.locator('[data-panel="numbers"] .assist').count() === 0, "dashboard: no strip under the numbers");
        ok(dash.errors.length === 0, `dashboard: no page errors (${dash.errors.join(" | ")})`);
        await dash.page.close();
    }
    const { page, errors, settle } = await openPage(browser, "cur", { width: 1440, height: 900 });
    await settle(1200);
    const stats = await page.textContent("#curatorStats");
    ok(/Flagged/.test(stats) && /No rule matched/.test(stats) && /Not identified/.test(stats), "curator: counts by result");
    ok(await page.evaluate(() => !document.getElementById("conditionsLabel") && document.getElementById("conditionsFit").parentElement.hidden), "curator: the chronic skin condition group is gone from the form");
    await page.click('#curatorStats [data-risk="r:flagged"]');
    await page.waitForTimeout(300);
    ok(await page.evaluate(() => document.getElementById("ingredientRiskFilter").value === "r:flagged"), "curator: a result count filters the list");
    ok(await page.locator("#chooseIngredientFileBtn").count() === 0 && await page.isVisible("#curSiRun"), "curator: Search and import replaces Import a file");
    await page.click("#curSiMany");
    await page.fill("#curSiNames", "Niacinamide\n1,2-Hexanediol");
    ok(/2 names/.test(await page.textContent(".pop-window")), "curator: the list window counts names, keeping 1,2-Hexanediol whole");
    await page.keyboard.press("Escape");
    await page.waitForTimeout(200);
    ok(/Discard changes/.test(await page.textContent("body")), "curator: leaving with names typed asks first");
    await page.keyboard.press("Escape");
    await page.waitForTimeout(200);
    ok(await page.locator("#curSiNames").count() === 1, "curator: Escape on the question keeps the window");
    ok(errors.length === 0, `curator: no page errors (${errors.join(" | ")})`);
} finally { await browser.close(); }
console.log(`redesign: ${checks} checks, ${failed} failed`);
process.exit(failed ? 1 : 0);
