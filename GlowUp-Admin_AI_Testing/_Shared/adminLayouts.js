



















import { icon, esc, hydrate, currentTheme, toast, openMenu, closeMenu, flip, askText, askConfirm, guideBadge, hideTip, playSweep } from "./adminShell.js";
import { datasetList, datasetSpec, measuresOf, mountWidget, unmountWidget, normalizeConfig, defaultTitle, VIEWS, CHARTS } from "./adminWidgets.js";
import { hasAssist, assistHidden, setAssistHidden, anyAssistHidden, showAllAssists } from "./adminAssist.js";
import { rowsFor, ROW_UNIT } from "./layoutRules.js";
import { setFoldPage, foldButton, FOLD_PARTS, paintFold, toggleFold, foldedNow, anyFoldChoice, forgetFoldChoices } from "./adminFold.js";
import { ANIMATIONS, ANIMATION_FRAMES, TRANSITIONS, EDGES, BUTTON_FX, SHADOWS, ORIENTS, LABEL_FONT_STYLES, LABEL_ALIGNS, readPixels, LIMITS, cleanStyle, cleanTexts, cleanPanelMap, contrast, applyPanelStyle, applyPanelTexts, ownTexts } from "./componentStyle.js";
import { SHARED_PAGES, cachedShared, readShared, writeShared, clearShared } from "./sharedLayouts.js";
import { signedInUid, readPersonal, writePersonal } from "./personalLayouts.js";
import { mergeLayouts } from "./layoutSync.js";

const LAYOUT_KEY = "glowup.layouts.v1";
// 17 Nov 2026: the layouts also live in the admin's own database copy (adminLayouts/{uid}), so every device gets them.
const SAVED_AT_KEY = "glowup.layouts.savedAt";        // when this browser last changed its layouts
const MET_KEY = "glowup.layouts.synced.";             // + uid: this browser has synced with that admin's copy before


const POSITIONS_VERSION = 7;
const MAX_PROFILES = 4;
const MAX_ADDED = 12;
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
let defaults = null;
let editing = false;




const ID = /^[a-z0-9_-]{1,40}$/i;
const cleanName = (text, length = 24) => String(text || "").replace(/\s+/g, " ").trim().slice(0, length);
const idList = (list, most = 40) => (Array.isArray(list) ? list.map(String).filter((id) => ID.test(id)).slice(0, most) : []);
const shortId = (value) => (ID.test(String(value || "")) ? String(value) : "");


