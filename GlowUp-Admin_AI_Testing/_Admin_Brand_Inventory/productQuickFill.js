// "Quick fill" at the top of the Add Product form: a name or a barcode in, a filled form out.
// The admin still checks and saves: nothing is written to the database here.
// Every value put into the form is marked "Detected" with the source it came from (capture.js); a value already in the form is
// never replaced unless the admin ticks it in the comparison window.
// 28 Nov 2026 (owner): the photo, gallery and phone buttons were removed. Opening a product that lacks details fills the EMPTY
// fields by itself (autoFill): it asks the sources by barcode, then by brand and name, and takes only a sure match of the same brand.
import { escapeHtml } from "../_Shared/adminCommon.js";
import { auth } from "../_Database/firebase-console.js";
import { icon, dialogFrame } from "../_Shared/adminShell.js";
import { lookupProduct } from "../_Shared/quickFill.js";
import { brandFor, existingProduct, queryKind, officialLinks } from "../_Shared/smartAdd.js";
import { planFill, chosenValues, matchRoute, ROUTE_WORDS, fieldLabel } from "../_Shared/capture.js";
import { barcodeKey, compactName } from "../_Shared/glowupRules.js";

const el = (id) => document.getElementById(id);
// the fields autoFill tries to complete (the photo link and the shades are left to the admin)
const AUTO_KEYS = ["name", "brand", "cosmeticType", "barcode", "fdaNumber", "ingredients"];

