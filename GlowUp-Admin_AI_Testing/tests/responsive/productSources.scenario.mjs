// New sources in the Add Product form (23 Nov 2026): UPCitemdb when Open Beauty Facts does not know a barcode (through
// /api/upc-lookup), and "Find shades online" (Makeup API). Every outside answer is made up by the test.
import { launch, openPage } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };

const CODE = "041554433005";
const UPC_ITEM = { title: "Maybelline Color Sensational Vivid Matte Liquid Lip Colour Mini", brand: "Maybelline", category: "Health & Beauty > Cosmetics > Makeup > Lip Makeup > Lipstick",
    ean: "0041554433005", upc: CODE, images: ["https://img.example/y.jpg"] };
const SHADES = [
    { name: "Maybelline Color Sensational Vivid Matte Liquid Lip Colour", category: "lipstick", product_type: "lipstick",
      product_colors: [{ hex_value: "#AC7773", colour_name: "Nude Thrill " }, { hex_value: "#BD7A80", colour_name: "Nude Flush " }, { hex_value: "#C41E3A", colour_name: "645 Red Revival" }] },
    { name: "Maybelline Superstay Matte Ink", category: "lipstick", product_type: "lipstick", product_colors: [{ hex_value: "#8B2E3F", colour_name: "Lover" }] }
];

async function answerNetwork(ctx, asked) {
    const json = (route, status, body) => route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body), headers: { "access-control-allow-origin": "*" } });
    await ctx.route("https://world.openbeautyfacts.org/**", (route) => json(route, 404, {}));
    await ctx.route("**/api/upc-lookup", async (route) => {
        const body = JSON.parse(route.request().postData() || "{}");
        asked.upc.push({ code: body.code, auth: route.request().headers().authorization || "" });
        return body.code === CODE ? json(route, 200, { found: 1, items: [UPC_ITEM] }) : json(route, 429, { error: "The free UPCitemdb look-ups for today are used up (100 a day)." });
    });
    await ctx.route("https://makeup-api.herokuapp.com/**", (route) => { asked.shades.push(route.request().url()); return json(route, 200, SHADES); });
}

