// QUICK SETTINGS: the gear at the far right of the header, on every admin page (like Gmail's quick settings).
//
// A click on the gear slides ONE panel in from the right edge of the screen, NEXT to the page (the page gets narrower, nothing is
// covered); a second click, the X or Esc slides it out again and the page is full width again. Focus moves into the panel on open
// and back to the gear on close. On a window of 900 px or less there is no room: the panel covers the page (a click outside closes
// it there), and under 600 px it is full width. It always starts closed on a new page. The GlowUp logo is a plain badge (pageFrame.js): no
// click, no count.
//
// The panel is built on its FIRST OPEN, not at page load. Only an empty, hidden holder for the Layout & Style controls exists
// before that (adminLayouts.js builds them into #layoutHost at start; the first open moves them into the panel).
//
// Two views in the same panel (never a second one), so both have the same place, size and opening animation:
//   Quick settings   - Layout and style, Recycle bin (red count), Appearance (Light / Dark / System), the page's own tools.
//   Layout and style - the existing Layout & Style controls (adminLayouts.js builds them into #layoutHost); a back arrow in the
//                      title bar returns to Quick settings.
// adminLayouts.js talks to it with two events: "glowup:settings-close" (arranging hides the panel) and "glowup:layout-view"
// (the Layout and style view was shown: paint it).
//
// What used to be behind the "..." on Inventory and the Curator (Clean up, Find duplicates ...) is listed in the Quick settings view
// under "This page" (setPageSettings); a page with nothing of its own has no such heading.
//
// THE RECYCLE BIN COUNT is shown ONLY inside the open panel (never on the gear, the logo, a title, a tooltip, an aria-label or a data
// attribute). It is added up from the lists adminStore.js keeps in this browser, when the panel is opened or while it is open and a
// list changes. It makes NO Firestore read or query. A list that is not kept is not counted.
import { keptBinnedCount, onListsRefreshed } from "./adminStore.js";
import { themeChoice, setThemeChoice, icon, hydrate } from "./adminShell.js";

export const BIN_PAGE = "../_Admin_Recycle_Bin/adminRecycleBin.html";
const BIN_LISTS = ["ingredients", "brands", "products"];

let win = null, button = null;
let pageItems = null;                             // () => the page's own entries, in the form openMenu takes
let view = "menu";                                // "menu" | "layout"

const el = (id) => document.getElementById(id);
const isOpen = () => !!win && win.classList.contains("is-open");

/** The entries removed, added up from the kept lists only. { n, unknown } (unknown = some list is not kept in this browser). */
function binCount() {
    let n = 0, unknown = false;
    BIN_LISTS.forEach((name) => { const kept = keptBinnedCount(name); if (kept === null) unknown = true; else n += kept; });
    return { n, unknown };
}

// ---------------------------------------------------------------------------------------------
// Open, close, views
// ---------------------------------------------------------------------------------------------
function show(next) {
    view = next;
    const host = el("layoutHost");
    const layout = view === "layout" && !!host && host.childElementCount > 0;
    if (view === "layout" && !layout) view = "menu";
    el("swMenuView").hidden = view !== "menu";
    if (host) host.hidden = view !== "layout";
    el("swBack").hidden = view !== "layout";
    el("swTitle").textContent = view === "layout" ? "Layout & Style" : "Quick settings";
    win.setAttribute("aria-label", el("swTitle").textContent);
    if (view === "menu") renderMenu();
    else document.dispatchEvent(new CustomEvent("glowup:layout-view"));
}

/** Builds the panel (once, on the first open). */
function build() {
    win = document.createElement("aside");
    win.className = "settings-window";
    win.id = "settingsWindow";
    win.setAttribute("role", "dialog");
    win.setAttribute("aria-label", "Quick settings");
    win.tabIndex = -1;
    win.innerHTML = `
        <div class="sw-head">
            <button type="button" class="icon-btn small" id="swBack" aria-label="Back to quick settings" hidden></button>
            <h3 id="swTitle">Quick settings</h3>
            <button type="button" class="icon-btn small" id="swClose" aria-label="Close"></button>
        </div>
        <div class="sw-body">
            <div class="sw-view" id="swMenuView"></div>
        </div>`;
    document.body.appendChild(win);
    const body = win.querySelector(".sw-body");
    const host = el("layoutHost");                // built by adminLayouts.js into the holder; it moves in here
    if (host) { host.classList.add("sw-view", "layout-view"); host.hidden = true; body.appendChild(host); }
    const stash = el("layoutStash");
    if (stash) stash.remove();
    win.querySelector("#swBack").innerHTML = icon("left");
    win.querySelector("#swClose").innerHTML = icon("x");
    el("swClose").addEventListener("click", closeSettingsWindow);
    el("swBack").addEventListener("click", () => show("menu"));
    button.setAttribute("aria-controls", "settingsWindow");
    hydrate(win);
}

