// Auth stand-in. window.__SIGNED_IN decides whether an admin is signed in; window.__LAT.auth delays the answer.
const LAT = () => (window.__LAT && window.__LAT.auth) || 0;
const USER = { uid: "admin1", email: "admin@example.test", getIdToken: async () => "test-token", getIdTokenResult: async () => ({ claims: { admin: true } }) };
const auth = { currentUser: null };
const listeners = new Set();
export const getAuth = () => auth;
export function onAuthStateChanged(a, cb) {
    listeners.add(cb);
    setTimeout(() => { auth.currentUser = window.__SIGNED_IN ? USER : null; cb(auth.currentUser); }, LAT());
    return () => listeners.delete(cb);
}
export async function signInWithEmailAndPassword(a, email, password) {
    await new Promise((r) => setTimeout(r, LAT()));
    if (password !== "right") { const e = new Error("bad"); e.code = "auth/invalid-credential"; throw e; }
    auth.currentUser = USER; return { user: USER };
}
export async function signOut() { auth.currentUser = null; listeners.forEach((cb) => cb(null)); }
export const setPersistence = async () => { };
export const browserSessionPersistence = {};
export const browserLocalPersistence = {};
