// Browser check of the "Find it yourself" guide and the paste tidying in the Ingredient form (2026-11-10).
//   Run: node tests/responsive/fieldSources.scenario.mjs
import { launch, openPage } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };
const browser = await launch();
try {
    for (const width of [1440, 390]) {
        const { page, errors, settle } = await openPage(browser, "cur", { width, height: 900 });
        await settle(800);
        await page.click("#addIngredientBtn");
        await page.waitForSelector("#addIngredientForm", { state: "visible" });
        ok(await page.locator('[data-field-sources="judgment"]:visible').count() >= 3, `${width}px: the judgment helper shows under the judgment boxes`);
        ok(await page.locator('[data-field-sources="name"]:visible').count() === 0, `${width}px: no source links before a name is typed`);
        await page.fill("#ingredientNameInput", "Sodium Hyaluronate");
        for (const field of ["name", "classification", "purpose", "description", "chemicalDescription", "aliases", "cautionNote", "commonUses", "allergens"]) {
            ok(await page.locator(`[data-field-sources="${field}"] details.how-to-fill`).count() === 1, `${width}px: guide under ${field}`);
        }
        ok(await page.locator('[data-field-sources="sources"]:visible').count() === 1, `${width}px: helper under Reference links`);
        // the guide stays open while the name is edited
        await page.locator('[data-field-sources="purpose"] summary').click();
        await page.type("#ingredientNameInput", "x");
        ok(await page.locator('[data-field-sources="purpose"] details[open]').count() === 1, `${width}px: an open guide stays open while typing`);
        await page.fill("#ingredientNameInput", "Sodium Hyaluronate");
        ok(/PRESERVATIVE|Function/.test(await page.textContent('[data-field-sources="purpose"] details')), `${width}px: the CosIng instruction is there`);
        // + ref
        await page.locator('[data-field-sources="chemicalDescription"] [data-add-ref="pubchem"]').click();
        ok((await page.inputValue("#ingredientSourcesInput")).startsWith("PubChem (NIH) | https://pubchem.ncbi.nlm.nih.gov/"), `${width}px: + ref adds "Label | https://..."`);
        await page.locator('[data-field-sources="aliases"] [data-add-ref="pubchem"]').click();
        ok((await page.inputValue("#ingredientSourcesInput")).split("\n").length === 1, `${width}px: the same source is not added twice`);
        await page.locator('[data-field-sources="name"] [data-add-ref="cosing"]').click();
        ok((await page.inputValue("#ingredientSourcesInput")).split("\n").length === 2, `${width}px: a second source goes on a new line`);
        // paste
        const paste = (selector, text) => page.evaluate(([sel, value]) => {
            const box = document.querySelector(sel); box.focus();
            const data = new DataTransfer(); data.setData("text", value);
            box.dispatchEvent(new ClipboardEvent("paste", { clipboardData: data, bubbles: true, cancelable: true }));
        }, [selector, text]);
        await page.fill("#ingredientPurposeInput", "");
        await paste("#ingredientPurposeInput", "  HUMECTANT\n");
        ok(await page.inputValue("#ingredientPurposeInput") === "HUMECTANT", `${width}px: purpose paste is trimmed`);
        ok((await page.textContent("#ingredientPurposeInput >> xpath=ancestor::div[contains(@class,'field')][1]")).includes("Looks like a function term"), `${width}px: function-term warning`);
        await page.type("#ingredientPurposeInput", "s");
        ok(await page.locator(".paste-note").count() === 0, `${width}px: the notice goes away when typing goes on`);
        await page.fill("#ingredientAliasesInput", "");
        await paste("#ingredientAliasesInput", "Water; Eau\nAqua, water");
        ok(await page.inputValue("#ingredientAliasesInput") === "Water, Eau, Aqua", `${width}px: alias list normalised`);
        await paste("#ingredientDescInput", "a ".repeat(200));
        ok((await page.textContent("#ingredientDescInput >> xpath=ancestor::div[contains(@class,'field')][1]")).includes("Large paste detected"), `${width}px: large paste notice`);
        await page.fill("#ingredientNameInput", "");
        await paste("#ingredientNameInput", "Aqua, Water");
        ok((await page.textContent("#ingredientNameInput >> xpath=ancestor::div[contains(@class,'field')][1]")).includes("one official name"), `${width}px: INCI list warning`);
        ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), `${width}px: no sideways scroll`);
        ok(errors.length === 0, `${width}px: no page errors ${errors.join("; ")}`);
        await page.close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
