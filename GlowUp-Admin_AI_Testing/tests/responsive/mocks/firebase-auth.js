
const LAT = () => (window.__LAT && window.__LAT.auth) || 0;
const USER = { uid: "admin1", email: "admin@example.test", metadata: { lastSignInTime: "Sat, 21 Nov 2026 06:10:00 GMT" }, getIdToken: async () => "test-token", getIdTokenResult: async () => ({ claims: { admin: true } }) };
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
    const known = window.__USERS && window.__USERS[email];
    if (known) {
        if (password !== known.password) { const e = new Error("bad"); e.code = "auth/invalid-credential"; throw e; }
        auth.currentUser = makeUser(email, known.uid, !!known.verified); return { user: auth.currentUser };
    }
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

// Admin sign-up (20 November 2026). window.__USERS = { email: { password, uid, verified } } are accounts that exist already
// (createUser answers email-already-in-use for them); window.__VERIFIED = true makes reload() see a confirmed e-mail.
function makeUser(email, uid, verified) {
    return { uid, email: String(email).toLowerCase(), emailVerified: verified, getIdToken: async () => "test-token", getIdTokenResult: async () => ({ claims: {} }) };
}
export async function createUserWithEmailAndPassword(a, email, password) {
    window.__CREATE_CALLS = (window.__CREATE_CALLS || 0) + 1;
    await new Promise((r) => setTimeout(r, LAT()));
    if (window.__CREATE_HANG) return new Promise(() => { });
    if (window.__USERS && window.__USERS[email]) { const e = new Error("in use"); e.code = "auth/email-already-in-use"; throw e; }
    if (window.__CREATE_ERROR) { const e = new Error("create"); e.code = window.__CREATE_ERROR; throw e; }
    auth.currentUser = makeUser(email, "new1", false);
    listeners.forEach((cb) => setTimeout(() => cb(auth.currentUser), 0));       // Firebase tells its listeners about the new session
    return { user: auth.currentUser };
}
export async function sendEmailVerification(user, settings) {
    window.__VERIFY_CALLS = (window.__VERIFY_CALLS || []).concat([{ email: user.email, url: settings && settings.url }]);
    await new Promise((r) => setTimeout(r, LAT()));
    if (window.__VERIFY_ERROR) { const e = new Error("verify"); e.code = window.__VERIFY_ERROR; throw e; }
}
export async function updateProfile(user, profile) { user.displayName = profile.displayName; }
export async function reload(user) { if (window.__VERIFIED) user.emailVerified = true; }
// Edit Profile (22 Nov 2026): the current password is checked against window.__PASSWORD (default "Passw0rd!")
export const EmailAuthProvider = { credential: (email, password) => ({ email, password }) };
export async function reauthenticateWithCredential(user, credential) {
    if (credential.password !== (window.__PASSWORD || "Passw0rd!")) { const e = new Error("wrong"); e.code = "auth/invalid-credential"; throw e; }
    return { user };
}
export async function updatePassword(user, password) { window.__PASSWORD = password; window.__PASSWORD_CHANGED = (window.__PASSWORD_CHANGED || 0) + 1; }
// e-mail link actions (authAction.js): window.__OOB = { code: email } holds the codes that still work
export async function verifyPasswordResetCode(a, code) {
    const email = window.__OOB && window.__OOB[code];
    if (!email) { const e = new Error("bad"); e.code = "auth/invalid-action-code"; throw e; }
    return email;
}
export async function confirmPasswordReset(a, code, password) {
    if (!window.__OOB || !window.__OOB[code]) { const e = new Error("bad"); e.code = "auth/expired-action-code"; throw e; }
    window.__RESET_TO = password; delete window.__OOB[code];
}
export async function applyActionCode(a, code) {
    if (!window.__OOB || !window.__OOB[code]) { const e = new Error("bad"); e.code = "auth/invalid-action-code"; throw e; }
    delete window.__OOB[code];
}
