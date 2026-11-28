// Add barcode list (23 Nov 2026; centred pop-up since 28 Nov): 1 Paste -> 2 Check -> 3 Import selected (saved as Pending).
// A centred window on a wide screen, a full-screen sheet on a phone. All look-up work is the existing code: parseEntries / lookupBatch /
// recordFromCandidate (_Shared/batchBarcodes.js) and the page's own lookupProduct + saveInventoryDraft. Nothing is saved until the
// admin presses "Import selected products", and then through the same path as a product file: Pending, reviewed = false, Detected.
import { askConfirm, icon } from "../_Shared/adminShell.js";
import { popWindow } from "../_Shared/planViews.js";
import { MAX_BARCODES, parseEntries, lookupBatch } from "../_Shared/batchBarcodes.js";

const make = (tag, className, text) => { const n = document.createElement(tag); if (className) n.className = className; if (text != null) n.textContent = text; return n; };

const STATUS = {
    found: { label: "Found", cls: "good" },
    notfound: { label: "Not found", cls: "warn" },
    duplicate: { label: "Duplicate", cls: "review" },
    invalid: { label: "Invalid", cls: "bad" }
};

/**
 * deps: lookup(code) -> { candidates, note }   existing(code) -> the catalogue product with this barcode, or null
 *       importRecords(records) -> summary text (throws on failure)   notify(text, kind)
 */
