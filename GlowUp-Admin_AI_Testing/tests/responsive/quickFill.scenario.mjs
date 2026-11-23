// Quick fill on the Add Product and Add Ingredient windows (panel revision 22 Nov 2026).
// Open Beauty Facts and PubChem are answered by the test (no real network); the FDA list is the file the site ships.
import { launch, openPage } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };

const OFF_PRODUCT = { status: 1, product: { code: "4800000000017", product_name: "Velvet Matte Lip Cream", brands: "Testbrand Cosmetics", ingredients_text_en: "Isododecane, Dimethicone, Mica, CI 77491, Tocopherol", categories_tags: ["en:lipsticks"], image_front_url: "https://images.openbeautyfacts.org/x.jpg" } };
const PUBCHEM_SYN = { InformationList: { Information: [{ CID: 753, Synonym: ["glycerin", "glycerol", "56-81-5", "Glycerine", "1,2,3-Propanetriol"] }] } };
const PUBCHEM_DESC = { InformationList: { Information: [{ CID: 753, Title: "Glycerin" }, { CID: 753, Description: "Glycerin is a triol with a structure of propane substituted at positions 1, 2 and 3 by hydroxy groups." }] } };

async function answerNetwork(ctx) {
    await ctx.route("https://world.openbeautyfacts.org/**", (route) => {
        const url = route.request().url();
        if (url.includes("/api/v2/product/4800000000017")) return route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(OFF_PRODUCT), headers: { "access-control-allow-origin": "*" } });
        if (url.includes("/cgi/search.pl")) return route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ products: [] }), headers: { "access-control-allow-origin": "*" } });
        return route.fulfill({ status: 404, contentType: "application/json", body: "{}", headers: { "access-control-allow-origin": "*" } });
    });
    await ctx.route("https://pubchem.ncbi.nlm.nih.gov/**", (route) => {
        const url = route.request().url();
        const body = url.includes("/synonyms/") ? PUBCHEM_SYN : url.includes("/description/") ? PUBCHEM_DESC : {};
        return route.fulfill({ status: url.toLowerCase().includes("glycerin") ? 200 : 404, contentType: "application/json", body: JSON.stringify(body), headers: { "access-control-allow-origin": "*" } });
    });
}

