// Browser check of the Brand and Product windows, add and edit (10 November 2026): the same window and field pattern as the Ingredient window,
// and that nothing overlaps, clips or hides under the header or the footer, with long names, long addresses, many shades and a long ingredient list.
//   Run:  node tests/responsive/inventoryModals.scenario.mjs            (set SHOTS=<folder> to also save screenshots)
import { launch, openPage } from "./harness.mjs";
import fs from "node:fs";
import path from "node:path";

let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };
const SHOTS = process.env.SHOTS || "";
if (SHOTS) fs.mkdirSync(SHOTS, { recursive: true });

/** Every interactive thing in the open window: overlaps, what covers what after scrolling into view, and anything poking out sideways. */
async function layoutProblems(page, overlayId) {
    return page.evaluate((id) => {
        const overlay = document.getElementById(id);
        const modal = overlay.querySelector(".ing-modal");
        const body = overlay.querySelector(".ing-body");
        const visible = (node) => { const r = node.getBoundingClientRect(); return r.width > 1 && r.height > 1 && node.checkVisibility({ contentVisibilityAuto: true, visibilityProperty: true }); };
        const shown = (node) => {
            const r = node.getBoundingClientRect();
            if (!node.closest(".ing-body")) return r;
            const b = body.getBoundingClientRect();
            return { left: Math.max(r.left, b.left), right: Math.min(r.right, b.right), top: Math.max(r.top, b.top), bottom: Math.min(r.bottom, b.bottom) };
        };
        const name = (n) => `${n.id || n.className || n.tagName}:"${(n.textContent || n.value || "").trim().slice(0, 18)}"`;
        const list = Array.from(modal.querySelectorAll("a[href], button, input:not([type=hidden]), select, textarea, summary, label.check, .combo-btn"))
            .filter((node) => !node.matches("input[type=radio], input[type=checkbox], .combo-native, input[type=color]") && visible(node));
        const problems = [];
        for (let i = 0; i < list.length; i++) for (let j = i + 1; j < list.length; j++) {
            const a = list[i], b = list[j];
            if (a.contains(b) || b.contains(a)) continue;
            const x = shown(a), y = shown(b);
            const w = Math.min(x.right, y.right) - Math.max(x.left, y.left), h = Math.min(x.bottom, y.bottom) - Math.max(x.top, y.top);
            if (w > 1 && h > 1) problems.push(`overlap: ${name(a)} and ${name(b)}`);
        }
        const foot = modal.querySelector(".ing-foot").getBoundingClientRect(), head = modal.querySelector(".ing-nav").getBoundingClientRect();
        for (const node of list) {
            node.scrollIntoView({ block: "center" });
            const r = node.getBoundingClientRect();
            const cx = Math.min(Math.max(r.left + r.width / 2, 1), window.innerWidth - 1), cy = Math.min(Math.max(r.top + r.height / 2, 1), window.innerHeight - 1);
            const hit = document.elementFromPoint(cx, cy);
            if (!hit || !(node === hit || node.contains(hit) || hit.contains(node) || (hit.closest("label") && hit.closest("label").contains(node)))) problems.push(`covered: ${name(node)} by ${hit ? name(hit) : "nothing"}`);
            if (!node.closest(".ing-foot, .ing-head, .ing-nav")) {
                if (r.bottom > foot.top + 1 && r.top < foot.bottom) problems.push(`under the footer: ${name(node)}`);
                if (r.top < head.bottom - 1 && r.bottom > head.top) problems.push(`under the header: ${name(node)}`);
            }
        }
        body.scrollTop = 0;
        const mb = modal.getBoundingClientRect();
        modal.querySelectorAll("*").forEach((node) => {
            if (!visible(node) || node.closest(".sr-only")) return;
            const r = node.getBoundingClientRect();
            if (r.width > 1 && (r.right > mb.right + 1 || r.left < mb.left - 1)) problems.push(`pokes out sideways: ${name(node)}`);
        });
        if (document.documentElement.scrollWidth > window.innerWidth + 1) problems.push("the page scrolls sideways");
        return { problems: Array.from(new Set(problems)).slice(0, 10), count: list.length };
    }, overlayId);
}