function cleanView(view) {
    const v = view || {};
    return { source: shortId(v.source), view: shortId(v.view), graph: shortId(v.graph), by: shortId(v.by), measure: shortId(v.measure) };
}
function cleanPage(raw) {
    const page = raw && typeof raw === "object" ? raw : {};
    const size = {};
    Object.keys(page.size || {}).slice(0, 60).forEach((id) => { if (ID.test(id)) size[id] = { w: Math.min(12, Math.max(2, Number(page.size[id].w) || 12)), h: Math.min(1400, Math.max(0, Number(page.size[id].h) || 0)), wpx: Math.min(2400, Math.max(0, Number(page.size[id].wpx) || 0)) }; });
    const views = {};
    Object.keys(page.views || {}).slice(0, 30).forEach((id) => { if (ID.test(id)) views[id] = cleanView(page.views[id]); });
    return {
        ver: Math.max(0, Math.min(1000, Number(page.ver) || 0)),
        fit: page.fit !== false,
        order: idList(page.order, 60), size, hidden: idList(page.hidden, 60), views,
        widgets: (Array.isArray(page.widgets) ? page.widgets : []).slice(0, MAX_ADDED).filter((w) => w && ID.test(String(w.id)))
            .map((w) => Object.assign({ id: String(w.id), title: cleanName(w.title, 40) }, cleanView(w))),
        groups: (Array.isArray(page.groups) ? page.groups : []).slice(0, MAX_ADDED).filter((g) => g && ID.test(String(g.id)))
            .map((g) => ({ id: String(g.id), title: cleanName(g.title, 40), mode: GROUP_MODES.some((m) => m.id === g.mode) ? g.mode : "tabs", members: idList(g.members, 8), active: Math.max(0, Math.min(7, Number(g.active) || 0)) })),

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


function cleanStore(saved) {
    const clean = { active: "default", profiles: [] };
    if (saved && Array.isArray(saved.profiles))
        Object.assign(clean, { active: String(saved.active || "default"), profiles: saved.profiles.slice(0, MAX_PROFILES).filter((p) => p && ID.test(String(p.id))).map((p) => cleanProfile(p, String(p.id))) });
    if (!clean.profiles.some((p) => p.id === clean.active)) clean.active = "default";
    return clean;
}
function migratePositions(s) {
    let moved = false;
    s.profiles.forEach((profile) => Object.keys(profile.pages).forEach((key) => {
        const saved = profile.pages[key];
        if (saved.ver < POSITIONS_VERSION) { saved.order = []; saved.size = {}; saved.ver = POSITIONS_VERSION; moved = true; }
    }));
    return moved;
}
function loadStore() {
    try { store = cleanStore(JSON.parse(localStorage.getItem(LAYOUT_KEY) || "null")); } catch (error) { console.warn("Saved layouts could not be read: ", error); }
    if (migratePositions(store)) saveStore();
    lastSaved = JSON.stringify(store);
}

// ---- the admin's own copy in the database (every device) ----
let syncUid = "", pushTimer = 0, lastSync = 0;
const localSavedAt = () => { try { return Number(localStorage.getItem(SAVED_AT_KEY)) || 0; } catch (error) { return 0; } };
function stampLocal(at) { try { localStorage.setItem(SAVED_AT_KEY, String(at)); } catch (error) {                       } }
function schedulePush() {
    if (!syncUid) return;
    window.clearTimeout(pushTimer);
    pushTimer = window.setTimeout(pushNow, 1200);
}
function pushNow() {
    window.clearTimeout(pushTimer); pushTimer = 0;
    if (!syncUid) return;
    writePersonal(syncUid, JSON.stringify(store), localSavedAt() || Date.now()).catch((error) => console.warn("The layouts could not be saved to the database: ", error));
}
async function syncPersonal() {
    try {
        const uid = await signedInUid();
        if (!uid) return;
        lastSync = Date.now();
        const remote = await readPersonal(uid);
        syncUid = uid;
        let met = false;
        try { met = localStorage.getItem(MET_KEY + uid) === "1"; } catch (error) {                       }
        const theirs = remote ? cleanStore(remote.store) : null;
        if (theirs) migratePositions(theirs);
        const merged = mergeLayouts(store, localSavedAt(), theirs, remote ? remote.savedAt : 0, !met);
        if (merged.apply && !editing) {
            store = cleanStore(merged.store);
            lastSaved = JSON.stringify(store);
            try { localStorage.setItem(LAYOUT_KEY, lastSaved); } catch (error) {                       }
            stampLocal(merged.savedAt);
            applyLayout();
        } else if (merged.apply && editing) { stampLocal(Date.now()); merged.push = true; }     // being arranged right now: this browser's copy wins
        if (merged.push) { if (!localSavedAt()) stampLocal(Date.now()); pushNow(); }
        try { localStorage.setItem(MET_KEY + uid, "1"); } catch (error) {                       }
    } catch (error) { console.warn("The layouts in the database could not be read: ", error); }
}
if (typeof window !== "undefined") {
    // back on this tab after working on another device: look again (at most every 30 seconds)
    window.addEventListener("focus", () => { if (syncUid && Date.now() - lastSync > 30000) syncPersonal(); });
    window.addEventListener("pagehide", () => { if (pushTimer) pushNow(); });
}






const UNDO_MOST = 50, UNDO_MERGE_MS = 700;
const CHECKPOINT_KEY = "glowup.layouts.checkpoints.v1", CHECKPOINTS_MOST = 10;
const undoStack = [], redoStack = [];
let lastSaved = null, lastStep = 0;
function saveStore() {
    const now = JSON.stringify(store);
    if (now !== lastSaved) { stampLocal(Date.now()); schedulePush(); }
    if (lastSaved !== null && now !== lastSaved) {
        if (undoStack.length === 0 || Date.now() - lastStep > UNDO_MERGE_MS) { undoStack.push(lastSaved); if (undoStack.length > UNDO_MOST) undoStack.shift(); }
        lastStep = Date.now();
        redoStack.length = 0;
    }
    lastSaved = now;
    try { localStorage.setItem(LAYOUT_KEY, now); } catch (error) { console.warn("The layout could not be saved: ", error); }
    paintUndo();
}

function restoreStore(json, message) {
    if (editing) { editing = false; document.body.classList.remove("layout-editing"); }
    store = cleanStore(JSON.parse(json));
    lastSaved = JSON.stringify(store);
    lastStep = 0;
    try { localStorage.setItem(LAYOUT_KEY, lastSaved); } catch (error) {                       }
    stampLocal(Date.now()); schedulePush();
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
    if (keepCheckpoints(list)) toast(`Checkpoint "${cleanName(name, 40)}" saved. Layout & Style > Saves brings it back.`, "ok");
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

document.addEventListener("keydown", (event) => {
    if (!(event.ctrlKey || event.metaKey) || event.altKey || !board() || !defaults) return;
    const key = String(event.key || "").toLowerCase();
    if (key !== "z" && key !== "y") return;
    const target = event.target;
    if ((target && target.closest && target.closest("input, textarea, select, [contenteditable]")) || document.querySelector(".modal-overlay[style*='flex']")) return;
    event.preventDefault();
    if (key === "y" || event.shiftKey) redoLayout(); else undoLayout();
});




let shared = null;
function setShared(data) {
    shared = data ? { appearance: cleanProfile({ appearance: data.appearance }, "shared").appearance, page: cleanPage(Object.assign({}, data.layout, { ver: POSITIONS_VERSION })) } : null;
}

const activeProfile = () => store.profiles.find((p) => p.id === store.active) || null;
const board = () => document.querySelector(".board");
const topPanels = () => Array.from(document.querySelectorAll(".board > .panel[data-panel]"));
const panelOf = (id) => document.querySelector(`.board .panel[data-panel="${CSS.escape(id)}"]`);
const labelOf = (id) => { const panel = panelOf(id); return panel ? panel.dataset.label || id : id; };
const byId = (id) => document.getElementById(id);


function pageData(create) {
    const profile = activeProfile();
    if (!profile) return create ? null : (shared ? shared.page : null);
    profile.pages = profile.pages || {};
    if (!profile.pages[pageId]) { if (!create) return null; profile.pages[pageId] = cleanPage({ ver: POSITIONS_VERSION }); }
    return profile.pages[pageId];
}


async function ensureProfile() {
    if (activeProfile()) return true;
    return newProfile();
}

async function newProfile() {
    if (store.profiles.length >= MAX_PROFILES) { toast(`${MAX_PROFILES} layouts are saved already. Delete one to make room.`, "error"); return false; }
    const name = await askText({ title: "Your own layout", label: "Your name (the layout is saved under it)", placeholder: "For example Maria", confirmLabel: "Create" });
    if (!name) return false;

    const from = activeProfile() || (shared ? { appearance: shared.appearance, pages: { [pageId]: shared.page } } : null);
    const profile = cleanProfile(JSON.parse(JSON.stringify({ name, appearance: from ? from.appearance : {}, pages: from ? from.pages : {} })), "p" + Date.now().toString(36));
    store.profiles.push(profile);
    store.active = profile.id;
    saveStore();
    applyLayout();
    return true;
}




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



    const ready = () => document.documentElement.classList.add("board-ready");
    const waitsForShared = !kept.fresh && !activeProfile();
    if (!waitsForShared) ready(); else window.setTimeout(ready, 1500);
    if (!kept.fresh) readShared(pageId).then((data) => {
        const before = JSON.stringify(shared);
        setShared(data);
        if (JSON.stringify(shared) !== before && !activeProfile()) applyLayout(); else paintDrawer();
    }).catch((error) => console.warn("The shared default layout could not be read: ", error)).finally(ready);
    syncPersonal();                                          // layouts made on another device (17 Nov 2026)
    byId("addPanelBtn").addEventListener("click", () => openPanelDialog(null));
    byId("doneArrangingBtn").addEventListener("click", () => setEditing(false));
    wireBoard();
    watchForFit();
    window.setTimeout(() => board().classList.add("settled"), 900);
}


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


        const after = head.querySelector(":scope > .guide-badge") || head.querySelector(":scope > .panel-title");
        if (after) after.insertAdjacentHTML("afterend", FOLD_PARTS); else head.insertAdjacentHTML("afterbegin", FOLD_PARTS);


        const closers = (panel.hasAttribute("data-no-fold") ? "" : foldButton()) + menu;
        const tail = head.querySelector("[data-head-end]");


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




function applyLayout() {
    if (!defaults) return;
    const page = pageData(false);
    const b = board();


    b.querySelectorAll(".panel.group").forEach((group) => {
        group.querySelectorAll(":scope > .panel-inner > .group-body > .panel").forEach((member) => { member.classList.remove("member", "active-member"); b.appendChild(member); });
        group.remove();
    });

    const wanted = new Map((page ? page.widgets : []).map((widget) => [widget.id, widget]));
    b.querySelectorAll(".panel[data-custom]").forEach((panel) => { if (!wanted.has(panel.dataset.panel)) { unmountWidget(panel); panel.remove(); } });
    wanted.forEach((widget) => {
        let panel = panelOf(widget.id);
        if (!panel) { panel = makeDataPanel(widget); b.appendChild(panel); }
        setTitle(panel, widget.title || defaultTitle(normalizeConfig(widget)));
        mountWidget(panel, widget);
    });

    defaults.order.forEach((id) => {
        const panel = panelOf(id), view = page && page.views[id];
        if (view && view.view && defaults.source[id]) { panel.classList.add("alt-view"); mountWidget(panel, Object.assign({}, view, { source: defaults.source[id] })); }
        else { panel.classList.remove("alt-view"); unmountWidget(panel); }
    });

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

    const tops = new Map(topPanels().map((panel) => [panel.dataset.panel, panel]));
    const order = (page ? page.order : []).filter((id) => tops.has(id));
    defaults.order.forEach((id) => { if (tops.has(id) && !order.includes(id)) order.push(id); });
    tops.forEach((panel, id) => { if (!order.includes(id)) order.push(id); });
    order.forEach((id) => b.appendChild(tops.get(id)));
    tops.forEach((panel, id) => {
        const size = (page && page.size[id]) || defaults.size[id] || { w: 6 };
        panel.style.setProperty("--w", String(Math.min(12, Math.max(2, Number(size.w) || 6))));
        applyPixelSize(panel, size.wpx, size.h);
        panel.classList.toggle("is-hidden", !!(page && page.hidden.includes(id)));
        labelSize(panel);
    });


    b.classList.toggle("pinned", !page || page.order.length === 0);

    const profile = activeProfile();
    document.body.classList.toggle("own-layout", !!profile);
    applyAppearance();
    paintFold();
    paintDrawer();
    fitNow();
}





function fitOn() {
    const page = pageData(false);
    return !page || page.fit !== false;
}

let fitWatcher = null, fitQueued = 0;


// Two sides (22 Nov 2026). On a wide board a wide panel (7 to 12 columns) belongs to the left side and a narrow one (5 or
// fewer) to the right side, folded or not, so opening or closing a panel never moves it to the other side. A thin divider
// is drawn between the sides (CSS: .board.two-sides). Arranging mode and narrower boards keep the normal flow.
// (Replaces the New22 fold placement, which let a panel grow into a folded neighbour's columns and moved folded bars right.)
const SIDES_MIN_WIDTH = 1100;
function placeSides(panels) {
    const b = board();
    const wide = !document.body.classList.contains("layout-editing") && b.clientWidth >= SIDES_MIN_WIDTH;
    let mainW = 0, hasMain = false, hasSide = false;
    panels.forEach((panel) => {
        const w = Math.min(12, Math.max(1, Number(panel.style.getPropertyValue("--w")) || 12));
        const side = !wide || w === 6 || w === 12 ? "" : w >= 7 ? "main" : "side";
        if (side === "main" && panel.offsetParent !== null) { hasMain = true; mainW = Math.max(mainW, w); }
        if (side === "side" && panel.offsetParent !== null) hasSide = true;
        if ((panel.getAttribute("data-side") || "") !== side) { if (side) panel.setAttribute("data-side", side); else panel.removeAttribute("data-side"); }
    });
    const two = wide && hasMain && hasSide;
    if (b.classList.contains("two-sides") !== two) b.classList.toggle("two-sides", two);
    if (two) b.style.setProperty("--main-w", String(mainW)); else b.style.removeProperty("--main-w");
}

function fitNow() {
    const b = board();
    if (!b) return;
    const on = fitOn();
    const panels = topPanels();
    placeSides(panels);
    if (b.classList.contains("fit") !== on) b.classList.toggle("fit", on);
    if (on) {
        b.style.setProperty("--row-unit", ROW_UNIT + "px");
        const gap = parseFloat(getComputedStyle(b).columnGap) || 16;
        b.classList.add("measuring");
        const heights = panels.map((panel) => panel.offsetHeight);
        b.classList.remove("measuring");
        panels.forEach((panel, index) => {
            const rows = String(rowsFor(heights[index], gap));
            if (panel.style.getPropertyValue("--rows") !== rows) panel.style.setProperty("--rows", rows);
        });
    } else panels.forEach((panel) => panel.style.removeProperty("--rows"));
    if (fitWatcher) fitWatcher.takeRecords();
}

function queueFit() {
    if (fitQueued) return;
    fitQueued = window.requestAnimationFrame(() => { fitQueued = 0; fitNow(); });
}


function watchForFit() {
    const b = board();
    fitWatcher = new MutationObserver(() => {

        if (document.body.classList.contains("is-dragging") && b.querySelector(":scope > .panel.floating")) return;
        queueFit();
    });
    fitWatcher.observe(b, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ["class", "style", "hidden"] });
    window.addEventListener("resize", queueFit, { passive: true });


    if (window.ResizeObserver) {
        let lastWidth = b.clientWidth;
        new ResizeObserver(() => { if (b.clientWidth !== lastWidth) { lastWidth = b.clientWidth; queueFit(); } }).observe(b);
    }
    if (document.fonts && document.fonts.ready) document.fonts.ready.then(queueFit).catch(() => { });
    fitNow();
    watchForSettingsGrow();
}






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


    const later = settingsGrowNow;
    let wasOpen = document.documentElement.classList.contains("qs-open");
    new MutationObserver(() => {
        const open = document.documentElement.classList.contains("qs-open");
        if (open === wasOpen) return;
        wasOpen = open;

        if (!open) clearSettingsGrow();
    }).observe(document.documentElement, { attributes: true, attributeFilter: ["class"] });
    new MutationObserver(() => { if (document.body.classList.contains("layout-editing")) clearSettingsGrow(); else if (wasOpen) later(); })
        .observe(document.body, { attributes: true, attributeFilter: ["class"] });
    if (window.ResizeObserver) {
        let lastWidth = b.clientWidth;
        new ResizeObserver(() => { if (b.clientWidth !== lastWidth) { lastWidth = b.clientWidth; if (wasOpen) later(); } }).observe(b);
    }
}


