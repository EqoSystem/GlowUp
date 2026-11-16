







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
function tell() { listeners.forEach((fn) => { try { fn(state); } catch (error) {                                             } }); }

export function onShortcuts(fn) { listeners.add(fn); return () => listeners.delete(fn); }
export function shortcutState() { return state; }

function apply(stored, from) {
    const merged = mergeWithCatalog(stored, CATALOG);
    state = { ...state, list: merged.list, seeded: merged.seeded, orphans: merged.orphans, loaded: from === "server" ? true : state.loaded, error: "" };
    return merged;
}
function keep(stored) { try { if (uid()) localStorage.setItem(CACHE_KEY + uid(), JSON.stringify(stored)); } catch (error) {                       } }
function kept() { try { return JSON.parse(localStorage.getItem(CACHE_KEY + uid()) || "null"); } catch (error) { return null; } }


export function whyFailed(error) {
    return saveFailedText(error, typeof navigator === "undefined" || navigator.onLine !== false);
}





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

            if (!stored || merged.added.length > 0 || merged.orphans.length > 0) setDoc(doc(db, SHORTCUT_COLLECTION, uid()), { ...next, updatedAt: serverTimestamp() }).catch(() => {});
        } catch (error) {
            state = { ...state, loaded: true, error: whyFailed(error), denied: /permission-denied/.test(String(error && error.code)) };
        } finally { reading = null; }
        tell();
        return state;
    })();
    return reading;
}





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


export function resetShortcuts() { return saveShortcuts(defaultList(CATALOG)); }


if (channel) channel.addEventListener("message", (event) => {
    const data = event.data || {};
    if (!data.stored || data.uid !== uid()) return;
    apply(data.stored, "server"); keep(data.stored); tell();
});
if (typeof window !== "undefined") window.addEventListener("focus", () => { if (state.loaded && uid()) loadShortcuts(false); });
