// KEYBOARD SHORTCUTS, at run time (9 November 2026): the one key listener, the "?" help and the command palette (Ctrl/Cmd+K).
//
// startShortcuts(scope) is called once per admin page (adminShell.mountShell). A page gives its actions their handlers with
// bindActions({ actionId: { run, isAvailable } }) or bindButtons({ actionId: "#buttonId" }); a bound BUTTON is the one source of
// truth: the shortcut clicks it, and only when it is there, shown and not disabled - the same conditions as for the mouse.
//
// The listener does nothing when: the device has no fine pointer (touch only), focus is in a box you type in, a dialog / menu /
// panel / the palette is open, a combo is being recorded or tested, the key is held down (event.repeat), or the account was
// refused as an admin. preventDefault() is called ONLY when a shortcut matches and runs. An action that cannot run now shows ONE
// short toast that says why (toast() in adminShell.js does not repeat a message that is already shown).
import { toast, icon, closeMenu } from "./adminShell.js";
import { CATALOG, BUILT_IN, PAGES, PAGE_ORDER, PAGE_URLS, catalogById } from "./shortcutCatalog.js";
import { comboFromEvent, comboLabels, comboText, ariaKeys, findShortcut, learnLayout, isMac, availabilityOf } from "./shortcutKeys.js";
import { loadShortcuts, onShortcuts, shortcutState } from "./shortcutStore.js";

let scope = "", started = false, recording = false;
const handlers = new Map();               // actionId -> { run, isAvailable }
const buttons = new Map();                // actionId -> selector of its on-screen button

export const shortcutsActive = () => started && !shortcutState().denied && desktop();
const desktop = () => !window.matchMedia || window.matchMedia("(hover: hover) and (pointer: fine)").matches;
/** While true (recording or testing a combo in Quick settings) no shortcut runs. */
export function setRecording(on) { recording = !!on; }

// ---------------------------------------------------------------------------------------------
// Actions of the page
// ---------------------------------------------------------------------------------------------
/** Gives actions of this page their handler. isAvailable() returns true, or the reason (a sentence) why it cannot run now. */
export function bindActions(map) {
    Object.keys(map).forEach((id) => handlers.set(id, map[id]));
    markButtons();
}
/** Binds actions to on-screen buttons: the shortcut presses the button, only when the button could be pressed. */
export function bindButtons(map) {
    Object.keys(map).forEach((id) => {
        const selector = map[id];
        buttons.set(id, selector);
        handlers.set(id, {
            run: () => { const b = document.querySelector(selector); if (b) { b.focus({ preventScroll: true }); b.click(); } },
            isAvailable: () => buttonReady(selector, catalogById.get(id))
        });
    });
    markButtons();
}
function buttonReady(selector, action) {
    const b = document.querySelector(selector);
    const label = action ? action.label : "This action";
    const off = !b || b.getClientRects().length === 0 || b.disabled || b.getAttribute("aria-disabled") === "true";
    if (!off) return true;
    // the button is hidden or greyed out: the reason the admin can act on
    if (/selected/i.test(label)) return `${label}: tick at least one entry first.`;
    if (/approve/i.test(label)) return `${label}: tick at least one draft as reviewed first.`;
    return !b || b.getClientRects().length === 0 ? `${label} isn't on the page right now.` : `${label} isn't available right now.`;
}
/** Can this action run here and now? true or the reason. */
export function availability(actionId) {
    return availabilityOf(catalogById.get(actionId), scope, handlers.get(actionId), (p) => PAGES[p] || p);
}
/** Runs an action through the same handler as its button. Returns true when it ran. */
export function runAction(actionId) {
    const ok = availability(actionId);
    if (ok !== true) { toast(String(ok), "info"); return false; }
    closeMenu();
    handlers.get(actionId).run();
    return true;
}
/** The admin's combo for an action (enabled ones only), or "". */
export function comboFor(actionId) {
    const s = shortcutState().list.find((x) => x.actionId === actionId && x.enabled);
    return s ? s.keys : "";
}