// readForm(): the form's current values { name, brand, cosmeticType, imageUrl, barcode, fdaNumber, ingredients }.
// fill(values, { partial }): puts values into the form. noteOrigins(rows, method, score): marks the filled fields as Detected.
// noteRaw(text, from): keeps the ingredient statement exactly as the source gave it.
// editingId(): the id of the product open in the form ("" for a new one), so its own inventory row is not "already in the inventory".
export function mountProductQuickFill({ brands, products, readForm, fill, openExisting, noteOrigins, noteRaw, editingId = () => "" }) {
    let shown = [];
    let run = 0;                                   // a newer fill (or a closed form) makes an older automatic fill stop
    const status = (text, tone) => { const s = el("productQuickStatus"); s.textContent = text || ""; s.className = "field-hint qf-status" + (tone ? " " + tone : ""); };
    const token = () => (auth.currentUser ? auth.currentUser.getIdToken() : "");
    const sameAsOpen = (c) => { const same = existingProduct(c, products(), brands()); return same && same.id === editingId() ? null : same; };

    function paint(list) {
        shown = list;
        const host = el("productQuickResults");
        if (list.length === 0) { host.innerHTML = ""; return; }
        host.innerHTML = list.map((c, at) => {
            const brand = brandFor(c.brand, brands());
            const same = sameAsOpen(c);
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
        const mine = ++run;
        status(query.kind === "barcode" ? `Looking up barcode ${query.value}...` : `Looking for "${query.value}"...`);
        el("productQuickFind").disabled = true;
        try {
            const { candidates, note } = await lookupProduct(text, { getToken: token });
            if (mine !== run) return;
            if (candidates.length === 0) {
                // no match is not the end: the barcode is kept in the form
                if (query.kind === "barcode") {
                    const current = readForm();
                    if (!current.barcode) { fill({ barcode: query.value }, { partial: true }); noteOrigins([{ key: "barcode", apply: true, from: "Scanned or typed barcode" }], "barcode", 1); }
                    status(`Nothing is known about barcode ${query.value} yet (Open Beauty Facts and UPCitemdb were asked${note ? "; " + note : ""}). It is kept in the Barcode box. Try the brand and the product name.`, "warn");
                } else status("Nothing found. Check the spelling, or try the brand and the product name.", "warn");
                paint([]);
                return;
            }
            status(`${candidates.length} found. Pick the right one: the form fills, nothing is saved yet.`);
            paint(candidates);
            // one strong answer: fill at once (the list stays for a change of mind)
            const first = candidates[0];
            const sure = (candidates.length === 1 && matchRoute(first.score) !== "weak") || (query.kind === "barcode" && barcodeKey(first.barcode) === query.key);
            if (sure && !sameAsOpen(first) && planFill(first, readForm()).every((r) => r.kind !== "conflict") && !(first.conflicts || []).length) useCandidate(first);
        } finally { if (mine === run) el("productQuickFind").disabled = false; }
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

    const gapsOf = (form) => {
        const gaps = [];
        if (!form.ingredients.length) gaps.push("the ingredient list");
        if (!form.cosmeticType) gaps.push("the cosmetic type");
        if (!form.brand) gaps.push("the brand");
        if (!form.barcode) gaps.push("the barcode");
        if (!form.fdaNumber) gaps.push("the FDA registration number");
        return gaps;
    };

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
        const gaps = gapsOf(readForm());
        const taken = rows.filter((r) => r.apply).map((r) => r.label.toLowerCase());
        status((taken.length ? `Filled ${taken.join(", ")} from ${c.origins.join(" + ")}. They are marked Detected: tick Checked beside each once it matches the pack.` : "Nothing new to fill.")
            + (brand || !values.brand ? "" : ` "${c.brand}" is a new brand: it is added when you save.`)
            + (gaps.length ? ` Still missing: ${gaps.join(", ")}.` : ""), gaps.length ? "warn" : "ok");
    }

    // ---- automatic fill: the empty fields of the product open in the form ----
    // Asks by barcode first (exact), then by brand and name. A candidate is used only when it is the same brand as the form and
    // its name match is not weak (matchRoute), so a different product's list never lands in the form. Values the form has are
    // never touched: only empty fields are filled, each marked Detected (Needs review on a "check" match), and the admin saves.
    async function autoFill() {
        const start = readForm();
        const empty = AUTO_KEYS.filter((k) => (Array.isArray(start[k]) ? !start[k].length : !start[k]));
        if (!empty.length || !(start.name || start.barcode)) return;
        const mine = ++run;
        el("productQuickFind").disabled = true;
        status(`Filling the missing details by itself (${gapsOf(start).join(", ") || empty.map((k) => fieldLabel(k).toLowerCase()).join(", ")}): asking the FDA Philippines list, Open Beauty Facts and UPCitemdb...`);
        const queries = [];
        if (start.barcode) queries.push(start.barcode);
        if (start.name) queries.push([start.brand, start.name].filter(Boolean).join(" "));
        const brandKey = compactName(start.brand);
        const filled = [], from = new Set();
        let seen = [];
        try {
            for (const text of queries) {
                const query = queryKind(text);
                const answer = await lookupProduct(text, { getToken: token }).catch(() => ({ candidates: [] }));
                if (mine !== run) return;
                const list = answer.candidates || [];
                seen = seen.concat(list);
                for (const c of list) {
                    const exactCode = query.kind === "barcode" && barcodeKey(c.barcode) === query.key;
                    const sameBrand = !brandKey || compactName(c.brand) === brandKey;
                    if (!sameBrand || (!exactCode && matchRoute(c.score) === "weak")) continue;
                    if (c.makeup === "skincare" || c.makeup === "out") continue;
                    const rows = planFill(c, readForm()).map((r) => ({ ...r, apply: r.kind === "new" && AUTO_KEYS.includes(r.key) && !r.sourcesDisagree }));
                    if (!rows.some((r) => r.apply)) continue;
                    const values = chosenValues(rows, c);
                    fill({ ...values, sources: c.sources }, { partial: true });
                    noteOrigins(rows, exactCode ? "barcode" : "lookup", exactCode ? 1 : c.score);
                    if (values.ingredients) noteRaw(c.ingredientsText || c.ingredients.join(", "), c.fieldFrom && c.fieldFrom.ingredients ? c.fieldFrom.ingredients : c.origins[0]);
                    rows.filter((r) => r.apply).forEach((r) => filled.push(r.label.toLowerCase()));
                    c.origins.forEach((o) => from.add(o));
                }
                if (!gapsOf(readForm()).length) break;
            }
            if (mine !== run) return;
            // the candidates stay listed below, so the admin can still pick another one by hand
            const unique = []; const keys = new Set();
            seen.forEach((c) => { const k = compactName(c.brand) + "|" + compactName(c.name) + "|" + barcodeKey(c.barcode || ""); if (!keys.has(k)) { keys.add(k); unique.push(c); } });
            paint(unique.slice(0, 6));
            const gaps = gapsOf(readForm());
            if (filled.length) status(`Filled by itself: ${filled.join(", ")} from ${Array.from(from).join(" + ")}. Marked Detected: check them against the pack, then save.${gaps.length ? ` No source had ${gaps.join(", ")}.` : ""}`, gaps.length ? "warn" : "ok");
            else status(`No source had the missing details (${gaps.join(", ")}) for this exact product.${unique.length ? " Similar products are listed below: fill from one only if it is the same product." : " Type another name or the barcode and press Find."}`, "warn");
        } finally { if (mine === run) el("productQuickFind").disabled = false; }
    }

    el("productQuickFind").addEventListener("click", () => find(el("productQuickInput").value));
    el("productQuickInput").addEventListener("keydown", (event) => { if (event.key === "Enter") { event.preventDefault(); find(el("productQuickInput").value); } });
    el("productQuickResults").addEventListener("click", (event) => {
        const fillButton = event.target.closest("[data-fill]"), openButton = event.target.closest("[data-open]");
        if (fillButton) useCandidate(shown[Number(fillButton.dataset.fill)]);
        if (openButton) { const same = sameAsOpen(shown[Number(openButton.dataset.open)]); if (same) openExisting(same); }
    });

    return {
        reset: () => { run++; el("productQuickInput").value = ""; el("productQuickFind").disabled = false; status(""); paint([]); },
        autoFill
    };
}