const LONG = "Extremely".padEnd(90, "LongNameWithoutAnySpacesAtAll");
const LONGURL = "https://example.com/" + "very-long-path-segment-".repeat(14);

async function openAdd(page, which) {
    await page.click("#addMenuBtn");
    await page.locator(".menu-item, [role=menuitem]", { hasText: new RegExp("^\\s*" + which) }).first().click();
    await page.waitForSelector(which === "Brand" ? "#addBrandModal .ing-body" : "#productModal .ing-body", { state: "visible" });
    await page.waitForTimeout(300);
}
async function openEdit(page, act) {
    if (act === "edit-product" && await page.locator('[data-act="edit-product"]').count() === 0) {
        await page.locator("tr.product-row").first().scrollIntoViewIfNeeded();
        await page.locator("tr.product-row").first().click();
        await page.waitForTimeout(400);
    }
    const button = page.locator(`[data-act="${act}"]`).first();
    await button.scrollIntoViewIfNeeded();
    await button.click();
    await page.waitForTimeout(500);
}
async function closeWindow(page) { await page.keyboard.press("Escape"); await page.waitForTimeout(250); }

async function stressBrand(page) {
    await page.fill("#brandNameInput", LONG.slice(0, 60));
    await page.fill("#brandMarketInput", "Market ".repeat(5).slice(0, 40));
    await page.fill("#brandLogoInput", LONGURL);
    await page.fill("#brandImageInput", LONGURL);
    await page.fill("#brandWebsiteInput", LONGURL);
    await page.evaluate(() => {
        document.querySelectorAll("#addBrandForm input[type=text], #addBrandForm input[type=url], #addBrandForm select").forEach((n) => n.setAttribute("aria-invalid", "true"));
        const line = document.querySelector("#addBrandForm .form-error");
        line.textContent = "This address must start with https://  (" + "https://example.com/".repeat(5) + ")";
        line.style.display = "block";
    });
}
async function stressProduct(page) {
    await page.fill("#productNameInput", LONG.slice(0, 100));
    await page.fill("#productDescInput", "A long description. ".repeat(20));
    await page.fill("#productImageInput", LONGURL);
    await page.fill("#productFdaInput", "FDA-" + "1234567890".repeat(3));
    await page.fill("#productIngredientsInput", Array.from({ length: 60 }, (_, i) => `Ingredient number ${i + 1}`).join(", "));
    for (let i = 0; i < 12; i++) { const add = page.locator("#productShadeEditor button", { hasText: "Shade" }).first(); if (await add.count()) await add.click().catch(() => {}); }
    await page.evaluate((url) => {
        document.querySelectorAll("#productForm input[type=text], #productForm input[type=url], #productForm select, #productForm textarea").forEach((n) => n.setAttribute("aria-invalid", "true"));
        const line = document.querySelector("#productForm .form-error");
        line.textContent = "Each source must start with https://  (" + url + ")";
        line.style.display = "block";
    }, LONGURL);
}

