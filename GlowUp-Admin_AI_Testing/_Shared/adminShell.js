










import { catchFailures } from "./quietConsole.js";
import { initLayouts, applyAppearance } from "./adminLayouts.js";
import { refreshData, refreshWait } from "./adminStore.js";
import { mountSettingsMenu } from "./adminSettingsMenu.js";
import { mountHeaderAccount } from "./headerAccount.js";
import { startShortcuts, keyChips } from "./shortcuts.js";
import { ariaKeys } from "./shortcutKeys.js";

catchFailures((text) => toast(text, "error"));




const ICONS = {
    logo: '<path d="M12 3l2.2 6.8L21 12l-6.8 2.2L12 21l-2.2-6.8L3 12l6.8-2.2z"/>',
    dashboard: '<rect x="3" y="3" width="7" height="9" rx="1.5"/><rect x="14" y="3" width="7" height="5" rx="1.5"/><rect x="14" y="12" width="7" height="9" rx="1.5"/><rect x="3" y="16" width="7" height="5" rx="1.5"/>',
    box: '<path d="M21 8l-9-5-9 5 9 5 9-5z"/><path d="M3 8v8l9 5 9-5V8"/><path d="M12 13v8"/>',
    flask: '<path d="M9 3h6"/><path d="M10 3v6l-5 9a2 2 0 0 0 1.8 3h10.4a2 2 0 0 0 1.8-3l-5-9V3"/><path d="M7.5 15h9"/>',
    logout: '<path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><path d="M16 17l5-5-5-5"/><path d="M21 12H9"/>',
    sun: '<circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/>',
    moon: '<path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/>',
    gear: '<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"/>',
    sliders: '<path d="M4 6h10M18 6h2M4 12h4M12 12h8M4 18h12"/><circle cx="16" cy="6" r="2"/><circle cx="10" cy="12" r="2"/><circle cx="18" cy="18" r="2"/>',
    search: '<circle cx="11" cy="11" r="7"/><path d="M21 21l-4.3-4.3"/>',
    plus: '<path d="M12 5v14M5 12h14"/>',
    edit: '<path d="M4 20h4L19 9l-4-4L4 16v4z"/><path d="M13.5 6.5l4 4"/>',
    trash: '<path d="M4 7h16"/><path d="M9 7V4h6v3"/><path d="M6 7l1 13h10l1-13"/><path d="M10 11v6M14 11v6"/>',
    check: '<path d="M5 12.5l4.5 4.5L19 7.5"/>',
    flag: '<path d="M5 21V4"/><path d="M5 4h11l-2 4 2 4H5"/>',
    x: '<path d="M6 6l12 12M18 6L6 18"/>',
    camera: '<path d="M4 8h3.2l1.8-2.5h6l1.8 2.5H20v11H4z"/><circle cx="12" cy="13.2" r="3.6"/>',
    refresh: '<path d="M20 11a8 8 0 0 0-14.5-3.5L4 9"/><path d="M4 4v5h5"/><path d="M4 13a8 8 0 0 0 14.5 3.5L20 15"/><path d="M20 20v-5h-5"/>',
    left: '<path d="M15 6l-6 6 6 6"/>',
    right: '<path d="M9 6l6 6-6 6"/>',
    down: '<path d="M6 9l6 6 6-6"/>',
    activity: '<path d="M3 12h4l3-8 4 16 3-8h4"/>',
    tag: '<path d="M3 12V4h8l10 10-8 8L3 12z"/><circle cx="7.5" cy="7.5" r="1.2"/>',
    lipstick: '<path d="M9 21h6v-8H9z"/><path d="M10 13V8l4-3v8"/>',
    alert: '<path d="M12 4l9 16H3L12 4z"/><path d="M12 10v4M12 17.5v.01"/>',
    inbox: '<path d="M3 13l3-8h12l3 8v6H3v-6z"/><path d="M3 13h5l1 3h6l1-3h5"/>',
    grip: '<path d="M9 6v.01M15 6v.01M9 12v.01M15 12v.01M9 18v.01M15 18v.01" stroke-width="2.6"/>',
    more: '<path d="M5 12v.01M12 12v.01M19 12v.01" stroke-width="2.8"/>',
    eye: '<path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3"/>',
    eyeOff: '<path d="M3 3l18 18"/><path d="M10.6 5.2A10.4 10.4 0 0 1 12 5c6.4 0 10 7 10 7a17 17 0 0 1-3.2 4.1M6.3 6.4C3.6 8.3 2 12 2 12s3.6 7 10 7c1.7 0 3.2-.5 4.5-1.2"/>',
    lock: '<rect x="5" y="11" width="14" height="9" rx="2"/><path d="M8 11V8a4 4 0 0 1 8 0v3"/>',
    user: '<circle cx="12" cy="8" r="4"/><path d="M4 21c1.5-4 4.5-6 8-6s6.5 2 8 6"/>',
    bars: '<path d="M4 20V10M10 20V4M16 20v-7M22 20H2"/>',
    line: '<path d="M3 20h18"/><path d="M4 15l5-5 4 3 7-8"/>',
    pie: '<path d="M12 4a8 8 0 1 0 8 8h-8z"/><path d="M15 3.5A8 8 0 0 1 20.5 9H15z"/>',
    table: '<rect x="3" y="4" width="18" height="16" rx="2"/><path d="M3 10h18M3 15h18M9 4v16"/>',
    list: '<path d="M9 6h11M9 12h11M9 18h11"/><path d="M4.5 6v.01M4.5 12v.01M4.5 18v.01" stroke-width="2.6"/>',
    rows: '<rect x="3" y="6" width="5" height="12" rx="1.5"/><rect x="9.5" y="6" width="5" height="12" rx="1.5"/><rect x="16" y="6" width="5" height="12" rx="1.5"/>',
    grid: '<rect x="3.5" y="3.5" width="7" height="7" rx="1.5"/><rect x="13.5" y="3.5" width="7" height="7" rx="1.5"/><rect x="3.5" y="13.5" width="7" height="7" rx="1.5"/><rect x="13.5" y="13.5" width="7" height="7" rx="1.5"/>',
    layers: '<path d="M12 3l9 5-9 5-9-5 9-5z"/><path d="M3 13l9 5 9-5"/>',
    split: '<rect x="3" y="5" width="7" height="14" rx="1.5"/><rect x="14" y="5" width="7" height="14" rx="1.5"/>',
    tabs: '<rect x="3" y="8" width="18" height="12" rx="2"/><path d="M3 8V6a2 2 0 0 1 2-2h5v4"/>',
    undo: '<path d="M4 9h10a6 6 0 0 1 0 12H8"/><path d="M8 5L4 9l4 4"/>',
    redo: '<path d="M20 9H10a6 6 0 0 0 0 12h6"/><path d="M16 5l4 4-4 4"/>',
    bulb: '<path d="M9 18h6M10 21h4"/><path d="M12 3a6 6 0 0 0-3.5 10.9c.5.4.8 1 .8 1.6V16h5.4v-.5c0-.6.3-1.2.8-1.6A6 6 0 0 0 12 3z"/>',
    ban: '<circle cx="12" cy="12" r="9"/><path d="M5.6 5.6l12.8 12.8"/>',
    save: '<path d="M5 3h11l3 3v15H5z"/><path d="M8 3v6h7V3M8 21v-7h8v7"/>',
    reset: '<path d="M3 12a9 9 0 1 0 3-6.7"/><path d="M3 4v5h5"/>',
    download: '<path d="M12 4v11M7 11l5 5 5-5M4 20h16"/>',
    upload: '<path d="M12 16V5M7 9l5-5 5 5M4 20h16"/>',
    layout: '<rect x="3" y="4" width="18" height="16" rx="2"/><path d="M3 10h18M9 10v10"/>',
    shield: '<path d="M12 3l8 3v6c0 4.5-3.2 7.8-8 9-4.8-1.2-8-4.5-8-9V6l8-3z"/><path d="M9 12l2.2 2.2L15 10.5"/>',
    type: '<path d="M5 6V4h14v2M12 4v16M9 20h6"/>',
    palette: '<path d="M12 3a9 9 0 1 0 0 18c1.5 0 2-1 2-2s-.5-1.5-.5-2.5S14.5 15 16 15h2a3 3 0 0 0 3-3c0-5-4-9-9-9z"/><circle cx="7.5" cy="11" r="1"/><circle cx="10" cy="7.5" r="1"/><circle cx="14.5" cy="7.5" r="1"/>',
    folder: '<path d="M3 7a2 2 0 0 1 2-2h4l2 2.5h8a2 2 0 0 1 2 2V18a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7z"/>',
    folderPlus: '<path d="M3 7a2 2 0 0 1 2-2h4l2 2.5h8a2 2 0 0 1 2 2V18a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7z"/><path d="M12 11v6M9 14h6"/>',
    sort: '<path d="M7 5v14M4 16l3 3 3-3"/><path d="M13 7h8M13 12h6M13 17h4"/>',
    play: '<path d="M8 5.5v13l11-6.5z"/>',
    keyboard: '<rect x="2.5" y="6" width="19" height="12" rx="2"/><path d="M6.5 10h.01M10 10h.01M13.5 10h.01M17 10h.01M6.5 14h.01M17 14h.01M9.5 14h5"/>',
    sparkle: '<path d="M11 3l1.9 5.1L18 10l-5.1 1.9L11 17l-1.9-5.1L4 10l5.1-1.9z"/><path d="M18.5 15l.9 2.1 2.1.9-2.1.9-.9 2.1-.9-2.1-2.1-.9 2.1-.9z"/>',
    up: '<path d="M6 15l6-6 6 6"/>',
    archive: '<rect x="3" y="4" width="18" height="5" rx="1.5"/><path d="M5 9v10a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V9"/><path d="M10 13h4"/>',
    monitor: '<rect x="3" y="4" width="18" height="12" rx="2"/><path d="M8 20h8M12 16v4"/>',
    minus: '<path d="M5 12h14"/>',
    clock: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
    bell: '<path d="M6 16v-5a6 6 0 0 1 12 0v5l1.5 2h-15L6 16z"/><path d="M10 21a2.2 2.2 0 0 0 4 0"/>',
    bug: '<rect x="8" y="8" width="8" height="11" rx="4"/><path d="M12 12v7M9.5 8V6.5a2.5 2.5 0 0 1 5 0V8M4 11h4M16 11h4M4 17h4M16 17h4M5.5 6.5L8 9M18.5 6.5L16 9"/>',
    mail: '<rect x="3" y="5" width="18" height="14" rx="2"/><path d="M3.5 6.5l8.5 6.5 8.5-6.5"/>',
    send: '<path d="M21 3L10.5 13.5"/><path d="M21 3l-6.5 18-4-7.5-7.5-4L21 3z"/>',
};

