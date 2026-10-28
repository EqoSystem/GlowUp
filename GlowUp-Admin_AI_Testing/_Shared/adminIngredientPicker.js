// The ingredient picker of the product form: search the ingredient library as you type, click a result to make it a
// removable pill, and create an ingredient on the spot when the library does not have it.
//
//   - The product's ingredient list stays ONE text box (the textarea): pasting a whole list, "Assist with AI" and saving all
//     keep working on it. The pills are a view of that text, and the search adds to it. A pill that matches an ingredient of the
//     library is solid ("linked"); one that does not is dashed, and clicking it offers to create it.
//   - The search costs no database read: it looks through the ingredients the page already holds.
//   - Everything typed is put on the page with textContent or .value, never as HTML.
//
// Things to change quickly:
import { dialogFrame } from "./adminShell.js";
import { matchesSearch } from "./glowupRules.js";
import { sameKey, toList } from "./adminCommon.js";

export const SHOW_AT_MOST = 8;              // results shown under the search box
export const SEARCH_FROM_LETTERS = 2;       // the search starts at this many letters
export const OFFER_CREATE = true;           // "+ Create new ingredient" when the library has no exact match
export const WORDS = {
    placeholder: "Search the ingredient library, for example Niac...",
    nothing: "No ingredient in the library matches.",
    create: (name) => `+ Create new ingredient "${name}"`,
    linked: "In the ingredient library",
    unlinked: "Not in the ingredient library yet. Click to create it.",
    remove: (name) => `Remove ${name}`,
    note: "Note for this product only (a different warning, or how much of it is in this formula)",
    noteSet: (text) => `Product note: ${text}`
};
export const NOTE_MAX_CHARS = 160;          // longest product-specific caution
export const MAX_NOTES = 80;                // most notes one product keeps

const make = (tag, className, text) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
};

/**
 * options = {
 *   textarea, search, results, pills   - the elements (see the product form)
 *   library()                          - [{ id, name, techName, aliases }] of the ingredients that exist (not in the bin)
 *   create({ name, techName, description, riskLevel })  - saves a new ingredient; resolves to its library entry (or throws)
 *   onChange()                         - called after the list was changed here
 * }
 * Returns { refresh() }: draw the pills again after the text was set by code.
 */
