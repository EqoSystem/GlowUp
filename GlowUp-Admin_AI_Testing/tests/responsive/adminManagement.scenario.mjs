// Admin Management page: sidebar item, cards and tabs from ONE listener, rows and their actions (you and a protected admin can only be viewed),
// archive / restore / bin / delete with confirmations and the right writes, rename, search, filter, export, details window.
// Run: node tests/responsive/adminManagement.scenario.mjs
import { launch, openPage } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (cond, what) => { checks++; if (!cond) { failed++; console.log("FAILED: " + what); } };
const doc = (page, path) => page.evaluate((path) => { const d = window.__STORE.get(path); return d ? JSON.parse(JSON.stringify(d)) : null; }, path);
const text = (page, sel) => page.evaluate((sel) => (document.querySelector(sel) || {}).textContent || "", sel);
const rowsOf = (page) => page.evaluate(() => [...document.querySelectorAll("#userTableBody tr[data-uid]")].map((r) => r.dataset.uid));
const actsOf = (page, uid) => page.evaluate((uid) => [...document.querySelectorAll(`tr[data-uid="${uid}"] [data-act]`)].map((b) => b.dataset.act).join(), uid);
const settleUi = async (page, ms = 150) => { await page.waitForTimeout(ms); await page.evaluate(() => document.getAnimations().forEach((a) => { try { a.finish(); } catch (e) { } })); };

const seedAdmins = () => {
    const t = Date.now(), day = 86400e3, ts = (ago) => ({ __ts: t - ago });
    window.__SEED.admins = {
        admin1: { email: "admin@example.test", name: "Admin Self", approvedAt: ts(90 * day), lastActiveAt: ts(30 * 1000) },
        m1: { email: "mia@example.com", name: "Mia Santos", approvedBy: "admin1", approvedAt: ts(2 * day), lastActiveAt: ts(60 * 1000) },
        m2: { email: "noel@example.com", name: "Noel Tan", approvedBy: "admin1", approvedAt: ts(20 * day), status: "archived", statusChangedAt: ts(1 * day) },
        m3: { email: "owner@example.com", name: "The Owner", approvedAt: ts(100 * day), protected: true },
        m4: { email: "dee@example.com", name: "Dee Park", approvedBy: "m1", approvedAt: ts(30 * day), inBin: true, status: "deleted", statusBefore: "archived", statusChangedAt: ts(3 * day) },
        m5: { email: "old@example.com", approvedAt: ts(10 * day) }
    };
};