/** aria-keyshortcuts on every bound button (no hover label: the site shows none on controls). */
function markButtons() {
    buttons.forEach((selector, id) => {
        const b = document.querySelector(selector);
        if (!b) return;
        const keys = comboFor(id);
        if (keys && desktop()) b.setAttribute("aria-keyshortcuts", ariaKeys(keys)); else b.removeAttribute("aria-keyshortcuts");
    });
}

// ---------------------------------------------------------------------------------------------
// Key chips (ONE look everywhere: list, dialog, help, palette, menus)
// ---------------------------------------------------------------------------------------------
/** <span class="kb-combo"><kbd>Ctrl</kbd><kbd>Alt</kbd><kbd>B</kbd></span>; the text for screen readers is the same formatter's. */
export function keyChips(keys, extraClass = "") {
    const box = document.createElement("span");
    box.className = "kb-combo" + (extraClass ? " " + extraClass : "");
    const labels = /^[?/]$|^Esc$/.test(keys) ? [keys] : comboLabels(keys);
    box.setAttribute("aria-label", /^[?/]$|^Esc$/.test(keys) ? keys : comboText(keys));
    labels.forEach((label) => { const k = document.createElement("kbd"); k.className = "kb-key"; k.textContent = label; k.setAttribute("aria-hidden", "true"); box.appendChild(k); });
    return box;
}

/** A message with every combo in it ("Ctrl+Alt+B", "⌘⌥B", "F5") drawn as key chips; the rest stays plain text. */
export function textWithChips(text) {
    const out = document.createDocumentFragment();
    const pattern = /((?:Ctrl|Alt|Shift)\+)+[^\s,.'()]+|[⌘⌥⇧]+[^\s,.'()]+|\bF\d{1,2}\b/g;
    let at = 0, m;
    while ((m = pattern.exec(text))) {
        if (m.index > at) out.appendChild(document.createTextNode(text.slice(at, m.index)));
        const chips = document.createElement("span");
        chips.className = "kb-combo";
        chips.setAttribute("aria-label", m[0]);
        const parts = /^[⌘⌥⇧]/.test(m[0]) ? Array.from(m[0].match(/[⌘⌥⇧]|[^⌘⌥⇧]+/g)) : m[0].split("+");
        parts.forEach((label) => { const k = document.createElement("kbd"); k.className = "kb-key"; k.textContent = label; k.setAttribute("aria-hidden", "true"); chips.appendChild(k); });
        out.appendChild(chips);
        at = m.index + m[0].length;
    }
    if (at < text.length) out.appendChild(document.createTextNode(text.slice(at)));
    return out;
}

// ---------------------------------------------------------------------------------------------
// A layer (the help and the palette): focus moves in and is kept inside, Esc / the X / the dark area close it, focus goes back
// ---------------------------------------------------------------------------------------------
/** opts = { title, className, onClose, labelledBy }. Returns { overlay, box, body, close }. */
export function openLayer({ title, className = "", onClose }) {
    const opener = document.activeElement;
    const overlay = document.createElement("div");
    overlay.className = "modal-overlay kb-layer";
    overlay.style.display = "flex";
    const box = document.createElement("div");
    box.className = "modal-content xl kb-modal " + className;
    box.setAttribute("role", "dialog");
    box.setAttribute("aria-modal", "true");
    const headId = "kbHead" + Math.random().toString(36).slice(2, 8);
    box.setAttribute("aria-labelledby", headId);
    box.innerHTML = `<div class="xl-head"><div class="xl-titles"><h3 id="${headId}"></h3></div><button type="button" class="icon-btn small" data-x aria-label="Close">${icon("x")}</button></div><div class="xl-body"></div>`;
    box.querySelector("h3").textContent = title;
    overlay.appendChild(box);
    document.body.appendChild(overlay);
    let closed = false;
    const close = () => {
        if (closed) return;
        closed = true;
        document.removeEventListener("keydown", onKey, true);
        overlay.remove();
        if (onClose) onClose();
        if (opener && opener.isConnected && typeof opener.focus === "function") opener.focus({ preventScroll: true });
    };
    const onKey = (event) => {
        const top = Array.from(document.querySelectorAll(".modal-overlay")).filter((o) => o.style.display === "flex" || getComputedStyle(o).display !== "none").pop();
        if (top !== overlay) return;
        if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); close(); return; }
        if (event.key === "Tab") trapTab(event, box);
    };
    document.addEventListener("keydown", onKey, true);
    overlay.addEventListener("mousedown", (event) => { if (event.target === overlay) close(); });
    box.querySelector("[data-x]").addEventListener("click", close);
    return { overlay, box, body: box.querySelector(".xl-body"), close };
}
/** Keeps Tab inside `box`. */
export function trapTab(event, box) {
    const all = Array.from(box.querySelectorAll('button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])')).filter((n) => !n.disabled && n.getClientRects().length > 0);
    if (all.length === 0) return;
    const first = all[0], last = all[all.length - 1];
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    else if (!box.contains(document.activeElement)) { event.preventDefault(); first.focus(); }
}

