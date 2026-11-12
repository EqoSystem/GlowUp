
import { launch, openPage } from "./harness.mjs";
let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };
const browser = await launch();
try {
    for (const [width, height] of [[1440, 900], [390, 844], [360, 800]]) {
        const { page, errors, settle } = await openPage(browser, "dash", { width, height });
        await settle(800);
        await page.waitForSelector("#supportList li", { timeout: 6000 });
        const text = await page.textContent("#supportList");
        ok(/log test delay problem/.test(text), `${width}px: the report's message shows`);
        ok(/Bug/.test(text), `${width}px: its kind shows`);
        ok(/Superstay Matte Ink/.test(text), `${width}px: the last scan shows`);
        ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), `${width}px: no sideways scroll`);

        ok(!/shade finder/.test(text), `${width}px: an app suggestion is not listed as a support report`);
        await page.waitForSelector("#suggestionList li", { timeout: 6000 });
        const ideas = await page.textContent("#suggestionList");
        ok(/Please add a shade finder for foundation/.test(ideas), `${width}px: the app suggestion's text shows in Suggestions`);
        ok(/App suggestion/.test(ideas), `${width}px: it is marked as an app suggestion`);
        ok(/\d{4}/.test(ideas), `${width}px: its date and time show`);
        ok(/Fit Me Matte/.test(ideas), `${width}px: its last scan shows`);
        if (width < 500) await page.locator("[data-panel=suggestions]").screenshot({ path: `${process.env.SHOT_DIR || "."}/dashboard_suggestions_${width}.png` });
        await page.click('#supportList button[data-action="handled"]');
        await page.waitForTimeout(500);
        ok(/No new bug reports/.test(await page.textContent("[data-panel=support]")), `${width}px: marking it handled empties the Bug list`);

        ok(/the scan said harmful/.test(await page.textContent("#supportWrongList")), `${width}px: the wrong scan result is in its own panel`);
        ok(/Mark Azcueta/.test(await page.textContent("#supportWrongList")) && /account user3abc/.test(await page.textContent("#supportWrongList")), `${width}px: the report shows the sender's name WITH the account id (the name is self-given)`);
        ok(/user1abc/.test(await page.textContent("#supportList")), `${width}px: an older report without a name still shows its account`);
        ok(/how do I change my email/.test(await page.textContent("#supportOtherList")), `${width}px: the other report is in its own panel`);
        ok(!/how do I change/.test(await page.textContent("#supportList")), `${width}px: kinds are not mixed`);
        ok(await page.evaluate(() => ["support", "supportWrong", "supportOther"].every((id) => document.querySelector(`[data-panel=${id}] .panel-fold-btn`) && document.getElementById(id + "ClearAll"))), `${width}px: each panel has its fold arrow and Clear all`);
        await page.click('#supportWrongList button[data-action="handled"]'); await page.waitForTimeout(400);
        ok(/No new wrong scan results/.test(await page.textContent("[data-panel=supportWrong]")), `${width}px: the Wrong scan results panel empties too`);
        await page.click('#suggestionList button[data-collection="supportReports"][data-action="reviewed"]');
        await page.waitForTimeout(500);
        ok(!/shade finder/.test(await page.textContent("[data-panel=suggestions]")), `${width}px: marking the app suggestion reviewed takes it off the list`);

        await page.reload(); await settle(800);
        await page.waitForSelector("#suggestionList li", { timeout: 6000 });
        const order = await page.evaluate(() => { const head = document.querySelector("[data-panel=suggestions] .panel-head"); const b = [...head.querySelectorAll("button")].map((x) => x.id || x.className); return b.join(","); });
        ok(/suggestionClearAll,[^,]*panel-fold-btn/.test(order), `${width}px: Clear all stands just left of the fold arrow (${order})`);
        await page.click("#suggestionClearAll");
        await page.waitForSelector(".modal-actions .btn.danger", { timeout: 4000 });
        await page.click(".modal-actions .btn.danger");
        await page.waitForTimeout(600);
        ok(/No suggestions waiting/.test(await page.textContent("[data-panel=suggestions]")), `${width}px: Clear all empties Suggestions`);
        ok(await page.$eval("#suggestionClearAll", (b) => b.hidden), `${width}px: Clear all hides when the list is empty`);
        ok(errors.length === 0, `${width}px: no page errors ${errors.join("; ")}`);
        await page.context().close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