export const BIN_ICON = '<svg class="ic bin-ic" viewBox="0 0 24 24" aria-hidden="true"><g class="bin-lid"><path d="M4 7h16"/><path d="M9 7V4h6v3"/></g><path d="M6 7l1 13h10l1-13"/><path d="M10 11v6M14 11v6"/></svg>';





export function setBinCount() {
    document.dispatchEvent(new CustomEvent("glowup:bin-count"));
}


export function icon(name, extraClass) {
    return `<svg class="ic${extraClass ? " " + extraClass : ""}" viewBox="0 0 24 24" aria-hidden="true">${ICONS[name] || ICONS.logo}</svg>`;
}


export function hydrateIcons(root = document) {
    root.querySelectorAll("i[data-icon]").forEach((holder) => { holder.outerHTML = icon(holder.getAttribute("data-icon")); });
}


export const esc = (value) => String(value === null || value === undefined ? "" : value)
    .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/'/g, "&#39;");


export const calmMotion = () => window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;




const THEME_KEY = "glowup.theme";

export function currentTheme() { return document.documentElement.dataset.theme === "light" ? "light" : "dark"; }

const systemTheme = () => "light"; // default theme is light; the OS preference is not followed


export function themeChoice() {
    let chosen = null;
    try { chosen = localStorage.getItem(THEME_KEY); } catch (error) {                      }
    return chosen === "light" || chosen === "dark" ? chosen : "system";
}