// ---------------------------------------------------------------------------------------------
// "?": the shortcuts of this page
// ---------------------------------------------------------------------------------------------
let helpOpen = null;
export function openHelp() {
    if (helpOpen) return;
    const layer = openLayer({ title: "Keyboard shortcuts", className: "kb-help", onClose: () => { helpOpen = null; } });
    helpOpen = layer;
    const intro = document.createElement("p");
    intro.className = "modal-intro";
    intro.textContent = `On ${PAGES[scope] || "this page"}. Change them in Quick settings › Keyboard shortcuts.`;
    layer.body.appendChild(intro);
    const mine = shortcutState().list.filter((s) => s.enabled && (s.pageScope === scope || s.pageScope === "all"));
    const group = (title, rows) => {
        const h = document.createElement("div"); h.className = "menu-heading"; h.textContent = title; layer.body.appendChild(h);
        const list = document.createElement("dl"); list.className = "kb-help-list";
        if (rows.length === 0) { const p = document.createElement("p"); p.className = "field-hint"; p.textContent = "No shortcuts here yet. Add one in Quick settings › Keyboard shortcuts."; layer.body.appendChild(p); return; }
        rows.forEach((r) => {
            const dt = document.createElement("dt"); dt.appendChild(keyChips(r.keys));
            const dd = document.createElement("dd");
            const name = document.createElement("b"); name.textContent = r.label;
            const about = document.createElement("span"); about.textContent = r.description;
            dd.append(name, about); list.append(dt, dd);
        });
        layer.body.appendChild(list);
    };
    const label = (s) => (catalogById.get(s.actionId) || {}).label || s.actionId;
    group(PAGES[scope] || "This page", mine.filter((s) => s.pageScope === scope).map((s) => ({ keys: s.keys, label: label(s), description: s.description })));
    group("Every page", mine.filter((s) => s.pageScope === "all").map((s) => ({ keys: s.keys, label: label(s), description: s.description })));
    group("Built in", BUILT_IN.map((b) => ({ keys: b.keys, label: b.label, description: b.description })));
    layer.box.querySelector("[data-x]").focus();
}

