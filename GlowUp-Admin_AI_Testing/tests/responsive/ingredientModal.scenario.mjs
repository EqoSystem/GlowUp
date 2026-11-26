


import { launch, openPage } from "./harness.mjs";
import fs from "node:fs";
import path from "node:path";

let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };
const SHOTS = process.env.SHOTS || "";
if (SHOTS) fs.mkdirSync(SHOTS, { recursive: true });


async function layoutProblems(page) {
    return page.evaluate(() => {
        const modal = document.querySelector("#addIngredientModal .ing-modal");
        const body = document.getElementById("ingBody");
        const visible = (node) => { const r = node.getBoundingClientRect(); return r.width > 1 && r.height > 1 && node.checkVisibility({ contentVisibilityAuto: true, visibilityProperty: true, opacityProperty: false }); };

        const shown = (node) => {
            const r = node.getBoundingClientRect();
            if (!node.closest("#ingBody")) return r;
            const b = body.getBoundingClientRect();
            return { left: Math.max(r.left, b.left), right: Math.min(r.right, b.right), top: Math.max(r.top, b.top), bottom: Math.min(r.bottom, b.bottom) };
        };
        const list = Array.from(modal.querySelectorAll("a[href], button, input:not([type=hidden]), select, textarea, summary, label.check, label.fit-choice, .combo-btn"))
            .filter((node) => !(node.matches("input[type=radio], input[type=checkbox]")) && !node.matches("select.combo-native, .combo-native") && visible(node));
        const problems = [];
        const rect = (node) => node.getBoundingClientRect();
        for (let i = 0; i < list.length; i++) for (let j = i + 1; j < list.length; j++) {
            const a = list[i], b = list[j];
            if (a.contains(b) || b.contains(a)) continue;
            const x = shown(a), y = shown(b);
            const w = Math.min(x.right, y.right) - Math.max(x.left, y.left), h = Math.min(x.bottom, y.bottom) - Math.max(x.top, y.top);
            if (w > 1 && h > 1) problems.push(`overlap: ${a.id || a.className || a.tagName}:"${(a.textContent || "").trim().slice(0, 20)}" and ${b.id || b.className || b.tagName}:"${(b.textContent || "").trim().slice(0, 20)}"`);
        }
        // New21: the window slides in (about 0.4 s, 9 animations). Measuring the footer once during that slide made whole
        // columns of fields read "under the footer" on some runs. Finish the motion, then measure the footer per field.
        document.getAnimations().forEach((a) => { try { a.finish(); } catch (e) { } });
        let foot = modal.querySelector(".ing-foot").getBoundingClientRect(), head = modal.querySelector(".ing-nav").getBoundingClientRect();
        for (const node of list) {
            foot = modal.querySelector(".ing-foot").getBoundingClientRect(); head = modal.querySelector(".ing-nav").getBoundingClientRect();
            node.scrollIntoView({ block: "center" });
            const r = rect(node);
            if (r.bottom > foot.top + 1 && !node.closest(".ing-foot") && !node.closest(".ing-head, .ing-nav")) { node.scrollIntoView({ block: "nearest" }); }
            const r2 = rect(node);
            const cx = Math.min(Math.max(r2.left + r2.width / 2, 1), window.innerWidth - 1), cy = Math.min(Math.max(r2.top + r2.height / 2, 1), window.innerHeight - 1);
            const hit = document.elementFromPoint(cx, cy);
            if (!hit || !(node === hit || node.contains(hit) || hit.contains(node) || (hit.closest && hit.closest("label") && hit.closest("label").contains(node)))) {
                problems.push(`covered: ${node.id || node.className || node.tagName}:"${(node.textContent || "").trim().slice(0, 20)}" by ${hit ? (hit.id || hit.className || hit.tagName) : "nothing"}`);
            }
            if (!node.closest(".ing-foot") && r2.bottom > foot.top + 1 && r2.top < foot.bottom) problems.push(`under the footer: ${node.id || node.className}`);
            if (!node.closest(".ing-head, .ing-nav") && r2.top < head.bottom - 1 && r2.bottom > head.top) problems.push(`under the header: ${node.id || node.className}`);
        }
        body.scrollTop = 0;

        const mb = modal.getBoundingClientRect();
        modal.querySelectorAll("*").forEach((node) => {
            if (!visible(node) || node.closest(".ing-nav")) return;
            const r = node.getBoundingClientRect();
            if (r.width > 1 && (r.right > mb.right + 1 || r.left < mb.left - 1) && !node.closest(".sr-only")) problems.push(`pokes out sideways: ${node.id || node.className || node.tagName}`);
        });
        if (document.documentElement.scrollWidth > window.innerWidth + 1) problems.push("the page scrolls sideways");
        return { problems: Array.from(new Set(problems)).slice(0, 12), count: list.length };
    });
}

