// User Management page: sidebar link, live cards and tabs from ONE listener, client-side search / filter / paging, CSV export,
// details window (live), edit window, confirmations for every destructive action, empty states, responsive layout.
// Run: node tests/responsive/userManagement.scenario.mjs   (SHOT_DIR=<folder> also saves pictures)
import { launch, openPage, check } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (cond, what) => { checks++; if (!cond) { failed++; console.log("FAILED: " + what); } };
const shot = async (page, name) => { if (process.env.SHOT_DIR) await page.screenshot({ path: `${process.env.SHOT_DIR}/${name}.png` }); };
const doc = (page, path) => page.evaluate((path) => { const d = window.__STORE.get(path); return d ? JSON.parse(JSON.stringify(d)) : null; }, path);
const text = (page, sel) => page.evaluate((sel) => (document.querySelector(sel) || {}).textContent || "", sel);
const rowsOf = (page) => page.evaluate(() => [...document.querySelectorAll("#userTableBody tr[data-uid]")].map((r) => r.dataset.uid));
const settleUi = async (page, ms = 150) => { await page.waitForTimeout(ms); await page.evaluate(() => document.getAnimations().forEach((a) => { try { a.finish(); } catch (e) { } })); };

const seedUsers = () => {
    const t = Date.now(), day = 86400e3, ts = (ago) => ({ __ts: t - ago });
    const U = {
        admin1: { fullName: "Admin Self", username: "adminself", email: "admin@example.test", createdAt: ts(90 * day) },
        u1: { fullName: "Ana Reyes", username: "ana", email: "ana@example.com", createdAt: ts(2 * day), skinType: "Oily", skinTone: "Medium", skinConditions: ["Acne", "Redness"], knownAllergens: ["Nickel"], age: 24, photoURL: "https://example.com/ana.png" },
        u2: { fullName: "Ben Cruz", username: "ben", email: "ben@example.com", createdAt: ts(20 * day), status: "archived", statusChangedAt: ts(1 * day), role: "admin" },
        u3: { fullName: "Cara Lim", username: "cara", email: "cara@example.com", createdAt: ts(40 * day), status: "banned" },
        u4: { fullName: "Dee Park", email: "dee@example.com", createdAt: ts(30 * day), inBin: true, status: "deleted", statusBefore: "archived", statusChangedAt: ts(3 * day) },
        u5: { fullName: "Eve Stone", email: "eve@example.com", createdAt: ts(3 * day), lastActiveAt: ts(60 * 1000) }
    };
    for (let i = 6; i <= 17; i++) U["u" + i] = { fullName: "Person " + String(i).padStart(2, "0"), username: "p" + i, email: `p${i}@example.com`, createdAt: ts((50 + i) * day) };
    window.__SEED.users = U;
};

