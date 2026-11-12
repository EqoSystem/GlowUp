










export const REASONS = {
    IN_USE_SAME_PAGE: "IN_USE_SAME_PAGE", ACTION_ALREADY_BOUND: "ACTION_ALREADY_BOUND", BROWSER_RESERVED: "BROWSER_RESERVED",
    OS_RESERVED: "OS_RESERVED", BROWSER_CONFLICT: "BROWSER_CONFLICT", INVALID_FORMAT: "INVALID_FORMAT",
    TEST_NOT_RECEIVED: "TEST_NOT_RECEIVED", SAVE_FAILED: "SAVE_FAILED"
};

const MODIFIER_CODES = new Set(["ControlLeft", "ControlRight", "AltLeft", "AltRight", "ShiftLeft", "ShiftRight", "MetaLeft", "MetaRight", "OSLeft", "OSRight", "AltGraph", "CapsLock", "Fn"]);

const ALLOWED_CODE = /^(Key[A-Z]|Digit[0-9]|F([1-9]|1[0-2])|Comma|Period|Slash|Semicolon|Quote|BracketLeft|BracketRight|Minus|Equal|Backquote|Backslash)$/;


export function isMac(nav) {
    const n = nav || (typeof navigator !== "undefined" ? navigator : {});
    const platform = (n.userAgentData && n.userAgentData.platform) || n.platform || "";
    return /mac/i.test(platform) || (/Macintosh/.test(n.userAgent || "") && !/Mobile/.test(n.userAgent || ""));
}


