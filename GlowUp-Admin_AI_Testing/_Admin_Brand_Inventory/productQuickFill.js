// "Quick fill" at the top of the Add Product form: name, barcode, camera, label photo or phone in, a filled form out.
// The admin still checks and saves: nothing is written to the database here.
import { escapeHtml } from "../_Shared/adminCommon.js";
import { icon } from "../_Shared/adminShell.js";
import { lookupProduct, scanBarcode, readLabelPhoto } from "../_Shared/quickFill.js";
import { openPhoneLink } from "../_Shared/phoneLink.js";
import { brandFor, existingProduct, queryKind, officialLinks } from "../_Shared/smartAdd.js";

const el = (id) => document.getElementById(id);

export function mountProductQuickFill({ brands, products, fill, fillIngredients, openExisting }) {
    let shown = [];
    const status = (text, tone) => { const s = el("productQuickStatus"); s.textContent = text || ""; s.className = "field-hint qf-status" + (tone ? " " + tone : ""); };

    function paint(list, query) {
        shown = list;
        const host = el("productQuickResults");
        if (list.length === 0) { host.innerHTML = ""; return; }
        host.innerHTML = list.map((c, at) => {
            const brand = brandFor(c.brand, brands());
            const same = existingProduct(c, products(), brands());
            const facts = [
                c.cosmeticType || "kind not known",
                c.ingredients.length ? `${c.ingredients.length} ingredients` : "no ingredient list",
                c.barcode ? `barcode ${c.barcode}` : "",
                c.fdaNumber ? `FDA ${c.fdaNumber}` : "",
                brand ? "" : (c.brand ? "new brand" : "brand not known")
            ].filter(Boolean).join(" · ");
            const notMakeup = c.makeup === "skincare" || c.makeup === "out";
            return `<div class="qf-hit${notMakeup ? " qf-out" : ""}">
                <div class="qf-hit-text"><strong>${escapeHtml(c.brand ? c.brand + " " : "")}${escapeHtml(c.name)}</strong>
                    <small>${escapeHtml(facts)}</small>
                    <small class="qf-from">From ${escapeHtml(c.origins.join(" + "))}${c.note ? " · " + escapeHtml(c.note) : ""}</small>
                    ${notMakeup ? '<small class="qf-warn">Not make-up: GlowUp cannot list it.</small>' : ""}
                    <small class="qf-links">Read more: ${officialLinks(c, brands()).map((l) => `<a href="${escapeHtml(l.url)}" target="_blank" rel="noopener noreferrer">${escapeHtml(l.label)}</a>`).join(" · ")}</small></div>
                ${same ? `<button type="button" class="btn" data-open="${at}">Open existing</button>`
                    : `<button type="button" class="btn primary" data-fill="${at}"${notMakeup ? " disabled" : ""}>${icon("check")}Fill form</button>`}
            </div>`;
        }).join("");
    }

    async function find(text) {
        const query = queryKind(text);
        if (query.kind === "empty") { status("Type a product name or a barcode first.", "warn"); el("productQuickInput").focus(); return; }
        status(query.kind === "barcode" ? `Looking up barcode ${query.value}...` : `Looking for "${query.value}"...`);
        el("productQuickFind").disabled = true;
        try {
            const { candidates } = await lookupProduct(text);
            if (candidates.length === 0) {
                status(query.kind === "barcode"
                    ? `Nothing is known about barcode ${query.value} yet. It is put in the Barcode box: type the name, or take a photo of the ingredient list.`
                    : "Nothing found. Check the spelling, try the brand and the product name, or take a photo of the ingredient list.", "warn");
                if (query.kind === "barcode") fill({ barcode: query.value, name: "", brand: "", cosmeticType: "", ingredients: [], sources: [], fdaNumber: "", imageUrl: "" }, { partial: true });
                paint([]);
                return;
            }
            status(`${candidates.length} found. Pick the right one: the form fills, nothing is saved yet.`);
            paint(candidates);
            // one answer, or a barcode answer whose first hit carries that barcode: fill at once (the list stays for a change of mind)
            const sure = candidates.length === 1 || (query.kind === "barcode" && candidates[0].barcode === query.value);
            if (sure && !existingProduct(candidates[0], products(), brands())) useCandidate(candidates[0]);
        } finally { el("productQuickFind").disabled = false; }
    }

    function useCandidate(c) {
        fill(c, { partial: false });
        const brand = brandFor(c.brand, brands());
        const gaps = [];
        if (!c.ingredients.length) gaps.push("the ingredient list (take a photo of it)");
        if (!c.cosmeticType) gaps.push("the cosmetic type");
        if (!brand && !c.brand) gaps.push("the brand");
        status(`Filled from ${c.origins.join(" + ")}.` + (brand || !c.brand ? "" : ` "${c.brand}" is a new brand: it is added when you save.`)
            + (gaps.length ? ` Still missing: ${gaps.join(", ")}.` : " Check it against the pack, then save."), gaps.length ? "warn" : "ok");
    }

    el("productQuickFind").addEventListener("click", () => find(el("productQuickInput").value));
    el("productQuickInput").addEventListener("keydown", (event) => { if (event.key === "Enter") { event.preventDefault(); find(el("productQuickInput").value); } });
    el("productQuickResults").addEventListener("click", (event) => {
        const fillButton = event.target.closest("[data-fill]"), openButton = event.target.closest("[data-open]");
        if (fillButton) useCandidate(shown[Number(fillButton.dataset.fill)]);
        if (openButton) { const same = existingProduct(shown[Number(openButton.dataset.open)], products(), brands()); if (same) openExisting(same); }
    });
    el("productQuickScan").addEventListener("click", async () => {
        const digits = await scanBarcode({ title: "Scan the product's barcode" });
        if (!digits) return;
        el("productQuickInput").value = digits;
        find(digits);
    });
    el("productQuickLabel").addEventListener("click", async () => {
        try {
            const read = await readLabelPhoto((text) => status(text));
            if (!read) { status(""); return; }
            if (read.ingredients.length < 2) { status("No ingredient list was found in that photo. Take it closer and straight on, with the whole list sharp.", "warn"); return; }
            fillIngredients(read.ingredients);
            status(`${read.ingredients.length} ingredients read from the photo and put in the list. Compare them with the pack: text recognition can misread a letter.`, "ok");
        } catch (error) { status(error.message || "The photo could not be read.", "warn"); }
    });
    el("productQuickPhone").addEventListener("click", () => openPhoneLink({
        title: "Scan with your phone",
        what: "barcode",
        onValue: ({ kind, value }) => {
            if (kind === "barcode" && value) { el("productQuickInput").value = value; find(value); }
            else if (kind === "ingredients" && value) {
                const names = value.split(",").map((n) => n.trim()).filter(Boolean);
                fillIngredients(names);
                status(`${names.length} ingredients came from the phone and are in the list. Compare them with the pack.`, "ok");
            }
        }
    }));

    return { reset: () => { el("productQuickInput").value = ""; status(""); paint([]); } };
}
