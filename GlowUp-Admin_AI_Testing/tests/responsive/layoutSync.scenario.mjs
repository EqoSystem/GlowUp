// Named layouts reach every device (17 Nov 2026): "Echo" made on the computer showed nowhere else, because layouts lived only in
// that browser. Now they are also kept in adminLayouts/{uid}. Checks, on a phone-sized page and on a desktop page:
//   1. a fresh phone (nothing saved) shows Echo from the database copy in the Layout list;
//   2. loading a page does not count as a change (it would otherwise overwrite a newer copy made on another device);
//   3. a change on this device is written to the database copy.
// Run: node tests/responsive/layoutSync.scenario.mjs
import { launch, openPage } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (cond, what) => { checks++; if (!cond) { failed++; console.log("FAILED: " + what); } };
const echo = { active: "default", profiles: [{ id: "echo1", name: "Echo", appearance: {}, pages: { dashboard: { ver: 7, fit: true, order: [], size: {} } } }] };

const browser = await launch();
try {
    // 1. a phone that has never saved a layout
    {
        const { page, ctx, url } = await openPage(browser, "dash", { go: false, theme: "dark", width: 360, height: 780, touch: true });
        await ctx.addInitScript((store) => { window.__SEED.adminLayouts = { admin1: { store: JSON.stringify(store), savedAt: 5000 } }; }, echo);
        await page.goto(url, { waitUntil: "load" });
        await page.waitForFunction(() => [...document.querySelectorAll("#profileSelect option")].some((o) => o.textContent === "Echo"), null, { timeout: 5000 }).catch(() => {});
        const r = await page.evaluate(() => ({
            options: [...document.querySelectorAll("#profileSelect option")].map((o) => o.textContent),
            local: JSON.parse(localStorage.getItem("glowup.layouts.v1") || "null"),
            met: localStorage.getItem("glowup.layouts.synced.admin1")
        }));
        ok(r.options.includes("Echo"), `phone: Echo is in the Layout list (${r.options.join(", ")})`);
        ok(r.local && r.local.profiles.some((p) => p.name === "Echo"), "phone: Echo is kept in this browser too");
        ok(r.met === "1", "phone: this browser remembers it has synced");
        await ctx.close();
    }
    // 2 + 3. a computer that synced before, with an older copy than the database
    {
        const { page, ctx, url } = await openPage(browser, "dash", { go: false, width: 1440 });
        await ctx.addInitScript((store) => {
            window.__SEED.adminLayouts = { admin1: { store: JSON.stringify({ active: "default", profiles: [{ ...store.profiles[0], name: "Echo renamed" }] }), savedAt: 9000 } };
            localStorage.setItem("glowup.layouts.v1", JSON.stringify(store));
            localStorage.setItem("glowup.layouts.savedAt", "4000");
            localStorage.setItem("glowup.layouts.synced.admin1", "1");
        }, echo);
        await page.goto(url, { waitUntil: "load" });
        await page.waitForTimeout(2500);
        let r = await page.evaluate(() => ({ options: [...document.querySelectorAll("#profileSelect option")].map((o) => o.textContent), at: localStorage.getItem("glowup.layouts.savedAt"), remoteAt: window.__STORE.get("adminLayouts/admin1").savedAt }));
        ok(r.options.includes("Echo renamed") && !r.options.includes("Echo"), `computer: the newer database copy wins (${r.options.join(", ")})`);
        ok(r.at === "9000" && r.remoteAt === 9000, `loading the page is not a change: nothing is written back (local ${r.at}, database ${r.remoteAt})`);

        // a real change on this device: pick the layout (the store changes) and wait for the write
        await page.evaluate(() => { const s = document.querySelector("#profileSelect"); s.value = "echo1"; s.dispatchEvent(new Event("change", { bubbles: true })); });
        await page.waitForTimeout(2200);
        r = await page.evaluate(() => { const d = window.__STORE.get("adminLayouts/admin1"); return { savedAt: d.savedAt, active: JSON.parse(d.store).active, keys: Object.keys(d).sort().join(",") }; });
        ok(r.savedAt > 9000, `a change on this device is written to the database copy (savedAt ${r.savedAt})`);
        ok(r.keys === "savedAt,store,updatedAt", `the database copy has only the fields the rule allows (${r.keys})`);
        await ctx.close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
