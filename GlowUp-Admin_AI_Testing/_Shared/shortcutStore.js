// KEYBOARD SHORTCUTS, where they are kept (9 November 2026).
//
// One document per admin: adminShortcuts/{uid} = { items: { actionId: {...} }, seeded: [actionId], updatedAt }.
// Only that admin reads or writes it (firestore.rules); any other account is refused (permission-denied, the "403" of Firestore).
// It follows the admin to every browser and computer. A copy is kept in this browser so a page knows its shortcuts at once;
// the database answer replaces it when it comes. Changes are shown at once and taken back, with a message, when the save fails.
// Other open tabs hear about a change (BroadcastChannel) and read again when they get the focus.
// These are personal settings, not work on the catalogue: they are NOT written to the activity log.
import { db, auth } from "../_Database/firebase-console.js";
import { doc, getDoc, setDoc, serverTimestamp } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { noteReads } from "./adminStore.js";
import { CATALOG } from "./shortcutCatalog.js";
import { mergeWithCatalog, toStored, defaultList, saveFailedText } from "./shortcutKeys.js";

export const SHORTCUT_COLLECTION = "adminShortcuts";
const CACHE_KEY = "glowup.shortcuts.v1.";
const REREAD_MS = 30 * 1000;

let state = { list: defaultList(CATALOG), seeded: CATALOG.filter((a) => a.keys).map((a) => a.id), orphans: [], loaded: false, error: "", denied: false };
let lastRead = 0, reading = null;
const listeners = new Set();
const channel = typeof BroadcastChannel === "function" ? new BroadcastChannel("glowup-shortcuts") : null;

const uid = () => (auth.currentUser ? auth.currentUser.uid : "");
function tell() { listeners.forEach((fn) => { try { fn(state); } catch (error) { /* one listener does not stop the others */ } }); }
/** Calls `fn(state)` after every change. Returns the way to stop. */
export function onShortcuts(fn) { listeners.add(fn); return () => listeners.delete(fn); }
export function shortcutState() { return state; }

function apply(stored, from) {
    const merged = mergeWithCatalog(stored, CATALOG);
    state = { ...state, list: merged.list, seeded: merged.seeded, orphans: merged.orphans, loaded: from === "server" ? true : state.loaded, error: "" };
    return merged;
}
function keep(stored) { try { if (uid()) localStorage.setItem(CACHE_KEY + uid(), JSON.stringify(stored)); } catch (error) { /* full or blocked */ } }
function kept() { try { return JSON.parse(localStorage.getItem(CACHE_KEY + uid()) || "null"); } catch (error) { return null; } }

/** What a refused or failed request means, in plain words. */
export function whyFailed(error) {
    return saveFailedText(error, typeof navigator === "undefined" || navigator.onLine !== false);
}

/**
 * Reads the admin's shortcuts (once per REREAD_MS unless `force`). The kept copy is used at once; a first visit gets the defaults,
 * and a later release's new defaults are added where the combo is still free (they are saved then, quietly).
 */
export async function loadShortcuts(force = false) {
    if (typeof auth.authStateReady === "function") await auth.authStateReady();
    if (!uid()) return state;
    const copy = kept();
    if (copy && !state.loaded) { apply(copy, "cache"); tell(); }
    if (!force && state.loaded && Date.now() - lastRead < REREAD_MS) return state;
    if (reading) return reading;
    reading = (async () => {
        try {
            const snapshot = await getDoc(doc(db, SHORTCUT_COLLECTION, uid()));
            noteReads(SHORTCUT_COLLECTION, 1);
            lastRead = Date.now();
            const stored = snapshot.exists() ? snapshot.data() : null;
            const merged = apply(stored, "server");
            const next = toStored(merged.list, merged.seeded);
            keep(next);
            // a first visit, new defaults or orphans to clear: written back once (failures here are quiet; the list still works)
            if (!stored || merged.added.length > 0 || merged.orphans.length > 0) setDoc(doc(db, SHORTCUT_COLLECTION, uid()), { ...next, updatedAt: serverTimestamp() }).catch(() => {});
        } catch (error) {
            state = { ...state, loaded: true, error: whyFailed(error), denied: /permission-denied/.test(String(error && error.code)) };
        } finally { reading = null; }
        tell();
        return state;
    })();
    return reading;
}

/**
 * Saves a new list. It is shown at once; when the database refuses, the old list comes back and the error is thrown
 * (the caller shows it next to what was being done). `list` = [{ actionId, keys, description, enabled, conflictFlag, isDefault }].
 */
export async function saveShortcuts(list) {
    if (!uid()) throw Object.assign(new Error("signed out"), { code: "unauthenticated" });
    if (typeof navigator !== "undefined" && navigator.onLine === false) throw Object.assign(new Error("offline"), { code: "unavailable" });
    const before = state;
    const now = Date.now();
    const clean = list.map((s) => ({ ...s, id: s.actionId, updatedAt: s.updatedAt || now, createdAt: s.createdAt || now }));
    const stored = toStored(clean, state.seeded);
    apply(stored, "local"); tell();
    try {
        await setDoc(doc(db, SHORTCUT_COLLECTION, uid()), { ...stored, updatedAt: serverTimestamp() });
        keep(stored);
        state = { ...state, orphans: [], denied: false };
        if (channel) channel.postMessage({ uid: uid(), stored });
        tell();
    } catch (error) {
        state = { ...before, denied: /permission-denied/.test(String(error && error.code)) };
        tell();
        throw error;
    }
}

/** Back to the defaults of the catalogue (the admin's own combos and changes are dropped). */
export function resetShortcuts() { return saveShortcuts(defaultList(CATALOG)); }

// another tab saved: take its list at once; a tab that comes back to the front reads again (at most every 30 seconds)
if (channel) channel.addEventListener("message", (event) => {
    const data = event.data || {};
    if (!data.stored || data.uid !== uid()) return;
    apply(data.stored, "server"); keep(data.stored); tell();
});
if (typeof window !== "undefined") window.addEventListener("focus", () => { if (state.loaded && uid()) loadShortcuts(false); });
