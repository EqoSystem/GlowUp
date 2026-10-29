// THE ADMIN SITE'S OWN COPY OF ITS THREE LISTS: brands, products and ingredients.
//
// Before this file every page downloaded whole collections each time it opened, and again after every single change:
// opening Brand & Product Inventory read about 715 documents, and so did each save. Now:
//
//   - the first page that needs a list downloads it ONCE and keeps it: in memory, and in this browser's localStorage,
//     so it is still there after a reload, in another tab, and after the browser is closed. It is wiped on sign-out
//     (and when another account signs in). Only what the signed-in admin's page read itself is kept: nothing more.
//   - a page that opens later gets the kept list AT ONCE: no waiting and no reads;
//   - after a change made on this site, only the documents that were written are read again;
//   - other people and the phone app change the data too. Once per page (and not more often than every SYNC_MIN_MS):
//     one count, then only the documents changed since the last sync (updatedAt, createdAt, addedAtUnix), merged into
//     the kept copy. A document DELETED elsewhere cannot show up in such a query, so the count is compared with the
//     kept copy after the merge: when they still differ, the list is downloaded in full (at most once an hour per list
//     on its own; the "Refresh data" button does it on request, at most once a minute). The kept list is shown first;
//     a changed one replaces it quietly (onListsRefreshed tells the page).
//   - KNOWN GAP: the phone app only adds 1 to "scans" on a brand or product and does not touch updatedAt, so a changed
//     scan counter does not arrive in a sync. It arrives with the next full download.
//   - every read is counted: see noteReads(), and the "[reads]" lines in the browser console.
//
// Pages use it by importing getDocs / addDoc / updateDoc / setDoc / deleteDoc / writeBatch from HERE instead of from
// the Firebase file. The calls look the same. Anything that is not one of the three whole lists (a query, another
// collection) goes straight through to Firebase.
import { auth, db } from "../_Database/firebase-console.js";
import * as fb from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";

/** The collections that are kept. */
export const LISTS = ["brands", "products", "ingredients"];
/** After more than this many changed documents one full download is simpler than reading them one by one. */
const MANY_CHANGES = 40;
const KEY = "glowup.store.v2.";                  // v2: localStorage, with a sync point (v1 copies in sessionStorage are ignored)
const disk = (() => { try { return window.localStorage; } catch (error) { return window.sessionStorage; } })();
const OVERLAP_MS = 5 * 60 * 1000;                // clocks differ a little: changes this close to the sync point are asked for again
const ADDED_OVERLAP_S = 2 * 3600;                // "addedAtUnix" comes from phone clocks: asked for 2 hours further back
const SYNC_MIN_MS = 30 * 1000;                   // not checked again within 30 s of the last check
const AUTO_FULL_EVERY_MS = 60 * 60 * 1000;       // at most one automatic full download per list per hour (counts differ)
// "Refresh data" downloads about 700 documents, so it is limited (kept in this browser): a pause after each use, and a cap per hour
export const REFRESH_COOLDOWN_MS = 2 * 60 * 1000;
export const REFRESH_MAX_PER_HOUR = 6;
const REFRESH_LOG_KEY = "glowup.refresh.log.v1";

// name -> { uid, at, docs: Map(id -> data) }
const memory = {};
const inFlight = {};
const countChecked = {};
const listeners = new Set();
let reads = 0;


// ---------------------------------------------------------------------------------------------
// The read counter: every document read through here, per page load, in the browser console
// ---------------------------------------------------------------------------------------------
const breakdown = {};
let logTimer = 0;
let sizeLogged = false;
/** Counts `n` documents read for `label`; the totals are written to the console 1.5 s after the last read and when the page is left. */
export function noteReads(label, n) {
    if (!(n > 0)) return;
    reads += n;
    breakdown[label] = (breakdown[label] || 0) + n;
    clearTimeout(logTimer);
    logTimer = setTimeout(logReads, 1500);
}
function logReads() {
    if (reads === 0) return;
    console.groupCollapsed("[reads] " + reads + " documents read on " + (location.pathname.split("/").pop() || "this page"));
    Object.keys(breakdown).forEach((label) => console.log(label + ": " + breakdown[label]));
    console.groupEnd();
}
window.addEventListener("pagehide", logReads);
/** The size of the kept copies in the browser's storage, written once per page load (UTF-16: about 2 bytes a character). */
function logStoredOnce() {
    if (sizeLogged) return;
    sizeLogged = true;
    let chars = 0;
    try { Object.keys(disk).filter((key) => key.startsWith(KEY)).forEach((key) => { chars += key.length + (disk.getItem(key) || "").length; }); } catch (error) { return; }
    console.info("[store] the kept copies take about " + Math.round(chars * 2 / 1024) + " KB of browser storage");
}
/** A count query, counted (one read for up to 1,000 entries; logged as 1). */
export async function getCountFromServer(target) {
    const result = await fb.getCountFromServer(target);
    noteReads("count queries", 1);
    return result;
}
/** A live listener, counted: the documents of the first answer, then the ones that changed. */
export function onSnapshot(target, next, fail) {
    return fb.onSnapshot(target, (snapshot) => { noteReads("live listener", snapshot.docChanges().length); next(snapshot); }, fail);
}

