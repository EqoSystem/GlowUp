// Browser check of the source link beside the Ingredient form's boxes and the Source Finder (10 November 2026).
//   Run: node tests/responsive/sourceLinks.scenario.mjs        (SHOTS=<folder> also saves screenshots of the three sections at every size)
import { launch, openPage } from "./harness.mjs";
import fs from "node:fs";
import path from "node:path";

let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };
const SHOTS = process.env.SHOTS || "";
if (SHOTS) fs.mkdirSync(SHOTS, { recursive: true });
const FIELDS = ["ingredientPurposeInput", "ingredientDescInput", "ingredientChemInput", "ingredientAliasesInput", "ingredientCautionInput", "ingredientUsesInput"];

async function overlaps(page) {
    return page.evaluate(() => {
        const modal = document.querySelector("#addIngredientModal .ing-modal"), body = document.getElementById("ingBody");
        const vis = (n) => { const r = n.getBoundingClientRect(); return r.width > 1 && r.height > 1 && n.checkVisibility({ contentVisibilityAuto: true, visibilityProperty: true }); };
        const shown = (n) => { const r = n.getBoundingClientRect(); if (!n.closest("#ingBody")) return r; const b = body.getBoundingClientRect(); return { left: Math.max(r.left, b.left), right: Math.min(r.right, b.right), top: Math.max(r.top, b.top), bottom: Math.min(r.bottom, b.bottom) }; };
        const list = Array.from(modal.querySelectorAll("a[href], button, input:not([type=hidden]), select, textarea, summary, label.check, .combo-btn, .counter"))
            .filter((n) => !n.matches("input[type=radio], input[type=checkbox], .combo-native") && vis(n));
        const out = [];
        for (let i = 0; i < list.length; i++) for (let j = i + 1; j < list.length; j++) {
            const a = list[i], b = list[j]; if (a.contains(b) || b.contains(a)) continue;
            const x = shown(a), y = shown(b);
            if (Math.min(x.right, y.right) - Math.max(x.left, y.left) > 1 && Math.min(x.bottom, y.bottom) - Math.max(x.top, y.top) > 1) out.push(`${a.id || a.className}/${b.id || b.className}`);
        }
        const foot = modal.querySelector(".ing-foot").getBoundingClientRect();
        for (const n of list) {
            if (n.closest(".ing-foot, .ing-head, .ing-nav") || n.classList.contains("counter")) continue;
            n.scrollIntoView({ block: "center" });
            const r = n.getBoundingClientRect();
            const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
            if (!hit || !(n === hit || n.contains(hit) || hit.contains(n) || (hit.closest("label") && hit.closest("label").contains(n)))) out.push("covered " + (n.id || n.className));
            if (r.bottom > foot.top + 1 && r.top < foot.bottom) out.push("under footer " + (n.id || n.className));
        }
        // counters stay inside their own row's hint line, left of the source column
        document.querySelectorAll(".has-src .counter").forEach((c) => {
            const row = c.closest(".field").getBoundingClientRect(), r = c.getBoundingClientRect(), box = c.closest(".field").querySelector("input, textarea").getBoundingClientRect();
            if (r.right > box.right + 1 || r.left < row.left - 1) out.push("counter outside its row " + c.id);
        });
        body.scrollTop = 0;
        return Array.from(new Set(out)).slice(0, 8);
    });
}

