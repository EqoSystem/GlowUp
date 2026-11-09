// Folders, the Recycle bin, and picking several entries to move them together.
// Shared by the Brand & Product Inventory and the Ingredient Curator.
//
//   pick         - the small box at the start of a row or card. Ctrl+click on the row does the same;
//                  Shift+click picks everything from the last pick to here.
//   drag         - a row, a card, or everything that is picked. A drop bar comes up from the bottom of the
//                  window with the folders, "New folder" and the Recycle bin. The folder chips above the
//                  list take drops too.
//   folders      - an entry is in one folder at most. The folder's name is saved ON the entry (its
//                  "folder" field), so everyone who uses the admin site sees the same arrangement.
//                  A folder that is still empty is only remembered in this browser.
//   Recycle bin  - an entry in the bin is switched off for the app and can be restored. Only
//                  "Delete forever" removes it from the database.
//
// This file handles the pointer, the keyboard and what is drawn. The page does the saving:
//
//   createOrganizer({
//       kind: "products", noun: "products", nounOne: "product", allLabel: "All",
//       panel,                    // the panel element (the "select all shown" box may sit anywhere in it)
//       host,                     // the element that holds the entries: each one has data-key, a .pick box
//                                 // when it can be picked, and data-drag when it can be dragged
//       shelf, bar,               // where the folder chips and the "3 selected" bar are drawn
//       shelfAdd, shelfEnd,       // optional: "New folder" (and Restore all / Empty bin) go into shelfAdd, the Recycle bin chip into
//                                 // shelfEnd; the "All" and folder chips stay in `shelf`. Without them everything is drawn in `shelf`.
//       folderNames: () => [...],                 // the folder names found in the data
//       folderOf: (key) => "name" | "",
//       titleOf: (key) => "name of the entry",
//       count: (view) => number,                  // view = { kind: "all" | "folder" | "bin", name }
//       binKeys: () => [...],                     // everything in the Recycle bin
//       move: async (keys, folder) => ({ text, undo }),      // folder "" = out of its folder
//       bin: async (keys) => ({ text, undo }),
//       restore: async (keys, folder) => ({ text }),
//       purge: async (keys) => true | false,      // the page asks first and says what happened
//       renameFolder: async (from, to) => {}, deleteFolder: async (name) => {},
//       onView: () => {}                          // another folder (or the bin) is shown: draw the entries again
//   })
//
// Everything a person typed (folder names, entry names) is written with textContent, never as HTML.
import { icon, BIN_ICON, toast, openMenu, closeMenu, askText, askConfirm, calmMotion, hideTip, playSweep } from "./adminShell.js";

const FOLDERS_KEY = "glowup.folders.v1";
const HOLD_MS = 350;                  // on a touch screen: press this long, then drag
const START_PX = 6;                   // the pointer moves this far before a press becomes a drag
export const MAX_FOLDERS = 20;

/** A folder name as it is saved: single spaces, 30 characters at most. */
export const cleanFolder = (text) => String(text === null || text === undefined ? "" : text).replace(/\s+/g, " ").trim().slice(0, 30);
const same = (a, b) => cleanFolder(a).toLowerCase() === cleanFolder(b).toLowerCase();

function remembered(kind) {
    try {
        const all = JSON.parse(localStorage.getItem(FOLDERS_KEY) || "{}");
        return Array.isArray(all[kind]) ? all[kind].map(cleanFolder).filter((name) => name).slice(0, MAX_FOLDERS) : [];
    } catch (error) { return []; }
}
function remember(kind, names) {
    try {
        const all = JSON.parse(localStorage.getItem(FOLDERS_KEY) || "{}");
        all[kind] = names.slice(0, MAX_FOLDERS);
        localStorage.setItem(FOLDERS_KEY, JSON.stringify(all));
    } catch (error) { /* private window: an empty folder is then forgotten when the page is left */ }
}

/** A mistake the person can read ("That brand has no products to move."), not a failure of the database. */
export function friendly(message) { const error = new Error(message); error.friendly = true; return error; }