/** The page is told when a kept list was replaced by a fresh download that DIFFERS from what it showed. */
export function onListsRefreshed(callback) { listeners.add(callback); return () => listeners.delete(callback); }

// ---------------------------------------------------------------------------------------------
// Dates: Firebase gives its own date objects; a kept copy holds them as two numbers and gives the same methods back
// ---------------------------------------------------------------------------------------------
class Stamp {
    constructor(seconds, nanoseconds) { this.seconds = seconds; this.nanoseconds = nanoseconds || 0; }
    toMillis() { return this.seconds * 1000 + Math.floor(this.nanoseconds / 1e6); }
    toDate() { return new Date(this.toMillis()); }
    toJSON() { return { seconds: this.seconds, nanoseconds: this.nanoseconds }; }
}
const isStampShape = (v) => v !== null && typeof v === "object" && typeof v.seconds === "number" && typeof v.nanoseconds === "number" && Object.keys(v).length === 2;

/** A copy of a value made of plain objects, lists, texts, numbers and Stamps (so nothing a page does to it reaches the kept copy). */
function copy(value) {
    if (value === null || typeof value !== "object") return value;
    if (value instanceof Stamp) return new Stamp(value.seconds, value.nanoseconds);
    if (value instanceof Date) return new Stamp(Math.floor(value.getTime() / 1000), (value.getTime() % 1000) * 1e6);
    if (typeof value.toDate === "function" && typeof value.seconds === "number") return new Stamp(value.seconds, value.nanoseconds);
    if (isStampShape(value)) return new Stamp(value.seconds, value.nanoseconds);
    if (Array.isArray(value)) return value.map(copy);
    const out = {};
    Object.keys(value).forEach((key) => { out[key] = copy(value[key]); });
    return out;
}

// ---------------------------------------------------------------------------------------------
// Which list is a reference about?
// ---------------------------------------------------------------------------------------------
/** The name of the list when `target` is one of the three WHOLE collections (not a query on it), else null. */
function wholeList(target) {
    return target && target.type === "collection" && LISTS.includes(target.id) ? target.id : null;
}
/** The list a document belongs to (null when it is not one of the three). */
function listOfDoc(ref) {
    const name = ref && ref.parent ? ref.parent.id : null;
    return name && LISTS.includes(name) ? name : null;
}
const uid = () => (auth.currentUser ? auth.currentUser.uid : "");

// ---------------------------------------------------------------------------------------------
// The kept copy
// ---------------------------------------------------------------------------------------------
function load(name) {
    const me = uid();
    if (memory[name] && memory[name].uid === me) return memory[name];
    try {
        const saved = JSON.parse(disk.getItem(KEY + name) || "null");
        if (saved && saved.uid === me && Array.isArray(saved.docs)) {
            memory[name] = { uid: me, at: Number(saved.at) || 0, synced: Number(saved.synced) || Number(saved.at) || 0, docs: new Map(saved.docs.map(([id, data]) => [id, copy(data)])) };
            logStoredOnce();
            return memory[name];
        }
    } catch (error) { /* a private window, or a copy that cannot be read: download again */ }
    return null;
}
function save(name, kept) {
    memory[name] = kept;
    try {
        disk.setItem(KEY + name, JSON.stringify({ uid: kept.uid, at: kept.at, synced: kept.synced, docs: Array.from(kept.docs.entries()) }));
        logStoredOnce();
    } catch (error) {
        // storage full (QuotaExceededError) or not allowed: the copy stays in memory only, and the stored one is dropped so it cannot go stale
        const full = !!error && (error.name === "QuotaExceededError" || error.name === "NS_ERROR_DOM_QUOTA_REACHED" || error.code === 22 || error.code === 1014);
        console.warn("[store] the " + name + " list is kept in memory only (" + (full ? "browser storage is full" : "browser storage is not available") + ")");
        try { disk.removeItem(KEY + name); } catch (ignored) { /* nothing to do */ }
    }
}
function dirtyOf(name) {
    try { return new Set(JSON.parse(disk.getItem(KEY + "changed." + name) || "[]")); } catch (error) { return new Set(); }
}
function setDirty(name, ids) {
    try { if (ids.size === 0) disk.removeItem(KEY + "changed." + name); else disk.setItem(KEY + "changed." + name, JSON.stringify(Array.from(ids))); }
    catch (error) { delete memory[name]; try { disk.removeItem(KEY + name); } catch (ignored) { /* nothing to do */ } }   // cannot remember what changed: forget the copy
}
/** A document of a kept list was written (or is about to be): it is read again the next time the list is asked for. */
function touch(ref) {
    const name = listOfDoc(ref);
    if (!name || !ref.id) return;
    const ids = dirtyOf(name);
    ids.add(ref.id);
    setDirty(name, ids);
}

