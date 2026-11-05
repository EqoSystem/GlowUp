// The editable lists an ingredient can point at: Allergen Family, Highlights and Skin Conditions.
//
// They live in ONE document, config/lists (see listRules.js for its shape and the rules about ids). The website reads it
// once per page (1 read, counted by the [reads] counter) and the phone app reads it about once per list check.
// While the document does not exist, the built-in lists are used (the same as the app's own), so nothing breaks before the
// first save. Only an admin can write it (firestore.rules).
//
//   loadLists()            - the three lists (built-in when the document is missing or cannot be read)
//   optionsOf(kind, keep)  - [{ value: id, label }] for a picker: not retired, plus the retired ones in `keep`
//   openListManager(kind)  - the "Edit list" window: add, rename, reorder, retire / restore, delete, with look-alike checks
//
// Everything typed is put on the page with textContent or .value, never as HTML.
import { db } from "../_Database/firebase-console.js";
import { doc, getDoc, serverTimestamp } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { noteReads, setDoc } from "./adminStore.js";
import { dialogFrame, icon, toast, askConfirm } from "./adminShell.js";
import { BUILT_IN, MAX_ITEMS, MAX_LABEL, normalizeLists, cleanItem, slugId, checkItem, keepOldName, usageCount, BUILT_IN_HIGHLIGHTS } from "./listRules.js";

export const KINDS = {
    allergens: { title: "Allergen families", one: "allergen family", words: true, intro: "A user who picked one of these in the app is warned about every ingredient marked with it. The words are what the app looks for in the user's own text." },
    conditions: { title: "Skin conditions", one: "skin condition", words: true, intro: "Each condition can be marked Avoid or Good on an ingredient. The words are what the app looks for in a user's saved condition." },
    highlights: { title: "Highlights", one: "highlight", words: false, intro: "A highlighted ingredient shows up among the chips of a scan result, even when it is safe. Key active and Umbrella term are built in." }
};

let current = null;                                   // { version, exists, allergens, conditions, highlights }

const copyLists = (lists) => ({ version: lists.version, exists: lists.exists, allergens: lists.allergens.map((i) => cleanItem(i)), conditions: lists.conditions.map((i) => cleanItem(i)), highlights: lists.highlights.map((i) => cleanItem(i)) });

/** The lists. Read from the database once (and again with `force`); the built-in ones when that is not possible. */
export async function loadLists(force) {
    if (current && !force) return copyLists(current);
    try {
        const snapshot = await getDoc(doc(db, "config", "lists"));
        noteReads("config/lists", 1);
        current = Object.assign(normalizeLists(snapshot.exists() ? snapshot.data() : null), { exists: snapshot.exists() });
    } catch (error) {
        console.warn("The lists could not be read; the built-in lists are used:", error);
        if (!current) current = Object.assign(normalizeLists(null), { exists: false, failed: true });
    }
    return copyLists(current);
}

/** The lists as last loaded (built-in ones before the first load). */
export const listsNow = () => copyLists(current || Object.assign(normalizeLists(null), { exists: false }));

/** The label of an id ("fragrance" -> "Fragrance / Parfum"); the id itself when it is not in the list. */
export function labelOf(kind, id) {
    const found = (current ? current[kind] : BUILT_IN[kind] || []).find((item) => item.id.toLowerCase() === String(id).toLowerCase());
    return found ? found.label : String(id);
}

/** Every item of a list as [{ value, label }], retired ones too: to name what is already saved on an ingredient. */
export function allOptions(kind) {
    return (current ? current[kind] : normalizeLists(null)[kind]).map((item) => ({ value: item.id, label: item.label }));
}

/** Options for a picker: [{ value, label }]. A retired item is offered only when `keep` (ids already on the ingredient) holds it. */
export function optionsOf(kind, keep) {
    const kept = new Set((keep || []).map((id) => String(id).toLowerCase()));
    return (current ? current[kind] : normalizeLists(null)[kind])
        .filter((item) => !item.retired || kept.has(item.id.toLowerCase()))
        .map((item) => ({ value: item.id, label: item.retired ? item.label + " (retired)" : item.label }));
}

/** Saves the three lists. Stops when somebody else saved in between (the version moved on). */
async function saveLists(next) {
    const ref = doc(db, "config", "lists");
    const latest = await getDoc(ref);
    noteReads("config/lists", 1);
    const serverVersion = latest.exists() ? Math.max(0, Math.floor(Number(latest.data().version) || 0)) : 0;
    if (serverVersion !== current.version) { await loadLists(true); throw new Error("The lists were changed somewhere else in the meantime. They were loaded again: look at them and repeat your change."); }
    const version = serverVersion + 1;
    const body = (items) => items.map((item) => ({ id: item.id, label: item.label, keywords: item.keywords, retired: item.retired === true }));
    await setDoc(ref, { version, updatedAt: serverTimestamp(), allergens: body(next.allergens), conditions: body(next.conditions), highlights: body(next.highlights) });
    current = Object.assign(normalizeLists({ version, allergens: next.allergens, conditions: next.conditions, highlights: next.highlights }), { exists: true });
}

