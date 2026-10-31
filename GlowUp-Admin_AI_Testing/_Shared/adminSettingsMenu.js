// THE SETTINGS WINDOW behind the GlowUp logo at the top of the sidebar, on every admin page.
//
// The logo button shows only the "G" and the red Recycle bin count. One tap opens ONE floating window, a second tap on the
// logo hides it again; Esc and the X in its title bar close it too. A click anywhere else does NOT close it and there is no
// dark cover, so the page stays usable while it is open. It always starts closed on a new page.
//
// Two views in the same window (never a second window):
//   Settings         - Layout and style, Recycle bin (red count), Archive, Appearance (Light / Dark / System), the signed-in
//                      e-mail, Sign Out.
//   Layout and style - the existing Layout & Style controls (adminLayouts.js builds them into #layoutHost); a back arrow in the
//                      title bar returns to Settings.
// It is dragged by its title bar (or the arrow keys), resized from its lower right corner (never smaller than WINDOW_MIN), kept
// inside the browser window, and remembers where it was and how big ("glowup.settingsWindow.v1" in this browser, in a
// try/catch). "Reset position" in the title bar puts it back where it first appears.
// adminLayouts.js talks to it with two events: "glowup:settings-close" (arranging hides the window) and "glowup:layout-view"
// (the Layout and style view was shown: paint it).
//
// THE RECYCLE BIN COUNT is read-only and uses the existing data code (adminStore.js): first the lists this browser keeps
// (no read at all); only a list that is not kept is counted with one count query (binned == true, the same query the
// Dashboard already makes), remembered for 5 minutes in this tab.
import { auth, db } from "../_Database/firebase-console.js";
import { onAuthStateChanged } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-auth.js";
import { collection, query, where } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { keptBinnedCount, getCountFromServer, onListsRefreshed } from "./adminStore.js";
import { themeChoice, setThemeChoice, icon, toast, hydrate } from "./adminShell.js";

export const BIN_PAGE = "../_Admin_Recycle_Bin/adminRecycleBin.html";
export const ARCHIVE_PAGE = "../_Admin_Archive/adminArchive.html";
const BIN_LISTS = ["ingredients", "brands", "products"];
const COUNT_KEEP_MS = 5 * 60 * 1000;
const COUNT_KEY = "glowup.binCount.";
const WINDOW_KEY = "glowup.settingsWindow.v1";
const WINDOW_MIN = { w: 300, h: 260 };
const WINDOW_GAP = 8;

let binCount = 0;
let recountTimer = 0;
let win = null, button = null;
let view = "menu";                                // "menu" | "layout"
let state = null;                                 // { left, top, width, height }; height 0 = as tall as the content (up to the window)

const el = (id) => document.getElementById(id);
const isOpen = () => !!win && win.classList.contains("is-open");

// ---------------------------------------------------------------------------------------------
// Place and size
// ---------------------------------------------------------------------------------------------
function windowDefaults() {
    const width = Math.min(360, window.innerWidth - 2 * WINDOW_GAP);
    return { left: window.innerWidth - width - 24, top: 76, width, height: 0 };
}
function readState() {
    try {
        const saved = JSON.parse(localStorage.getItem(WINDOW_KEY) || "null");
        if (saved && [saved.left, saved.top, saved.width].every((n) => Number.isFinite(Number(n))))
            return { left: Number(saved.left), top: Number(saved.top), width: Number(saved.width), height: Number(saved.height) || 0 };
    } catch (error) { /* a private window, or a value that cannot be read */ }
    return windowDefaults();
}
function saveState() { try { localStorage.setItem(WINDOW_KEY, JSON.stringify(state)); } catch (error) { /* not kept */ } }

