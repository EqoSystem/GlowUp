






















import { auth, db } from "../_Database/firebase-console.js";
import * as fb from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";


export const LISTS = ["brands", "products", "ingredients"];

const MANY_CHANGES = 40;
const KEY = "glowup.store.v2.";
const disk = (() => { try { return window.localStorage; } catch (error) { return window.sessionStorage; } })();
const OVERLAP_MS = 5 * 60 * 1000;
const ADDED_OVERLAP_S = 2 * 3600;
const SYNC_MIN_MS = 30 * 1000;
const AUTO_FULL_EVERY_MS = 60 * 60 * 1000;

export const REFRESH_COOLDOWN_MS = 2 * 60 * 1000;
export const REFRESH_MAX_PER_HOUR = 6;
const REFRESH_LOG_KEY = "glowup.refresh.log.v1";


const memory = {};
const inFlight = {};
const countChecked = {};
const listeners = new Set();
let reads = 0;





const breakdown = {};
let logTimer = 0;
let sizeLogged = false;

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

function logStoredOnce() {
    if (sizeLogged) return;
    sizeLogged = true;
    let chars = 0;
    try { Object.keys(disk).filter((key) => key.startsWith(KEY)).forEach((key) => { chars += key.length + (disk.getItem(key) || "").length; }); } catch (error) { return; }
    console.info("[store] the kept copies take about " + Math.round(chars * 2 / 1024) + " KB of browser storage");
}

export async function getCountFromServer(target) {
    const result = await fb.getCountFromServer(target);
    noteReads("count queries", 1);
    return result;
}

export function onSnapshot(target, next, fail) {
    return fb.onSnapshot(target, (snapshot) => { noteReads("live listener", snapshot.docChanges().length); next(snapshot); }, fail);
}


export function onListsRefreshed(callback) { listeners.add(callback); return () => listeners.delete(callback); }




class Stamp {
    constructor(seconds, nanoseconds) { this.seconds = seconds; this.nanoseconds = nanoseconds || 0; }
    toMillis() { return this.seconds * 1000 + Math.floor(this.nanoseconds / 1e6); }
    toDate() { return new Date(this.toMillis()); }
    toJSON() { return { seconds: this.seconds, nanoseconds: this.nanoseconds }; }
}
const isStampShape = (v) => v !== null && typeof v === "object" && typeof v.seconds === "number" && typeof v.nanoseconds === "number" && Object.keys(v).length === 2;


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





function wholeList(target) {
    return target && target.type === "collection" && LISTS.includes(target.id) ? target.id : null;
}

function listOfDoc(ref) {
    const name = ref && ref.parent ? ref.parent.id : null;
    return name && LISTS.includes(name) ? name : null;
}
const uid = () => (auth.currentUser ? auth.currentUser.uid : "");




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
    } catch (error) {                                                                       }
    return null;
}
function save(name, kept) {
    memory[name] = kept;
    try {
        disk.setItem(KEY + name, JSON.stringify({ uid: kept.uid, at: kept.at, synced: kept.synced, docs: Array.from(kept.docs.entries()) }));
        logStoredOnce();
    } catch (error) {

        const full = !!error && (error.name === "QuotaExceededError" || error.name === "NS_ERROR_DOM_QUOTA_REACHED" || error.code === 22 || error.code === 1014);
        console.warn("[store] the " + name + " list is kept in memory only (" + (full ? "browser storage is full" : "browser storage is not available") + ")");
        try { disk.removeItem(KEY + name); } catch (ignored) {                     }
    }
}
function dirtyOf(name) {
    try { return new Set(JSON.parse(disk.getItem(KEY + "changed." + name) || "[]")); } catch (error) { return new Set(); }
}
function setDirty(name, ids) {
    try { if (ids.size === 0) disk.removeItem(KEY + "changed." + name); else disk.setItem(KEY + "changed." + name, JSON.stringify(Array.from(ids))); }
    catch (error) { delete memory[name]; try { disk.removeItem(KEY + name); } catch (ignored) {                     } }
}

function touch(ref) {
    const name = listOfDoc(ref);
    if (!name || !ref.id) return;
    const ids = dirtyOf(name);
    ids.add(ref.id);
    setDirty(name, ids);
}





export function keptBinnedCount(name) {
    if (!LISTS.includes(name)) return null;
    const kept = load(name);
    if (!kept) return null;
    let n = 0;
    kept.docs.forEach((data) => { if (data && data.binned === true) n++; });
    return n;
}