export function createOrganizer(spec) {
    const selected = new Set();
    let view = { kind: "all", name: "" };
    let anchor = null;                        // where a Shift+click range starts
    let press = null, drag = null, justDragged = false;
    const many = (n) => `${n} ${n === 1 ? spec.nounOne : spec.noun}`;

    // ---------------------------------------------------------------------------------------------
    // Folders
    // ---------------------------------------------------------------------------------------------
    const folders = () => {
        const out = [];
        const add = (name) => { const clean = cleanFolder(name); if (clean && !out.some((have) => same(have, clean))) out.push(clean); };
        spec.folderNames().forEach(add);
        remembered(spec.kind).forEach(add);
        return out.sort((a, b) => a.localeCompare(b, undefined, { sensitivity: "base" }));
    };
    const keep = (name) => { const list = remembered(spec.kind); if (!list.some((have) => same(have, name))) { list.push(cleanFolder(name)); remember(spec.kind, list); } };
    const forget = (name) => remember(spec.kind, remembered(spec.kind).filter((have) => !same(have, name)));
    /** What is shown now. A folder that no longer exists falls back to everything. */
    const currentView = () => {
        if (view.kind === "folder" && !folders().some((name) => same(name, view.name))) view = { kind: "all", name: "" };
        return view;
    };
    /**
     * Shows a view. The Recycle bin and a folder are places of their own (docs/NAVIGATION_RULES.md, 9 November 2026): going
     * there from the whole list adds a step to the browser's history, so the browser's Back (and the Back button the shelf
     * shows there) returns to the whole list. `fromHistory` = it is the browser's Back itself: nothing is added.
     */
    const show = (next, fromHistory) => {
        const was = view.kind;
        view = next && next.kind === "bin" ? { kind: "bin", name: "" }
            : next && next.kind === "folder" && cleanFolder(next.name) ? { kind: "folder", name: folders().find((name) => same(name, next.name)) || cleanFolder(next.name) }
            : { kind: "all", name: "" };
        if (!fromHistory) {
            const ours = history.state && history.state.glowupView === spec.kind;
            try {
                if (was === "all" && view.kind !== "all") history.pushState({ glowupView: spec.kind }, "");
                else if (was !== "all" && view.kind === "all" && ours) history.back();
            } catch (error) { /* history not available: the Back button still works */ }
        }
        selected.clear();
        anchor = null;
        spec.onView();
    };
    window.addEventListener("popstate", () => { if (view.kind !== "all") show({ kind: "all" }, true); });

    // ---------------------------------------------------------------------------------------------
    // What happens to entries (the page saves; this says what happened and offers Undo)
    // ---------------------------------------------------------------------------------------------
    const fail = (error, what) => {
        if (error && error.friendly) toast(error.message, "info");
        else { console.error(what + " failed: ", error); toast(`${what} did not work. ${error && error.message ? error.message : ""}`, "error"); }
    };
    const forgetPicked = (keys) => { keys.forEach((key) => selected.delete(key)); refresh(); };

    async function moveTo(keys, name) {
        const folder = cleanFolder(name);
        if (folder) keep(folder);
        try {
            const done = await spec.move(keys, folder);
            forgetPicked(keys);
            toast(folder ? `${done.text} moved to "${folder}".` : `${done.text} taken out of the folder.`, "ok",
                done.undo ? { label: "Undo", run: async () => { try { await done.undo(); toast("Put back.", "ok"); } catch (error) { fail(error, "Undo"); } refresh(); } } : null);
        } catch (error) { fail(error, "Moving"); refresh(); }
    }

    /**
     * Every removal asks first (9 November 2026): from a row's button, the selection bar, the Delete key, a drag onto the
     * bin or a data panel. `asked` = the caller has already asked with its own, more exact question.
     * Escape, the backdrop or Cancel mean: keep them.
     */
    async function toBin(keys, asked) {
        if (!keys || keys.length === 0) return;
        if (!asked) {
            const names = keys.map((key) => spec.titleOf(key) || "(no name)");
            const one = keys.length === 1;
            const sure = await askConfirm({
                title: one ? `Delete "${names[0]}"?` : `Delete ${many(keys.length)}?`,
                message: (one ? "It goes" : "They go") + " to the Recycle bin and leave the lists the app uses. " + (one ? "It" : "They") + " can be restored from the Recycle bin until deleted forever.",
                items: one ? null : names, confirmLabel: "Move to Recycle bin", danger: true
            });
            if (!sure) { refresh(); return; }
        }
        try {
            const done = await spec.bin(keys);
            forgetPicked(keys);
            toast(`${done.text} moved to the Recycle bin.`, "ok",
                done.undo ? { label: "Undo", run: async () => { try { await done.undo(); toast("Restored.", "ok"); } catch (error) { fail(error, "Undo"); } refresh(); } } : null);
        } catch (error) { fail(error, "Moving to the Recycle bin"); refresh(); }
    }

    async function restore(keys, name) {
        const folder = cleanFolder(name);
        if (folder) keep(folder);
        try {
            const done = await spec.restore(keys, folder || undefined);
            forgetPicked(keys);
            toast(folder ? `${done.text} restored into "${folder}".` : `${done.text} restored.`, "ok");
        } catch (error) { fail(error, "Restoring"); refresh(); }
    }

    async function purge(keys) {
        try { if (await spec.purge(keys)) forgetPicked(keys); }
        catch (error) { fail(error, "Deleting"); }
        refresh();
    }

    /** Asks for a name, makes the folder, and (when keys are given) puts those entries into it. */
    async function newFolder(keys, fromBin) {
        const all = folders();
        if (all.length >= MAX_FOLDERS) { toast(`${MAX_FOLDERS} folders are the most. Delete one first.`, "error"); return; }
        const typed = await askText({ title: "New folder", label: "Name of the folder", placeholder: "For example Lip products", confirmLabel: keys && keys.length ? "Create and move" : "Create", maxLength: 30 });
        const clean = cleanFolder(typed);
        if (!clean) return;
        const existing = all.find((name) => same(name, clean));
        const name = existing || clean;
        keep(name);
        if (keys && keys.length > 0) { if (fromBin) await restore(keys, name); else await moveTo(keys, name); return; }
        toast(existing ? `"${name}" exists already.` : `Folder "${name}" made. Drag entries onto it.`, existing ? "info" : "ok");
        refresh();
    }

    /** A menu with every folder: where should these entries go? */
    function chooseFolder(anchorEl, keys) {
        const names = folders();
        const items = names.map((name) => ({ label: name, icon: "folder", checked: keys.length > 0 && keys.every((key) => same(spec.folderOf(key), name)), run: () => moveTo(keys, name) }));
        if (names.length > 0) items.unshift({ heading: "Move to" });
        items.push({ label: "New folder", icon: "folderPlus", description: "Make a folder and put them in it.", run: () => newFolder(keys, false) });
        if (keys.some((key) => spec.folderOf(key))) items.push({ separator: true }, { label: "Take out of the folder", icon: "x", run: () => moveTo(keys, "") });
        openMenu(anchorEl, items);
    }

    function folderMenu(anchorEl, name) {
        openMenu(anchorEl, [
            {
                label: "Rename", icon: "edit", run: async () => {
                    const next = cleanFolder(await askText({ title: "Rename the folder", label: "Name", value: name, maxLength: 30 }));
                    if (!next || next === name) return;
                    if (folders().some((have) => same(have, next) && !same(have, name))) { toast(`"${next}" exists already.`, "error"); return; }
                    try {
                        await spec.renameFolder(name, next);
                        forget(name); keep(next);
                        if (view.kind === "folder" && same(view.name, name)) view = { kind: "folder", name: next };
                        toast("Folder renamed.", "ok");
                    } catch (error) { fail(error, "Renaming"); }
                    spec.onView();
                }
            },
            {
                label: "Delete folder", icon: "trash", danger: true, description: "Its entries stay in the list. Only the folder goes.", run: async () => {
                    const inside = spec.count({ kind: "folder", name });
                    if (!(await askConfirm({ title: `Delete the folder "${name}"?`, message: inside > 0 ? `Its ${many(inside)} stay in the list. Only the folder goes.` : "It is empty. Only the folder goes.", confirmLabel: "Delete folder", danger: true }))) return;
                    try {
                        await spec.deleteFolder(name);
                        forget(name);
                        if (view.kind === "folder" && same(view.name, name)) view = { kind: "all", name: "" };
                        toast("Folder deleted. Its entries are still in the list.", "ok");
                    } catch (error) { fail(error, "Deleting the folder"); }
                    spec.onView();
                }
            }
        ]);
    }

    // ---------------------------------------------------------------------------------------------
    // Picking
    // ---------------------------------------------------------------------------------------------
    const entries = () => Array.from(spec.host.querySelectorAll("[data-key]"));
    const pickable = (el) => !!el.querySelector(".pick");
    const pickAllBox = () => (spec.panel || spec.host).querySelector("[data-pick-all]");

    /** Every key "select all" covers: what the page says (also rows on other pages), else the rows on screen. */
    const allKeys = () => (spec.pickScope && spec.pickScope()) || entries().filter(pickable).map((el) => el.dataset.key);

    function decorate() {
        let seen = 0, picked = 0;
        entries().forEach((el) => {
            const box = el.querySelector(".pick");
            if (!box) return;
            const on = selected.has(el.dataset.key);
            seen++; if (on) picked++;
            el.classList.toggle("is-picked", on);
            box.setAttribute("aria-checked", String(on));
        });
        spec.host.classList.toggle("picking", selected.size > 0);
        const all = pickAllBox();
        const scope = spec.pickScope && spec.pickScope();
        if (scope) { seen = scope.length; picked = scope.filter((key) => selected.has(key)).length; }
        if (all) { all.setAttribute("aria-checked", seen > 0 && picked === seen ? "true" : picked > 0 ? "mixed" : "false"); all.disabled = seen === 0; }
    }

    function toggle(key, on) {
        const next = on === undefined ? !selected.has(key) : on;
        if (next) selected.add(key); else selected.delete(key);
        anchor = key;
        decorate(); paintBar();
        if (next) { const el = entries().find((entry) => entry.dataset.key === key); if (el && el.classList.contains("fx")) playSweep(el); }
    }
    function range(toKey) {
        const keys = entries().filter(pickable).map((el) => el.dataset.key);
        const a = keys.indexOf(anchor), b = keys.indexOf(toKey);
        if (a < 0 || b < 0) { toggle(toKey, true); return; }
        keys.slice(Math.min(a, b), Math.max(a, b) + 1).forEach((key) => selected.add(key));
        decorate(); paintBar();
    }
    function pickAll() {
        const keys = allKeys();
        const every = keys.length > 0 && keys.every((key) => selected.has(key));
        keys.forEach((key) => { if (every) selected.delete(key); else selected.add(key); });
        decorate(); paintBar();
    }
    const clear = () => { selected.clear(); anchor = null; decorate(); paintBar(); };

    // a click on a pick box (or Ctrl+click on a row) picks it; the page's own click handler never sees that click
    (spec.panel || spec.host).addEventListener("click", (event) => {
        if (event.target.closest("[data-pick-all]")) { event.stopPropagation(); pickAll(); return; }
        if (!spec.host.contains(event.target)) return;
        if (justDragged) { justDragged = false; event.stopPropagation(); event.preventDefault(); return; }
        const item = event.target.closest("[data-key]");
        if (!item || !pickable(item)) return;
        const onBox = !!event.target.closest(".pick");
        if (!onBox && !((event.ctrlKey || event.metaKey) && !event.target.closest("button, a, input, select, textarea"))) return;
        event.stopPropagation(); event.preventDefault();
        if (event.shiftKey && anchor) range(item.dataset.key); else toggle(item.dataset.key);
    }, true);

    spec.host.addEventListener("keydown", (event) => {
        if (event.target.closest("input, textarea, select")) return;
        const item = event.target.closest("[data-key]");
        if (event.key === " " && item && event.target === item && pickable(item)) { event.preventDefault(); toggle(item.dataset.key); }
        else if (event.key === "Delete" && selected.size > 0) { event.preventDefault(); if (view.kind === "bin") purge(Array.from(selected)); else toBin(Array.from(selected)); }
        else if (event.key === "Escape" && selected.size > 0 && !drag) { event.stopPropagation(); clear(); }
        else if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "a" && item) { event.preventDefault(); pickAll(); }
    });

    // ---------------------------------------------------------------------------------------------
    // The shelf (folder chips, the bin) and the bar that shows while something is picked
    // ---------------------------------------------------------------------------------------------
    function chip(className, attrs, iconHtml, label, count) {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "folder-chip " + className;
        Object.keys(attrs).forEach((key) => { if (attrs[key] !== null && attrs[key] !== undefined) button.setAttribute(key, attrs[key]); });
        button.innerHTML = iconHtml;
        if (label) { const name = document.createElement("span"); name.className = "chip-name"; name.textContent = label; button.appendChild(name); }
        if (count !== undefined && count !== null) { const number = document.createElement("span"); number.className = "chip-count"; number.textContent = String(count); button.appendChild(number); }
        return button;
    }

    function paintShelf() {
        const shelf = spec.shelf;
        if (!shelf) return;
        const now = currentView(), names = folders(), inBin = spec.count({ kind: "bin" });
        // a part goes to "lead" (All, the folders) unless put() says "add" (New folder, Restore all, Empty bin) or "end" (the bin)
        const kids = [], slot = new Map();
        const put = (node, where) => { kids.push(node); slot.set(node, where); return node; };
        kids.push(chip(now.kind === "all" ? "on" : "", { "data-view": "all", "data-drop": "all", "data-tip-title": spec.allLabel || "All", "data-tip": `Every ${spec.nounOne}, whatever folder it is in.`, "data-tip-preview": "Drop entries here to take them out of their folder." },
            icon("layers"), spec.allLabel || "All", spec.count({ kind: "all" })));
        names.forEach((name) => kids.push(chip(now.kind === "folder" && same(now.name, name) ? "on" : "", { "data-view": "folder", "data-name": name, "data-drop": "folder:" + name, "data-tip-title": name, "data-tip": "Show only what is in this folder.", "data-tip-preview": "Drop entries here to put them in it." },
            icon("folder"), name, spec.count({ kind: "folder", name }))));
        if (names.length < MAX_FOLDERS) put(chip("add", { "data-new": "1", "data-drop": "new", "aria-label": "New folder", "data-tip-title": "New folder", "data-tip": `Make a folder to arrange the ${spec.noun}.`, "data-tip-preview": "Drop entries here to make a folder for them." },
            icon("folderPlus"), names.length === 0 ? "New folder" : "", null), "add");
        if (now.kind === "folder") {
            const more = document.createElement("button");
            more.type = "button"; more.className = "icon-btn small"; more.setAttribute("data-folder-menu", "1"); more.setAttribute("aria-label", "Folder options"); more.setAttribute("aria-haspopup", "menu");
            more.dataset.tipTitle = "Folder options"; more.dataset.tip = "Rename this folder or delete it. Its entries always stay.";
            more.innerHTML = icon("more");
            kids.push(more);
        }
        const spacer = document.createElement("span");
        spacer.className = "spacer";
        if (!spec.shelfAdd) put(spacer, "lead");                         // one row: the bin goes to the far end
        if (now.kind === "bin" && inBin > 0) {
            const act = (label, name, cls, tip) => { const button = document.createElement("button"); button.type = "button"; button.className = "row-btn " + cls; button.dataset.act = name; button.textContent = label; button.dataset.tip = tip; return button; };
            put(act("Restore all", "restoreAll", "", "Everything in the Recycle bin goes back to the list."), "add");
            put(act("Empty bin", "empty", "danger", "Delete everything in the Recycle bin from the database. It asks first."), "add");
        }
        put(chip("bin-chip" + (now.kind === "bin" ? " on" : ""), { "data-view": "bin", "data-drop": "bin", "data-tip-title": "Recycle bin", "data-tip": "What was removed. The app does not use it, and it can be restored.", "data-tip-preview": "Drop entries here to remove them. Only Delete forever is final." },
            BIN_ICON, now.kind === "bin" ? "Recycle bin" : "", inBin > 0 ? inBin : null), "end");
        // low-key on purpose: only the small bin (and a number when something is in it) until it is opened, pointed at, or something is dragged
        kids[kids.length - 1].setAttribute("aria-label", "Recycle bin" + (inBin > 0 ? ", " + inBin + " inside" : ", empty"));
        // inside the Recycle bin or a folder: a Back icon right after the bin, so the way out is plain (the browser's Back does the same)
        if (now.kind !== "all") {
            const back = document.createElement("button");
            back.type = "button"; back.className = "icon-btn shelf-back"; back.dataset.view = "all";
            back.setAttribute("aria-label", `Back to all ${spec.noun}`);
            back.innerHTML = icon("left");
            put(back, "end");
        }
        const hosts = { lead: shelf, add: spec.shelfAdd || shelf, end: spec.shelfEnd || shelf };
        new Set(Object.values(hosts)).forEach((host) => host.replaceChildren(...kids.filter((node) => hosts[slot.get(node) || "lead"] === host)));
    }

    const onShelfClick = (event) => {
        const button = event.target.closest("button");
        if (!button) return;
        if (button.dataset.view) show({ kind: button.dataset.view, name: button.dataset.name || "" });
        else if (button.hasAttribute("data-new")) newFolder(null, false);
        else if (button.hasAttribute("data-folder-menu")) folderMenu(button, view.name);
        else if (button.dataset.act === "restoreAll") restore(spec.binKeys());
        else if (button.dataset.act === "empty") purge(spec.binKeys());
    };
    new Set([spec.shelf, spec.shelfAdd, spec.shelfEnd].filter(Boolean)).forEach((host) => host.addEventListener("click", onShelfClick));

    function paintBar() {
        const bar = spec.bar;
        if (!bar) return;
        bar.hidden = selected.size === 0;
        if (selected.size === 0) { bar.replaceChildren(); return; }
        const keys = () => Array.from(selected);
        const button = (label, cls, run, tip, iconHtml) => {
            const el = document.createElement("button");
            el.type = "button"; el.className = "row-btn " + cls;
            if (iconHtml) el.innerHTML = iconHtml;
            el.append(label);
            if (tip) el.dataset.tip = tip;
            el.addEventListener("click", (event) => run(event.currentTarget));
            return el;
        };
        const text = document.createElement("span");
        text.className = "w-bar-text";
        const strong = document.createElement("b");
        strong.textContent = String(selected.size);
        text.append(strong, " selected");
        const spacer = document.createElement("span");
        spacer.className = "spacer";
        const kids = [text];
        if (view.kind === "bin") kids.push(
            button("Restore", "bar-main", () => restore(keys()), "Back to the list. The app uses them again as before.", icon("undo")),
            button("Delete forever", "bar-quiet danger", () => purge(keys()), "Delete them from the database. It asks first."));
        else kids.push(
            button("Folder", "bar-main", (el) => chooseFolder(el, keys()), "Put them in a folder (or take them out).", icon("folder")),
            button("Recycle bin", "bar-quiet danger", () => toBin(keys()), "Remove them. They can be restored from the Recycle bin.", BIN_ICON));
        if (view.kind !== "bin" && typeof spec.selectionActions === "function") {
            spec.selectionActions(keys()).forEach((action) => {
                if (!action || typeof action.run !== "function") return;
                kids.push(button(action.label, action.className || "bar-main", (el) => action.run(keys(), el), action.tip, action.icon || ""));
            });
        }
        kids.push(spacer, button("Clear", "bar-quiet", clear, "Pick nothing."));
        bar.replaceChildren(...kids);
    }

    /** Call after the page has drawn its entries: the picks, the folder chips and the bar follow. */
    function refresh() { decorate(); paintShelf(); paintBar(); }

    /** Call after the data was loaded again: what no longer exists is no longer picked. */
    function sync(validKeys) {
        const valid = validKeys instanceof Set ? validKeys : new Set(validKeys);
        Array.from(selected).forEach((key) => { if (!valid.has(key)) selected.delete(key); });
    }

    // ---------------------------------------------------------------------------------------------
    // Dragging
    // ---------------------------------------------------------------------------------------------
    let ghost = null, dock = null, over = null;

    function buildGhost(keys) {
        const el = document.createElement("div");
        el.className = "drag-ghost" + (keys.length > 1 ? " many" : "");
        const name = document.createElement("span");
        name.className = "ghost-name";
        name.textContent = spec.titleOf(keys[0]) || many(1);
        el.appendChild(name);
        if (keys.length > 1) { const count = document.createElement("span"); count.className = "ghost-count"; count.textContent = String(keys.length); el.appendChild(count); }
        document.body.appendChild(el);
        return el;
    }

    function buildDock(keys) {
        const el = document.createElement("div");
        el.className = "dock";
        el.setAttribute("role", "group");
        el.setAttribute("aria-label", "Drop here");
        const target = (drop, iconHtml, label, cls) => {
            const box = document.createElement("div");
            box.className = "dock-target" + (cls ? " " + cls : "");
            box.dataset.drop = drop;
            box.innerHTML = iconHtml;
            const text = document.createElement("span");
            text.textContent = label;
            box.appendChild(text);
            return box;
        };
        const list = document.createElement("div");
        list.className = "dock-folders";
        const names = folders();
        if (view.kind === "bin") list.appendChild(target("all", icon("undo"), "Restore"));
        else if (keys.some((key) => spec.folderOf(key))) list.appendChild(target("all", icon("x"), "No folder"));
        names.forEach((name) => { if (!(view.kind === "folder" && same(view.name, name))) list.appendChild(target("folder:" + name, icon("folder"), name)); });
        if (names.length < MAX_FOLDERS) list.appendChild(target("new", icon("folderPlus"), "New folder", "add"));
        el.appendChild(list);
        if (view.kind !== "bin") el.appendChild(target("bin", BIN_ICON, "Recycle bin", "dock-bin"));
        document.body.appendChild(el);
        return el;
    }

    const placeGhost = (x, y) => { if (ghost) ghost.style.translate = `${x + 14}px ${y - ghost.offsetHeight - 10}px`; };

    function begin() {
        const key = press.key;
        const keys = selected.has(key) ? Array.from(selected) : [key];
        drag = { keys, x: press.x, y: press.y };
        document.body.classList.add("is-dragging", "is-moving-items");
        hideTip(); closeMenu();
        entries().forEach((el) => { if (keys.includes(el.dataset.key)) el.classList.add("lifting"); });
        press.item.classList.remove("held");
        ghost = buildGhost(keys);
        dock = buildDock(keys);
        document.addEventListener("keydown", onKeyWhileDragging, true);
    }

    function setOver(target) {
        if (target === over) return;
        if (over) over.classList.remove("drop-on");
        over = target;
        if (over) over.classList.add("drop-on");
        if (ghost) ghost.classList.toggle("over-bin", !!over && over.dataset.drop === "bin");
    }

    /** What the pointer is over, when that takes a drop of what is dragged. */
    function targetAt(x, y) {
        const under = document.elementFromPoint(x, y);
        const target = under && under.closest ? under.closest("[data-drop]") : null;
        if (!target) return null;
        const drop = target.dataset.drop;
        if (view.kind === "bin" && drop === "bin") return null;                                       // it is in the bin already
        if (view.kind === "folder" && drop.startsWith("folder:") && same(drop.slice(7), view.name)) return null;
        return target;
    }

    function move(event) {
        if (!press) return;
        const x = event.clientX, y = event.clientY;
        if (!drag) {
            if (Math.hypot(x - press.x, y - press.y) < START_PX) return;
            if (!press.ready) { cancelPress(); return; }                 // a finger that moves before the hold is over is scrolling
            begin();
        }
        placeGhost(x, y);
        setOver(targetAt(x, y));
    }

    function cleanup() {
        window.removeEventListener("pointermove", move);
        window.removeEventListener("pointerup", up);
        window.removeEventListener("pointercancel", cancel);
        document.removeEventListener("keydown", onKeyWhileDragging, true);
        document.body.classList.remove("is-dragging", "is-moving-items");
        if (press) { window.clearTimeout(press.timer); press.item.classList.remove("held"); }
        if (over) over.classList.remove("drop-on");
        over = null; press = null; drag = null;
    }
    const cancelPress = () => { cleanup(); };
    const dropLifting = () => entries().forEach((el) => el.classList.remove("lifting"));

    function leave(oldGhost, oldDock, how) {
        const quick = calmMotion();
        if (oldGhost) {
            if (how && !quick) { oldGhost.classList.add(how); window.setTimeout(() => oldGhost.remove(), 320); }
            else oldGhost.remove();
        }
        if (oldDock) {
            if (quick) oldDock.remove();
            else window.setTimeout(() => { oldDock.classList.add("leaving"); window.setTimeout(() => oldDock.remove(), 220); }, how === "back" ? 0 : 260);
        }
    }

    function cancel() {
        const wasDragging = !!drag, oldGhost = ghost, oldDock = dock, from = drag;
        ghost = null; dock = null;
        cleanup();
        if (!wasDragging) return;
        dropLifting();
        if (oldGhost && from) oldGhost.style.translate = `${from.x}px ${from.y}px`;
        leave(oldGhost, oldDock, "back");
    }
    function onKeyWhileDragging(event) { if (event.key === "Escape") { event.stopPropagation(); event.preventDefault(); cancel(); } }

    function up(event) {
        if (!drag) { cleanup(); return; }
        const target = targetAt(event.clientX, event.clientY) || over;
        const keys = drag.keys, oldGhost = ghost, oldDock = dock, from = drag, inBin = view.kind === "bin";
        ghost = null; dock = null;
        justDragged = true;
        window.setTimeout(() => { justDragged = false; }, 0);          // the click that follows a drag is not a click on the row
        cleanup();
        if (!target) {
            dropLifting();
            if (oldGhost) oldGhost.style.translate = `${from.x}px ${from.y}px`;
            leave(oldGhost, oldDock, "back");
            return;
        }
        // what was dragged flies into what it was dropped on
        const drop = target.dataset.drop, box = target.getBoundingClientRect();
        if (oldGhost) oldGhost.style.translate = `${box.left + box.width / 2 - oldGhost.offsetWidth / 2}px ${box.top + box.height / 2 - oldGhost.offsetHeight / 2}px`;
        target.classList.remove("gulp"); void target.offsetWidth; target.classList.add("gulp");
        window.setTimeout(() => target.classList.remove("gulp"), 450);
        leave(oldGhost, drop === "new" ? null : oldDock, drop === "bin" ? "popping" : "dropping");
        if (drop === "new" && oldDock) oldDock.remove();

        if (drop === "bin") toBin(keys);
        else if (drop === "new") { dropLifting(); newFolder(keys, inBin); }
        else if (drop === "all") { if (inBin) restore(keys); else moveTo(keys, ""); }
        else if (drop.startsWith("folder:")) { if (inBin) restore(keys, drop.slice(7)); else moveTo(keys, drop.slice(7)); }
        else dropLifting();
    }

    spec.host.addEventListener("pointerdown", (event) => {
        if (event.button > 0 || press) return;
        const item = event.target.closest("[data-key][data-drag]");
        if (!item || !spec.host.contains(item) || event.target.closest("button, a, input, select, textarea, .detail-box")) return;
        const touch = event.pointerType === "touch" || event.pointerType === "pen";
        press = { item, key: item.dataset.key, x: event.clientX, y: event.clientY, ready: !touch, timer: 0 };
        if (touch) press.timer = window.setTimeout(() => { if (press && !drag) { press.ready = true; press.item.classList.add("held"); } }, HOLD_MS);
        window.addEventListener("pointermove", move);
        window.addEventListener("pointerup", up);
        window.addEventListener("pointercancel", cancel);
    });
    // on a touch screen the page must not scroll under an entry that is held or dragged
    spec.host.addEventListener("touchmove", (event) => { if (drag || (press && press.ready)) event.preventDefault(); }, { passive: false });
    spec.host.addEventListener("contextmenu", (event) => { if (drag || (press && press.timer)) event.preventDefault(); });      // a long press must not open the browser's own menu

    return {
        selected,
        get view() { return currentView(); },
        show, refresh, sync, clear, folders,
        toBin, restore, purge, moveTo, chooseFolder, newFolder
    };
}
