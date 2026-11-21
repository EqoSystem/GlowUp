// Header bell + admin profile (_Shared/headerAccount.js): alignment with the gear, the live badge, the grouped dropdown,
// mark one / mark all as read, one dropdown at a time, outside click / Escape, the profile menu (Edit Profile, My activity log), and that the live
// listener is released on sign-out and when the page is left.
// Run: node tests/responsive/headerAccount.scenario.mjs   (SHOT_DIR=<folder> also saves pictures)
import { launch, openPage, check } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (cond, what) => { checks++; if (!cond) { failed++; console.log("FAILED: " + what); } };
const shot = async (page, name) => { if (process.env.SHOT_DIR) await page.screenshot({ path: `${process.env.SHOT_DIR}/${name}.png` }); };
const doc = (page, path) => page.evaluate((path) => { const d = window.__STORE.get(path); return d ? JSON.parse(JSON.stringify(d)) : null; }, path);
const badge = (page) => page.evaluate(() => { const b = document.querySelector(".hdr-badge"); return b && !b.hidden ? b.textContent : ""; });
const popOpen = (page, sel) => page.evaluate((sel) => !!document.querySelector(sel), sel);

const seedReports = () => {
    const now = Math.floor(Date.now() / 1000);
    window.__SEED.supportReports = {
        b1: { uid: "u1abcdefgh", userName: "Mark Azcueta", category: "Bug", message: "The scan button freezes after I take a photo of a lipstick and nothing happens until I restart the app completely.", status: "new", createdAtUnix: now - 2 * 3600 },
        b2: { uid: "u2abcdefgh", userName: "", category: "Bug", message: "Crash on Settings", status: "new", createdAtUnix: now - 5 * 60 },
        b3: { uid: "u3", userName: "Already Read", category: "Bug", message: "Seen before", status: "new", read: true, createdAtUnix: now - 86400 },
        s1: { uid: "u4abcdefgh", userName: "Ana", category: "Suggestion", message: "Please add a shade finder", status: "new", createdAtUnix: now - 3 * 86400 },
        w1: { uid: "u5abcdefgh", userName: "Ben", category: "Wrong scan result", message: "It said harmful but the list is fine", status: "new", createdAtUnix: now - 30 },
        o1: { uid: "u6abcdefgh", userName: "Cara", category: "Other", message: "Hello there", status: "new", createdAtUnix: now - 7200 },
        h1: { uid: "u7", userName: "Handled", category: "Bug", message: "Closed on the dashboard", status: "handled", createdAtUnix: now - 100 }
    };
};