export function setThemeChoice(choice) {
    if (choice === "light" || choice === "dark") { setTheme(choice); return; }
    try { localStorage.removeItem(THEME_KEY); } catch (error) {                      }
    setTheme(systemTheme(), false);
}


let followingSystem = false;
function followSystemTheme() {
    if (followingSystem || !window.matchMedia) return;
    followingSystem = true;
    const system = window.matchMedia("(prefers-color-scheme: light)");
    system.addEventListener("change", () => { if (themeChoice() === "system") setTheme("light", false); });
}

export function setTheme(theme, remember = true) {
    document.documentElement.dataset.theme = theme === "light" ? "light" : "dark";
    if (remember) { try { localStorage.setItem(THEME_KEY, document.documentElement.dataset.theme); } catch (error) {                      } }
    document.querySelectorAll(".theme-toggle").forEach(paintThemeButton);
    applyAppearance();
    document.dispatchEvent(new CustomEvent("glowup:theme"));
}

function paintThemeButton(button) {
    const dark = currentTheme() === "dark";
    button.innerHTML = icon(dark ? "sun" : "moon");
    button.setAttribute("aria-label", dark ? "Switch to light mode" : "Switch to dark mode");
    button.dataset.tipTitle = dark ? "Light mode" : "Dark mode";
    button.dataset.tip = dark ? "Switch every page to the light look." : "Switch every page to the dark look.";
}


export function mountThemeToggle(host) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "icon-btn theme-toggle";
    paintThemeButton(button);
    button.addEventListener("click", () => setTheme(currentTheme() === "dark" ? "light" : "dark"));
    host.appendChild(button);

    followSystemTheme();
    return button;
}










export function addRefreshDataButton(host) {
    try {
        const tools = host && host.classList.contains("topbar") ? host.querySelector(".topbar-tools") : host;
        if (!tools) return;
        const baseTip = "Refresh data, about 700 reads";
        const button = document.createElement("button");
        button.type = "button";
        button.className = "icon-btn refresh-btn";
        button.id = "refreshDataButton";
        button.setAttribute("aria-label", "Refresh data");
        button.innerHTML = icon("refresh");
        const words = (seconds) => seconds >= 3600 ? "1 h" : seconds >= 60 ? Math.floor(seconds / 60) + " min " + (seconds % 60 ? (seconds % 60) + " s" : "").trim() : seconds + " s";
        let working = false, ticker = 0;
        const paint = () => {
            const wait = refreshWait();
            const blocked = working || wait.seconds > 0;
            button.classList.toggle("is-blocked", blocked);
            button.classList.toggle("is-working", working);
            button.setAttribute("aria-disabled", String(blocked));
            const tip = working ? baseTip + ". Refreshing now..." : wait.seconds > 0
                ? baseTip + ". " + (wait.hourly ? "The hourly limit is used up. Try again in " : "Wait ") + words(wait.seconds).trim() + "."
                : baseTip;
            button.setAttribute("data-tip", tip);
            button.setAttribute("aria-label", tip);
            if (wait.seconds > 0 && !ticker) ticker = setInterval(paint, 1000);
            if (wait.seconds <= 0 && ticker) { clearInterval(ticker); ticker = 0; }
        };
        button.addEventListener("click", async () => {
            if (working || refreshWait().seconds > 0) { paint(); return; }
            working = true;
            paint();
            try { await refreshData(); } catch (error) { console.error("Refresh data failed:", error); }
            working = false;
            paint();
        });
        paint();
        tools.prepend(button);
    } catch (error) {
        console.warn("The Refresh data button could not be added:", error);
    }
}









export function placeInHeader(searchSelector, actionsId) {
    const bar = document.querySelector(".topbar");
    if (!bar) return;
    const search = document.querySelector(".board " + searchSelector);
    const controls = bar.querySelector(".topbar-controls");
    const tools = bar.querySelector(".topbar-tools");
    const actions = document.getElementById(actionsId);
    if (search && controls) { bar.classList.add("has-search"); controls.insertBefore(search, tools); }
    if (actions && tools) tools.insertBefore(actions, tools.firstChild);
}





export function placeHeaderParts() {
    const bar = document.querySelector("main.main-content > header.topbar");
    const holder = document.querySelector("[data-header-parts]");
    if (!bar || !holder) return bar;
    const controls = bar.querySelector(".topbar-controls");
    const tools = bar.querySelector(".topbar-tools");
    bar.classList.add("has-search", "has-tabs");
    holder.querySelectorAll(":scope > .header-tabs, :scope > .search-container").forEach((part) => controls.insertBefore(part, tools));
    const end = holder.querySelector(":scope > [data-header-end]");
    if (end) {
        const gear = tools.querySelector(".quick-gear");
        if (gear) gear.before(...end.childNodes); else tools.append(...end.childNodes);
    }
    holder.remove();
    return bar;
}

