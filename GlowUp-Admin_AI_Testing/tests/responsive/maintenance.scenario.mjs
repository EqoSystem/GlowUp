// New22: Maintenance Mode (header pill, Quick settings card, watchdog, advance notice, announcements) and the dashboard's
// report Reply panel / delete. Run: node tests/responsive/maintenance.scenario.mjs   (SHOT_DIR=<folder> saves pictures)
import { launch, openPage, check } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (cond, what) => { checks++; if (!cond) { failed++; console.log("FAILED: " + what); } };
const shot = async (page, name) => { if (process.env.SHOT_DIR) await page.screenshot({ path: `${process.env.SHOT_DIR}/${name}.png` }); };
const docOf = (page, path) => page.evaluate((p) => { const d = window.__STORE.get(p); return d ? JSON.parse(JSON.stringify(d, (k, v) => (v && typeof v.toMillis === "function" ? v.toMillis() : v))) : null; }, path);
const text = (page, sel) => page.evaluate((s) => (document.querySelector(s) || {}).textContent || "", sel);
const wait = (page, ms = 200) => page.waitForTimeout(ms);
const seedReports = () => {
    const now = Math.floor(Date.now() / 1000);
    window.__SEED.supportReports = {
        b1: { uid: "u1abcdefgh", userName: "Mark Azcueta", category: "Bug", message: "The scan button freezes.", status: "new", createdAtUnix: now - 3600 },
        o1: { uid: "u2abcdefgh", userName: "Ana", category: "Other", message: "Hello there", status: "new", createdAtUnix: now - 60 }
    };
};
const localIn = (ms) => { const d = new Date(ms), p = (n) => String(n).padStart(2, "0"); return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`; };

const browser = await launch();
try {
    {
        const { page, ctx, errors, url, settle } = await openPage(browser, "dash", { width: 1440, height: 900, go: false });
        await ctx.addInitScript(seedReports);
        await page.goto(url); await wait(page, 1600); await settle(300);

        // ---- the pill ----
        const pill = await page.locator("#maintenancePill").boundingBox(), bell = await page.locator("#notificationsBtn").boundingBox(), gear = await page.locator("#quickSettingsBtn").boundingBox();
        ok(pill && bell && pill.x + pill.width <= bell.x + 1, "the pill sits left of the bell");
        ok(Math.abs(pill.height - gear.height) < 0.6 && Math.abs((pill.y + pill.height / 2) - (gear.y + gear.height / 2)) < 0.6, "the pill has the gear's height and centre line");
        ok(/System Online/.test(await text(page, "#maintenancePill")) && await page.getAttribute("#maintenancePill", "data-state") === "online", "no document = System Online");
        ok((await check(page)).length === 0, "header has no layout faults with the pill");

        // ---- the pill opens the card ----
        await page.click("#maintenancePill"); await settle(400);
        ok(await page.isVisible("#maintenance-card") && /System Status & Maintenance Mode/.test(await text(page, "#maintenance-card")), "the pill opens Quick settings at the card");
        ok(await page.getAttribute("#mmToggle", "aria-checked") === "false" && await page.isDisabled("#mmNotice"), "off: the switch is off and the notice waits for a schedule");
        await page.fill("#mmBannerTitle", "Scheduled App Update");
        ok(/^20 \/ 80$/.test((await page.locator("#maintenance-card .counter").first().textContent()).trim()), "the title has a counter");

        // ---- switch on now: asks first, then the snapshot drives everything ----
        await page.click("#mmToggle");
        ok(/Start maintenance now\?/.test(await text(page, ".modal-content")), "starting now asks first");
        await page.keyboard.press("Escape"); await wait(page);
        ok(!(await docOf(page, "systemConfig/maintenanceMode")) && await page.getAttribute("#mmToggle", "aria-checked") === "false", "Escape starts nothing and the switch goes back");
        await page.click("#mmToggle");
        await page.click(".modal-content .btn.danger"); await wait(page, 300);
        const on = await docOf(page, "systemConfig/maintenanceMode");
        ok(on && on.isActive === true && on.updatedBy === "admin1" && on.bannerTitle === "Scheduled App Update", `maintenance is on ${JSON.stringify(on && on.isActive)}`);
        ok(await page.getAttribute("#maintenancePill", "data-state") === "active" && /Maintenance Active/.test(await text(page, "#maintenancePill")), "the pill turns red: Maintenance Active");
        ok(await page.isDisabled("#mmStart") && await page.isDisabled("#mmEnd"), "no rescheduling while maintenance is live");
        ok(await page.evaluate(() => [...window.__STORE.entries()].some(([k, v]) => k.startsWith("auditLog/") && (v.items || []).some((i) => i.collection === "systemConfig"))), "the save is in the audit log");
        await shot(page, "maintenance_active");

        // ---- switch off: asks, then online ----
        await page.click("#mmToggle");
        ok(/End maintenance\?/.test(await text(page, ".modal-content")), "ending asks first");
        await page.click(".modal-content .save-btn"); await wait(page, 300);
        ok((await docOf(page, "systemConfig/maintenanceMode")).isActive === false && await page.getAttribute("#maintenancePill", "data-state") === "online", "maintenance is off again");

        // ---- schedule: a start in the future arms it (amber) ----
        await page.fill("#mmStart", localIn(Date.now() + 3600e3));
        await page.fill("#mmEnd", localIn(Date.now() + 3 * 3600e3));
        await page.click("#mmToggle"); await wait(page, 300);
        const armed = await docOf(page, "systemConfig/maintenanceMode");
        ok(armed.isActive === false && armed.scheduledStart > Date.now() && armed.scheduledEnd > armed.scheduledStart, "a future start is saved as a schedule");
        ok(await page.getAttribute("#maintenancePill", "data-state") === "scheduled" && /Update Scheduled/.test(await text(page, "#maintenancePill")), "the pill turns amber: Update Scheduled");
        ok(!(await page.isDisabled("#mmNotice")) && (await text(page, "#mmNotice")) === "Send Advance Notice", "the notice can be sent now");

        // ---- advance notice ----
        await page.click("#mmNotice"); await wait(page, 300);
        const noticed = await docOf(page, "systemConfig/maintenanceMode");
        ok(noticed.notifyUsers === true && noticed.notifiedAt > 0, "the notice sets notifyUsers and notifiedAt");
        ok(await page.evaluate(() => [...window.__STORE.entries()].some(([k, v]) => k.startsWith("announcements/") && v.kind === "maintenance" && /Heads up! GlowUp will be unavailable/.test(v.body))), "the notice is also an announcement for the app's Notifications");
        ok(await page.isDisabled("#mmNotice") && /Notice Sent/.test(await text(page, "#mmNotice")), "the button says Notice Sent and waits");

        // ---- watchdog: the start comes, then the end passes ----
        await page.evaluate(() => { const d = window.__STORE.get("systemConfig/maintenanceMode"); d.scheduledStart = { toMillis: () => Date.now() - 1000, seconds: Math.floor(Date.now() / 1000) - 1 }; window.__NOTIFY(); });
        await wait(page, 400);
        ok((await docOf(page, "systemConfig/maintenanceMode")).isActive === true, "the watchdog switches maintenance on when the start comes");
        await page.evaluate(() => { const d = window.__STORE.get("systemConfig/maintenanceMode"); d.scheduledEnd = { toMillis: () => Date.now() - 1000, seconds: Math.floor(Date.now() / 1000) - 1 }; window.__NOTIFY(); });
        await wait(page, 400);
        const ended = await docOf(page, "systemConfig/maintenanceMode");
        ok(ended.isActive === false && ended.notifyUsers === false && !ended.scheduledStart, "the watchdog ends it when the end passes and resets the notice");
        ok(await page.getAttribute("#maintenancePill", "data-state") === "online", "the pill is green again by itself");

        // ---- announcement ----
        await page.fill("#annTitleBox", "New shades added");
        await page.fill("#annBody", "Twelve new lipstick shades are in the catalogue.");
        await page.click("#annPost");
        ok(/Post to every app user\?/.test(await text(page, ".modal-content")), "posting asks first");
        await page.click(".modal-content .save-btn"); await wait(page, 300);
        ok(await page.evaluate(() => [...window.__STORE.entries()].some(([k, v]) => k.startsWith("announcements/") && v.kind === "update" && v.title === "New shades added")), "the announcement is posted");
        await page.click("#swClose"); await settle(350);

        // ---- the report Reply panel ----
        await page.evaluate(() => document.querySelector('[data-action="reply"][data-id="b1"]').scrollIntoView());
        ok(await page.isVisible('[data-action="reply"][data-id="b1"]'), "each report has Reply");
        await page.click('[data-action="reply"][data-id="b1"]'); await settle(350);
        ok(await page.isVisible(".reply-drawer") && /Reply to Mark Azcueta/.test(await text(page, ".reply-drawer")) && /scan button freezes/.test(await text(page, ".reply-drawer")), "the panel shows who and what");
        const dr = await page.locator(".reply-drawer").boundingBox();
        ok(Math.abs(dr.x + dr.width - 1440) < 2, "the panel sits on the right edge");
        ok(await page.isDisabled(".reply-drawer .save-btn"), "Send waits for text");
        await page.fill("#replyText", "Thanks! Fixed in version 0.76.");
        ok(/^30 \/ 500$/.test((await text(page, ".reply-drawer .counter")).trim()), "the reply has a counter");
        await page.keyboard.press("Escape"); await wait(page);
        ok(/Discard reply\?/.test(await text(page, ".modal-content")), "Escape with text asks Discard reply?");
        await page.click(".modal-content .cancel-btn"); await wait(page);
        ok(await page.isVisible(".reply-drawer") && await page.inputValue("#replyText") === "Thanks! Fixed in version 0.76.", "Keep writing keeps the text");
        await page.click(".reply-drawer .save-btn"); await wait(page, 350);
        const notes = await page.evaluate(() => [...window.__STORE.entries()].filter(([k]) => k.startsWith("users/u1abcdefgh/notifications/")).map(([, v]) => v));
        ok(notes.length === 1 && notes[0].kind === "reply" && notes[0].body === "Thanks! Fixed in version 0.76." && notes[0].read === false, "the reply lands in the user's notifications");
        const b1 = await docOf(page, "supportReports/b1");
        ok(b1.status === "handled" && b1.reply === "Thanks! Fixed in version 0.76.", "the report is marked handled with the reply");
        ok(!(await page.isVisible(".reply-drawer")) && /Reply sent/.test(await text(page, "#toastHost")), "the panel closes and a toast confirms");

        // ---- X deletes (after a question) ----
        await page.click('[data-action="delete"][data-id="o1"]');
        ok(/Delete this report\?/.test(await text(page, ".modal-content")), "X asks first");
        await page.keyboard.press("Escape"); await wait(page);
        ok(!!(await docOf(page, "supportReports/o1")), "Escape keeps the report");
        await page.click('[data-action="delete"][data-id="o1"]');
        await page.click(".modal-content .btn.danger"); await wait(page, 300);
        ok(!(await docOf(page, "supportReports/o1")), "the report is deleted after the confirmed click");
        const real = errors.filter((e) => !/ViewTransition/.test(e));
        ok(real.length === 0, `no page errors ${real.join("; ")}`);
        await ctx.close();
    }

    // ---- phones: the pill is a dot, nothing overflows; the reply panel takes the width ----
    for (const [width, theme] of [[390, "light"], [360, "dark"], [768, "dark"], [1024, "light"]]) {
        const tag = `${width} ${theme}`;
        const { page, ctx, errors, url, settle } = await openPage(browser, "inv", { width, height: 820, theme, go: false, touch: width < 500 });
        await page.goto(url); await wait(page, 1600); await settle(300);
        const faults = await check(page);
        ok(faults.length === 0, `${tag}: header faults ${faults.join(" | ")}`);
        const p = await page.locator("#maintenancePill").boundingBox();
        ok(p && p.x >= 0 && p.x + p.width <= width, `${tag}: the pill is on screen`);
        await page.click("#maintenancePill"); await settle(400);
        const card = await page.locator("#maintenance-card").boundingBox();
        ok(card && card.x >= -0.5 && card.x + card.width <= width + 0.5, `${tag}: the card fits`);
        await shot(page, `maintenance_card_${width}_${theme}`);
        const real = errors.filter((e) => !/ViewTransition/.test(e));
        ok(real.length === 0, `${tag}: no page errors ${real.join("; ")}`);
        await ctx.close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