async function stress(page) {
    await page.fill("#ingredientNameInput", "Poly".padEnd(80, "methylsilsesquioxane"));
    await page.fill("#ingredientSourcesInput", "https://www.cir-safety.org/" + "very-long-path-segment-".repeat(20));
    await page.fill("#ingredientAliasesInput", Array.from({ length: 30 }, (_, i) => `Alias number ${i + 1}`).join(", "));
    await page.fill("#ingredientCautionInput", "x".repeat(160));
    await page.evaluate(() => {
        document.querySelectorAll("#addIngredientForm details.sources-box").forEach((d) => { d.open = true; });
        document.querySelectorAll("#addIngredientForm input[type=text], #addIngredientForm input[type=number], #addIngredientForm textarea, #addIngredientForm select").forEach((n) => n.setAttribute("aria-invalid", "true"));
        const line = document.querySelector("#addIngredientForm .form-error");
        line.textContent = "Each reference link must contain a web address that starts with https://  (" + "https://example.com/".repeat(5) + ")";
        line.style.display = "block";
    });
}

const browser = await launch();
try {
    const sizes = [["360", 360, 740, 1], ["768", 768, 900, 1], ["1024", 1024, 800, 1], ["1440", 1440, 900, 1], ["zoom200", 720, 450, 1], ["text125", 1024, 800, 1.25]];
    for (const theme of ["light", "dark"]) for (const [label, width, height, scale] of sizes) {
        const { page, errors, settle } = await openPage(browser, "cur", { width, height, theme });
        await page.route("https://pubchem.ncbi.nlm.nih.gov/**", (route) => route.fulfill({ status: 200, headers: { "access-control-allow-origin": "*" }, contentType: "application/json", body: "{}" }));
        await settle(500);
        if (scale !== 1) await page.evaluate((s) => document.documentElement.style.setProperty("--scale", String(s)), scale);
        await page.click("#addIngredientBtn");
        await page.waitForSelector("#addIngredientForm", { state: "visible" });
        const tag = `${theme} ${label}`;


        ok(await page.locator(".ing-modal > .ing-head, .ing-modal > .ing-nav, .ing-modal > .ing-form").count() === 3, `${tag}: header, section bar and form are the three parts of the window`);
        ok(await page.evaluate(() => { const m = document.querySelector(".ing-modal"); const f = document.querySelector(".ing-foot"); return getComputedStyle(f).position === "static" && f.parentElement.classList.contains("ing-form") && m.contains(f); }), `${tag}: the footer is a flex part, not laid over the body`);
        ok(await page.locator("#addIngredientModal .modal-actions").evaluate((n) => { const r = n.getBoundingClientRect(); return Math.abs(r.right - n.closest(".ing-modal").getBoundingClientRect().right) < 40; }), `${tag}: Cancel and Save are at the bottom right`);
        const order = await page.evaluate(() => Array.from(document.querySelectorAll(".ing-modal .modal-actions button")).map((b) => b.textContent.trim()));
        ok(order[0] === "Cancel" && order[1] === "Save Ingredient", `${tag}: Cancel is left of Save`);
        ok(await page.locator(".ing-nav-btn").count() === 3, `${tag}: three sections in the bar`);
        ok(await page.evaluate(() => Array.from(document.querySelectorAll(".ing-section-title")).map((h) => h.textContent).join("|") === "What app users read|Other names and sources|How the app judges it"), `${tag}: the three section names are kept`);

        ok(await page.evaluate(() => Array.from(document.querySelectorAll(".ing-modal .field")).every((f) => f.firstElementChild && f.firstElementChild.classList.contains("label-row"))), `${tag}: every field starts with a label row`);
        ok(await page.evaluate(() => Array.from(document.querySelectorAll(".ing-modal .label-row > label[for]")).every((l) => document.getElementById(l.htmlFor))), `${tag}: every label points at its box`);

        const sequence = await page.evaluate(() => Array.from(document.querySelectorAll("#secJudge .label-row > label, #secJudge .label-row > .label-text")).map((l) => l.textContent.replace(/\(optional\)/, "").trim()));
        ok(sequence.join("|") === "Risk Level|Minimum Age|Caution Note|Commonly Used In|Highlights|Allergen Family|Skin Types|Chronic Skin Conditions", `${tag}: order in the last section (got ${sequence.join("|")})`);

        const halves = await page.evaluate(() => Array.from(document.querySelectorAll(".ing-grid > .half")).map((n) => n.querySelector("label").textContent.trim().replace(/\s*\*$/, "")));
        ok(halves.join("|") === "INCI name|Ingredient classification|Risk Level|Minimum Age", `${tag}: only the short boxes share a row`);
        if (width < 768) ok(await page.evaluate(() => { const a = document.getElementById("ingredientNameInput").getBoundingClientRect(), b = document.getElementById("ingredientClassInputCombo") ? document.getElementById("ingredientClassInputCombo").getBoundingClientRect() : document.getElementById("ingredientClassInput").getBoundingClientRect(); return b.top >= a.bottom - 1; }), `${tag}: one column on a narrow screen`);

        ok(await page.evaluate(() => { const r = document.getElementById("ingredientNameInput").getBoundingClientRect(); return Math.abs(r.height - 40 * (() => { const g = document.createElement("div"); g.style.cssText = "position:absolute;visibility:hidden;width:calc(1000 * var(--u));height:1px"; document.body.appendChild(g); const u = g.getBoundingClientRect().width / 1000; g.remove(); return u; })()) < 1.5; }), `${tag}: a text box is 40 units high (40 px on a desktop)`);
        ok(await page.evaluate(() => document.getElementById("ingredientDescInput").getBoundingClientRect().height >= 95), `${tag}: a text area is at least 96 px high`);
        ok(await page.evaluate(() => Array.from(document.querySelectorAll(".label-actions .icon-btn")).every((b) => { const r = b.getBoundingClientRect(); return r.width >= 31.5 && Math.abs(r.width - r.height) < 0.6; })), `${tag}: icon buttons are square and at least 32 px`);
        ok(await page.evaluate(() => Array.from(document.querySelectorAll("#allergenLabel, #conditionsLabel")).every((l) => { const slot = l.parentElement.querySelector(".label-actions"); const kids = Array.from(slot.children); return kids.length === 2 && kids[0].classList.contains("info-slot") && kids[1].classList.contains("list-edit"); })), `${tag}: info icon then edit icon, at the right of the label row`);
        ok(await page.evaluate(() => Array.from(document.querySelectorAll(".fit-row")).every((r) => r.tagName === "FIELDSET" && r.querySelector("legend"))), `${tag}: each skin row is a fieldset with a legend`);
        ok(await page.evaluate(() => Array.from(document.querySelectorAll("#skinTypesFit .fit-choice span")).slice(0, 3).map((s) => s.textContent).join("|") === "Avoid|Neutral|Good"), `${tag}: Avoid | Neutral | Good (no bare dash)`);
        ok(await page.evaluate(() => { const rows = Array.from(document.querySelectorAll(".fit-row")).map((r) => Math.round(r.getBoundingClientRect().height)); return Math.max(...rows) - Math.min(...rows) <= 2 || window.innerWidth < 768; }), `${tag}: the skin rows have one height`);

        ok(await page.locator('[data-field-sources="purpose"]:visible').count() === 0, `${tag}: no sources before a name is typed`);
        ok(await page.locator('[data-field-sources="name"], [data-field-sources="classification"], [data-field-sources="allergens"]').count() === 0, `${tag}: no Sources row for the INCI name, the classification and the allergen family`);
        await page.fill("#ingredientNameInput", "Sodium Hyaluronate");
        for (const field of ["purpose", "description", "chemicalDescription", "aliases", "sources", "cautionNote", "commonUses"]) {
            ok(await page.locator(`[data-field-sources="${field}"] details.sources-box`).count() === 1, `${tag}: one Sources row under ${field}`);
        }
        ok(await page.locator("details.sources-box[open]").count() === 0, `${tag}: Sources rows start closed`);
        ok(/^Sources \(\d+\)$/.test((await page.textContent('[data-field-sources="purpose"] summary')).trim()), `${tag}: the row reads Sources (N)`);
        ok(await page.locator('[data-field-sources="judgment"]:visible').count() === 1, `${tag}: the admin-judgment line is shown once`);


        let result = await layoutProblems(page);
        ok(result.problems.length === 0, `${tag}: no overlap, nothing covered, nothing under the header or footer (${result.count} controls) ${result.problems.join(" ; ")}`);
        if (SHOTS) await page.screenshot({ path: path.join(SHOTS, `${theme}-${label}-plain.png`) });
        await stress(page);
        result = await layoutProblems(page);
        ok(result.problems.length === 0, `${tag}: worst case (long name and link, 30 aliases, every box in error, all Sources open): ${result.problems.join(" ; ")}`);
        if (SHOTS) {
            await page.evaluate(() => { document.getElementById("ingBody").scrollTop = 0; });
            await page.screenshot({ path: path.join(SHOTS, `${theme}-${label}-worst-top.png`) });
            await page.evaluate(() => { const b = document.getElementById("ingBody"); b.scrollTop = b.scrollHeight; });
            await page.screenshot({ path: path.join(SHOTS, `${theme}-${label}-worst-bottom.png`) });
        }
        ok(errors.length === 0, `${tag}: no page errors ${errors.join("; ")}`);
        await page.context().close();
    }


    // Quick fill's skin decisions (26 Nov 2026): fills on leaving the name box, gives a reason per group, takes its ticks back, saves nothing.
    for (const width of [1100, 390]) {
        const { page, errors, settle } = await openPage(browser, "cur", { width, height: 800 });
        await settle(400);
        const cors = { "access-control-allow-origin": "*" };
        await page.route("https://pubchem.ncbi.nlm.nih.gov/rest/**", (route) => {
            const url = route.request().url();
            if (url.includes("pug_view")) return route.fulfill({ status: 200, headers: cors, contentType: "application/json", body: JSON.stringify({ Record: { Information: [{ String: "H317 (61.7%): May cause an allergic skin reaction" }] } }) });
            if (url.includes("synonyms")) return route.fulfill({ status: 200, headers: cors, contentType: "application/json", body: JSON.stringify({ InformationList: { Information: [{ CID: 999, Synonym: ["Testolinum"] }] } }) });
            return route.fulfill({ status: 404, headers: cors, body: "" });
        });
        await page.click("#addIngredientBtn");
        await page.waitForSelector("#addIngredientForm", { state: "visible" });
        await page.fill("#ingredientNameInput", "Testolinum");
        await page.keyboard.press("Enter");
        await page.waitForSelector("#conditionsWhy p", { state: "attached", timeout: 8000 });
        const picked = () => page.evaluate(() => Array.from(document.querySelectorAll("#skinTypesFit .fit-row, #conditionsFit .fit-row")).filter((r) => r.querySelector('input[value="avoid"]:checked')).map((r) => r.dataset.value).join());
        ok(await picked() === "Sensitive,allergiccontact,contact", `${width}: H317 ticks Sensitive and allergic contact dermatitis (got ${await picked()})`);
        const why = await page.evaluate(() => ["allergenWhy", "skinTypesWhy", "conditionsWhy", "riskWhy"].map((id) => document.getElementById(id).textContent));
        ok(/Not filled/.test(why[0]) && /H317/.test(why[1]) && /H317/.test(why[2]) && /Not filled/.test(why[3]), `${width}: every group says why`);
        ok(await page.locator("#skinTypesWhy a.source-link[target=_blank]").count() === 1, `${width}: the reason links its page`);
        ok(/Nothing is saved yet/.test(await page.textContent("#ingredientQuickStatus")), `${width}: the summary says nothing is saved`);
        const found = await layoutProblems(page);
        ok(found.problems.length === 0, `${width}: filled form layout ${found.problems.join("; ")}`);
        if (SHOTS) { await page.locator("#skinTypesWhy").scrollIntoViewIfNeeded(); await page.screenshot({ path: path.join(SHOTS, `quickfill-${width}.png`) }); await page.evaluate(() => { document.getElementById("ingBody").scrollTop = 0; }); await page.screenshot({ path: path.join(SHOTS, `quickfill-${width}-top.png`) }); }
        await page.fill("#ingredientNameInput", "Other name");
        ok(await picked() === "" && await page.textContent("#skinTypesWhy") === "", `${width}: a new name takes the ticks and reasons back`);
        ok(errors.filter((e) => !/404/.test(e)).length === 0, `${width}: no page errors ${errors.join("; ")}`);
        await page.context().close();
    }

    {
        const { page, errors, settle } = await openPage(browser, "cur", { width: 1100, height: 800 });
        await settle(400);
        await page.route("https://pubchem.ncbi.nlm.nih.gov/rest/**", (route) => route.request().url().includes("Nonsensename")
            ? route.fulfill({ status: 404, contentType: "text/plain", body: "" })
            : route.fulfill({ status: 200, contentType: "text/plain", headers: { "access-control-allow-origin": "*" }, body: "753\n" }));
        await page.click("#addIngredientBtn");
        await page.waitForSelector("#addIngredientForm", { state: "visible" });
        await page.fill("#ingredientNameInput", "Glycerin");


        await page.click('.ing-nav-btn[data-jump="secJudge"]');
        await page.waitForTimeout(700);
        ok(await page.locator('.ing-nav-btn[data-jump="secJudge"][aria-current="true"]').count() === 1, "clicking a section in the bar marks it");
        ok(await page.evaluate(() => { const a = document.getElementById("secJudgeTitle").getBoundingClientRect().top, b = document.getElementById("ingBody").getBoundingClientRect().top; return a >= b - 1 && a < b + 120; }), "and the section's title is at the top of the body, not hidden");
        await page.evaluate(() => { document.getElementById("ingBody").scrollTop = 0; });
        await page.waitForTimeout(200);
        ok(await page.locator('.ing-nav-btn[data-jump="secUsers"][aria-current="true"]').count() === 1, "scrolling back up moves the mark back");


        const purpose = '[data-field-sources="purpose"]';
        await page.click(`${purpose} summary`);
        await page.type("#ingredientNameInput", "x");
        ok(await page.locator(`${purpose} details[open]`).count() === 1, "an open Sources row stays open while the name changes");
        await page.fill("#ingredientNameInput", "Glycerin");

        const rows = await page.evaluate((sel) => Array.from(document.querySelectorAll(sel + " .lookup-row")).map((r) => ({ name: r.querySelector(".source-link").textContent, look: r.querySelector(".lookup-text").textContent.length > 15, ref: r.querySelector(".ref-add").disabled })), purpose);
        ok(rows.length === 2 && rows.every((r) => r.look), "each row has the source and what to look for");
        ok(rows.find((r) => r.name === "EU CosIng").ref === true && rows.find((r) => r.name === "COSMILE Europe").ref === true, "+ ref is off for a landing or a search page");

        const hrefs = await page.evaluate(() => Array.from(document.querySelectorAll("#addIngredientForm a.source-link")).map((a) => ({ href: a.href, rel: a.rel, target: a.target })));
        ok(hrefs.length > 10 && hrefs.every((h) => /^https:\/\//.test(h.href) && !/google|bing|duckduckgo/.test(h.href) && h.target === "_blank" && /noopener/.test(h.rel)), "all links: https, no search engine, new tab, noopener");
        ok(hrefs.some((h) => h.href === "https://cosmileeurope.eu/inci/results/?q=Glycerin"), "COSMILE opens its own results for the name");

        await page.evaluate(() => { window.__copied = ""; Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText: (t) => { window.__copied = t; return Promise.resolve(); } } }); document.querySelectorAll('a.source-link[data-source]').forEach((a) => a.addEventListener("click", (e) => e.preventDefault())); });
        await page.click(`${purpose} a[data-source="cosing"]`);
        ok(await page.evaluate(() => window.__copied) === "Glycerin", "a landing page copies the INCI name");
        ok(await page.locator(".toast", { hasText: "INCI name copied. Paste it in the site's search box." }).count() >= 1, "and the toast says so");

        const toastsBefore = await page.locator(".toast").count();
        await page.evaluate(() => { window.__copied = ""; });
        for (let i = 0; i < 5; i++) await page.click(`${purpose} a[data-source="cosing"]`);
        ok(await page.evaluate(() => window.__copied) === "", "repeated taps within 2 seconds do nothing more");
        ok(await page.locator(".toast").count() === toastsBefore, "and add no more messages");
        ok(await page.locator(`${purpose} a[data-source="cosing"].is-wait`).count() === 1, "the link shows it is waiting");
        await page.evaluate(() => { window.__copied = ""; });
        await page.click(`${purpose} a[data-source="cosmile"]`);
        ok(await page.evaluate(() => window.__copied) === "", "a search link copies nothing");

        const chem = '[data-field-sources="chemicalDescription"]';
        await page.click(`${chem} summary`);
        await page.locator(`${chem} [data-add-ref="pubchem"]`).click();
        await page.waitForFunction(() => document.getElementById("ingredientSourcesInput").value.length > 0, null, { timeout: 4000 });
        ok(await page.inputValue("#ingredientSourcesInput") === "PubChem (NIH) | https://pubchem.ncbi.nlm.nih.gov/compound/753", "+ ref adds the compound page, labelled");
        await page.locator(`${chem} [data-add-ref="pubchem"]`).click();
        await page.waitForTimeout(400);
        ok((await page.inputValue("#ingredientSourcesInput")).split("\n").length === 1, "the same page is not added twice");
        await page.waitForTimeout(2100);
        await page.fill("#ingredientNameInput", "Nonsensename");
        await page.locator(`${chem} [data-add-ref="pubchem"]`).click();
        await page.waitForTimeout(500);
        ok((await page.inputValue("#ingredientSourcesInput")).split("\n").length === 1, "a name PubChem does not know adds nothing");
        ok(await page.locator(".toast", { hasText: "no page for this exact name" }).count() >= 1, "and says so");
        await page.fill("#ingredientNameInput", "Glycerin");

        const refs = '[data-field-sources="sources"]';
        await page.click(`${refs} summary`);
        { const hrefs = await page.locator(`${refs} .lookup-row a.source-link`).evaluateAll((as) => as.map((a) => a.href)); ok(hrefs.length >= 5 && new Set(hrefs).size === hrefs.length, `Reference links lists each source once (${hrefs.length} rows, ${new Set(hrefs).size} different)`); }
        ok(/Used for:/.test(await page.textContent(`${refs} .lookup-text`)), "with what it is used for");

        await page.fill("#ingredientSourcesInput", "https://www.cir-safety.org/ingredients\nMy note | https://example.com/x\nhttps://unknown.example/y");
        await page.focus("#ingredientAliasesInput");
        ok(await page.inputValue("#ingredientSourcesInput") === "Cosmetic Ingredient Review | https://www.cir-safety.org/ingredients\nMy note | https://example.com/x\nhttps://unknown.example/y", "a bare address gets its label when the box is left");


        await page.fill("#ingredientNameInput", "");
        await page.evaluate(() => { const line = document.querySelector("#addIngredientForm .form-error"); document.getElementById("ingredientNameInput").focus(); line.textContent = "Please enter the ingredient name."; line.style.display = "block"; });
        await page.waitForTimeout(300);
        ok(await page.locator("#addIngredientForm .form-error").isVisible(), "a failed save shows its message");
        ok(await page.locator("#addIngredientForm [aria-invalid=true]").count() === 1, "the box that failed is marked invalid");
        ok(await page.evaluate(() => { const r = document.activeElement.getBoundingClientRect(), b = document.getElementById("ingBody").getBoundingClientRect(); return r.top >= b.top - 1 && r.bottom <= b.bottom + 1; }), "and it is in view inside the body");
        await page.fill("#ingredientNameInput", "Glycerin");
        ok(await page.locator("#addIngredientForm [aria-invalid=true]").count() === 0, "typing clears the mark");


        await page.click(".ing-head h3");
        await page.evaluate(() => { document.getElementById("ingBody").scrollTop = 0; document.querySelector(".ing-close").focus(); });
        const trail = [];
        for (let i = 0; i < 260; i++) {
            await page.keyboard.press("Tab");
            const info = await page.evaluate(() => { const a = document.activeElement; const r = a.getBoundingClientRect(); return { inside: !!a.closest("#addIngredientModal"), top: Math.round(r.top + document.getElementById("ingBody").scrollTop), where: a.closest(".ing-body") ? "body" : a.closest(".ing-nav") ? "nav" : a.closest(".ing-foot") ? "foot" : "head", id: a.id || a.className }; });
            trail.push(info);
            if (!info.inside) break;
        }
        ok(trail.every((t) => t.inside), "Tab never leaves the window (it goes round)");
        ok(trail.some((t) => t.where === "foot"), "Tab reaches Save and Cancel");
        const inBody = trail.filter((t) => t.where === "body");
        let backwards = 0;
        for (let i = 1; i < inBody.length; i++) if (inBody[i].top < inBody[i - 1].top - 8 && inBody[i].where === "body") backwards++;
        ok(backwards <= 8, `the tab order follows the page top to bottom (${backwards} small jumps back, from boxes sharing a row or the wrap round)`);
        // New21: after 90 Tabs the focus can sit inside an open list (how many controls there are depends on how fast the
        // Sources rows load), and Esc then rightly closes that list first. Put the focus on the window itself, then check.
        await page.click(".ing-head h3");
        await page.keyboard.press("Escape");
        const closed = await page.waitForFunction(() => getComputedStyle(document.getElementById("addIngredientModal")).display === "none", null, { timeout: 2000 }).then(() => true, () => false);
        ok(closed, "Esc closes the window");
        ok(errors.filter((e) => !/404/.test(e)).length === 0, "no page errors in the behaviour checks " + errors.join("; "));
        await page.context().close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