export function mountShell(page) {
    const main = document.querySelector("main.main-content");
    document.body.classList.add("app");



    if (!window.GlowUpFrame) throw new Error("pageFrame.js must be the first thing inside <main> of an admin page.");
    window.GlowUpFrame.build();
    const sidebar = document.querySelector("aside.sidebar");
    const topbar = main.querySelector("header.topbar");
    wireNavDrop(sidebar.querySelector(".nav-links"));


    const tools = topbar.querySelector(".topbar-tools");
    tools.innerHTML = "";
    followSystemTheme();

    const bar =document.createElement("div");
    bar.className = "editing-bar";
    bar.innerHTML = `${icon("layout")}<span><b>Arranging.</b> Drag a panel to move it; drag its right or bottom edge to resize it.</span><span class="spacer"></span>
        <button type="button" class="btn" id="addPanelBtn">${icon("plus")}Panel</button>
        <button type="button" class="btn primary" id="doneArrangingBtn">${icon("check")}Done</button>`;
    topbar.insertAdjacentElement("afterend", bar);

    hydrate(document);
    mountSettingsMenu(tools);
    mountHeaderAccount(tools);
    initLayouts(page);
    watchForNewControls();
    startShortcuts(page);
}







function wireNavDrop(list) {
    const drop = list.querySelector(".nav-drop");
    const links = Array.from(list.querySelectorAll("a"));
    const home = null;
    let at = null;
    const place = (link, instant) => {
        if (!link) { drop.style.opacity = "0"; return; }
        const top = link.offsetTop, bottom = list.clientHeight - (link.offsetTop + link.offsetHeight);
        const goingDown = at !== null && top > at;

        const lead = "0.2s cubic-bezier(.2, .8, .2, 1)", follow = "0.46s cubic-bezier(.3, 1.45, .5, 1)";
        drop.style.transition = instant ? "none" : `top ${goingDown ? follow : lead}, bottom ${goingDown ? lead : follow}, opacity .2s, background-color .3s, border-color .3s`;
        drop.style.top = top + "px"; drop.style.bottom = bottom + "px"; drop.style.opacity = "1";
        at = top;
    };
    place(home, true);
    links.forEach((link) => {

        const over = () => place(link.classList.contains("active") ? null : link);
        link.addEventListener("mouseenter", over);
        link.addEventListener("focus", over);
    });
    list.addEventListener("mouseleave", () => place(home));
    list.addEventListener("focusout", () => { if (!list.contains(document.activeElement)) place(home); });
    let placeQueued = 0;
    window.addEventListener("resize", () => { if (!placeQueued) placeQueued = requestAnimationFrame(() => { placeQueued = 0; place(home, true); }); }, { passive: true });
}


export function hydrate(root = document) {
    hydrateIcons(root);
    hydrateGuides(root);
    hydrateInfos(root);
    tagFx(root);
}





export function guideBadge(title, text, preview) {
    return `<button type="button" class="guide-badge" aria-label="What is this?" data-tip-title="${esc(title)}" data-tip="${esc(text)}"${preview ? ` data-tip-preview="${esc(preview)}"` : ""}>i</button>`;
}


function hydrateGuides(root) {
    root.querySelectorAll("[data-guide]:not([data-guide-done])").forEach((control) => {
        control.setAttribute("data-guide-done", "1");
        const title = control.getAttribute("data-guide-title") || control.textContent.replace(/\s+/g, " ").trim() || control.getAttribute("aria-label") || "";
        control.insertAdjacentHTML("afterend", guideBadge(title, control.getAttribute("data-guide"), control.getAttribute("data-guide-preview")));
    });
}





let infoPop = null, infoOwner = null, infoPinned = false, infoTimer = 0;

function infoElement() {
    if (infoPop) return infoPop;
    infoPop = document.createElement("div");
    infoPop.className = "info-pop";
    infoPop.id = "infoPop";
    infoPop.setAttribute("role", "note");
    infoPop.addEventListener("mouseenter", () => clearTimeout(infoTimer));
    infoPop.addEventListener("mouseleave", () => closeInfoSoon());
    document.body.appendChild(infoPop);
    return infoPop;
}

function openInfo(badge, pinned) {
    clearTimeout(infoTimer);
    const box = infoElement();
    if (infoOwner && infoOwner !== badge) infoOwner.setAttribute("aria-expanded", "false");
    infoOwner = badge;
    infoPinned = !!pinned;
    box.replaceChildren();
    (badge._info || []).forEach((line) => {
        const row = document.createElement("p");
        row.className = "info-line";
        row.dataset.tone = line.tone || "";
        row.textContent = line.text;
        box.appendChild(row);
        if (line.label && line.run) {
            const act = document.createElement("button");
            act.type = "button";
            act.className = "row-btn";
            act.textContent = line.label;
            act.addEventListener("click", () => { closeInfo(false); line.run(); });
            row.appendChild(act);
        }
    });
    badge.setAttribute("aria-expanded", "true");
    badge.setAttribute("aria-describedby", "infoPop");
    box.classList.add("show");
    const at = badge.getBoundingClientRect(), size = box.getBoundingClientRect();
    let left = at.left + at.width / 2 - size.width / 2;
    left = Math.max(8, Math.min(left, window.innerWidth - size.width - 8));
    let top = at.bottom + 6;
    if (top + size.height > window.innerHeight - 8) top = at.top - size.height - 6;
    box.style.left = left + "px";
    box.style.top = Math.max(8, top) + "px";
}

