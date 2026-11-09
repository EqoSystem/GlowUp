// Display layouts: how the panels of a page are shown, per person.
//
// One admin sign-in is shared by a few people, so each of them keeps a layout under their own name
// (up to 4). "Default layout" is the fixed starting point and can never be changed.
// A layout remembers, for every page:
//   - where each panel sits and how big it is (drag a panel by its title to move it; while "Arranging"
//     the whole panel can be grabbed and its right and bottom edges resize it)
//   - what a panel is shown as: its standard look, a list, a row, a grid or a graph (the panel's "..." menu)
//   - data panels that were added, panels that were hidden, and panels that were combined into one
//     (as tabs, stacked, or side by side)
//   - whether the gaps between panels are closed ("Auto resize": on unless the person switches it off)
// and for the whole site: font, text size, spacing, colours (kept per theme) and how round the corners are.
//
// Auto resize: a short panel beside a tall one used to leave a hole under it. With it on, the board is a grid of very
// low rows; each panel takes the rows its own height needs and the browser slots it into the first free place, so the
// next panel moves up into the hole. It is worked out again whenever a panel's content, the window or the layout changes.
// Folding (adminFold.js): every panel with a title can be folded to that title; an empty panel folds by itself.
//
// Layouts are kept in this browser (localStorage). Export / Import carries one to another computer.
// What a panel CONTAINS is never changed here: that is the pages' own code and the database.
import { icon, esc, hydrate, currentTheme, toast, openMenu, closeMenu, flip, askText, askConfirm, guideBadge, hideTip } from "./adminShell.js";
import { datasetList, datasetSpec, measuresOf, mountWidget, unmountWidget, normalizeConfig, defaultTitle, VIEWS, CHARTS } from "./adminWidgets.js";
import { hasAssist, assistHidden, setAssistHidden, anyAssistHidden, showAllAssists } from "./adminAssist.js";
import { rowsFor, ROW_UNIT } from "./layoutRules.js";
import { setFoldPage, foldButton, FOLD_PARTS, paintFold, toggleFold, foldedNow, anyFoldChoice, forgetFoldChoices } from "./adminFold.js";
import { ANIMATIONS, ANIMATION_FRAMES, TRANSITIONS, EDGES, ORIENTS, NOTE_PLACES, LIMITS, cleanStyle, cleanTexts, cleanPanelMap, contrast, applyPanelStyle, applyPanelTexts, ownTexts } from "./componentStyle.js";
import { SHARED_PAGES, cachedShared, readShared, writeShared, clearShared } from "./sharedLayouts.js";

const LAYOUT_KEY = "glowup.layouts.v1";
// Version of the DEFAULT panel positions. A saved page older than this loses its saved order and sizes (names, fonts,
// colours, views, added panels and groups stay) so the new default positions show. Raise it whenever a page's default order or widths change.
const POSITIONS_VERSION = 7;
const MAX_PROFILES = 4;
const MAX_ADDED = 12;                             // data panels one page of a layout can hold
const FONTS = [
    { id: "", label: "System (default)", stack: "" },
    { id: "poppins", label: "Poppins (the app's font)", stack: '"Poppins", "Segoe UI", sans-serif', web: "Poppins:wght@400;500;600;700" },
    { id: "inter", label: "Inter", stack: '"Inter", "Segoe UI", sans-serif', web: "Inter:wght@400;500;600;700" },
    { id: "serif", label: "Georgia (serif)", stack: 'Georgia, "Times New Roman", serif' },
    { id: "verdana", label: "Verdana (wide)", stack: "Verdana, Geneva, sans-serif" },
    { id: "mono", label: "Consolas (fixed width)", stack: 'Consolas, "Courier New", monospace' }
];
const FONT_IDS = () => FONTS.map((f) => f.id);
const GROUP_MODES = [
    { id: "tabs", label: "Tabs", icon: "tabs", hint: "One at a time; a tab for each." },
    { id: "stack", label: "Stacked", icon: "list", hint: "All of them, one under the other." },
    { id: "side", label: "Side by side", icon: "split", hint: "All of them next to each other." }
];

let store = { active: "default", profiles: [] };
let pageId = "";
let defaults = null;                              // the fixed layout, read from the page itself
let editing = false;

// ---------------------------------------------------------------------------------------------
// The saved layouts
// ---------------------------------------------------------------------------------------------
const ID = /^[a-z0-9_-]{1,40}$/i;
const cleanName = (text, length = 24) => String(text || "").replace(/\s+/g, " ").trim().slice(0, length);
const idList = (list, most = 40) => (Array.isArray(list) ? list.map(String).filter((id) => ID.test(id)).slice(0, most) : []);
const shortId = (value) => (ID.test(String(value || "")) ? String(value) : "");

