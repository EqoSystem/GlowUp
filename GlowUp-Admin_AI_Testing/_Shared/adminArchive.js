// THE ARCHIVE: entries that left the Recycle bin. Kept 30 days, then deleted for good by the server job
// (api/archive-purge.js). Design and the database impact check: ARCHIVE_DESIGN.md.
//
// One document per entry in the collection "archive", id "<collection>__<id>":
//   type, sourceCollection, sourceId, name, data (the whole original document, still binned), deleted_at, expires_at,
//   deleted_by (uid), deleted_by_email, archivedWith (a product moved with its brand: the brand's id).
// Moving to the Archive writes that document and deletes the original IN ONE BATCH, so an entry is never in both places
// and never lost in between. The app never reads "archive" (and the rules let only admins read it).
// Nothing here changes how the Recycle bin's own Restore and Delete permanently work.
import { db, auth } from "../_Database/firebase-console.js";
import { collection, doc, getDoc, serverTimestamp, Timestamp } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { getDocs, writeBatch, noteReads } from "./adminStore.js";

export const ARCHIVE = "archive";
export const KEEP_DAYS = 30;
export const WARN_DAYS = 3;
export const DAY_MS = 24 * 60 * 60 * 1000;
export const TYPE_OF = { ingredients: "ingredient", brands: "brand", products: "product" };
const PAIRS_PER_BATCH = 200;                      // two writes per entry; Firestore takes at most 500 in one batch

export const archiveId = (collectionName, id) => `${collectionName}__${id}`;
const nameOf = (data, collectionName) => String((data && (data.name || (collectionName === "products" ? data.productName : ""))) || `Unnamed ${TYPE_OF[collectionName]}`).slice(0, 200);
const millis = (value) => (value && typeof value.toMillis === "function" ? value.toMillis() : value && typeof value.seconds === "number" ? value.seconds * 1000 : 0);

/** Whole days left before the server job deletes the entry (0 when the time is up). */
export function daysLeft(entry, now = Date.now()) {
    const end = millis(entry.expires_at);
    return end ? Math.max(0, Math.ceil((end - now) / DAY_MS)) : KEEP_DAYS;
}
export const expired = (entry, now = Date.now()) => millis(entry.expires_at) > 0 && millis(entry.expires_at) <= now;

async function runPairs(pairs) {
    for (let i = 0; i < pairs.length; i += PAIRS_PER_BATCH) {
        const batch = writeBatch(db);
        pairs.slice(i, i + PAIRS_PER_BATCH).forEach((pair) => pair(batch));
        await batch.commit();
    }
}

/**
 * MOVE TO ARCHIVE. entries = [{ collection, id, with }] (with = the brand a product goes with, or "").
 * Each entry is read again first: one that is no longer in the Recycle bin (restored meanwhile, or gone) is skipped.
 * Resolves { moved, skipped: [name] }. Throws when a batch is refused (nothing of that batch is written).
 */
export async function moveToArchive(entries) {
    const user = auth.currentUser;
    if (!user) throw new Error("Not signed in.");
    const fresh = await Promise.all(entries.map((e) => getDoc(doc(db, e.collection, e.id))));
    noteReads("archive: checked before moving", fresh.length);
    const expires = Timestamp.fromMillis(Date.now() + KEEP_DAYS * DAY_MS);
    const pairs = [], skipped = [];
    fresh.forEach((snap, i) => {
        const e = entries[i];
        if (!snap.exists() || snap.data().binned !== true) { skipped.push(e.name || e.id); return; }
        const data = snap.data();
        pairs.push((batch) => {
            batch.set(doc(db, ARCHIVE, archiveId(e.collection, e.id)), {
                type: TYPE_OF[e.collection], sourceCollection: e.collection, sourceId: e.id, name: nameOf(data, e.collection), data,
                deleted_at: serverTimestamp(), expires_at: expires, deleted_by: user.uid, deleted_by_email: user.email || "", archivedWith: e.with || ""
            });
            batch.delete(doc(db, e.collection, e.id));
        });
    });
    await runPairs(pairs);
    return { moved: pairs.length, skipped };
}

/** Every archived entry: [{ id, ...fields }]. One read per entry (a plain query, counted in the console). */
export async function listArchive() {
    const snapshot = await getDocs(collection(db, ARCHIVE));
    return snapshot.docs.map((d) => Object.assign({ id: d.id }, d.data()));
}

/**
 * RESTORE: the entries go back to the Recycle bin, exactly as they were (the bin's own Restore brings them back from there).
 * Never overwrites: an entry whose id is in use again is skipped. Resolves { restored, skipped: [name] }.
 */
export async function restoreFromArchive(entries) {
    const there = await Promise.all(entries.map((e) => getDoc(doc(db, e.sourceCollection, e.sourceId))));
    noteReads("archive: checked before restoring", there.length);
    const pairs = [], skipped = [];
    there.forEach((snap, i) => {
        const e = entries[i];
        if (snap.exists() || !TYPE_OF[e.sourceCollection] || !e.data) { skipped.push(e.name || e.sourceId); return; }
        pairs.push((batch) => {
            batch.set(doc(db, e.sourceCollection, e.sourceId), e.data);
            batch.delete(doc(db, ARCHIVE, e.id));
        });
    });
    await runPairs(pairs);
    return { restored: pairs.length, skipped };
}

/** DELETE NOW: only the archive documents go (the originals left the database when they were archived). */
export async function deleteFromArchive(entries) {
    const steps = entries.map((e) => (batch) => batch.delete(doc(db, ARCHIVE, e.id)));
    for (let i = 0; i < steps.length; i += 400) {
        const batch = writeBatch(db);
        steps.slice(i, i + 400).forEach((step) => step(batch));
        await batch.commit();
    }
    return steps.length;
}
