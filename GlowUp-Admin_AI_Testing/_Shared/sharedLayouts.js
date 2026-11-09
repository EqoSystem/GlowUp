// The SHARED default layout: what "Default layout" shows on a page for every admin, on every computer (9 November 2026).
//
// One document per page: siteLayouts/{page} = { layout, appearance, updatedAt, updatedBy }. Only admins read or write it
// (firestore.rules). Without a document the page's own built-in arrangement is the default, as before.
// Reads: ONE per page opening, and none when the last answer is less than CACHE_MS old (it is kept in this browser).
// What is read is never trusted: adminLayouts.js passes it through the same cleaning as a saved or imported layout.
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

/** The last answer kept in this browser for `page` ({ layout, appearance } or null), and whether it is still fresh. */
export function cachedShared(page) {
    try {
        const kept = JSON.parse(localStorage.getItem(CACHE_KEY + page) || "null");
        if (kept && typeof kept === "object") return { data: kept.data || null, fresh: Date.now() - Number(kept.at || 0) < CACHE_MS };
    } catch (error) { /* nothing kept */ }
    return { data: null, fresh: false };
}
function keep(page, data) { try { localStorage.setItem(CACHE_KEY + page, JSON.stringify({ at: Date.now(), data })); } catch (error) { /* full or blocked */ } }

/** The shared default of `page` from the database ({ layout, appearance } or null). Waits for the sign-in. */
export async function readShared(page) {
    if (typeof auth.authStateReady === "function") await auth.authStateReady();
    if (!auth.currentUser) return cachedShared(page).data;
    const snapshot = await getDoc(doc(db, SHARED_COLLECTION, page));
    noteReads(SHARED_COLLECTION + "/" + page, 1);
    const data = snapshot.exists() ? { layout: snapshot.data().layout || {}, appearance: snapshot.data().appearance || {} } : null;
    keep(page, data);
    return data;
}

/** Makes `layoutOf(page)` (an already cleaned page layout) and `appearance` the default of each page in `pages`. One write. */
export async function writeShared(pages, layoutOf, appearance) {
    const user = auth.currentUser;
    if (!user) throw new Error("You are signed out. Sign in again.");
    const batch = writeBatch(db);
    pages.forEach((page) => batch.set(doc(db, SHARED_COLLECTION, page), { layout: layoutOf(page), appearance: appearance || {}, updatedAt: serverTimestamp(), updatedBy: user.uid }));
    await batch.commit();
    pages.forEach((page) => keep(page, { layout: layoutOf(page), appearance: appearance || {} }));
}

/** The pages in `pages` go back to their built-in default. */
export async function clearShared(pages) {
    const batch = writeBatch(db);
    pages.forEach((page) => batch.delete(doc(db, SHARED_COLLECTION, page)));
    await batch.commit();
    pages.forEach((page) => keep(page, null));
}