export function mountIngredientPicker(options) {
    const { textarea, search, results, pills } = options;
    search.placeholder = WORDS.placeholder;
    let shown = [], active = -1;
    // product-specific overrides: what is said for THIS product when it differs from the ingredient's own caution note.
    // { ingredient key -> { name, caution, percent } }; the app and the website fall back to the ingredient's own note when there is none.
    const notes = new Map();

    const names = () => toList(textarea.value);
    const write = (list) => {
        textarea.value = list.join(", ");
        textarea.dispatchEvent(new Event("input", { bubbles: true }));       // the page's own summary and the pills follow
    };
    const libraryEntry = (name) => {
        const key = sameKey(name);
        return options.library().find((entry) => [entry.name, entry.techName, ...(entry.aliases || [])].some((n) => sameKey(n) === key)) || null;
    };

    function add(name) {
        const clean = String(name || "").replace(/\s+/g, " ").trim();
        if (!clean) return;
        const list = names();
        if (!list.some((n) => sameKey(n) === sameKey(clean))) list.push(clean);
        write(list);
        closeResults();
        search.value = "";
        if (options.onChange) options.onChange();
    }
    function remove(name) {
        notes.delete(sameKey(name));
        write(names().filter((n) => sameKey(n) !== sameKey(name)));
        if (options.onChange) options.onChange();
    }

    // ---- the pills
    function refresh() {
        pills.replaceChildren();
        names().forEach((name) => {
            const linked = !!libraryEntry(name);
            const pill = make("span", "ing-pill" + (linked ? " linked" : " unlinked"));
            const label = make(linked ? "span" : "button", "ing-pill-name", name);
            if (!linked) { label.type = "button"; label.addEventListener("click", () => openCreate(name)); }
            pill.title = linked ? WORDS.linked : WORDS.unlinked;
            const x = make("button", "ing-pill-x");
            x.type = "button";
            x.setAttribute("aria-label", WORDS.remove(name));
            x.textContent = "×";
            x.addEventListener("click", () => remove(name));
            const own = notes.get(sameKey(name));
            const noteButton = make("button", "ing-pill-note" + (own ? " has" : ""));
            noteButton.type = "button";
            noteButton.textContent = "✎";
            noteButton.setAttribute("aria-label", own ? WORDS.noteSet(own.caution || (own.percent !== null ? own.percent + "%" : "")) : WORDS.note);
            noteButton.title = own ? WORDS.noteSet(own.caution || (own.percent !== null ? own.percent + "%" : "")) : WORDS.note;
            noteButton.addEventListener("click", () => openNote(name));
            pill.append(label, noteButton, x);
            pills.appendChild(pill);
        });
    }
    textarea.addEventListener("input", refresh);

    // ---- the search box
    function closeResults() {
        results.hidden = true; active = -1; shown = [];
        results.replaceChildren();
        search.setAttribute("aria-expanded", "false");
        search.removeAttribute("aria-activedescendant");
    }
    function paintActive() {
        Array.from(results.children).forEach((row, index) => { row.classList.toggle("active", index === active); row.setAttribute("aria-selected", String(index === active)); });
        if (active >= 0 && results.children[active]) search.setAttribute("aria-activedescendant", results.children[active].id);
    }
    function openResults() {
        const typed = search.value.trim();
        results.replaceChildren();
        shown = [];
        if (typed.length < SEARCH_FROM_LETTERS) { closeResults(); return; }
        const have = new Set(names().map(sameKey));
        const found = options.library().filter((entry) => !have.has(sameKey(entry.name)) && matchesSearch(typed, [entry.name, entry.techName, ...(entry.aliases || [])])).slice(0, SHOW_AT_MOST);
        found.forEach((entry, index) => {
            const row = make("li", "ing-result");
            row.id = "ingResult" + index; row.setAttribute("role", "option");
            row.append(make("span", "ing-result-name", entry.name));
            if (entry.techName && sameKey(entry.techName) !== sameKey(entry.name)) row.append(make("span", "ing-result-tech", entry.techName));
            row.addEventListener("mousedown", (event) => { event.preventDefault(); add(entry.name); });
            results.appendChild(row);
            shown.push({ type: "add", name: entry.name });
        });
        if (found.length === 0) results.appendChild(make("li", "ing-result none", WORDS.nothing));
        if (OFFER_CREATE && !libraryEntry(typed)) {
            const row = make("li", "ing-result create", WORDS.create(typed));
            row.id = "ingResultCreate"; row.setAttribute("role", "option");
            row.addEventListener("mousedown", (event) => { event.preventDefault(); closeResults(); openCreate(typed); });
            results.appendChild(row);
            shown.push({ type: "create", name: typed });
        }
        results.hidden = false;
        search.setAttribute("aria-expanded", "true");
        active = shown.length > 0 ? 0 : -1;
        paintActive();
    }
    search.addEventListener("input", openResults);
    search.addEventListener("focus", openResults);
    search.addEventListener("blur", () => setTimeout(closeResults, 120));
    search.addEventListener("keydown", (event) => {
        if (event.key === "ArrowDown" || event.key === "ArrowUp") {
            if (results.hidden) openResults();
            if (shown.length === 0) return;
            event.preventDefault();
            active = (active + (event.key === "ArrowDown" ? 1 : -1) + shown.length) % shown.length;
            paintActive();
        } else if (event.key === "Enter") {
            event.preventDefault();
            const choice = shown[active];
            if (choice && choice.type === "add") add(choice.name);
            else if (choice && choice.type === "create") { closeResults(); openCreate(choice.name); }
            else if (search.value.trim()) add(search.value);                       // nothing offered: the typed words are taken as they are
        } else if (event.key === "Escape" && !results.hidden) {
            event.preventDefault(); event.stopPropagation(); closeResults();
        } else if (event.key === "Backspace" && search.value === "" && names().length > 0) {
            // nothing typed: Backspace takes the last pill back, like a tag box
            const list = names(); list.pop(); write(list);
        }
    });

    // ---- a note for this product only
    function openNote(name) {
        const key = sameKey(name), own = notes.get(key) || { caution: "", percent: null };
        const frame = dialogFrame(name, () => frame.close());
        const intro = make("p", "modal-intro", "Only for this product. Left empty, the product uses the ingredient's own caution note.");
        const form = make("form", "modal-form");
        const cautionInput = make("input"); cautionInput.type = "text"; cautionInput.maxLength = NOTE_MAX_CHARS; cautionInput.value = own.caution || ""; cautionInput.autocomplete = "off";
        cautionInput.placeholder = "For example: Rinse thoroughly after 60 seconds.";
        const percentInput = make("input"); percentInput.type = "number"; percentInput.min = "0"; percentInput.max = "100"; percentInput.step = "0.1";
        percentInput.value = own.percent !== null && own.percent !== undefined ? String(own.percent) : ""; percentInput.placeholder = "For example 2";
        const field = (labelText, input, id) => { const wrap = make("div", "field"); const label = make("label", "", labelText); input.id = id; label.htmlFor = id; wrap.append(label, input); return wrap; };
        const error = make("p", "form-error"); error.setAttribute("role", "alert");
        const actions = make("div", "modal-actions");
        const clear = make("button", "cancel-btn", "Remove note"); clear.type = "button";
        const cancel = make("button", "cancel-btn", "Cancel"); cancel.type = "button";
        const save = make("button", "save-btn", "Save note"); save.type = "submit";
        actions.append(clear, cancel, save);
        form.append(field("Caution for this product", cautionInput, "productNoteCaution"), field("How much of it is in this formula (%)", percentInput, "productNotePercent"), error, actions);
        frame.box.append(intro, form);
        clear.addEventListener("click", () => { notes.delete(key); frame.close(); refresh(); if (options.onChange) options.onChange(); });
        cancel.addEventListener("click", () => frame.close());
        form.addEventListener("submit", (event) => {
            event.preventDefault();
            const caution = cautionInput.value.replace(/\s+/g, " ").trim().slice(0, NOTE_MAX_CHARS);
            const text = percentInput.value.trim();
            const percent = text === "" ? null : Number(text);
            if (percent !== null && !(percent >= 0 && percent <= 100)) { error.textContent = "The share is a number from 0 to 100."; percentInput.focus(); return; }
            if (!caution && percent === null) notes.delete(key);
            else if (notes.size < MAX_NOTES || notes.has(key)) notes.set(key, { name, caution, percent });
            frame.close();
            refresh();
            if (options.onChange) options.onChange();
        });
        cautionInput.focus();
    }

    // ---- "Create on the fly": a small form, then the new ingredient is in the library AND in this product
    function openCreate(startName) {
        const frame = dialogFrame("New ingredient", () => frame.close());
        const note = make("p", "modal-intro", "Saved in the ingredient library at once, so every product can use it. You can add the rest later in the Ingredient Curator.");
        const form = make("form", "modal-form");
        const field = (labelText, input) => { const wrap = make("div", "field"); const label = make("label", "", labelText); input.id = "newIng" + labelText.replace(/\W/g, ""); label.htmlFor = input.id; wrap.append(label, input); return wrap; };
        const nameInput = make("input"); nameInput.type = "text"; nameInput.maxLength = 80; nameInput.value = startName || ""; nameInput.autocomplete = "off";
        const techInput = make("input"); techInput.type = "text"; techInput.maxLength = 120; techInput.autocomplete = "off"; techInput.placeholder = "As printed on labels (empty = the name above)";
        const descInput = make("textarea"); descInput.rows = 3; descInput.maxLength = 600; descInput.placeholder = "What it is and what it does, in plain words.";
        const riskInput = make("select");
        [["", "Let the app decide"], ["safe", "Safe"], ["caution", "Caution"], ["harmful", "Harmful"]].forEach(([value, text]) => { const o = make("option", "", text); o.value = value; riskInput.appendChild(o); });
        const error = make("p", "form-error"); error.setAttribute("role", "alert");
        const actions = make("div", "modal-actions");
        const cancel = make("button", "cancel-btn", "Cancel"); cancel.type = "button";
        const save = make("button", "save-btn", "Create and add"); save.type = "submit";
        actions.append(cancel, save);
        form.append(field("Ingredient name", nameInput), field("INCI name (as printed on labels)", techInput), field("Plain-words definition", descInput), field("How the app judges it", riskInput), error, actions);
        frame.box.append(note, form);
        cancel.addEventListener("click", () => frame.close());
        form.addEventListener("submit", async (event) => {
            event.preventDefault();
            const name = nameInput.value.replace(/\s+/g, " ").trim();
            const description = descInput.value.trim();
            if (name.length < 2) { error.textContent = "Type the ingredient name."; nameInput.focus(); return; }
            if (!description) { error.textContent = "Write a short plain-words definition."; descInput.focus(); return; }
            const existing = libraryEntry(name);
            if (existing) { frame.close(); add(existing.name); return; }              // it was there all along: just use it
            save.disabled = true; save.textContent = "Saving...";
            try {
                const made = await options.create({ name, techName: techInput.value.replace(/\s+/g, " ").trim() || name, description, riskLevel: riskInput.value });
                frame.close();
                // a pill typed with another spelling than the new name now links to it
                const list = names().filter((n) => sameKey(n) !== sameKey(startName || ""));
                if (!list.some((n) => sameKey(n) === sameKey(made && made.name ? made.name : name))) list.push(made && made.name ? made.name : name);
                write(list);
                search.value = "";
                if (options.onChange) options.onChange();
            } catch (failure) {
                console.error("The ingredient could not be created:", failure);
                error.textContent = (failure && failure.message) || "The ingredient could not be saved.";
                save.disabled = false; save.textContent = "Create and add";
            }
        });
        nameInput.focus();
    }

    /** The product-specific notes of the ingredients that are in the list now: [{ name, caution, percent }]. */
    function getNotes() {
        const keys = new Set(names().map(sameKey));
        return Array.from(notes.entries()).filter(([key]) => keys.has(key)).map(([, note]) => ({ name: note.name, caution: note.caution || "", percent: note.percent === undefined ? null : note.percent }));
    }
    /** Loads the notes a product already has (call it when a product is opened, before refresh()). */
    function setNotes(list) {
        notes.clear();
        (Array.isArray(list) ? list : []).slice(0, MAX_NOTES).forEach((item) => {
            if (!item || typeof item !== "object" || !String(item.name || "").trim()) return;
            const percent = item.percent === null || item.percent === undefined || item.percent === "" ? null : Number(item.percent);
            notes.set(sameKey(item.name), { name: String(item.name).trim(), caution: String(item.caution || "").slice(0, NOTE_MAX_CHARS), percent: Number.isFinite(percent) ? percent : null });
        });
        refresh();
    }

    refresh();
    return { refresh, getNotes, setNotes };
}
