























import { keptBinnedCount, onListsRefreshed } from "./adminStore.js";
import { themeChoice, setThemeChoice, icon, hydrate } from "./adminShell.js";
import { paintShortcutsView } from "./shortcutsPanel.js";
import { watchRequests, onRequestsChanged, paintRequestsView, requestsState } from "./adminRequests.js";

export const BIN_PAGE = "../_Admin_Recycle_Bin/adminRecycleBin.html";
const BIN_LISTS = ["ingredients", "brands", "products"];

let win = null, button = null;
let pageItems = null;
let view = "menu";

const el = (id) => document.getElementById(id);
const isOpen = () => !!win && win.classList.contains("is-open");


function binCount() {
    let n = 0, unknown = false;
    BIN_LISTS.forEach((name) => { const kept = keptBinnedCount(name); if (kept === null) unknown = true; else n += kept; });
    return { n, unknown };
}




function show(next) {
    view = next;
    const host = el("layoutHost");
    const layout = view === "layout" && !!host && host.childElementCount > 0;
    if (view === "layout" && !layout) view = "menu";
    el("swMenuView").hidden = view !== "menu";
    if (host) host.hidden = view !== "layout";
    el("swKeysView").hidden = view !== "shortcuts";
    el("swReqView").hidden = view !== "requests";
    el("swBack").hidden = view === "menu";
    el("swTitle").textContent = view === "layout" ? "Layout & Style" : view === "shortcuts" ? "Keyboard shortcuts" : view === "requests" ? "Admin requests" : "Quick settings";
    win.setAttribute("aria-label", el("swTitle").textContent);
    if (view === "menu") renderMenu();
    else if (view === "shortcuts") paintShortcutsView(el("swKeysView"));
    else if (view === "requests") paintRequestsView(el("swReqView"));
    else document.dispatchEvent(new CustomEvent("glowup:layout-view"));
}


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
            <div class="sw-view keys-view" id="swKeysView" hidden></div>
            <div class="sw-view req-view" id="swReqView" aria-live="polite" hidden></div>
        </div>`;
    document.body.appendChild(win);

    const backdrop = document.createElement("div");
    backdrop.className = "sw-backdrop";
    backdrop.setAttribute("aria-hidden", "true");
    backdrop.addEventListener("click", closeSettingsWindow);
    document.body.appendChild(backdrop);
    const body = win.querySelector(".sw-body");
    const host = el("layoutHost");
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

const overlay = () => window.matchMedia("(max-width: 1023px)").matches;
let hideTimer = 0;





function placeBelowHeader() {
    const bar = button && button.closest(".topbar"), page = document.querySelector("main.main-content"), root = document.documentElement.style;
    if (bar) root.setProperty("--qs-top", bar.offsetHeight + "px");
    if (page) root.setProperty("--qs-sb", Math.max(0, page.offsetWidth - page.clientWidth) + "px");
}

function openWindow() {
    if (isOpen()) return;
    if (!win) build();
    placeBelowHeader();
    clearTimeout(hideTimer);
    win.classList.add("is-shown");
    win.classList.add("is-open");

    if (document.documentElement.classList.contains("sb-mobile-open") || document.documentElement.classList.contains("sb-hover")) {
        document.documentElement.classList.remove("sb-mobile-open", "sb-hover");
        const menuButton = document.getElementById("menuBtn");
        if (menuButton) {
            menuButton.setAttribute("aria-expanded", "false");
            menuButton.setAttribute("aria-label", "Open menu");
        }
    }
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

    hideTimer = setTimeout(() => { if (!isOpen()) win.classList.remove("is-shown"); }, 320);
    if (hadFocus) button.focus({ preventScroll: true });
}


export function setPageSettings(items) {
    pageItems = items;
    if (isOpen() && view === "menu") renderMenu();
}




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


function pageEntries() {
    const items = (pageItems ? pageItems() : []).filter((entry) => !entry.separator);
    if (items.length === 0) return [];
    return [separator(), heading("This page"), ...items.map((entry) => item({
        label: entry.label, description: entry.description, iconName: entry.icon, danger: entry.danger, disabled: entry.disabled,
        run: () => { closeSettingsWindow(); entry.run(); }
    }))];
}

// "Admin requests": people who pressed Sign Up on the login page; the count is live (adminRequests.js)
function requestsItem() {
    const { list, problem } = requestsState();
    const n = list ? list.length : 0;
    const description = list === null ? "Loading..." : problem ? "Could not be loaded. Open it to retry." : n === 0 ? "No requests waiting" : `${n} waiting for approval`;
    return item({ label: "Admin requests", iconName: "user", badge: n > 0 ? (n > 99 ? "99+" : String(n)) : "", description, run: () => show("requests") });
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
        requestsItem(),
        item({ label: "Keyboard shortcuts", iconName: "keyboard", description: "Keys for this page's actions. Press ? on a page to see them.", run: () => show("shortcuts") }),
        item({ label: "Recycle bin", iconName: "trash", checked: here.includes("Recycle_Bin"), badge: n > 0 ? (n > 99 ? "99+" : String(n)) : "", description, run: go(BIN_PAGE) }),
        separator(),
        heading("Appearance"),
        item({ label: "Light", iconName: "sun", checked: choice === "light", run: () => { setThemeChoice("light"); renderMenu(); } }),
        item({ label: "Dark", iconName: "moon", checked: choice === "dark", run: () => { setThemeChoice("dark"); renderMenu(); } }),
        item({ label: "System", iconName: "monitor", checked: choice === "system", description: "Follows this computer's setting.", run: () => { setThemeChoice("system"); renderMenu(); } }),
        ...pageEntries()
    );
}








export function mountSettingsMenu(tools) {
    if (!tools) return null;
    if (button && tools.contains(button)) return button;
    button = document.createElement("button");
    button.type = "button";
    button.className = "icon-btn quick-gear";
    button.id = "quickSettingsBtn";
    button.setAttribute("aria-label", "Quick settings");
    button.setAttribute("data-tip", "Quick settings");
    button.setAttribute("aria-haspopup", "dialog");
    button.setAttribute("aria-expanded", "false");
    button.innerHTML = icon("gear");
    tools.append(button);


    if (!el("layoutHost")) {
        const stash = document.createElement("div");
        stash.id = "layoutStash";
        stash.innerHTML = '<div id="layoutHost" hidden></div>';
        document.body.appendChild(stash);
    }

    button.addEventListener("click", () => { if (isOpen()) closeSettingsWindow(); else openWindow(); });


    const idle = window.requestIdleCallback || ((fn) => setTimeout(fn, 1200));
    idle(() => { if (!win) build(); }, { timeout: 3000 });
    document.addEventListener("glowup:settings-close", closeSettingsWindow);

    document.addEventListener("keydown", (event) => {
        if (event.key !== "Escape" || event.defaultPrevented || !isOpen()) return;
        if (document.querySelector(".modal-overlay[style*='flex'], .menu.show, .facet-panel")) return;
        closeSettingsWindow();
    });


    document.addEventListener("pointerdown", (event) => {
        if (!isOpen() || !overlay() || win.contains(event.target) || button.contains(event.target)) return;
        if (event.target.closest && event.target.closest(".modal-overlay, .menu, .info-pop, .toast, .facet-panel")) return;
        closeSettingsWindow();
    }, true);

    let placeQueued = 0;
    window.addEventListener("resize", () => { if (isOpen() && !placeQueued) placeQueued = requestAnimationFrame(() => { placeQueued = 0; placeBelowHeader(); }); }, { passive: true });
    document.addEventListener("glowup:theme", () => { if (isOpen() && view === "menu") renderMenu(); });
    document.addEventListener("glowup:bin-count", () => { if (isOpen() && view === "menu") renderMenu(); });
    watchRequests();
    onRequestsChanged(() => {
        if (!isOpen()) return;
        if (view === "menu") renderMenu();
        else if (view === "requests") paintRequestsView(el("swReqView"));
    });
    onListsRefreshed(() => { if (isOpen() && view === "menu") renderMenu(); });
    return button;
}