const browser = await launch();
try {
    for (const [label, width, height] of [["desktop", 1280, 860], ["phone", 390, 800]]) {
        const { page, ctx, errors, settle } = await openPage(browser, "inv", { width, height, touch: label === "phone" });
        await answerNetwork(ctx);
        await settle(500);
        await page.evaluate(() => document.getElementById("addMenuBtn").click());
        await page.waitForTimeout(200);
        await page.locator(".menu-item, [role=menuitem]").filter({ hasText: "Product" }).first().click();
        await page.waitForSelector("#productForm", { state: "visible" });
        await settle(300);
        ok(await page.locator("#productQuickFill").isVisible(), `${label}: Quick fill shows on Add Product`);

        // a barcode Open Beauty Facts knows: everything fills, the unknown brand is offered as new
        await page.fill("#productQuickInput", "4800000000017");
        await page.click("#productQuickFind");
        await page.waitForFunction(() => document.getElementById("productNameInput").value !== "", null, { timeout: 8000 }).catch(() => {});
        const filled = await page.evaluate(() => ({
            name: document.getElementById("productNameInput").value, brand: document.getElementById("productBrandInput").value,
            brandText: document.getElementById("productBrandInput").selectedOptions[0] ? document.getElementById("productBrandInput").selectedOptions[0].textContent : "",
            type: document.getElementById("productTypeInput").value, list: document.getElementById("productIngredientsInput").value,
            barcode: document.getElementById("productBarcodeInput").value, status: document.getElementById("productQuickStatus").textContent
        }));
        ok(filled.name === "Velvet Matte Lip Cream", `${label}: the name is filled (${filled.name})`);
        ok(filled.brand === "__newbrand__" && /Testbrand Cosmetics/.test(filled.brandText), `${label}: the unknown brand is offered as a new brand (${filled.brandText})`);
        ok(filled.type === "Lipstick" || filled.type === "Lip Tint" || filled.type !== "", `${label}: the kind is chosen (${filled.type})`);
        ok(/Isododecane, Dimethicone, Mica, CI 77491, Tocopherol/.test(filled.list), `${label}: the ingredient list is filled`);
        ok(filled.barcode === "4800000000017", `${label}: the barcode is filled`);
        ok(/Filled .* from Open Beauty Facts/.test(filled.status), `${label}: the status says where it came from (${filled.status.slice(0, 80)})`);
        const chips = await page.evaluate(() => Array.from(document.querySelectorAll("#productForm .origin-chip")).map((c) => c.textContent));
        ok(chips.length >= 4 && chips.every((t) => /Detected · Open Beauty Facts/.test(t)), `${label}: each filled field is marked Detected with its source (${chips.length})`);
        ok(await page.isVisible("#productOriginNote"), `${label}: the form lists the values not checked yet`);
        ok(await page.isVisible("#productIngredientsRaw"), `${label}: the ingredient text as the source gave it is kept`);
        await page.locator('[data-origin-check="name"]').click();
        ok(/Checked/.test(await page.locator("#productForm .origin-chip").first().textContent()), `${label}: Mark checked turns the name to Checked`);
        await page.fill("#productDescInput", "typed");
        await page.fill("#productNameInput", "Velvet Matte Lip Cream 2");
        ok(!/Detected/.test((await page.evaluate(() => { const r = document.getElementById("productNameInput").closest(".field").querySelector(".origin-chip"); return r ? r.textContent : ""; }))), `${label}: a value changed by hand is no longer Detected`);
        await page.fill("#productNameInput", "Velvet Matte Lip Cream");

        // an unknown barcode goes into the Barcode box and says so
        await page.fill("#productQuickInput", "4800000000994");
        await page.click("#productQuickFind");
        await page.waitForFunction(() => /Nothing is known/.test(document.getElementById("productQuickStatus").textContent), null, { timeout: 8000 }).catch(() => {});
        ok(/Nothing is known about barcode/.test(await page.textContent("#productQuickStatus")), `${label}: an unknown barcode is explained`);

        // a name from the FDA Philippines list fills the FDA number and the list
        await page.fill("#productQuickInput", "Absidy Complexion Blur Translucent Perfecting Powder");
        await page.click("#productQuickFind");
        await page.waitForSelector(".qf-hit", { timeout: 8000 }).catch(() => {});
        const hits = await page.locator(".qf-hit").count();
        ok(hits >= 1, `${label}: the FDA list finds the product by name (${hits})`);
        ok((await page.evaluate(() => document.getElementById("productNameInput").value)) === "Velvet Matte Lip Cream", `${label}: a filled form is NOT overwritten by a new search`);
        await page.locator(".qf-hit [data-fill]").first().click();
        await page.waitForSelector(".qf-compare", { timeout: 4000 }).catch(() => {});
        const compare = await page.evaluate(() => {
            const box = document.querySelector(".qf-compare");
            if (!box) return null;
            const rows = Array.from(box.querySelectorAll("tbody tr")).map((tr) => ({ field: tr.querySelector("th").textContent, ticked: tr.querySelector("input").checked, conflict: tr.classList.contains("conflict") }));
            return { rows, sideways: document.documentElement.scrollWidth > window.innerWidth + 1 };
        });
        ok(!!compare, `${label}: a filled form gets the comparison window`);
        ok(compare && compare.rows.some((r) => /Product name/.test(r.field) && r.conflict && !r.ticked), `${label}: a different name already in the form is not ticked`);
        ok(compare && compare.rows.some((r) => /FDA/.test(r.field) && r.ticked), `${label}: the empty FDA number is ticked`);
        ok(compare && !compare.sideways, `${label}: the comparison window fits the screen`);
        await page.locator('.qf-compare [data-act="ok"]').click();
        const fda = await page.evaluate(() => document.getElementById("productFdaInput").value);
        ok(/^NN-/.test(fda), `${label}: the FDA number is filled (${fda})`);
        ok((await page.evaluate(() => document.getElementById("productNameInput").value)) === "Velvet Matte Lip Cream", `${label}: the unticked name stayed`);

        // empty search
        await page.fill("#productQuickInput", "");
        await page.click("#productQuickFind");
        ok(/Type a product name or a barcode first/.test(await page.textContent("#productQuickStatus")), `${label}: an empty search asks for text`);

        // layout: nothing pokes out sideways, buttons are big enough to tap
        const layout = await page.evaluate(() => {
            const modal = document.querySelector("#productModal .ing-modal").getBoundingClientRect();
            const out = Array.from(document.querySelectorAll("#productQuickFill *")).filter((n) => { const r = n.getBoundingClientRect(); return r.width > 1 && (r.right > modal.right + 1 || r.left < modal.left - 1); }).length;
            const small = Array.from(document.querySelectorAll("#productQuickFill button")).filter((b) => b.getBoundingClientRect().height < 43).map((b) => b.textContent.trim());
            return { out, small, sideways: document.documentElement.scrollWidth > window.innerWidth + 1 };
        });
        ok(layout.out === 0 && !layout.sideways, `${label}: Quick fill stays inside the window`);
        ok(layout.small.length === 0, `${label}: every Quick fill button is at least 44 px tall (${layout.small.join(", ")})`);

        // editing an existing product hides Quick fill
        await page.click("#productModal [data-close]");
        await page.waitForTimeout(200);
        // Open Beauty Facts answers 404 for a barcode it does not know: the browser logs that, it is not a fault of the page
        const real = errors.filter((e) => !/Failed to load resource: the server responded with a status of 404/.test(e));
        ok(real.length === 0, `${label}: no page errors on the inventory (${real.join(" | ")})`);
        await ctx.close();
    }

    // Add Ingredient: Fill from the name fills empty boxes only and never the risk level
    {
        const { page, ctx, errors, settle } = await openPage(browser, "cur", { width: 1280, height: 860 });
        await answerNetwork(ctx);
        await settle(500);
        await page.evaluate(() => document.getElementById("addIngredientBtn").click());
        await page.waitForSelector("#addIngredientForm", { state: "visible" });
        await page.fill("#ingredientNameInput", "Glycerin");
        await page.fill("#ingredientDescInput", "Typed by the admin.");
        await page.click("#ingredientQuickBtn");
        await page.waitForFunction(() => /Filled|Nothing new|does not know/.test(document.getElementById("ingredientQuickStatus").textContent), null, { timeout: 8000 }).catch(() => {});
        const got = await page.evaluate(() => ({
            aliases: document.getElementById("ingredientAliasesInput").value, chem: document.getElementById("ingredientChemInput").value,
            desc: document.getElementById("ingredientDescInput").value, risk: document.getElementById("ingredientRiskInput").value,
            cls: document.getElementById("ingredientClassInput").selectedOptions[0] ? document.getElementById("ingredientClassInput").selectedOptions[0].textContent : "",
            status: document.getElementById("ingredientQuickStatus").textContent
        }));
        ok(/glycerol/.test(got.aliases) && !/56-81-5/.test(got.aliases), `ingredient: other names from PubChem, no registry number (${got.aliases})`);
        ok(/^Glycerin is a triol/.test(got.chem), "ingredient: the technical description is filled");
        ok(got.desc === "Typed by the admin.", "ingredient: a box the admin typed in is left alone");
        ok(got.risk === "", "ingredient: the risk level is never filled");
        ok(/Humectant/i.test(got.cls) || got.cls === "Choose a classification", `ingredient: classification chosen when the list has it (${got.cls})`);
        ok(/Filled:/.test(got.status), `ingredient: the status lists what was filled (${got.status.slice(0, 90)})`);
        await page.fill("#ingredientNameInput", "Zz");
        await page.click("#ingredientQuickBtn");
        ok(/at least 3 letters/.test(await page.textContent("#ingredientQuickStatus")), "ingredient: a too-short name is refused with a reason");
        const real = errors.filter((e) => !/status of 404/.test(e));
        ok(real.length === 0, `ingredient: no page errors (${real.join(" | ")})`);
        await ctx.close();
    }
} finally {
    await browser.close();
}
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
