





import { db, auth } from "../_Database/firebase-console.js";
import { doc, getDoc, serverTimestamp } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { noteReads, writeBatch } from "./adminStore.js";

export const SHARED_COLLECTION = "siteLayouts";
export const SHARED_PAGES = [
    { id: "dashboard", label: "Dashboard" },
    { id: "inventory", label: "Brand & Product Inventory" },
    { id: "curator", label: "Ingredient Curator" },
    { id: "bin", label: "Recycle Bin" },
    { id: "archive", label: "Archive" }
];
const CACHE_KEY = "glowup.sharedLayout.v1.";
const CACHE_MS = 5 * 60 * 1000;


export function cachedShared(page) {
    try {
        const kept = JSON.parse(localStorage.getItem(CACHE_KEY + page) || "null");
        if (kept && typeof kept === "object") return { data: kept.data || null, fresh: Date.now() - Number(kept.at || 0) < CACHE_MS };
    } catch (error) {                    }
    return { data: null, fresh: false };
}
function keep(page, data) { try { localStorage.setItem(CACHE_KEY + page, JSON.stringify({ at: Date.now(), data })); } catch (error) {                       } }


export async function readShared(page) {
    if (typeof auth.authStateReady === "function") await auth.authStateReady();
    if (!auth.currentUser) return cachedShared(page).data;
    const snapshot = await getDoc(doc(db, SHARED_COLLECTION, page));
    noteReads(SHARED_COLLECTION + "/" + page, 1);
    const data = snapshot.exists() ? { layout: snapshot.data().layout || {}, appearance: snapshot.data().appearance || {} } : null;
    keep(page, data);
    return data;
}


export async function writeShared(pages, layoutOf, appearance) {
    const user = auth.currentUser;
    if (!user) throw new Error("You are signed out. Sign in again.");
    const batch = writeBatch(db);
    pages.forEach((page) => batch.set(doc(db, SHARED_COLLECTION, page), { layout: layoutOf(page), appearance: appearance || {}, updatedAt: serverTimestamp(), updatedBy: user.uid }));
    await batch.commit();
    pages.forEach((page) => keep(page, { layout: layoutOf(page), appearance: appearance || {} }));
}


export async function clearShared(pages) {
    const batch = writeBatch(db);
    pages.forEach((page) => batch.delete(doc(db, SHARED_COLLECTION, page)));
    await batch.commit();
    pages.forEach((page) => keep(page, null));
}
