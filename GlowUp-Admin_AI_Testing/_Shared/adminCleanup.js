// Two dialogs the Brand & Product Inventory and the Ingredient Curator share:
//
//   openPlanDialog       - "Clean up": the steps of a clean-up (see glowupCatalog.js, cleanupPlan), each with the list of
//                          changes it would make. The admin ticks rows and presses "Apply" on one step at a time.
//   openDuplicatesDialog - "Find duplicates": groups of entries that are (or may be) the same thing. Per group the admin
//                          chooses which entry to keep and merges the others into it, or says they are not the same.
//
// Nothing here reads or writes the database: the page gives the lists and does the saving. Nothing is ever deleted by
// these dialogs: an entry that is removed goes to the Recycle bin. Every change is shown first and asked about.
// Everything a person typed (names of products, brands, ingredients) is written with textContent, never as HTML.
import { icon, toast, askConfirm } from "./adminShell.js";

const many = (n, one, more) => `${n} ${n === 1 ? one : more}`;
const KIND = { brands: ["brand", "brands"], products: ["product", "products"], ingredients: ["ingredient entry", "ingredient entries"] };
const FIELD = { cosmeticType: "kind", brandName: "brand", brandId: "brand", aliases: "other names", scans: "scans", ingredients: "ingredient list", sources: "sources", barcode: "barcode", shades: "shades", category: "category", name: "name" };

function make(tag, className, textContent) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (textContent !== undefined && textContent !== null) node.textContent = textContent;
    return node;
}

/** The frame of a big dialog. Esc, the X and a click on the dark area close it (not while something is being saved). */
/**
 * A small window that says a scan is running, with a bar and a Cancel button. Shown at once: the work behind it is cut into slices (see
 * ingredientLookalikesAsync), so the bar really moves. update(done, total) moves it; close() removes it; `cancelled()` is true after Cancel.
 */
export function showScan(title, text, onCancel) {
    const overlay = make("div", "modal-overlay");
    overlay.style.display = "flex";
    const box = make("div", "modal-content");
    box.setAttribute("role", "alertdialog");
    box.setAttribute("aria-modal", "true");
    box.setAttribute("aria-busy", "true");
    const status = make("p", "scan-status", text);
    status.setAttribute("role", "status");
    const track = make("div", "scan-track");
    const fill = make("div", "scan-fill");
    track.append(fill);
    const actions = make("div", "modal-actions");
    const cancel = make("button", "cancel-btn", "Cancel");
    cancel.type = "button";
    actions.append(cancel);
    box.append(make("h3", "", title), status, track, actions);
    overlay.append(box);
    document.body.appendChild(overlay);
    let stopped = false;
    const stop = () => { stopped = true; cancel.disabled = true; cancel.textContent = "Cancelling..."; if (onCancel) onCancel(); };
    cancel.addEventListener("click", stop);
    const onKey = (event) => { if (event.key === "Escape") { event.stopPropagation(); stop(); } };
    document.addEventListener("keydown", onKey, true);
    cancel.focus();
    return {
        update(done, total) { fill.style.width = Math.min(100, Math.round((done / Math.max(1, total)) * 100)) + "%"; status.textContent = text.replace("{done}", done).replace("{total}", total); },
        close() { document.removeEventListener("keydown", onKey, true); overlay.remove(); },
        cancelled: () => stopped
    };
}

