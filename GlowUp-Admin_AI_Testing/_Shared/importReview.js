// The import drop box, step by step (9 November 2026), the same on Brand & Product Inventory and the Ingredient Curator:
//
//   1. READING  - a calm cover lies over the drop box ONLY (never over the page): what is happening now, and Cancel. The
//                 admin can keep working anywhere else on the page meanwhile.
//   2. READY    - the cover says how many records were found, with "Check and save" and "Discard". A message in the corner
//                 says so too (with "Check now"), and when the tab is in the background its title shows "File read".
//   3. CHECKING - a large window lists every record, each field editable, each one can be left out; a second tab shows the
//                 text that was read from the file (editable, and "Read again" from it). NOTHING is in the database yet.
//                 X / Escape / "Later" close it and keep the records waiting in the drop box; "Discard" asks first.
//   4. SAVING   - only now are the ticked records written, as Pending, by the page's own save. The window cannot be closed
//                 while saving. The result is said under the drop box.
//
// The rules of docs/NAVIGATION_RULES.md: a visible exit everywhere; Escape and the backdrop never discard and never save;
// discarding asks "Discard ...?" with "Keep checking"; leaving the page while records wait asks the browser's question;
// every wait has Cancel; nothing covers other panels (the cover is the drop box's own size); reduced motion is respected.
// Everything read from a file is written with textContent / .value, never as HTML.
import { icon, toast, askConfirm } from "./adminShell.js";

const PAGE_SIZE = 20;                          // records per page of the check window: a file of any length stays quick

/**
 * Starts one import on a drop box. One at a time per drop box.
 * spec = {
 *   dropbox, choose, status,                  // the drop box button, the "Choose file" button, the status line under it
 *   file,                                     // the File
 *   read: async (file, say) => text,          // reads the file in this browser
 *   extract: async (text, say, signal) => ({ draft, notes: [], model, refused: "" }),
 *   review: { title, lists: [{ key, label, nounOne, noun, fields: [{ key, label, kind: "text" | "lines" | "area" | "select", options, wide }], titleOf, noteOf }] },
 *   save: async (draft, say) => "what was saved"     // writes the ticked records; its answer is shown under the drop box
 * }
 */