const browser = await launch();
try {
    // ---------- the page, 1440 ----------
    {
        const { page, ctx, errors, url, settle } = await openPage(browser, "users", { width: 1440, height: 900, go: false });
        await ctx.addInitScript(seedUsers);
        await page.goto(url); await page.waitForTimeout(1600); await settle(300);

        // sidebar
        const nav = await page.evaluate(() => [...document.querySelectorAll("#sidebarNav a")].map((a) => ({ label: a.getAttribute("aria-label"), active: a.classList.contains("active"), href: a.getAttribute("href") })));
        ok(JSON.stringify(nav.map((n) => n.label)) === JSON.stringify(["Dashboard", "User Management", "Brand & Product Inventory", "Ingredient Curator"]), `sidebar order ${JSON.stringify(nav.map((n) => n.label))}`);
        ok(nav.find((n) => n.label === "User Management").active, "User Management is the active link");
        ok(await page.evaluate(() => { const links = [...document.querySelectorAll("#sidebarNav a")]; const a = links[2], b = links[1]; const sa = getComputedStyle(a), sb = getComputedStyle(b); return sa.fontSize === sb.fontSize && a.getBoundingClientRect().height === b.getBoundingClientRect().height && a.querySelector("svg") && a.querySelector("svg").getBoundingClientRect().width === b.querySelector("svg").getBoundingClientRect().width; }), "the new sidebar item has the size of its neighbours");
        ok(!(await page.$(".um-title")) && !(await page.$("#userTabs")), "one page title (the header) and no header tabs: the cards are the filters");
        ok(await page.evaluate(() => !!document.querySelector("[data-panel=list] .panel-head #userSearchInput")), "the search sits inside the Users container");
        ok(/Username/.test(await text(page, ".user-table thead")), "the first column is called Username");

        // cards, tabs: one listener
        const val = (n) => text(page, `#kpi${n}Value`), week = (n) => text(page, `#kpi${n}Week`);
        ok(await val("All") === "18" && await val("Active") === "15" && await val("Archived") === "1" && await val("Bin") === "1", `cards ${await val("All")}/${await val("Active")}/${await val("Archived")}/${await val("Bin")}`);
        ok(await week("All") === "+2 this week", `total delta ${await week("All")}`);
        ok(await week("Archived") === "+1 this week" && await week("Bin") === "+1 this week", "archived / bin deltas use the status change time");
        ok(await val("Online") === "1", `Online now counts a user active in the last 5 minutes (${await val("Online")})`);
        await page.click("#kpiOnline"); await page.waitForTimeout(150);
        ok(JSON.stringify(await rowsOf(page)) === JSON.stringify(["u5"]) && /Online Now/.test(await text(page, "#userTitle")) && await page.getAttribute("#kpiOnline", "aria-pressed") === "true", "the Online card lists who is online");
        await page.evaluate(() => { const d = window.__STORE.get("users/u5"); window.__STORE.set("users/u5", Object.assign({}, d, { lastActiveAt: { __ts: Date.now() - 10 * 60 * 1000 } })); window.__NOTIFY(); });
        await page.waitForTimeout(150);
        ok(await page.isVisible("#userEmpty") && await text(page, "#userEmptyMessage") === "No users are online.", "the Online tab has a safe empty state");
        await page.click("#kpiAll"); await page.waitForTimeout(150);
        const liveBefore = await page.evaluate(() => window.__LIVE());
        ok(await page.evaluate(() => !document.querySelector(".kpi-value.skeleton")), "the skeleton leaves the numbers once data is in");

        // table
        ok((await rowsOf(page)).length === 10 && await page.inputValue("#userPageSize") === "10", "10 rows on the first page");
        ok(/1-10 of 18 users/.test(await text(page, "#userPager")) && /Page 1 of 2/.test(await text(page, "#userPager")), "pager says 1-10 of 18, Page 1 of 2");
        const ana = await page.evaluate(() => { const r = document.querySelector('tr[data-uid="u1"]'); return { avatarImg: !!r.querySelector(".um-avatar img"), role: r.querySelector(".status-badge.role-user")?.textContent, status: r.querySelector(".status-badge.user-active")?.textContent, last: r.children[6].textContent.trim() }; });
        ok(ana.avatarImg && ana.role === "User" && ana.status === "Active" && ana.last === "Not recorded", `Ana's row ${JSON.stringify(ana)}`);
        const eve = await page.evaluate(() => document.querySelector('tr[data-uid="u5"] .um-avatar').textContent);
        ok(eve === "E", "a user with no photo shows the first letter");
        ok(await page.evaluate(() => !!document.querySelector('tr[data-uid="u3"] .status-badge.user-banned') && !!document.querySelector('tr[data-uid="u3"] [data-act="unban"]')), "a banned user has Unban");
        await shot(page, "users_1440");

        // paging
        await page.selectOption("#userPageSize", "25");
        ok((await rowsOf(page)).length === 18 && (await text(page, "#userPager")) === "", "25 rows per page shows all 18 (no pager)");
        ok(await page.evaluate(() => !!document.querySelector('tr[data-uid="admin1"] .chip.um-self') && !document.querySelector('tr[data-uid="admin1"] [data-act="edit"]') && !document.querySelector('tr[data-uid="admin1"] .pick')), "your own row can only be viewed");
        await page.selectOption("#userPageSize", "10");
        await page.click('#userPager [data-go="1"]');
        ok((await rowsOf(page)).length === 8 && /Page 2 of 2/.test(await text(page, "#userPager")), "next page shows the last 8");
        await page.click('#userPager [data-go="-1"]');

        // tabs
        await page.click('#userKpis [data-tab="archived"]');
        ok(JSON.stringify(await rowsOf(page)) === '["u2"]' && /Archived Users/.test(await text(page, "#userTitle")), "Archived tab shows only archived users");
        ok(await page.evaluate(() => [...document.querySelectorAll('tr[data-uid="u2"] [data-act]')].map((b) => b.dataset.act).join() === "view,restore,bin"), "archived rows: View, Restore, Delete");
        await page.click('#userKpis [data-tab="bin"]');
        ok(JSON.stringify(await rowsOf(page)) === '["u4"]' && await page.evaluate(() => [...document.querySelectorAll('tr[data-uid="u4"] [data-act]')].map((b) => b.dataset.act).join() === "view,restore,purge"), "bin rows: View, Restore, Delete permanently");
        await page.click('#userKpis [data-tab="active"]');
        ok(!(await rowsOf(page)).includes("u3") && !(await rowsOf(page)).includes("u2"), "Active tab leaves out banned, archived and binned users");
        await page.click("#kpiBin");
        ok(JSON.stringify(await rowsOf(page)) === '["u4"]', "a card opens its tab");
        await page.click('#userKpis [data-tab="all"]');

        // search is client side
        await page.fill("#userSearchInput", "ben@exa"); await settleUi(page, 300);
        ok(JSON.stringify(await rowsOf(page)) === '["u2"]', "search finds an e-mail");
        await page.fill("#userSearchInput", "u17"); await settleUi(page, 300);
        ok(JSON.stringify(await rowsOf(page)) === '["u17"]', "search finds a UID");
        await page.fill("#userSearchInput", "zzzz"); await settleUi(page, 300);
        ok(await page.isVisible("#userEmpty") && /No users match/.test(await text(page, "#userEmptyMessage")) && await page.evaluate(() => !!document.querySelector(".um-empty-art")), "no match: a friendly empty state with an illustration");
        await page.fill("#userSearchInput", ""); await settleUi(page, 300);
        ok((await rowsOf(page)).length === 10, "clearing the search brings the list back");
        ok(await page.evaluate(() => window.__LIVE()) === liveBefore, "tabs, search and paging used no new listener");

        // filter
        await page.click("#userFilterBtn");
        await page.selectOption("#facet_role", "admin");
        await page.click(".facet-actions .save-btn");
        ok(JSON.stringify(await rowsOf(page)) === '["u2"]' && /Filter \(1\)/.test(await text(page, "#userFilterBtn")), "the Role filter works and shows on the button");
        await page.click("#userFilterBtn");
        await page.click(".facet-actions .cancel-btn");
        ok((await rowsOf(page)).length === 10, "Clear removes the filter");
        await page.click("#userFilterBtn");
        await page.selectOption("#facet_status", "banned");
        await page.click(".facet-actions .save-btn");
        ok(JSON.stringify(await rowsOf(page)) === '["u3"]', "the Status filter finds Banned");
        await page.click("#userFilterBtn"); await page.click(".facet-actions .cancel-btn");

        // export: the visible list
        await page.fill("#userSearchInput", "ana"); await settleUi(page, 300);
        const [download] = await Promise.all([page.waitForEvent("download"), page.click("#userExportBtn")]);
        const csv = (await (await import("node:fs")).promises.readFile(await download.path(), "utf8")).replace(/^﻿/, "").split("\r\n");
        ok(csv[0] === "UID,Name,Email,Role,Status,Joined At,Last Active" && csv.length === 2 && /^u1,Ana Reyes,ana@example\.com,User,Active,/.test(csv[1]), `export holds only the filtered rows ${JSON.stringify(csv)}`);
        ok(/^glowup-users-\d{4}-\d{2}-\d{2}\.csv$/.test(download.suggestedFilename()), "export file name");
        await page.fill("#userSearchInput", ""); await settleUi(page, 300);

        // details window: live
        await page.click('tr[data-uid="u1"] .um-name');
        ok(await page.isVisible(".um-dialog") && /Skin type/.test(await text(page, ".um-dialog")) && /Oily/.test(await text(page, ".um-dialog")) && /Nickel/.test(await text(page, ".um-dialog")), "the details window shows account info and skin profile");
        ok(await page.evaluate(() => document.querySelectorAll(".um-dialog .um-tags .chip").length === 3), "skin concerns and allergens are tags");
        ok(await page.evaluate(() => document.activeElement.closest(".um-dialog") !== null), "focus is inside the window");
        await page.evaluate(() => { const d = window.__STORE.get("users/u1"); window.__STORE.set("users/u1", Object.assign({}, d, { fullName: "Ana R. Reyes", skinType: "Dry" })); window.__NOTIFY(); });
        await page.waitForTimeout(120);
        ok(/Ana R\. Reyes/.test(await text(page, ".um-dialog")) && /Dry/.test(await text(page, ".um-dialog")), "the open window follows a live change");
        for (let i = 0; i < 6; i++) await page.keyboard.press("Tab");
        ok(await page.evaluate(() => document.activeElement.closest(".um-dialog") !== null), "Tab stays inside the window");
        await page.keyboard.press("Escape");
        ok(!(await page.isVisible(".um-dialog")), "Escape closes the window");
        await page.click('tr[data-uid="u5"] [data-act="view"]');
        ok(/No skin profile found/.test(await text(page, ".um-dialog")), "a user with no skin profile gets a calm message");
        await page.mouse.click(5, 450);
        ok(!(await page.isVisible(".um-dialog")), "a click beside the window closes it");
        await page.click('tr[data-uid="u5"] [data-act="view"]');
        await page.click(".um-dialog .btn.primary");
        ok(!(await page.isVisible(".um-dialog")), "Close closes it");

        // edit
        await page.click('tr[data-uid="u1"] [data-act="edit"]');
        ok(await page.inputValue("#umName") === "Ana R. Reyes" && await page.inputValue("#umEmail") === "ana@example.com" && await page.isDisabled("#umEmail") === false && await page.getAttribute("#umEmail", "readonly") !== null, "the form is filled in and the e-mail is read-only");
        await page.fill("#umName", "A");
        await page.click(".um-dialog .save-btn");
        ok(/at least 2 letters/.test(await text(page, ".um-form .form-error")) && await page.getAttribute("#umName", "aria-invalid") === "true", "an invalid name is caught inline");
        await page.fill("#umName", "Ana Reyes-Cruz");
        await page.keyboard.press("Escape");
        ok(await page.evaluate(() => [...document.querySelectorAll(".modal-content h3")].some((h) => /Discard changes\?/.test(h.textContent))), "Escape with changes asks Discard changes?");
        await page.click(".modal-overlay:last-of-type .cancel-btn");
        ok(await page.inputValue("#umName") === "Ana Reyes-Cruz", "Keep editing keeps the form");
        await page.selectOption("#umRole", "admin");
        await page.click(".um-dialog .save-btn");
        await page.waitForTimeout(250);
        const saved = await doc(page, "users/u1");
        ok(saved.fullName === "Ana Reyes-Cruz" && saved.role === "admin" && saved.status === undefined, `the edit is saved ${JSON.stringify([saved.fullName, saved.role])}`);
        ok(await page.evaluate(() => [...window.__STORE.entries()].some(([k, v]) => k.startsWith("auditLog/") && (v.items || []).some((i) => i.collection === "users" && i.docId === "u1" && i.action === "update"))), "the edit is in the audit log");
        ok(!(await page.isVisible(".um-dialog")) && /Changes saved/.test(await text(page, "#toastHost")), "the window closes and a toast says saved");
        ok(await page.evaluate(() => !!document.querySelector('tr[data-uid="u1"] .status-badge.role-admin')), "the table shows the change by itself");

        // confirmations: Escape and Cancel change nothing
        await page.click('tr[data-uid="u5"] [data-act="more"]');
        const menu = await page.evaluate(() => [...document.querySelectorAll(".menu .menu-item")].map((b) => b.textContent));
        ok(JSON.stringify(menu) === JSON.stringify(["View Details", "Edit User", "Move to Archive", "Delete to Recycle Bin"]), `More menu ${JSON.stringify(menu)}`);
        await page.click(".menu .menu-item:nth-of-type(3)");
        ok(/Archive User/.test(await text(page, ".modal-content h3")), "Move to Archive asks first");
        await page.keyboard.press("Escape"); await page.waitForTimeout(100);
        ok((await doc(page, "users/u5")).status === undefined, "Escape on the question archives nothing");
        await page.click('tr[data-uid="u5"] [data-act="more"]');
        await page.click(".menu .menu-item:nth-of-type(3)");
        await page.click(".modal-content .save-btn");
        await page.waitForTimeout(250);
        const eveDoc = await doc(page, "users/u5");
        ok(eveDoc.status === "archived" && eveDoc.inBin === false && eveDoc.statusChangedBy === "admin1", `archived ${JSON.stringify(eveDoc.status)}`);
        ok(await text(page, "#kpiArchivedValue") === "2" && await text(page, "#kpiActiveValue") === "14", "the cards follow by themselves");

        await page.click('tr[data-uid="u5"] [data-act="bin"]');
        ok(/Move to Recycle Bin/.test(await text(page, ".modal-content h3")), "Delete to Recycle Bin asks first");
        await page.click(".modal-content .btn.danger");
        await page.waitForTimeout(250);
        const eveBin = await doc(page, "users/u5");
        ok(eveBin.status === "deleted" && eveBin.inBin === true && eveBin.statusBefore === "archived", "moved to the bin, remembering where it was");
        await page.click('#userKpis [data-tab="bin"]');
        await page.click('tr[data-uid="u5"] [data-act="restore"]');
        await page.click(".modal-content .save-btn");
        await page.waitForTimeout(250);
        const eveBack = await doc(page, "users/u5");
        ok(eveBack.status === "archived" && eveBack.inBin === false, "Restore from the bin goes back to Archived");

        // permanent delete: confirmation lists the name; Cancel keeps it
        await page.click('tr[data-uid="u4"] [data-act="purge"]');
        ok(/Delete Permanently/.test(await text(page, ".modal-content h3")) && /Dee Park/.test(await text(page, ".modal-content")), "Permanently Delete names the user");
        await page.click(".modal-content .cancel-btn");
        ok(!!(await doc(page, "users/u4")), "Cancel keeps the user");
        await page.click('tr[data-uid="u4"] [data-act="purge"]');
        await page.click(".modal-content .btn.danger");
        await page.waitForTimeout(250);
        ok(!(await doc(page, "users/u4")), "the user is deleted after the confirmed click");
        ok(await text(page, "#userEmptyMessage") === "The Recycle Bin is empty." && await page.isVisible("#userEmpty"), "an empty bin gets its empty state");

        // unban
        await page.click('#userKpis [data-tab="all"]');
        await page.click('tr[data-uid="u3"] [data-act="unban"]');
        ok(/Unban User/.test(await text(page, ".modal-content h3")), "Unban asks first");
        await page.click(".modal-content .save-btn");
        await page.waitForTimeout(250);
        ok((await doc(page, "users/u3")).status === "active", "Unban sets the user active");

        // New23: banning from the edit form asks first (red button); Cancel keeps the form and bans nobody
        await page.click('tr[data-uid="u3"] [data-act="edit"]');
        await page.selectOption("#umStatus", "banned");
        await page.click(".um-dialog .save-btn");
        ok(await page.evaluate(() => { const top = [...document.querySelectorAll(".modal-overlay")].pop(); return /Ban User/.test((top.querySelector("h3") || {}).textContent) && !!top.querySelector(".btn.danger"); }), "Ban from the edit form asks first, with a red button");
        await page.click(".modal-overlay:last-of-type .cancel-btn"); await page.waitForTimeout(150);
        ok((await doc(page, "users/u3")).status === "active" && await page.isVisible(".um-dialog"), "Cancel bans nobody and keeps the form");
        await page.click(".um-dialog .save-btn");
        await page.locator(".modal-overlay").last().locator(".btn.danger").click(); await page.waitForTimeout(250);
        ok((await doc(page, "users/u3")).status === "banned", "confirming bans the user");

        // bulk
        await page.click('tr[data-uid="u6"] .pick'); await page.click('tr[data-uid="u7"] .pick');
        ok(await page.isVisible("#userBar") && /2 selected/.test(await text(page, "#userBarCount")) && await page.isVisible("#barArchive") && !(await page.isVisible("#barRestore")), "selecting rows shows the bar with the actions that fit");
        await page.click("#barBin");
        ok(await page.evaluate(() => document.querySelectorAll(".modal-content .confirm-list li").length === 2), "the bulk question lists both names");
        await page.keyboard.press("Escape");
        ok(!(await doc(page, "users/u6")).status, "Escape on the bulk question changes nothing");
        await page.click("#barClear");
        ok(!(await page.isVisible("#userBar")), "Cancel clears the selection");

        // one listener only; released on leaving
        ok(await page.evaluate(() => window.__LIVE()) === liveBefore, "still the same single page listener after all of this");
        await page.evaluate(() => window.dispatchEvent(new Event("pagehide")));
        ok(await page.evaluate(() => window.__LIVE()) < liveBefore, "leaving the page releases the users listener");
        const real = errors.filter((e) => !/ViewTransition|ERR_FAILED/.test(e));
        ok(real.length === 0, `no page errors ${real.join("; ")}`);
        await ctx.close();
    }

    // ---------- first load: skeleton, then data; and a refused read ----------
    {
        const { page, ctx, url } = await openPage(browser, "users", { width: 1280, height: 860, go: false, lat: { auth: 0, read: 900 } });
        await ctx.addInitScript(seedUsers);
        await page.goto(url); await page.waitForTimeout(500);
        ok(await page.evaluate(() => !!document.querySelector("#userTableBody .sk-list") && !!document.querySelector(".kpi-value.skeleton")), "skeleton rows and skeleton numbers while loading");
        await page.waitForTimeout(2200);
        ok(await page.evaluate(() => !document.querySelector("#userTableBody .sk-list") && !document.querySelector(".kpi-value.skeleton")), "the skeleton is gone once the data is here");
        await ctx.close();
    }
    {
        const { page, ctx, url } = await openPage(browser, "users", { width: 1280, height: 860, go: false });
        await ctx.addInitScript(() => { window.__SNAP_ERROR = "permission-denied"; });
        await page.goto(url); await page.waitForTimeout(1600);
        ok(/permission denied/i.test(await text(page, "#userTableBody")) && await page.isVisible("#userRetry"), "a refused read says so and offers Try again");
        await page.evaluate(() => { window.__SNAP_ERROR = null; window.__SEED = window.__SEED || {}; });
        await ctx.close();
    }

    // ---------- responsive ----------
    for (const [width, theme, touch] of [[1024, "light", false], [768, "dark", false], [390, "light", true], [360, "dark", true]]) {
        const tag = `${width} ${theme}`;
        const { page, ctx, errors, url, settle } = await openPage(browser, "users", { width, height: 860, theme, go: false, touch });
        await ctx.addInitScript(seedUsers);
        await page.goto(url); await page.waitForTimeout(1600); await settle(300);
        const faults = await check(page);
        ok(faults.length === 0, `${tag}: layout faults: ${faults.join(" | ")}`);
        const kp = await page.evaluate(() => { const r = [...document.querySelectorAll("#userKpis .kpi")].map((k) => k.getBoundingClientRect()); return { n: r.length, within: r.every((b) => b.left >= -0.5 && b.right <= document.documentElement.clientWidth + 0.5) }; });
        ok(kp.n === 5 && kp.within, `${tag}: five cards, all inside the window`);
        ok((await rowsOf(page)).length === 10, `${tag}: the table lists users`);
        const actionsIn = await page.evaluate(() => { const c = document.querySelector(".table-container"), b = document.querySelector('tr[data-uid="u1"] .um-actions').getBoundingClientRect(), cr = c.getBoundingClientRect(); return b.right <= cr.right + 1 || c.scrollWidth > c.clientWidth; });
        ok(actionsIn, `${tag}: the actions are reachable`);
        await page.click('tr[data-uid="u1"] [data-act="edit"]'); await settle(300);
        const dlg = await page.locator(".um-dialog").boundingBox();
        ok(dlg && dlg.x >= -0.5 && dlg.x + dlg.width <= width + 0.5, `${tag}: the edit window fits`);
        await shot(page, `users_edit_${width}_${theme}`);
        await page.keyboard.press("Escape"); await settle(150);
        await shot(page, `users_${width}_${theme}`);
        const real = errors.filter((e) => !/ViewTransition|ERR_FAILED/.test(e));
        ok(real.length === 0, `${tag}: no page errors ${real.join("; ")}`);
        await ctx.close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