/** Only known settings, as plain numbers and short texts: a saved or imported layout can never carry anything else. */
function cleanView(view) {
    const v = view || {};
    return { source: shortId(v.source), view: shortId(v.view), graph: shortId(v.graph), by: shortId(v.by), measure: shortId(v.measure) };
}
function cleanPage(raw) {
    const page = raw && typeof raw === "object" ? raw : {};
    const size = {};
    Object.keys(page.size || {}).slice(0, 60).forEach((id) => { if (ID.test(id)) size[id] = { w: Math.min(12, Math.max(2, Number(page.size[id].w) || 12)), h: Math.min(1400, Math.max(0, Number(page.size[id].h) || 0)) }; });
    const views = {};
    Object.keys(page.views || {}).slice(0, 30).forEach((id) => { if (ID.test(id)) views[id] = cleanView(page.views[id]); });
    return {
        ver: Math.max(0, Math.min(1000, Number(page.ver) || 0)),
        fit: page.fit !== false,                      // Auto resize: on unless it was switched off
        order: idList(page.order, 60), size, hidden: idList(page.hidden, 60), views,
        widgets: (Array.isArray(page.widgets) ? page.widgets : []).slice(0, MAX_ADDED).filter((w) => w && ID.test(String(w.id)))
            .map((w) => Object.assign({ id: String(w.id), title: cleanName(w.title, 40) }, cleanView(w))),
        groups: (Array.isArray(page.groups) ? page.groups : []).slice(0, MAX_ADDED).filter((g) => g && ID.test(String(g.id)))
            .map((g) => ({ id: String(g.id), title: cleanName(g.title, 40), mode: GROUP_MODES.some((m) => m.id === g.mode) ? g.mode : "tabs", members: idList(g.members, 8), active: Math.max(0, Math.min(7, Number(g.active) || 0)) })),
        // "Style and text" of single panels (componentStyle.js)
        styles: cleanPanelMap(page.styles, (raw) => cleanStyle(raw, FONT_IDS())),
        texts: cleanPanelMap(page.texts, cleanTexts)
    };
}
function cleanProfile(raw, id) {
    const profile = { id, name: cleanName(raw.name) || "Layout", appearance: {}, pages: {} };
    const a = raw.appearance || {};
    if (FONTS.some((f) => f.id === a.font)) profile.appearance.font = a.font;
    if (Number(a.scale) >= 0.85 && Number(a.scale) <= 1.25) profile.appearance.scale = Number(a.scale);
    if (a.radius !== undefined && a.radius !== "" && Number(a.radius) >= 0 && Number(a.radius) <= 24) profile.appearance.radius = Number(a.radius);
    if (a.density === "compact") profile.appearance.density = "compact";
    const hex = (v) => (/^#[0-9a-f]{6}$/i.test(String(v)) ? String(v) : undefined);
    profile.appearance.colors = {};
    ["dark", "light"].forEach((theme) => { const c = (a.colors && a.colors[theme]) || {}; profile.appearance.colors[theme] = { accent: hex(c.accent), panel: hex(c.panel), bg: hex(c.bg) }; });
    Object.keys(raw.pages || {}).slice(0, 10).forEach((key) => { if (/^[a-z]{1,20}$/.test(key)) profile.pages[key] = cleanPage(raw.pages[key]); });
    return profile;
}

/** A whole store (saved, from a checkpoint or from Undo) with only known settings. */
function cleanStore(saved) {
    const clean = { active: "default", profiles: [] };
    if (saved && Array.isArray(saved.profiles))
        Object.assign(clean, { active: String(saved.active || "default"), profiles: saved.profiles.slice(0, MAX_PROFILES).filter((p) => p && ID.test(String(p.id))).map((p) => cleanProfile(p, String(p.id))) });
    if (!clean.profiles.some((p) => p.id === clean.active)) clean.active = "default";
    return clean;
}
function loadStore() {
    try { store = cleanStore(JSON.parse(localStorage.getItem(LAYOUT_KEY) || "null")); } catch (error) { console.warn("Saved layouts could not be read: ", error); }
    let moved = false;
    store.profiles.forEach((profile) => Object.keys(profile.pages).forEach((key) => {
        const saved = profile.pages[key];
        if (saved.ver < POSITIONS_VERSION) { saved.order = []; saved.size = {}; saved.ver = POSITIONS_VERSION; moved = true; }
    }));
    if (moved) saveStore();
    lastSaved = JSON.stringify(store);
}

// ---------------------------------------------------------------------------------------------
// Undo, Redo and checkpoints (9 November 2026): every change of a layout can be taken back
// ---------------------------------------------------------------------------------------------
// Each save remembers the layouts as they were before it. Changes that follow each other within UNDO_MERGE_MS (a slider
// being dragged, a panel being resized) are ONE step. Undo is kept for this visit of the page; checkpoints are kept in this browser.
const UNDO_MOST = 50, UNDO_MERGE_MS = 700;
const CHECKPOINT_KEY = "glowup.layouts.checkpoints.v1", CHECKPOINTS_MOST = 10;
const undoStack = [], redoStack = [];
let lastSaved = null, lastStep = 0;
function saveStore() {
    const now = JSON.stringify(store);
    if (lastSaved !== null && now !== lastSaved) {
        if (undoStack.length === 0 || Date.now() - lastStep > UNDO_MERGE_MS) { undoStack.push(lastSaved); if (undoStack.length > UNDO_MOST) undoStack.shift(); }
        lastStep = Date.now();
        redoStack.length = 0;
    }
    lastSaved = now;
    try { localStorage.setItem(LAYOUT_KEY, now); } catch (error) { console.warn("The layout could not be saved: ", error); }
    paintUndo();
}
/** Puts `json` (a whole store) on the page as the layouts now. */
function restoreStore(json, message) {
    if (editing) { editing = false; document.body.classList.remove("layout-editing"); }
    store = cleanStore(JSON.parse(json));
    lastSaved = JSON.stringify(store);
    lastStep = 0;
    try { localStorage.setItem(LAYOUT_KEY, lastSaved); } catch (error) { /* full or blocked */ }
    applyLayout();
    paintUndo();
    if (message) toast(message, "ok");
}
export function undoLayout() {
    if (undoStack.length === 0) { toast("Nothing to undo.", "info"); return; }
    redoStack.push(JSON.stringify(store));
    restoreStore(undoStack.pop(), "Undone. Redo puts it back.");
}
export function redoLayout() {
    if (redoStack.length === 0) { toast("Nothing to redo.", "info"); return; }
    undoStack.push(JSON.stringify(store));
    restoreStore(redoStack.pop(), "Done again.");
}
function paintUndo() {
    const undo = byId("undoLayoutBtn"), redo = byId("redoLayoutBtn");
    if (undo) undo.disabled = undoStack.length === 0;
    if (redo) redo.disabled = redoStack.length === 0;
}
function checkpoints() {
    try { const list = JSON.parse(localStorage.getItem(CHECKPOINT_KEY) || "[]"); return Array.isArray(list) ? list.filter((c) => c && c.id && c.store).slice(0, CHECKPOINTS_MOST) : []; } catch (error) { return []; }
}
function keepCheckpoints(list) {
    try { localStorage.setItem(CHECKPOINT_KEY, JSON.stringify(list.slice(0, CHECKPOINTS_MOST))); return true; }
    catch (error) { toast("The checkpoint could not be kept: this browser's storage is full.", "error"); return false; }
}
async function saveCheckpoint() {
    const list = checkpoints();
    const stamp = new Date().toLocaleString([], { month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" });
    const name = await askText({ title: "Save a checkpoint", label: "Name (what the layouts look like now)", value: "Before changes, " + stamp, maxLength: 40, confirmLabel: "Save checkpoint" });
    if (!name) return;
    list.unshift({ id: "c" + Date.now().toString(36), name: cleanName(name, 40), at: Date.now(), store: JSON.parse(JSON.stringify(store)) });
    if (list.length > CHECKPOINTS_MOST) toast(`Only the newest ${CHECKPOINTS_MOST} checkpoints are kept: the oldest one goes.`, "info");
    if (keepCheckpoints(list)) toast(`Checkpoint "${cleanName(name, 40)}" saved. Layout & Style > Checkpoints brings it back.`, "ok");
}
function openCheckpointMenu(anchor) {
    const list = checkpoints();
    const items = [{ label: "Save a checkpoint now", icon: "save", description: "Keeps every layout as it is now, in this browser.", run: saveCheckpoint }];
    if (list.length) items.push({ heading: "Go back to" });
    list.forEach((c) => items.push({
        label: c.name, icon: "reset", description: new Date(c.at).toLocaleString(),
        run: async () => {
            if (!(await askConfirm({ title: `Go back to "${c.name}"?`, message: "Every layout becomes what it was then. Undo brings back what you have now.", confirmLabel: "Go back" }))) return;
            undoStack.push(JSON.stringify(store)); redoStack.length = 0;
            restoreStore(JSON.stringify(c.store), `Back to "${c.name}".`);
        }
    }));
    if (list.length) items.push({ separator: true }, {
        label: "Delete all checkpoints", icon: "trash", danger: true, run: async () => {
            if (await askConfirm({ title: "Delete every checkpoint?", message: "The layouts themselves stay as they are.", confirmLabel: "Delete checkpoints", danger: true })) { keepCheckpoints([]); toast("Checkpoints deleted.", "ok"); }
        }
    });
    openMenu(anchor, items);
}
// Ctrl+Z / Ctrl+Y (Ctrl+Shift+Z): not while typing and not while a form is open (the form has its own Cancel)
document.addEventListener("keydown", (event) => {
    if (!(event.ctrlKey || event.metaKey) || event.altKey || !board() || !defaults) return;
    const key = String(event.key || "").toLowerCase();
    if (key !== "z" && key !== "y") return;
    const target = event.target;
    if ((target && target.closest && target.closest("input, textarea, select, [contenteditable]")) || document.querySelector(".modal-overlay[style*='flex']")) return;
    event.preventDefault();
    if (key === "y" || event.shiftKey) redoLayout(); else undoLayout();
});

// ---------------------------------------------------------------------------------------------
// The shared default (sharedLayouts.js): what "Default layout" shows for every admin
// ---------------------------------------------------------------------------------------------
let shared = null;                                // { appearance, page } of this page, cleaned; null = the page's built-in arrangement
function setShared(data) {
    shared = data ? { appearance: cleanProfile({ appearance: data.appearance }, "shared").appearance, page: cleanPage(Object.assign({}, data.layout, { ver: POSITIONS_VERSION })) } : null;
}

const activeProfile = () => store.profiles.find((p) => p.id === store.active) || null;
const board = () => document.querySelector(".board");
const topPanels = () => Array.from(document.querySelectorAll(".board > .panel[data-panel]"));
const panelOf = (id) => document.querySelector(`.board .panel[data-panel="${CSS.escape(id)}"]`);
const labelOf = (id) => { const panel = panelOf(id); return panel ? panel.dataset.label || id : id; };
const byId = (id) => document.getElementById(id);

/** This page's part of the active layout (made when `create` is on). On the default layout: the shared default's page, or null. */
function pageData(create) {
    const profile = activeProfile();
    if (!profile) return create ? null : (shared ? shared.page : null);
    profile.pages = profile.pages || {};
    if (!profile.pages[pageId]) { if (!create) return null; profile.pages[pageId] = cleanPage({ ver: POSITIONS_VERSION }); }
    return profile.pages[pageId];
}

/** The default layout is fixed: before anything is changed the person gets a layout of their own. Resolves false when they cancel. */
async function ensureProfile() {
    if (activeProfile()) return true;
    return newProfile();
}

async function newProfile() {
    if (store.profiles.length >= MAX_PROFILES) { toast(`${MAX_PROFILES} layouts are saved already. Delete one to make room.`, "error"); return false; }
    const name = await askText({ title: "Your own layout", label: "Your name (the layout is saved under it)", placeholder: "For example Maria", confirmLabel: "Create" });
    if (!name) return false;
    // a copy of what is on screen: the active layout, or the shared default of this page
    const from = activeProfile() || (shared ? { appearance: shared.appearance, pages: { [pageId]: shared.page } } : null);
    const profile = cleanProfile(JSON.parse(JSON.stringify({ name, appearance: from ? from.appearance : {}, pages: from ? from.pages : {} })), "p" + Date.now().toString(36));
    store.profiles.push(profile);
    store.active = profile.id;
    saveStore();
    applyLayout();
    return true;
}

// ---------------------------------------------------------------------------------------------
// Start
// ---------------------------------------------------------------------------------------------
export function initLayouts(page) {
    pageId = page;
    setFoldPage(page);
    if (!board()) return;
    defaults = { order: [], size: {}, source: {} };
    topPanels().forEach((panel) => {
        const id = panel.dataset.panel;
        defaults.order.push(id);
        defaults.size[id] = { w: Number(panel.dataset.w) || 12 };
        if (panel.dataset.source) defaults.source[id] = panel.dataset.source;
        decorate(panel);
    });
    loadStore();
    const kept = cachedShared(pageId);
    setShared(kept.data);
    buildDrawer();
    applyLayout();
    if (!kept.fresh) readShared(pageId).then((data) => {
        const before = JSON.stringify(shared);
        setShared(data);
        if (JSON.stringify(shared) !== before && !activeProfile()) applyLayout(); else paintDrawer();
    }).catch((error) => console.warn("The shared default layout could not be read: ", error));
    byId("addPanelBtn").addEventListener("click", () => openPanelDialog(null));
    byId("doneArrangingBtn").addEventListener("click", () => setEditing(false));
    wireBoard();
    watchForFit();
    window.setTimeout(() => board().classList.add("settled"), 900);          // the panels' entrance is over: moving them must not replay it
}

/** Gives a panel what every panel has: an inner box for its content, the gold label and the edges for arranging, and its "..." menu. */
function decorate(panel) {
    if (panel.dataset.decorated) return;
    panel.dataset.decorated = "1";
    const inner = document.createElement("div");
    inner.className = "panel-inner";
    while (panel.firstChild) inner.appendChild(panel.firstChild);
    panel.appendChild(inner);
    panel.insertAdjacentHTML("afterbegin", `<div class="panel-tools">${icon("grip")}<span class="tools-name"></span><span class="size"></span></div>`);
    panel.insertAdjacentHTML("beforeend", `<div class="resize-x"></div><div class="resize-y" title="Drag to set the height. Double-click: automatic height."></div>`);
    const menu = `<button type="button" class="icon-btn small panel-menu-btn" aria-label="Panel options" aria-haspopup="menu">${icon("more")}</button>`;
    const head = inner.querySelector(":scope > .panel-head");
    if (head) {
        // the short line a folded panel shows beside its title goes right after the title and its "i"; the arrow that folds
        // the panel and the "..." menu close the bar
        const after = head.querySelector(":scope > .guide-badge") || head.querySelector(":scope > .panel-title");
        if (after) after.insertAdjacentHTML("afterend", FOLD_PARTS); else head.insertAdjacentHTML("afterbegin", FOLD_PARTS);
        // the arrow and the "..." menu close the bar, except for a part that asks to stay at the very end (data-head-end: the bin)
        // data-no-fold: the list a page is for has no arrow, and so can never be folded away
        const closers = (panel.hasAttribute("data-no-fold") ? "" : foldButton()) + menu;
        const tail = head.querySelector("[data-head-end]");
        // without such a part they get a group of their own, so on a narrow panel they wrap together (the "..." is invisible until
        // the pointer is over the panel: alone on a second line it would leave an empty band)
        if (tail) tail.insertAdjacentHTML("beforebegin", closers); else head.insertAdjacentHTML("beforeend", `<span class="head-end">${closers}</span>`);
    } else panel.querySelector(".panel-tools").insertAdjacentHTML("beforeend", menu);
    panel.querySelector(".tools-name").textContent = panel.dataset.label || panel.dataset.panel;
}

function setTitle(panel, title) {
    panel.dataset.label = title;
    panel.querySelector(":scope > .panel-tools .tools-name").textContent = title;
    const heading = panel.querySelector(":scope > .panel-inner > .panel-head .panel-title");
    if (heading && (panel.dataset.custom || panel.classList.contains("group"))) heading.textContent = title;
}

function makeDataPanel(widget) {
    const panel = document.createElement("section");
    panel.className = "panel widget";
    panel.dataset.panel = widget.id;
    panel.dataset.custom = "1";
    panel.innerHTML = `<div class="panel-head"><h3 class="panel-title"></h3><span class="panel-count widget-count"></span><span class="spacer"></span></div>`;
    decorate(panel);
    return panel;
}

function makeGroupPanel(group) {
    const panel = document.createElement("section");
    panel.className = "panel group";
    panel.dataset.panel = group.id;
    panel.innerHTML = `<div class="panel-head group-head"><h3 class="panel-title"></h3><div class="group-tabs" role="tablist"></div><span class="spacer"></span></div><div class="group-body"></div>`;
    decorate(panel);
    return panel;
}

/** The tabs (or the title) of a combined panel, and which of its panels shows. */
function paintGroup(panel, group) {
    const members = Array.from(panel.querySelectorAll(":scope > .panel-inner > .group-body > .panel"));
    const mode = GROUP_MODES.some((m) => m.id === group.mode) ? group.mode : "tabs";
    panel.dataset.group = mode;
    const active = Math.min(Math.max(0, group.active || 0), members.length - 1);
    const tabs = panel.querySelector(".group-tabs");
    tabs.replaceChildren(...members.map((member, index) => {
        const tab = document.createElement("button");
        tab.type = "button";
        tab.className = "group-tab" + (index === active ? " on" : "");
        tab.setAttribute("role", "tab");
        tab.setAttribute("aria-selected", String(index === active));
        tab.dataset.index = String(index);
        tab.textContent = member.dataset.label || member.dataset.panel;
        return tab;
    }));
    members.forEach((member, index) => member.classList.toggle("active-member", mode !== "tabs" || index === active));
    panel.querySelector(".group-body").style.setProperty("--n", String(Math.max(1, members.length)));
}

// ---------------------------------------------------------------------------------------------
// Putting the active layout on the page
// ---------------------------------------------------------------------------------------------
function applyLayout() {
    if (!defaults) return;
    const page = pageData(false);
    const b = board();

    // 1. every combined panel is taken apart again ...
    b.querySelectorAll(".panel.group").forEach((group) => {
        group.querySelectorAll(":scope > .panel-inner > .group-body > .panel").forEach((member) => { member.classList.remove("member", "active-member"); b.appendChild(member); });
        group.remove();
    });
    // 2. ... data panels the layout does not have go, the ones it has are made
    const wanted = new Map((page ? page.widgets : []).map((widget) => [widget.id, widget]));
    b.querySelectorAll(".panel[data-custom]").forEach((panel) => { if (!wanted.has(panel.dataset.panel)) { unmountWidget(panel); panel.remove(); } });
    wanted.forEach((widget) => {
        let panel = panelOf(widget.id);
        if (!panel) { panel = makeDataPanel(widget); b.appendChild(panel); }
        setTitle(panel, widget.title || defaultTitle(normalizeConfig(widget)));
        mountWidget(panel, widget);
    });
    // 3. a built-in panel shown as a list, a row, a grid or a graph
    defaults.order.forEach((id) => {
        const panel = panelOf(id), view = page && page.views[id];
        if (view && view.view && defaults.source[id]) { panel.classList.add("alt-view"); mountWidget(panel, Object.assign({}, view, { source: defaults.source[id] })); }
        else { panel.classList.remove("alt-view"); unmountWidget(panel); }
    });
    // 4. the combined panels of this layout
    (page ? page.groups : []).forEach((group) => {
        const members = group.members.map(panelOf).filter((panel) => panel && !panel.classList.contains("member"));
        if (members.length === 0) return;
        const panel = makeGroupPanel(group);
        b.appendChild(panel);
        const body = panel.querySelector(".group-body");
        members.forEach((member) => { member.classList.add("member"); member.classList.remove("is-hidden"); member.style.height = ""; member.removeAttribute("data-fixed-h"); body.appendChild(member); });
        setTitle(panel, group.title || members.map((m) => m.dataset.label).join(" + "));
        paintGroup(panel, group);
    });
    // 5. order, sizes, hidden
    const tops = new Map(topPanels().map((panel) => [panel.dataset.panel, panel]));
    const order = (page ? page.order : []).filter((id) => tops.has(id));
    defaults.order.forEach((id) => { if (tops.has(id) && !order.includes(id)) order.push(id); });
    tops.forEach((panel, id) => { if (!order.includes(id)) order.push(id); });          // something new goes to the end
    order.forEach((id) => b.appendChild(tops.get(id)));
    tops.forEach((panel, id) => {
        const size = (page && page.size[id]) || defaults.size[id] || { w: 6 };
        panel.style.setProperty("--w", String(Math.min(12, Math.max(2, Number(size.w) || 6))));
        if (size.h) { panel.style.height = size.h + "px"; panel.setAttribute("data-fixed-h", "1"); } else { panel.style.height = ""; panel.removeAttribute("data-fixed-h"); }
        panel.classList.toggle("is-hidden", !!(page && page.hidden.includes(id)));
        labelSize(panel);
    });

    // the default places: a panel marked data-right sits in the right-hand column whatever the packing would do
    b.classList.toggle("pinned", !page || page.order.length === 0);

    const profile = activeProfile();
    document.body.classList.toggle("own-layout", !!profile);          // with a layout of your own, panels move by their title bar
    applyAppearance();
    paintFold();
    paintDrawer();
    fitNow();
}

// ---------------------------------------------------------------------------------------------
// Auto resize: the panels are packed without gaps
// ---------------------------------------------------------------------------------------------
/** True when the active layout closes the gaps on this page. The default layout always does. */
function fitOn() {
    const page = pageData(false);
    return !page || page.fit !== false;
}

let fitWatcher = null, fitQueued = 0;

/** Measures every panel's own height and gives it that many rows of the packing grid (see layoutRules.js). */
function fitNow() {
    const b = board();
    if (!b) return;
    const on = fitOn();
    const panels = topPanels();
    if (b.classList.contains("fit") !== on) b.classList.toggle("fit", on);
    if (on) {
        b.style.setProperty("--row-unit", ROW_UNIT + "px");
        const gap = parseFloat(getComputedStyle(b).columnGap) || 16;
        b.classList.add("measuring");                 // for one moment every panel has its own height again
        const heights = panels.map((panel) => panel.offsetHeight);
        b.classList.remove("measuring");
        panels.forEach((panel, index) => {
            const rows = String(rowsFor(heights[index], gap));
            if (panel.style.getPropertyValue("--rows") !== rows) panel.style.setProperty("--rows", rows);
        });
    } else panels.forEach((panel) => panel.style.removeProperty("--rows"));
    if (fitWatcher) fitWatcher.takeRecords();         // what was just written here is not a reason to measure again
}

function queueFit() {
    if (fitQueued) return;
    fitQueued = window.requestAnimationFrame(() => { fitQueued = 0; fitNow(); });
}

/** Whatever changes a panel's height (a list that loaded, a row that opened, a helper line, the window) packs the page again. */
function watchForFit() {
    const b = board();
    fitWatcher = new MutationObserver(() => {
        // while a panel is carried around its place is not final, and measuring on every step would make it stutter
        if (document.body.classList.contains("is-dragging") && b.querySelector(":scope > .panel.floating")) return;
        queueFit();
    });
    fitWatcher.observe(b, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ["class", "style", "hidden"] });
    window.addEventListener("resize", queueFit, { passive: true });
    // the board can get narrower or wider without the window changing (the sidebar folds, the settings panel opens): its text
    // wraps onto more or fewer lines, so every panel is measured again or it would run over the panel under it
    if (window.ResizeObserver) {
        let lastWidth = b.clientWidth;
        new ResizeObserver(() => { if (b.clientWidth !== lastWidth) { lastWidth = b.clientWidth; queueFit(); } }).observe(b);
    }
    if (document.fonts && document.fonts.ready) document.fonts.ready.then(queueFit).catch(() => { });
    fitNow();
    watchForSettingsGrow();
}

// ---------------------------------------------------------------------------------------------
// While Quick settings pushes the page (7 November 2026): a panel left with empty room at the right of its row (Quick actions
// beside nothing) widens into that room. When Quick settings closes, every panel has its own saved width again. The saved
// layout (--w) is never touched: the extra width is only data-qs-grow + --qs-span, cleared on close.
// ---------------------------------------------------------------------------------------------
function clearSettingsGrow() {
    document.querySelectorAll(".board > .panel[data-qs-grow]").forEach((panel) => { panel.removeAttribute("data-qs-grow"); panel.style.removeProperty("--qs-span"); });
}

function settingsGrowNow() {
    clearSettingsGrow();
    const b = board();
    const pushing = document.documentElement.classList.contains("qs-open") && window.matchMedia("(min-width: 1024px)").matches;
    if (!b || !pushing || document.body.classList.contains("layout-editing")) return;
    const style = getComputedStyle(b);
    const gap = parseFloat(style.columnGap) || 16;
    const box = b.getBoundingClientRect();
    const left = box.left + parseFloat(style.paddingLeft), right = box.right - parseFloat(style.paddingRight);
    const column = (right - left + gap) / 12;
    if (!(column > 0)) return;
    const rects = topPanels().filter((panel) => panel.offsetParent !== null).map((panel) => ({ panel, r: panel.getBoundingClientRect() }));
    const grows = [];
    rects.forEach(({ panel, r }) => {
        // the nearest panel to its right that shares any of its height; none means the room runs to the board's edge
        let edge = right;
        rects.forEach((other) => {
            if (other.panel === panel || other.r.bottom <= r.top + 1 || other.r.top >= r.bottom - 1 || other.r.left < r.right - 1) return;
            edge = Math.min(edge, other.r.left - gap);
        });
        const free = Math.round((edge - r.right) / column);
        if (free < 1) return;
        const span = Math.min(12, Math.round((r.width + gap) / column) + free);
        grows.push([panel, span]);
    });
    grows.forEach(([panel, span]) => { panel.style.setProperty("--qs-span", String(span)); panel.setAttribute("data-qs-grow", ""); });
}

function watchForSettingsGrow() {
    const b = board();
    if (!b) return;
    // at once: the page width snaps when Quick settings opens (no animated margin), so the room is already final. A
    // MutationObserver and a ResizeObserver both answer before the next picture is drawn: the panel is never seen at its old width.
    const later = settingsGrowNow;
    let wasOpen = document.documentElement.classList.contains("qs-open");
    new MutationObserver(() => {
        const open = document.documentElement.classList.contains("qs-open");
        if (open === wasOpen) return;
        wasOpen = open;
        // opening: the board gets narrower, and its ResizeObserver below does the work after layout. Under 1024 px nothing is pushed.
        if (!open) clearSettingsGrow();
    }).observe(document.documentElement, { attributes: true, attributeFilter: ["class"] });
    new MutationObserver(() => { if (document.body.classList.contains("layout-editing")) clearSettingsGrow(); else if (wasOpen) later(); })
        .observe(document.body, { attributes: true, attributeFilter: ["class"] });
    if (window.ResizeObserver) {
        let lastWidth = b.clientWidth;
        new ResizeObserver(() => { if (b.clientWidth !== lastWidth) { lastWidth = b.clientWidth; if (wasOpen) later(); } }).observe(b);
    }
}

/** The switch in Layout & Style: gaps closed, or every panel on its own line of the grid as before. */
async function setFit(on) {
    if (!(await ensureProfile())) { paintDrawer(); return; }
    capture();
    pageData(true).fit = !!on;
    commit(on ? "Gaps closed: panels move up into free space." : "Auto resize is off for this page: panels keep their own rows.");
}

/**
 * The "Auto resize" button: every panel gets the height of its content again (a height set by hand is dropped) and
 * the gaps are closed. "Undo" in the message puts the sizes back.
 */
async function autoResize() {
    if (!activeProfile()) {
        toast("The default layout is resized already: its panels are packed without gaps. Make your own layout to change sizes.", "info", { label: "Make mine", run: () => newProfile() });
        return;
    }
    capture();
    const page = pageData(true);
    const before = JSON.stringify({ size: page.size, fit: page.fit });
    const fixed = Object.keys(page.size).filter((id) => page.size[id].h > 0).length;
    Object.keys(page.size).forEach((id) => { page.size[id].h = 0; });
    page.fit = true;
    commit();
    const what = fixed > 0 ? `${fixed} panel${fixed === 1 ? "" : "s"} got the height of ${fixed === 1 ? "its" : "their"} content back, and the gaps are closed.` : "Panels are packed without gaps.";
    toast(what, "ok", { label: "Undo", run: () => { const old = JSON.parse(before); page.size = old.size; page.fit = old.fit; commit(); } });
}

function labelSize(panel) {
    const label = panel.querySelector(":scope > .panel-tools .size");
    if (label) label.textContent = `${getComputedStyle(panel).getPropertyValue("--w").trim() || 12}/12` + (panel.style.height ? " · " + panel.style.height : "");
}

/** The font, sizes and colours of the active layout. Colours are kept per theme, so a dark choice never spoils the light look. */
export function applyAppearance() {
    const root = document.documentElement, style = root.style;
    const profile = activeProfile();
    const look = profile && profile.appearance ? profile.appearance : !profile && shared ? shared.appearance : {};
    const font = FONTS.find((f) => f.id === (look.font || "")) || FONTS[0];
    if (font.web && !document.getElementById("font-" + font.id)) {
        const link = document.createElement("link");
        link.id = "font-" + font.id; link.rel = "stylesheet"; link.href = "https://fonts.googleapis.com/css2?family=" + font.web + "&display=swap";
        document.head.appendChild(link);
    }
    const set = (name, value) => { if (value === undefined || value === null || value === "") style.removeProperty(name); else style.setProperty(name, value); };
    set("--font", font.stack);
    set("--scale", look.scale ? String(look.scale) : "");
    const round = look.radius !== undefined && look.radius !== "";
    set("--radius", round ? look.radius + "px" : "");
    set("--radius-sm", round ? Math.max(2, Math.round(look.radius * 0.64)) + "px" : "");
    if (look.density === "compact") root.dataset.density = "compact"; else delete root.dataset.density;
    const colours = (look.colors && look.colors[currentTheme()]) || {};
    set("--accent", colours.accent); set("--accent-text", colours.accent);
    set("--surface", colours.panel);
    set("--bg", colours.bg);
    paintPanelStyles();
}

/** The font list of a font id, loading a web font the first time it is used ("" for none). */
function fontStackOf(id) {
    const font = FONTS.find((f) => f.id === (id || ""));
    if (!font || !font.id) return "";
    if (font.web && !document.getElementById("font-" + font.id)) {
        const link = document.createElement("link");
        link.id = "font-" + font.id; link.rel = "stylesheet"; link.href = "https://fonts.googleapis.com/css2?family=" + font.web + "&display=swap";
        document.head.appendChild(link);
    }
    return font.stack;
}

/** "Style and text" of every panel on the page (again after a change of light / dark mode: colours are kept per mode). */
function paintPanelStyles() {
    const b = board();
    if (!b || !defaults) return;
    const page = pageData(false), theme = currentTheme();
    b.querySelectorAll(".panel[data-panel]").forEach((panel) => {
        const id = panel.dataset.panel;
        const style = (page && page.styles && page.styles[id]) || {};
        applyPanelStyle(panel, style, theme, fontStackOf(style.font));
        let texts = (page && page.texts && page.texts[id]) || {};
        // an added or combined panel has its own name (Rename): its title is not one of the fixed texts
        if (panel.dataset.custom || panel.classList.contains("group")) {
            const heading = panel.querySelector(":scope > .panel-inner > .panel-head .panel-title");
            if (heading) heading.dataset.ownText = heading.textContent = panel.dataset.label || heading.textContent;
            texts = Object.assign({}, texts, { title: "" });
        }
        applyPanelTexts(panel, texts);
    });
}

/** Writes where the panels are and how big they are into the active layout. */
function capture() {
    const page = pageData(true);
    if (!page) return;
    page.order = topPanels().map((panel) => panel.dataset.panel);
    topPanels().forEach((panel) => { page.size[panel.dataset.panel] = { w: Number(getComputedStyle(panel).getPropertyValue("--w")) || 12, h: panel.style.height ? parseInt(panel.style.height, 10) : 0 }; });
    saveStore();
}

/** Saves a change of what the page is made of and draws the page again. */
function commit(message) {
    saveStore();
    applyLayout();
    if (message) toast(message, "ok");
}

async function setEditing(on) {
    if (on === editing) return;
    if (on && !(await ensureProfile())) return;
    editing = on;
    document.body.classList.toggle("layout-editing", on);
    closeMenu();
    if (on) closeWindow();
    else { capture(); toast(`Layout saved for ${activeProfile().name}.`, "ok"); }
    paintDrawer();
}

// ---------------------------------------------------------------------------------------------
// What a panel is shown as, hiding, removing, combining
// ---------------------------------------------------------------------------------------------
const widgetOf = (page, id) => (page ? page.widgets.find((widget) => widget.id === id) : null);
const groupOf = (page, id) => (page ? page.groups.find((group) => group.id === id) : null);
const groupHolding = (page, id) => (page ? page.groups.find((group) => group.members.includes(id)) : null);

/** What a panel shows now: { source, view, graph, by, measure }, or null for a built-in panel in its standard look. */
function viewOf(id) {
    const page = pageData(false);
    const widget = widgetOf(page, id);
    if (widget) return normalizeConfig(widget);
    const view = page && page.views[id];
    return view && view.view && defaults.source[id] ? normalizeConfig(Object.assign({}, view, { source: defaults.source[id] })) : null;
}

async function setView(id, config) {
    if (!(await ensureProfile())) return;
    capture();
    const page = pageData(true), widget = widgetOf(page, id);
    if (widget) Object.assign(widget, cleanView(normalizeConfig(Object.assign({}, widget, config))));
    else if (config) page.views[id] = cleanView(normalizeConfig(Object.assign({ source: defaults.source[id] }, config)));
    else delete page.views[id];
    commit();
}

async function hidePanel(id, hidden) {
    if (!(await ensureProfile())) return;
    capture();
    const page = pageData(true);
    page.hidden = page.hidden.filter((x) => x !== id);
    if (hidden) page.hidden.push(id);
    commit(hidden ? `"${labelOf(id)}" is hidden. Bring it back with Layout & Style > Panel.` : null);
}

/** A combined panel with fewer than two panels left is no combined panel: its last one goes back to the page. */
function tidyGroups(page) {
    page.groups.slice().forEach((group) => {
        if (group.members.length >= 2) return;
        const at = page.order.indexOf(group.id);
        page.order.splice(at < 0 ? page.order.length : at, at < 0 ? 0 : 1, ...group.members);
        group.members.forEach((member) => { if (page.size[group.id] && !page.size[member]) page.size[member] = Object.assign({}, page.size[group.id]); });
        delete page.size[group.id];
        page.groups = page.groups.filter((g) => g !== group);
    });
}

async function removePanel(id) {
    const sure = await askConfirm({ title: `Remove "${labelOf(id)}"?`, message: "Only the panel goes; its data stays. You can add it again with Layout & Style > Panel.", confirmLabel: "Remove panel", danger: true });
    if (!sure) return;
    capture();
    const page = pageData(true);
    page.widgets = page.widgets.filter((widget) => widget.id !== id);
    page.groups.forEach((group) => { group.members = group.members.filter((member) => member !== id); });
    page.order = page.order.filter((x) => x !== id);
    delete page.size[id];
    tidyGroups(page);
    commit();
}

async function renamePanel(id) {
    if (!(await ensureProfile())) return;
    const page = pageData(false);
    const target = widgetOf(page, id) || groupOf(page, id);
    if (!target) return;
    const name = await askText({ title: "Name of the panel", label: "Name", value: labelOf(id), maxLength: 40 });
    if (!name) return;
    target.title = cleanName(name, 40);
    commit();
}

/** Puts panel `id` together with panel `targetId` in one combined panel (a new one, or the one `targetId` already is). */
async function combine(id, targetId) {
    if (!(await ensureProfile())) return;
    capture();
    const page = pageData(true);
    const target = groupOf(page, targetId), mine = groupOf(page, id);
    if (target) {
        target.members.push(...(mine ? mine.members : [id]));
        if (mine) { page.groups = page.groups.filter((group) => group !== mine); delete page.size[mine.id]; }
        target.active = target.members.length - 1;
        page.order = page.order.filter((x) => x !== id);
    } else if (mine) {
        mine.members.push(targetId);
        mine.active = mine.members.length - 1;
        page.order = page.order.filter((x) => x !== targetId);
    } else {
        const group = { id: "g" + Date.now().toString(36), title: cleanName(`${labelOf(targetId)} + ${labelOf(id)}`, 40), mode: "tabs", members: [targetId, id], active: 1 };
        page.groups.push(group);
        page.order = page.order.map((x) => (x === targetId ? group.id : x)).filter((x) => x !== id);     // it takes the place and the size of the panel it was dropped on
        page.size[group.id] = Object.assign({}, page.size[targetId] || defaults.size[targetId] || { w: 6 }, { h: 0 });
    }
    page.hidden = page.hidden.filter((x) => x !== id && x !== targetId);
    commit("Combined. Its menu changes it to tabs, stacked or side by side, or splits it up again.");
}

async function splitGroup(id) {
    capture();
    const page = pageData(true), group = groupOf(page, id);
    if (!group) return;
    const at = page.order.indexOf(id);
    page.order.splice(at < 0 ? page.order.length : at, at < 0 ? 0 : 1, ...group.members);
    group.members.forEach((member) => { if (!page.size[member]) page.size[member] = Object.assign({}, page.size[id] || { w: 6 }); });
    delete page.size[id];
    page.groups = page.groups.filter((g) => g !== group);
    commit();
}

async function takeOut(memberId) {
    capture();
    const page = pageData(true), group = groupHolding(page, memberId);
    if (!group) return;
    group.members = group.members.filter((member) => member !== memberId);
    group.active = 0;
    const at = page.order.indexOf(group.id);
    page.order.splice(at < 0 ? page.order.length : at + 1, 0, memberId);
    tidyGroups(page);
    commit();
}

function openPanelMenu(panel, anchor) {
    const id = panel.dataset.panel, page = pageData(false);
    const isGroup = panel.classList.contains("group"), isCustom = !!panel.dataset.custom, isMember = panel.classList.contains("member");
    const source = isCustom ? (widgetOf(page, id) || {}).source : defaults.source[id];
    const now = viewOf(id);
    const items = [];

    if (source && datasetSpec(source)) {
        items.push({ heading: "Show as" });
        if (!isCustom) items.push({ label: "Standard", icon: "layout", description: "The page's own look.", checked: !now, run: () => setView(id, null) });
        VIEWS.forEach((v) => items.push({ label: v.label, icon: v.icon, description: v.hint, checked: !!now && now.view === v.id, run: () => setView(id, Object.assign({}, now || {}, { view: v.id })) }));
        if (now && now.view === "graph") items.push({ label: "Graph settings", icon: "sliders", description: "Bars, line, donut or table, and what to count.", run: () => openPanelDialog(id) });
    }
    if (isGroup) {
        const group = groupOf(page, id);
        items.push({ heading: "Combined panel" });
        GROUP_MODES.forEach((m) => items.push({ label: m.label, icon: m.icon, description: m.hint, checked: group && group.mode === m.id, run: () => { group.mode = m.id; commit(); } }));
        items.push({ label: "Split up", icon: "split", description: "Each panel stands by itself again.", run: () => splitGroup(id) });
    }
    if (isMember) items.push({ separator: true }, { label: "Take out of the group", icon: "split", run: () => takeOut(id) });
    else {
        const others = topPanels().filter((other) => other !== panel && !other.classList.contains("is-hidden")).slice(0, 8);
        if (others.length > 0) {
            items.push({ heading: "Combine with" });
            others.forEach((other) => items.push({ label: other.dataset.label || other.dataset.panel, icon: "layers", run: () => combine(id, other.dataset.panel) }));
        }
    }
    items.push({ separator: true });
    items.push({ label: "Style and text...", icon: "palette", description: "Size, cards, text, font, colours and motion of this panel.", run: () => openStyleDialog(id) });
    if (isCustom || isGroup) items.push({ label: "Rename", icon: "edit", run: () => renamePanel(id) });
    if (isCustom) items.push({ label: "Remove panel", icon: "trash", danger: true, run: () => removePanel(id) });
    else if (!isGroup && !isMember) {
        const hidden = panel.classList.contains("is-hidden");
        items.push({ label: hidden ? "Show panel" : "Hide panel", icon: hidden ? "eye" : "eyeOff", run: () => hidePanel(id, !hidden) });
    }
    if (!isMember && panel.querySelector(":scope > .panel-inner > .panel-head .panel-fold-btn")) {
        const folded = foldedNow(id);
        items.push({ label: folded ? "Open panel" : "Fold panel", icon: folded ? "down" : "up", description: folded ? null : "Only its title stays. The arrow at its top right opens it again.", run: () => toggleFold(id) });
    }
    if (hasAssist(id)) { const off = assistHidden(id); items.push({ label: off ? "Show helper" : "Hide helper", icon: "sparkle", description: off ? "The line that says what needs attention here." : null, run: () => setAssistHidden(id, !off) }); }
    items.push({ label: editing ? "Done arranging" : "Arrange panels", icon: "grip", description: editing ? null : "Move and resize every panel.", run: () => setEditing(!editing) });
    openMenu(anchor, items);
}

// ---------------------------------------------------------------------------------------------
// "Add a panel" and "Graph settings": one small form with a live preview
// ---------------------------------------------------------------------------------------------
/** editId = null: a new data panel. Otherwise the settings of that panel. */
async function openPanelDialog(editId) {
    const sets = datasetList();
    if (sets.length === 0) { toast("This page has no data to show in a panel.", "info"); return; }
    if (!(await ensureProfile())) return;
    const page = pageData(true);
    const editingWidget = editId ? widgetOf(page, editId) : null;
    const builtIn = editId && !editingWidget;
    const start = editId ? viewOf(editId) : null;
    let config = normalizeConfig(start || { source: sets[0].id, view: "graph" });
    let name = editingWidget ? editingWidget.title : "", nameTouched = !!name;

    const overlay = document.createElement("div");
    overlay.className = "modal-overlay";
    overlay.style.display = "flex";
    overlay.innerHTML = `
        <div class="modal-content wide panel-dialog" role="dialog" aria-modal="true">
            <h3>${editId ? "Panel settings" : "Add a panel"}</h3>
            <p class="modal-intro">${editId ? "Change how this panel shows its data." : "Show the page's data once more, your way. The data itself stays the same."}</p>
            <form class="modal-form" novalidate>
                <div class="form-row">
                    <div class="field"${builtIn ? " hidden" : ""}><label for="pdSource">Show</label><select id="pdSource"></select></div>
                    <div class="field"${builtIn ? " hidden" : ""}><label for="pdName">Name</label><input type="text" id="pdName" maxlength="40" autocomplete="off"></div>
                </div>
                <div class="field"><span class="field-label">As</span><div class="segmented" id="pdView"></div></div>
                <div class="form-row" id="pdGraphRow">
                    <div class="field"><span class="field-label">Graph</span><div class="segmented" id="pdChart"></div></div>
                    <div class="field"><label for="pdBy">Group by</label><select id="pdBy"></select></div>
                </div>
                <div class="field" id="pdMeasureField"><label for="pdMeasure">Count</label><select id="pdMeasure"></select></div>
                <div class="field"><span class="field-label">Preview</span><section class="panel preview"><div class="panel-inner"></div></section></div>
                <div class="field" id="pdHiddenField" hidden><span class="field-label">Hidden panels (click to bring back)</span><div class="checks" id="pdHidden"></div></div>
                <div class="modal-actions">
                    <button type="button" class="cancel-btn" data-close>Cancel</button>
                    <button type="submit" class="save-btn">${editId ? "Save" : "Add panel"}</button>
                </div>
            </form>
        </div>`;
    document.body.appendChild(overlay);
    const q = (selector) => overlay.querySelector(selector);
    const preview = q(".panel.preview");
    const close = () => { document.removeEventListener("keydown", onKey, true); unmountWidget(preview); overlay.remove(); };
    const onKey = (event) => { if (event.key === "Escape") { event.stopPropagation(); close(); } };
    document.addEventListener("keydown", onKey, true);
    overlay.addEventListener("mousedown", (event) => { if (event.target === overlay) close(); });
    q("[data-close]").addEventListener("click", close);

    const segmented = (host, options, value, pick) => {
        host.replaceChildren(...options.map((option) => {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "seg" + (option.id === value ? " on" : "");
            button.innerHTML = icon(option.icon);
            button.append(option.label);
            if (option.hint) button.dataset.tip = option.hint;
            button.disabled = !!option.disabled;
            button.addEventListener("click", () => pick(option.id));
            return button;
        }));
    };
    const paint = () => {
        const d = datasetSpec(config.source), dims = (d && d.dimensions) || [], measures = measuresOf(d);
        q("#pdSource").innerHTML = sets.map((set) => `<option value="${esc(set.id)}">${esc(set.label)}</option>`).join("");
        q("#pdSource").value = config.source;
        if (!nameTouched) name = defaultTitle(config);
        q("#pdName").value = name;
        segmented(q("#pdView"), VIEWS, config.view, (view) => { config = normalizeConfig(Object.assign({}, config, { view })); paint(); });
        const graph = config.view === "graph";
        q("#pdGraphRow").hidden = !graph;
        q("#pdMeasureField").hidden = !graph || measures.length < 2;
        if (graph) {
            const timed = dims.some((dim) => dim.ordered);
            segmented(q("#pdChart"), CHARTS.map((c) => Object.assign({}, c, { disabled: c.id === "line" && !timed, hint: c.id === "line" && !timed ? "A line needs something in order, such as days. This data has none." : null })),
                config.graph, (chart) => { config = normalizeConfig(Object.assign({}, config, { graph: chart })); paint(); });
            q("#pdBy").innerHTML = dims.map((dim) => `<option value="${esc(dim.id)}">${esc(dim.label)}</option>`).join("");
            q("#pdBy").value = config.by;
            q("#pdMeasure").innerHTML = measures.map((m) => `<option value="${esc(m.id)}">${esc(m.label)}</option>`).join("");
            q("#pdMeasure").value = config.measure;
        }
        mountWidget(preview, config);
    };
    q("#pdSource").addEventListener("change", (event) => { config = normalizeConfig(Object.assign({}, config, { source: event.target.value, by: "", measure: "" })); paint(); });
    q("#pdBy").addEventListener("change", (event) => { config = normalizeConfig(Object.assign({}, config, { by: event.target.value })); paint(); });
    q("#pdMeasure").addEventListener("change", (event) => { config = normalizeConfig(Object.assign({}, config, { measure: event.target.value })); paint(); });
    q("#pdName").addEventListener("input", (event) => { name = event.target.value; nameTouched = name.trim().length > 0; });

    if (!editId && page.hidden.length > 0) {
        q("#pdHiddenField").hidden = false;
        q("#pdHidden").replaceChildren(...page.hidden.filter((id) => panelOf(id)).map((id) => {
            const chip = document.createElement("button");
            chip.type = "button"; chip.className = "row-btn"; chip.innerHTML = icon("eye"); chip.append(labelOf(id));
            chip.addEventListener("click", () => { close(); hidePanel(id, false); });
            return chip;
        }));
    }
    q("form").addEventListener("submit", (event) => {
        event.preventDefault();
        capture();
        if (editingWidget) { Object.assign(editingWidget, cleanView(config)); editingWidget.title = cleanName(name, 40) || defaultTitle(config); }
        else if (builtIn) page.views[editId] = cleanView(config);
        else {
            if (page.widgets.length >= MAX_ADDED) { toast(`A page holds at most ${MAX_ADDED} added panels. Remove one first.`, "error"); return; }
            const widget = Object.assign({ id: "w" + Date.now().toString(36), title: cleanName(name, 40) || defaultTitle(config) }, cleanView(config));
            page.widgets.push(widget);
            page.order.push(widget.id);
            page.size[widget.id] = { w: 6, h: 0 };
        }
        close();
        commit(editId ? null : "Panel added at the end of the page. Drag it by its title to where you want it.");
        if (!editId) { const added = topPanels().pop(); if (added) added.scrollIntoView({ behavior: "smooth", block: "center" }); }
    });
    hydrate(overlay);
    paint();
}

// ---------------------------------------------------------------------------------------------
// "Style and text" of ONE panel (9 November 2026): size, the cards inside, the fixed texts, font, colours, corners, motion.
// Every change shows at once on the real panel (the form sits at the side, so the panel stays in view). Cancel puts back
// what was there; Save keeps it in the layout, and Undo (Layout & Style, or Ctrl+Z) takes it back again.
// ---------------------------------------------------------------------------------------------
const COLOUR_FIELDS = [
    { key: "bg", label: "Panel", token: "--surface" },
    { key: "card", label: "Cards inside", token: "--surface-2" },
    { key: "text", label: "Text", token: "--text" },
    { key: "accent", label: "Accent", token: "--accent" },
    { key: "border", label: "Outline", token: "--border" }
];
const WIDTHS = [[12, "Whole width"], [9, "Three quarters"], [8, "Two thirds"], [6, "Half"], [4, "One third"], [3, "One quarter"]];

async function openStyleDialog(id) {
    if (!(await ensureProfile())) return;
    const panel = panelOf(id);
    if (!panel) return;
    if (editing) await setEditing(false);
    const page = pageData(true);
    page.styles = page.styles || {}; page.texts = page.texts || {};
    const theme = currentTheme();
    const before = { style: JSON.parse(JSON.stringify(page.styles[id] || {})), texts: JSON.parse(JSON.stringify(page.texts[id] || {})), w: getComputedStyle(panel).getPropertyValue("--w").trim(), h: panel.style.height };
    let style = JSON.parse(JSON.stringify(before.style)), texts = JSON.parse(JSON.stringify(before.texts));
    const named = !!panel.dataset.custom || panel.classList.contains("group");          // its title is its own name (Rename)
    const own = ownTexts(panel);
    const hasCards = !!panel.querySelector(".kpi-row, .stat-row");
    const isMember = panel.classList.contains("member");
    const opt = (list, value) => list.map((o) => `<option value="${esc(o.id)}"${o.id === (value || "") ? " selected" : ""}>${esc(o.label)}</option>`).join("");
    const width = Number(before.w) || 12;
    const widthOptions = WIDTHS.some(([w]) => w === width) ? WIDTHS : WIDTHS.concat([[width, `${width} of 12 columns`]]);

    const overlay = document.createElement("div");
    overlay.className = "modal-overlay style-overlay";
    overlay.style.display = "flex";
    overlay.innerHTML = `
        <div class="modal-content style-dialog" role="dialog" aria-modal="true" aria-labelledby="sdTitle">
            <h3 id="sdTitle">Style and text: ${esc(labelOf(id))}</h3>
            <p class="modal-intro">Changes show on the panel at once. Nothing here moves a panel out of the page's grid, so panels never overlap or leave holes, on any screen.</p>
            <form class="modal-form" novalidate>
                <div class="form-section"${isMember ? " hidden" : ""}>
                    <span class="form-section-title">Size</span>
                    <div class="form-row">
                        <div class="field"><label for="sdWidth">Width</label><select id="sdWidth">${widthOptions.map(([w, l]) => `<option value="${w}"${w === width ? " selected" : ""}>${esc(l)} (${w}/12)</option>`).join("")}</select></div>
                        <div class="field"><label for="sdHeight">Height (pixels)</label><input type="number" id="sdHeight" min="120" max="1400" step="10" placeholder="Automatic" value="${before.h ? parseInt(before.h, 10) : ""}"></div>
                    </div>
                    <span class="field-hint">Empty height = as tall as its content. On a tablet a narrow panel takes half the width, on a phone every panel takes the whole width: this setting is for wide screens.</span>
                </div>

                <div class="form-section">
                    <span class="form-section-title">Cards inside</span>
                    <div class="field"><label for="sdOrient">Orientation</label><select id="sdOrient"${hasCards ? "" : " disabled"}>${opt(ORIENTS, style.orient)}</select>
                    <span class="field-hint">${hasCards ? "Side by side or one under the other. A panel is never turned at an angle: a turned panel would cover the ones next to it." : "This panel has no row of cards. To show its list as a row, grid or graph, use its ... menu > Show as."}</span></div>
                </div>

                <div class="form-section">
                    <span class="form-section-title">Text</span>
                    <p class="field-hint">Text goes only into these fixed places, so it always lines up and moves with the panel. Names and numbers from the database are never changed here.</p>
                    <div class="field"${own.title === null ? " hidden" : ""}><label for="sdTitleText">Title</label><input type="text" id="sdTitleText" maxlength="${LIMITS.title}" autocomplete="off" placeholder="${esc(own.title || "")}" value="${esc(named ? labelOf(id) : texts.title || "")}">
                        <span class="field-hint">${named ? "This panel's own name." : "Empty = the page's own title."}</span></div>
                    <div class="field"><label for="sdSubtitle">Subtitle</label><input type="text" id="sdSubtitle" maxlength="${LIMITS.subtitle}" autocomplete="off" placeholder="One short line under the title" value="${esc(texts.subtitle || "")}">
                        <span class="field-hint">Shown right under the title. At most ${LIMITS.subtitle} characters.</span></div>
                    <div class="field"><label for="sdNote">Note</label><textarea id="sdNote" rows="3" maxlength="${LIMITS.note}" placeholder="For example: Check these every morning before 10.">${esc(texts.note || "")}</textarea>
                        <div class="form-row"><div class="field"><label for="sdNotePlace">Where the note goes</label><select id="sdNotePlace">${opt(NOTE_PLACES, texts.notePlace || "top")}</select></div></div>
                        <span class="field-hint">Plain text in a framed box, at the top or the bottom of the panel only. At most ${LIMITS.note} characters and 5 lines.</span></div>
                    <div class="field" id="sdLabelsField"${own.labels.length ? "" : " hidden"}><span class="field-label">Card labels</span>
                        <div class="form-row" id="sdLabels">${own.labels.slice(0, LIMITS.labels).map((label, i) => `<div class="field"><label for="sdLabel${i}">Card ${i + 1}</label><input type="text" id="sdLabel${i}" data-label-index="${i}" maxlength="${LIMITS.label}" autocomplete="off" placeholder="${esc(label)}" value="${esc((texts.labels && texts.labels[i]) || "")}"></div>`).join("")}</div>
                        <span class="field-hint">Empty = the page's own label (shown greyed).</span></div>
                </div>

                <div class="form-section">
                    <span class="form-section-title">Font</span>
                    <div class="form-row">
                        <div class="field"><label for="sdFont">Font</label><select id="sdFont"><option value="">Same as the page</option>${FONTS.filter((f) => f.id).map((f) => `<option value="${esc(f.id)}"${f.id === style.font ? " selected" : ""}>${esc(f.label)}</option>`).join("")}</select></div>
                        <div class="field"><label for="sdScale">Text size: <span id="sdScaleText"></span></label><input type="range" id="sdScale" min="${LIMITS.scaleMin}" max="${LIMITS.scaleMax}" step="0.05" value="${style.scale || 1}"></div>
                    </div>
                    <span class="field-hint">Text never gets smaller than 12 pixels, so it stays readable on phones.</span>
                </div>

                <div class="form-section">
                    <span class="form-section-title">Colours for ${esc(theme)} mode</span>
                    <div class="colour-row" id="sdColours"></div>
                    <span class="field-hint contrast-note" id="sdContrast"></span>
                    <span class="field-hint">Colours are kept for each mode on its own: switch to the other mode to set its colours.</span>
                </div>

                <div class="form-section">
                    <span class="form-section-title">Corners</span>
                    <div class="drawer-row"><label class="check"><input type="checkbox" id="sdRadiusOwn"${style.radius !== undefined ? " checked" : ""}> <span>Own roundness</span></label><input type="range" id="sdRadius" min="0" max="${LIMITS.radiusMax}" step="1" value="${style.radius !== undefined ? style.radius : 14}"></div>
                </div>

                <div class="form-section">
                    <span class="form-section-title">Motion</span>
                    <div class="form-row">
                        <div class="field"><label for="sdAnim">When the page opens</label><div class="inline-actions"><select id="sdAnim">${opt(ANIMATIONS, style.anim)}</select><button type="button" class="icon-btn small" id="sdPlay" aria-label="Play the animation">${icon("play")}</button></div></div>
                        <div class="field"><label for="sdTrans">Transition of things inside</label><select id="sdTrans">${opt(TRANSITIONS, style.trans)}</select></div>
                    </div>
                    <div class="field"><label for="sdEdge">Edge light</label><select id="sdEdge">${opt(EDGES, style.edge)}</select></div>
                    <span class="field-hint" id="sdMotionHint"></span>
                </div>

                <div class="modal-actions">
                    <button type="button" class="btn danger" id="sdReset">${icon("reset")}Reset this panel</button>
                    <span class="spacer"></span>
                    <button type="button" class="cancel-btn" data-close>Cancel</button>
                    <button type="submit" class="save-btn">Save</button>
                </div>
            </form>
        </div>`;
    document.body.appendChild(overlay);
    panel.classList.add("style-target");
    panel.scrollIntoView({ behavior: "smooth", block: "nearest" });
    const q = (selector) => overlay.querySelector(selector);

    const sizeNow = () => {
        if (isMember) return;
        panel.style.setProperty("--w", q("#sdWidth").value);
        const h = Number(q("#sdHeight").value);
        if (h >= 120 && h <= 1400) { panel.style.height = Math.round(h) + "px"; panel.setAttribute("data-fixed-h", "1"); }
        else { panel.style.height = ""; panel.removeAttribute("data-fixed-h"); }
        labelSize(panel);
        queueFit();
    };
    const show = () => {
        const clean = cleanStyle(style, FONT_IDS());
        applyPanelStyle(panel, clean, theme, fontStackOf(clean.font));
        applyPanelTexts(panel, named ? Object.assign(cleanTexts(texts), { title: "" }) : cleanTexts(texts));
        if (named) { const heading = panel.querySelector(":scope > .panel-inner > .panel-head .panel-title"); if (heading) heading.textContent = q("#sdTitleText").value.trim() || labelOf(id); }
        q("#sdScaleText").textContent = Math.round((Number(q("#sdScale").value) || 1) * 100) + " %";
        q("#sdRadius").disabled = !q("#sdRadiusOwn").checked;
        const pick = (list, value) => (list.find((o) => o.id === (value || "")) || list[0]).hint;
        q("#sdMotionHint").textContent = `${pick(ANIMATIONS, style.anim)} ${pick(TRANSITIONS, style.trans)} ${pick(EDGES, style.edge)}`;
        paintColours();
        queueFit();
    };
    // colours: an empty one follows the page; the swatch shows what is on screen now
    const paintColours = () => {
        const mine = (style.colors && style.colors[theme]) || {};
        const css = getComputedStyle(panel);
        const toHex = (value) => { const v = String(value || "").trim(); return /^#[0-9a-f]{6}$/i.test(v) ? v : "#888888"; };
        const host = q("#sdColours");
        if (!host.childElementCount) {
            host.innerHTML = COLOUR_FIELDS.map((f) => `<label>${esc(f.label)} <input type="color" data-colour="${f.key}"> <button type="button" class="row-btn" data-colour-clear="${f.key}">Page colour</button></label>`).join("");
        }
        COLOUR_FIELDS.forEach((f) => {
            const input = host.querySelector(`[data-colour="${f.key}"]`);
            if (document.activeElement !== input) input.value = mine[f.key] || toHex(css.getPropertyValue(f.token));
            host.querySelector(`[data-colour-clear="${f.key}"]`).disabled = !mine[f.key];
        });
        const read = (key, token) => mine[key] || toHex(css.getPropertyValue(token));
        const textColour = read("text", "--text");
        const pairs = [["the panel", contrast(textColour, read("bg", "--surface"))], ["the cards", contrast(textColour, read("card", "--surface-2"))]];
        const weak = pairs.filter(([, ratio]) => ratio > 0 && ratio < 4.5);
        const note = q("#sdContrast");
        note.classList.toggle("warn", weak.length > 0);
        note.textContent = weak.length ? `Hard to read: the text on ${weak.map(([where, r]) => `${where} has a contrast of ${r} to 1`).join(" and ")}. Aim for at least 4.5 to 1.` : "The text is easy to read on these colours.";
    };

    let saved = false, dirty = false, asking = false;
    // unsaved changes: leaving the page asks the browser's own question; Escape, the backdrop and Cancel ask "Discard changes?"
    // (Escape, the backdrop or "Keep editing" in that question all mean: keep editing)
    const onLeave = (event) => { if (dirty && !saved) { event.preventDefault(); event.returnValue = ""; } };
    window.addEventListener("beforeunload", onLeave);
    overlay.addEventListener("input", () => { dirty = true; });
    overlay.addEventListener("change", () => { dirty = true; });
    const tryClose = async () => {
        if (asking) return;
        if (dirty) {
            asking = true;
            const discard = await askConfirm({ title: "Discard changes?", message: "What you changed on this panel is not saved yet.", confirmLabel: "Discard", cancelLabel: "Keep editing", danger: true });
            asking = false;
            if (!discard) return;
        }
        close();
    };
    const close = () => {
        window.removeEventListener("beforeunload", onLeave);
        document.removeEventListener("keydown", onKey, true);
        panel.classList.remove("style-target");
        overlay.remove();
        if (saved) return;
        // Cancel: what was there before comes back
        if (!isMember) {
            panel.style.setProperty("--w", before.w || "12");
            if (before.h) { panel.style.height = before.h; panel.setAttribute("data-fixed-h", "1"); } else { panel.style.height = ""; panel.removeAttribute("data-fixed-h"); }
            labelSize(panel);
        }
        paintPanelStyles();
        queueFit();
    };
    const onKey = (event) => { if (event.key === "Escape" && !asking) { event.stopPropagation(); tryClose(); } };
    document.addEventListener("keydown", onKey, true);
    overlay.addEventListener("mousedown", (event) => { if (event.target === overlay) tryClose(); });
    q("[data-close]").addEventListener("click", tryClose);

    const setStyle = (key, value) => { if (value === "" || value === undefined || value === null) delete style[key]; else style[key] = value; show(); };
    q("#sdWidth").addEventListener("change", sizeNow);
    q("#sdHeight").addEventListener("change", sizeNow);
    q("#sdOrient").addEventListener("change", (event) => setStyle("orient", event.target.value));
    q("#sdFont").addEventListener("change", (event) => setStyle("font", event.target.value));
    q("#sdScale").addEventListener("input", (event) => setStyle("scale", Number(event.target.value) === 1 ? "" : Number(event.target.value)));
    q("#sdRadiusOwn").addEventListener("change", (event) => setStyle("radius", event.target.checked ? Number(q("#sdRadius").value) : ""));
    q("#sdRadius").addEventListener("input", (event) => setStyle("radius", Number(event.target.value)));
    q("#sdAnim").addEventListener("change", (event) => { setStyle("anim", event.target.value); play(); });
    q("#sdTrans").addEventListener("change", (event) => setStyle("trans", event.target.value));
    q("#sdEdge").addEventListener("change", (event) => setStyle("edge", event.target.value));
    const play = () => {
        const frames = ANIMATION_FRAMES[style.anim || ""];
        if (!frames || window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
        panel.animate(frames, { duration: style.anim === "pop" ? 450 : 380, easing: "cubic-bezier(.2, .8, .2, 1)" });
    };
    q("#sdPlay").addEventListener("click", play);
    q("#sdColours").addEventListener("input", (event) => {
        const key = event.target.dataset && event.target.dataset.colour;
        if (!key) return;
        style.colors = style.colors || {}; style.colors[theme] = style.colors[theme] || {};
        style.colors[theme][key] = event.target.value;
        show();
    });
    q("#sdColours").addEventListener("click", (event) => {
        const key = event.target.dataset && event.target.dataset.colourClear;
        if (!key || !style.colors || !style.colors[theme]) return;
        delete style.colors[theme][key];
        show();
    });
    const textInput = () => {
        texts = { subtitle: q("#sdSubtitle").value, note: q("#sdNote").value, notePlace: q("#sdNotePlace").value, labels: {} };
        if (!named) texts.title = q("#sdTitleText").value;
        overlay.querySelectorAll("[data-label-index]").forEach((input) => { if (input.value.trim()) texts.labels[input.dataset.labelIndex] = input.value; });
        show();
    };
    ["#sdTitleText", "#sdSubtitle", "#sdNote"].forEach((selector) => q(selector).addEventListener("input", textInput));
    q("#sdNotePlace").addEventListener("change", textInput);
    overlay.querySelectorAll("[data-label-index]").forEach((input) => input.addEventListener("input", textInput));
    q("#sdReset").addEventListener("click", () => {
        dirty = true;
        style = {}; texts = {};
        overlay.querySelectorAll("#sdSubtitle, #sdNote, [data-label-index]").forEach((input) => { input.value = ""; });
        if (!named) q("#sdTitleText").value = "";
        ["#sdOrient", "#sdFont", "#sdAnim", "#sdTrans", "#sdEdge"].forEach((selector) => { q(selector).value = ""; });
        q("#sdScale").value = 1; q("#sdRadiusOwn").checked = false;
        show();
        toast("This panel has its page look again. Save keeps it; Cancel brings your settings back.", "info");
    });

    q("form").addEventListener("submit", (event) => {
        event.preventDefault();
        const cleanS = cleanStyle(style, FONT_IDS()), cleanT = cleanTexts(texts);
        if (Object.keys(cleanS).length) page.styles[id] = cleanS; else delete page.styles[id];
        if (Object.keys(cleanT).length) page.texts[id] = cleanT; else delete page.texts[id];
        if (named) {
            const target = widgetOf(page, id) || groupOf(page, id), name = cleanName(q("#sdTitleText").value, 40);
            if (target && name) target.title = name;
        }
        // only THIS panel's size is kept: the order of the panels is not touched. (capture() would write the order too, and a
        // page without a saved order keeps its default right-hand column; with one, that column falls into the flow.)
        if (!isMember) {
            const h = Number(q("#sdHeight").value);
            page.size[id] = { w: Math.min(12, Math.max(2, Number(q("#sdWidth").value) || 12)), h: h >= 120 && h <= 1400 ? Math.round(h) : 0 };
        }
        saved = true;
        close();
        commit("Saved. Undo (Layout & Style, or Ctrl+Z) takes it back.");
    });
    hydrate(overlay);
    show();
    q("#sdTitleText").focus();
}

// ---------------------------------------------------------------------------------------------
// The shared default: "Make this the default for everyone" and "Back to the built-in default"
// ---------------------------------------------------------------------------------------------
/** clear = false: the active layout becomes the default of the chosen pages; true: those pages get their built-in default back. */
async function openSharedDialog(clear) {
    const profile = activeProfile();
    if (!clear && !profile) { toast("Choose or make your own layout first: the default is made from it.", "info"); return; }
    const overlay = document.createElement("div");
    overlay.className = "modal-overlay";
    overlay.style.display = "flex";
    overlay.innerHTML = `
        <div class="modal-content" role="dialog" aria-modal="true" aria-labelledby="shTitle">
            <h3 id="shTitle">${clear ? "Back to the built-in default" : "Make this the default for everyone"}</h3>
            <p class="modal-intro">${clear
                ? "On the pages you tick, Default layout shows the page's own arrangement again, for every admin."
                : `Every admin who uses <b>Default layout</b> on the pages you tick sees "${esc(profile.name)}": its arrangement, panel styles and texts, font and colours. Their own layouts stay as they are.`}</p>
            <form class="modal-form" novalidate>
                <div class="checks" id="shPages">${SHARED_PAGES.map((p) => `<label class="check"><input type="checkbox" value="${esc(p.id)}"${p.id === pageId ? " checked" : ""}> <span>${esc(p.label)}</span></label>`).join("")}</div>
                <span class="field-hint">${clear ? "" : "A page you have not changed in this layout gets its built-in arrangement, with this layout's font and colours."}</span>
                <p class="form-error" id="shError" hidden></p>
                <div class="modal-actions">
                    <button type="button" class="cancel-btn" data-close>Cancel</button>
                    <button type="submit" class="save-btn${clear ? " danger" : ""}">${clear ? "Use the built-in default" : "Make it the default"}</button>
                </div>
            </form>
        </div>`;
    document.body.appendChild(overlay);
    const q = (selector) => overlay.querySelector(selector);
    let saving = false;                           // while the database is written, Escape and the backdrop do nothing
    const close = () => { document.removeEventListener("keydown", onKey, true); overlay.remove(); };
    const onKey = (event) => { if (event.key === "Escape") { event.stopPropagation(); if (!saving) close(); } };
    document.addEventListener("keydown", onKey, true);
    overlay.addEventListener("mousedown", (event) => { if (event.target === overlay && !saving) close(); });
    q("[data-close]").addEventListener("click", () => { if (!saving) close(); });
    q("form").addEventListener("submit", async (event) => {
        event.preventDefault();
        const pages = Array.from(overlay.querySelectorAll("#shPages input:checked")).map((input) => input.value).filter((id) => SHARED_PAGES.some((p) => p.id === id));
        const error = q("#shError");
        if (pages.length === 0) { error.textContent = "Tick at least one page."; error.hidden = false; return; }
        const button = q(".save-btn");
        button.disabled = true;
        saving = true; q("[data-close]").disabled = true;
        try {
            if (clear) await clearShared(pages);
            else {
                if (editing) capture();
                // through JSON: the database takes no "undefined" (a colour that was never set)
                const layoutOf = (p) => JSON.parse(JSON.stringify(cleanPage(Object.assign({}, profile.pages[p] || {}, { ver: POSITIONS_VERSION }))));
                await writeShared(pages, layoutOf, JSON.parse(JSON.stringify(profile.appearance || {})));
            }
            if (pages.includes(pageId)) setShared(clear ? null : { layout: cleanPage(Object.assign({}, profile ? profile.pages[pageId] || {} : {}, { ver: POSITIONS_VERSION })), appearance: profile ? profile.appearance : {} });
            close();
            applyLayout();
            toast(clear ? `Built-in default again on ${pages.length} page(s).` : `"${profile.name}" is now the default on ${pages.length} page(s), for every admin.`, "ok");
        } catch (failure) {
            saving = false; q("[data-close]").disabled = false;
            button.disabled = false;
            error.textContent = failure && failure.code === "permission-denied"
                ? "The database refused it. The rule for shared layouts (siteLayouts in firestore.rules) must be published in the Firebase console first."
                : "It was not saved: " + ((failure && failure.message) || "something went wrong") + ".";
            error.hidden = false;
        }
    });
    hydrate(overlay);
}

// ---------------------------------------------------------------------------------------------
// Moving and resizing panels
// A held panel floats above the page and sends out rings like a beacon; the others glide out of its way.
// ---------------------------------------------------------------------------------------------
function wireBoard() {
    const b = board();
    const scroller = document.querySelector("main.main-content");
    let drag = null, slot = null, frame = 0;

    b.addEventListener("click", (event) => {
        const menuButton = event.target.closest(".panel-menu-btn");
        if (menuButton) { openPanelMenu(menuButton.closest(".panel"), menuButton); return; }
        const foldBtn = event.target.closest(".panel-fold-btn");
        if (foldBtn) { hideTip(); toggleFold(foldBtn.closest(".panel").dataset.panel); return; }
        const tab = event.target.closest(".group-tab");
        if (!tab) return;
        const panel = tab.closest(".panel.group"), page = pageData(false), group = groupOf(page, panel.dataset.panel);
        if (!group) return;
        group.active = Number(tab.dataset.index) || 0;
        saveStore();
        paintGroup(panel, group);
    });
    b.addEventListener("dblclick", (event) => {
        if (!editing || !event.target.closest(".resize-y")) return;
        const panel = event.target.closest(".panel");
        panel.style.height = ""; panel.removeAttribute("data-fixed-h");
        labelSize(panel); capture();
    });

    const place = () => {
        const box = b.getBoundingClientRect();
        drag.panel.style.translate = `${drag.px - box.left - drag.grabX - drag.panel.offsetLeft}px ${drag.py - box.top - drag.grabY - drag.panel.offsetTop}px`;
    };
    const placeSlot = () => { if (slot) slot.style.cssText = `left:${drag.panel.offsetLeft}px;top:${drag.panel.offsetTop}px;width:${drag.panel.offsetWidth}px;height:${drag.panel.offsetHeight}px`; };
    const autoScroll = () => {
        if (!drag || !drag.started || drag.mode !== "move") return;
        const edge = 80, box = scroller.getBoundingClientRect();
        const push = drag.py < box.top + edge ? -1 : drag.py > box.bottom - edge ? 1 : 0;
        if (push) { scroller.scrollTop += push * 14; place(); }
        frame = window.requestAnimationFrame(autoScroll);
    };

    const move = (event) => {
        if (!drag) return;
        drag.px = event.clientX; drag.py = event.clientY;
        if (!drag.started) {
            if (Math.hypot(drag.px - drag.x, drag.py - drag.y) < 6) return;
            if (!activeProfile()) {                                  // the default layout does not move
                release();
                toast("The default layout is fixed. Make your own to move, resize or change panels.", "info", { label: "Make mine", run: () => newProfile() });
                return;
            }
            drag.started = true;
            document.body.classList.add("is-dragging");
            hideTip(); closeMenu();
            if (drag.mode === "move") {
                drag.panel.classList.remove("held");
                drag.panel.classList.add("floating");
                slot = document.createElement("div");
                slot.className = "drop-slot";
                b.appendChild(slot);
                placeSlot();
                frame = window.requestAnimationFrame(autoScroll);
            }
        }
        const panel = drag.panel, rect = panel.getBoundingClientRect();
        if (drag.mode === "x") {
            const gap = parseFloat(getComputedStyle(b).columnGap) || 16;
            const column = (b.clientWidth - parseFloat(getComputedStyle(b).paddingLeft) * 2 + gap) / 12;
            const span = Math.min(12, Math.max(2, Math.round((event.clientX - rect.left + gap) / column)));
            if (String(span) !== getComputedStyle(panel).getPropertyValue("--w").trim()) flip(topPanels().filter((other) => other !== panel), () => panel.style.setProperty("--w", String(span)));
            labelSize(panel);
            return;
        }
        if (drag.mode === "y") {
            panel.style.height = Math.min(1400, Math.max(120, Math.round((event.clientY - rect.top) / 10) * 10)) + "px";
            panel.setAttribute("data-fixed-h", "1");
            labelSize(panel);
            return;
        }
        place();
        if (Date.now() - drag.swapped < 300) return;                 // let the others finish gliding before the next change of place
        const under = document.elementsFromPoint(drag.px, drag.py).map((el) => (el.closest ? el.closest(".board > .panel") : null)).find((other) => other && other !== panel);
        if (!under) return;
        const box = under.getBoundingClientRect();
        const before = box.width > b.clientWidth * 0.7 ? drag.py < box.top + box.height / 2 : drag.px < box.left + box.width / 2;
        if (before ? under.previousElementSibling === panel : under.nextElementSibling === panel) return;      // it is there already
        flip(topPanels().filter((other) => other !== panel), () => b.insertBefore(panel, before ? under : under.nextElementSibling));
        drag.swapped = Date.now();
        place(); placeSlot();
    };

    const release = () => {
        window.removeEventListener("pointermove", move);
        window.removeEventListener("pointerup", release);
        window.removeEventListener("pointercancel", release);
        window.cancelAnimationFrame(frame);
        if (!drag) return;
        const done = drag;
        drag = null;
        done.panel.classList.remove("held");
        document.body.classList.remove("is-dragging");
        if (slot) { slot.remove(); slot = null; }
        if (!done.started) return;
        queueFit();                                   // packing waited while the panel was carried
        if (done.mode === "move") {
            // the panel settles into its place
            const panel = done.panel;
            panel.style.transition = "translate .3s cubic-bezier(.3, 1.3, .5, 1)";
            panel.style.translate = "0px 0px";
            window.setTimeout(() => { panel.classList.remove("floating"); panel.style.transition = ""; panel.style.translate = ""; }, 310);
        }
        capture();
    };

    b.addEventListener("pointerdown", (event) => {
        if (event.button > 0 || drag) return;
        const panel = event.target.closest(".board > .panel");
        if (!panel || event.target.closest(".panel-menu-btn, .panel-fold-btn")) return;
        let mode;
        if (editing) mode = event.target.closest(".resize-x") ? "x" : event.target.closest(".resize-y") ? "y" : "move";
        else {
            // outside "Arranging" a panel is moved by the free part of its title bar
            const head = event.target.closest(".panel-head");
            if (!head || head.closest(".panel") !== panel || event.target.closest("button, a, input, select, textarea, .pager")) return;
            mode = "move";
        }
        const rect = panel.getBoundingClientRect();
        drag = { panel, mode, x: event.clientX, y: event.clientY, px: event.clientX, py: event.clientY, grabX: event.clientX - rect.left, grabY: event.clientY - rect.top, started: false, swapped: 0 };
        if (editing) { event.preventDefault(); if (mode === "move") panel.classList.add("held"); }
        window.addEventListener("pointermove", move);
        window.addEventListener("pointerup", release);
        window.addEventListener("pointercancel", release);
    });
}

// ---------------------------------------------------------------------------------------------
// The Layout & Style view
// ---------------------------------------------------------------------------------------------
// Since 31 October 2026 these controls are not a window of their own: adminSettingsMenu.js owns the ONE floating window (fixed
// place and width) and shows this view in it when "Layout and style" is chosen, with a back arrow. Here only
// the controls are built (into #layoutHost) and kept painted. Arranging, "Panel" and Esc hide the window with "glowup:settings-close".
let colourTheme = "";                             // the theme whose colours the view edits ("" = the one on screen)
const closeWindow = () => document.dispatchEvent(new CustomEvent("glowup:settings-close"));

/** The value of a colour token for `theme`, read from the site's own style sheet (for the theme that is not on screen). */
function themeToken(theme, name) {
    try {
        for (const sheet of Array.from(document.styleSheets)) {
            let rules;
            try { rules = sheet.cssRules; } catch (error) { continue; }      // a sheet from another address (fonts)
            for (const rule of Array.from(rules || [])) {
                if (rule.selectorText && rule.selectorText.includes(`[data-theme="${theme}"]`)) {
                    const value = rule.style.getPropertyValue(name).trim();
                    if (value) return value;
                }
            }
        }
    } catch (error) { /* no style sheet to read */ }
    return "";
}

function buildDrawer() {
    const host = byId("layoutHost");
    if (!host) return;
    host.innerHTML = `
        <div class="drawer-section first">
            <h4>${icon("user")}Layout ${guideBadge("Layouts", "One sign-in, several people: pick your name to get your own arrangement, panels, font and colours. Up to " + MAX_PROFILES + " layouts are kept in this browser.", "Default layout is what every admin starts from: the page's own arrangement, or the one an admin made the default for everyone (... > Make this the default for everyone).")}</h4>
            <div class="drawer-row">
                <select id="profileSelect" aria-label="Layout"></select>
                <button type="button" class="icon-btn small" id="profileMenuBtn" aria-label="Layout options" aria-haspopup="menu">${icon("more")}</button>
            </div>
            <div class="drawer-actions">
                <button type="button" class="btn primary" id="arrangeBtn" data-tip-title="Arrange panels" data-tip="Grab any panel to move it, or drag its right or bottom edge to resize it." data-tip-preview="Outside arranging a panel still moves by its title bar.">${icon("grip")}<span>Arrange</span></button>
                <button type="button" class="btn" id="drawerAddPanel" data-tip-title="Add a panel" data-tip="Show the page's data once more as a list, a row, a grid or a graph, or bring a hidden panel back.">${icon("plus")}Panel</button>
            </div>
            <div class="drawer-actions">
                <button type="button" class="btn" id="autoResizeBtn" data-tip-title="Auto resize" data-tip="Every panel gets the height of its content again, and the gaps between panels are closed: a panel moves up into free space." data-tip-preview="A height you set by hand is dropped. The message that follows has an Undo.">${icon("layout")}Auto resize</button>
                <button type="button" class="btn" id="openAllBtn" data-tip-title="Folded panels" data-tip="A panel with nothing to show folds to its title by itself. This forgets the panels you folded or opened by hand on this page.">${icon("down")}Reset folds</button>
            </div>
            <div class="drawer-row" id="fitRow">
                <span>Keep the gaps closed ${guideBadge("Keep the gaps closed", "On: when a panel grows or shrinks, the others move up or make room, so no hole is left between panels.", "Off: every panel keeps its own row, as before.")}</span>
                <button type="button" class="switch" id="fitSwitch" role="switch" aria-checked="true" aria-label="Keep the gaps closed"><i></i></button>
            </div>
            <div class="drawer-actions">
                <button type="button" class="btn" id="stylePanelBtn" aria-haspopup="menu">${icon("palette")}Style a panel</button>
            </div>
            <div class="undo-row">
                <button type="button" class="btn" id="undoLayoutBtn" disabled>${icon("undo")}Undo</button>
                <button type="button" class="btn" id="redoLayoutBtn" disabled>${icon("redo")}Redo</button>
                <button type="button" class="btn" id="checkpointBtn" aria-haspopup="menu">${icon("save")}Checkpoints</button>
            </div>
            <p class="drawer-note">Undo and Redo (also Ctrl+Z and Ctrl+Y) take back any change of a layout. A <b>checkpoint</b> keeps every layout as it is now, to come back to later.</p>
            <p class="drawer-note">Every panel has an <b>arrow</b> at its top right that folds it to its title, and a <b>...</b> menu: show it as a list, grid or graph, combine it with another panel, or hide it. A panel with nothing to show folds by itself.</p>
        </div>

        <div class="drawer-section">
            <h4>${icon("type")}Text</h4>
            <div class="drawer-row"><span>Font</span><select id="lookFont">${FONTS.map((f) => `<option value="${f.id}">${esc(f.label)}</option>`).join("")}</select></div>
            <div class="drawer-row"><span>Text size</span><input type="range" id="lookScale" min="0.85" max="1.25" step="0.05"></div>
            <div class="drawer-row"><span>Spacing</span><select id="lookDensity"><option value="">Comfortable</option><option value="compact">Compact</option></select></div>
        </div>

        <div class="drawer-section">
            <h4>${icon("palette")}Colours <span class="small muted" id="lookThemeNote"></span></h4>
            <div class="segmented lw-theme-tabs" id="lookThemeTabs" role="tablist" aria-label="Colours of which mode">
                <button type="button" class="seg" role="tab" data-theme-tab="light">${icon("sun")}Light mode</button>
                <button type="button" class="seg" role="tab" data-theme-tab="dark">${icon("moon")}Dark mode</button>
            </div>
            <div class="drawer-row"><span>Accent (buttons)</span><input type="color" id="lookAccent"></div>
            <div class="drawer-row"><span>Panels</span><input type="color" id="lookPanel"></div>
            <div class="drawer-row"><span>Page background</span><input type="color" id="lookBg"></div>
            <div class="drawer-row"><span>Round corners</span><input type="range" id="lookRadius" min="0" max="24" step="1"></div>
        </div>

        <input type="file" id="importFile" accept="application/json,.json" hidden>`;
    hydrate(host);
    // the window shows this view: paint it with the current look, starting on the colours of the mode on screen
    document.addEventListener("glowup:layout-view", () => { colourTheme = currentTheme(); paintDrawer(); });

    byId("lookThemeTabs").addEventListener("click", (event) => {
        const tab = event.target.closest("[data-theme-tab]");
        if (!tab) return;
        colourTheme = tab.dataset.themeTab;
        paintDrawer();
    });
    byId("profileSelect").addEventListener("change", async (event) => {
        if (editing) await setEditing(false);
        store.active = event.target.value;
        saveStore();
        applyLayout();
    });
    byId("profileMenuBtn").addEventListener("click", (event) => openProfileMenu(event.currentTarget));
    byId("arrangeBtn").addEventListener("click", () => setEditing(!editing));
    byId("undoLayoutBtn").addEventListener("click", undoLayout);
    byId("stylePanelBtn").addEventListener("click", (event) => openMenu(event.currentTarget, [{ heading: "Size, cards, text, font, colours, motion of" }].concat(
        Array.from(document.querySelectorAll(".board .panel[data-panel]")).filter((panel) => !panel.classList.contains("is-hidden"))
            .map((panel) => ({ label: panel.dataset.label || panel.dataset.panel, icon: "palette", run: () => { closeWindow(); openStyleDialog(panel.dataset.panel); } })))));
    byId("redoLayoutBtn").addEventListener("click", redoLayout);
    byId("checkpointBtn").addEventListener("click", (event) => openCheckpointMenu(event.currentTarget));
    byId("drawerAddPanel").addEventListener("click", () => { closeWindow(); openPanelDialog(null); });
    byId("autoResizeBtn").addEventListener("click", autoResize);
    byId("fitSwitch").addEventListener("click", () => setFit(!fitOn()));
    byId("openAllBtn").addEventListener("click", () => { forgetFoldChoices(); paintDrawer(); toast("Every panel shows when it has something to show, and folds when it has not.", "ok"); });

    const look = async (change) => {
        if (!(await ensureProfile())) { paintDrawer(); return; }
        const profile = activeProfile();
        profile.appearance = profile.appearance || {};
        change(profile.appearance);
        saveStore();
        applyAppearance();
    };
    // the colours of the mode chosen with the Light mode / Dark mode tabs (at first the one on screen)
    const colour = (key) => (event) => { const value = event.target.value, theme = colourTheme || currentTheme(); look((a) => { a.colors = a.colors || {}; a.colors[theme] = a.colors[theme] || {}; a.colors[theme][key] = value; }); };
    byId("lookFont").addEventListener("change", (event) => { const value = event.target.value; look((a) => { a.font = value; }); });
    byId("lookScale").addEventListener("input", (event) => { const value = Number(event.target.value); look((a) => { a.scale = value; }); });
    byId("lookDensity").addEventListener("change", (event) => { const value = event.target.value; look((a) => { a.density = value; }); });
    byId("lookRadius").addEventListener("input", (event) => { const value = Number(event.target.value); look((a) => { a.radius = value; }); });
    byId("lookAccent").addEventListener("input", colour("accent"));
    byId("lookPanel").addEventListener("input", colour("panel"));
    byId("lookBg").addEventListener("input", colour("bg"));
    byId("importFile").addEventListener("change", importProfile);
    document.addEventListener("keydown", (event) => {
        if (event.key !== "Escape" || event.defaultPrevented) return;
        if (editing && !document.querySelector(".modal-overlay[style*='flex'], .menu")) setEditing(false);
    });
}

function openProfileMenu(anchor) {
    const profile = activeProfile();
    openMenu(anchor, [
        { label: "New layout", icon: "plus", description: "Under your name. It starts as a copy of what you see now.", disabled: store.profiles.length >= MAX_PROFILES, run: () => newProfile() },
        { label: "Rename", icon: "edit", disabled: !profile, run: async () => { const name = await askText({ title: "Name of this layout", label: "Name", value: profile.name }); if (name) { profile.name = cleanName(name); commit(); } } },
        {
            label: "Delete", icon: "trash", danger: true, disabled: !profile, run: async () => {
                if (!(await askConfirm({ title: `Delete the layout "${profile.name}"?`, message: "The default layout comes back. This cannot be undone.", confirmLabel: "Delete layout", danger: true }))) return;
                if (editing) { editing = false; document.body.classList.remove("layout-editing"); }
                store.profiles = store.profiles.filter((p) => p.id !== profile.id);
                store.active = "default";
                commit();
            }
        },
        { separator: true },
        { label: "Reset this page", icon: "reset", description: "Panels back to the default places. Font and colours stay.", disabled: !profile, run: () => { if (profile.pages) delete profile.pages[pageId]; commit(); } },
        { label: "Reset font and colours", icon: "reset", disabled: !profile, run: () => { profile.appearance = {}; commit(); } },
        { label: "Make this the default for everyone", icon: "monitor", description: "Every admin's Default layout on the pages you choose becomes this layout.", disabled: !profile, run: () => openSharedDialog(false) },
        { label: "Back to the built-in default", icon: "reset", description: "Default layout shows the page's own arrangement again.", disabled: !shared, run: () => openSharedDialog(true) },
        { label: "Show every helper", icon: "sparkle", description: "The helper lines that were hidden on this page come back.", disabled: !anyAssistHidden(), run: () => { showAllAssists(); toast("The helpers are back.", "ok"); } },
        { separator: true },
        { label: "Export", icon: "download", description: "Save this layout as a small file.", disabled: !profile, run: exportProfile },
        { label: "Import", icon: "upload", description: "Load a layout file from another computer.", disabled: store.profiles.length >= MAX_PROFILES, run: () => byId("importFile").click() }
    ]);
}

function exportProfile() {
    const profile = activeProfile();
    if (!profile) return;
    const file = new Blob([JSON.stringify({ glowupLayout: 1, profile }, null, 2)], { type: "application/json" });
    const link = document.createElement("a");
    link.href = URL.createObjectURL(file);
    link.download = "glowup-layout-" + profile.name.replace(/[^a-z0-9]+/gi, "-").toLowerCase() + ".json";
    link.click();
    window.setTimeout(() => URL.revokeObjectURL(link.href), 2000);
}

async function importProfile(event) {
    const file = event.target.files[0];
    event.target.value = "";
    if (!file) return;
    try {
        const data = JSON.parse(await file.text());
        if (!data || data.glowupLayout !== 1 || !data.profile || typeof data.profile !== "object") throw new Error("not a layout file");
        if (store.profiles.length >= MAX_PROFILES) { toast(`${MAX_PROFILES} layouts are saved already. Delete one to make room.`, "error"); return; }
        const profile = cleanProfile(data.profile, "p" + Date.now().toString(36));
        store.profiles.push(profile);
        store.active = profile.id;
        commit(`Layout "${profile.name}" imported.`);
    } catch (error) {
        toast("That file is not a GlowUp layout.", "error");
    }
}

function paintDrawer() {
    if (!byId("profileSelect")) return;
    const profile = activeProfile();
    byId("profileSelect").innerHTML = `<option value="default">${shared ? "Default layout (set for everyone)" : "Default layout (fixed)"}</option>` + store.profiles.map((p) => `<option value="${esc(p.id)}">${esc(p.name)}</option>`).join("");
    byId("profileSelect").value = store.active;
    byId("arrangeBtn").querySelector("span").textContent = editing ? "Done arranging" : "Arrange";
    byId("fitSwitch").setAttribute("aria-checked", String(fitOn()));
    byId("openAllBtn").disabled = !anyFoldChoice();
    paintUndo();
    const look = (profile && profile.appearance) || {};
    const css = getComputedStyle(document.documentElement);
    const toHex = (value) => { const v = value.trim(); return /^#[0-9a-f]{6}$/i.test(v) ? v : "#888888"; };
    byId("lookFont").value = look.font || "";
    byId("lookScale").value = look.scale || 1;
    byId("lookDensity").value = look.density || "";
    byId("lookRadius").value = look.radius !== undefined && look.radius !== "" ? look.radius : 14;
    // the colours of the tab that is chosen: the one on screen reads the page, the other one its kept choice or the site's own value
    const theme = colourTheme || currentTheme();
    const kept = (look.colors && look.colors[theme]) || {};
    const valueOf = (key, token) => theme === currentTheme() ? toHex(css.getPropertyValue(token)) : toHex(kept[key] || themeToken(theme, token));
    byId("lookAccent").value = valueOf("accent", "--accent");
    byId("lookPanel").value = valueOf("panel", "--surface");
    byId("lookBg").value = valueOf("bg", "--bg");
    byId("lookThemeNote").textContent = "for " + theme + " mode";
    document.querySelectorAll("#lookThemeTabs [data-theme-tab]").forEach((tab) => {
        const on = tab.dataset.themeTab === theme;
        tab.classList.toggle("on", on);
        tab.setAttribute("aria-selected", String(on));
    });
}