function closeInfo(giveFocusBack) {
    clearTimeout(infoTimer);
    if (infoPop) infoPop.classList.remove("show");
    if (infoOwner) { infoOwner.setAttribute("aria-expanded", "false"); if (giveFocusBack) infoOwner.focus(); }
    infoOwner = null;
    infoPinned = false;
}
function closeInfoSoon() {
    if (infoPinned) return;
    clearTimeout(infoTimer);
    infoTimer = setTimeout(() => closeInfo(false), 160);
}





export function infoBadge({ label, text, lines, window: asWindow, title, key }) {
    const badge = document.createElement("button");
    badge.type = "button";
    badge.className = "guide-badge info-badge";
    badge.textContent = "i";
    badge.setAttribute("aria-label", label || "More information");
    badge.setAttribute("aria-haspopup", "true");
    badge.setAttribute("aria-expanded", "false");
    badge._info = Array.isArray(lines) && lines.length ? lines : [{ text: String(text || "") }];
    if (asWindow) {

        badge.setAttribute("aria-haspopup", "dialog");
        badge.setAttribute("aria-expanded", String(!!(infoWin && infoWinKey === key)));
        badge.addEventListener("click", () => { if (infoWin && infoWinKey === key) closeInfoWindow(true); else openInfoWindow(badge, { key, title, lines: badge._info }); });
        return badge;
    }
    badge.addEventListener("mouseenter", () => { if (!infoPinned) openInfo(badge, false); });
    badge.addEventListener("mouseleave", closeInfoSoon);
    badge.addEventListener("focus", () => { if (!infoPinned) openInfo(badge, false); });
    badge.addEventListener("blur", (event) => { if (!infoPop || !infoPop.contains(event.relatedTarget)) { infoPinned = false; closeInfoSoon(); } });
    badge.addEventListener("click", () => { if (infoOwner === badge && infoPinned) closeInfo(false); else openInfo(badge, true); });
    return badge;
}


let infoWin = null, infoWinKey = "", infoWinOpener = null;
const TONE_ORDER = { bad: 0, warn: 1, info: 2 };
const TONE_TAG = { bad: "Important", warn: "Check", info: "Info", ok: "OK" };

function fillInfoWindow(lines) {
    const list = infoWin.querySelector(".iw-list");
    list.replaceChildren();

    const rows = (lines || []).map((line, index) => ({ line, index })).sort((a, b) => ((TONE_ORDER[a.line.tone] ?? 3) - (TONE_ORDER[b.line.tone] ?? 3)) || a.index - b.index);
    rows.forEach(({ line }) => {
        const item = document.createElement("li");
        item.className = "iw-row";
        item.dataset.tone = line.tone || "";
        const tag = document.createElement("span");
        tag.className = "iw-tag";
        tag.textContent = TONE_TAG[line.tone] || "Note";
        const text = document.createElement("p");
        text.className = "iw-text";
        text.textContent = line.text;
        const body = document.createElement("div");
        body.className = "iw-body";
        body.append(tag, text);
        item.appendChild(body);
        if (line.label && line.run) {
            const act = document.createElement("button");
            act.type = "button";
            act.className = "row-btn";
            act.textContent = line.label;
            act.addEventListener("click", () => line.run());
            item.appendChild(act);
        }
        list.appendChild(item);
    });
    const count = infoWin.querySelector(".iw-count");
    count.textContent = String((lines || []).length);
    const serious = (lines || []).filter((line) => line.tone === "bad").length;
    infoWin.dataset.serious = serious > 0 ? "1" : "";
}

function placeInfoWindow(left, top) {
    const box = infoWin.getBoundingClientRect();
    infoWin.style.left = Math.max(4, Math.min(left, window.innerWidth - Math.min(box.width, window.innerWidth) - 4)) + "px";
    infoWin.style.top = Math.max(4, Math.min(top, window.innerHeight - 60)) + "px";
}

function buildInfoWindow() {
    const win = document.createElement("div");
    win.className = "info-window";
    win.setAttribute("role", "dialog");
    win.setAttribute("tabindex", "-1");
    win.innerHTML = '<div class="iw-head" tabindex="0" aria-label="Notes window. Drag it, or move it with the arrow keys."><span class="iw-title"></span><span class="iw-count"></span><button type="button" class="icon-btn small iw-close" aria-label="Close the notes window"></button></div><ul class="iw-list"></ul>';
    win.querySelector(".iw-close").innerHTML = icon("x");
    win.querySelector(".iw-close").addEventListener("click", () => closeInfoWindow(true));
    const head = win.querySelector(".iw-head");
    head.addEventListener("pointerdown", (event) => {
        if (event.target.closest(".iw-close")) return;
        event.preventDefault();
        const box = win.getBoundingClientRect(), dx = event.clientX - box.left, dy = event.clientY - box.top;
        win.classList.add("moving");
        const move = (e) => placeInfoWindow(e.clientX - dx, e.clientY - dy);
        const stop = () => { win.classList.remove("moving"); window.removeEventListener("pointermove", move); window.removeEventListener("pointerup", stop); window.removeEventListener("pointercancel", stop); };
        window.addEventListener("pointermove", move);
        window.addEventListener("pointerup", stop);
        window.addEventListener("pointercancel", stop);
    });
    head.addEventListener("keydown", (event) => {
        const step = event.shiftKey ? 60 : 20, box = win.getBoundingClientRect();
        const to = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] }[event.key];
        if (!to) return;
        event.preventDefault();
        placeInfoWindow(box.left + to[0], box.top + to[1]);
    });
    document.body.appendChild(win);
    return win;
}