function bigDialog(title, intro, onClose) {
    const overlay = make("div", "modal-overlay");
    overlay.style.display = "flex";
    const box = make("div", "modal-content xl");
    box.setAttribute("role", "dialog");
    box.setAttribute("aria-modal", "true");
    const head = make("div", "xl-head");
    const titles = make("div", "xl-titles");
    titles.append(make("h3", "", title));
    if (intro) titles.append(make("p", "modal-intro", intro));
    const x = make("button", "icon-btn");
    x.type = "button"; x.setAttribute("aria-label", "Close"); x.innerHTML = icon("x");
    head.append(titles, x);
    const body = make("div", "xl-body");
    const foot = make("div", "xl-foot");
    box.append(head, body, foot);
    overlay.appendChild(box);
    document.body.appendChild(overlay);

    let busy = false;
    const close = () => { if (busy) return; document.removeEventListener("keydown", onKey, true); overlay.remove(); if (onClose) onClose(); };
    const onKey = (event) => { if (event.key === "Escape" && !document.querySelector(".modal-overlay:not(:has(.xl))[style*=flex]")) { event.stopPropagation(); close(); } };
    overlay.addEventListener("mousedown", (event) => { if (event.target === overlay) close(); });
    document.addEventListener("keydown", onKey, true);
    x.addEventListener("click", close);
    return { box, body, foot, close, setBusy: (value) => { busy = value; box.classList.toggle("is-busy", value); } };
}

/** A tick box that looks like the page's own (the .pick style). */
function tickBox(label, on) {
    const box = make("button", "pick");
    box.type = "button";
    box.setAttribute("role", "checkbox");
    box.setAttribute("aria-label", label);
    box.setAttribute("aria-checked", on ? "true" : "false");
    return box;
}
const isTicked = (box) => box.getAttribute("aria-checked") === "true";

/** What a change writes, in a few plain words ("kind: Lipstick; other names: A, B"). */
function fieldsInWords(fields) {
    return Object.entries(fields || {}).filter(([key]) => key !== "binnedWith" && key !== "brandId").map(([key, value]) =>
        `${FIELD[key] || key}: ${Array.isArray(value) ? (value.length > 4 ? value.slice(0, 4).join(", ") + ` and ${value.length - 4} more` : value.join(", ")) : value}`).join("; ");
}

// =============================================================================================
//  Clean up
// =============================================================================================
/**
 * options = {
 *   title, intro,
 *   makePlan: () => plan,                          // glowupCatalog.cleanupPlan(...) on the page's data as it is NOW
 *   apply: async (changes, step) => {},            // the page writes these changes and loads its data again
 *   notes: (plan) => [{ title, text, items[] }],   // things a person must decide; shown under the steps (optional)
 *   onClose
 * }
 */
