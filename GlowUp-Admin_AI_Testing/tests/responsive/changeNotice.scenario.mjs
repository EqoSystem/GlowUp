// New23: saving a product or an ingredient on the website also writes ONE notice to announcements (the app's Notifications), in the same
// batch as the save and the audit record; a brand save writes none.
// Run: node tests/responsive/changeNotice.scenario.mjs
import { launch, openPage } from "./harness.mjs";
let checks = 0, failed = 0;
const ok = (cond, what) => { checks++; if (!cond) { failed++; console.log("FAILED: " + what); } };
const notices = (page) => page.evaluate(() => [...window.__STORE.entries()].filter(([k]) => k.startsWith("announcements/")).map(([, v]) => ({ kind: v.kind, title: v.title, body: v.body, by: v.createdBy, keys: Object.keys(v).sort().join(",") })));

const browser = await launch();
try {
    const { page, ctx, url, settle } = await openPage(browser, "dash", { width: 1280, height: 800, go: false });
    await page.goto(url); await page.waitForTimeout(1500); await settle(300);
    const save = (collection, id, data) => page.evaluate(async ([collection, id, data]) => {
        const store = await import("/_Shared/adminStore.js"); const fb = await import("https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js"); const { db } = await import("/_Database/firebase-console.js");
        await store.writeBatch(db).update(fb.doc(db, collection, id), data).commit();
    }, [collection, id, data]);
    const before = (await notices(page)).length;
    await save("brands", "b1", { name: "Brand Edit" });
    ok((await notices(page)).length === before, "a brand save writes no notice");
    await save("ingredients", "i1", { name: "Niacinamide", description: "Vitamin B3" });
    let list = await notices(page);
    ok(list.length === before + 1, "an ingredient save writes one notice");
    const n = list[list.length - 1];
    ok(n && n.kind === "ingredient" && n.title === "Ingredient updated" && /Niacinamide was updated/.test(n.body), `the notice text ${JSON.stringify(n)}`);
    ok(n && n.keys === "body,createdAt,createdBy,kind,title" && !!n.by, `the notice has exactly the fields the rules allow (${n && n.keys})`);
    await save("products", "p1", { scans: 5 });
    ok((await notices(page)).length === before + 1, "a scan counter alone writes no notice");
    await save("products", "p1", { name: "Velvet Tint", status: "Active" });
    list = await notices(page);
    ok(list.length === before + 2 && list[list.length - 1].kind === "product", "a product save writes a product notice");
    await ctx.close();
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
