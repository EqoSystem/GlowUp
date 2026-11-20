

import { launch, openPage } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };
const browser = await launch();
try {
    {
        // no saved choice and a DARK system: the site still opens light; clearing the email clears the password
        const { page, settle } = await openPage(browser, "login", { theme: null, width: 1024, height: 800 });
        await settle(500);
        ok(await page.evaluate(() => document.documentElement.dataset.theme) === "light", "the default theme is light (even on a dark system)");
        await page.fill("#username", "someone@example.com");
        await page.fill("#password", "secret123");
        await page.fill("#username", "");
        ok(await page.inputValue("#password") === "", "clearing the email clears the password");
        await page.fill("#username", "a@b.co");
        await page.fill("#password", "x1");
        await page.fill("#username", "a@b.c");
        ok(await page.inputValue("#password") === "x1", "editing the email (not clearing it) keeps the password");
    }
    for (const width of [1440, 390]) {
        const { page, errors, settle } = await openPage(browser, "login", { width, height: 800 });
        await settle(500);
        await page.fill("#username", "someone@example.com");
        await page.fill("#password", "abc12");
        ok(await page.locator(".password-length-cover").count() === 0, `${width}px: no cover while typing`);
        await page.press("#password", "Enter");
        await page.waitForSelector(".password-length-cover", { timeout: 4000 });
        const cover = await page.textContent(".password-length-cover");
        ok(cover.length >= 40, `${width}px: the cover is a long row (${cover.length} dots), not 5`);
        ok(await page.inputValue("#password") === "abc12", `${width}px: the real value stays in the input`);
        const box = await page.locator("#login-form .password-box").boundingBox(), row = await page.locator(".password-length-cover").boundingBox();
        const eye = await page.locator("#passwordEye").boundingBox();
        ok(row.x + row.width <= eye.x + 1, `${width}px: the row of dots ends before the eye (no overlap)`);
        ok(row.width <= box.width + 1, `${width}px: the row stays inside the box`);
        ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), `${width}px: no sideways scroll`);
        await page.fill("#password", "");
        await page.press("#password", "Enter");
        const note = page.locator("#loginMessage");
        ok(await note.isVisible(), `${width}px: the message shows`);
        await page.waitForTimeout(2200);
        ok(!(await note.isVisible()), `${width}px: the message is gone after 1.5 seconds`);
        await page.fill("#password", "abc12");
        await page.press("#password", "Enter");
        await page.waitForSelector(".password-length-cover", { timeout: 4000 });
        await page.type("#password", "x");
        ok(await page.locator(".password-length-cover").count() === 0, `${width}px: typing again removes the cover`);
        await page.close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
