// Admin sign-up (20 November 2026): login page "Sign Up" -> "Create Admin Account" -> live "Check Your Email",
// and the live "Admin requests" view in Quick settings where an admin approves or declines.
// Checks layout at desktop and phone widths in both themes, the navigation rules (Back, Escape, "Discard changes?"),
// what is written to the database, and that the cards and the admin list follow changes in real time.
// Run: node tests/responsive/signup.scenario.mjs   (SHOT_DIR=<folder> also saves pictures)
import { launch, openPage, check } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (cond, what) => { checks++; if (!cond) { failed++; console.log("FAILED: " + what); } };
const shot = async (page, name) => { if (process.env.SHOT_DIR) await page.screenshot({ path: `${process.env.SHOT_DIR}/${name}.png`, fullPage: true }); };
const visible = (page, id) => page.evaluate((id) => { const el = document.getElementById(id); return !!el && !el.hidden && el.getBoundingClientRect().height > 0; }, id);
const storeDoc = (page, path) => page.evaluate((path) => { const d = window.__STORE.get(path); return d ? JSON.parse(JSON.stringify(d)) : null; }, path);
const PASS = "Glow#2026x";

async function fill(page, { name = "Maria Santos", email = "maria@example.com", password = PASS, confirm = PASS, agree = true } = {}) {
    await page.fill("#signupName", name);
    await page.fill("#signupEmail", email);
    await page.fill("#signupPassword", password);
    await page.fill("#signupConfirm", confirm);
    if (agree !== await page.isChecked("#signupAgree")) await page.click("#signupAgree");
}

