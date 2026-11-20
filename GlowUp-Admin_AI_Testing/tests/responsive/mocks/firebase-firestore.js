
const LAT = () => (window.__LAT && window.__LAT.read) || 0;
const wait = (ms) => new Promise((r) => setTimeout(r, ms));

export class Timestamp {
    constructor(seconds, nanoseconds) { this.seconds = seconds; this.nanoseconds = nanoseconds || 0; }
    static now() { return Timestamp.fromMillis(Date.now()); }
    static fromMillis(ms) { return new Timestamp(Math.floor(ms / 1000), (ms % 1000) * 1e6); }
    static fromDate(d) { return Timestamp.fromMillis(d.getTime()); }
    toMillis() { return this.seconds * 1000 + Math.floor(this.nanoseconds / 1e6); }
    toDate() { return new Date(this.toMillis()); }
    valueOf() { return String(this.toMillis()).padStart(16, "0"); }
}
const SERVER_TS = { __serverTs: true };
const DELETE = { __delete: true };
export const serverTimestamp = () => SERVER_TS;
export const deleteField = () => DELETE;

function revive(v) {
    if (v && typeof v === "object") {
        if (typeof v.__ts === "number") return Timestamp.fromMillis(v.__ts);
        if (Array.isArray(v)) return v.map(revive);
        const o = {}; for (const k in v) o[k] = revive(v[k]); return o;
    }
    return v;
}
const store = new Map();
(function seed() {
    const s = window.__SEED || {};
    for (const col in s) for (const id in s[col]) store.set(col + "/" + id, revive(s[col][id]));
})();
window.__STORE = store;

