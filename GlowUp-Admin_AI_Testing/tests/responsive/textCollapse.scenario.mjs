// Browser check (9 November 2026): no text may be squeezed into a one-letter-wide column (the "Waiting for review" card list
// once showed a product name letter by letter).   Run: node tests/responsive/textCollapse.scenario.mjs
import { launch, openPage } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };
// an element that holds words (6+ letters), is narrower than 5 letters across, and is at least 4 lines tall
const squeezed = () => {
    const bad = [];
    for (const node of document.querySelectorAll("body *")) {
        const own = Array.from(node.childNodes).filter((n) => n.nodeType === 3).map((n) => n.textContent.trim()).join(" ");
        if (own.length < 6) continue;
        const box = node.getBoundingClientRect();
        if (box.width === 0 || box.height === 0 || getComputedStyle(node).visibility === "hidden") continue;
        const line = parseFloat(getComputedStyle(node).lineHeight) || 18;
        if (box.width < 40 && box.height >= line * 4) bad.push(node.className + " | " + own.slice(0, 30));
    }
    return bad;
};
const browser = await launch();
try {
    for (const width of [1440, 1100, 768, 390]) {
        for (const qs of [false, true]) {
            const { page, errors, settle } = await openPage(browser, "inv", { width, height: 900 });
            await settle(800);
            if (qs) { await page.click("#quickSettingsBtn"); await settle(500); }
            await page.locator('[data-layout="cards"]').click({ timeout: 1500 }).catch(() => {});
            await page.locator('[data-show="review"]').click({ timeout: 1500 }).catch(() => {});
            await settle(500);
            ok(await page.locator(".product-row", { hasText: "Colourbounce" }).count() >= 1 || width < 768, `${width}px: the long-named product without a list is in the view`);
            const bad = await page.evaluate(squeezed);
            ok(bad.length === 0, `${width}px${qs ? " with Quick settings" : ""}, Waiting for review as cards: ${bad.slice(0, 3).join(" ; ")}`);
            await page.context().close();
        }
        for (const key of ["dash", "cur", "bin"]) {
            const { page, settle } = await openPage(browser, key, { width, height: 900 });
            await settle(800);
            await page.locator('[data-layout="cards"]').click({ timeout: 1500 }).catch(() => {});
            await settle(400);
            const bad = await page.evaluate(squeezed);
            ok(bad.length === 0, `${width}px ${key}: ${bad.slice(0, 3).join(" ; ")}`);
            await page.context().close();
        }
    }
} finally { await browser.close(); }
console.log(`checks made: ${checks}`);
if (failed) { console.log(`${failed} FAILED`); process.exit(1); }
console.log("ALL PASSED");
