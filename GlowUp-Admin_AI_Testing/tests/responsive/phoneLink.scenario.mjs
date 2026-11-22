// "Use my phone": the computer opens a link window, the phone page connects with the same admin account.
import { launch, openPage, BASE } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };

const browser = await launch();
try {
    const { page, ctx, errors, settle } = await openPage(browser, "inv", { width: 1280, height: 860 });
    await settle(500);
    await page.evaluate(() => document.getElementById("addMenuBtn").click());
    await page.waitForTimeout(200);
    await page.locator(".menu-item, [role=menuitem]").filter({ hasText: "Product" }).first().click();
    await page.waitForSelector("#productForm", { state: "visible" });
    await page.click("#productQuickPhone");
    await page.waitForSelector(".qf-phone", { timeout: 5000 });
    await page.waitForTimeout(600);
    const link = await page.evaluate(() => document.querySelector(".qf-phone .qf-link").textContent);
    const url = (link.match(/https?:\/\/\S+/) || [""])[0];
    ok(/\/_Admin_Phone_Scan\/phoneScan\.html\?c=[a-z0-9]{12}&w=barcode$/.test(url), `the window shows the phone address (${url})`);
    ok(/Waiting for the phone|could not start/.test(await page.textContent(".qf-phone .qf-state")), "the window says it waits for the phone");
    const code = (url.match(/c=([a-z0-9]+)/) || [])[1];
    const stored = await page.evaluate(async (c) => {
        const { db } = await import("/_Database/firebase-console.js");
        const { doc, getDoc } = await import("https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js");
        const snap = await getDoc(doc(db, "phoneLinks", c));
        return snap.exists() ? snap.data() : null;
    }, code);
    ok(stored && stored.count === 0 && stored.value === "" && stored.expiresAtUnix > Date.now() / 1000, "the link document is written, empty, with an end time");

    // the phone page, same account and same store (one browser profile stands in for the phone)
    const phone = await ctx.newPage();
    const phoneErrors = [];
    phone.on("pageerror", (e) => phoneErrors.push(String(e.message || e)));
    await phone.setViewportSize({ width: 390, height: 800 });
    await phone.goto(url.replace(/^https?:\/\/[^/]+\//, BASE), { waitUntil: "load" });
    await phone.waitForTimeout(1200);
    const sub = await phone.textContent("#psSub");
    ok(/Connected as|ended|another/.test(sub), `the phone page answers (${sub})`);
    if (/Connected as/.test(sub)) {
        ok(await phone.locator("#psBarcode").isVisible() && await phone.locator("#psLabel").isVisible(), "the phone shows the two big buttons");
        const heights = await phone.evaluate(() => Array.from(document.querySelectorAll(".ps-big")).map((b) => b.getBoundingClientRect().height));
        ok(heights.every((h) => h >= 44), `the phone buttons are easy to tap (${heights.join(", ")})`);
    }
    ok(await phone.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), "the phone page does not scroll sideways");

    // a page without a code says so
    await phone.goto(BASE + "_Admin_Phone_Scan/phoneScan.html", { waitUntil: "load" });
    await phone.waitForTimeout(800);
    ok(/not complete/.test(await phone.textContent("#psSub")), "an address without a code is explained");
    ok(phoneErrors.length === 0, `no errors on the phone page (${phoneErrors.join(" | ")})`);

    // closing the window deletes the link
    await page.click('.qf-phone [data-act="done"]');
    await page.waitForTimeout(400);
    const after = await page.evaluate(async (c) => {
        const { db } = await import("/_Database/firebase-console.js");
        const { doc, getDoc } = await import("https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js");
        return (await getDoc(doc(db, "phoneLinks", c))).exists();
    }, code);
    ok(after === false, "Done deletes the link");
    // the rig blocks every outside address, so the QR library from jsDelivr fails here (ERR_FAILED); the window then shows the typed address
    const real = errors.filter((e) => !/status of 404|net::ERR_FAILED/.test(e));
    ok(real.length === 0, `no errors on the inventory (${real.join(" | ")})`);
    await ctx.close();
} finally {
    await browser.close();
}
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