const browser = await launch();
try {
    // ---------- layout of the three cards ----------
    for (const [width, height, theme] of [[1440, 900, "light"], [768, 1024, "dark"], [390, 844, "light"], [360, 740, "dark"]]) {
        const tag = `${width} ${theme}`;
        const { page, ctx, errors, settle } = await openPage(browser, "login", { width, height, theme, touch: width < 500 });
        const up = await page.locator("#signupLink").boundingBox(), forgot = await page.locator("#forgotLink").boundingBox();
        ok(up && up.height >= 43.5, `${tag}: Sign Up has a 44 px tap area`);
        ok(up && forgot && Math.abs(up.y - forgot.y) < 2 && up.x + up.width <= forgot.x, `${tag}: Sign Up sits left of Forgot password? on one row`);
        ok(await page.evaluate(() => !document.querySelector("#signupLink").hasAttribute("title")), `${tag}: no tooltip on Sign Up`);
        await page.click("#signupLink");
        await settle(400);
        ok(await visible(page, "signupView") && !(await visible(page, "loginView")), `${tag}: Sign Up shows the Create Admin Account card only`);
        ok(await page.evaluate(() => location.hash === "#signup"), `${tag}: the address says #signup`);
        ok(await page.evaluate(() => document.activeElement.id === "signupName"), `${tag}: focus starts on Full Name`);
        ok(await page.evaluate(() => ["signupName", "signupEmail", "signupPassword", "signupConfirm"].every((id) => { const input = document.getElementById(id); return input.maxLength > 0 && input.closest(".field")?.querySelector(".counter"); })), `${tag}: every sign-up text field has a maxlength and live counter`);
        ok(await page.evaluate(() => document.querySelectorAll("#signupView .counter").length === 4), `${tag}: sign-up counters stay aligned under their fields`);
        const faults = await check(page);
        ok(faults.length === 0, `${tag}: sign-up card layout faults: ${faults.join(" | ")}`);
        const back = await page.locator("#signupBack").boundingBox(), title = await page.locator("#signupTitle").boundingBox();
        ok(back && title && back.y + back.height <= title.y + 2 || back.x + back.width <= title.x, `${tag}: the back arrow does not cover the title`);
        await fill(page);
        ok(await page.evaluate(() => document.querySelectorAll("#pwRules .pw-rule.met").length === 5), `${tag}: all five password rules tick`);
        ok(await page.isEnabled("#signupBtn"), `${tag}: Request Admin Access is ready once everything is filled`);
        await shot(page, `signup_form_${width}_${theme}`);
        await page.click("#signupBtn");
        await page.waitForTimeout(250);
        await settle(300);
        ok(await visible(page, "checkView"), `${tag}: the request opens Check Your Email`);
        const faults2 = await check(page);
        ok(faults2.length === 0, `${tag}: check card layout faults: ${faults2.join(" | ")}`);
        await shot(page, `signup_check_${width}_${theme}`);
        ok(errors.length === 0, `${tag}: no page errors ${errors.join("; ")}`);
        await ctx.close();
    }

    // ---------- the whole flow, live ----------
    {
        const { page, ctx, errors, settle } = await openPage(browser, "login", { width: 1280, height: 860 });
        ok(await page.isDisabled("#signupLink") === false, "flow: Sign Up can be pressed");
        await page.click("#signupLink");
        ok(await page.isDisabled("#signupBtn"), "flow: the button waits until the form is filled");

        // a clean form leaves at once: Escape and the back arrow
        await page.keyboard.press("Escape");
        await settle(200);
        ok(await visible(page, "loginView") && await page.evaluate(() => location.hash === ""), "flow: Escape on an empty form goes back to log-in");
        await page.click("#signupLink");
        await page.click("#signupBack");
        await settle(200);
        ok(await visible(page, "loginView"), "flow: the back arrow goes back to log-in");
        ok(await page.evaluate(() => document.activeElement.id === "username"), "flow: focus returns to the email box");

        // a filled form asks "Discard changes?"; Escape means keep editing
        await page.click("#signupLink");
        await page.fill("#signupName", "Maria");
        await page.click("#signupBack");
        ok(await page.evaluate(() => /Discard changes\?/.test(document.querySelector(".modal-content h3")?.textContent || "")), "flow: a filled form asks Discard changes?");
        ok(await page.evaluate(() => [...document.querySelectorAll(".modal-content button")].map((b) => b.textContent).join("|") === "Keep editing|Discard"), "flow: the choices are Keep editing / Discard");
        await page.keyboard.press("Escape");
        await settle(150);
        ok(await visible(page, "signupView") && await page.inputValue("#signupName") === "Maria", "flow: Escape keeps editing, the name is still there");
        await page.goBack();
        await page.waitForTimeout(200);
        ok(await page.evaluate(() => !!document.querySelector(".modal-content")), "flow: the browser's Back also asks first");
        await page.click(".modal-content .btn.danger");
        await settle(300);
        ok(await visible(page, "loginView"), "flow: Discard goes back to log-in");
        await page.click("#signupLink");
        ok(await page.inputValue("#signupName") === "", "flow: a discarded form starts empty");

        // problems are named before anything is sent
        await fill(page, { password: "weakpass", confirm: "weakpass" });
        await page.click("#signupBtn");
        ok(/does not meet every rule/.test(await page.textContent("#signupMessage")), "flow: a weak password is explained");
        ok(await page.getAttribute("#signupPassword", "aria-invalid") === "true", "flow: the password box is marked");
        ok(await page.evaluate(() => !window.__CREATE_CALLS), "flow: nothing is sent for a weak password");
        await fill(page, { confirm: "Glow#2026y" });
        ok(await page.evaluate(() => !document.getElementById("pwMatch").hidden && !document.getElementById("pwMatch").classList.contains("met")), "flow: different passwords show 'do not match yet'");

        // the Privacy Policy opens and gives focus back
        await page.click('#signupView [data-opens="privacyDialog"]');
        ok(await page.evaluate(() => document.getElementById("privacyDialog").open), "flow: Privacy Policy opens");
        await page.keyboard.press("Escape");
        ok(await page.evaluate(() => !document.getElementById("privacyDialog").open && document.activeElement.dataset.opens === "privacyDialog"), "flow: Escape closes it and focus returns");

        // send
        await fill(page);
        await page.click("#signupBtn");
        await page.waitForTimeout(300);
        const req = await storeDoc(page, "adminRequests/new1");
        ok(req && req.status === "pending" && req.emailVerified === false && req.email === "maria@example.com" && req.name === "Maria Santos", "flow: a pending request is written for the new account " + JSON.stringify(req));
        ok(!(await storeDoc(page, "admins/new1")), "flow: signing up does NOT make an admin");
        const verify = await page.evaluate(() => window.__VERIFY_CALLS || []);
        ok(verify.length === 1 && /\/_Index\/index\.html$/.test(verify[0].url || ""), "flow: one confirmation e-mail, leading back to the log-in page");
        ok(await page.evaluate(() => location.hash === "#check-email" && document.getElementById("checkEmail").textContent === "maria@example.com"), "flow: Check Your Email names the address");
        ok(await page.isDisabled("#resendBtn") && /Send again in \d+ s/.test(await page.textContent("#resendBtn")), "flow: Send the link again waits a minute");
        ok(await page.evaluate(() => location.pathname.endsWith("index.html")), "flow: the person is NOT sent to the dashboard");

        // live: the confirmation is noticed by itself
        await page.evaluate(() => { window.__VERIFIED = true; });
        await page.waitForTimeout(4600);
        ok(await page.getAttribute("#checkView", "data-state") === "confirmed", "flow: the card notices the confirmed e-mail by itself");
        ok((await storeDoc(page, "adminRequests/new1")).emailVerified === true, "flow: the request is marked confirmed for the admins");
        // live: an admin's approval shows at once
        await page.evaluate(() => { const d = window.__STORE.get("adminRequests/new1"); window.__STORE.set("adminRequests/new1", Object.assign({}, d, { status: "approved" })); window.__NOTIFY(); });
        await page.waitForTimeout(100);
        ok(await page.getAttribute("#checkView", "data-state") === "approved" && await visible(page, "checkLogin"), "flow: an approval shows at once with a Log in button");
        await page.click("#checkLogin");
        await settle(250);
        ok(await visible(page, "loginView") && await page.inputValue("#username") === "maria@example.com", "flow: Log in returns to the form with the e-mail filled in");
        ok(await page.evaluate(() => document.activeElement.id === "password"), "flow: focus goes to the password");
        await page.goBack();
        await page.waitForTimeout(150);
        ok(!(await visible(page, "checkView")) && !(await visible(page, "signupView")) || await page.inputValue("#signupName") === "", "flow: Back never reopens the sent form with its data");
        ok(errors.length === 0, `flow: no page errors ${errors.join("; ")}`);
        await ctx.close();
    }

    // ---------- an e-mail that already has an account (an app user) ----------
    {
        const { page, ctx, errors, url } = await openPage(browser, "login", { width: 1280, height: 860, go: false });
        await ctx.addInitScript(() => { window.__USERS = { "app@example.com": { password: "Glow#2026x", uid: "app1", verified: true } }; });
        await page.goto(url); await page.waitForTimeout(900);
        await page.click("#signupLink");
        await fill(page, { email: "app@example.com", password: "Wrong#2026x", confirm: "Wrong#2026x" });
        await page.click("#signupBtn");
        await page.waitForTimeout(300);
        ok(/already has a GlowUp account/.test(await page.textContent("#signupMessage")), "existing: a wrong password for an existing account is explained");
        ok(!(await storeDoc(page, "adminRequests/app1")), "existing: nothing is written");
        await fill(page, { email: "app@example.com" });
        await page.click("#signupBtn");
        await page.waitForTimeout(300);
        const req = await storeDoc(page, "adminRequests/app1");
        ok(req && req.status === "pending" && req.emailVerified === true, "existing: the right password sends a request marked confirmed");
        ok(await page.getAttribute("#checkView", "data-state") === "confirmed" && await page.evaluate(() => !(window.__VERIFY_CALLS || []).length), "existing: no confirmation e-mail for a confirmed account");
        // live decline
        await page.evaluate(() => { window.__STORE.delete("adminRequests/app1"); window.__NOTIFY(); });
        await page.waitForTimeout(100);
        ok(await page.getAttribute("#checkView", "data-state") === "declined", "existing: a decline shows at once");
        await page.click("#checkToLogin");
        await page.waitForTimeout(200);
        ok(await visible(page, "loginView"), "existing: Back to Login works after a decline");
        ok(errors.length === 0, `existing: no page errors ${errors.join("; ")}`);
        await ctx.close();
    }

    // ---------- logging in while the request waits ----------
    {
        const { page, ctx, url } = await openPage(browser, "login", { width: 1280, height: 860, go: false });
        await ctx.addInitScript(() => {
            window.__USERS = { "wait@example.com": { password: "Glow#2026x", uid: "w1", verified: false } };
            window.__SEED.adminRequests = { w1: { name: "Wait Ing", email: "wait@example.com", status: "pending", emailVerified: false } };
        });
        await page.goto(url); await page.waitForTimeout(900);
        await page.fill("#username", "wait@example.com");
        await page.fill("#password", "Glow#2026x");
        await page.click("#loginBtn");
        await page.waitForTimeout(300);
        ok(/Confirm your email first/.test(await page.textContent("#loginMessage")), "waiting: log-in before confirming says to open the link");
        await page.evaluate(() => { window.__VERIFIED = true; });
        await page.fill("#password", "Glow#2026x");
        await page.click("#loginBtn");
        await page.waitForTimeout(300);
        ok(/waiting for an admin to approve/.test(await page.textContent("#loginMessage")), "waiting: after confirming it says the request waits");
        ok((await storeDoc(page, "adminRequests/w1")).emailVerified === true, "waiting: the log-in marks the request confirmed");
        ok(await page.evaluate(() => location.pathname.endsWith("index.html")), "waiting: no way into the dashboard");
        await ctx.close();
    }

    // ---------- admins: Quick settings > Admin requests, live ----------
    for (const [width, theme] of [[1440, "light"], [360, "dark"]]) {
        const tag = `admin ${width} ${theme}`;
        const { page, ctx, errors, url, settle } = await openPage(browser, "dash", { width, height: 860, theme, go: false, touch: width < 500 });
        await ctx.addInitScript(() => {
            const t = Date.now();
            window.__SEED.adminRequests = {
                r1: { name: "Ana Reyes", email: "ana@example.com", status: "pending", emailVerified: true, createdAt: { __ts: t - 3600e3 } },
                r2: { name: "Ben Cruz", email: "ben@example.com", status: "pending", emailVerified: false, createdAt: { __ts: t - 60e3 } },
                r3: { name: "Old One", email: "old@example.com", status: "approved", emailVerified: true, createdAt: { __ts: t - 86400e3 } }
            };
        });
        await page.goto(url); await page.waitForTimeout(1500);
        await page.click("#quickSettingsBtn");
        await settle(400);
        const item = page.locator(".settings-window .menu-item", { hasText: "Admin requests" });
        ok(await item.count() === 1, `${tag}: Quick settings lists Admin requests`);
        ok((await item.locator(".menu-badge").textContent().catch(() => "")) === "2", `${tag}: the badge counts the 2 pending requests`);
        await item.click();
        await settle(300);
        ok(await page.locator("#swReqView .req-row").count() === 2, `${tag}: two rows, the approved one is not listed`);
        const rows = await page.evaluate(() => [...document.querySelectorAll("#swReqView .req-row")].map((r) => ({ name: r.querySelector(".req-name").textContent, approve: !r.querySelector(".btn.primary").disabled })));
        ok(rows[0].name === "Ana Reyes" && rows[0].approve && rows[1].name === "Ben Cruz" && !rows[1].approve, `${tag}: oldest first; Approve only for a confirmed e-mail ${JSON.stringify(rows)}`);
        ok(await page.locator("#swReqView .req-why").count() === 1, `${tag}: the reason Approve is closed is written out`);
        const faults = await check(page);
        ok(faults.length === 0, `${tag}: layout faults: ${faults.join(" | ")}`);
        await shot(page, `admin_requests_${width}_${theme}`);

        // live: a new request arrives while the view is open
        await page.evaluate(() => { window.__STORE.set("adminRequests/r4", { name: "Cara Lim", email: "cara@example.com", status: "pending", emailVerified: false }); window.__NOTIFY(); });
        await page.waitForTimeout(150);
        ok(await page.locator("#swReqView .req-row").count() === 3, `${tag}: a new request appears by itself`);
        ok(await page.evaluate(() => /New admin request from Cara Lim/.test(document.getElementById("toastHost")?.textContent || "")), `${tag}: a notice says a new request came in`);

        // approve: asks, then one batch adds the admin and closes the request
        await page.locator("#swReqView .req-row", { hasText: "Ana Reyes" }).locator(".btn.primary").click();
        ok(await page.evaluate(() => /Give admin access\?/.test(document.querySelector(".modal-content")?.textContent || "")), `${tag}: Approve asks first`);
        await page.keyboard.press("Escape");
        await page.waitForTimeout(100);
        ok(!(await storeDoc(page, "admins/r1")), `${tag}: Escape on the question approves nothing`);
        await page.locator("#swReqView .req-row", { hasText: "Ana Reyes" }).locator(".btn.primary").click();
        await page.click(".modal-content .save-btn");
        await page.waitForTimeout(250);
        const admin = await storeDoc(page, "admins/r1"), closed = await storeDoc(page, "adminRequests/r1");
        ok(admin && admin.email === "ana@example.com" && admin.approvedBy === "admin1", `${tag}: the admin is added ${JSON.stringify(admin)}`);
        ok(closed && closed.status === "approved" && closed.decidedBy === "admin1", `${tag}: the request is marked approved`);
        ok(await page.evaluate(() => [...window.__STORE.keys()].some((k) => k.startsWith("auditLog/"))), `${tag}: the approval is in the audit log`);
        ok(await page.locator("#swReqView .req-row").count() === 2, `${tag}: the approved row leaves the list by itself`);

        // decline: destructive, Escape cancels
        await page.locator("#swReqView .req-row", { hasText: "Ben Cruz" }).locator(".btn.danger").click();
        await page.keyboard.press("Escape");
        await page.waitForTimeout(100);
        ok(!!(await storeDoc(page, "adminRequests/r2")), `${tag}: Escape on Decline deletes nothing`);
        await page.locator("#swReqView .req-row", { hasText: "Ben Cruz" }).locator(".btn.danger").click();
        await page.click(".modal-content .btn.danger");
        await page.waitForTimeout(250);
        ok(!(await storeDoc(page, "adminRequests/r2")), `${tag}: Decline deletes the request`);
        await page.locator("#swReqView .req-row", { hasText: "Cara Lim" }).locator(".btn.danger").click();
        await page.click(".modal-content .btn.danger");
        await page.waitForTimeout(250);
        ok(await page.locator("#swReqView .req-empty").count() === 1, `${tag}: an empty list says No requests waiting`);
        ok(errors.length === 0, `${tag}: no page errors ${errors.join("; ")}`);
        await ctx.close();
    }

    // ---------- admins: the list cannot be read ----------
    {
        const { page, ctx, url, settle } = await openPage(browser, "dash", { width: 1280, height: 860, go: false });
        await ctx.addInitScript(() => { window.__SNAP_ERROR = "permission-denied"; });
        await page.goto(url); await page.waitForTimeout(1500);
        await page.click("#quickSettingsBtn");
        await settle(300);
        await page.locator(".settings-window .menu-item", { hasText: "Admin requests" }).click();
        ok(await page.locator("#swReqView .req-error").count() === 1 && /rules may not be published/.test(await page.textContent("#swReqView")), "error: a refused read says so");
        await page.evaluate(() => { window.__SNAP_ERROR = null; });
        await page.click("#swReqView .req-error .btn");
        await page.waitForTimeout(200);
        ok(await page.locator("#swReqView .req-error").count() === 0, "error: Retry loads the list again");
        await ctx.close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