export function openInfoWindow(opener, opts) {
    closeInfo(false);
    if (!infoWin) infoWin = buildInfoWindow();
    const fresh = !infoWin.classList.contains("open") || infoWinKey !== opts.key;
    infoWinKey = opts.key;
    infoWinOpener = opener;
    infoWin.setAttribute("aria-label", opts.title || "Notes");
    infoWin.querySelector(".iw-title").textContent = opts.title || "Notes";
    fillInfoWindow(opts.lines);
    infoWin.classList.add("open");
    if (opener) opener.setAttribute("aria-expanded", "true");
    if (fresh) {

        const at = opener ? opener.getBoundingClientRect() : { right: window.innerWidth - 24, bottom: 80 };
        const width = infoWin.getBoundingClientRect().width;
        placeInfoWindow(at.right - width, at.bottom + 8);
        infoWin.focus({ preventScroll: true });
    }
}


export function refreshInfoWindow(key, lines) {
    if (!infoWin || !infoWin.classList.contains("open") || infoWinKey !== key) return;
    if (!lines || lines.length === 0) { closeInfoWindow(false); return; }
    fillInfoWindow(lines);
    const opener = document.querySelector(`#${CSS.escape(key)} .info-badge`);
    if (opener) { infoWinOpener = opener; opener.setAttribute("aria-expanded", "true"); }
}

export function closeInfoWindow(giveFocusBack) {
    if (!infoWin) return;
    infoWin.classList.remove("open");
    const opener = infoWinOpener;
    infoWinKey = "";
    infoWinOpener = null;
    if (opener && opener.isConnected) { opener.setAttribute("aria-expanded", "false"); if (giveFocusBack) opener.focus(); }
}

function hydrateInfos(root) {
    if (!root.querySelectorAll) return;
    root.querySelectorAll("[data-info]:not([data-info-done])").forEach((slot) => {
        slot.setAttribute("data-info-done", "1");
        slot.appendChild(infoBadge({ label: slot.getAttribute("data-info-label") || "More information", text: slot.getAttribute("data-info") }));
    });
}


document.addEventListener("keydown", (event) => {
    if (event.key !== "Escape") return;
    if (infoOwner) { event.preventDefault(); event.stopImmediatePropagation(); closeInfo(true); }
    else if (infoWin && infoWin.classList.contains("open") && infoWin.contains(document.activeElement)) { event.preventDefault(); event.stopImmediatePropagation(); closeInfoWindow(true); }
}, true);
document.addEventListener("pointerdown", (event) => {
    if (infoOwner && !infoOwner.contains(event.target) && !(infoPop && infoPop.contains(event.target))) closeInfo(false);
}, true);

let pop = null;
function showTip(target) {
    if (document.body.classList.contains("is-dragging")) return;
    if (!pop) { pop = document.createElement("div"); pop.className = "guide-pop"; pop.setAttribute("role", "tooltip"); document.body.appendChild(pop); }
    const title = target.dataset.tipTitle, text = target.dataset.tip, preview = target.dataset.tipPreview;
    pop.innerHTML = (title ? `<b>${esc(title)}</b>` : "") + esc(text) + (preview ? `<div class="preview">${icon("eye")}<span>${esc(preview)}</span></div>` : "");
    pop.classList.add("show");
    const box = target.getBoundingClientRect(), size = pop.getBoundingClientRect();
    let left = box.left + box.width / 2 - size.width / 2;
    left = Math.max(8, Math.min(left, window.innerWidth - size.width - 8));
    let top = box.bottom + 8;
    if (top + size.height > window.innerHeight - 8) top = box.top - size.height - 8;
    pop.style.left = left + "px";
    pop.style.top = Math.max(8, top) + "px";
}
export function hideTip() { if (pop) pop.classList.remove("show"); }

const CONTROL = "button, a, select, input, textarea, label, [role=button]";
const tipOf = (node) => {
    const t = node.closest && node.closest("[data-tip]");
    return t && (t.classList.contains("guide-badge") || !t.closest(CONTROL)) ? t : null;
};
document.addEventListener("mouseover", (event) => { const t = tipOf(event.target); if (t) showTip(t); });
document.addEventListener("mouseout", (event) => { const t = tipOf(event.target); if (t && !t.contains(event.relatedTarget)) hideTip(); });
document.addEventListener("focusin", (event) => { const t = tipOf(event.target); if (t) showTip(t); });
document.addEventListener("focusout", hideTip);
document.addEventListener("scroll", hideTip, { capture: true, passive: true });
document.addEventListener("pointerdown", hideTip, true);




const FX = "button:not(.guide-badge):not(.menu-item):not(.group-tab):not(.brand-toggle):not(.pick):not(.assist-next):not(.assist-x), a.btn, .tile, .nav-links a, .kpi, .ingredient-card, .sign-out, .w-tile, .w-card";

function tagFx(root) {
    if (root.matches && root.matches(FX)) root.classList.add("fx");
    if (root.querySelectorAll) root.querySelectorAll(FX).forEach((control) => control.classList.add("fx"));
}

export function playSweep(control) {
    if (!control || control.disabled) return;
    control.classList.remove("fx-run");
    void control.offsetWidth;
    control.classList.add("fx-run");
}

const answersClick = (control) => !!control && (control.matches("button, a[href], [role=button], input, select, summary, label") || getComputedStyle(control).cursor === "pointer");
document.addEventListener("pointerdown", (event) => { const control = event.target.closest && event.target.closest(".fx"); if (answersClick(control)) playSweep(control); });
document.addEventListener("animationend", (event) => { if (event.animationName === "fx-glow") event.target.classList.remove("fx-run"); });






function watchForNewControls() {
    new MutationObserver((changes) => {
        changes.forEach((change) => change.addedNodes.forEach((node) => { if (node.nodeType === 1 && !node.classList.contains("guide-pop")) hydrate(node); }));
    }).observe(document.body, { childList: true, subtree: true });
}
if (document.body) tagFx(document.body);