const browser = await launch();
try {
    for (const [label, width, height] of [["desktop", 1280, 860], ["phone", 390, 800]]) {
        const { page, ctx, errors, settle } = await openPage(browser, "inv", { width, height, touch: label === "phone" });
        const asked = { upc: [], shades: [] };
        await answerNetwork(ctx, asked);
        await settle(500);
        await page.evaluate(() => document.getElementById("addMenuBtn").click());
        await page.waitForTimeout(200);
        await page.locator(".menu-item, [role=menuitem]").filter({ hasText: "Product" }).first().click();
        await page.waitForSelector("#productForm", { state: "visible" });

        // a mistyped barcode (wrong last digit) never reaches UPCitemdb
        await page.fill("#productQuickInput", "3600531652300");
        await page.click("#productQuickFind");
        await page.waitForFunction(() => /Nothing is known/.test(document.getElementById("productQuickStatus").textContent), null, { timeout: 8000 }).catch(() => {});
        ok(asked.upc.length === 0, `${label}: a barcode with a wrong check digit is not sent to UPCitemdb`);
        ok(/last digit does not match/.test(await page.textContent("#productQuickStatus")), `${label}: the status says the barcode's last digit is wrong`);
        await page.fill("#productBarcodeInput", "");

        // Open Beauty Facts does not know it, UPCitemdb does
        await page.fill("#productQuickInput", CODE);
        await page.click("#productQuickFind");
        await page.waitForFunction(() => document.getElementById("productNameInput").value !== "", null, { timeout: 8000 }).catch(() => {});
        const form = await page.evaluate(() => ({ name: document.getElementById("productNameInput").value, type: document.getElementById("productTypeInput").value,
            barcode: document.getElementById("productBarcodeInput").value, brand: document.getElementById("productBrandInput").value,
            from: document.querySelector(".qf-hit .qf-from") ? document.querySelector(".qf-hit .qf-from").textContent : "",
            chip: (document.getElementById("productNameInput").closest(".field").querySelector(".origin-chip") || {}).textContent || "" }));
        ok(asked.upc.length === 1 && asked.upc[0].code === CODE && asked.upc[0].auth === "Bearer test-token", `${label}: UPCitemdb is asked through the site's function, with the admin's sign-in`);
        ok(form.name === "Color Sensational Vivid Matte Liquid Lip Colour Mini", `${label}: the name comes from UPCitemdb, brand taken off (${form.name})`);
        ok(form.brand === "maybelline" && form.type === "Lipstick" && form.barcode === CODE, `${label}: brand matched to the inventory, type and barcode filled`);
        ok(/UPCitemdb/.test(form.from) && /UPCitemdb/.test(form.chip), `${label}: the result and the field say UPCitemdb`);
        ok(/not the ingredient list/.test(await page.textContent("#productQuickResults")), `${label}: the result says UPCitemdb has no ingredient list`);

        // a second barcode: UPCitemdb's daily limit is explained
        await page.fill("#productQuickInput", "4006381333931");
        await page.click("#productQuickFind");
        await page.waitForFunction(() => /100 a day/.test(document.getElementById("productQuickStatus").textContent), null, { timeout: 8000 }).catch(() => {});
        ok(/100 a day/.test(await page.textContent("#productQuickStatus")), `${label}: the UPCitemdb daily limit is explained`);

        // Find shades online
        await page.locator("#productShadeFind").scrollIntoViewIfNeeded();
        await page.click("#productShadeFind");
        await page.waitForSelector(".shade-find", { timeout: 6000 }).catch(() => {});
        ok(asked.shades.length === 1 && /brand=maybelline&product_type=lipstick/.test(asked.shades[0]), `${label}: the Makeup API is asked for the brand and kind (${asked.shades[0] || "nothing"})`);
        const dialog = await page.evaluate(() => {
            const box = document.querySelector(".shade-find");
            if (!box) return null;
            const r = box.getBoundingClientRect();
            return { first: box.querySelector(".shade-find-product b").textContent, rows: box.querySelectorAll(".shade-find-row").length,
                ticked: box.querySelectorAll(".shade-find-row input:checked").length, fits: r.left >= -1 && r.right <= window.innerWidth + 1 && document.documentElement.scrollWidth <= window.innerWidth + 1 };
        });
        ok(dialog && /Vivid Matte/.test(dialog.first), `${label}: the closest product is offered first`);
        ok(dialog && dialog.rows === 3 && dialog.ticked === 3, `${label}: its shades are listed and ticked (form had none)`);
        ok(dialog && dialog.fits, `${label}: the shade window fits the screen`);
        await page.locator(".shade-find-row input").nth(1).uncheck();
        await page.locator('.shade-find [data-act="add"]').click();
        await page.waitForTimeout(300);
        const after = await page.evaluate(() => ({ swatches: document.querySelectorAll("#productShadeEditor .shade-hex").length,
            codes: Array.from(document.querySelectorAll("#productShadeEditor input")).map((i) => i.value).join("|"),
            chip: (document.getElementById("productShadeEditor").closest(".field").querySelector(".origin-chip") || {}).textContent || "",
            status: document.getElementById("productShadeFindStatus").textContent }));
        ok(/#AC7773/.test(after.codes) && /#C41E3A/.test(after.codes) && !/#BD7A80/.test(after.codes), `${label}: only the ticked colours are added (${after.codes.slice(0, 120)})`);
        ok(/\b645\b/.test(after.codes), `${label}: the code printed in front of a shade name goes in the Code box`);
        ok(/Needs review/.test(after.chip) && /Makeup API/.test(after.chip), `${label}: the shades are marked Needs review, from the Makeup API (${after.chip})`);
        ok(/2 shades added/.test(after.status), `${label}: the status says what was added`);

        // the save keeps the shade names, and asks first because values are not checked
        await page.fill("#productIngredientsInput", "Dimethicone, Mica, Talc, Glycerin");
        await page.selectOption("#productStatusInput", "Pending");
        await page.click("#saveProductBtn");
        await page.waitForTimeout(800);
        const saved = await page.evaluate(() => { for (const [k, v] of window.__STORE) if (k.startsWith("products/") && v.name === "Color Sensational Vivid Matte Liquid Lip Colour Mini") return JSON.parse(JSON.stringify(v)); return null; });
        ok(saved && saved.shades.length === 2 && saved.shadeNames.join("|") === "Nude Thrill|645 Red Revival", `${label}: shade names are saved next to the colours (${saved && saved.shadeNames})`);
        ok(saved && saved.fieldOrigins.shades.state === "review" && saved.fieldOrigins.name.from === "UPCitemdb", `${label}: the origins are saved`);

        const real = errors.filter((e) => !/status of (404|429)/.test(e));
        ok(real.length === 0, `${label}: no page errors (${real.join(" | ")})`);
        await ctx.close();
    }
} finally {
    await browser.close();
}
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
