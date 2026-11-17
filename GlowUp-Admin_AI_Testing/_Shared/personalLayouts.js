// The admin's own saved layouts in the database: adminLayouts/{uid}, readable and writable only by that admin (firestore.rules).
// Added 17 November 2026 so a layout made on the computer ("Echo") also shows on the phone. How the two sides are put
// together is in layoutSync.js. A failure (offline, rule not published) only logs a warning: the browser's own copy keeps working.
import { db, auth } from "../_Database/firebase-console.js";
import { doc, getDoc, setDoc, serverTimestamp } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { onAuthStateChanged } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-auth.js";
import { noteReads } from "./adminStore.js";

export const PERSONAL_COLLECTION = "adminLayouts";
export const PERSONAL_MOST_CHARS = 900000;          // the rule's limit (a Firestore document holds about 1 MB)

/** The signed-in admin's uid, once sign-in has settled ("" when nobody is signed in). */
export function signedInUid() {
    return new Promise((resolve) => {
        if (auth.currentUser) { resolve(auth.currentUser.uid); return; }
        let done = false;
        const stop = onAuthStateChanged(auth, (user) => {
            if (done) return;
            done = true;
            resolve(user ? user.uid : "");
            setTimeout(() => stop(), 0);                 // after the call has returned, so `stop` is set
        });
    });
}

/** { store, savedAt } or null when this admin has no copy yet. */
export async function readPersonal(uid) {
    const snapshot = await getDoc(doc(db, PERSONAL_COLLECTION, uid));
    noteReads(PERSONAL_COLLECTION, 1);
    if (!snapshot.exists()) return null;
    const data = snapshot.data();
    let store = null;
    try { store = JSON.parse(String(data.store || "null")); } catch (error) { console.warn("The saved layouts in the database could not be read: ", error); }
    return store ? { store, savedAt: Number(data.savedAt) || 0 } : null;
}

export async function writePersonal(uid, json, savedAt) {
    if (json.length > PERSONAL_MOST_CHARS) throw new Error("The layouts are too large to keep in the database.");
    await setDoc(doc(db, PERSONAL_COLLECTION, uid), { store: json, savedAt: Math.round(savedAt), updatedAt: serverTimestamp() });
}
