// Login page: "Forgot password?" opens a dialog that sends Firebase's password-reset e-mail.
// Checks: the link and its 44 px tap area, Escape / backdrop / Cancel close it, a wrong e-mail is caught,
// an unknown e-mail gets the same answer as a known one, a network fault keeps the dialog open for a retry,
// Escape does nothing while sending, and "Back to log in" returns focus to the form with the e-mail filled in.
// Run: node tests/responsive/resetPassword.scenario.mjs
import { launch, openPage } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (cond, what) => { checks++; if (!cond) { failed++; console.log("FAILED: " + what); } };
const dialogOpen = (page) => page.evaluate(() => !!document.querySelector(".reset-dialog"));

const browser = await launch();
try {
    for (const [width, theme] of [[1440, "light"], [360, "dark"]]) {
        const { page, ctx } = await openPage(browser, "login", { width, height: 800, theme });
        const tag = `${width} ${theme}`;
        const link = page.locator("#forgotLink");
        ok(await link.isVisible(), `${tag}: the Forgot password link is shown`);
        const box = await link.boundingBox();
        ok(box && box.height >= 43.5, `${tag}: the link's tap area is at least 44 px tall (${box && box.height})`);
        const eye = await page.locator("#passwordEye").boundingBox();
        ok(box && eye && box.y >= eye.y + eye.height - 0.5, `${tag}: the link does not cover the eye button`);
        ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), `${tag}: no sideways scroll`);
        ok(await page.evaluate(() => !document.querySelector("#forgotLink").hasAttribute("title")), `${tag}: no tooltip on the link`);

        // Escape, backdrop and Cancel close it; focus goes back to the link
        await link.click();
        ok(await dialogOpen(page), `${tag}: the link opens the dialog`);
        await page.keyboard.press("Escape");
        ok(!(await dialogOpen(page)), `${tag}: Escape closes the dialog`);
        ok(await page.evaluate(() => document.activeElement && document.activeElement.id === "forgotLink"), `${tag}: focus returns to the link`);
        await link.click();
        await page.mouse.click(5, 795);
        ok(!(await dialogOpen(page)), `${tag}: a click beside the box closes it`);
        await link.click();
        await page.click(".reset-dialog .cancel-btn");
        ok(!(await dialogOpen(page)), `${tag}: Cancel closes it`);

        // the typed login e-mail is taken over; a wrong one is caught before sending
        await page.fill("#username", "owner@example.com");
        await link.click();
        ok(await page.inputValue("#resetEmail") === "owner@example.com", `${tag}: the typed e-mail is filled in`);
        await page.fill("#resetEmail", "not-an-email");
        await page.click(".reset-dialog .save-btn");
        ok(await page.locator(".reset-dialog .form-error").isVisible(), `${tag}: a wrong e-mail shows a problem line`);
        ok(await page.evaluate(() => !(window.__RESET_CALLS || []).length), `${tag}: nothing is sent for a wrong e-mail`);

        // a network fault keeps the dialog open with a retry
        await page.evaluate(() => { window.__RESET_ERROR = "auth/network-request-failed"; });
        await page.fill("#resetEmail", "owner@example.com");
        await page.click(".reset-dialog .save-btn");
        await page.waitForTimeout(80);
        ok(/internet/i.test(await page.textContent(".reset-dialog .form-error")), `${tag}: a network fault says so`);
        ok(await page.isEnabled(".reset-dialog .save-btn") && (await page.textContent(".reset-dialog .save-btn")) === "Send link", `${tag}: Send link can be pressed again`);

        // an unknown e-mail gets the same neutral answer
        await page.evaluate(() => { window.__RESET_ERROR = "auth/user-not-found"; });
        await page.click(".reset-dialog .save-btn");
        await page.waitForTimeout(80);
        const said = await page.textContent(".reset-dialog .reset-sent").catch(() => "");
        ok(/If owner@example\.com belongs to an admin account/.test(said), `${tag}: an unknown e-mail gets the neutral answer`);
        const calls = await page.evaluate(() => window.__RESET_CALLS);
        ok(calls.length === 2 && /\/_Index\/index\.html$/.test(calls[1].url || ""), `${tag}: the link's Continue leads back to the login page`);
        await page.click(".reset-dialog .reset-actions .save-btn");
        ok(!(await dialogOpen(page)), `${tag}: Back to log in closes the dialog`);
        ok(await page.evaluate(() => document.activeElement && document.activeElement.id === "password"), `${tag}: focus goes to the password box`);

        // Escape and backdrop do nothing while sending; Cancel still leaves
        await page.evaluate(() => { window.__RESET_ERROR = null; window.__RESET_HANG = true; });
        await link.click();
        await page.click(".reset-dialog .save-btn");
        await page.waitForTimeout(50);
        ok((await page.textContent(".reset-dialog .save-btn")) === "Sending...", `${tag}: the button says Sending...`);
        await page.keyboard.press("Escape");
        await page.mouse.click(5, 795);
        ok(await dialogOpen(page), `${tag}: Escape and a click beside it do not close it while sending`);
        await page.click(".reset-dialog .cancel-btn");
        ok(!(await dialogOpen(page)), `${tag}: Cancel leaves even while sending`);
        await ctx.close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
