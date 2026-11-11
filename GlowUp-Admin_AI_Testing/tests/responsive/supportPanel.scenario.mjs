// Browser check: the Dashboard lists support reports sent from the app (10 November 2026).   Run: node tests/responsive/supportPanel.scenario.mjs
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
        // the app's Suggestion kind (11 November 2026) is in the Suggestions panel, not among the support reports
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
        ok(/No new support reports/.test(await page.textContent("[data-panel=support]")), `${width}px: marking it handled empties the list`);
        await page.click('#suggestionList button[data-collection="supportReports"][data-action="reviewed"]');
        await page.waitForTimeout(500);
        ok(!/shade finder/.test(await page.textContent("[data-panel=suggestions]")), `${width}px: marking the app suggestion reviewed takes it off the list`);
        ok(errors.length === 0, `${width}px: no page errors ${errors.join("; ")}`);
        await page.context().close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