const overlay = () => window.matchMedia("(max-width: 900px)").matches;     // no room beside the page: the panel covers it
let hideTimer = 0;

function openWindow() {
    if (isOpen()) return;
    if (!win) build();
    clearTimeout(hideTimer);
    win.classList.add("is-shown");
    void win.offsetWidth;                         // the panel is drawn at its start place first, then slides in
    win.classList.add("is-open");
    document.documentElement.classList.add("qs-open");
    button.setAttribute("aria-expanded", "true");
    show("menu");
    win.focus({ preventScroll: true });
}

export function closeSettingsWindow() {
    if (!isOpen()) return;
    const hadFocus = win.contains(document.activeElement) || document.activeElement === document.body;
    win.classList.remove("is-open");
    document.documentElement.classList.remove("qs-open");
    button.setAttribute("aria-expanded", "false");
    // it slides out first, then leaves the layout
    hideTimer = setTimeout(() => { if (!isOpen()) win.classList.remove("is-shown"); }, 320);
    if (hadFocus) button.focus({ preventScroll: true });
}

/** The page's own entries for the Quick settings view: a function that returns them as openMenu takes them, asked each time the view is drawn. */
export function setPageSettings(items) {
    pageItems = items;
    if (isOpen() && view === "menu") renderMenu();
}

// ---------------------------------------------------------------------------------------------
// The Quick settings view
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

/** "This page": the entries that moved here from the page's "..." menu. Choosing one closes the panel, because a dialog or the list comes next. */
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
    const { n, unknown } = binCount();
    const description = n > 0 ? `${n} ${n === 1 ? "entry" : "entries"} removed` : unknown ? "Open it to see what was removed." : "Nothing removed";
    host.replaceChildren(
        ...(layoutHere ? [item({ label: "Layout and style", iconName: "sliders", description: "Arrange the panels, fonts and colours.", run: () => show("layout") })] : []),
        item({ label: "Recycle bin", iconName: "trash", checked: here.includes("Recycle_Bin"), badge: n > 0 ? (n > 99 ? "99+" : String(n)) : "", description, run: go(BIN_PAGE) }),
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
/**
 * Adds the gear to the header's tools (once) and wires it. Called by adminShell.mountShell on every admin page, before adminLayouts
 * fills #layoutHost. `tools` = the header's .topbar-tools. Returns the gear. Nothing of the panel is built yet.
 */
export function mountSettingsMenu(tools) {
    if (!tools) return null;
    if (button && tools.contains(button)) return button;
    button = document.createElement("button");
    button.type = "button";
    button.className = "icon-btn quick-gear";
    button.id = "quickSettingsBtn";
    button.setAttribute("aria-label", "Quick settings");
    button.setAttribute("data-tip", "Quick settings");        // the site shows no hover label on controls (adminShell.js tipOf); the text is here for it
    button.setAttribute("aria-haspopup", "dialog");
    button.setAttribute("aria-expanded", "false");
    button.innerHTML = icon("gear");
    tools.append(button);

    // the holder for the Layout & Style controls until the panel exists (adminLayouts.js finds #layoutHost)
    if (!el("layoutHost")) {
        const stash = document.createElement("div");
        stash.id = "layoutStash";
        stash.innerHTML = '<div id="layoutHost" hidden></div>';
        document.body.appendChild(stash);
    }

    button.addEventListener("click", () => { if (isOpen()) closeSettingsWindow(); else openWindow(); });
    document.addEventListener("glowup:settings-close", closeSettingsWindow);
    // Esc closes it, unless a dialog, pop-up or menu has taken that key already (they stop it before it gets here)
    document.addEventListener("keydown", (event) => {
        if (event.key !== "Escape" || event.defaultPrevented || !isOpen()) return;
        if (document.querySelector(".modal-overlay[style*='flex'], .menu.show, .facet-panel")) return;
        closeSettingsWindow();
    });
    // Beside the page the panel stays open while the page is used. Over the page (a small window) a click outside closes it;
    // a dialog, menu or message opened from inside the panel is part of it.
    document.addEventListener("pointerdown", (event) => {
        if (!isOpen() || !overlay() || win.contains(event.target) || button.contains(event.target)) return;
        if (event.target.closest && event.target.closest(".modal-overlay, .menu, .info-pop, .toast, .facet-panel")) return;
        closeSettingsWindow();
    }, true);
    document.addEventListener("glowup:theme", () => { if (isOpen() && view === "menu") renderMenu(); });
    document.addEventListener("glowup:bin-count", () => { if (isOpen() && view === "menu") renderMenu(); });
    onListsRefreshed(() => { if (isOpen() && view === "menu") renderMenu(); });
    return button;
}
