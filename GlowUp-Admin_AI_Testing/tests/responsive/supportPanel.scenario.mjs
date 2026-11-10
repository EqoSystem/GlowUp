// Browser check: the Dashboard lists support reports sent from the app (10 November 2026).   Run: node tests/responsive/supportPanel.scenario.mjs
import { launch, openPage } from "./harness.mjs";
let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };
const browser = await launch();
try {
    for (const width of [1440, 390]) {
        const { page, errors, settle } = await openPage(browser, "dash", { width, height: 900 });
        await settle(800);
        await page.waitForSelector("#supportList li", { timeout: 6000 });
        const text = await page.textContent("#supportList");
        ok(/log test delay problem/.test(text), `${width}px: the report's message shows`);
        ok(/Bug/.test(text), `${width}px: its kind shows`);
        ok(/Superstay Matte Ink/.test(text), `${width}px: the last scan shows`);
        ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), `${width}px: no sideways scroll`);
        await page.click('#supportList button[data-action="handled"]');
        await page.waitForTimeout(500);
        ok(/No new support reports/.test(await page.textContent("[data-panel=support]")), `${width}px: marking it handled empties the list`);
        ok(errors.length === 0, `${width}px: no page errors ${errors.join("; ")}`);
        await page.context().close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