const db = { type: "firestore" };
export const getFirestore = () => db;
let autoId = 0;
export function collection(parent, ...segs) {
    const base = parent && parent.path ? parent.path + "/" : "";
    const path = base + segs.join("/");
    return { type: "collection", path, id: segs[segs.length - 1] };
}
export function doc(parent, ...segs) {
    if (parent && parent.type === "collection") {
        const id = segs.length ? segs.join("/") : "auto" + (++autoId) + Math.random().toString(36).slice(2, 8);
        return { type: "doc", path: parent.path + "/" + id, id, parent };
    }
    const path = segs.join("/");
    const parts = path.split("/");
    return { type: "doc", path, id: parts[parts.length - 1], parent: collection(db, ...parts.slice(0, -1)) };
}
export const where = (field, op, value) => ({ kind: "where", field, op, value });
export const orderBy = (field, dir) => ({ kind: "orderBy", field, dir: dir || "asc" });
export const limit = (n) => ({ kind: "limit", n });
export const startAfter = (...v) => ({ kind: "noop" });
export const documentId = () => "__name__";
export function query(target, ...constraints) {
    return { type: "query", path: target.path, constraints: (target.constraints || []).concat(constraints) };
}
const cmpVal = (v) => (v instanceof Timestamp ? v.toMillis() : v);
const get = (data, field) => field.split(".").reduce((o, k) => (o == null ? undefined : o[k]), data);
function test(data, c) {
    const a = cmpVal(get(data, c.field)), b = cmpVal(c.value);
    switch (c.op) {
        case "==": return a === b;
        case "!=": return a !== undefined && a !== b;
        case "<": return a !== undefined && a < b;
        case "<=": return a !== undefined && a <= b;
        case ">": return a !== undefined && a > b;
        case ">=": return a !== undefined && a >= b;
        case "in": return (c.value || []).includes(a);
        case "not-in": return a !== undefined && !(c.value || []).includes(a);
        case "array-contains": return Array.isArray(a) && a.includes(b);
        case "array-contains-any": return Array.isArray(a) && a.some((x) => (c.value || []).includes(x));
    }
    return true;
}
function snapDoc(path, data) {
    const id = path.split("/").pop();
    return { id, ref: doc(db, path), exists: () => data !== undefined, data: () => (data === undefined ? undefined : structuredCloneTs(data)), get: (f) => get(data || {}, f) };
}
function structuredCloneTs(v) {
    if (v instanceof Timestamp) return new Timestamp(v.seconds, v.nanoseconds);
    if (Array.isArray(v)) return v.map(structuredCloneTs);
    if (v && typeof v === "object") { const o = {}; for (const k in v) o[k] = structuredCloneTs(v[k]); return o; }
    return v;
}
function run(target) {
    const prefix = target.path + "/";
    let rows = [];
    for (const [path, data] of store) {
        if (!path.startsWith(prefix)) continue;
        if (path.slice(prefix.length).includes("/")) continue;
        rows.push([path, data]);
    }
    const cs = target.constraints || [];
    cs.filter((c) => c.kind === "where").forEach((c) => { rows = rows.filter(([, d]) => test(d, c)); });
    const orders = cs.filter((c) => c.kind === "orderBy");
    if (orders.length) rows.sort((x, y) => {
        for (const o of orders) { const a = cmpVal(get(x[1], o.field)), b = cmpVal(get(y[1], o.field)); if (a === b) continue; const r = a < b ? -1 : 1; return o.dir === "desc" ? -r : r; }
        return 0;
    });
    const lim = cs.find((c) => c.kind === "limit");
    if (lim) rows = rows.slice(0, lim.n);
    const docs = rows.map(([p, d]) => snapDoc(p, d));
    return { docs, size: docs.length, empty: docs.length === 0, forEach: (f) => docs.forEach(f), docChanges: () => docs.map((d) => ({ type: "added", doc: d })) };
}
export async function getDocs(target) { await wait(LAT()); return run(target); }
export async function getDoc(ref) { await wait(LAT()); return snapDoc(ref.path, store.get(ref.path)); }
export async function getCountFromServer(target) { await wait(LAT()); const r = run(target); return { data: () => ({ count: r.size }) }; }
// Live like Firestore: every listener answers once, then again after each committed write (window.__NOTIFY() after
// a test changes window.__STORE by hand). window.__SNAP_ERROR = a code makes new listeners fail with it.
const liveListeners = new Set();
const answer = (l) => { if (l.target.type === "doc") l.next(snapDoc(l.target.path, store.get(l.target.path))); else l.next(run(l.target)); };
function notifyAll() { liveListeners.forEach((l) => { try { answer(l); } catch (e) { } }); }
window.__NOTIFY = notifyAll;
window.__LIVE = () => liveListeners.size;
export function onSnapshot(target, next, fail) {
    const l = { target, next };
    setTimeout(() => {
        if (window.__SNAP_ERROR) { if (fail) fail(Object.assign(new Error("pretend"), { code: window.__SNAP_ERROR })); return; }
        liveListeners.add(l); answer(l);
    }, LAT());
    return () => { liveListeners.delete(l); };
}
function applyWrite(path, data, merge) {
    const old = merge ? Object.assign({}, store.get(path) || {}) : {};
    for (const k in data) {
        const v = data[k];
        if (v === DELETE) { delete old[k]; continue; }
        old[k] = v === SERVER_TS ? Timestamp.now() : v;
    }
    store.set(path, old);
}
export function writeBatch() {
    const ops = [];
    const b = {
        set(ref, data, opts) { ops.push(() => applyWrite(ref.path, data, opts && opts.merge)); return b; },
        update(ref, ...rest) {
            const data = typeof rest[0] === "string" ? { [rest[0]]: rest[1] } : rest[0];
            ops.push(() => applyWrite(ref.path, data, true)); return b;
        },
        delete(ref) { ops.push(() => store.delete(ref.path)); return b; },
        async commit() { await wait(LAT()); if (window.__FAIL_WRITES) throw Object.assign(new Error("pretend failure"), { code: window.__FAIL_WRITES }); ops.forEach((f) => f()); window.__WRITES = (window.__WRITES || 0) + 1; setTimeout(notifyAll, 0); }
    };
    return b;
}
export const setDoc = (ref, data, opts) => writeBatch().set(ref, data, opts).commit();
export const updateDoc = (ref, ...rest) => writeBatch().update(ref, ...rest).commit();
export const deleteDoc = (ref) => writeBatch().delete(ref).commit();
export async function addDoc(col, data) { const ref = doc(col); await setDoc(ref, data); return ref; }
export async function runTransaction(d, fn) { return fn({ get: getDoc, set: (r, x, o) => applyWrite(r.path, x, o && o.merge), update: (r, x) => applyWrite(r.path, x, true), delete: (r) => store.delete(r.path) }); }
export const increment = (n) => n;
export const arrayUnion = (...v) => v;
export const arrayRemove = () => [];
export const enableIndexedDbPersistence = async () => { };
export const initializeFirestore = () => db;