/** Puts the window where `next` says, inside the browser window and never smaller than its minimum. Returns what it used. */
function place(next) {
    const maxW = Math.max(WINDOW_MIN.w, window.innerWidth - 2 * WINDOW_GAP), maxH = Math.max(WINDOW_MIN.h, window.innerHeight - 2 * WINDOW_GAP);
    const s = Object.assign({}, next);
    s.width = Math.min(maxW, Math.max(WINDOW_MIN.w, s.width));
    s.height = s.height ? Math.min(maxH, Math.max(WINDOW_MIN.h, s.height)) : 0;
    win.style.width = s.width + "px";
    win.style.height = s.height ? s.height + "px" : "";
    const shownH = win.offsetHeight || s.height || WINDOW_MIN.h;
    s.left = Math.min(Math.max(WINDOW_GAP, s.left), Math.max(WINDOW_GAP, window.innerWidth - s.width - WINDOW_GAP));
    s.top = Math.min(Math.max(WINDOW_GAP, s.top), Math.max(WINDOW_GAP, window.innerHeight - shownH - WINDOW_GAP));
    win.style.left = s.left + "px";
    win.style.top = s.top + "px";
    return s;
}

/** Dragging the title bar moves the window; dragging the corner resizes it. Listens on window (no pointer capture), like the rest of the site. */
function wireWindow() {
    const follow = (event, onMove) => {
        event.preventDefault();
        win.classList.add("moving");
        const move = (e) => { state = place(onMove(e)); };
        const stop = () => {
            win.classList.remove("moving");
            window.removeEventListener("pointermove", move); window.removeEventListener("pointerup", stop); window.removeEventListener("pointercancel", stop);
            saveState();
        };
        window.addEventListener("pointermove", move); window.addEventListener("pointerup", stop); window.addEventListener("pointercancel", stop);
    };
    const head = win.querySelector(".sw-head");
    head.addEventListener("pointerdown", (event) => {
        if (event.button !== 0 || event.target.closest("button")) return;
        const box = win.getBoundingClientRect(), dx = event.clientX - box.left, dy = event.clientY - box.top;
        follow(event, (e) => Object.assign({}, state, { left: e.clientX - dx, top: e.clientY - dy }));
    });
    head.addEventListener("keydown", (event) => {
        const step = event.shiftKey ? 60 : 20;
        const to = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] }[event.key];
        if (!to || event.target !== head) return;
        event.preventDefault();
        state = place(Object.assign({}, state, { left: state.left + to[0], top: state.top + to[1] }));
        saveState();
    });
    win.querySelector(".sw-resize").addEventListener("pointerdown", (event) => {
        if (event.button !== 0) return;
        const start = { x: event.clientX, y: event.clientY, w: win.offsetWidth, h: win.offsetHeight };
        follow(event, (e) => Object.assign({}, state, { width: start.w + e.clientX - start.x, height: start.h + e.clientY - start.y }));
    });
    el("swReset").addEventListener("click", () => {
        try { localStorage.removeItem(WINDOW_KEY); } catch (error) { /* nothing kept */ }
        state = place(windowDefaults());
        toast("The window is back where it first appears.", "ok");
    });
    window.addEventListener("resize", () => { if (isOpen()) state = place(state); });
}

// ---------------------------------------------------------------------------------------------
// Open, close, views
// ---------------------------------------------------------------------------------------------
function show(next) {
    view = next;
    const layout = view === "layout" && !!el("layoutHost") && el("layoutHost").childElementCount > 0;
    if (view === "layout" && !layout) view = "menu";
    el("swMenuView").hidden = view !== "menu";
    el("layoutHost").hidden = view !== "layout";
    el("swBack").hidden = view !== "layout";
    el("swTitle").textContent = view === "layout" ? "Layout & Style" : "Settings";
    win.setAttribute("aria-label", el("swTitle").textContent);
    if (view === "menu") renderMenu();
    else document.dispatchEvent(new CustomEvent("glowup:layout-view"));
    state = place(state);
}

function openWindow() {
    if (isOpen()) return;
    win.classList.add("is-open");
    button.setAttribute("aria-expanded", "true");
    show("menu");
    win.focus({ preventScroll: true });
}

