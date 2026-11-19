// Login page lock-out (20 November 2026): 3 wrong passwords for one e-mail lock that e-mail for 5 minutes and recommend
// "Forgot password?". Checks the warning after the 2nd try, the lock and its countdown, that a reload keeps the lock, that another
// e-mail is not locked, that the lock ends after 5 minutes, and that a right password clears the count.
// Run: node tests/responsive/lockout.scenario.mjs
import { launch, openPage } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (cond, what) => { checks++; if (!cond) { failed++; console.log("FAILED: " + what); } };

const browser = await launch();
try {
    const { page, ctx, url, settle } = await openPage(browser, "login", { width: 1280, height: 800, go: false });
    await page.clock.install({ time: new Date("2026-11-20T08:00:00Z") });
    await page.evaluate(() => { }).catch(() => { });
    await page.goto(url);
    await settle(300);
    const msg = () => page.textContent("#loginMessage");
    const tryLogin = async (email, password) => {
        await page.fill("#username", email);
        await page.fill("#password", password);
        await page.evaluate(() => document.querySelector("#login-form").requestSubmit());
        await page.clock.runFor(200);
    };

    await tryLogin("owner@example.com", "wrong1");
    ok(/not correct/i.test(await msg()), "1st wrong password: the usual message");
    ok(!/locked/i.test(await msg()), "1st wrong password: no lock yet");
    await tryLogin("owner@example.com", "wrong2");
    ok(/1 try left/i.test(await msg()) && /Forgot password/i.test(await msg()), "2nd wrong password: warns and recommends Forgot password");
    ok(await page.evaluate(() => document.querySelector("#forgotLink").classList.contains("recommend")), "the Forgot password link is highlighted");
    await tryLogin("owner@example.com", "wrong3");
    ok(/locked for this email for 5:00|locked for this email for 4:5\d/i.test(await msg()), "3rd wrong password: locked for 5 minutes (" + (await msg()) + ")");
    ok(await page.isDisabled("#loginBtn"), "the Log in button is disabled while locked");
    ok(/Locked \d:\d\d/.test(await page.textContent("#loginBtn")), "the button shows the countdown");
    await page.clock.runFor(61000);
    ok(/3:5\d|4:0\d/.test(await msg()), "the countdown goes down (" + (await msg()) + ")");

    // the right password is refused while locked
    await tryLogin("owner@example.com", "right");
    ok(await page.isDisabled("#loginBtn") && /locked/i.test(await msg()), "even the right password waits for the lock");

    // another e-mail is not locked
    await page.fill("#username", "other@example.com");
    await page.fill("#password", "x");
    await page.clock.runFor(1100);
    ok(!(await page.isDisabled("#loginBtn")), "another e-mail is not locked");

    // a reload keeps the lock
    await page.reload();
    await settle(300);
    await page.fill("#username", "owner@example.com");
    await page.fill("#password", "right");
    await page.clock.runFor(1100);
    ok(await page.isDisabled("#loginBtn"), "after a reload the e-mail is still locked");

    // after 5 minutes the lock ends and a right password works and clears the count
    await page.clock.runFor(5 * 60 * 1000);
    ok(!(await page.isDisabled("#loginBtn")), "the lock ends after 5 minutes");
    const left = await page.evaluate(() => { try { return localStorage.getItem("glowup.loginLock.v1"); } catch (e) { return "x"; } });
    ok(!/owner@example\.com/.test(left || ""), "the e-mail itself is not stored");
    await ctx.close();
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