/**
 * How many entries of a kept list are in the Recycle bin (binned == true), counted in the kept copy: no read at all.
 * Null when this browser keeps no copy of that list for the signed-in admin (the caller then asks for a count).
 */
export function keptBinnedCount(name) {
    if (!LISTS.includes(name)) return null;
    const kept = load(name);
    if (!kept) return null;
    let n = 0;
    kept.docs.forEach((data) => { if (data && data.binned === true) n++; });
    return n;
}

/** Forgets everything that is kept (sign-out, or another account). */
export function clearStore() {
    LISTS.forEach((name) => { delete memory[name]; });
    try { Object.keys(disk).filter((key) => key.startsWith(KEY)).forEach((key) => disk.removeItem(key)); } catch (error) { /* nothing to clear */ }
}

async function download(name, ref, label) {
    const snapshot = await fb.getDocs(ref);
    noteReads(name + " " + (label || "full download"), Math.max(1, snapshot.size));
    const kept = { uid: uid(), at: Date.now(), synced: Date.now(), docs: new Map(snapshot.docs.map((d) => [d.id, copy(d.data())])) };
    setDirty(name, new Set());
    save(name, kept);
    return kept;
}

const fingerprint = (kept) => JSON.stringify(Array.from(kept.docs.entries()));

/** The list, from the kept copy when there is one. */
function list(name, ref) {
    if (inFlight[name]) return inFlight[name];
    inFlight[name] = (async () => {
        const kept = load(name);
        if (!kept) return download(name, ref);

        // what this site changed since: those documents are read again, and only those
        const ids = Array.from(dirtyOf(name));
        if (ids.length > MANY_CHANGES) return download(name, ref, "full download (many changes made here)");
        if (ids.length > 0) {
            const fresh = await Promise.all(ids.map((id) => fb.getDoc(fb.doc(ref, id))));
            noteReads(name + " changed on this site", ids.length);
            fresh.forEach((snap, i) => { if (snap.exists()) kept.docs.set(ids[i], copy(snap.data())); else kept.docs.delete(ids[i]); });
            setDirty(name, new Set());
            save(name, kept);
        }

        // other people and the app change the data too
        if (!countChecked[name] && Date.now() - (kept.synced || 0) > SYNC_MIN_MS) {
            countChecked[name] = true;
            refreshQuietly(name, ref, kept);
        }
        return kept;
    })();
    const done = () => { delete inFlight[name]; };
    inFlight[name].then(done, done);
    return inFlight[name];
}

/** The documents changed since the last sync (updatedAt and createdAt from the website, addedAtUnix from the phone), merged into `kept`. */
async function incremental(name, ref, kept) {
    const since = Math.max(0, (kept.synced || kept.at) - OVERLAP_MS);
    const stamp = fb.Timestamp.fromMillis(since);
    const asked = await Promise.all([
        fb.getDocs(fb.query(ref, fb.where("updatedAt", ">", stamp))),
        fb.getDocs(fb.query(ref, fb.where("createdAt", ">", stamp))),
        fb.getDocs(fb.query(ref, fb.where("addedAtUnix", ">", Math.floor(since / 1000) - ADDED_OVERLAP_S)))
    ]);
    let n = 0;
    asked.forEach((snapshot) => snapshot.docs.forEach((d) => { n++; kept.docs.set(d.id, copy(d.data())); }));
    noteReads(name + " changed since the last sync", n);
    kept.synced = Date.now();
}

/**
 * The kept list is on screen already. Behind it: one count, then the documents changed since the last sync. A document
 * deleted elsewhere cannot appear in such a query, so the count is compared with the merged copy: when they still
 * differ, the list is downloaded in full, but only once an hour per list on its own (Refresh data does it on request).
 */