export function flip(elements, change, duration = 280) {
    const list = Array.from(elements);
    const before = new Map(list.map((el) => [el, el.getBoundingClientRect()]));
    change();
    if (calmMotion()) return;
    list.forEach((el) => {
        if (!el.isConnected) return;
        const was = before.get(el), now = el.getBoundingClientRect();
        const dx = was.left - now.left, dy = was.top - now.top;
        if (Math.abs(dx) < 1 && Math.abs(dy) < 1) return;
        el.animate([{ transform: `translate(${dx}px, ${dy}px)` }, { transform: "translate(0, 0)" }], { duration, easing: "cubic-bezier(.2, .8, .2, 1)" });
    });
}





export function toast(message, kind = "info", action) {
    let host = document.getElementById("toastHost");
    if (!host) { host = document.createElement("div"); host.id = "toastHost"; host.className = "toast-host"; host.setAttribute("aria-live", "polite"); document.body.appendChild(host); }

    const same = Array.from(host.children).find((t) => t.dataset.text === message && t.dataset.kind === kind && !action);
    if (same) { clearTimeout(Number(same.dataset.timer)); same.dataset.timer = String(window.setTimeout(() => same.remove(), kind === "error" ? 8000 : 3500)); return; }
    const box = document.createElement("div");
    box.dataset.text = message; box.dataset.kind = kind;
    box.className = "toast " + kind;
    const text = document.createElement("span");
    text.textContent = message;
    box.appendChild(text);
    if (action) {
        const button = document.createElement("button");
        button.type = "button"; button.className = "row-btn"; button.textContent = action.label;
        button.addEventListener("click", () => { box.remove(); action.run(); });
        box.appendChild(button);
    }
    host.appendChild(box);
    box.dataset.timer = String(window.setTimeout(() => box.remove(), kind === "error" ? 8000 : action ? 7000 : 3500));
}

let openMenuEl = null, closeOpenMenu = null;
export function closeMenu() { if (closeOpenMenu) closeOpenMenu(); }





export function openMenu(anchor, items) {
    const wasMine = openMenuEl && openMenuEl.anchor === anchor;
    closeMenu();
    if (wasMine) return;

    const menu = document.createElement("div");
    menu.className = "menu";
    menu.setAttribute("role", "menu");
    menu.anchor = anchor;
    items.filter((item) => item).forEach((item) => {
        if (item.separator) { const line = document.createElement("div"); line.className = "menu-sep"; menu.appendChild(line); return; }
        if (item.heading) { const head = document.createElement("div"); head.className = "menu-heading"; head.textContent = item.heading; menu.appendChild(head); return; }
        const button = document.createElement("button");
        button.type = "button";
        button.className = "menu-item" + (item.danger ? " danger" : "") + (item.checked ? " checked" : "");
        button.setAttribute("role", "menuitem");
        button.disabled = !!item.disabled;
        button.innerHTML = item.icon ? icon(item.icon) : `<span class="menu-gap"></span>`;
        const text = document.createElement("span");
        text.className = "menu-text";
        const label = document.createElement("span");
        label.textContent = item.label;
        text.appendChild(label);
        if (item.description) { const more = document.createElement("small"); more.textContent = item.description; text.appendChild(more); }
        button.appendChild(text);
        if (item.keys) { button.appendChild(keyChips(item.keys, "kb-hint")); button.setAttribute("aria-keyshortcuts", ariaKeys(item.keys)); }
        if (item.checked) button.insertAdjacentHTML("beforeend", icon("check", "menu-check"));
        button.addEventListener("click", () => { closeMenu(); if (item.run) item.run(); });
        menu.appendChild(button);
    });
    document.body.appendChild(menu);

    const box = anchor.getBoundingClientRect(), size = menu.getBoundingClientRect();
    let left = box.right - size.width;
    if (left < 8) left = Math.min(box.left, window.innerWidth - size.width - 8);
    let top = box.bottom + 6;
    if (top + size.height > window.innerHeight - 8) top = Math.max(8, box.top - size.height - 6);
    menu.style.left = Math.max(8, left) + "px";
    menu.style.top = top + "px";
    menu.classList.add("show");
    anchor.setAttribute("aria-expanded", "true");

    const enabled = () => Array.from(menu.querySelectorAll(".menu-item:not(:disabled)"));
    const onDown = (event) => { if (!menu.contains(event.target) && !anchor.contains(event.target)) closeMenu(); };
    const onKey = (event) => {
        if (event.key === "Escape") { event.stopPropagation(); closeMenu(); anchor.focus(); return; }
        if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return;
        event.preventDefault();
        const list = enabled(), now = list.indexOf(document.activeElement);
        const next = list[(now + (event.key === "ArrowDown" ? 1 : list.length - 1) + (now < 0 && event.key === "ArrowUp" ? 1 : 0)) % list.length];
        if (next) next.focus();
    };
    document.addEventListener("pointerdown", onDown, true);
    document.addEventListener("keydown", onKey, true);
    window.addEventListener("resize", closeMenu);
    openMenuEl = menu;
    closeOpenMenu = () => {
        document.removeEventListener("pointerdown", onDown, true);
        document.removeEventListener("keydown", onKey, true);
        window.removeEventListener("resize", closeMenu);
        anchor.removeAttribute("aria-expanded");
        menu.remove();
        openMenuEl = null; closeOpenMenu = null;
    };
}