// ---------------------------------------------------------------------------------------------
// Ctrl/Cmd+K: the command palette
// ---------------------------------------------------------------------------------------------
let paletteOpen = null;
export function openPalette() {
    if (paletteOpen) return;
    const layer = openLayer({ title: "Command palette", className: "kb-palette", onClose: () => { paletteOpen = null; } });
    paletteOpen = layer;
    layer.overlay.classList.add("kb-top");
    const search = document.createElement("input");
    search.type = "search"; search.className = "kb-search"; search.placeholder = "Search pages and actions"; search.autocomplete = "off";
    search.setAttribute("aria-label", "Search pages and actions");
    search.setAttribute("role", "combobox"); search.setAttribute("aria-expanded", "true");
    const list = document.createElement("div");
    list.className = "kb-results"; list.setAttribute("role", "listbox"); list.id = "kbResults";
    search.setAttribute("aria-controls", list.id);
    const empty = document.createElement("p");
    empty.className = "field-hint kb-empty"; empty.textContent = "Nothing found. Try another word."; empty.hidden = true;
    layer.body.append(search, list, empty);

    const entries = [];
    PAGE_ORDER.filter((p) => PAGE_URLS[p]).forEach((p) => entries.push({ kind: "page", id: "nav." + p, label: PAGES[p], hint: p === scope ? "You are here" : "Page", here: p === scope }));
    CATALOG.filter((a) => !a.id.startsWith("nav.")).forEach((a) => entries.push({ kind: "action", id: a.id, label: a.label, hint: PAGES[a.scope], action: a }));
    let at = 0, shown = [];
    const paint = () => {
        const words = search.value.toLowerCase().split(/\s+/).filter(Boolean);
        shown = entries.filter((e) => words.every((w) => (e.label + " " + e.hint).toLowerCase().includes(w)));
        // this page's actions first, then pages, then other pages' actions
        const rank = (e) => (e.kind === "action" && (e.action.scope === scope || e.action.scope === "all") ? 0 : e.kind === "page" ? 1 : 2);
        shown.sort((a, b) => rank(a) - rank(b));
        at = Math.min(at, Math.max(0, shown.length - 1));
        list.replaceChildren(...shown.map((e, i) => {
            const row = document.createElement("div");
            row.className = "kb-result"; row.id = "kbr" + i; row.setAttribute("role", "option");
            const reason = e.kind === "action" ? availability(e.id) : true;
            const otherPage = e.kind === "action" && e.action.scope !== "all" && e.action.scope !== scope;
            const blocked = reason !== true && !otherPage;
            if (blocked) { row.classList.add("is-off"); row.setAttribute("aria-disabled", "true"); }
            if (i === at) { row.classList.add("is-at"); row.setAttribute("aria-selected", "true"); search.setAttribute("aria-activedescendant", row.id); }
            row.innerHTML = icon(e.kind === "page" ? "right" : (e.action && e.action.destructive ? "trash" : "sparkle"));
            const text = document.createElement("span"); text.className = "menu-text";
            const name = document.createElement("span"); name.textContent = e.label;
            const small = document.createElement("small");
            small.textContent = blocked ? String(reason) : otherPage ? `On ${PAGES[e.action.scope]}: opens that page first.` : e.hint;
            text.append(name, small); row.appendChild(text);
            const keys = comboFor(e.kind === "page" ? e.id : e.id);
            if (keys) row.appendChild(keyChips(keys, "kb-hint"));
            row.addEventListener("mousemove", () => { if (at !== i) { at = i; paint(); } });
            row.addEventListener("click", () => choose(e));
            return row;
        }));
        empty.hidden = shown.length > 0;
        const current = list.querySelector(".is-at");
        if (current) current.scrollIntoView({ block: "nearest" });
    };
    const choose = (e) => {
        if (!e) return;
        if (e.kind === "page") { layer.close(); if (!e.here) window.location.href = PAGE_URLS[e.id.slice(4)]; return; }
        const a = e.action;
        if (a.scope !== "all" && a.scope !== scope) { layer.close(); window.location.href = PAGE_URLS[a.scope] + "?do=" + encodeURIComponent(a.id); return; }
        const reason = availability(a.id);
        if (reason !== true) { toast(String(reason), "info"); return; }
        layer.close();
        runAction(a.id);
    };
    search.addEventListener("input", () => { at = 0; paint(); });
    search.addEventListener("keydown", (event) => {
        if (event.key === "ArrowDown") { event.preventDefault(); at = Math.min(shown.length - 1, at + 1); paint(); }
        else if (event.key === "ArrowUp") { event.preventDefault(); at = Math.max(0, at - 1); paint(); }
        else if (event.key === "Enter") { event.preventDefault(); choose(shown[at]); }
    });
    paint();
    search.focus();
}

