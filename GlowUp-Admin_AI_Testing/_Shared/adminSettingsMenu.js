// THE SETTINGS WINDOW behind the GlowUp logo at the top of the sidebar, on every admin page.
//
// The logo button shows the "G" and the red Recycle bin count. One tap opens ONE floating window, a second tap on the logo hides
// it again; Esc and the X in its title bar close it too. A click anywhere else does NOT close it and there is no dark cover, so
// the page stays usable while it is open. It always starts closed on a new page.
//
// Two views in the same window (never a second window), so both have the same place, size and opening animation:
//   Settings         - Layout and style, Recycle bin (red count), Appearance (Light / Dark / System), the page's own tools.
//   Layout and style - the existing Layout & Style controls (adminLayouts.js builds them into #layoutHost); a back arrow in the
//                      title bar returns to Settings.
// Its left edge sits ON the sidebar's right edge (New7.pdf, 5 November 2026): the style sheet gives both the same value, --sidebar-w,
// so when the sidebar folds the window goes with it, open, with no gap at any frame. While it is open the page's title slides out from
// under it (html.settings-open). Its layout is fixed (New8.pdf): one width on every page, as tall as its content up to the browser
// window, then its list scrolls under the title bar. It cannot be dragged or resized.
// The Archive is opened from the button on the Recycle Bin page, and Sign Out sits at the bottom of the sidebar (pageFrame.js).
// adminLayouts.js talks to it with two events: "glowup:settings-close" (arranging hides the window) and "glowup:layout-view"
// (the Layout and style view was shown: paint it).
//
// THE GEAR beside Add on Inventory and the Curator opens the same window (wireSettingsButton). What used to be behind the "..."
// there (Clean up, Find duplicates ...) is listed in the Settings view under "This page" (setPageSettings).
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
const BIN_LISTS = ["ingredients", "brands", "products"];
const COUNT_KEEP_MS = 5 * 60 * 1000;
const COUNT_KEY = "glowup.binCount.";

let binCount = 0;
let recountTimer = 0;
let win = null, button = null;
let opener = null;                                // what opened the window: the focus goes back there
let pageItems = null;                             // () => the page's own entries, in the form openMenu takes
let view = "menu";                                // "menu" | "layout"

const el = (id) => document.getElementById(id);
const isOpen = () => !!win && win.classList.contains("is-open");

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
}

function openWindow(from) {
    if (isOpen()) return;
    opener = from || button;
    win.classList.add("is-open");
    document.documentElement.classList.add("settings-open");
    opener.setAttribute("aria-expanded", "true");
    show("menu");
    win.focus({ preventScroll: true });
}

export function closeSettingsWindow() {
    if (!isOpen()) return;
    const hadFocus = win.contains(document.activeElement);
    win.classList.remove("is-open");
    document.documentElement.classList.remove("settings-open");
    opener.setAttribute("aria-expanded", "false");
    if (hadFocus) opener.focus({ preventScroll: true });
}

/** A second button that opens and closes the window (the gear of a page). */
export function wireSettingsButton(gear) {
    gear.addEventListener("click", () => { if (isOpen()) closeSettingsWindow(); else openWindow(gear); });
}

/** The page's own entries for the Settings view: a function that returns them as openMenu takes them, asked each time the view is drawn. */
export function setPageSettings(items) {
    pageItems = items;
    if (isOpen() && view === "menu") renderMenu();
}

// ---------------------------------------------------------------------------------------------
// The Settings view
// ---------------------------------------------------------------------------------------------
function item({ label, description, iconName, danger, checked, badge, disabled, run }) {
    const row = document.createElement("button");
    row.type = "button";
    row.disabled = !!disabled;
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

/** "This page": the entries that moved here from the page's "..." menu. Choosing one closes the window, because a dialog or the list comes next. */
function pageEntries() {
    const items = (pageItems ? pageItems() : []).filter((entry) => !entry.separator);
    if (items.length === 0) return [];
    return [separator(), heading("This page"), ...items.map((entry) => item({
        label: entry.label, description: entry.description, iconName: entry.icon, danger: entry.danger, disabled: entry.disabled,
        run: () => { closeSettingsWindow(); entry.run(); }
    }))];
}

function renderMenu() {
    const host = el("swMenuView");
    if (!host) return;
    const choice = themeChoice(), here = location.pathname;
    const go = (address) => () => { window.location.href = address; };
    const layoutHere = !!el("layoutHost") && el("layoutHost").childElementCount > 0;
    host.replaceChildren(
        ...(layoutHere ? [item({ label: "Layout and style", iconName: "sliders", description: "Arrange the panels, fonts and colours.", run: () => show("layout") })] : []),
        item({ label: "Recycle bin", iconName: "trash", checked: here.includes("Recycle_Bin"), badge: binCount > 0 ? (binCount > 99 ? "99+" : String(binCount)) : "", description: binCount > 0 ? `${binCount} ${binCount === 1 ? "entry" : "entries"} removed` : "Nothing removed", run: go(BIN_PAGE) }),
        separator(),
        heading("Appearance"),
        item({ label: "Light", iconName: "sun", checked: choice === "light", run: () => { setThemeChoice("light"); renderMenu(); } }),
        item({ label: "Dark", iconName: "moon", checked: choice === "dark", run: () => { setThemeChoice("dark"); renderMenu(); } }),
        item({ label: "System", iconName: "monitor", checked: choice === "system", description: "Follows this computer's setting.", run: () => { setThemeChoice("system"); renderMenu(); } }),
        ...pageEntries()
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
        <div class="sw-head">
            <button type="button" class="icon-btn small" id="swBack" aria-label="Back to settings" data-tip="Back to settings" hidden></button>
            <h3 id="swTitle">Settings</h3>
            <button type="button" class="icon-btn small" id="swClose" aria-label="Close" data-tip="Close"></button>
        </div>
        <div class="sw-body">
            <div class="sw-view" id="swMenuView"></div>
            <div class="sw-view layout-view" id="layoutHost" hidden></div>
        </div>`;
    document.body.appendChild(win);
    win.querySelector("#swBack").innerHTML = icon("left");
    win.querySelector("#swClose").innerHTML = icon("x");

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