export function dialogFrame(title, onDismiss) {
    const overlay = document.createElement("div");
    overlay.className = "modal-overlay";
    overlay.style.display = "flex";
    const box = document.createElement("div");
    box.className = "modal-content";
    box.setAttribute("role", "dialog");
    box.setAttribute("aria-modal", "true");
    const heading = document.createElement("h3");
    heading.textContent = title;
    box.appendChild(heading);
    overlay.appendChild(box);
    document.body.appendChild(overlay);
    const onKey = (event) => { if (event.key === "Escape") { event.stopPropagation(); onDismiss(); } };
    overlay.addEventListener("mousedown", (event) => { if (event.target === overlay) onDismiss(); });
    document.addEventListener("keydown", onKey, true);
    return { overlay, box, close: () => { document.removeEventListener("keydown", onKey, true); overlay.remove(); } };
}






export function askConfirm(options) {
    return new Promise((resolve) => {
        const done = (answer) => { frame.close(); resolve(answer); };
        const frame = dialogFrame(options.title || "Are you sure?", () => done(false));
        if (options.message) { const p = document.createElement("p"); p.className = "modal-intro"; p.textContent = options.message; frame.box.appendChild(p); }
        if (options.items && options.items.length > 0) {
            const list = document.createElement("ul");
            list.className = "confirm-list";
            options.items.slice(0, 12).forEach((name) => { const li = document.createElement("li"); li.textContent = name; list.appendChild(li); });
            if (options.items.length > 12) { const li = document.createElement("li"); li.className = "muted"; li.textContent = `and ${options.items.length - 12} more`; list.appendChild(li); }
            frame.box.appendChild(list);
        }
        const actions = document.createElement("div");
        actions.className = "modal-actions";
        if (options.inline) frame.box.classList.add("confirm-inline");
        else actions.style.marginTop = "18px";
        const cancel = document.createElement("button");
        cancel.type = "button"; cancel.className = "cancel-btn"; cancel.textContent = options.cancelLabel || "Cancel";
        const ok = document.createElement("button");
        ok.type = "button"; ok.className = options.danger ? "btn danger solid" : "save-btn"; ok.textContent = options.confirmLabel || "Confirm";
        // optional icons (Sign out): an icon before each label, and a badge before the title
        if (options.cancelIcon) { cancel.classList.add("has-ic"); cancel.insertAdjacentHTML("afterbegin", icon(options.cancelIcon)); }
        if (options.confirmIcon) { ok.classList.add("has-ic"); ok.insertAdjacentHTML("afterbegin", icon(options.confirmIcon)); }
        if (options.badgeIcon) {
            const head = frame.box.querySelector("h3");
            const wrap = document.createElement("div");
            wrap.className = "confirm-head" + (options.danger ? " danger" : "");
            wrap.innerHTML = `<span class="confirm-badge">${icon(options.badgeIcon)}</span>`;
            const words = document.createElement("div");
            words.className = "confirm-words";
            head.replaceWith(wrap);
            words.appendChild(head);
            const intro = frame.box.querySelector(".modal-intro");
            if (intro) words.appendChild(intro);
            wrap.appendChild(words);
        }
        cancel.addEventListener("click", () => done(false));
        ok.addEventListener("click", () => done(true));
        actions.append(cancel, ok);
        frame.box.appendChild(actions);
        (options.danger ? cancel : ok).focus();
    });
}


export function askText(options) {
    return new Promise((resolve) => {
        const done = (answer) => { frame.close(); resolve(answer); };
        const frame = dialogFrame(options.title || "Name", () => done(null));
        const form = document.createElement("form");
        form.className = "modal-form";
        const field = document.createElement("div");
        field.className = "field";
        const label = document.createElement("label");
        label.textContent = options.label || "Name";
        const input = document.createElement("input");
        input.type = "text"; input.maxLength = options.maxLength || 24; input.value = options.value || ""; input.placeholder = options.placeholder || ""; input.autocomplete = "off";
        label.htmlFor = input.id = "askTextInput";
        field.append(label, input);
        const actions = document.createElement("div");
        actions.className = "modal-actions";
        const cancel = document.createElement("button");
        cancel.type = "button"; cancel.className = "cancel-btn"; cancel.textContent = "Cancel";
        const ok = document.createElement("button");
        ok.type = "submit"; ok.className = "save-btn"; ok.textContent = options.confirmLabel || "Save";
        actions.append(cancel, ok);
        form.append(field, actions);
        frame.box.appendChild(form);
        cancel.addEventListener("click", () => done(null));
        form.addEventListener("submit", (event) => { event.preventDefault(); const text = input.value.replace(/\s+/g, " ").trim(); if (text) done(text); else input.focus(); });
        input.focus(); input.select();
    });
}





export function paginate(items, state) {
    const pages = Math.max(1, Math.ceil(items.length / state.size));
    state.page = Math.min(Math.max(1, state.page || 1), pages);
    const start = (state.page - 1) * state.size;
    return items.slice(start, start + state.size);
}


export function renderPager(host, state, total, onChange, noun = "items") {
    if (!host) return;
    const pages = Math.max(1, Math.ceil(total / state.size));
    if (total <= state.size) { host.innerHTML = ""; return; }
    const from = (state.page - 1) * state.size + 1, to = Math.min(total, state.page * state.size);
    host.innerHTML = `<span class="pager-range">${from}-${to} of ${total} ${esc(noun)}</span>
        <span class="btn-group">
            <button type="button" class="icon-btn" data-go="-1" aria-label="Previous page"${state.page <= 1 ? " disabled" : ""}>${icon("left")}</button>
            <span class="page-now"><span class="page-long">Page ${state.page} of ${pages}</span><span class="page-short">${state.page}/${pages}</span></span>
            <button type="button" class="icon-btn" data-go="1" aria-label="Next page"${state.page >= pages ? " disabled" : ""}>${icon("right")}</button>
        </span>`;
    host.onclick = (event) => {
        const button = event.target.closest("button[data-go]");
        if (!button || button.disabled) return;
        state.page += Number(button.dataset.go);
        onChange();
    };
}