const browser = await launch();
try {
    for (const [width, theme, touch] of [[1440, "light", false], [1024, "dark", false], [390, "light", true], [360, "dark", true]]) {
        const tag = `${width} ${theme}`;
        const { page, ctx, errors, url, settle } = await openPage(browser, "dash", { width, height: 860, theme, go: false, touch });
        await ctx.addInitScript(seedReports);
        await page.goto(url); await page.waitForTimeout(1500);
        await settle(300);

        // ---- placement and size: bell, gear, profile on one line, same box as the gear ----
        const box = (sel) => page.locator(sel).boundingBox();
        const bell = await box("#notificationsBtn"), gear = await box("#quickSettingsBtn"), me = await box("#accountBtn");
        ok(bell && gear && me, `${tag}: bell, gear and profile are all in the header`);
        ok(bell.x + bell.width <= gear.x + 1 && gear.x + gear.width <= me.x + 1, `${tag}: order is bell, gear, profile (left to right)`);
        ok([bell, me].every((b) => Math.abs(b.width - gear.width) < 0.6 && Math.abs(b.height - gear.height) < 0.6), `${tag}: same size as the gear (${gear.width}x${gear.height})`);
        ok([bell, me].every((b) => Math.abs((b.y + b.height / 2) - (gear.y + gear.height / 2)) < 0.6), `${tag}: same vertical centre as the gear`);
        const right = await page.evaluate(() => document.documentElement.clientWidth);
        ok(me.x + me.width <= right + 0.5, `${tag}: the profile is the rightmost and fully on screen`);
        ok(await page.evaluate(() => ["notificationsBtn", "accountBtn"].every((id) => !document.getElementById(id).hasAttribute("title") && !document.getElementById(id).hasAttribute("data-tip"))), `${tag}: no tooltip on the new icons`);
        const gearStyle = await page.evaluate(() => { const g = getComputedStyle(document.getElementById("quickSettingsBtn")), b = getComputedStyle(document.getElementById("notificationsBtn")); return [g.height === b.height, g.borderRadius === b.borderRadius, g.backgroundColor === b.backgroundColor, g.color === b.color]; });
        ok(gearStyle.every(Boolean), `${tag}: the bell has the gear's radius, background and colour ${JSON.stringify(gearStyle)}`);
        const av = await page.evaluate(() => { const a = document.getElementById("accountBtn"); return { text: a.textContent, round: getComputedStyle(a).borderRadius, bg: getComputedStyle(a).backgroundColor, pink: getComputedStyle(document.documentElement).getPropertyValue("--accent").trim() }; });
        ok(av.text === "A", `${tag}: the avatar shows the first letter of the e-mail (${av.text})`);
        ok(av.bg !== "rgba(0, 0, 0, 0)" && parseFloat(av.round) >= 17, `${tag}: the avatar is a filled circle ${av.round} ${av.bg}`);
        const faults = await check(page);
        ok(faults.length === 0, `${tag}: header layout faults: ${faults.join(" | ")}`);

        // ---- the live badge ----
        ok(await badge(page) === "5", `${tag}: the badge counts unread new reports (5: read and handled ones are left out) got "${await badge(page)}"`);
        ok(/5 new/.test(await page.getAttribute("#notificationsBtn", "aria-label")), `${tag}: the bell's label says how many`);

        // ---- the dropdown ----
        await page.click("#notificationsBtn");
        await settle(300);
        const view = await page.evaluate(() => ({
            groups: [...document.querySelectorAll(".np-pop .np-group-title")].map((h) => h.textContent),
            kinds: [...new Set([...document.querySelectorAll(".np-pop .np-kind")].map((n) => n.textContent))].sort(),
            unread: document.querySelectorAll(".np-pop .np-item:not(.is-read)").length, read: document.querySelectorAll(".np-pop .np-item.is-read").length,
            icons: document.querySelectorAll(".np-pop .np-item svg").length, foot: document.querySelector(".np-pop .np-foot")?.textContent || "",
            count: document.querySelector(".np-pop .np-unread")?.textContent || ""
        }));
        ok(view.groups.length >= 1 && view.groups.every((g) => g === "Today" || g === "Earlier") && view.groups.includes("Earlier"), `${tag}: grouped into Today and Earlier ${JSON.stringify(view.groups)}`);
        ok(JSON.stringify(view.kinds) === JSON.stringify(["Bug report", "Other report", "Suggestion", "Wrong scan result"]), `${tag}: each row names its kind in words ${JSON.stringify(view.kinds)}`);
        ok(view.unread === 5 && view.read === 1 && view.count === "5", `${tag}: pink dots on the 5 unread, the read one muted, the count in the header ${JSON.stringify(view)}`);
        ok(view.icons === 0 && view.foot === "View all reports", `${tag}: no icon per row; a link to all reports at the bottom`);
        const first = await page.evaluate(() => { const i = [...document.querySelectorAll(".np-item")].find((n) => /scan button freezes/.test(n.textContent)); return { msg: i.querySelector(".np-msg").textContent, from: i.querySelector(".np-from").textContent, when: i.querySelector(".np-when").textContent }; });
        ok(first.msg.length <= 90 && first.msg.endsWith("…"), `${tag}: a long message is cut to a preview (${first.msg.length})`);
        ok(first.from === "Mark Azcueta", `${tag}: the sender is the name, or the account when there is no name (${first.from})`);
        ok(/^(just now|\d+ min ago|\d+ h ago)$/.test(first.when), `${tag}: relative time (${first.when})`);
        const times = await page.evaluate(() => [...document.querySelectorAll(".np-item .np-when")].map((n) => n.textContent));
        ok(times.includes("2 h ago") && times.includes("3 d ago") && times.includes("just now"), `${tag}: relative times are right ${JSON.stringify(times)}`);
        ok((await page.getAttribute("#notificationsBtn", "aria-expanded")) === "true", `${tag}: aria-expanded is true while open`);
        const pop = await page.locator(".np-pop").boundingBox();
        ok(pop.x >= -0.5 && pop.x + pop.width <= right + 0.5 && pop.y + pop.height <= 860 + 0.5, `${tag}: the dropdown is inside the window ${JSON.stringify(pop)} vw ${right}`);
        await shot(page, `bell_${width}_${theme}`);

        // live: a report arrives while it is open
        await page.evaluate(() => { window.__STORE.set("supportReports/n9", { uid: "u9abcdefgh", userName: "Dee", category: "Bug", message: "Brand new report", status: "new", createdAtUnix: Math.floor(Date.now() / 1000) }); window.__NOTIFY(); });
        await page.waitForTimeout(120);
        ok(await badge(page) === "6", `${tag}: the badge goes up by itself`);
        ok(await page.locator(".np-pop .np-item").count() === 7 && /Brand new report/.test(await page.textContent(".np-pop")), `${tag}: the open panel shows it by itself`);

        // a click marks it read and opens the reply window in the middle of the page
        await page.locator(".np-item", { hasText: "Please add a shade finder" }).click();
        await page.waitForTimeout(300);
        const done = await doc(page, "supportReports/s1");
        ok(done && done.read === true && done.status === "new", `${tag}: the report is marked read:true (its status is left alone) ${JSON.stringify(done && done.read)}`);
        ok(await badge(page) === "5", `${tag}: the badge goes down by one`);
        const rw = await page.evaluate(() => { const d = document.querySelector(".reply-drawer"); if (!d) return null; const r = d.getBoundingClientRect(); return { title: d.querySelector("h3").textContent, cx: r.left + r.width / 2, cy: r.top + r.height / 2, w: innerWidth, h: innerHeight }; });
        ok(rw && rw.title === "Reply to Ana" && Math.abs(rw.cx - rw.w / 2) < 2 && Math.abs(rw.cy - rw.h / 2) < 2, `${tag}: the reply window opens, centred ${JSON.stringify(rw)}`);
        await page.keyboard.press("Escape"); await page.waitForTimeout(150);
        ok(!(await page.$(".reply-drawer")), `${tag}: Escape closes an empty reply`);

        // a failed write puts the dot back
        await page.click("#notificationsBtn"); await settle(250);
        await page.evaluate(() => { window.__FAIL_WRITES = "unavailable"; });
        await page.locator(".np-item", { hasText: "Hello there" }).click();
        await page.waitForTimeout(250);
        ok(await badge(page) === "5" && (await doc(page, "supportReports/o1")).read !== true, `${tag}: a failed write puts the report back`);
        await page.evaluate(() => { window.__FAIL_WRITES = null; });
        await page.keyboard.press("Escape"); await page.waitForTimeout(150);

        // mark all
        await page.click("#notificationsBtn"); await settle(250);
        await page.click(".np-all");
        await page.waitForTimeout(250);
        ok(await badge(page) === "", `${tag}: Mark all as read clears the badge`);
        const left = await page.evaluate(() => [...window.__STORE.entries()].filter(([k, v]) => k.startsWith("supportReports/") && v.status === "new" && v.read !== true).map(([k]) => k));
        ok(left.length === 0, `${tag}: every new report now has read:true ${JSON.stringify(left)}`);
        ok((await doc(page, "supportReports/h1")).read === undefined, `${tag}: a handled report is not touched`);
        ok(await page.locator(".np-pop .np-item:not(.is-read)").count() === 0 && await page.isDisabled(".np-all"), `${tag}: every row is muted and the button is off`);
        await page.keyboard.press("Escape"); await page.waitForTimeout(100);

        // ---- one dropdown at a time ----
        await page.click("#accountBtn");
        await settle(250);
        ok(await popOpen(page, ".ap-pop") && !(await popOpen(page, ".np-pop")), `${tag}: opening the profile closes the bell panel`);
        ok(await page.getAttribute("#notificationsBtn", "aria-expanded") === "false", `${tag}: the bell reports closed`);
        await page.click("#notificationsBtn");
        await settle(250);
        ok(await popOpen(page, ".np-pop") && !(await popOpen(page, ".ap-pop")), `${tag}: opening the bell closes the profile menu`);
        ok(await page.locator(".hdr-pop").count() === 1, `${tag}: never more than one dropdown`);
        await page.evaluate(() => document.body.dispatchEvent(new PointerEvent("pointerdown", { bubbles: true })));
        await page.waitForTimeout(100);
        ok(!(await popOpen(page, ".hdr-pop")), `${tag}: a click outside closes it`);
        await page.click("#notificationsBtn"); await settle(150);
        await page.keyboard.press("Escape");
        ok(!(await popOpen(page, ".hdr-pop")) && await page.evaluate(() => document.activeElement.id === "notificationsBtn"), `${tag}: Escape closes it and focus returns to the bell`);
        await page.click("#notificationsBtn"); await settle(150);
        await page.click("#notificationsBtn");
        ok(!(await popOpen(page, ".hdr-pop")), `${tag}: the bell toggles`);
        // the gear still works, with a dropdown open
        await page.click("#accountBtn"); await settle(150);
        await page.click("#quickSettingsBtn"); await settle(350);
        ok(!(await popOpen(page, ".hdr-pop")) && await page.getAttribute("#quickSettingsBtn", "aria-expanded") === "true", `${tag}: the gear still opens Quick settings and closes the dropdown`);
        await page.click("#swClose"); await settle(350);

        // ---- profile menu ----
        await page.click("#accountBtn"); await settle(250);
        const who = await page.evaluate(() => ({ name: document.querySelector(".ap-name").textContent, email: document.querySelector(".ap-email").textContent, items: [...document.querySelectorAll(".ap-pop .menu-item")].map((b) => b.textContent) }));
        ok(who.email === "admin@example.test" && who.name === "Admin", `${tag}: name and e-mail are shown ${JSON.stringify(who)}`);
        ok(JSON.stringify(who.items) === JSON.stringify(["Edit Profile", "My activity log"]), `${tag}: Edit Profile and My activity log, no Settings or Logout ${JSON.stringify(who.items)}`);
        ok(await page.evaluate(() => /Last login: Nov 21, 2026 · \d{1,2}:10 (AM|PM)/.test(document.querySelector(".ap-last")?.textContent || "")), `${tag}: the last sign-in time is shown`);
        await shot(page, `profile_${width}_${theme}`);

        // ---- My activity log: only this admin's records ----
        await page.evaluate(() => {
            const now = Math.floor(Date.now() / 1000);
            window.__STORE.set("auditLog/m1", { uid: "admin1", email: "admin@example.test", atUnix: now - 60, page: "adminBrandInventory.html", count: 1, items: [{ collection: "products", docId: "p1", action: "update", name: "Mine" }] });
            window.__STORE.set("auditLog/o1", { uid: "other", email: "other@example.test", atUnix: now - 30, page: "adminBrandInventory.html", count: 1, items: [{ collection: "products", docId: "p2", action: "update", name: "Theirs" }] });
        });
        await page.click(".ap-pop .menu-item:nth-of-type(2)"); await settle(400);
        const log = await page.evaluate(() => { const box = [...document.querySelectorAll(".modal-content")].pop(); return { title: box.querySelector("h3").textContent, text: box.textContent }; });
        ok(log.title === "My activity log" && /admin@example\.test/.test(log.text) && !/other@example\.test/.test(log.text), `${tag}: My activity log shows only my own records ${log.title}`);
        await page.keyboard.press("Escape"); await settle(200);

        // ---- Edit Profile: wrong current password, then a good change; Discard asks first ----
        await page.click("#accountBtn"); await settle(200);
        await page.click(".ap-pop .menu-item:first-of-type"); await settle(300);
        ok(await page.isVisible("#epName") && await page.inputValue("#epEmail") === "admin@example.test", `${tag}: Edit Profile opens with the name and the e-mail`);
        ok(await page.evaluate(() => ["epName", "epEmail", "epCurrent", "epNext", "epConfirm"].every((id) => { const input = document.getElementById(id); return input && input.maxLength > 0 && !input.closest(".field")?.querySelector(".counter"); })), `${tag}: Edit Profile text fields have limits without counters`);
        await page.fill("#epName", "Grace Admin");
        await page.fill("#epCurrent", "nope"); await page.fill("#epNext", "NewPassw0rd!"); await page.fill("#epConfirm", "NewPassw0rd!");
        await page.click(".ep-dialog .save-btn"); await settle(300);
        ok(/current password is not right/.test(await page.evaluate(() => document.querySelector(".ep-dialog .form-error").textContent)) && await page.getAttribute("#epCurrent", "aria-invalid") === "true", `${tag}: a wrong current password is named at its box`);
        await page.fill("#epCurrent", "Passw0rd!");
        await page.click(".ep-dialog .save-btn"); await settle(400);
        const saved = await page.evaluate(() => ({ open: !!document.querySelector(".ep-dialog"), changed: window.__PASSWORD_CHANGED || 0 }));
        ok(!saved.open && saved.changed === 1, `${tag}: the password is changed and the window closes ${JSON.stringify(saved)}`);
        await page.click("#accountBtn"); await settle(200);
        ok(await page.evaluate(() => document.querySelector(".ap-name").textContent) === "Grace Admin" && await page.evaluate(() => document.querySelector("#accountBtn .hdr-letter")?.textContent) === "G", `${tag}: the new name shows in the menu and the avatar`);
        await page.click(".ap-pop .menu-item:first-of-type"); await settle(300);
        await page.fill("#epName", "Someone Else");
        await page.keyboard.press("Escape"); await settle(200);
        ok(await page.evaluate(() => [...document.querySelectorAll(".modal-content h3")].some((h) => /Discard changes\?/.test(h.textContent))), `${tag}: leaving with changes asks Discard changes?`);
        await page.locator(".modal-overlay").last().locator(".btn.danger").click(); await settle(200);
        ok(!(await page.isVisible(".ep-dialog")), `${tag}: Discard closes Edit Profile`);
        await page.evaluate(() => { window.__PASSWORD = "Passw0rd!"; window.__PASSWORD_CHANGED = 0; });

        // the listener is released: on leaving the page, and on sign-out
        const base = await page.evaluate(() => window.__LIVE());
        await page.evaluate(() => window.dispatchEvent(new Event("pagehide")));
        const afterHide = await page.evaluate(() => window.__LIVE());
        ok(afterHide <= base - 1, `${tag}: leaving the page releases the bell's listener (and the page's other live listeners) (${base} -> ${afterHide})`);
        await page.evaluate(() => window.dispatchEvent(Object.assign(new Event("pageshow"), { persisted: true })));
        ok(await page.evaluate(() => window.__LIVE()) === base, `${tag}: coming back from the cache listens again`);
        const beforeOut = await page.evaluate(() => window.__LIVE());
        await page.evaluate(async () => { const m = await import("https://www.gstatic.com/firebasejs/10.12.0/firebase-auth.js"); window.__LEAVE = true; await m.signOut(); });
        await page.waitForTimeout(100).catch(() => { });
        ok(true, `${tag}: (sign-out ran)`);
        const after = await page.evaluate(() => (window.__LIVE ? window.__LIVE() : -1)).catch(() => -1);
        ok(after === -1 || after < beforeOut, `${tag}: signing out releases the listener (${beforeOut} -> ${after})`);
        const real = errors.filter((e) => !/Execution context|navigat|ViewTransition|Mark as read failed/i.test(e));
        ok(real.length === 0, `${tag}: no page errors ${real.join("; ")}`);
        await ctx.close();
    }

    // ---- the other admin pages carry the same header ----
    for (const key of ["inv", "cur", "bin", "arc"]) {
        const { page, ctx, errors } = await openPage(browser, key, { width: 1280, height: 860 });
        const b = await page.locator("#notificationsBtn").boundingBox(), g = await page.locator("#quickSettingsBtn").boundingBox(), a = await page.locator("#accountBtn").boundingBox();
        ok(b && g && (key === "cur" ? !a : a) && b.x < g.x && (key === "cur" ? true : g.x < a.x) && Math.abs(b.y - g.y) < 0.6 && (key === "cur" || Math.abs(a.y - g.y) < 0.6), `${key}: header actions aligned`);
        const faults = await check(page);
        ok(faults.length === 0, `${key}: layout faults: ${faults.join(" | ")}`);
        ok(errors.length === 0, `${key}: no page errors ${errors.join("; ")}`);
        await ctx.close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