export function closeSettingsWindow() {
    if (!isOpen()) return;
    const hadFocus = win.contains(document.activeElement);
    win.classList.remove("is-open");
    button.setAttribute("aria-expanded", "false");
    if (hadFocus) button.focus({ preventScroll: true });
}

// ---------------------------------------------------------------------------------------------
// The Settings view
// ---------------------------------------------------------------------------------------------
function item({ label, description, iconName, danger, checked, badge, run }) {
    const row = document.createElement("button");
    row.type = "button";
    row.className = "menu-item" + (danger ? " danger" : "") + (checked ? " checked" : "");
    row.innerHTML = iconName ? icon(iconName) : `<span class="menu-gap"></span>`;
    const text = document.createElement("span");
    text.className = "menu-text";
    const name = document.createElement("span");
    name.textContent = label;
    text.appendChild(name);
    if (description) { const small = document.createElement("small"); small.textContent = description; text.appendChild(small); }
    row.appendChild(text);
    if (badge) { const count = document.createElement("span"); count.className = "menu-badge"; count.textContent = badge; row.appendChild(count); }
    if (checked) row.insertAdjacentHTML("beforeend", icon("check", "menu-check"));
    row.addEventListener("click", run);
    return row;
}
const heading = (text) => { const h = document.createElement("div"); h.className = "menu-heading"; h.textContent = text; return h; };
const separator = () => { const s = document.createElement("div"); s.className = "menu-sep"; return s; };

function renderMenu() {
    const host = el("swMenuView");
    if (!host) return;
    const choice = themeChoice(), user = auth.currentUser, here = location.pathname;
    const go = (address) => () => { window.location.href = address; };
    const layoutHere = !!el("layoutHost") && el("layoutHost").childElementCount > 0;
    const note = document.createElement("div");
    note.className = "menu-note";
    note.title = "Signed in as";
    note.textContent = user && user.email ? user.email : "Signed in";
    host.replaceChildren(
        ...(layoutHere ? [item({ label: "Layout and style", iconName: "sliders", description: "Arrange the panels, fonts and colours.", run: () => show("layout") })] : []),
        item({ label: "Recycle bin", iconName: "trash", checked: here.includes("Recycle_Bin"), badge: binCount > 0 ? (binCount > 99 ? "99+" : String(binCount)) : "", description: binCount > 0 ? `${binCount} ${binCount === 1 ? "entry" : "entries"} removed` : "Nothing removed", run: go(BIN_PAGE) }),
        item({ label: "Archive", iconName: "archive", checked: here.includes("_Admin_Archive"), description: "Kept 30 days, then deleted.", run: go(ARCHIVE_PAGE) }),
        separator(),
        heading("Appearance"),
        item({ label: "Light", iconName: "sun", checked: choice === "light", run: () => { setThemeChoice("light"); renderMenu(); } }),
        item({ label: "Dark", iconName: "moon", checked: choice === "dark", run: () => { setThemeChoice("dark"); renderMenu(); } }),
        item({ label: "System", iconName: "monitor", checked: choice === "system", description: "Follows this computer's setting.", run: () => { setThemeChoice("system"); renderMenu(); } }),
        separator(),
        note,
        item({ label: "Sign Out", iconName: "logout", danger: true, run: () => document.dispatchEvent(new CustomEvent("glowup:sign-out")) })
    );
}