const browser = await launch();
try {
    const { page, ctx, errors, url, settle } = await openPage(browser, "users", { width: 1440, height: 900, go: false });
    await ctx.addInitScript(seedAdmins);
    await page.goto(url.replace("_Admin_User_Management/adminUserManagement.html", "_Admin_Admin_Management/adminAdminManagement.html"));
    await page.waitForTimeout(1600); await settle(300);

    // sidebar
    const nav = await page.evaluate(() => [...document.querySelectorAll("#sidebarNav a")].map((a) => ({ label: a.getAttribute("aria-label"), active: a.classList.contains("active") })));
    ok(JSON.stringify(nav.map((n) => n.label)) === JSON.stringify(["Dashboard", "User Management", "Admin Management", "Brand & Product Inventory", "Ingredient Curator"]), `sidebar ${JSON.stringify(nav.map((n) => n.label))}`);
    ok(nav.find((n) => n.label === "Admin Management").active && !nav.find((n) => n.label === "User Management").active, "Admin Management is the active link");
    ok(/Admin/.test(await text(page, ".user-table thead")) && !/Role/.test(await text(page, ".user-table thead")) && !/Username/.test(await text(page, ".user-table thead")), "columns: Admin, Email, Status, Approved At, Last Active");

    // cards
    const val = (n) => text(page, `#kpi${n}Value`);
    ok(await val("All") === "6" && await val("Active") === "4" && await val("Archived") === "1" && await val("Bin") === "1", `cards ${await val("All")}/${await val("Active")}/${await val("Archived")}/${await val("Bin")}`);
    ok(await val("Online") === "2", `Online now counts admins seen in the last 5 minutes (${await val("Online")})`);
    ok(/Total Admins/.test(await text(page, "#userKpis")) && /Active Admins/.test(await text(page, "#userKpis")), "cards are named for admins");

    // rows and actions
    ok(await actsOf(page, "admin1") === "view" && await page.evaluate(() => !!document.querySelector('tr[data-uid="admin1"] .chip.um-self') && !document.querySelector('tr[data-uid="admin1"] .pick')), "your own row can only be viewed");
    ok(await actsOf(page, "m3") === "view" && /Protected/.test(await text(page, 'tr[data-uid="m3"] .um-actions')) && await page.evaluate(() => !document.querySelector('tr[data-uid="m3"] .pick')), "a protected admin can only be viewed");
    ok(await actsOf(page, "m1") === "view,edit,more", `an active admin: ${await actsOf(page, "m1")}`);
    ok(await actsOf(page, "m2") === "view,restore,bin" && await actsOf(page, "m4") === "view,restore,purge", "archived: Restore, Delete; in the bin: Restore, Delete permanently");
    ok(/Approved by Admin Self/.test(await text(page, 'tr[data-uid="m1"]')) && /Added in the console/.test(await text(page, 'tr[data-uid="admin1"]')), "who approved is shown by name");
    ok(/old@example\.com/.test(await text(page, 'tr[data-uid="m5"] .um-name')), "an admin record with no name shows the e-mail");

    // tabs
    await page.click('#userKpis [data-tab="archived"]');
    ok(JSON.stringify(await rowsOf(page)) === '["m2"]' && /Archived Admins/.test(await text(page, "#userTitle")), "the Archived tab");
    await page.click('#userKpis [data-tab="bin"]');
    ok(JSON.stringify(await rowsOf(page)) === '["m4"]', "the Recycle Bin tab");
    await page.click('#userKpis [data-tab="online"]');
    ok((await rowsOf(page)).sort().join() === "admin1,m1", "the Online tab");
    await page.click('#userKpis [data-tab="all"]');

    // search + filter + export
    await page.fill("#userSearchInput", "noel@"); await settleUi(page, 300);
    ok(JSON.stringify(await rowsOf(page)) === '["m2"]', "search finds an e-mail");
    await page.fill("#userSearchInput", "zzzz"); await settleUi(page, 300);
    ok(await page.isVisible("#userEmpty") && /No admins match/.test(await text(page, "#userEmptyMessage")), "no match: a friendly empty state");
    await page.fill("#userSearchInput", ""); await settleUi(page, 300);
    await page.click("#userFilterBtn");
    ok(!(await page.$("#facet_role")) && !!(await page.$("#facet_status")), "the filter has Status only (no Role)");
    await page.selectOption("#facet_status", "archived");
    await page.click(".facet-actions .save-btn");
    ok(JSON.stringify(await rowsOf(page)) === '["m2"]', "the Status filter works");
    await page.click("#userFilterBtn"); await page.click(".facet-actions .cancel-btn");
    const [download] = await Promise.all([page.waitForEvent("download"), page.click("#userExportBtn")]);
    const csv = (await (await import("node:fs")).promises.readFile(await download.path(), "utf8")).replace(/^﻿/, "").split("\r\n");
    ok(csv[0] === "UID,Name,Email,Status,Approved At,Last Active" && csv.length >= 7, `export header ${csv[0]}`);
    ok(/^glowup-admins-\d{4}-\d{2}-\d{2}\.csv$/.test(download.suggestedFilename()), "export file name");

    // details
    await page.click('tr[data-uid="m1"] [data-act="view"]');
    ok(await page.isVisible(".um-dialog") && /Mia Santos/.test(await text(page, ".um-dialog")) && /mia@example\.com/.test(await text(page, ".um-dialog")) && !/Skin/.test(await text(page, ".um-dialog")), "details: account info, no skin profile");
    await page.keyboard.press("Escape");

    // archive one admin: a confirmation, then the right fields are written
    await page.click('tr[data-uid="m1"] [data-act="more"]');
    ok(JSON.stringify(await page.evaluate(() => [...document.querySelectorAll(".menu .menu-item")].map((b) => b.textContent))) === JSON.stringify(["View Details", "Edit Admin", "Move to Archive", "Delete to Recycle Bin"]), "the More menu");
    await page.click(".menu .menu-item:nth-of-type(3)");
    ok(/Archive Admin/.test(await text(page, ".modal-content h3")), "archiving asks first");
    await page.click(".modal-content .save-btn"); await settleUi(page, 300);
    const m1 = await doc(page, "admins/m1");
    ok(m1 && m1.status === "archived" && m1.inBin === false && m1.statusChangedBy === "admin1", `archive wrote ${JSON.stringify(m1 && { s: m1.status, b: m1.inBin, by: m1.statusChangedBy })}`);
    ok(m1.email === "mia@example.com" && m1.name === "Mia Santos", "the e-mail and name are not touched by a status change");

    // restore it
    await page.click('#userKpis [data-tab="archived"]');
    await page.click('tr[data-uid="m1"] [data-act="restore"]');
    await page.click(".modal-content .save-btn"); await settleUi(page, 300);
    ok((await doc(page, "admins/m1")).status === "active", "restore brings the admin back");

    // bin, then delete for good only from the bin
    await page.click('#userKpis [data-tab="bin"]');
    await page.click('tr[data-uid="m4"] [data-act="purge"]');
    ok(/Delete Permanently/.test(await text(page, ".modal-content h3")) && /Dee Park/.test(await text(page, ".modal-content")), "delete permanently names the admin");
    await page.click(".modal-content .btn.danger"); await settleUi(page, 300);
    ok(!(await doc(page, "admins/m4")), "delete permanently removes the record from the bin");
    ok(await doc(page, "admins/m3") && await doc(page, "admins/admin1"), "the protected admin and you are never removed");

    // rename
    await page.click('#userKpis [data-tab="all"]');
    await page.click('tr[data-uid="m5"] [data-act="edit"]');
    await page.fill("#umName", "Old Admin");
    await page.click(".um-form .save-btn"); await settleUi(page, 300);
    ok((await doc(page, "admins/m5")).name === "Old Admin" && (await doc(page, "admins/m5")).email === "old@example.com", "rename changes the name only");
    ok(errors.length === 0, `no page errors ${JSON.stringify(errors.slice(0, 2))}`);
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
