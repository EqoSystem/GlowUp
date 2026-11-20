// New23 (20 November 2026): the guideline probe (contrast in both themes, 44 px touch targets, no overlap or spill) on the
// parts that only exist once opened: the bell and profile dropdowns, the System status card, the Reply drawer, the user
// details and edit windows, and the sign-up card. guidelines.scenario.mjs covers the pages as they first open.
// Run: node tests/responsive/guidelinesOpen.scenario.mjs          (exit 1 on any fault)
import { launch, openPage } from "./harness.mjs";
import { probe } from "./guidelinesProbe.mjs";

const seed = () => {
    const now = Math.floor(Date.now() / 1000), t = Date.now();
    window.__SEED.supportReports = {
        b1: { uid: "u1abcdefgh", userName: "Mark Azcueta With An Unusually Long Display Name", category: "Bug", message: "The scan button freezes after I take a photo of a lipstick and nothing happens until I restart the app completely.", status: "new", createdAtUnix: now - 7200 },
        s1: { uid: "u4abcdefgh", userName: "Ana", category: "Suggestion", message: "Please add a shade finder", status: "new", createdAtUnix: now - 3 * 86400 }
    };
    window.__SEED.users = {
        admin1: { fullName: "Admin Self", email: "admin@example.test", createdAt: { __ts: t - 9e9 } },
        u1: { fullName: "Ana Reyes Dela Cruz Santos Villanueva", username: "ana_with_a_very_long_username_here", email: "ana.reyes.delacruz.santos@example.com", createdAt: { __ts: t - 2e8 }, skinType: "Oily", skinConditions: ["Acne", "Redness", "Rosacea", "Hyperpigmentation"], knownAllergens: ["Nickel", "Fragrance (parfum)"] }
    };
};
const finish = (page) => page.evaluate(() => document.getAnimations().forEach((a) => { try { a.finish(); } catch (e) { } }));

const STATES = [
    { key: "dash", name: "bell dropdown", open: (p) => p.click("#notificationsBtn") },
    { key: "dash", name: "profile dropdown", open: (p) => p.click("#accountBtn") },
    { key: "dash", name: "Reply drawer", open: (p) => p.evaluate(() => import("/_Shared/replyDrawer.js").then((m) => m.openReplyDrawer({ id: "b1", uid: "u1abcdefgh", userName: "Mark Azcueta With An Unusually Long Display Name", category: "Bug", message: "The scan button freezes after I take a photo of a lipstick and nothing happens until I restart the app completely.", createdAtUnix: Math.floor(Date.now() / 1000) - 7200 }))) },
    { key: "users", name: "user details", open: (p) => p.click('#userTableBody tr[data-uid="u1"] [data-act="view"]') },
    { key: "users", name: "user edit", open: (p) => p.click('#userTableBody tr[data-uid="u1"] [data-act="edit"]') },
    { key: "login", name: "sign-up card", open: (p) => p.click("#signupLink") }
];

const browser = await launch();
let faults = 0;
try {
    for (const theme of ["light", "dark"]) for (const [w, h, touch] of [[1440, 900, false], [390, 844, true]]) for (const st of STATES) {
        const { page, ctx, url, settle } = await openPage(browser, st.key, { theme, width: w, height: h, touch, go: false });
        await ctx.addInitScript(seed);
        await page.goto(url); await page.waitForTimeout(1500); await settle(300);
        let out = { low: [], small: [] }, layout = [], fail = "";
        try {
            await st.open(page); await page.waitForTimeout(500); await finish(page);
            out = await page.evaluate(probe);
            const a = await page.evaluate(() => window.__audit());
            layout = [...a.hscroll, ...a.spill, ...a.offscreen];   // an open dropdown or drawer covers the page on purpose, so text overlap is not counted
        } catch (e) { fail = String(e.message || e).split("\n")[0]; }
        const targets = touch ? out.small : [];
        const n = out.low.length + targets.length + layout.length + (fail ? 1 : 0);
        if (n) {
            faults += n;
            console.log(`--- ${st.name} ${theme} ${w}px${touch ? " touch" : ""}`);
            if (fail) console.log("  could not open: " + fail);
            out.low.forEach((x) => console.log("  contrast  " + x));
            targets.forEach((x) => console.log("  target    " + x));
            layout.forEach((x) => console.log("  layout    " + x));
        }
        await ctx.close();
    }
} finally { await browser.close(); }
console.log(faults === 0 ? "ALL PASSED" : `${faults} faults`);
process.exit(faults === 0 ? 0 : 1);
