// THE ADMIN SITE'S OWN COPY OF ITS THREE LISTS: brands, products and ingredients.
//
// Before this file every page downloaded whole collections each time it opened, and again after every single change:
// opening Brand & Product Inventory read about 715 documents, and so did each save. Now:
//
//   - the first page that needs a list downloads it ONCE and keeps it: in memory, and in this browser tab's own
//     storage (sessionStorage), so it is still there after moving to another page. It is gone when the tab is closed
//     and wiped on sign-out. Only what the signed-in admin's page read itself is kept: nothing more.
//   - a page that opens later gets the kept list AT ONCE: no waiting and no reads;
//   - after a change made on this site, only the documents that were written are read again;
//   - other people and the phone app change the data too, so a kept list is checked with one cheap count per page,
//     and downloaded in full again when the count differs, when the admin reloads the page (F5), or when it is
//     older than FULL_AFTER_MS. An old list is still SHOWN first; the fresh one replaces it quietly when it arrives
//     (onListsRefreshed tells the page).
//
// Pages use it by importing getDocs / addDoc / updateDoc / setDoc / deleteDoc / writeBatch from HERE instead of from
// the Firebase file. The calls look the same. Anything that is not one of the three whole lists (a query, another
// collection) goes straight through to Firebase.
import { auth } from "../_Database/firebase-console.js";
import * as fb from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";

/** The collections that are kept. */
export const LISTS = ["brands", "products", "ingredients"];
/** A kept list older than this is downloaded again in full (other people and the app change the data too). */
export const FULL_AFTER_MS = 5 * 60 * 1000;
/** After more than this many changed documents one full download is simpler than reading them one by one. */
const MANY_CHANGES = 40;
const KEY = "glowup.store.v1.";

// name -> { uid, at, docs: Map(id -> data) }
const memory = {};
const inFlight = {};
const countChecked = {};
const listeners = new Set();
let reads = 0;

// the admin pressed reload (F5): they asked for what is in the database NOW
const isReload = (() => {
    try { const nav = performance.getEntriesByType("navigation")[0]; return !!nav && nav.type === "reload"; } catch (error) { return false; }
})();
const reloadServed = {};

/** How many documents this page has read through the store (for the console, and for measuring). */
export const readsThisPage = () => reads;

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
        const saved = JSON.parse(sessionStorage.getItem(KEY + name) || "null");
        if (saved && saved.uid === me && Array.isArray(saved.docs)) {
            memory[name] = { uid: me, at: Number(saved.at) || 0, docs: new Map(saved.docs.map(([id, data]) => [id, copy(data)])) };
            return memory[name];
        }
    } catch (error) { /* a private window, or a copy that cannot be read: download again */ }
    return null;
}
function save(name, kept) {
    memory[name] = kept;
    try { sessionStorage.setItem(KEY + name, JSON.stringify({ uid: kept.uid, at: kept.at, docs: Array.from(kept.docs.entries()) })); }
    catch (error) { try { sessionStorage.removeItem(KEY + name); } catch (ignored) { /* nothing to do */ } }      // too big, or not allowed: memory only
}
function dirtyOf(name) {
    try { return new Set(JSON.parse(sessionStorage.getItem(KEY + "changed." + name) || "[]")); } catch (error) { return new Set(); }
}
function setDirty(name, ids) {
    try { if (ids.size === 0) sessionStorage.removeItem(KEY + "changed." + name); else sessionStorage.setItem(KEY + "changed." + name, JSON.stringify(Array.from(ids))); }
    catch (error) { delete memory[name]; try { sessionStorage.removeItem(KEY + name); } catch (ignored) { /* nothing to do */ } }   // cannot remember what changed: forget the copy
}
/** A document of a kept list was written (or is about to be): it is read again the next time the list is asked for. */
function touch(ref) {
    const name = listOfDoc(ref);
    if (!name || !ref.id) return;
    const ids = dirtyOf(name);
    ids.add(ref.id);
    setDirty(name, ids);
}

/** Forgets everything that is kept (sign-out, or another account). */
export function clearStore() {
    LISTS.forEach((name) => { delete memory[name]; });
    try { Object.keys(sessionStorage).filter((key) => key.startsWith(KEY)).forEach((key) => sessionStorage.removeItem(key)); } catch (error) { /* nothing to clear */ }
}

async function download(name, ref) {
    const snapshot = await fb.getDocs(ref);
    reads += Math.max(1, snapshot.size);
    const kept = { uid: uid(), at: Date.now(), docs: new Map(snapshot.docs.map((d) => [d.id, copy(d.data())])) };
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
        if (isReload && !reloadServed[name]) { reloadServed[name] = true; return download(name, ref); }

        // what this site changed since: those documents are read again, and only those
        const ids = Array.from(dirtyOf(name));
        if (ids.length > MANY_CHANGES) return download(name, ref);
        if (ids.length > 0) {
            const fresh = await Promise.all(ids.map((id) => fb.getDoc(fb.doc(ref, id))));
            reads += ids.length;
            fresh.forEach((snap, i) => { if (snap.exists()) kept.docs.set(ids[i], copy(snap.data())); else kept.docs.delete(ids[i]); });
            setDirty(name, new Set());
            save(name, kept);
        }

        // other people and the app change the data too
        const old = Date.now() - kept.at > FULL_AFTER_MS;
        if (old || !countChecked[name]) {
            countChecked[name] = true;
            refreshQuietly(name, ref, kept, old);
        }
        return kept;
    })();
    const done = () => { delete inFlight[name]; };
    inFlight[name].then(done, done);
    return inFlight[name];
}

/**
 * The kept list is on screen already. Behind it: one count tells whether documents were added or removed elsewhere;
 * when they were, or when the copy is old, the list is downloaded again and the page is told if it differs.
 */
async function refreshQuietly(name, ref, kept, old) {
    try {
        let needed = old;
        if (!needed) {
            const count = (await fb.getCountFromServer(ref)).data().count;
            reads += 1;
            needed = count !== kept.docs.size;
        }
        if (!needed) return;
        const before = fingerprint(kept);
        const fresh = await download(name, ref);
        if (fingerprint(fresh) !== before) listeners.forEach((tell) => { try { tell(name); } catch (error) { console.error(error); } });
    } catch (error) { console.warn("Could not check the " + name + " list for changes:", error); }
}

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
    if (name === null) return fb.getDocs(target);
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
