// "Quick fill" at the top of the Add Product form: name, barcode, camera, label photo, gallery photos or phone in, a filled form out.
// The admin still checks and saves: nothing is written to the database here.
// Every value put into the form is marked "Detected" with the source it came from (capture.js); a value already in the form is
// never replaced unless the admin ticks it in the comparison window.
import { escapeHtml } from "../_Shared/adminCommon.js";
import { icon, dialogFrame, askConfirm } from "../_Shared/adminShell.js";
import { lookupProduct, scanBarcode, readLabelPhoto, pickPhotos, readTextFromFile, barcodeFromPhoto } from "../_Shared/quickFill.js";
import { openPhoneLink } from "../_Shared/phoneLink.js";
import { brandFor, existingProduct, queryKind, officialLinks } from "../_Shared/smartAdd.js";
import {
    planFill, chosenValues, matchRoute, ROUTE_WORDS, fieldLabel, PHOTO_ROLES, guessPhotoRole, usefulLines
} from "../_Shared/capture.js";
import { barcodeKey } from "../_Shared/glowupRules.js";

const el = (id) => document.getElementById(id);
const OCR_SURE = 75;                    // text recognition's own confidence (0 to 100) below which a read list "Needs review"

// readForm(): the form's current values { name, brand, cosmeticType, imageUrl, barcode, fdaNumber, ingredients }.
// fill(values, { partial }): puts values into the form. noteOrigins(rows, method, score): marks the filled fields as Detected.
// noteRaw(text, from): keeps the ingredient statement exactly as the source or the photo gave it.
export function mountProductQuickFill({ brands, products, readForm, fill, openExisting, noteOrigins, noteRaw }) {
    let shown = [];
    const status = (text, tone) => { const s = el("productQuickStatus"); s.textContent = text || ""; s.className = "field-hint qf-status" + (tone ? " " + tone : ""); };

    function paint(list) {
        shown = list;
        const host = el("productQuickResults");
        if (list.length === 0) { host.innerHTML = ""; return; }
        host.innerHTML = list.map((c, at) => {
            const brand = brandFor(c.brand, brands());
            const same = existingProduct(c, products(), brands());
            const route = matchRoute(c.score);
            const facts = [
                c.cosmeticType || "kind not known",
                c.ingredients.length ? `${c.ingredients.length} ingredients` : "no ingredient list",
                c.barcode ? `barcode ${c.barcode}` : "",
                c.fdaNumber ? `FDA ${c.fdaNumber}` : "",
                brand ? "" : (c.brand ? "new brand" : "brand not known")
            ].filter(Boolean).join(" · ");
            const notMakeup = c.makeup === "skincare" || c.makeup === "out";
            const disagree = (c.conflicts || []).map((x) => fieldLabel(x.key).toLowerCase());
            return `<div class="qf-hit${notMakeup ? " qf-out" : ""}">
                <div class="qf-hit-text"><strong>${escapeHtml(c.brand ? c.brand + " " : "")}${escapeHtml(c.name)}</strong>
                    <small>${escapeHtml(facts)}</small>
                    <small class="qf-from">From ${escapeHtml(c.origins.join(" + "))}${c.note ? " · " + escapeHtml(c.note) : ""}</small>
                    ${ROUTE_WORDS[route] ? `<small class="qf-route qf-route-${route}">${escapeHtml(ROUTE_WORDS[route])}${route === "strong" ? "" : ": compare the name with the pack"}</small>` : ""}
                    ${disagree.length ? `<small class="qf-warn">The sources disagree on the ${escapeHtml(disagree.join(", "))}: you choose when you fill.</small>` : ""}
                    ${notMakeup ? '<small class="qf-warn">Not make-up: GlowUp cannot list it.</small>' : ""}
                    ${same ? `<small class="qf-warn">Already in the inventory as "${escapeHtml(same.name)}".</small>` : ""}
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
                // no match is not the end: the barcode is kept in the form and the next way in is offered
                if (query.kind === "barcode") {
                    const current = readForm();
                    if (!current.barcode) { fill({ barcode: query.value }, { partial: true }); noteOrigins([{ key: "barcode", apply: true, from: "Scanned or typed barcode" }], "barcode", 1); }
                    status(`Nothing is known about barcode ${query.value} yet. It is kept in the Barcode box. Next: take a photo of the ingredient list (or choose photos), then type the name.`, "warn");
                } else status("Nothing found. Check the spelling, try the brand and the product name, or take a photo of the ingredient list.", "warn");
                paint([]);
                return;
            }
            status(`${candidates.length} found. Pick the right one: the form fills, nothing is saved yet.`);
            paint(candidates);
            // one strong answer for an empty form: fill at once (the list stays for a change of mind)
            const first = candidates[0];
            const sure = (candidates.length === 1 && matchRoute(first.score) !== "weak") || (query.kind === "barcode" && barcodeKey(first.barcode) === query.key);
            if (sure && !existingProduct(first, products(), brands()) && planFill(first, readForm()).every((r) => r.kind !== "conflict") && !(first.conflicts || []).length) useCandidate(first);
        } finally { el("productQuickFind").disabled = false; }
    }

    // The comparison window: one row per field, what the form has, what the source says, and a tick for each value to take.
    function compare(c, rows) {
        return new Promise((resolve) => {
            const finish = (answer) => { frame.close(); resolve(answer); };
            const frame = dialogFrame(`Fill from ${c.origins.join(" + ")}?`, () => finish(null));
            frame.box.classList.add("wide", "qf-compare");
            const shownRows = rows.filter((r) => r.kind === "new" || r.kind === "conflict");
            const conflictsOf = (key) => ((c.conflicts || []).find((x) => x.key === key) || { values: [] }).values;
            frame.box.insertAdjacentHTML("beforeend", `
                <p class="modal-intro">Ticked values go into the form. A value the form already has is only replaced if you tick it. Nothing is saved until you press Save Product.</p>
                <table class="qf-table"><thead><tr><th scope="col">Take</th><th scope="col">Field</th><th scope="col">In the form now</th><th scope="col">From the source</th></tr></thead><tbody>
                ${shownRows.map((r, at) => `<tr class="${r.kind}">
                    <td><input type="checkbox" data-row="${at}" ${r.apply ? "checked" : ""} aria-label="Take the ${escapeHtml(r.label.toLowerCase())} from the source"></td>
                    <th scope="row">${escapeHtml(r.label)}${r.kind === "conflict" ? '<br><small class="qf-warn">differs</small>' : ""}</th>
                    <td>${r.current ? escapeHtml(r.current.slice(0, 220)) : '<span class="muted">empty</span>'}</td>
                    <td>${escapeHtml(r.proposed.slice(0, 220))}<br><small class="muted">from ${escapeHtml(r.from || "the source")}</small>
                        ${conflictsOf(r.key).length ? `<br><small class="qf-warn">Sources disagree: ${conflictsOf(r.key).map((v) => `${escapeHtml(v.from)} says "${escapeHtml(v.value.slice(0, 80))}"`).join("; ")}</small>` : ""}</td>
                </tr>`).join("")}
                </tbody></table>
                ${rows.some((r) => r.kind === "absent") ? `<p class="field-hint">Not given by this source (left as they are): ${escapeHtml(rows.filter((r) => r.kind === "absent").map((r) => r.label).join(", "))}.</p>` : ""}
                <div class="modal-actions"><button type="button" class="cancel-btn" data-act="cancel">Cancel</button><button type="button" class="save-btn" data-act="ok">Fill the ticked values</button></div>`);
            frame.box.querySelector('[data-act="cancel"]').addEventListener("click", () => finish(null));
            frame.box.querySelector('[data-act="ok"]').addEventListener("click", () => {
                frame.box.querySelectorAll("[data-row]").forEach((box) => { shownRows[Number(box.dataset.row)].apply = box.checked; });
                finish(rows);
            });
        });
    }

    async function useCandidate(c) {
        let rows = planFill(c, readForm());
        if (rows.some((r) => r.kind === "conflict" || r.sourcesDisagree)) {
            rows = await compare(c, rows);
            if (!rows) { status("Nothing was changed."); return; }
        }
        const values = chosenValues(rows, c);
        fill({ ...values, sources: c.sources }, { partial: true });
        noteOrigins(rows, rows.some((r) => r.key === "barcode" && r.apply) && c.score === 1 ? "barcode" : "lookup", c.score);
        if (values.ingredients) noteRaw(c.ingredientsText || c.ingredients.join(", "), c.fieldFrom && c.fieldFrom.ingredients ? c.fieldFrom.ingredients : c.origins[0]);
        const brand = brandFor(c.brand, brands());
        const after = readForm();
        const gaps = [];
        if (!after.ingredients.length) gaps.push("the ingredient list (take a photo of it)");
        if (!after.cosmeticType) gaps.push("the cosmetic type");
        if (!after.brand) gaps.push("the brand");
        const taken = rows.filter((r) => r.apply).map((r) => r.label.toLowerCase());
        status((taken.length ? `Filled ${taken.join(", ")} from ${c.origins.join(" + ")}. They are marked Detected: tick Checked beside each once it matches the pack.` : "Nothing new to fill.")
            + (brand || !values.brand ? "" : ` "${c.brand}" is a new brand: it is added when you save.`)
            + (gaps.length ? ` Still missing: ${gaps.join(", ")}.` : ""), gaps.length ? "warn" : "ok");
    }

    // An ingredient list from a photo or the phone. A list already in the form is replaced only when the admin says so.
    async function takeIngredients(names, rawText, from, method, sure) {
        const current = readForm();
        if (current.ingredients.length) {
            const yes = await askConfirm({ title: "Replace the ingredient list?", message: `The form already lists ${current.ingredients.length} ingredients. Replace them with the ${names.length} from ${from.toLowerCase()}?`, confirmLabel: "Replace", cancelLabel: "Keep mine" });
            if (!yes) { status("The ingredient list in the form was kept."); return false; }
        }
        fill({ ingredients: names }, { partial: true });
        noteOrigins([{ key: "ingredients", apply: true, from, sourcesDisagree: !sure }], method, sure ? 1 : 0.5);
        noteRaw(rawText, from);
        return true;
    }

    async function photosDialog() {
        const files = await pickPhotos();
        if (!files.length) return;
        const urls = files.map((f) => URL.createObjectURL(f));
        let closed = false;                       // Close, Escape or the backdrop also stops reading the photos still waiting
        const finish = () => { closed = true; urls.forEach((u) => URL.revokeObjectURL(u)); frame.close(); };
        const frame = dialogFrame("Read product photos", finish);
        frame.box.classList.add("wide", "qf-photos");
        frame.box.insertAdjacentHTML("beforeend", `
            <p class="modal-intro">Say what each photo shows. The barcode photo is looked up, the ingredient list is read into the form, the front and back are read so you can copy their text. The photos stay on this computer: only what you put in the form is saved.</p>
            <ul class="qf-photo-list">${files.map((f, at) => `<li>
                <img src="${urls[at]}" alt="Photo ${at + 1}" loading="lazy">
                <div><label class="small" for="qfRole${at}">${escapeHtml(f.name.slice(0, 60))}</label>
                <select id="qfRole${at}" data-role="${at}">${PHOTO_ROLES.map((r) => `<option value="${r.id}"${guessPhotoRole(f.name, at) === r.id ? " selected" : ""}>${escapeHtml(r.label)}</option>`).join("")}<option value="skip">Do not read</option></select>
                <p class="field-hint" data-state="${at}" role="status" aria-live="polite"></p></div></li>`).join("")}</ul>
            <div class="modal-actions"><button type="button" class="cancel-btn" data-act="close">Close</button><button type="button" class="save-btn" data-act="read">${icon("check")}Read the photos</button></div>`);
        frame.box.querySelector('[data-act="close"]').addEventListener("click", finish);
        const readButton = frame.box.querySelector('[data-act="read"]');
        readButton.addEventListener("click", async () => {
            readButton.disabled = true;
            let ingredientsTaken = false;
            for (let at = 0; at < files.length && !closed; at++) {
                const role = frame.box.querySelector(`[data-role="${at}"]`).value;
                const say = (text, tone) => { const p = frame.box.querySelector(`[data-state="${at}"]`); p.textContent = text; p.className = "field-hint" + (tone ? " " + tone : ""); };
                if (role === "skip") { say("Not read."); continue; }
                try {
                    if (role === "barcode") {
                        say("Looking for the bars...");
                        const code = await barcodeFromPhoto(files[at]);
                        if (closed) break;
                        if (!code) { say("No barcode found. Take it closer, straight on, with the bars sharp.", "warn"); continue; }
                        say(`Barcode ${code}: looked up in the form.`, "ok");
                        el("productQuickInput").value = code;
                        await find(code);
                    } else if (role === "ingredients") {
                        const read = await readTextFromFile(files[at], (t) => say(t));
                        if (closed) break;
                        if (!read || read.ingredients.length < 2) { say("No ingredient list was found. Take it closer, straight on, with the whole list sharp.", "warn"); continue; }
                        if (ingredientsTaken) { say(`${read.ingredients.length} ingredients read; the list from the photo above is already in the form.`, "warn"); continue; }
                        const sure = read.confidence >= OCR_SURE;
                        if (await takeIngredients(read.ingredients, read.text, "Photo of the label (text recognition)", "ocr", sure)) {
                            ingredientsTaken = true;
                            say(`${read.ingredients.length} ingredients put in the form${sure ? "" : " (the text was hard to read: marked Needs review)"}. Compare them with the pack.`, sure ? "ok" : "warn");
                        } else say("Kept the list you had.");
                    } else {
                        const read = await readTextFromFile(files[at], (t) => say(t));
                        const lines = usefulLines(read && read.text);
                        if (!lines.length) { say("No text could be read.", "warn"); continue; }
                        say("");
                        const box = frame.box.querySelector(`[data-state="${at}"]`);
                        box.innerHTML = `<details open><summary>Text read (${lines.length} lines). Text recognition can misread letters.</summary>${lines.map((l) => `<span class="qf-line">${escapeHtml(l)}${role === "front" ? ` <button type="button" class="btn small" data-name="${escapeHtml(l)}">Use as name</button>` : ""}</span>`).join("")}</details>`;
                    }
                } catch (error) { say(error.message || "The photo could not be read.", "warn"); }
            }
            readButton.disabled = false;
        });
        frame.box.addEventListener("click", (event) => {
            const b = event.target.closest("[data-name]");
            if (!b) return;
            fill({ name: b.dataset.name.slice(0, 100) }, { partial: true });
            noteOrigins([{ key: "name", apply: true, from: "Photo of the front (text recognition)", sourcesDisagree: true }], "ocr", 0.5);
            b.disabled = true; b.textContent = "Put in the form";
        });
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
        if (!digits) { status("No barcode was read. Type the digits under the bars in the box above, or choose a photo of the barcode.", "warn"); return; }
        el("productQuickInput").value = digits;
        find(digits);
    });
    el("productQuickLabel").addEventListener("click", async () => {
        try {
            const read = await readLabelPhoto((text) => status(text));
            if (!read) { status(""); return; }
            if (read.ingredients.length < 2) { status("No ingredient list was found in that photo. Take it closer and straight on, with the whole list sharp.", "warn"); return; }
            const sure = read.confidence >= OCR_SURE;
            if (await takeIngredients(read.ingredients, read.text, "Photo of the label (text recognition)", "ocr", sure))
                status(`${read.ingredients.length} ingredients read from the photo and put in the list${sure ? "" : " (hard to read: marked Needs review)"}. Compare them with the pack: text recognition can misread a letter.`, sure ? "ok" : "warn");
        } catch (error) { status(error.message || "The photo could not be read.", "warn"); }
    });
    el("productQuickPhotos").addEventListener("click", photosDialog);
    el("productQuickPhone").addEventListener("click", () => openPhoneLink({
        title: "Scan with your phone",
        what: "barcode",
        onValue: async ({ kind, value }) => {
            if (kind === "barcode" && value) { el("productQuickInput").value = value; find(value); }
            else if (kind === "ingredients" && value) {
                const names = value.split(",").map((n) => n.trim()).filter(Boolean);
                if (await takeIngredients(names, value, "Phone photo of the label (text recognition)", "phone", true))
                    status(`${names.length} ingredients came from the phone and are in the list. Compare them with the pack.`, "ok");
            }
        }
    }));

    return { reset: () => { el("productQuickInput").value = ""; status(""); paint([]); } };
}