export function clearStore() {
    LISTS.forEach((name) => { delete memory[name]; });
    try { Object.keys(disk).filter((key) => key.startsWith(KEY)).forEach((key) => disk.removeItem(key)); } catch (error) {                        }
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


function list(name, ref) {
    if (inFlight[name]) return inFlight[name];
    inFlight[name] = (async () => {
        const kept = load(name);
        if (!kept) return download(name, ref);


        const ids = Array.from(dirtyOf(name));
        if (ids.length > MANY_CHANGES) return download(name, ref, "full download (many changes made here)");
        if (ids.length > 0) {
            const fresh = await Promise.all(ids.map((id) => fb.getDoc(fb.doc(ref, id))));
            noteReads(name + " changed on this site", ids.length);
            fresh.forEach((snap, i) => { if (snap.exists()) kept.docs.set(ids[i], copy(snap.data())); else kept.docs.delete(ids[i]); });
            setDirty(name, new Set());
            save(name, kept);
        }


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
            try { last = Number(disk.getItem(mark)) || 0; } catch (error) {            }
            if (Date.now() - last > AUTO_FULL_EVERY_MS) {
                try { disk.setItem(mark, String(Date.now())); } catch (error) {            }
                result = await download(name, ref, "AUTOMATIC full download (counts differed: " + kept.docs.size + " kept, " + count + " on the server)");
            } else {
                console.warn("[reads] the " + name + " counts differ (" + kept.docs.size + " kept, " + count + " on the server), but the automatic download of the last hour was used: press Refresh data.");
                save(name, kept);
            }
        } else save(name, kept);
        if (fingerprint(result) !== before) listeners.forEach((tell) => { try { tell(name); } catch (error) { console.error(error); } });
    } catch (error) { console.warn("Could not check the " + name + " list for changes:", error); }
}


function refreshLog() {
    try {
        const saved = JSON.parse(localStorage.getItem(REFRESH_LOG_KEY) || "[]");
        const now = Date.now();
        return Array.isArray(saved) ? saved.map(Number).filter((time) => time > now - 3600000 && time <= now + 60000).slice(-REFRESH_MAX_PER_HOUR) : [];
    } catch (error) { return []; }
}


export function refreshWait() {
    const log = refreshLog(), now = Date.now();
    const pause = log.length ? Math.ceil((log[log.length - 1] + REFRESH_COOLDOWN_MS - now) / 1000) : 0;
    const hourly = log.length >= REFRESH_MAX_PER_HOUR ? Math.ceil((log[0] + 3600000 - now) / 1000) : 0;
    return hourly > 0 && hourly > pause ? { seconds: hourly, hourly: true } : { seconds: Math.max(0, pause), hourly: false };
}





export async function refreshData() {
    const wait = refreshWait().seconds;
    if (wait > 0) return wait;
    try { localStorage.setItem(REFRESH_LOG_KEY, JSON.stringify(refreshLog().concat(Date.now()))); } catch (error) {                                                    }
    const before = {};
    LISTS.forEach((name) => { const kept = load(name); before[name] = kept ? fingerprint(kept) : ""; });
    for (const name of LISTS) {
        const fresh = await download(name, fb.collection(db, name), "full download (Refresh data)");
        if (fingerprint(fresh) !== before[name]) listeners.forEach((tell) => { try { tell(name); } catch (error) { console.error(error); } });
    }
    return 0;
}


window.addEventListener("storage", (event) => { LISTS.forEach((name) => { if (event.key === KEY + name) delete memory[name]; }); });


function snapshotOf(kept) {
    const docs = Array.from(kept.docs.entries()).map(([id, data]) => ({ id, data: () => copy(data), exists: () => true }));
    return { docs, size: docs.length, empty: docs.length === 0, forEach: (visit) => docs.forEach(visit) };
}




export async function getDocs(target) {
    const name = wholeList(target);
    if (name === null) { const result = await fb.getDocs(target); noteReads("queries", result.size); return result; }
    return snapshotOf(await list(name, target));
}










export const AUDIT_COLLECTION = "auditLog";
const AUDIT_MOST_ITEMS = 450;
function auditItem(action, ref, rest) {
    let fields = [];
    if (typeof rest[0] === "string") fields = rest.filter((value, index) => index % 2 === 0 && typeof value === "string");
    else if (rest[0] && typeof rest[0] === "object") fields = Object.keys(rest[0]);
    const data = rest[0] && typeof rest[0] === "object" ? rest[0] : {};
    const item = { collection: String((ref.parent && ref.parent.id) || ""), docId: String(ref.id || ""), action, fields: fields.filter((f) => f !== "updatedAt").slice(0, 30) };
    if (typeof data.name === "string" && data.name) item.name = data.name.slice(0, 120);
    return item;
}
function auditRecord(items) {
    const user = auth.currentUser;
    return {
        uid: user ? user.uid : "", email: user && user.email ? user.email : "",
        at: fb.serverTimestamp(), atUnix: Math.floor(Date.now() / 1000),
        page: String((typeof location !== "undefined" && location.pathname) || "").split("/").filter(Boolean).slice(-1)[0] || "",
        count: items.length, items: items.slice(0, AUDIT_MOST_ITEMS)
    };
}
const logged = (ref) => !(ref.parent && ref.parent.id === AUDIT_COLLECTION);

export function writeBatch(database) {
    const batch = fb.writeBatch(database || db);
    const items = [];
    const api = {
        set(ref, ...rest) { touch(ref); batch.set(ref, ...rest); if (logged(ref)) items.push(auditItem("set", ref, rest)); return api; },
        update(ref, ...rest) { touch(ref); batch.update(ref, ...rest); if (logged(ref)) items.push(auditItem("update", ref, rest)); return api; },
        delete(ref) { touch(ref); batch.delete(ref); if (logged(ref)) items.push(auditItem("delete", ref, [])); return api; },
        commit: () => {
            if (items.length > 0) batch.set(fb.doc(fb.collection(db, AUDIT_COLLECTION)), auditRecord(items));
            return batch.commit();
        }
    };
    return api;
}
export function updateDoc(ref, ...rest) { return writeBatch(db).update(ref, ...rest).commit(); }
export function setDoc(ref, ...rest) { return writeBatch(db).set(ref, ...rest).commit(); }
export function deleteDoc(ref) { return writeBatch(db).delete(ref).commit(); }
export async function addDoc(target, data) {
    const made = fb.doc(target);
    await writeBatch(db).set(made, data).commit();
    return made;
}