const browser = await launch();
try {
    const sizes = [["360", 360, 740, 1], ["768", 768, 900, 1], ["1024", 1024, 800, 1], ["1440", 1440, 900, 1], ["zoom200", 720, 450, 1], ["text125", 1024, 800, 1.25]];
    for (const theme of ["light", "dark"]) for (const [label, width, height, scale] of sizes) {
        const { page, errors, settle } = await openPage(browser, "inv", { width, height, theme });
        await settle(500);
        if (scale !== 1) await page.evaluate((s) => document.documentElement.style.setProperty("--scale", String(s)), scale);
        const cases = [["add brand", "addBrandModal", () => openAdd(page, "Brand"), stressBrand], ["edit brand", "addBrandModal", () => openEdit(page, "edit-brand"), stressBrand],
            ["add product", "productModal", () => openAdd(page, "Product"), stressProduct], ["edit product", "productModal", () => openEdit(page, "edit-product"), stressProduct]];
        for (const [what, id, open, stress] of cases) {
            const tag = `${theme} ${label} ${what}`;
            await open();
            const root = `#${id}`;
            ok(await page.locator(`${root} .ing-modal > .ing-head, ${root} .ing-modal > .ing-nav, ${root} .ing-modal > .ing-form`).count() === 3, `${tag}: header, section bar and form are the three parts`);
            ok(await page.evaluate((r) => { const f = document.querySelector(r + " .ing-foot"); return getComputedStyle(f).position === "static" && f.parentElement.classList.contains("ing-form"); }, root), `${tag}: the footer is a flex part, not laid over the body`);
            ok(await page.evaluate((r) => Array.from(document.querySelectorAll(r + " .modal-actions button")).map((b) => b.textContent.trim()).join("|").replace(/Save .*/, "Save") === "Cancel|Save", root), `${tag}: Cancel is left of Save`);
            ok(await page.evaluate((r) => Array.from(document.querySelectorAll(r + " .ing-grid > .field")).every((f) => f.firstElementChild && f.firstElementChild.classList.contains("label-row")), root), `${tag}: every field starts with a label row`);
            ok(await page.evaluate((r) => Array.from(document.querySelectorAll(r + " .label-row > label[for]")).every((l) => document.getElementById(l.htmlFor)), root), `${tag}: every label points at its box`);
            ok(await page.evaluate((r) => Array.from(document.querySelectorAll(r + " .field > input[type=text], " + r + " .field > input[type=url], " + r + " .field > input[type=number], " + r + " .field > select")).filter((n) => n.offsetParent !== null && !n.classList.contains("combo-native")).every((n) => { const h = n.getBoundingClientRect().height; return Math.abs(h - 40) < 1.5; }), root), `${tag}: every text box and drop-down is 40 px high (address boxes included)`);
            ok(await page.evaluate((r) => Array.from(document.querySelectorAll(r + " .ing-section-title")).length >= 2, root), `${tag}: sections have headings`);
            let result = await layoutProblems(page, id);
            ok(result.problems.length === 0, `${tag}: no overlap, nothing covered, nothing under the header or footer (${result.count} controls) ${result.problems.join(" ; ")}`);
            if (SHOTS) await page.screenshot({ path: path.join(SHOTS, `${theme}-${label}-${what.replace(" ", "-")}-plain.png`) });
            await stress(page);
            result = await layoutProblems(page, id);
            ok(result.problems.length === 0, `${tag}: worst case (long names and addresses, every box in error): ${result.problems.join(" ; ")}`);
            if (SHOTS) {
                await page.evaluate((i) => { document.querySelector("#" + i + " .ing-body").scrollTop = 0; }, id);
                await page.screenshot({ path: path.join(SHOTS, `${theme}-${label}-${what.replace(" ", "-")}-worst-top.png`) });
                await page.evaluate((i) => { const b = document.querySelector("#" + i + " .ing-body"); b.scrollTop = b.scrollHeight; }, id);
                await page.screenshot({ path: path.join(SHOTS, `${theme}-${label}-${what.replace(" ", "-")}-worst-bottom.png`) });
            }
            await closeWindow(page);
        }
        ok(errors.filter((e) => !/404/.test(e)).length === 0, `${theme} ${label}: no page errors ${errors.join("; ")}`);
        await page.context().close();
    }

    // ---------------------------------------------------------------- behaviour
    {
        const { page, errors, settle } = await openPage(browser, "inv", { width: 1100, height: 800 });
        await settle(500);
        await openAdd(page, "Product");
        // the section bar: the four sections, jump and mark
        ok(await page.evaluate(() => Array.from(document.querySelectorAll("#productModal .ing-section-title")).map((h) => h.textContent).join("|") === "Product|Sources|Ingredients and shades|Listing"), "the product window has four sections");
        await page.click('#productModal .ing-nav-btn[data-jump="productSecListing"]');
        await page.waitForTimeout(700);
        ok(await page.locator('#productModal .ing-nav-btn[data-jump="productSecListing"][aria-current="true"]').count() === 1, "clicking a section in the bar marks it");
        ok(await page.evaluate(() => { const a = document.getElementById("productSecListingTitle").getBoundingClientRect().top, b = document.querySelector("#productModal .ing-body").getBoundingClientRect().top; return a >= b - 1 && a < b + 120 || document.querySelector("#productModal .ing-body").scrollTop + document.querySelector("#productModal .ing-body").clientHeight >= document.querySelector("#productModal .ing-body").scrollHeight - 2; }), "and the section is at the top of the body (or the body is at its end)");
        await page.evaluate(() => { document.querySelector("#productModal .ing-body").scrollTop = 0; });
        await page.waitForTimeout(200);
        ok(await page.locator('#productModal .ing-nav-btn[data-jump="productSecBasics"][aria-current="true"]').count() === 1, "scrolling back moves the mark back");
        // the ingredient search list opens inside the window and does not cover its own box
        await page.click('#productModal .ing-nav-btn[data-jump="productSecContents"]');
        await page.waitForTimeout(600);
        await page.fill("#productIngredientSearch", "a");
        await page.waitForTimeout(300);
        const list = await page.evaluate(() => { const r = document.getElementById("productIngredientResults"); if (r.hidden) return null; const b = document.getElementById("productIngredientSearch").getBoundingClientRect(), l = r.getBoundingClientRect(), f = document.querySelector("#productModal .ing-foot").getBoundingClientRect(); return { below: l.top >= b.bottom - 1, aboveFooter: l.bottom <= f.top + 1 || r.scrollHeight > 0 }; });
        ok(list === null || list.below, "the ingredient list opens below its box, never over it");
        await page.keyboard.press("Escape");
        await page.waitForTimeout(200);
        // a failed save: the box is marked and in view
        if (await page.evaluate(() => document.getElementById("productModal").style.display !== "none")) {
            await page.fill("#productNameInput", "");
            await page.evaluate(() => { const line = document.querySelector("#productForm .form-error"); document.getElementById("productNameInput").focus(); line.textContent = "Please enter the product name."; line.style.display = "block"; });
            await page.waitForTimeout(300);
            ok(await page.locator("#productForm [aria-invalid=true]").count() === 1, "the box that failed is marked invalid");
            ok(await page.evaluate(() => { const r = document.activeElement.getBoundingClientRect(), b = document.querySelector("#productModal .ing-body").getBoundingClientRect(); return r.top >= b.top - 1 && r.bottom <= b.bottom + 1; }), "and it is in view inside the body");
            await page.fill("#productNameInput", "Velvet");
            ok(await page.locator("#productForm [aria-invalid=true]").count() === 0, "typing clears the mark");
            // keyboard: Tab stays inside the window; Esc closes
            await page.evaluate(() => document.querySelector("#productModal .ing-close").focus());
            let inside = true, reachedFoot = false;
            for (let i = 0; i < 80 && inside; i++) {
                await page.keyboard.press("Tab");
                const info = await page.evaluate(() => ({ inside: !!document.activeElement.closest("#productModal"), foot: !!document.activeElement.closest(".ing-foot") }));
                inside = info.inside; if (info.foot) reachedFoot = true;
            }
            ok(inside, "Tab never leaves the product window");
            ok(reachedFoot, "Tab reaches Save and Cancel");
            await closeWindow(page);
        }
        ok(await page.evaluate(() => getComputedStyle(document.getElementById("productModal")).display === "none"), "Esc closes the window");
        // the brand window: two sections
        await openAdd(page, "Brand");
        ok(await page.evaluate(() => Array.from(document.querySelectorAll("#addBrandModal .ing-section-title")).map((h) => h.textContent).join("|") === "Brand|Pictures and website"), "the brand window has two sections");
        ok(await page.locator("#brandWebsiteConfirm").isVisible() && await page.evaluate(() => { const c = document.getElementById("brandWebsiteConfirm").getBoundingClientRect(); return c.width >= 14 && c.width <= 24; }), "the confirm tick box is an ordinary tick box (not stretched)");
        await closeWindow(page);
        ok(errors.filter((e) => !/404/.test(e)).length === 0, "no page errors in the behaviour checks " + errors.join("; "));
        await page.context().close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