export function openPlanDialog(options) {
    const dialog = bigDialog(options.title || "Clean up", options.intro || "", options.onClose);
    const done = { bin: 0, update: 0, steps: 0 };
    const open = new Set();                       // the steps whose list is shown
    const unticked = new Map();                   // step id -> Set of "collection/id" the admin took the tick off (or, for a careful step, put it ON)

    const keyOf = (change) => change.collection + "/" + change.docId;
    const wantedOf = (step) => {
        const flipped = unticked.get(step.id) || new Set();
        // a careful step starts with nothing ticked: there the set holds what WAS ticked
        return step.changes.filter((change) => (step.careful ? flipped.has(keyOf(change)) : !flipped.has(keyOf(change))));
    };
    const flip = (step, change) => {
        if (!unticked.has(step.id)) unticked.set(step.id, new Set());
        const set = unticked.get(step.id), key = keyOf(change);
        if (set.has(key)) set.delete(key); else set.add(key);
    };

    function draw() {
        let plan;
        try { plan = options.makePlan(); }
        catch (error) { console.error("The clean-up plan could not be made: ", error); dialog.body.replaceChildren(make("p", "table-message error", "The list of changes could not be made. " + (error.message || ""))); return; }
        const pending = plan.steps.filter((step) => step.changes.length > 0);
        dialog.body.replaceChildren();

        if (done.steps > 0) dialog.body.append(make("p", "plan-done", `Done so far: ${many(done.bin, "entry", "entries")} moved to the Recycle bin, ${many(done.update, "entry", "entries")} changed.`));
        if (pending.length === 0) dialog.body.append(make("p", "plan-clean", done.steps > 0 ? "Nothing is left to clean up." : "Nothing to clean up: the rules found nothing to change."));

        plan.steps.forEach((step, index) => {
            if (step.changes.length === 0) return;
            const section = make("section", "plan-step" + (step.careful ? " careful" : ""));
            const head = make("div", "plan-head");
            const toggle = make("button", "plan-toggle");
            toggle.type = "button";
            toggle.innerHTML = icon("down");
            toggle.setAttribute("aria-expanded", open.has(step.id) ? "true" : "false");
            toggle.setAttribute("aria-label", "Show or hide the list");
            const titleBox = make("div", "plan-title");
            titleBox.append(make("b", "", `${index + 1}. ${step.title}`));
            const bins = step.changes.filter((change) => change.action === "bin").length, updates = step.changes.length - bins;
            titleBox.append(make("span", "muted small", [bins > 0 ? many(bins, "entry", "entries") + " to the Recycle bin" : "", updates > 0 ? many(updates, "entry", "entries") + " changed" : ""].filter((t) => t).join(", ")));
            const applyButton = make("button", "btn primary");
            applyButton.type = "button";
            head.append(toggle, titleBox, applyButton);
            section.append(head, make("p", "plan-why", step.why));

            const paintApply = () => {
                const count = wantedOf(step).length;
                applyButton.textContent = count === 0 ? "Tick rows to apply" : `Apply ${count} of ${step.changes.length}`;
                applyButton.disabled = count === 0;
            };
            paintApply();

            const listHost = make("div", "plan-list");
            listHost.hidden = !open.has(step.id);
            const table = make("table", "admin-table plan-table");
            const headRow = make("tr");
            const allCell = make("th");
            const all = tickBox("Tick every row", wantedOf(step).length === step.changes.length);
            allCell.append(all);
            headRow.append(allCell, make("th", "", "Entry"), make("th", "", "What happens"), make("th", "", "Why"));
            const thead = make("thead"); thead.append(headRow);
            const tbody = make("tbody");
            const boxes = [];
            step.changes.forEach((change) => {
                const row = make("tr");
                const tickCell = make("td");
                const box = tickBox("Include " + (change.name || "this entry"), wantedOf(step).includes(change));
                boxes.push([box, change]);
                tickCell.append(box);
                const nameCell = make("td");
                nameCell.append(make("b", "", change.name || "(no name)"));
                const under = [KIND[change.collection][0], change.brand].filter((t) => t).join(" · ");
                nameCell.append(make("div", "muted small", under));
                const whatCell = make("td");
                whatCell.append(make("span", "chip " + (change.action === "bin" ? "bad" : "warn"), change.action === "bin" ? "to the Recycle bin" : "changed"));
                if (change.action === "update") whatCell.append(make("div", "muted small", fieldsInWords(change.fields)));
                row.append(tickCell, nameCell, whatCell, make("td", "plan-reason", change.reason));
                box.addEventListener("click", () => { flip(step, change); box.setAttribute("aria-checked", isTicked(box) ? "false" : "true"); all.setAttribute("aria-checked", wantedOf(step).length === step.changes.length ? "true" : "false"); paintApply(); });
                tbody.append(row);
            });
            all.addEventListener("click", () => {
                const turnOn = wantedOf(step).length !== step.changes.length;
                boxes.forEach(([box, change]) => { if (isTicked(box) !== turnOn) { flip(step, change); box.setAttribute("aria-checked", turnOn ? "true" : "false"); } });
                all.setAttribute("aria-checked", turnOn ? "true" : "false");
                paintApply();
            });
            table.append(thead, tbody);
            const scroller = make("div", "table-container");
            scroller.append(table);
            listHost.append(scroller);
            section.append(listHost);
            toggle.addEventListener("click", () => {
                if (open.has(step.id)) open.delete(step.id); else open.add(step.id);
                listHost.hidden = !open.has(step.id);
                toggle.setAttribute("aria-expanded", open.has(step.id) ? "true" : "false");
            });

            applyButton.addEventListener("click", async () => {
                const changes = wantedOf(step);
                if (changes.length === 0) return;
                const binCount = changes.filter((change) => change.action === "bin").length, updateCount = changes.length - binCount;
                const what = [binCount > 0 ? `move ${many(binCount, "entry", "entries")} to the Recycle bin` : "", updateCount > 0 ? `change ${many(updateCount, "entry", "entries")}` : ""].filter((t) => t).join(" and ");
                if (!(await askConfirm({
                    title: `Apply this step: ${what}?`,
                    message: "Nothing is deleted. What goes to the Recycle bin can be restored from there.",
                    items: changes.map((change) => `${change.name || "(no name)"}${change.brand ? " (" + change.brand + ")" : ""}: ${change.action === "bin" ? "to the Recycle bin" : fieldsInWords(change.fields)}`),
                    confirmLabel: "Apply"
                }))) return;
                dialog.setBusy(true);
                applyButton.disabled = true; applyButton.textContent = "Saving...";
                try {
                    await options.apply(changes, step);
                    done.bin += binCount; done.update += updateCount; done.steps++;
                    unticked.delete(step.id); open.delete(step.id);
                    toast(`Done: ${many(binCount, "entry", "entries")} to the Recycle bin, ${many(updateCount, "entry", "entries")} changed.`, "ok");
                } catch (error) {
                    console.error("Applying the step failed: ", error);
                    toast("The step was not applied completely. " + (error && error.message ? error.message : ""), "error");
                }
                dialog.setBusy(false);
                draw();
            });
            dialog.body.append(section);
        });

        // what the clean-up leaves for a person
        const notes = options.notes ? options.notes(plan) : [];
        notes.filter((note) => note && note.items && note.items.length > 0).forEach((note) => {
            const section = make("section", "plan-step note");
            const fold = make("details");
            const summary = make("summary");
            summary.append(make("b", "", `${note.title}: ${note.items.length}`));
            fold.append(summary, make("p", "plan-why", note.text));
            const list = make("ul", "confirm-list");
            note.items.slice(0, 200).forEach((item) => list.append(make("li", "", item)));
            fold.append(list);
            if (note.action) { const button = make("button", "btn"); button.type = "button"; button.textContent = note.action.label; button.addEventListener("click", () => { dialog.close(); note.action.run(); }); fold.append(button); }
            section.append(fold);
            dialog.body.append(section);
        });
    }

    const closeButton = make("button", "cancel-btn", "Close");
    closeButton.type = "button";
    closeButton.addEventListener("click", () => dialog.close());
    dialog.foot.append(make("span", "muted small", "Each step is applied by itself, after you confirm it. Nothing is deleted."), make("span", "spacer"), closeButton);
    draw();
    return dialog;
}

