const PROJECT = "glowup-admin-portal";
const FIREBASE_WEB_KEY = "AIzaSyAkb23xQ69a2Q4tpucGty9yDEoH0ab1JDM";
const KEY_REFERER = "https://glowup-admin-snowy.vercel.app/";

async function withTimeout(url, options, seconds) {
    const stop = new AbortController();
    const timer = setTimeout(() => stop.abort(), seconds * 1000);
    try { return await fetch(url, { ...options, signal: stop.signal }); } finally { clearTimeout(timer); }
}

async function adminUid(idToken) {
    if (!idToken) return "";
    const who = await withTimeout("https://identitytoolkit.googleapis.com/v1/accounts:lookup?key=" + FIREBASE_WEB_KEY,
        { method: "POST", headers: { "Content-Type": "application/json", Referer: KEY_REFERER }, body: JSON.stringify({ idToken }) }, 10);
    if (!who.ok) return "";
    const user = ((await who.json()).users || [])[0];
    if (!user || !user.localId) return "";
    const entry = await withTimeout(`https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents/admins/${encodeURIComponent(user.localId)}`,
        { headers: { Authorization: "Bearer " + idToken } }, 10);
    if (!entry.ok) return "";
    // an archived or binned admin keeps the record but loses access (same as isAdmin() in firestore.rules)
    const f = ((await entry.json().catch(() => ({}))) || {}).fields || {};
    const status = (f.status && f.status.stringValue) || "active";
    if (status !== "active" || (f.inBin && f.inBin.booleanValue === true)) return "";
    return user.localId;
}

module.exports = { adminUid };
