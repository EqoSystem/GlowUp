

import { launch, openPage } from "./harness.mjs";
let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };
const browser = await launch();
try {
    for (const [width, height] of [[1440, 900], [390, 844], [360, 800]]) {
        const { page, errors, settle } = await openPage(browser, "login", { width, height });
        await settle(600);
        ok(await page.$(".login-card .icon-box") === null, `${width}px: no icon above the title`);
        const m = await page.evaluate(() => {
            const card = document.querySelector(".login-card").getBoundingClientRect();
            const h1 = document.querySelector(".login-card h1").getBoundingClientRect();
            const main = document.querySelector(".login-main").getBoundingClientRect();
            const pad = parseFloat(getComputedStyle(document.querySelector(".login-card")).paddingTop);
            return { gapTop: h1.top - card.top, pad, above: card.top - main.top, below: main.bottom - card.bottom, scroll: document.documentElement.scrollWidth <= innerWidth + 1 };
        });
        ok(Math.abs(m.gapTop - m.pad) <= 2, `${width}px: the title starts at the card's top padding (${m.gapTop.toFixed(1)} vs ${m.pad})`);
        ok(m.below < 0 || Math.abs(m.above - m.below) <= 24, `${width}px: the card is centred (${m.above.toFixed(0)} above, ${m.below.toFixed(0)} below)`);
        ok(m.scroll, `${width}px: no sideways scroll`);
        if (process.env.SHOT_DIR) await page.screenshot({ path: `${process.env.SHOT_DIR}/login_${width}.png` });
        ok(errors.length === 0, `${width}px: no page errors ${errors.join("; ")}`);
        await page.context().close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