async function setFit(on) {
    if (!(await ensureProfile())) { paintDrawer(); return; }
    capture();
    pageData(true).fit = !!on;
    commit(on ? "Gaps closed: panels move up into free space." : "Auto resize is off for this page: panels keep their own rows.");
}





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
    if (label) label.textContent = `${getComputedStyle(panel).getPropertyValue("--w").trim() || 12}/12` + (panel.hasAttribute("data-fixed-w") ? " · " + panel.style.getPropertyValue("--wpx") + " wide" : "") + (panel.style.height ? " · " + panel.style.height : "");
}

// Pixel size of a panel (0 = automatic). The width is a ceiling inside the panel's columns (CSS: width min(--wpx, 100%), wide screens only).
function applyPixelSize(panel, wpx, h) {
    if (wpx > 0) { panel.style.setProperty("--wpx", Math.round(wpx) + "px"); panel.setAttribute("data-fixed-w", "1"); }
    else { panel.style.removeProperty("--wpx"); panel.removeAttribute("data-fixed-w"); }
    if (h > 0) { panel.style.height = Math.round(h) + "px"; panel.setAttribute("data-fixed-h", "1"); }
    else { panel.style.height = ""; panel.removeAttribute("data-fixed-h"); }
}


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


function paintPanelStyles() {
    const b = board();
    if (!b || !defaults) return;
    const page = pageData(false), theme = currentTheme();
    b.querySelectorAll(".panel[data-panel]").forEach((panel) => {
        const id = panel.dataset.panel;
        const style = (page && page.styles && page.styles[id]) || {};
        applyPanelStyle(panel, style, theme, fontStackOf(style.font));
        let texts = (page && page.texts && page.texts[id]) || {};

        if (panel.dataset.custom || panel.classList.contains("group")) {
            const heading = panel.querySelector(":scope > .panel-inner > .panel-head .panel-title");
            if (heading) heading.dataset.ownText = heading.textContent = panel.dataset.label || heading.textContent;
            texts = Object.assign({}, texts, { title: "" });
        }
        applyPanelTexts(panel, texts);
    });
}


