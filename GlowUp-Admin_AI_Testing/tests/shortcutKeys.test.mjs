


import * as K from "../_Shared/shortcutKeys.js";
import { CATALOG, PAGES } from "../_Shared/shortcutCatalog.js";

let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };
const eq = (a, b, what) => ok(JSON.stringify(a) === JSON.stringify(b), `${what}: got ${JSON.stringify(a)}, wanted ${JSON.stringify(b)}`);
const ev = (code, mods = {}) => ({ code, ctrlKey: !!mods.ctrl, metaKey: !!mods.meta, altKey: !!mods.alt, shiftKey: !!mods.shift });
const actions = Object.fromEntries(CATALOG.map((a) => [a.id, a]));
const check = (keys, actionId, scope, shortcuts = [], browser = "Chrome", mac = false) => K.checkCombo({ keys, actionId, scope, shortcuts, actions, pages: PAGES, browser, mac });
const codes = (list) => list.map((m) => m.code + ":" + m.level);


eq(K.comboFromEvent(ev("KeyB", { ctrl: true, alt: true })).keys, "Ctrl+Alt+KeyB", "Ctrl+Alt+B");
eq(K.comboFromEvent(ev("KeyB", { meta: true, alt: true })).keys, "Ctrl+Alt+KeyB", "Cmd counts as Ctrl");
eq(K.comboFromEvent(ev("KeyB", { shift: true, alt: true, ctrl: true })).keys, "Ctrl+Alt+Shift+KeyB", "modifier order is fixed");
eq(K.comboFromEvent(ev("AltLeft", { alt: true, ctrl: true })), { keys: null, partial: "Ctrl+Alt" }, "modifiers only: no combo yet, live partial");
eq(K.normaliseCombo("Shift+Alt+Ctrl+KeyZ"), "Ctrl+Alt+Shift+KeyZ", "normalise reorders");
eq(K.normaliseCombo("Hyper+KeyZ"), null, "unknown modifier is refused");
ok(K.isValidFormat("Ctrl+Alt+KeyB") && K.isValidFormat("Alt+Digit3") && K.isValidFormat("Ctrl+F5"), "valid formats");
ok(!K.isValidFormat("KeyB") && !K.isValidFormat("Shift+KeyB") && !K.isValidFormat("Ctrl+Alt+ArrowUp") && !K.isValidFormat("Ctrl+Tab"), "invalid formats");


K.setLayoutMap(null);
eq(K.comboText("Ctrl+Alt+KeyB", { mac: false }), "Ctrl+Alt+B", "Windows text");
eq(K.comboText("Ctrl+Alt+Shift+KeyB", { mac: true }), "⌘⌥⇧B", "Mac text");
eq(K.comboLabels("Ctrl+Alt+Digit1", { mac: false }), ["Ctrl", "Alt", "1"], "digit chips");
eq(K.comboText("Ctrl+Slash", { mac: false }), "Ctrl+/", "punctuation");
ok(!/Key|Digit/.test(K.comboText("Ctrl+Alt+KeyQ", { mac: false })), "never shows a raw code");
K.setLayoutMap(new Map([["KeyY", "z"], ["KeyZ", "y"]]));
eq(K.comboText("Ctrl+Alt+KeyY", { mac: false }), "Ctrl+Alt+Z", "the admin's own layout is used for the letter");
K.setLayoutMap(null);
eq(K.ariaKeys("Ctrl+Alt+KeyB", { mac: false }), "Control+Alt+B", "aria-keyshortcuts on Windows");
eq(K.ariaKeys("Ctrl+Alt+KeyB", { mac: true }), "Meta+Alt+B", "aria-keyshortcuts on a Mac");