const browser = await launch();
try {
    for (const [label, width, height] of [["360", 360, 740], ["768", 768, 900], ["1024", 1024, 800], ["1440", 1440, 900], ["zoom200", 720, 450]]) {
        const { page, errors, settle } = await openPage(browser, "cur", { width, height });
        await settle(400);
        await page.click("#addIngredientBtn");
        await page.waitForSelector("#addIngredientForm", { state: "visible" });
        await page.fill("#ingredientNameInput", "Glycerin");
        ok(await page.locator(".field.has-src").count() === 6, `${label}: six boxes have a source link`);
        // layout: wide = source beside the box, top-aligned with it; narrow = under it, above the hint
        const geo = await page.evaluate(() => {
            const box = document.getElementById("ingredientPurposeInput").getBoundingClientRect(), src = document.getElementById("ingredientPurposeInputSource").getBoundingClientRect();
            const hint = document.querySelector("#ingredientPurposeInput ~ .hint-row").getBoundingClientRect();
            return { beside: src.left >= box.right, topDiff: Math.abs(src.top - box.top), under: src.top >= box.bottom - 1 && src.bottom <= hint.top + 1, srcH: src.height, wide: document.querySelector(".ing-modal").getBoundingClientRect().width >= 640 };
        });
        if (geo.wide) ok(geo.beside && geo.topDiff < 2, `${label}: wide window - the source box is beside the box, top-aligned (diff ${geo.topDiff})`);
        else ok(geo.under, `${label}: narrow window - the source box is under the box and above the hint`);
        ok(Math.abs(geo.srcH - 40) < 1.5, `${label}: the source box is 40 px high`);
        ok(await page.evaluate(() => document.getElementById("ingredientDescInputSource").getBoundingClientRect().height < 45), `${label}: beside a text area it stays one line (not stretched)`);
        // the header chip
        ok(/Sources: 0 of 0 fields/.test(await page.textContent("#ingredientSourceCount")), `${label}: header chip: no box has a value yet`);
        let problems = await overlaps(page);
        ok(problems.length === 0, `${label}: nothing overlaps or hides (plain) ${problems.join(" ; ")}`);
        // every kind of address, in every box
        const cases = [
            ["ingredientPurposeInput", "https://www.cir-safety.org/ingredient/glycerin", /Industry\/reference/, "valid CIR page"],
            ["ingredientDescInput", "https://www.google.com/search?q=glycerin", /search engine/, "Google search blocked"],
            ["ingredientChemInput", "https://cosmileeurope.eu/", /general page/, "home page warned"],
            ["ingredientAliasesInput", "javascript:alert(1)", /not allowed/, "javascript: blocked"],
            ["ingredientCautionInput", "https://example.com/" + "very-long-path-".repeat(30), /trusted sources/, "very long untrusted address"],
            ["ingredientUsesInput", "not-a-url", /whole address/, "invalid"]
        ];
        await page.fill("#ingredientPurposeInput", "Keeps skin moist.");
        await page.fill("#ingredientCautionInput", "can irritate");
        for (const [box, url, expect, what] of cases) {
            await page.fill(`#${box}Source`, url);
            const status = await page.textContent(`#${box}SourceStatus`);
            ok(expect.test(status), `${label}: ${what} (${status.slice(0, 60)})`);
        }
        ok(await page.evaluate(() => ["ingredientDescInputSource", "ingredientAliasesInputSource", "ingredientUsesInputSource"].every((id) => document.getElementById(id).getAttribute("aria-invalid") === "true")
            && document.getElementById("ingredientPurposeInputSource").getAttribute("aria-invalid") === "false"), `${label}: the three blocked ones are marked, the good one is not`);
        ok(/Sources: 2 of \d fields/.test(await page.textContent("#ingredientSourceCount")), `${label}: the chip counts saved-able sources`);
        // the finder, under the last row
        await page.locator("#ingredientUsesInputSource ~ .src-actions .src-find, .field:has(#ingredientUsesInput) .src-find").first().click();
        await page.waitForSelector(".src-finder:not([hidden])");
        ok(await page.evaluate(() => document.querySelector(".src-finder").closest(".field").contains(document.getElementById("ingredientUsesInput"))), `${label}: the finder opens in the page, under its own box`);
        ok(await page.locator(".src-finder .finder-row").count() >= 2, `${label}: the finder lists where to look`);
        ok(await page.evaluate(() => Array.from(document.querySelectorAll(".src-finder a")).every((a) => a.href.startsWith("https://") && !/google|bing|duckduckgo/.test(a.href) && a.target === "_blank")), `${label}: finder links go to the sites themselves, new tab`);
        await page.waitForTimeout(400);
        problems = await overlaps(page);
        ok(problems.length === 0, `${label}: worst case (all rows in error, finder open under the last row): ${problems.join(" ; ")}`);
        if (SHOTS) {
            for (const sec of ["secUsers", "secNames", "secJudge"]) {
                await page.evaluate((id) => document.getElementById(id).scrollIntoView({ block: "start" }), sec);
                await page.waitForTimeout(150);
                await page.screenshot({ path: path.join(SHOTS, `${label}-${sec}.png`) });
            }
        }
        // Esc closes the finder and gives the focus back
        await page.focus("#finderName");
        await page.keyboard.press("Escape");
        await page.waitForTimeout(150);
        ok(await page.locator(".src-finder:not([hidden])").count() === 0, `${label}: Esc closes the finder`);
        ok(await page.evaluate(() => document.activeElement && document.activeElement.classList.contains("src-find")), `${label}: and the focus is back on Find source`);
        ok(await page.evaluate(() => getComputedStyle(document.getElementById("addIngredientModal")).display !== "none"), `${label}: the window stays open`);
        // a blocked address stops the save
        await page.click("#saveIngredientBtn");
        await page.waitForTimeout(250);
        ok(/source link|TEST|test copy/i.test(await page.textContent("#addIngredientForm .form-error")), `${label}: a blocked source link is reported on save`);
        ok(errors.filter((e) => !/404/.test(e)).length === 0, `${label}: no page errors ${errors.join("; ")}`);
        await page.context().close();
    }
    // keyboard order in one row: box -> source -> Open -> Check link -> Find source
    {
        const { page, settle } = await openPage(browser, "cur", { width: 1200, height: 900 });
        await settle(400);
        await page.click("#addIngredientBtn");
        await page.fill("#ingredientNameInput", "Glycerin");
        await page.fill("#ingredientPurposeInputSource", "https://www.cir-safety.org/ingredient/glycerin");
        await page.focus("#ingredientPurposeInput");
        const seen = [];
        for (let i = 0; i < 4; i++) { await page.keyboard.press("Tab"); seen.push(await page.evaluate(() => document.activeElement.id || document.activeElement.className)); }
        ok(seen[0] === "ingredientPurposeInputSource" && /src-open/.test(seen[1]) && /src-check/.test(seen[2]) && /src-find/.test(seen[3]), `tab order box > source > Open > Check link > Find source (${seen.join(" > ")})`);
        await page.keyboard.press("Enter");
        await page.waitForTimeout(200);
        ok(await page.locator(".src-finder:not([hidden])").count() === 1, "Enter on Find source opens the finder");
        // the Find source button waits for a name
        await page.fill("#ingredientNameInput", "");
        ok(await page.locator(".field:has(#ingredientPurposeInput) .src-find").isDisabled(), "Find source is off without an INCI name");
        ok(/Enter the INCI name first/.test(await page.textContent(".field:has(#ingredientPurposeInput) .src-find-hint")), "and says why");
        await page.context().close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