function capture() {
    const page = pageData(true);
    if (!page) return;
    page.order = topPanels().map((panel) => panel.dataset.panel);
    topPanels().forEach((panel) => { page.size[panel.dataset.panel] = { w: Number(getComputedStyle(panel).getPropertyValue("--w")) || 12, h: panel.style.height ? parseInt(panel.style.height, 10) : 0, wpx: panel.hasAttribute("data-fixed-w") ? parseInt(panel.style.getPropertyValue("--wpx"), 10) || 0 : 0 }; });
    saveStore();
}


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




const widgetOf = (page, id) => (page ? page.widgets.find((widget) => widget.id === id) : null);
const groupOf = (page, id) => (page ? page.groups.find((group) => group.id === id) : null);
const groupHolding = (page, id) => (page ? page.groups.find((group) => group.members.includes(id)) : null);


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
        page.order = page.order.map((x) => (x === targetId ? group.id : x)).filter((x) => x !== id);
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






const COLOUR_FIELDS = [
    { key: "bg", label: "Panel", token: "--surface" },
    { key: "card", label: "Cards inside", token: "--surface-2" },
    { key: "text", label: "Text", token: "--text" },
    { key: "accent", label: "Accent", token: "--accent" },
    { key: "border", label: "Outline", token: "--border" },
    { key: "shadow", label: "Shadow", token: "", def: "#000000" }
];

