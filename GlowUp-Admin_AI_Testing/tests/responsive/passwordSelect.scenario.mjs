// Login page: after Log in, a long row of dots covers the password so its length cannot be counted.
// Highlighting the password (Ctrl+A, drag, double-click) must take that cover away, so the highlight shows.
// Run: node tests/responsive/passwordSelect.scenario.mjs
import { launch, openPage } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (cond, what) => { checks++; if (!cond) { failed++; console.log("FAILED: " + what); } };
const covered = (page) => page.evaluate(() => !!document.querySelector(".password-length-cover"));

const browser = await launch();
try {
    for (const way of ["keyboard", "drag"]) {
        const { page, ctx } = await openPage(browser, "login");
        await page.evaluate(() => { window.__SIGN_IN_WAIT = true; });
        await page.fill("#username", "admin@example.com").catch(() => page.fill("input[type=email]", "admin@example.com"));
        await page.fill("#password", "secret123");
        await page.evaluate(() => document.querySelector("form").requestSubmit());
        await page.waitForTimeout(150);
        if (!(await covered(page))) {
            // the mock may have finished the sign-in; put the cover back the way a failed sign-in leaves it
            await page.evaluate(() => document.querySelector("form").requestSubmit());
            await page.waitForTimeout(50);
        }
        ok(await covered(page), `${way}: the length cover is shown after Log in`);
        if (way === "keyboard") {
            await page.focus("#password");
            await page.keyboard.press("Control+A");
        } else {
            const box = await page.locator("#password").boundingBox();
            await page.mouse.move(box.x + 14, box.y + box.height / 2);
            await page.mouse.down();
            await page.mouse.move(box.x + box.width - 60, box.y + box.height / 2, { steps: 6 });
            await page.mouse.up();
        }
        await page.waitForTimeout(100);
        ok(!(await covered(page)), `${way}: highlighting the password removes the cover`);
        ok(await page.evaluate(() => !document.querySelector(".password-box").classList.contains("length-hidden")), `${way}: the real dots are visible again`);
        await ctx.close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