const mine = [{ actionId: "inventory.addBrand", pageScope: "inventory", keys: "Ctrl+Alt+KeyB", enabled: true }, { actionId: "bin.restore", pageScope: "bin", keys: "Ctrl+Alt+KeyR", enabled: true }, { actionId: "nav.dashboard", pageScope: "all", keys: "Ctrl+Alt+Digit1", enabled: true }];
let r = check("Ctrl+Alt+KeyB", "inventory.addProduct", "inventory", mine);
eq(codes(r), ["IN_USE_SAME_PAGE:warn"], "A: same page");
ok(/Ctrl\+Alt\+B is already used for 'Add brand' on Brand & Product Inventory\./.test(r[0].text) && r[0].replaceable && r[0].owner === "inventory.addBrand", "A: words, owner and Replace: " + r[0].text);
r = check("Ctrl+Alt+KeyR", "inventory.addProduct", "inventory", mine);
eq(codes(r), ["IN_USE_SAME_PAGE:info"], "A: another page is only a note");
ok(/Also used on Recycle Bin for 'Restore selected'\. That's fine; shortcuts only work on their own page\./.test(r[0].text), "A: info words: " + r[0].text);
eq(codes(check("Ctrl+Alt+Digit1", "inventory.addProduct", "inventory", mine)), ["IN_USE_SAME_PAGE:warn"], "A: a combo for every page clashes on every page");
eq(codes(check("Ctrl+KeyK", "inventory.addProduct", "inventory", mine)), ["IN_USE_SAME_PAGE:block"], "A: GlowUp's own keys cannot be taken");


r = check("Ctrl+Alt+KeyJ", "inventory.addBrand", "inventory", mine);
eq(codes(r), ["ACTION_ALREADY_BOUND:warn"], "B");
ok(/'Add brand' currently uses Ctrl\+Alt\+B\. Saving will replace it\./.test(r[0].text), "B words: " + r[0].text);
eq(codes(check("Ctrl+Alt+KeyB", "inventory.addBrand", "inventory", mine)), [], "B: the same combo again is no change");


r = check("Ctrl+KeyT", "inventory.addBrand", "inventory");
eq(codes(r), ["BROWSER_RESERVED:block"], "C");
ok(/Ctrl\+T is reserved by your browser for opening a new tab, so GlowUp can't use it\. Try a combo with Ctrl\+Alt\./.test(r[0].text), "C words: " + r[0].text);
eq(codes(check("F5", "inventory.addBrand", "inventory")), ["BROWSER_RESERVED:block"], "C: F5 without a modifier says reserved, not format");
eq(codes(check("Ctrl+Shift+Tab", "inventory.addBrand", "inventory")), ["BROWSER_RESERVED:block"], "C: Ctrl+Shift+Tab");
eq(codes(check("Ctrl+Digit3", "inventory.addBrand", "inventory")), ["BROWSER_RESERVED:block"], "C: Ctrl+3 switches tabs");


r = check("Alt+Tab", "inventory.addBrand", "inventory");
eq(codes(r), ["OS_RESERVED:block"], "D");
ok(/Alt\+Tab is used by your operating system for switching apps\./.test(r[0].text), "D words: " + r[0].text);
eq(codes(check("Ctrl+Space", "inventory.addBrand", "inventory", [], "Safari", true)), ["OS_RESERVED:block"], "D: Cmd+Space on a Mac");


r = check("Ctrl+Shift+KeyP", "inventory.addBrand", "inventory", [], "Firefox");
eq(codes(r), ["BROWSER_CONFLICT:warn"], "E");
ok(/^In Firefox, Ctrl\+Shift\+P opens a private window/.test(r[0].text) && /won't work in Firefox/.test(r[0].text), "E leads with the admin's browser: " + r[0].text);
r = check("Ctrl+Shift+KeyP", "inventory.addBrand", "inventory", [], "Chrome");
ok(/in Firefox/.test(r[0].text) && !/^In Chrome/.test(r[0].text), "E in a browser it does not touch names where it fails: " + r[0].text);
r = check("Alt+KeyF", "inventory.addBrand", "inventory", [], "Brave");
eq(codes(r), ["BROWSER_CONFLICT:warn"], "E: Alt+letter menu keys");
ok(/^In Brave/.test(r[0].text), "E: Brave counts as Chromium/all: " + r[0].text);


r = check("Shift+KeyB", "inventory.addBrand", "inventory");
eq(codes(r), ["INVALID_FORMAT:block"], "F: Shift + a letter is typing");
ok(/Add a letter or number key with Ctrl, Alt, or Shift\. Single keys aren't allowed so shortcuts don't fire while you type\./.test(r[0].text), "F words");
eq(codes(check("KeyB", "inventory.addBrand", "inventory")), ["INVALID_FORMAT:block"], "F: a single key");
eq(codes(check("Ctrl+Alt+ArrowLeft", "inventory.addBrand", "inventory")), ["INVALID_FORMAT:block"], "F: unsupported key");
eq(codes(check("", "inventory.addBrand", "inventory")), ["INVALID_FORMAT:block"], "F: nothing");


eq(K.testVerdict(true, "Ctrl+Alt+KeyB", "Edge", { mac: false }), { code: null, text: "✓ Works in Edge." }, "G: received");
r = K.testVerdict(false, "Ctrl+Alt+KeyB", "Edge", { mac: false });
ok(r.code === "TEST_NOT_RECEIVED" && /^GlowUp didn't receive Ctrl\+Alt\+B\. Your browser, operating system, or a browser extension is probably using it\. Try another combo\.$/.test(r.text), "G: not received words");
eq(K.saveFailedText({ code: "unavailable" }, true), "You're offline. Shortcuts can be changed once you're back online.", "H: offline");
eq(K.saveFailedText({ code: "internal" }, true), "Couldn't save. Check your connection and try again.", "H: failed save");
ok(/Only admins/.test(K.saveFailedText({ code: "permission-denied" })), "H: refused (not an admin)");


const list = [
    { actionId: "inventory.addProduct", pageScope: "inventory", keys: "Ctrl+Alt+KeyP", enabled: true },
    { actionId: "dashboard.addProduct", pageScope: "dashboard", keys: "Ctrl+Alt+KeyP", enabled: true },
    { actionId: "nav.bin", pageScope: "all", keys: "Ctrl+Alt+Digit4", enabled: true },
    { actionId: "curator.import", pageScope: "curator", keys: "Ctrl+Alt+KeyI", enabled: false }
];
eq((K.findShortcut(list, "inventory", "Ctrl+Alt+KeyP") || {}).actionId, "inventory.addProduct", "the same combo does the page's own thing (inventory)");
eq((K.findShortcut(list, "dashboard", "Ctrl+Alt+KeyP") || {}).actionId, "dashboard.addProduct", "the same combo does the page's own thing (dashboard)");
eq(K.findShortcut(list, "curator", "Ctrl+Alt+KeyP"), null, "nothing on a page without it");
eq((K.findShortcut(list, "archive", "Ctrl+Alt+Digit4") || {}).actionId, "nav.bin", "a combo for every page works on every page");
eq(K.findShortcut(list, "curator", "Ctrl+Alt+KeyI"), null, "a switched-off shortcut does nothing");


const brand = actions["inventory.addBrand"];
eq(K.availabilityOf(brand, "inventory", { isAvailable: () => true }), true, "available");
eq(K.availabilityOf(brand, "inventory", { isAvailable: () => "Add brand isn't available right now." }), "Add brand isn't available right now.", "the button's own reason");
eq(K.availabilityOf(brand, "curator", { isAvailable: () => true }, (s) => PAGES[s]), "This works on Brand & Product Inventory.", "another page");
ok(/isn't ready yet/.test(K.availabilityOf(brand, "inventory", undefined)), "no handler yet (still loading)");
eq(K.availabilityOf(undefined, "inventory", {}), "This action no longer exists.", "a removed action");
ok(/isn't available/.test(K.availabilityOf(brand, "inventory", { isAvailable: () => { throw new Error("x"); } })), "a failing check is 'not available', not a crash");


const defaults = K.defaultList(CATALOG, 1);
ok(defaults.length === CATALOG.filter((a) => a.keys).length && defaults.every((s) => s.isDefault && s.enabled), "the defaults");
const scopes = {};
let doubled = false;
defaults.forEach((s) => { const key = s.pageScope + "|" + s.keys; if (scopes[key]) doubled = true; scopes[key] = 1; });
ok(!doubled, "no default combo is used twice on one page");
ok(defaults.every((s) => K.checkCombo({ keys: s.keys, actionId: s.actionId, scope: s.pageScope, shortcuts: defaults.filter((x) => x.actionId !== s.actionId), actions, pages: PAGES, browser: "Chrome", mac: false })
    .every((m) => m.level === "info")), "every default passes its own checks (no block, no warning) in Chrome");
ok(CATALOG.filter((a) => a.destructive).every((a) => !a.keys), "destructive actions have no default combo");
ok(CATALOG.every((a) => a.description.length <= 120), "every description fits 120 characters");

const stored = { items: { "inventory.addBrand": { keys: "Ctrl+Alt+KeyP", description: "Mine", enabled: true }, "gone.action": { keys: "Ctrl+Alt+KeyG" } },
    seeded: CATALOG.filter((a) => a.keys && a.id !== "inventory.addProduct").map((a) => a.id) };
const merged = K.mergeWithCatalog(stored, CATALOG, 2);
eq(merged.orphans, ["gone.action"], "an orphan (its action was removed) is found");
ok(!merged.list.some((s) => s.actionId === "gone.action"), "the orphan is not shown");
ok(!merged.list.some((s) => s.actionId === "inventory.import"), "a deleted default does not come back");
ok(!merged.list.some((s) => s.actionId === "inventory.addProduct") && merged.seeded.includes("inventory.addProduct"), "a new default that clashes with the admin's own combo is not given");
eq(merged.list.find((s) => s.actionId === "inventory.addBrand").description, "Mine", "the admin's own description is kept");
const fresh = K.mergeWithCatalog({ items: {}, seeded: [] }, CATALOG, 3);
eq(fresh.added.length, defaults.length, "a first visit gets every default");
eq(K.mergeWithCatalog(K.toStored(fresh.list, fresh.seeded), CATALOG, 4).added, [], "saved and read back: nothing added twice");
eq(K.mergeWithCatalog({ items: { "inventory.addBrand": "junk", "inventory.import": { keys: "nonsense" } }, seeded: CATALOG.map((a) => a.id) }, CATALOG, 5).list.filter((s) => s.actionId === "inventory.addBrand" || s.actionId === "inventory.import").length, 0, "broken rows are skipped, not a crash");


eq(K.detectBrowser({ userAgent: "Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36 Edg/130.0" }), "Edge", "Edge");
eq(K.detectBrowser({ userAgent: "Mozilla/5.0 (Windows NT 10.0; rv:131.0) Gecko/20100101 Firefox/131.0" }), "Firefox", "Firefox");
eq(K.detectBrowser({ userAgent: "Mozilla/5.0 (Macintosh) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15" }), "Safari", "Safari");
eq(K.detectBrowser({ brave: {}, userAgent: "Chrome/130" }), "Brave", "Brave");
ok(K.isMac({ platform: "MacIntel" }) && !K.isMac({ platform: "Win32" }), "Mac detection");

console.log(`checks made: ${checks}`);
if (failed) { console.log(`${failed} FAILED`); process.exit(1); }
console.log("ALL PASSED");
