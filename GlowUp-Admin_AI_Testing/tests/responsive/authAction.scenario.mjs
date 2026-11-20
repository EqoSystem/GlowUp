// 22 Nov 2026: the links Firebase e-mails, handled on the log-in page (_Index/authAction.js):
// reset password = Email Verified! -> Set a New Password (rules live, Save locked until they pass) -> Password Set Successfully! -> log in;
// an expired link says so and offers a new one; the code leaves the address bar at once.
// Run: node tests/responsive/authAction.scenario.mjs
import { launch, openPage } from "./harness.mjs";
let checks = 0, failed = 0;
const ok = (cond, what) => { checks++; if (!cond) { failed++; console.log("FAILED: " + what); } };
const browser = await launch();
try {
    for (const [width, height, theme] of [[1280, 860, "light"], [390, 844, "dark"]]) {
        const tag = `${width} ${theme}`;
        const { page, ctx, url, errors, check } = await openPage(browser, "login", { width, height, theme, go: false, touch: width < 500 });
        await ctx.addInitScript(() => { window.__OOB = { abc: "admin@example.test" }; });
        await page.goto(url.replace(/(\?|$)/, "?mode=resetPassword&oobCode=abc&")); await page.waitForTimeout(900);
        const h1 = () => page.textContent("#actionView h1");
        ok(await h1() === "Email Verified!" && !(await page.isVisible("#loginView")), `${tag}: a good link shows Email Verified!`);
        ok(!/oobCode/.test(page.url()), `${tag}: the code leaves the address bar`);
        await page.click("#actionView .login-btn"); await page.waitForTimeout(250);
        ok(await h1() === "Set a New Password" && /admin@example\.test/.test(await page.textContent("#actionView")), `${tag}: Set a New Password names the account`);
        await page.fill("#newPassword", "short"); await page.fill("#confirmPassword", "short");
        ok(await page.isDisabled("#actionView .login-btn"), `${tag}: Save stays off until every rule passes`);
        await page.fill("#newPassword", "Newpass1!"); await page.fill("#confirmPassword", "Newpass2!");
        ok(await page.isDisabled("#actionView .login-btn") && /do not match/.test(await page.textContent("#actionView .pw-match")), `${tag}: two different passwords keep Save off`);
        await page.fill("#confirmPassword", "Newpass1!");
        ok(!(await page.isDisabled("#actionView .login-btn")), `${tag}: Save opens when the rules pass and both match`);
        const box = await page.locator("#actionView").boundingBox();
        ok(box && box.x >= 0 && box.x + box.width <= width + 0.5, `${tag}: the panel fits the window`);
        await page.click("#actionView .login-btn"); await page.waitForTimeout(250);
        ok(await h1() === "Password Set Successfully!" && await page.evaluate(() => window.__RESET_TO) === "Newpass1!", `${tag}: saved with the code from the link`);
        await page.click("#actionView .login-btn"); await page.waitForTimeout(250);
        ok(await page.isVisible("#loginView") && !(await page.$("#actionView")) && await page.inputValue("#username") === "admin@example.test", `${tag}: Continue to Login shows the log-in card with the e-mail`);
        // the same link again: used up
        await page.goto(url.replace(/(\?|$)/, "?mode=resetPassword&oobCode=used&")); await page.waitForTimeout(900);
        ok(/does not work any more/.test(await h1()), `${tag}: a used link says so`);
        await page.click("#actionView .login-btn"); await page.waitForTimeout(300);
        ok(await page.isVisible(".reset-dialog") && await page.isVisible("#loginView"), `${tag}: Send a new link opens the reset dialog`);
        ok(errors.length === 0, `${tag}: no page errors ${errors.join("; ")}`);
        await ctx.close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