// ---------------------------------------------------------------------------------------------
// Start
// ---------------------------------------------------------------------------------------------
/** Builds the window (closed) and wires the logo button (once). Called by adminShell.mountShell on every admin page, before adminLayouts fills #layoutHost. */
export function mountSettingsMenu() {
    button = el("brandMenuBtn");
    if (!button || button.dataset.wired === "1") return;
    button.dataset.wired = "1";

    win = document.createElement("aside");
    win.className = "settings-window";
    win.id = "settingsWindow";
    win.setAttribute("role", "dialog");
    win.setAttribute("aria-label", "Settings");
    win.tabIndex = -1;
    win.innerHTML = `
        <div class="sw-head" tabindex="0" aria-label="Settings window. Drag it, or move it with the arrow keys.">
            <button type="button" class="icon-btn small" id="swBack" aria-label="Back to settings" data-tip="Back to settings" hidden></button>
            <h3 id="swTitle">Settings</h3>
            <button type="button" class="icon-btn small" id="swReset" aria-label="Reset position" data-tip="Reset position"></button>
            <button type="button" class="icon-btn small" id="swClose" aria-label="Close" data-tip="Close"></button>
        </div>
        <div class="sw-body">
            <div class="sw-view" id="swMenuView"></div>
            <div class="sw-view layout-view" id="layoutHost" hidden></div>
        </div>
        <span class="sw-resize" aria-hidden="true"></span>`;
    document.body.appendChild(win);
    win.querySelector("#swBack").innerHTML = icon("left");
    win.querySelector("#swReset").innerHTML = icon("reset");
    win.querySelector("#swClose").innerHTML = icon("x");
    state = place(readState());
    wireWindow();

    button.addEventListener("click", () => { if (isOpen()) closeSettingsWindow(); else openWindow(); });
    el("swClose").addEventListener("click", closeSettingsWindow);
    el("swBack").addEventListener("click", () => show("menu"));
    document.addEventListener("glowup:settings-close", closeSettingsWindow);
    // Esc closes it, unless a dialog, pop-up or menu has taken that key already (they stop it before it gets here)
    document.addEventListener("keydown", (event) => {
        if (event.key !== "Escape" || event.defaultPrevented || !isOpen()) return;
        if (document.querySelector(".modal-overlay[style*='flex'], .menu.show, .facet-panel")) return;
        closeSettingsWindow();
    });

    document.addEventListener("glowup:bin-count", queueRecount);
    document.addEventListener("glowup:theme", () => { if (isOpen() && view === "menu") renderMenu(); });
    onListsRefreshed(queueRecount);
    onAuthStateChanged(auth, (user) => { if (user) { queueRecount(); if (isOpen() && view === "menu") renderMenu(); } });
    hydrate(win);
}

// ---------------------------------------------------------------------------------------------
// The Recycle bin count on the logo
// ---------------------------------------------------------------------------------------------
function queueRecount() {
    clearTimeout(recountTimer);
    recountTimer = setTimeout(recount, 250);
}

async function recount() {
    if (!auth.currentUser) return;
    const parts = await Promise.all(BIN_LISTS.map((name) => {
        const kept = keptBinnedCount(name);
        return kept !== null ? kept : countedBinned(name);
    }));
    binCount = parts.reduce((sum, n) => sum + (Number(n) || 0), 0);
    paintBadge();
    if (isOpen() && view === "menu") renderMenu();
}

/** One count query for a list this browser does not keep, remembered for 5 minutes in this tab. 0 when it fails. */
async function countedBinned(name) {
    try {
        const saved = JSON.parse(sessionStorage.getItem(COUNT_KEY + name) || "null");
        if (saved && Date.now() - saved.at < COUNT_KEEP_MS) return Number(saved.n) || 0;
    } catch (error) { /* not kept: ask */ }
    try {
        const n = (await getCountFromServer(query(collection(db, name), where("binned", "==", true)))).data().count;
        try { sessionStorage.setItem(COUNT_KEY + name, JSON.stringify({ n, at: Date.now() })); } catch (error) { /* not kept */ }
        return n;
    } catch (error) {
        console.warn("The Recycle bin of " + name + " could not be counted:", error);
        return 0;
    }
}

function paintBadge() {
    const badge = el("brandBinBadge");
    if (!badge || !button) return;
    badge.textContent = binCount > 99 ? "99+" : String(binCount);
    badge.hidden = binCount === 0;
    button.setAttribute("aria-label", binCount > 0 ? `Settings. Recycle bin: ${binCount}` : "Settings");      // the tooltip stays "Settings"
}
