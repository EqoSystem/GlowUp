// The log-in lock kept in the database (loginLocks/{sha-256 of the e-mail}), so it also holds in a private window, on another
// browser or on another computer. The browser's own lock (loginLockout.js) stays for the friendly countdown; this one is the
// shared record. The database rules decide the count and the five minutes, not this file: a page cannot lock for longer or
// for less. Free tier: no Cloud Functions, the rules do the work. When the database cannot be reached the log-in is not
// blocked by this lock (Firebase Authentication's own limit still applies).
import { db } from "../_Database/firebase-console.js";
import { doc, getDoc, setDoc, deleteDoc, serverTimestamp } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";

export const COLLECTION = "loginLocks";
export const MAX_TRIES = 3;
const WAIT_MS = 4000;

export async function keyOf(email) {
    const bytes = new TextEncoder().encode(String(email || "").trim().toLowerCase());
    const digest = await crypto.subtle.digest("SHA-256", bytes);
    return Array.from(new Uint8Array(digest), (b) => b.toString(16).padStart(2, "0")).join("");
}

const limited = (promise) => Promise.race([promise, new Promise((resolve) => setTimeout(() => resolve(undefined), WAIT_MS))]);

/** Milliseconds this e-mail is locked for, from the database (0 = not locked or not reachable). */
export async function remoteRemaining(email, now = Date.now()) {
    try {
        const snap = await limited(getDoc(doc(db, COLLECTION, await keyOf(email))));
        if (!snap || !snap.exists()) return 0;
        const until = Number(snap.data().untilUnix) * 1000;
        return until > now ? until - now : 0;
    } catch (error) { return 0; }
}

/** One wrong password: adds 1 to the count in the database (the rules set the lock at the 3rd). Returns { fails, lockedMs } or null. */
export async function remoteFail(email, now = Date.now()) {
    try {
        const ref = doc(db, COLLECTION, await keyOf(email));
        const snap = await limited(getDoc(ref));
        if (snap === undefined) return null;
        const old = snap.exists() ? snap.data() : null;
        const finished = old && ((Number(old.untilUnix) > 0 && Number(old.untilUnix) * 1000 <= now) || Number(old.fails) >= MAX_TRIES);
        const fails = !old || finished ? 1 : Number(old.fails) + 1;
        const untilUnix = fails >= MAX_TRIES ? Math.floor(now / 1000) + 300 : 0;
        await limited(setDoc(ref, { fails, untilUnix, updatedAt: serverTimestamp() }));
        return { fails, lockedMs: untilUnix ? untilUnix * 1000 - now : 0 };
    } catch (error) { return null; }
}

/** A right password: the count is removed (the person is signed in, which the rules ask for). */
export async function remoteClear(email) {
    try { await limited(deleteDoc(doc(db, COLLECTION, await keyOf(email)))); } catch (error) {                       }
}