export function detectBrowser(nav) {
    const n = nav || (typeof navigator !== "undefined" ? navigator : {});
    const ua = n.userAgent || "";
    if (n.brave) return "Brave";
    if (/Edg\//.test(ua)) return "Edge";
    if (/OPR\//.test(ua)) return "Opera";
    if (/Firefox\//.test(ua)) return "Firefox";
    if (/Chrome\//.test(ua)) return "Chrome";
    if (/Safari\//.test(ua)) return "Safari";
    return "your browser";
}





export function comboFromEvent(event) {
    const mods = [];
    if (event.ctrlKey || event.metaKey) mods.push("Ctrl");
    if (event.altKey) mods.push("Alt");
    if (event.shiftKey) mods.push("Shift");
    const code = String(event.code || "");
    if (!code || MODIFIER_CODES.has(code)) return { keys: null, partial: mods.join("+") };
    return { keys: mods.concat(code).join("+"), partial: mods.join("+") };
}


export function parseCombo(keys) {
    if (typeof keys !== "string" || !keys) return null;
    const parts = keys.split("+");
    const code = parts.pop();
    const out = { ctrl: false, alt: false, shift: false, code };
    for (const part of parts) {
        if (part === "Ctrl") out.ctrl = true; else if (part === "Alt") out.alt = true; else if (part === "Shift") out.shift = true; else return null;
    }
    return code ? out : null;
}


export function normaliseCombo(keys) {
    const p = parseCombo(keys);
    if (!p) return null;
    return [p.ctrl && "Ctrl", p.alt && "Alt", p.shift && "Shift", p.code].filter(Boolean).join("+");
}


export function isValidFormat(keys) {
    const p = parseCombo(keys);
    return !!p && (p.ctrl || p.alt || p.shift) && ALLOWED_CODE.test(p.code) && !MODIFIER_CODES.has(p.code)

        && !(p.shift && !p.ctrl && !p.alt && /^(Key|Digit)/.test(p.code));
}





export const BROWSER_RESERVED = {
    "Ctrl+KeyT": "opening a new tab", "Ctrl+KeyW": "closing the tab", "Ctrl+KeyN": "opening a new window", "Ctrl+KeyQ": "quitting the browser",
    "Ctrl+Tab": "switching tabs", "Ctrl+Shift+KeyN": "opening a private window", "Ctrl+Shift+KeyT": "reopening a closed tab",
    "Ctrl+Shift+KeyW": "closing the window", "Ctrl+Shift+Tab": "switching tabs", "Shift+Delete": "deleting a suggestion",
    "Ctrl+KeyR": "reloading the page", "F5": "reloading the page", "Ctrl+KeyL": "going to the address bar"
};

export const OS_RESERVED = {
    "Alt+F4": "closing the window", "Alt+Tab": "switching apps", "Ctrl+Alt+Delete": "the security screen",
    "Ctrl+Space": "Spotlight search on a Mac", "Ctrl+KeyH": "hiding the window on a Mac", "Ctrl+KeyM": "minimising the window on a Mac",
    "Ctrl+Alt+KeyT": "opening a terminal on Linux", "Ctrl+Alt+ArrowUp": "turning the screen on some computers"
};





export const BROWSER_CONFLICT = {
    "Ctrl+Shift+KeyB": [{ browser: "Chromium", behavior: "shows or hides the bookmarks bar", overridable: true }, { browser: "Firefox", behavior: "opens the bookmarks library", overridable: false }],
    "Ctrl+Shift+KeyP": [{ browser: "Firefox", behavior: "opens a private window", overridable: false }, { browser: "Edge", behavior: "opens a private window", overridable: false }],
    "Ctrl+Shift+KeyI": [{ browser: "all", behavior: "opens the developer tools", overridable: false }],
    "Ctrl+Shift+KeyJ": [{ browser: "all", behavior: "opens the developer console", overridable: false }],
    "Ctrl+Shift+KeyC": [{ browser: "all", behavior: "opens the element inspector", overridable: false }],
    "Ctrl+Shift+KeyO": [{ browser: "Chromium", behavior: "opens the bookmark manager", overridable: true }],
    "Ctrl+Shift+KeyA": [{ browser: "Chromium", behavior: "opens tab search", overridable: true }, { browser: "Firefox", behavior: "opens the add-ons page", overridable: true }],
    "Ctrl+Shift+KeyDelete": [{ browser: "all", behavior: "clears the browsing data", overridable: false }],
    "Ctrl+KeyS": [{ browser: "all", behavior: "saves the page", overridable: true }],
    "Ctrl+KeyP": [{ browser: "all", behavior: "prints the page", overridable: true }],
    "Ctrl+KeyF": [{ browser: "all", behavior: "finds text on the page", overridable: true }],
    "Ctrl+KeyG": [{ browser: "all", behavior: "finds the next match", overridable: true }],
    "Ctrl+KeyD": [{ browser: "all", behavior: "bookmarks the page", overridable: true }],
    "Ctrl+KeyH": [{ browser: "all", behavior: "opens the history", overridable: true }],
    "Ctrl+KeyJ": [{ browser: "all", behavior: "opens the downloads", overridable: true }],
    "Ctrl+KeyK": [{ browser: "all", behavior: "goes to the search bar", overridable: true }],
    "Ctrl+KeyE": [{ browser: "all", behavior: "goes to the search bar", overridable: true }],
    "Ctrl+KeyO": [{ browser: "all", behavior: "opens a file", overridable: true }],
    "Ctrl+KeyU": [{ browser: "all", behavior: "shows the page source", overridable: true }],
    "Ctrl+KeyB": [{ browser: "Firefox", behavior: "opens the bookmarks sidebar", overridable: true }],
    "Ctrl+KeyI": [{ browser: "Firefox", behavior: "opens the page info", overridable: true }],
    "Alt+KeyF": [{ browser: "all", behavior: "opens the browser menu", overridable: false }],
    "Alt+KeyE": [{ browser: "all", behavior: "opens the browser menu", overridable: false }],
    "Alt+KeyD": [{ browser: "all", behavior: "goes to the address bar", overridable: false }],
    "Alt+KeyV": [{ browser: "Firefox", behavior: "opens the View menu", overridable: false }],
    "Alt+KeyS": [{ browser: "Firefox", behavior: "opens the History menu", overridable: false }],
    "Alt+KeyB": [{ browser: "Firefox", behavior: "opens the Bookmarks menu", overridable: false }],
    "Alt+KeyT": [{ browser: "Firefox", behavior: "opens the Tools menu", overridable: false }],
    "Alt+KeyH": [{ browser: "Firefox", behavior: "opens the Help menu", overridable: false }],
    "F1": [{ browser: "all", behavior: "opens the browser help", overridable: true }],
    "F3": [{ browser: "all", behavior: "finds the next match", overridable: true }],
    "F6": [{ browser: "all", behavior: "goes to the address bar", overridable: false }],
    "F7": [{ browser: "all", behavior: "turns on caret browsing", overridable: true }],
    "F11": [{ browser: "all", behavior: "switches to full screen", overridable: false }],
    "F12": [{ browser: "all", behavior: "opens the developer tools", overridable: false }]
};

for (let n = 1; n <= 9; n++) BROWSER_RESERVED["Ctrl+Digit" + n] = "switching to tab " + n;





export const APP_KEYS = {
    "Ctrl+KeyK": "Command palette", "Ctrl+KeyZ": "Undo a layout change", "Ctrl+KeyY": "Redo a layout change",
    "Ctrl+Shift+KeyZ": "Redo a layout change", "Ctrl+KeyA": "Select every entry of a list"
};

const CHROMIUM = new Set(["Chrome", "Edge", "Brave", "Opera"]);
const appliesTo = (entry, browser) => entry.browser === "all" || entry.browser === browser || (entry.browser === "Chromium" && CHROMIUM.has(browser));




const PUNCTUATION = { Comma: ",", Period: ".", Slash: "/", Semicolon: ";", Quote: "'", BracketLeft: "[", BracketRight: "]", Minus: "-", Equal: "=", Backquote: "`", Backslash: "\\" };
const NAMED = { Tab: "Tab", Delete: "Delete", Space: "Space", ArrowUp: "↑", ArrowDown: "↓", ArrowLeft: "←", ArrowRight: "→", Escape: "Esc", Enter: "Enter", Backspace: "Backspace" };
let layoutMap = null;


export async function learnLayout(nav) {
    const n = nav || (typeof navigator !== "undefined" ? navigator : {});
    try { if (n.keyboard && n.keyboard.getLayoutMap) layoutMap = await n.keyboard.getLayoutMap(); } catch (error) { layoutMap = null; }
    return layoutMap;
}
export function setLayoutMap(map) { layoutMap = map || null; }


export function keyLabel(code) {
    const own = layoutMap && typeof layoutMap.get === "function" ? layoutMap.get(code) : null;
    if (own && own.trim()) return own.length === 1 ? own.toUpperCase() : own;
    if (/^Key[A-Z]$/.test(code)) return code.slice(3);
    if (/^Digit[0-9]$/.test(code)) return code.slice(5);
    if (/^F\d{1,2}$/.test(code)) return code;
    return PUNCTUATION[code] || NAMED[code] || code.replace(/(Left|Right)$/, "");
}


export function comboLabels(keys, options = {}) {
    const p = parseCombo(keys);
    if (!p) return [];
    const mac = options.mac !== undefined ? options.mac : isMac();
    const out = [];
    if (p.ctrl) out.push(mac ? "⌘" : "Ctrl");
    if (p.alt) out.push(mac ? "⌥" : "Alt");
    if (p.shift) out.push(mac ? "⇧" : "Shift");
    out.push(keyLabel(p.code));
    return out;
}


export function comboText(keys, options = {}) {
    const mac = options.mac !== undefined ? options.mac : isMac();
    return comboLabels(keys, { mac }).join(mac ? "" : "+");
}


export function ariaKeys(keys, options = {}) {
    const p = parseCombo(keys);
    if (!p) return "";
    const mac = options.mac !== undefined ? options.mac : isMac();
    return [p.ctrl && (mac ? "Meta" : "Control"), p.alt && "Alt", p.shift && "Shift", keyLabel(p.code)].filter(Boolean).join("+");
}










export function checkCombo({ keys, actionId, scope, shortcuts = [], actions = {}, pages = {}, browser = "Chrome", mac }) {
    const out = [];
    const shown = comboText(keys, { mac });
    const combo = normaliseCombo(keys);
    if (!combo) return [{ code: REASONS.INVALID_FORMAT, level: "block", text: formatText() }];
    if (BROWSER_RESERVED[combo]) return [{ code: REASONS.BROWSER_RESERVED, level: "block", text: `${shown} is reserved by your browser for ${BROWSER_RESERVED[combo]}, so GlowUp can't use it. Try a combo with ${mac ?? isMac() ? "⌘⌥" : "Ctrl+Alt"}.` }];
    if (OS_RESERVED[combo]) return [{ code: REASONS.OS_RESERVED, level: "block", text: `${shown} is used by your operating system for ${OS_RESERVED[combo]}. Try another combo.` }];
    if (!isValidFormat(combo)) return [{ code: REASONS.INVALID_FORMAT, level: "block", text: formatText() }];
    if (APP_KEYS[combo]) return [{ code: REASONS.IN_USE_SAME_PAGE, level: "block", owner: APP_KEYS[combo], text: `${shown} is already used for '${APP_KEYS[combo]}' on every GlowUp page. Choose another combo.` }];

    const pageName = (s) => pages[s] || s;
    const labelOf = (id) => (actions[id] && actions[id].label) || id;
    const sameScope = (s) => s === scope || s === "all" || scope === "all";
    const others = shortcuts.filter((s) => s.actionId !== actionId && normaliseCombo(s.keys) === combo);
    const clash = others.find((s) => sameScope(s.pageScope));
    if (clash) out.push({ code: REASONS.IN_USE_SAME_PAGE, level: "warn", owner: clash.actionId, replaceable: true,
        text: `${shown} is already used for '${labelOf(clash.actionId)}' on ${pageName(clash.pageScope === "all" ? scope : clash.pageScope)}${clash.pageScope === "all" ? " (it works on every page)" : ""}.` });
    others.filter((s) => !sameScope(s.pageScope)).forEach((s) => out.push({ code: REASONS.IN_USE_SAME_PAGE, level: "info", owner: s.actionId,
        text: `Also used on ${pageName(s.pageScope)} for '${labelOf(s.actionId)}'. That's fine; shortcuts only work on their own page.` }));

    const own = shortcuts.find((s) => s.actionId === actionId && s.keys && normaliseCombo(s.keys) !== combo);
    if (own) out.push({ code: REASONS.ACTION_ALREADY_BOUND, level: "warn", text: `'${labelOf(actionId)}' currently uses ${comboText(own.keys, { mac })}. Saving will replace it.` });

    const conflicts = (BROWSER_CONFLICT[combo] || []);
    if (conflicts.length > 0) {
        const mine = conflicts.filter((c) => appliesTo(c, browser));
        const first = mine[0] || conflicts[0];
        const where = first.browser === "all" ? "every browser" : first.browser === "Chromium" ? "Chrome, Edge and Brave" : first.browser;
        const more = conflicts.filter((c) => c !== first).map((c) => (c.browser === "Chromium" ? "Chrome, Edge and Brave" : c.browser === "all" ? "every browser" : c.browser) + " " + c.behavior);
        const lead = mine.length > 0 ? `In ${browser}, ${shown} ${first.behavior}` : `${shown} ${first.behavior} in ${where}`;
        const ends = first.overridable ? "GlowUp usually gets it first, but not always." : "It won't work in " + (mine.length > 0 ? browser : where) + ".";
        out.push({ code: REASONS.BROWSER_CONFLICT, level: "warn", text: `${lead}${more.length ? " (and in " + more.join("; ") + ")" : ""}. ${ends}` });
    }
    return out;
}


export function formatText() {
    return "Add a letter or number key with Ctrl, Alt, or Shift. Single keys aren't allowed so shortcuts don't fire while you type.";
}













export function mergeWithCatalog(stored, catalog, now = Date.now()) {
    const items = (stored && stored.items && typeof stored.items === "object") ? stored.items : {};
    const seeded = new Set(Array.isArray(stored && stored.seeded) ? stored.seeded : []);
    const byId = new Map(catalog.map((a) => [a.id, a]));
    const list = [], orphans = [];
    Object.keys(items).forEach((id) => {
        const action = byId.get(id), row = items[id];
        if (!action) { orphans.push(id); return; }
        if (!row || typeof row !== "object") return;
        const keys = normaliseCombo(row.keys);
        if (!keys || !isValidFormat(keys)) return;
        list.push({ id, actionId: id, pageScope: action.scope, keys, description: String(row.description || action.description || "").slice(0, 120),
            enabled: row.enabled !== false, isDefault: !!row.isDefault, conflictFlag: !!row.conflictFlag, createdAt: row.createdAt || now, updatedAt: row.updatedAt || now });
    });
    const added = [];
    catalog.forEach((action) => {
        if (!action.keys || seeded.has(action.id)) return;
        seeded.add(action.id);
        if (list.some((s) => s.actionId === action.id)) return;
        const busy = list.some((s) => s.keys === action.keys && (s.pageScope === action.scope || s.pageScope === "all" || action.scope === "all"));
        if (busy) return;
        list.push({ id: action.id, actionId: action.id, pageScope: action.scope, keys: action.keys, description: action.description, enabled: true, isDefault: true, conflictFlag: false, createdAt: now, updatedAt: now });
        added.push(action.id);
    });
    return { list, orphans, added, seeded: Array.from(seeded) };
}


export function defaultList(catalog, now = Date.now()) {
    return mergeWithCatalog(null, catalog, now).list;
}


export function toStored(list, seeded) {
    const items = {};
    list.forEach((s) => { items[s.actionId] = { keys: s.keys, description: s.description, enabled: !!s.enabled, conflictFlag: !!s.conflictFlag, isDefault: !!s.isDefault, pageScope: s.pageScope, createdAt: s.createdAt, updatedAt: s.updatedAt }; });
    return { items, seeded: Array.from(new Set(seeded || [])) };
}


export function findShortcut(list, scope, keys) {
    const combo = normaliseCombo(keys);
    if (!combo) return null;
    const live = list.filter((s) => s.enabled && s.keys === combo);
    return live.find((s) => s.pageScope === scope) || live.find((s) => s.pageScope === "all") || null;
}





export function testVerdict(received, keys, browser, options = {}) {
    if (received) return { code: null, text: `✓ Works in ${browser}.` };
    return { code: REASONS.TEST_NOT_RECEIVED, text: `GlowUp didn't receive ${comboText(keys, options)}. Your browser, operating system, or a browser extension is probably using it. Try another combo.` };
}


export function saveFailedText(error, online = true) {
    const code = String((error && error.code) || "");
    if (/permission-denied|unauthenticated/.test(code)) return "Only admins can change keyboard shortcuts. Sign in again with an admin account.";
    if (!online || code === "unavailable") return "You're offline. Shortcuts can be changed once you're back online.";
    return "Couldn't save. Check your connection and try again.";
}





export function availabilityOf(action, scope, handler, pageName = (s) => s) {
    if (!action) return "This action no longer exists.";
    if (action.scope !== "all" && action.scope !== scope) return `This works on ${pageName(action.scope)}.`;
    if (!handler) return `${action.label} isn't ready yet. Try again in a moment.`;
    try {
        const answer = handler.isAvailable ? handler.isAvailable() : true;
        return answer === true || answer === undefined ? true : String(answer || `${action.label} isn't available right now.`);
    } catch (error) { return `${action.label} isn't available right now.`; }
}