export function startImport(spec) {
    const { dropbox, choose, status } = spec;
    if (dropbox.dataset.importBusy) { toast("A file is already being read here. Wait for it, or cancel it first.", "info"); return; }
    const job = { cancelled: false, stop: new AbortController(), text: "", result: null, edited: false };
    dropbox.dataset.importBusy = "1";
    dropbox.disabled = true;
    if (choose) choose.disabled = true;
    const report = (message, failed) => { status.textContent = message || ""; status.classList.toggle("error", !!failed); };
    report("");

    // the cover: over the drop box only, in a holder of the same size (nothing moves, nothing else is covered)
    let holder = dropbox.parentElement.classList.contains("import-drop-holder") ? dropbox.parentElement : null;
    if (!holder) {
        holder = document.createElement("div");
        holder.className = "import-drop-holder";
        dropbox.insertAdjacentElement("beforebegin", holder);
        holder.appendChild(dropbox);
    }
    const cover = document.createElement("div");
    cover.className = "import-cover";
    cover.setAttribute("role", "status");
    cover.setAttribute("aria-live", "polite");
    cover.innerHTML = `<span class="import-spinner" aria-hidden="true"></span><span class="import-cover-text"></span><span class="import-cover-sub"></span><span class="import-cover-actions"></span>`;
    holder.appendChild(cover);
    const coverText = cover.querySelector(".import-cover-text"), coverSub = cover.querySelector(".import-cover-sub"), actions = cover.querySelector(".import-cover-actions");
    const button = (label, cls, run, ic) => { const b = document.createElement("button"); b.type = "button"; b.className = cls; if (ic) b.innerHTML = icon(ic); b.append(label); b.addEventListener("click", run); return b; };
    let lastSay = 0;
    const say = (message) => {
        // a message at most every 120 ms: a long OCR or many parts never make the page busy with redrawing
        const now = Date.now();
        if (now - lastSay < 120 && !/done|ready|failed/i.test(message)) return;
        lastSay = now;
        coverSub.textContent = message;
    };
    const title = document.title;
    const onLeave = (event) => { event.preventDefault(); event.returnValue = ""; };
    window.addEventListener("beforeunload", onLeave);
    const onVisible = () => { if (!document.hidden) document.title = title; };
    document.addEventListener("visibilitychange", onVisible);

    const finish = (message, failed) => {
        window.removeEventListener("beforeunload", onLeave);
        document.removeEventListener("visibilitychange", onVisible);
        document.title = title;
        cover.remove();
        delete dropbox.dataset.importBusy;
        dropbox.disabled = false;
        if (choose) choose.disabled = false;
        report(message, failed);
    };
    const cancel = () => {
        job.cancelled = true;
        job.stop.abort();
        finish(`Reading "${spec.file.name}" was cancelled. Nothing was saved.`, false);
    };

    const reading = () => {
        cover.classList.remove("ready");
        coverText.textContent = `Reading "${spec.file.name}"`;
        coverSub.textContent = "This may take a while for a long file. You can keep working meanwhile.";
        actions.replaceChildren(button("Cancel", "btn", cancel, "x"));
    };
    const counts = (draft) => spec.review.lists.map((list) => { const n = (draft[list.key] || []).length; return n ? `${n} ${n === 1 ? list.nounOne : list.noun}` : ""; }).filter(Boolean).join(" and ");
    const ready = () => {
        cover.classList.add("ready");
        coverText.textContent = `Ready to check: ${counts(job.result.draft)}`;
        coverSub.textContent = "Nothing is saved until you have checked it.";
        actions.replaceChildren(button("Check and save", "btn primary", openCheck, "check"), button("Discard", "btn", discard, "trash"));
    };
    const discard = async () => {
        if (!(await askConfirm({ title: "Discard what was read?", message: `The records read from "${spec.file.name}" are thrown away. Nothing was saved.`, confirmLabel: "Discard", cancelLabel: "Keep them", danger: true }))) return;
        finish(`Discarded. Nothing from "${spec.file.name}" was saved.`, false);
    };

    async function run() {
        reading();
        try {
            job.text = await spec.read(spec.file, say);
            if (job.cancelled) return;
            await extractNow(job.text);
        } catch (error) {
            if (job.cancelled) return;
            const reason = error && error.name === "AbortError" ? "The AI took too long." : (error && error.message) || "Something went wrong.";
            finish(`The file was not read. ${reason} Nothing was saved.`, true);
            toast(`"${spec.file.name}" was not read. ${reason}`, "error");
        }
    }
    async function extractNow(text) {
        const result = await spec.extract(text, say, job.stop.signal);
        if (job.cancelled) return;
        if (result.refused) { finish(result.refused, true); toast(`"${spec.file.name}" cannot be imported here. The reason is under the drop box.`, "error"); return; }
        const total = spec.review.lists.reduce((n, list) => n + (result.draft[list.key] || []).length, 0);
        if (total === 0) { finish((result.notes || []).join(" ") || "No records were found in this file. Nothing was saved.", true); return; }
        // every record starts ticked; the window shows what the page knows about it (already listed, flagged ...)
        spec.review.lists.forEach((list) => (result.draft[list.key] || []).forEach((record) => { record.__keep = true; }));
        job.result = result;
        ready();
        if (document.hidden) document.title = "✓ File read · " + title;
        toast(`"${spec.file.name}" is read: ${counts(result.draft)} to check.`, "ok", { label: "Check now", run: openCheck });
    }

    // ---- 3. the check window ----
    let windowOpen = false;
    function openCheck() {
        if (windowOpen || !job.result) return;
        windowOpen = true;
        // the "is read" message has done its work: it must not lie over the window's buttons
        document.querySelectorAll("#toastHost .toast").forEach((t) => { if (t.textContent.includes(`"${spec.file.name}" is read`)) t.remove(); });
        const draft = job.result.draft;
        const lists = spec.review.lists.filter((list) => (draft[list.key] || []).length > 0);
        let tab = lists[0].key, page = 0, saving = false;
        const overlay = document.createElement("div");
        overlay.className = "modal-overlay";
        overlay.style.display = "flex";
        overlay.innerHTML = `
            <div class="modal-content xl import-check" role="dialog" aria-modal="true" aria-labelledby="icTitle">
                <div class="xl-head">
                    <div class="ic-head-text"><h3 id="icTitle"></h3><p class="modal-intro ic-intro">Check every record against your file. Change what is wrong; untick what should not be saved. Nothing is in the database yet.</p></div>
                    <button type="button" class="icon-btn" data-later aria-label="Close. The records keep waiting in the drop box.">${icon("x")}</button>
                </div>
                <div class="segmented ic-tabs" role="tablist"></div>
                <div class="xl-body"></div>
                <div class="xl-foot">
                    <span class="field-hint ic-notes"></span>
                    <span class="spacer"></span>
                    <button type="button" class="btn danger" data-discard>${icon("trash")}Discard</button>
                    <button type="button" class="cancel-btn" data-later>Later</button>
                    <button type="button" class="save-btn" data-save></button>
                </div>
            </div>`;
        document.body.appendChild(overlay);
        const box = overlay.querySelector(".import-check"), body = box.querySelector(".xl-body"), tabs = box.querySelector(".ic-tabs");
        box.querySelector("#icTitle").textContent = `${spec.review.title}: ${spec.file.name}`;
        box.querySelector(".ic-notes").textContent = [job.result.model ? `Read by ${job.result.model}.` : "Read straight from the file, no AI."].concat(job.result.notes || []).join(" ");

        const kept = () => lists.reduce((n, list) => n + draft[list.key].filter((r) => r.__keep).length, 0);
        const paintSave = () => { const n = kept(); const b = box.querySelector("[data-save]"); b.textContent = n ? `Save ${n} as Pending` : "Nothing ticked"; b.disabled = n === 0 || saving; };
        const close = () => { document.removeEventListener("keydown", onKey, true); overlay.remove(); windowOpen = false; };
        const later = () => { if (saving) return; close(); };
        // Escape is for the window on top only (a "Discard ...?" question above this one answers it itself)
        const onTop = () => Array.from(document.querySelectorAll(".modal-overlay")).filter((o) => getComputedStyle(o).display !== "none").pop() === overlay;
        const onKey = (event) => { if (event.key === "Escape" && onTop()) { event.stopPropagation(); later(); } };
        document.addEventListener("keydown", onKey, true);
        // the backdrop does nothing: a stray click must not close a long check (X, Later and Escape do)
        box.querySelectorAll("[data-later]").forEach((b) => b.addEventListener("click", later));
        box.querySelector("[data-discard]").addEventListener("click", async () => {
            if (saving) return;
            if (!(await askConfirm({ title: "Discard what was read?", message: job.edited ? "Your changes and the records are thrown away. Nothing was saved." : "The records are thrown away. Nothing was saved.", confirmLabel: "Discard", cancelLabel: "Keep checking", danger: true }))) return;
            close();
            finish(`Discarded. Nothing from "${spec.file.name}" was saved.`, false);
        });
        box.querySelector("[data-save]").addEventListener("click", async () => {
            if (saving) return;
            saving = true;
            box.classList.add("is-busy");
            box.querySelectorAll("[data-later], [data-discard]").forEach((b) => { b.disabled = true; });
            paintSave();
            const chosen = {};
            Object.keys(draft).forEach((key) => {
                chosen[key] = Array.isArray(draft[key]) ? draft[key].filter((r) => !r || typeof r !== "object" || r.__keep !== false).map((r) => { if (r && typeof r === "object") { const copy = Object.assign({}, r); delete copy.__keep; return copy; } return r; }) : draft[key];
            });
            try {
                const done = await spec.save(chosen, (message) => { box.querySelector(".ic-notes").textContent = message; });
                close();
                finish(done, false);
            } catch (error) {
                saving = false;
                box.classList.remove("is-busy");
                box.querySelectorAll("[data-later], [data-discard]").forEach((b) => { b.disabled = false; });
                box.querySelector(".ic-notes").textContent = "Not saved: " + ((error && error.message) || "something went wrong") + ". Try again.";
                paintSave();
            }
        });

        const paintTabs = () => {
            tabs.replaceChildren(...lists.map((list) => tabButton(list.key, `${list.label} (${draft[list.key].length})`)), tabButton("__text", "Text from the file"));
        };
        const tabButton = (key, label) => {
            const b = document.createElement("button");
            b.type = "button"; b.className = "seg" + (tab === key ? " on" : ""); b.setAttribute("role", "tab"); b.setAttribute("aria-selected", String(tab === key));
            b.textContent = label;
            b.addEventListener("click", () => { tab = key; page = 0; paintTabs(); paintBody(); });
            return b;
        };
        const paintBody = () => {
            body.scrollTop = 0;
            if (tab === "__text") return paintText();
            const list = lists.find((l) => l.key === tab), records = draft[list.key];
            const pages = Math.max(1, Math.ceil(records.length / PAGE_SIZE));
            page = Math.min(page, pages - 1);
            const bar = document.createElement("div");
            bar.className = "ic-bar";
            const all = button("Tick all", "row-btn", () => { records.forEach((r) => { r.__keep = true; }); job.edited = true; paintBody(); paintSave(); });
            const none = button("Untick all", "row-btn", () => { records.forEach((r) => { r.__keep = false; }); job.edited = true; paintBody(); paintSave(); });
            const where = document.createElement("span"); where.className = "field-hint";
            where.textContent = `${records.filter((r) => r.__keep).length} of ${records.length} ticked`;
            bar.append(all, none, where);
            if (pages > 1) {
                const pager = document.createElement("span"); pager.className = "ic-pager";
                const prev = button("", "icon-btn small", () => { page--; paintBody(); }, "left"); prev.disabled = page === 0; prev.setAttribute("aria-label", "Previous page");
                const next = button("", "icon-btn small", () => { page++; paintBody(); }, "right"); next.disabled = page >= pages - 1; next.setAttribute("aria-label", "Next page");
                const label = document.createElement("span"); label.textContent = `Page ${page + 1} of ${pages}`;
                pager.append(prev, label, next);
                bar.append(pager);
            }
            const rows = records.slice(page * PAGE_SIZE, (page + 1) * PAGE_SIZE).map((record, i) => rowOf(list, record, page * PAGE_SIZE + i, where));
            body.replaceChildren(bar, ...rows);
        };
        const rowOf = (list, record, index, where) => {
            const row = document.createElement("article");
            row.className = "ic-row" + (record.__keep ? "" : " off");
            const head = document.createElement("label");
            head.className = "ic-row-head";
            const tick = document.createElement("input"); tick.type = "checkbox"; tick.checked = !!record.__keep;
            tick.setAttribute("aria-label", "Save this record");
            const name = document.createElement("b"); name.textContent = `${index + 1}. ${list.titleOf(record) || "(no name)"}`;
            head.append(tick, name);
            const note = list.noteOf ? list.noteOf(record) : "";
            if (note) { const chip = document.createElement("span"); chip.className = "chip warn"; chip.textContent = note; head.append(chip); }
            tick.addEventListener("change", () => {
                record.__keep = tick.checked; job.edited = true;
                row.classList.toggle("off", !tick.checked);
                where.textContent = `${list.key in draft ? draft[list.key].filter((r) => r.__keep).length : 0} of ${draft[list.key].length} ticked`;
                paintSave();
            });
            const fields = document.createElement("div");
            fields.className = "ic-fields";
            list.fields.forEach((f) => {
                const wrap = document.createElement("label");
                wrap.className = "field" + (f.wide ? " wide" : "");
                const caption = document.createElement("span"); caption.className = "field-label"; caption.textContent = f.label;
                let input;
                if (f.kind === "select") {
                    input = document.createElement("select");
                    f.options.forEach(([value, text]) => { const o = document.createElement("option"); o.value = value; o.textContent = text; input.appendChild(o); });
                    input.value = String(record[f.key] || "");
                } else if (f.kind === "lines" || f.kind === "area") {
                    input = document.createElement("textarea");
                    input.rows = f.kind === "lines" ? Math.min(6, Math.max(2, (record[f.key] || []).length)) : 2;
                    input.value = f.kind === "lines" ? (Array.isArray(record[f.key]) ? record[f.key] : []).join("\n") : String(record[f.key] || "");
                    if (f.kind === "lines") input.placeholder = "One per line";
                } else {
                    input = document.createElement("input"); input.type = "text"; input.autocomplete = "off";
                    input.value = String(record[f.key] == null ? "" : record[f.key]);
                }
                input.addEventListener("input", () => {
                    record[f.key] = f.kind === "lines" ? input.value.split(/\r?\n/).map((v) => v.trim()).filter(Boolean) : input.value;
                    job.edited = true;
                    if (f === list.fields[0] || f.key === list.titleKey) name.textContent = `${index + 1}. ${list.titleOf(record) || "(no name)"}`;
                });
                input.addEventListener("change", () => { if (f.kind === "select") { record[f.key] = input.value; job.edited = true; } });
                wrap.append(caption, input);
                fields.appendChild(wrap);
            });
            row.append(head, fields);
            return row;
        };
        const paintText = () => {
            const hint = document.createElement("p");
            hint.className = "field-hint";
            hint.textContent = "This is the text that was read from your file. If it was read wrongly (a scanned page, a broken table), correct it here and read it again: the records are made anew from it.";
            const area = document.createElement("textarea");
            area.className = "ic-text";
            area.value = job.text;
            area.spellcheck = false;
            const again = button("Read again from this text", "btn", async () => {
                if (job.edited && !(await askConfirm({ title: "Read the text again?", message: "The records are made anew from the text. Changes you made to the records are lost.", confirmLabel: "Read again", cancelLabel: "Keep my records" }))) return;
                again.disabled = true;
                box.classList.add("is-busy");
                box.querySelector(".ic-notes").textContent = "Reading the text again...";
                try {
                    const result = await spec.extract(area.value, (m) => { box.querySelector(".ic-notes").textContent = m; }, job.stop.signal);
                    if (result.refused) throw new Error(result.refused);
                    spec.review.lists.forEach((list) => (result.draft[list.key] || []).forEach((r) => { r.__keep = true; }));
                    job.text = area.value; job.result = result; job.edited = false;
                    close();
                    ready();
                    openCheck();
                } catch (error) {
                    box.classList.remove("is-busy");
                    again.disabled = false;
                    box.querySelector(".ic-notes").textContent = "Not read again: " + ((error && error.message) || "something went wrong") + ".";
                }
            });
            body.replaceChildren(hint, area, again);
        };
        paintTabs();
        paintBody();
        paintSave();
        box.querySelector("[data-save]").focus();
    }

    run();
    return { cancel };
}