// ---------------------------------------------------------------------------------------------
// The "Edit list" window
// ---------------------------------------------------------------------------------------------
const make = (tag, className, content) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (content !== undefined) node.textContent = content;
    return node;
};
const iconButton = (name, label, onClick) => {
    const button = make("button", "icon-btn small");
    button.type = "button";
    button.setAttribute("aria-label", label);
    button.setAttribute("data-tip", label);
    button.innerHTML = icon(name);
    button.addEventListener("click", onClick);
    return button;
};

/**
 * Opens the window for one list. `context.ingredients()` gives the website's ingredients (to count who uses an item);
 * `context.onSaved(kind)` is called after a save so the page can draw its pickers again. Resolves when the window closes.
 */
export function openListManager(kind, context) {
    const spec = KINDS[kind];
    return new Promise((resolve) => {
        const work = current[kind].map((item) => Object.assign({}, item, { keywords: item.keywords.slice() }));
        const firstLabels = new Map(work.map((item) => [item.id, item.label]));
        let dirty = false;
        let pendingWarning = "";                        // the add form: a look-alike was shown once; the same name again means "add anyway"

        const finish = () => { frame.close(); resolve(); };
        const tryClose = async () => {
            if (dirty && !(await askConfirm({ title: "Leave without saving?", message: "Your changes to this list are not saved yet.", confirmLabel: "Leave", danger: true }))) return;
            finish();
        };
        const frame = dialogFrame(spec.title, () => { tryClose(); });
        frame.box.classList.add("wide", "lists-box");
        const intro = make("p", "modal-intro", spec.intro);
        const reach = make("p", "field-hint", "Phones take over a change at their next list check (within about 10 minutes). Retire an item instead of deleting it when users may have chosen it already.");
        const rows = make("ul", "lists-rows");
        const note = make("p", "form-error lists-note");
        note.setAttribute("role", "alert");
        const addBox = make("div", "lists-add");
        const addLabel = make("input"); addLabel.type = "text"; addLabel.maxLength = MAX_LABEL; addLabel.placeholder = `New ${spec.one}`; addLabel.autocomplete = "off"; addLabel.setAttribute("aria-label", `Name of the new ${spec.one}`);
        const addWords = make("input"); addWords.type = "text"; addWords.maxLength = 120; addWords.placeholder = "Words to look for, separated by commas (optional)"; addWords.autocomplete = "off"; addWords.setAttribute("aria-label", "Words the app looks for");
        const addButton = make("button", "btn primary", "Add"); addButton.type = "button";
        addBox.append(addLabel);
        if (spec.words) addBox.append(addWords);
        addBox.append(addButton);
        const addNote = make("p", "field-hint lists-add-note");
        addNote.setAttribute("role", "status");

        const actions = make("div", "modal-actions");
        const cancel = make("button", "cancel-btn", "Close"); cancel.type = "button";
        const save = make("button", "save-btn", "Save list"); save.type = "button"; save.disabled = true;
        actions.append(cancel, save);
        frame.box.append(intro, reach, rows, addBox, addNote, note, actions);

        const mark = () => { dirty = true; save.disabled = false; note.textContent = ""; };
        const words = (value) => String(value || "").split(",").map((w) => w.trim().toLowerCase()).filter((w) => w.length >= 2);
        const usedBy = (item) => usageCount(kind, item.id, context.ingredients());
        const builtIn = (item) => kind === "highlights" && BUILT_IN_HIGHLIGHTS.includes(item.id);

        function paint() {
            rows.replaceChildren();
            work.forEach((item, index) => {
                const row = make("li", "lists-row" + (item.retired ? " retired" : ""));
                const move = make("span", "lists-move");
                const up = iconButton("up", `Move ${item.label} up`, () => { if (index > 0) { work.splice(index - 1, 0, work.splice(index, 1)[0]); mark(); paint(); } });
                const down = iconButton("down", `Move ${item.label} down`, () => { if (index < work.length - 1) { work.splice(index + 1, 0, work.splice(index, 1)[0]); mark(); paint(); } });
                up.disabled = index === 0; down.disabled = index === work.length - 1;
                move.append(up, down);

                const label = make("input", "lists-label"); label.type = "text"; label.value = item.label; label.maxLength = MAX_LABEL; label.autocomplete = "off";
                label.setAttribute("aria-label", `Name of ${item.label}`);
                label.addEventListener("input", () => { item.label = label.value; mark(); });
                label.addEventListener("change", () => {
                    const verdict = checkItem(work, label.value, item.keywords, item.id);
                    if (verdict.error) { note.textContent = verdict.error; label.value = item.label = firstLabels.get(item.id) || item.label; }
                    else if (verdict.warnings.length) note.textContent = "Saved as typed, but: " + verdict.warnings.join(" ");
                    else note.textContent = "";
                });
                if (builtIn(item)) { label.readOnly = true; }

                const middle = make("span", "lists-mid");
                middle.appendChild(label);
                if (spec.words) {
                    const keys = make("input", "lists-keys"); keys.type = "text"; keys.value = item.keywords.join(", "); keys.maxLength = 120; keys.autocomplete = "off";
                    keys.placeholder = "Words to look for"; keys.setAttribute("aria-label", `Words the app looks for in ${item.label}`);
                    keys.addEventListener("input", () => { item.keywords = words(keys.value); mark(); });
                    middle.appendChild(keys);
                }

                const count = usedBy(item);
                const meta = make("span", "lists-meta");
                meta.append(make("code", "", item.id), make("span", "", count === 0 ? "not used" : `used by ${count}`));

                const buttons = make("span", "lists-actions");
                if (!builtIn(item)) {
                    const retire = make("button", "row-btn", item.retired ? "Restore" : "Retire"); retire.type = "button";
                    retire.addEventListener("click", () => { item.retired = !item.retired; mark(); paint(); });
                    const del = make("button", "row-btn danger", "Delete"); del.type = "button";
                    del.disabled = count > 0;
                    del.addEventListener("click", async () => {
                        if (!(await askConfirm({ title: `Delete "${item.label}"?`, message: "No ingredient uses it. Users who saved it keep their own text, but the app no longer knows it as a listed item.", confirmLabel: "Delete", danger: true }))) return;
                        work.splice(work.indexOf(item), 1); mark(); paint();
                    });
                    buttons.append(retire, del);
                } else buttons.append(make("span", "field-hint", "built in"));
                row.append(move, middle, meta, buttons);
                rows.appendChild(row);
            });
        }

        addButton.addEventListener("click", () => {
            const label = addLabel.value.replace(/\s+/g, " ").trim();
            const keys = words(addWords.value);
            if (work.length >= MAX_ITEMS) { addNote.textContent = `A list holds at most ${MAX_ITEMS} items.`; return; }
            const verdict = checkItem(work, label, keys, null);
            if (verdict.error) { addNote.textContent = verdict.error; pendingWarning = ""; addButton.textContent = "Add"; return; }
            const signature = label.toLowerCase() + "|" + keys.join(",");
            if (verdict.warnings.length && pendingWarning !== signature) {
                pendingWarning = signature;
                addNote.textContent = verdict.warnings.join(" ") + " Press the button again to add it anyway.";
                addButton.textContent = "Add anyway";
                return;
            }
            work.push({ id: slugId(label, work.map((item) => item.id)), label, keywords: spec.words ? (keys.length ? keys : [label.toLowerCase()]) : [], retired: false });
            addLabel.value = ""; addWords.value = ""; addNote.textContent = ""; pendingWarning = ""; addButton.textContent = "Add";
            mark(); paint();
            addLabel.focus();
        });
        [addLabel, addWords].forEach((field) => {
            field.addEventListener("input", () => { pendingWarning = ""; addButton.textContent = "Add"; addNote.textContent = ""; });
            field.addEventListener("keydown", (event) => { if (event.key === "Enter") { event.preventDefault(); addButton.click(); } });
        });

        cancel.addEventListener("click", () => { tryClose(); });
        save.addEventListener("click", async () => {
            // every name must pass: nothing empty, nothing the same as another
            for (const item of work) {
                const verdict = checkItem(work, item.label, item.keywords, item.id);
                if (verdict.error) { note.textContent = verdict.error; return; }
            }
            const next = work.map((item) => {
                const clean = cleanItem({ id: item.id, label: item.label, keywords: item.keywords, retired: item.retired });
                if (!clean) return null;
                if (spec.words) keepOldName(clean, firstLabels.get(item.id));
                if (spec.words && clean.keywords.length === 0) clean.keywords = [clean.label.toLowerCase()];
                return clean;
            });
            if (next.some((item) => !item)) { note.textContent = "One of the names cannot be saved. Check them."; return; }
            save.disabled = true; save.textContent = "Saving...";
            try {
                await saveLists(Object.assign({}, current, { [kind]: next }));
                dirty = false;
                toast(`${spec.title} saved. The app takes them over at its next list check.`, "ok");
                if (context.onSaved) await context.onSaved(kind);
                finish();
            } catch (error) {
                console.error("The list could not be saved:", error);
                note.textContent = (error && error.message) || "The list could not be saved.";
                save.disabled = false; save.textContent = "Save list";
            }
        });

        paint();
        addLabel.focus();
    });
}
