
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
// Password reset: window.__RESET_ERROR = an error code makes it fail; window.__RESET_HANG = true never answers; each request is noted.
export async function sendPasswordResetEmail(a, email, settings) {
    window.__RESET_CALLS = (window.__RESET_CALLS || []).concat([{ email, url: settings && settings.url }]);
    if (window.__RESET_HANG) return new Promise(() => { });
    await new Promise((r) => setTimeout(r, LAT()));
    if (window.__RESET_ERROR) { const e = new Error("reset"); e.code = window.__RESET_ERROR; throw e; }
}