// ---------------------------------------------------------------------------------------------
// The one listener
// ---------------------------------------------------------------------------------------------
const TYPING = "input, textarea, select, [contenteditable]:not([contenteditable=false])";
/** True while something sits over the page (a dialog, a menu, the palette, an open panel of the page). */
export function layerOpen() {
    return !!document.querySelector(".modal-overlay[style*='flex'], .kb-layer, .menu.show, .import-check, .style-overlay, .facet-panel, .snapx-open")
        || document.body.classList.contains("snapx-open")
        || (document.documentElement.classList.contains("qs-open") && window.matchMedia("(max-width: 1023px)").matches)
        || document.documentElement.classList.contains("sb-peek");
}

function onKey(event) {
    if (!shortcutsActive() || recording || event.repeat || event.isComposing) return;
    const target = event.target;
    if (target && target.closest && target.closest(TYPING)) return;
    if (layerOpen()) return;
    // ? : the help (event.key, so it works on every keyboard layout)
    if (event.key === "?" && !event.ctrlKey && !event.metaKey && !event.altKey) { event.preventDefault(); openHelp(); return; }
    // Ctrl/Cmd+K : the palette
    if ((event.ctrlKey || event.metaKey) && !event.altKey && !event.shiftKey && event.code === "KeyK") { event.preventDefault(); openPalette(); return; }
    // / : this page's search box, where the page has not taken it already
    if (event.key === "/" && !event.ctrlKey && !event.metaKey && !event.altKey && !event.defaultPrevented) {
        const box = document.querySelector(".topbar .search-container input, .topbar input[type=search], header input[type=search]");
        if (box && box.getClientRects().length > 0) { event.preventDefault(); box.focus(); box.select(); }
        return;
    }
    const { keys } = comboFromEvent(event);
    if (!keys) return;
    const hit = findShortcut(shortcutState().list, scope, keys);
    if (!hit) return;
    event.preventDefault();
    runAction(hit.actionId);
}

/** Once per admin page, after the sign-in check. `page` = "dashboard" | "inventory" | "curator" | "bin" | "archive". */
export function startShortcuts(page) {
    if (started) return;
    started = true;
    scope = page;
    learnLayout().then(markButtons);
    // going to another page: the same way as the sidebar link (the page's own leave-guard runs on the way out)
    const go = (p) => () => { if (p === scope) return; window.location.href = PAGE_URLS[p]; };
    bindActions({
        "nav.dashboard": { run: go("dashboard") }, "nav.inventory": { run: go("inventory") }, "nav.curator": { run: go("curator") },
        "nav.bin": { run: go("bin") }, "nav.archive": { run: go("archive") },
        "app.quickSettings": { run: () => { const b = document.getElementById("quickSettingsBtn"); if (b) b.click(); }, isAvailable: () => (document.getElementById("quickSettingsBtn") ? true : "Quick settings isn't on this page.") }
    });
    document.addEventListener("keydown", onKey);
    onShortcuts(() => markButtons());
    loadShortcuts().then(runAskedAction);
}

/** ?do=<actionId> (from the palette on another page): run it once its page is ready (at most 8 seconds), then drop it from the address. */
function runAskedAction() {
    const params = new URLSearchParams(location.search);
    const id = params.get("do");
    if (!id) return;
    params.delete("do");
    history.replaceState(history.state, "", location.pathname + (params.toString() ? "?" + params : "") + location.hash);
    const action = catalogById.get(id);
    if (!action || action.scope !== scope) return;
    const until = Date.now() + 8000;
    const tryIt = () => {
        if (availability(id) === true && !layerOpen()) { runAction(id); return; }
        if (Date.now() < until) setTimeout(tryIt, 250); else runAction(id);       // the last try says why it could not run
    };
    tryIt();
}

export { isMac };