async function openStyleDialog(id) {
    if (!(await ensureProfile())) return;
    const panel = panelOf(id);
    if (!panel) return;
    if (editing) await setEditing(false);
    const page = pageData(true);
    page.styles = page.styles || {}; page.texts = page.texts || {};
    const theme = currentTheme();
    const before = { style: JSON.parse(JSON.stringify(page.styles[id] || {})), texts: JSON.parse(JSON.stringify(page.texts[id] || {})), w: getComputedStyle(panel).getPropertyValue("--w").trim(), h: panel.style.height, wpx: panel.hasAttribute("data-fixed-w") ? parseInt(panel.style.getPropertyValue("--wpx"), 10) || 0 : 0 };
    let style = JSON.parse(JSON.stringify(before.style)), texts = JSON.parse(JSON.stringify(before.texts));
    const named = !!panel.dataset.custom || panel.classList.contains("group");
    const own = ownTexts(panel);
    const hasCards = !!panel.querySelector(".kpi-row, .stat-row");
    const isMember = panel.classList.contains("member");
    const opt = (list, value) => list.map((o) => `<option value="${esc(o.id)}"${o.id === (value || "") ? " selected" : ""}>${esc(o.label)}</option>`).join("");
    // the column span is no longer chosen here (New17): the panel keeps the span it already has, so no saved layout moves
    const width = Math.min(12, Math.max(2, Number(before.w) || 12));
    // a long explanation sits behind a small "i" at the end of its title (tap, keyboard focus or hover), not under it
    const tip = (text, label) => `<span class="info-slot" data-info="${esc(text)}" data-info-label="${esc(label)}"></span>`;
    const head = (title, text) => `<div class="sd-section-head"><span class="form-section-title">${title}</span>${text ? tip(text, "About " + title.replace(/<[^>]*>/g, "")) : ""}</div>`;
    const matchingPanels = [...document.querySelectorAll(".board .panel[data-panel]")]
        .filter((p) => p !== panel && p.querySelector(".kpi-row, .stat-row") && ownTexts(p).labels.length > 0);

    const overlay = document.createElement("div");
    overlay.className = "modal-overlay style-overlay";
    overlay.style.display = "flex";
    overlay.innerHTML = `
        <div class="modal-content style-dialog" role="dialog" aria-modal="true" aria-labelledby="sdTitle">
            <div class="sd-section-head sd-title-head"><h3 id="sdTitle">Style and text: ${esc(labelOf(id))}</h3>${tip("Changes show on the panel at once. Nothing here moves a panel out of the page's grid, so panels never overlap or leave holes, on any screen.", "About this window")}</div>
            <form class="modal-form" novalidate>
                <div class="form-section"${isMember ? " hidden" : ""}>
                    ${head("Size", `Empty = automatic. A pixel width can only make the panel narrower than the room it has on the page, never wider, so panels never overlap. On a tablet a narrow panel takes half the width and on a phone every panel takes the whole width: pixel sizes apply on wide screens only. Height ${LIMITS.heightMin} to ${LIMITS.heightMax}, width ${LIMITS.widthMin} to ${LIMITS.widthMax} pixels; a panel taller than its content scrolls inside.`)}
                    <div class="form-row">
                        <div class="field"><label for="sdWidthPx">Width (pixels)</label><input type="number" id="sdWidthPx" inputmode="numeric" min="${LIMITS.widthMin}" max="${LIMITS.widthMax}" step="10" placeholder="Automatic" value="${before.wpx || ""}"><span class="field-error" id="sdWidthPxErr" role="alert" hidden></span></div>
                        <div class="field"><label for="sdHeight">Height (pixels)</label><input type="number" id="sdHeight" inputmode="numeric" min="${LIMITS.heightMin}" max="${LIMITS.heightMax}" step="10" placeholder="Automatic" value="${before.h ? parseInt(before.h, 10) : ""}"><span class="field-error" id="sdHeightErr" role="alert" hidden></span></div>
                    </div>
                </div>

                <div class="form-section">
                    ${head("Cards inside", hasCards ? "Side by side or one under the other. A panel is never turned at an angle: a turned panel would cover the ones next to it." : "This panel has no row of cards. To show its list as a row, grid or graph, use its ... menu > Show as.")}
                    <div class="field"><label for="sdOrient">Orientation</label><select id="sdOrient"${hasCards ? "" : " disabled"}>${opt(ORIENTS, style.orient)}</select></div>
                </div>

                <div class="form-section">
                    ${head("Text", "Only the text this panel already has can be changed here (its title and its card labels); nothing new is added to the panel. Names and numbers from the database are never changed.")}
                    <div class="field"${own.title === null ? " hidden" : ""}><label for="sdTitleText">Title</label><input type="text" id="sdTitleText" maxlength="${LIMITS.title}" autocomplete="off" placeholder="${esc(own.title || "")}" value="${esc(named ? labelOf(id) : texts.title || "")}">
                        <span class="field-hint">${named ? "This panel's own name." : "Empty = the page's own title."}</span></div>
                    <div class="field" id="sdLabelsField"${own.labels.length ? "" : " hidden"}><div class="sd-section-head"><span class="field-label">Card labels</span>${tip(`Empty text = the page's own label (shown greyed). Size ${LIMITS.labelSizeMin} to ${LIMITS.labelSizeMax} pixels; the label wraps inside its card and never overlaps. The look (size, style, align) can be shared: every card of this panel, and every other panel on this page that has a row of cards. The words of each label stay its own.`, "About card labels")}</div>
                        <label class="check"><input type="checkbox" id="sdSameLook" checked> <span>Same look on every card of this panel</span></label>
                        <label class="check"${matchingPanels.length ? "" : " hidden"}><input type="checkbox" id="sdMatchPanels"> <span>Also on the ${matchingPanels.length} other panel${matchingPanels.length === 1 ? "" : "s"} with cards on this page</span></label>
                        <div class="label-rows" id="sdLabels">${own.labels.slice(0, LIMITS.labels).map((label, i) => {
                            const look = (texts.labelStyles && texts.labelStyles[i]) || {};
                            return `<div class="label-row">
                                <div class="field"><label for="sdLabel${i}">Card ${i + 1}</label><input type="text" id="sdLabel${i}" data-label-index="${i}" maxlength="${LIMITS.label}" autocomplete="off" placeholder="${esc(label)}" value="${esc((texts.labels && texts.labels[i]) || "")}"></div>
                                <div class="label-look">
                                    <div class="field"><label for="sdLabelSize${i}">Size (px)</label><input type="number" id="sdLabelSize${i}" data-label-size="${i}" inputmode="numeric" min="${LIMITS.labelSizeMin}" max="${LIMITS.labelSizeMax}" step="1" placeholder="Auto" value="${look.size || ""}"></div>
                                    <div class="field"><label for="sdLabelStyle${i}">Style</label><select id="sdLabelStyle${i}" data-label-style="${i}">${opt(LABEL_FONT_STYLES, look.style)}</select></div>
                                    <div class="field"><label for="sdLabelAlign${i}">Align</label><select id="sdLabelAlign${i}" data-label-align="${i}">${opt(LABEL_ALIGNS, look.align)}</select></div>
                                </div>
                            </div>`;
                        }).join("")}</div></div>
                </div>

                <div class="form-section">
                    ${head("Font", "Text never gets smaller than 12 pixels, so it stays readable on phones.")}
                    <div class="form-row">
                        <div class="field"><label for="sdFont">Font</label><select id="sdFont"><option value="">Same as the page</option>${FONTS.filter((f) => f.id).map((f) => `<option value="${esc(f.id)}"${f.id === style.font ? " selected" : ""}>${esc(f.label)}</option>`).join("")}</select></div>
                        <div class="field"><label for="sdScale">Text size: <span id="sdScaleText"></span></label><input type="range" id="sdScale" min="${LIMITS.scaleMin}" max="${LIMITS.scaleMax}" step="0.05" value="${style.scale || 1}"></div>
                    </div>
                </div>

                <div class="form-section">
                    ${head(`Colours for ${esc(theme)} mode`, "Colours are kept for each mode on its own: switch to the other mode to set its colours.")}
                    <div class="colour-row" id="sdColours"></div>
                    <span class="field-hint contrast-note" id="sdContrast"></span>
                </div>

                <div class="form-section">
                    ${head("Shadow", "The shadow's colour is the \"Shadow\" colour above (black when you leave it on the page colour). It is kept for each mode on its own.")}
                    <div class="form-row">
                        <div class="field"><label for="sdShadow">Panel shadow</label><select id="sdShadow">${opt(SHADOWS, style.shadow)}</select></div>
                        <div class="field"><label for="sdCardShadow">Cards inside</label><select id="sdCardShadow">${opt(SHADOWS, style.cardShadow)}</select></div>
                    </div>
                </div>

                <div class="form-section">
                    <span class="form-section-title">Corners</span>
                    <div class="drawer-row"><label class="check"><input type="checkbox" id="sdRadiusOwn"${style.radius !== undefined ? " checked" : ""}> <span>Own roundness</span></label><input type="range" id="sdRadius" min="0" max="${LIMITS.radiusMax}" step="1" value="${style.radius !== undefined ? style.radius : 14}"></div>
                </div>

                <div class="form-section">
                    <span class="form-section-title">Motion</span>
                    <div class="form-row">
                        <div class="field"><label for="sdAnim">When the page opens</label><select id="sdAnim">${opt(ANIMATIONS, style.anim)}</select></div>
                        <div class="field"><label for="sdTrans">Transition of things inside</label><select id="sdTrans">${opt(TRANSITIONS, style.trans)}</select></div>
                    </div>
                    <button type="button" class="btn sd-play" id="sdPlay" aria-label="Play the animation">${icon("play")}Play the animation</button>
                    <div class="field"><label for="sdEdge">Edge light</label><select id="sdEdge">${opt(EDGES, style.edge)}</select></div>
                    <div class="field"><label for="sdBtn">Buttons when pressed</label><select id="sdBtn">${opt(BUTTON_FX, style.btn)}</select></div>
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

    const showError = (id, message) => { const box = q("#" + id); box.textContent = message; box.hidden = !message; };
    // Reads the two pixel boxes; a bad value is named under its own box and nothing is applied.
    const readSize = () => {
        const w = readPixels(q("#sdWidthPx").value, LIMITS.widthMin, LIMITS.widthMax, "The width");
        const h = readPixels(q("#sdHeight").value, LIMITS.heightMin, LIMITS.heightMax, "The height");
        showError("sdWidthPxErr", w.message); showError("sdHeightErr", h.message);
        q("#sdWidthPx").setAttribute("aria-invalid", w.ok ? "false" : "true"); q("#sdHeight").setAttribute("aria-invalid", h.ok ? "false" : "true");
        return { ok: w.ok && h.ok, w: w.value, h: h.value };
    };
    const sizeNow = () => {
        if (isMember) return;
        const size = readSize();
        if (size.ok) applyPixelSize(panel, size.w, size.h);
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
        q("#sdMotionHint").textContent = `${pick(ANIMATIONS, style.anim)} ${pick(TRANSITIONS, style.trans)} ${pick(EDGES, style.edge)} ${pick(BUTTON_FX, style.btn)}`;
        paintColours();
        queueFit();
    };

    const paintColours = () => {
        const mine = (style.colors && style.colors[theme]) || {};
        const css = getComputedStyle(panel);
        const toHex = (value, def) => { const v = String(value || "").trim(); return /^#[0-9a-f]{6}$/i.test(v) ? v : (def || "#888888"); };
        const host = q("#sdColours");
        if (!host.childElementCount) {
            host.innerHTML = COLOUR_FIELDS.map((f) => `<label>${esc(f.label)} <input type="color" data-colour="${f.key}"> <button type="button" class="row-btn" data-colour-clear="${f.key}">Page colour</button></label>`).join("");
        }
        COLOUR_FIELDS.forEach((f) => {
            const input = host.querySelector(`[data-colour="${f.key}"]`);
            if (document.activeElement !== input) input.value = mine[f.key] || toHex(f.token ? css.getPropertyValue(f.token) : "", f.def);
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


    const onLeave = (event) => { if (window.__glowupLeaving) return; if (dirty && !saved) { event.preventDefault(); event.returnValue = ""; } };
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
        if (window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches) overlay.remove();
        else { overlay.classList.add("closing"); setTimeout(() => overlay.remove(), 200); }
        if (saved) return;

        if (!isMember) {
            panel.style.setProperty("--w", before.w || "12");
            applyPixelSize(panel, before.wpx, before.h ? parseInt(before.h, 10) : 0);
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
    q("#sdHeight").addEventListener("change", sizeNow);
    q("#sdWidthPx").addEventListener("change", sizeNow);
    q("#sdOrient").addEventListener("change", (event) => setStyle("orient", event.target.value));
    q("#sdFont").addEventListener("change", (event) => setStyle("font", event.target.value));
    q("#sdScale").addEventListener("input", (event) => setStyle("scale", Number(event.target.value) === 1 ? "" : Number(event.target.value)));
    q("#sdRadiusOwn").addEventListener("change", (event) => setStyle("radius", event.target.checked ? Number(q("#sdRadius").value) : ""));
    q("#sdRadius").addEventListener("input", (event) => setStyle("radius", Number(event.target.value)));
    q("#sdAnim").addEventListener("change", (event) => { setStyle("anim", event.target.value); play(); });
    q("#sdTrans").addEventListener("change", (event) => setStyle("trans", event.target.value));
    q("#sdEdge").addEventListener("change", (event) => setStyle("edge", event.target.value));
    q("#sdBtn").addEventListener("change", (event) => { setStyle("btn", event.target.value); const b = panel.querySelector(".fx:not(:disabled)"); if (b) playSweep(b); });
    q("#sdShadow").addEventListener("change", (event) => setStyle("shadow", event.target.value));
    q("#sdCardShadow").addEventListener("change", (event) => setStyle("cardShadow", event.target.value));
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
    // Typing or changing a label's look repaints on the next animation frame, and only the labels whose values changed are touched.
    let textFrame = 0;
    const collectTexts = () => {
        texts = { labels: {}, labelStyles: {} };
        if (!named) texts.title = q("#sdTitleText").value;
        overlay.querySelectorAll("[data-label-index]").forEach((input) => { if (input.value.trim()) texts.labels[input.dataset.labelIndex] = input.value; });
        overlay.querySelectorAll(".label-row").forEach((row, i) => {
            texts.labelStyles[i] = { size: row.querySelector("[data-label-size]").value, style: row.querySelector("[data-label-style]").value, align: row.querySelector("[data-label-align]").value };
        });
    };
    const textInput = () => {
        collectTexts();
        if (textFrame) return;
        textFrame = requestAnimationFrame(() => { textFrame = 0; show(); });
    };
    q("#sdTitleText").addEventListener("input", textInput);
    overlay.querySelectorAll("[data-label-index]").forEach((input) => { input.addEventListener("input", textInput); });
    // a card's look (size, style, align) is copied to every card of this panel while "Same look on every card" is on
    const LOOK_KEYS = ["data-label-size", "data-label-style", "data-label-align"];
    const shareLook = (event) => {
        const box = event.target;
        if (q("#sdSameLook") && q("#sdSameLook").checked) {
            const key = LOOK_KEYS.find((k) => box.hasAttribute(k));
            overlay.querySelectorAll(`[${key}]`).forEach((other) => { if (other !== box) other.value = box.value; });
        }
        textInput();
    };
    overlay.querySelectorAll("[data-label-size], [data-label-style], [data-label-align]").forEach((input) => { input.addEventListener("input", shareLook); input.addEventListener("change", shareLook); });
    q("#sdReset").addEventListener("click", () => {
        dirty = true;
        style = {}; texts = {};
        overlay.querySelectorAll("[data-label-index], [data-label-size]").forEach((input) => { input.value = ""; });
        overlay.querySelectorAll("[data-label-style], [data-label-align]").forEach((input) => { input.value = ""; });
        if (!named) q("#sdTitleText").value = "";
        ["#sdOrient", "#sdFont", "#sdAnim", "#sdTrans", "#sdEdge", "#sdBtn", "#sdShadow", "#sdCardShadow"].forEach((selector) => { q(selector).value = ""; });
        q("#sdScale").value = 1; q("#sdRadiusOwn").checked = false;
        show();
        toast("This panel has its page look again. Save keeps it; Cancel brings your settings back.", "info");
    });

    q("form").addEventListener("submit", (event) => {
        event.preventDefault();
        const size = isMember ? { ok: true, w: 0, h: 0 } : readSize();
        if (!size.ok) { toast("Fix the size first: the box that needs correcting is marked under it.", "error"); (q("#sdWidthPx[aria-invalid=true]") || q("#sdHeight[aria-invalid=true]")).focus(); return; }
        const cleanS = cleanStyle(style, FONT_IDS()), cleanT = cleanTexts(texts);
        if (Object.keys(cleanS).length) page.styles[id] = cleanS; else delete page.styles[id];
        if (Object.keys(cleanT).length) page.texts[id] = cleanT; else delete page.texts[id];
        if (named) {
            const target = widgetOf(page, id) || groupOf(page, id), name = cleanName(q("#sdTitleText").value, 40);
            if (target && name) target.title = name;
        }


        if (!isMember) {
            page.size[id] = { w: width, h: size.h, wpx: size.w };
        }
        // "Also on the other panels with cards": the first card's look goes to every card of each matching panel (their words stay theirs)
        const matchBox = q("#sdMatchPanels");
        if (matchBox && matchBox.checked && cleanT.labelStyles) {
            const look = cleanT.labelStyles["0"] || Object.values(cleanT.labelStyles)[0];
            matchingPanels.forEach((other) => {
                const otherId = other.dataset.panel, count = Math.min(ownTexts(other).labels.length, LIMITS.labels);
                const labelStyles = {};
                for (let i = 0; i < count; i++) labelStyles[i] = Object.assign({}, look);
                const merged = cleanTexts(Object.assign({}, page.texts[otherId] || {}, { labelStyles }));
                if (Object.keys(merged).length) page.texts[otherId] = merged; else delete page.texts[otherId];
            });
        }
        saved = true;
        close();
        commit("Saved. Undo (Layout & Style, or Ctrl+Z) takes it back.");
    });
    hydrate(overlay);
    show();
    q("#sdTitleText").focus();
}





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
    let saving = false;
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
            if (!activeProfile()) {
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
        if (Date.now() - drag.swapped < 300) return;
        const under = document.elementsFromPoint(drag.px, drag.py).map((el) => (el.closest ? el.closest(".board > .panel") : null)).find((other) => other && other !== panel);
        if (!under) return;
        const box = under.getBoundingClientRect();
        const before = box.width > b.clientWidth * 0.7 ? drag.py < box.top + box.height / 2 : drag.px < box.left + box.width / 2;
        if (before ? under.previousElementSibling === panel : under.nextElementSibling === panel) return;
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
        queueFit();
        if (done.mode === "move") {

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







let colourTheme = "";
const closeWindow = () => document.dispatchEvent(new CustomEvent("glowup:settings-close"));


function themeToken(theme, name) {
    try {
        for (const sheet of Array.from(document.styleSheets)) {
            let rules;
            try { rules = sheet.cssRules; } catch (error) { continue; }
            for (const rule of Array.from(rules || [])) {
                if (rule.selectorText && rule.selectorText.includes(`[data-theme="${theme}"]`)) {
                    const value = rule.style.getPropertyValue(name).trim();
                    if (value) return value;
                }
            }
        }
    } catch (error) {                              }
    return "";
}

function buildDrawer() {
    const host = byId("layoutHost");
    if (!host) return;
    host.innerHTML = `
        <div class="drawer-section first">
            <h4>${icon("user")}Layout ${guideBadge("Layouts", "One sign-in, several people: pick your name to get your own arrangement, panels, font and colours. Up to " + MAX_PROFILES + " layouts are kept with your account, so they show on every device you sign in on.", "Default layout is what every admin starts from: the page's own arrangement, or the one an admin made the default for everyone (... > Make this the default for everyone).")}</h4>
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
                <button type="button" class="btn" id="checkpointBtn" aria-haspopup="menu">${icon("save")}Saves</button>
            </div>
            <p class="drawer-note">Undo and Redo (also Ctrl+Z and Ctrl+Y) take back any change of a layout. <b>Saves</b> keeps every layout as it is now (a checkpoint), to come back to later.</p>
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
    if (file.size > 1024 * 1024) { toast("A layout file is at most 1 MB. This one is not a GlowUp layout.", "error"); return; }
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