async function refreshQuietly(name, ref, kept) {
    try {
        const before = fingerprint(kept);
        const count = (await fb.getCountFromServer(ref)).data().count;
        noteReads(name + " count check", 1);
        await incremental(name, ref, kept);
        let result = kept;
        if (kept.docs.size !== count) {
            const mark = KEY + "autofull." + name;
            let last = 0;
            try { last = Number(disk.getItem(mark)) || 0; } catch (error) { /* none */ }
            if (Date.now() - last > AUTO_FULL_EVERY_MS) {
                try { disk.setItem(mark, String(Date.now())); } catch (error) { /* none */ }
                result = await download(name, ref, "AUTOMATIC full download (counts differed: " + kept.docs.size + " kept, " + count + " on the server)");
            } else {
                console.warn("[reads] the " + name + " counts differ (" + kept.docs.size + " kept, " + count + " on the server), but the automatic download of the last hour was used: press Refresh data.");
                save(name, kept);
            }
        } else save(name, kept);
        if (fingerprint(result) !== before) listeners.forEach((tell) => { try { tell(name); } catch (error) { console.error(error); } });
    } catch (error) { console.warn("Could not check the " + name + " list for changes:", error); }
}

/** The times "Refresh data" was used in the last hour (newest last, at most the hourly cap). */
function refreshLog() {
    try {
        const saved = JSON.parse(localStorage.getItem(REFRESH_LOG_KEY) || "[]");
        const now = Date.now();
        return Array.isArray(saved) ? saved.map(Number).filter((time) => time > now - 3600000 && time <= now + 60000).slice(-REFRESH_MAX_PER_HOUR) : [];
    } catch (error) { return []; }
}

/** How long "Refresh data" is blocked: { seconds, hourly } (hourly = the cap per hour is used up, not just the pause). */
export function refreshWait() {
    const log = refreshLog(), now = Date.now();
    const pause = log.length ? Math.ceil((log[log.length - 1] + REFRESH_COOLDOWN_MS - now) / 1000) : 0;
    const hourly = log.length >= REFRESH_MAX_PER_HOUR ? Math.ceil((log[0] + 3600000 - now) / 1000) : 0;
    return hourly > 0 && hourly > pause ? { seconds: hourly, hourly: true } : { seconds: Math.max(0, pause), hourly: false };
}

/**
 * The "Refresh data" button: forget nothing, download the three lists in full, tell the pages what changed.
 * Returns the seconds still to wait when it is blocked (nothing is downloaded then), else 0.
 */
export async function refreshData() {
    const wait = refreshWait().seconds;
    if (wait > 0) return wait;
    try { localStorage.setItem(REFRESH_LOG_KEY, JSON.stringify(refreshLog().concat(Date.now()))); } catch (error) { /* not kept: the limit then holds only per page */ }
    const before = {};
    LISTS.forEach((name) => { const kept = load(name); before[name] = kept ? fingerprint(kept) : ""; });
    for (const name of LISTS) {
        const fresh = await download(name, fb.collection(db, name), "full download (Refresh data)");
        if (fingerprint(fresh) !== before[name]) listeners.forEach((tell) => { try { tell(name); } catch (error) { console.error(error); } });
    }
    return 0;
}

// another tab saved a newer copy: use that one instead of the one in this tab's memory
window.addEventListener("storage", (event) => { LISTS.forEach((name) => { if (event.key === KEY + name) delete memory[name]; }); });

/** What a page gets: the same shape Firebase's own answer has (docs, forEach, size, empty). */
function snapshotOf(kept) {
    const docs = Array.from(kept.docs.entries()).map(([id, data]) => ({ id, data: () => copy(data), exists: () => true }));
    return { docs, size: docs.length, empty: docs.length === 0, forEach: (visit) => docs.forEach(visit) };
}

// ---------------------------------------------------------------------------------------------
// What pages import in the place of Firebase's own functions
// ---------------------------------------------------------------------------------------------
export async function getDocs(target) {
    const name = wholeList(target);
    if (name === null) { const result = await fb.getDocs(target); noteReads("queries", result.size); return result; }
    return snapshotOf(await list(name, target));
}
export function updateDoc(ref, ...rest) { touch(ref); return fb.updateDoc(ref, ...rest); }
export function setDoc(ref, ...rest) { touch(ref); return fb.setDoc(ref, ...rest); }
export function deleteDoc(ref) { touch(ref); return fb.deleteDoc(ref); }
export async function addDoc(target, data) {
    const made = await fb.addDoc(target, data);
    const name = wholeList(target);
    if (name && made && made.id) { const ids = dirtyOf(name); ids.add(made.id); setDirty(name, ids); }
    return made;
}
export function writeBatch(database) {
    const batch = fb.writeBatch(database);
    const api = {
        set(ref, ...rest) { touch(ref); batch.set(ref, ...rest); return api; },
        update(ref, ...rest) { touch(ref); batch.update(ref, ...rest); return api; },
        delete(ref) { touch(ref); batch.delete(ref); return api; },
        commit: () => batch.commit()
    };
    return api;
}