// =============================================================================================
//  Find duplicates
// =============================================================================================
/**
 * options = {
 *   title, intro, threshold (0 to 1, where to start),
 *   sections: [{
 *       id, label,                                              // "Products", "Brands"
 *       find: (threshold) => ({ sure: [group], possible: [group] }),   // group = { reason, items[], keep, alike }
 *       describe: (item) => ({ title, lines: [text], badges: [{ text, tone }] }),
 *       explainMerge: (keep, others) => [text],                 // what the merge will change, line by line
 *       merge: async (keep, others) => {},                      // the page writes and loads its data again
 *       remove: async (others, keep) => {},                     // the others go to the Recycle bin, nothing is taken over (optional)
 *       whyNotRemove: (item) => "" | "why this one cannot simply be removed"
 *   }],
 *   onClose
 * }
 */
export function openDuplicatesDialog(options) {
    const dialog = bigDialog(options.title || "Find duplicates", options.intro || "", options.onClose);
    const state = { section: options.sections[0].id, threshold: typeof options.threshold === "number" ? options.threshold : 0.85 };
    const tally = { found: 0, merged: 0, removed: 0, skipped: 0 };
    const skipped = new Set();                    // groups the admin said are not the same (until the dialog closes)
    const keepOf = new Map();                     // group signature -> the id chosen to keep
    const signature = (group) => group.items.map((item) => item.id).sort().join("|");

    const bar = make("div", "dup-bar");
    const tabs = make("div", "segmented");
    options.sections.forEach((section) => {
        const tab = make("button", "seg", section.label);
        tab.type = "button"; tab.dataset.section = section.id;
        tab.addEventListener("click", () => { state.section = section.id; draw(); });
        tabs.append(tab);
    });
    const slider = make("input");
    slider.type = "range"; slider.min = "60"; slider.max = "100"; slider.step = "1"; slider.value = String(Math.round(state.threshold * 100));
    slider.setAttribute("aria-label", "How alike two names must be to be listed");
    const sliderLabel = make("label", "dup-slider");
    const sliderText = make("span", "");
    sliderLabel.append(sliderText, slider);
    let sliderTimer = 0;
    slider.addEventListener("input", () => {                       // the number follows the slider at once; the search starts when it rests
        state.threshold = Number(slider.value) / 100;
        sliderText.textContent = `Also list names at least ${Math.round(state.threshold * 100)}% alike`;
        window.clearTimeout(sliderTimer);
        sliderTimer = window.setTimeout(draw, 250);
    });
    if (options.sections.length > 1) bar.append(tabs);
    bar.append(make("span", "spacer"), sliderLabel);
    const list = make("div", "dup-list");
    dialog.body.append(bar, list);

    const summary = make("span", "muted small");
    const closeButton = make("button", "cancel-btn", "Close");
    closeButton.type = "button";
    closeButton.addEventListener("click", () => dialog.close());
    dialog.foot.append(summary, make("span", "spacer"), closeButton);

    let drawing = 0;                                               // the number of the newest search; an older one that comes back late is dropped
    async function draw() {
        const mine = ++drawing;
        const section = options.sections.find((s) => s.id === state.section) || options.sections[0];
        tabs.querySelectorAll(".seg").forEach((tab) => tab.classList.toggle("on", tab.dataset.section === section.id));
        sliderText.textContent = `Also list names at least ${Math.round(state.threshold * 100)}% alike`;
        let found;
        // while the search runs: a bar in the list (real progress), and the controls that would start another one wait
        const waiting = make("div", "scan-inline");
        const waitText = make("p", "scan-status", "Checking the names...");
        waitText.setAttribute("role", "status");
        const track = make("div", "scan-track"), fill = make("div", "scan-fill");
        track.append(fill); waiting.append(waitText, track);
        list.replaceChildren(waiting);
        list.setAttribute("aria-busy", "true");
        tabs.querySelectorAll(".seg").forEach((tab) => { tab.disabled = true; });
        const progress = (done, total) => { fill.style.width = Math.round((done / Math.max(1, total)) * 100) + "%"; waitText.textContent = `Checking the names: ${done} / ${total}`; };
        try { found = await section.find(state.threshold, { onProgress: progress, cancelled: () => mine !== drawing || !document.body.contains(list) }); }
        catch (error) {
            console.error("Looking for duplicates failed: ", error);
            if (mine !== drawing) return;
            list.removeAttribute("aria-busy"); tabs.querySelectorAll(".seg").forEach((tab) => { tab.disabled = false; });
            list.replaceChildren(make("p", "table-message error", "The search for duplicates did not work. Close this window and try again."));    // the real error is in the console
            return;
        }
        if (mine !== drawing || found === null) return;             // a newer search took over (or the window closed): this one is dropped
        list.removeAttribute("aria-busy");
        tabs.querySelectorAll(".seg").forEach((tab) => { tab.disabled = false; });
        const groups = found.sure.map((group) => Object.assign({ sure: true }, group)).concat(found.possible.slice().sort((a, b) => (b.alike || 0) - (a.alike || 0)).map((group) => Object.assign({ sure: false }, group)))
            .filter((group) => !skipped.has(signature(group)));
        tally.found = Math.max(tally.found, found.sure.length + found.possible.length);
        summary.textContent = `${many(groups.length, "group", "groups")} shown · merged ${tally.merged} · removed ${tally.removed} · said "not the same" ${tally.skipped}`;
        list.replaceChildren();
        if (groups.length === 0) { list.append(make("p", "plan-clean", "No duplicates found. Move the slider to the left to list names that are less alike.")); return; }

        groups.forEach((group) => {
            const id = signature(group);
            const keepId = keepOf.has(id) && group.items.some((item) => item.id === keepOf.get(id)) ? keepOf.get(id) : group.keep.id;
            const card = make("section", "dup-group" + (group.sure ? " sure" : ""));
            const head = make("div", "dup-head");
            head.append(make("span", "chip " + (group.sure ? "bad" : "warn"), group.sure ? "the same" : `${Math.round((group.alike || 0) * 100)}% alike`), make("span", "muted small", group.reason));
            card.append(head);
            const rows = make("div", "dup-items");
            group.items.forEach((item) => {
                const shown = section.describe(item);
                const row = make("label", "dup-item" + (item.id === keepId ? " keep" : ""));
                const radio = make("input");
                radio.type = "radio"; radio.name = "keep-" + id; radio.checked = item.id === keepId;
                radio.addEventListener("change", () => { keepOf.set(id, item.id); draw(); });
                const textBox = make("div", "dup-text");
                const titleLine = make("div", "dup-title");
                titleLine.append(make("b", "", shown.title || "(no name)"));
                (shown.badges || []).forEach((badge) => titleLine.append(make("span", "chip " + (badge.tone || ""), badge.text)));
                textBox.append(titleLine);
                (shown.lines || []).filter((line) => line).forEach((line) => textBox.append(make("div", "muted small", line)));
                row.append(radio, textBox, make("span", "dup-role", item.id === keepId ? "keep this one" : ""));
                rows.append(row);
            });
            card.append(rows);

            const keep = group.items.find((item) => item.id === keepId), others = group.items.filter((item) => item.id !== keepId);
            const actions = make("div", "dup-actions");
            const mergeButton = make("button", "btn primary", "Merge into the one kept");
            mergeButton.type = "button";
            mergeButton.addEventListener("click", async () => {
                if (!(await askConfirm({
                    title: `Merge ${many(others.length, "entry", "entries")} into "${section.describe(keep).title}"?`,
                    message: "This is what will change. The entries that are merged go to the Recycle bin and can be restored from there.",
                    items: section.explainMerge(keep, others), confirmLabel: "Merge"
                }))) return;
                await run(() => section.merge(keep, others), () => { tally.merged++; }, "Merged.");
            });
            actions.append(mergeButton);
            if (section.remove) {
                const blocked = others.map((item) => (section.whyNotRemove ? section.whyNotRemove(item) : "")).find((why) => why);
                const removeButton = make("button", "btn danger", "Only remove the others");
                removeButton.type = "button";
                if (blocked) { removeButton.disabled = true; removeButton.dataset.tip = blocked + " Merge them instead."; }
                else removeButton.dataset.tip = "The others go to the Recycle bin; nothing of theirs is taken over. They can be removed for good on the Recycle bin page.";
                removeButton.addEventListener("click", async () => {
                    if (!(await askConfirm({
                        title: `Move ${many(others.length, "entry", "entries")} to the Recycle bin?`,
                        message: `"${section.describe(keep).title}" stays as it is and takes nothing over. The others can be restored from the Recycle bin.`,
                        items: others.map((item) => section.describe(item).title), confirmLabel: "Move to the Recycle bin", danger: true
                    }))) return;
                    await run(() => section.remove(others, keep), () => { tally.removed++; }, "Moved to the Recycle bin.");
                });
                actions.append(removeButton);
            }
            const skipButton = make("button", "btn ghost", "Not the same");
            skipButton.type = "button";
            skipButton.dataset.tip = "Leaves both as they are and takes this group off the list until the dialog is opened again.";
            skipButton.addEventListener("click", () => { skipped.add(id); tally.skipped++; draw(); });
            actions.append(skipButton);
            card.append(actions);
            list.append(card);
        });
    }

    async function run(work, count, message) {
        dialog.setBusy(true);
        try { await work(); count(); toast(message, "ok"); }
        catch (error) { console.error("The change failed: ", error); toast("That did not work. " + (error && error.message ? error.message : ""), "error"); }
        dialog.setBusy(false);
        draw();
    }

    draw();
    return dialog;
}