export function openBatchLookup(deps) {
    let rows = [];            // { key, raw, code, status, why, record, selected, busy }
    let busy = false;         // a look-up or an import is running: Escape and the backdrop do not close
    let abort = null;
    let nextKey = 1;

    const dismiss = async () => {
        if (busy) return;
        const dirty = textarea.value.trim() !== "" || rows.length > 0;
        if (dirty && !(await askConfirm({ title: "Discard changes?", message: "The barcodes and results in this window will be lost.", confirmLabel: "Discard", cancelLabel: "Keep editing" }))) return;
        frame.close();
    };
    const frame = popWindow("Add barcode list", dismiss, { subtitle: "Find many products at once. Nothing is saved until you import." });
    frame.box.classList.add("batch-window");
    const steps = make("ol", "pw-steps");
    ["Paste barcodes", "Check results", "Import"].forEach((words, i) => { const li = make("li"); li.append(make("b", "", String(i + 1)), document.createTextNode(words)); steps.append(li); });
    const atStep = (n) => Array.from(steps.children).forEach((li, i) => { li.classList.toggle("now", i === n); li.classList.toggle("done", i < n); if (i === n) li.setAttribute("aria-current", "step"); else li.removeAttribute("aria-current"); });
    const body = frame.body;
    body.append(steps);
    atStep(0);

    // ---------- step 1: enter ----------
    const enter = make("section", "bl-enter");
    const label = make("label", "field-label", "Barcodes"); label.htmlFor = "batchLookupText";
    const textarea = make("textarea"); textarea.id = "batchLookupText"; textarea.rows = 7; textarea.spellcheck = false; textarea.autocomplete = "off";
    textarea.placeholder = "Paste or enter barcodes here, one per line or separated by commas.";
    const counts = make("p", "field-hint bl-counts"); counts.setAttribute("role", "status"); counts.setAttribute("aria-live", "polite");
    const enterActions = make("div", "bl-actions");
    const clearAll = make("button", "btn", "Clear all"); clearAll.type = "button";
    const run = make("button", "save-btn", "Look up barcodes"); run.type = "button";
    enterActions.append(clearAll, make("span", "spacer"), run);
    enter.append(label, textarea, counts);

    // ---------- step 2 + 3: results, summary, import ----------
    const results = make("section", "bl-results"); results.hidden = true;
    const progress = make("p", "field-hint bl-progress"); progress.setAttribute("role", "status"); progress.setAttribute("aria-live", "polite");
    const summary = make("div", "bl-summary");
    const tools = make("div", "bl-tools");
    const selectAll = make("label", "bl-selectall");
    const selectAllBox = make("input"); selectAllBox.type = "checkbox";
    selectAll.append(selectAllBox, make("span", "", "Select all eligible"));
    const clearResults = make("button", "btn", "Clear results"); clearResults.type = "button";
    const backToList = make("button", "btn", "Edit the list"); backToList.type = "button";
    tools.append(selectAll, backToList, clearResults);
    const list = make("ul", "bl-list"); list.setAttribute("aria-label", "Look-up results");
    const importBar = make("div", "bl-importbar");
    const importNote = make("span", "field-hint");
    const cancelLookup = make("button", "cancel-btn", "Cancel look-up"); cancelLookup.type = "button"; cancelLookup.hidden = true;
    const importBtn = make("button", "save-btn", "Import selected products"); importBtn.type = "button";
    importBar.append(importNote, cancelLookup, importBtn);
    results.append(progress, summary, tools, list);
    frame.foot.append(enterActions, importBar);
    importBar.hidden = true;
    body.append(enter, results);

    // ---------- helpers ----------
    const entries = () => parseEntries(textarea.value);
    function paintCounts() {
        const all = entries();
        const valid = all.filter((e) => e.state === "valid"), invalid = all.filter((e) => e.state === "invalid").length, dup = all.filter((e) => e.state === "duplicate").length;
        const over = Math.max(0, valid.length - MAX_BARCODES);
        counts.textContent = all.length === 0
            ? `Up to ${MAX_BARCODES} barcodes at a time, 8 to 14 digits, leading zeros kept.`
            : `${all.length} entered: ${valid.length} valid, ${dup} duplicate, ${invalid} invalid.` + (over ? ` ${over} over the limit of ${MAX_BARCODES} will wait for the next run.` : "");
        counts.classList.toggle("warn", over > 0 || invalid > 0);
        run.disabled = valid.length === 0 || busy;
        clearAll.disabled = textarea.value === "" || busy;
    }

    const eligible = () => rows.filter((r) => r.status === "found");
    const selected = () => rows.filter((r) => r.status === "found" && r.selected);

    function paintResults() {
        const tally = (s) => rows.filter((r) => r.status === s).length;
        summary.replaceChildren();
        [["Submitted", rows.length, ""], ["Matched", tally("found"), "good"], ["Not found", tally("notfound"), "warn"], ["Duplicate", tally("duplicate"), "review"], ["Invalid", tally("invalid"), "bad"], ["Selected", selected().length, "pink"]]
            .forEach(([name, n, cls]) => { const c = make("span", "chip " + cls); c.append(make("b", "", String(n)), document.createTextNode(" " + name)); summary.appendChild(c); });
        list.replaceChildren();
        rows.forEach((row) => {
            const li = make("li", "bl-row"); li.dataset.key = row.key; li.dataset.status = row.status;
            const pick = make("input"); pick.type = "checkbox"; pick.checked = row.selected && row.status === "found"; pick.disabled = row.status !== "found" || busy;
            pick.setAttribute("aria-label", "Select " + (row.code || row.raw));
            pick.addEventListener("change", () => { row.selected = pick.checked; paintResults(); });
            const main = make("div", "bl-main");
            const code = make("span", "bl-code", row.code || row.raw);
            const name = make("span", "bl-name", row.record ? [row.record.brandName, row.record.productName].filter(Boolean).join(": ") : (row.why || ""));
            main.append(code, name);
            if (row.record && row.why) main.appendChild(make("span", "bl-why", row.why));
            const st = STATUS[row.status] || STATUS.notfound;
            const badge = make("span", "chip " + st.cls, row.busy ? "Looking up..." : st.label);
            const actions = make("span", "bl-row-actions");
            if (row.status === "notfound" && !row.busy && row.code) {
                const retry = make("button", "btn", "Retry"); retry.type = "button"; retry.disabled = busy;
                retry.addEventListener("click", () => retryOne(row));
                actions.appendChild(retry);
            }
            const remove = make("button", "icon-btn"); remove.type = "button"; remove.setAttribute("aria-label", "Remove " + (row.code || row.raw)); remove.innerHTML = icon("x"); remove.disabled = busy;
            remove.addEventListener("click", () => { rows = rows.filter((r) => r !== row); if (rows.length === 0) toEnter(); else paintResults(); });
            actions.appendChild(remove);
            li.append(pick, main, badge, actions);
            list.appendChild(li);
        });
        const eligibleCount = eligible().length;
        selectAllBox.checked = eligibleCount > 0 && selected().length === eligibleCount;
        selectAllBox.disabled = eligibleCount === 0 || busy;
        importBtn.disabled = selected().length === 0 || busy;
        importNote.textContent = selected().length === 0 ? "Nothing selected: nothing will be imported." : `${selected().length} will be saved as Pending for admin review.`;
        clearResults.disabled = busy; backToList.disabled = busy;
    }

    function toEnter() { results.hidden = true; importBar.hidden = true; enter.hidden = false; enterActions.hidden = false; atStep(0); paintCounts(); textarea.focus(); }
    function toResults() { enter.hidden = true; enterActions.hidden = true; results.hidden = false; importBar.hidden = false; atStep(1); paintResults(); }

    // ---------- look-up ----------
    function classify(row, record, notFoundWhy) {
        if (!record) { row.status = "notfound"; row.why = notFoundWhy || "No source knows this barcode."; row.record = null; row.selected = false; return; }
        const owner = deps.existing ? deps.existing(row.code) : null;
        row.record = record;
        if (owner) { row.status = "duplicate"; row.why = `Already in the catalogue as "${owner.name}".`; row.selected = false; return; }
        row.status = "found"; row.why = ""; row.selected = true;
    }

    async function lookOne(row) {
        row.busy = true; paintResults();
        let out = { records: [], notFound: [] };
        try { out = await lookupBatch([row.code], deps.lookup, { gapMs: 0, signal: abort && abort.signal }); } catch (error) { out.notFound.push({ why: "The look-up failed. Check the connection and retry." }); }
        row.busy = false;
        classify(row, out.records[0] || null, out.notFound[0] && out.notFound[0].why);
    }

    async function retryOne(row) {
        if (busy) return;
        busy = true; abort = new AbortController(); paintResults();
        await lookOne(row);
        busy = false; paintResults();
    }

    async function runLookup() {
        const all = entries();
        const valid = all.filter((e) => e.state === "valid").slice(0, MAX_BARCODES);
        if (valid.length === 0 || busy) return;
        rows = all.filter((e) => e.state !== "valid" || valid.includes(e)).map((e) => ({
            key: nextKey++, raw: e.raw, code: e.code, status: e.state === "valid" ? "notfound" : e.state, record: null, selected: false, busy: false,
            why: e.state === "duplicate" ? "Repeated in the list." : e.state === "invalid" ? "Not 8 to 14 digits." : ""
        }));
        busy = true; abort = new AbortController(); cancelLookup.hidden = false;
        toResults();
        const todo = rows.filter((r) => r.status === "notfound" && r.code);
        let n = 0;
        for (const row of todo) {
            if (abort.signal.aborted) { row.status = "notfound"; row.why = "Look-up cancelled."; continue; }
            progress.textContent = `Looking up ${++n} of ${todo.length}: ${row.code}`;
            await lookOne(row);
            paintResults();
            if (n < todo.length && !abort.signal.aborted) await new Promise((r) => setTimeout(r, 250));
        }
        progress.textContent = abort.signal.aborted ? "Look-up cancelled. What was found so far is kept." : `Done: ${eligible().length} of ${todo.length} found.`;
        busy = false; cancelLookup.hidden = true; paintResults();
    }

    // ---------- import ----------
    async function doImport() {
        const chosen = selected();
        if (chosen.length === 0 || busy) return;
        const ok = await askConfirm({
            title: "Import selected products?",
            message: `${chosen.length} product${chosen.length === 1 ? "" : "s"} will be saved as Pending. Nothing goes live until you review it.`,
            confirmLabel: "Import selected products", cancelLabel: "Back"
        });
        if (!ok) return;
        busy = true; atStep(2); paintResults(); importBtn.textContent = "Importing...";
        try {
            const text = await deps.importRecords(chosen.map((r) => r.record));
            const done = new Set(chosen.map((r) => r.key));
            rows = rows.filter((r) => !done.has(r.key));        // what was saved leaves the list; everything else stays to correct or retry
            progress.textContent = text || "Saved as Pending for admin review.";
            if (rows.length === 0) { busy = false; importBtn.textContent = "Import selected products"; textarea.value = ""; toEnter(); return; }
        } catch (error) {
            deps.notify("The import did not finish: " + (error && error.message ? error.message : "try again") + ". Your results are kept.", "error");
        }
        busy = false; atStep(1); importBtn.textContent = "Import selected products"; paintResults();
    }

    // ---------- wiring ----------
    textarea.addEventListener("input", paintCounts);
    clearAll.addEventListener("click", () => { textarea.value = ""; paintCounts(); textarea.focus(); });
    run.addEventListener("click", runLookup);
    selectAllBox.addEventListener("change", () => { eligible().forEach((r) => { r.selected = selectAllBox.checked; }); paintResults(); });
    clearResults.addEventListener("click", () => { rows = []; textarea.value = ""; toEnter(); });
    backToList.addEventListener("click", toEnter);
    cancelLookup.addEventListener("click", () => { if (abort) abort.abort(); });
    importBtn.addEventListener("click", doImport);
    paintCounts();
    textarea.focus();
    return { close: () => frame.close() };
}
