// Removes the Firebase Auth sign-in account of users that an admin deletes permanently from the Recycle Bin.
//   POST { uids: ["..."] }   header Authorization: Bearer <admin's Firebase ID token>
// The browser can only delete Firestore documents; deleting somebody else's Auth account needs the service account, so it happens here.
// Safety (all checked on the server): the caller is an active admin; every uid has a users/{uid} document that is in the Recycle Bin
// (inBin true, status "deleted"); no uid is the caller's own or has an admins/{uid} document. At most 25 uids per request.
// A uid whose Auth account is already gone counts as removed. Environment variable: FIREBASE_SERVICE_ACCOUNT (already used by email-otp).
// Free plan: a few small calls per request; no Cloud Functions, no billing.
const crypto = require("crypto");
const { adminUid } = require("./_adminAuth");

const PROJECT = "glowup-admin-portal";
const DOCS = `https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents`;
const MAX_UIDS = 25, MAX_BODY = 8 * 1024;

function send(res, status, body) {
    res.statusCode = status;
    res.setHeader("Content-Type", "application/json; charset=utf-8");
    res.setHeader("Cache-Control", "no-store");
    res.end(JSON.stringify(body));
}
async function timed(url, options, seconds) {
    const stop = new AbortController();
    const timer = setTimeout(() => stop.abort(), seconds * 1000);
    try { return await fetch(url, { ...options, signal: stop.signal }); } finally { clearTimeout(timer); }
}

const LF = String.fromCharCode(10), BSN = String.fromCharCode(92) + String.fromCharCode(110);
function parseAccount(raw) {
    let t = String(raw || "").trim();
    for (let i = 0; i < 2; i++) {
        try { const v = JSON.parse(t); if (typeof v === "string") { t = v.trim(); continue; } return v; } catch (e) { }
        try { return JSON.parse(t.split(LF).join(BSN)); } catch (e) { }
        break;
    }
    return null;
}
async function serviceToken(account) {
    const now = Math.floor(Date.now() / 1000);
    const b = (o) => Buffer.from(JSON.stringify(o)).toString("base64url");
    const unsigned = b({ alg: "RS256", typ: "JWT" }) + "." + b({ iss: account.client_email, scope: "https://www.googleapis.com/auth/cloud-platform", aud: "https://oauth2.googleapis.com/token", iat: now, exp: now + 3000 });
    const signature = crypto.createSign("RSA-SHA256").update(unsigned).sign(account.private_key).toString("base64url");
    const r = await timed("https://oauth2.googleapis.com/token", { method: "POST", headers: { "Content-Type": "application/x-www-form-urlencoded" },
        body: "grant_type=" + encodeURIComponent("urn:ietf:params:oauth:grant-type:jwt-bearer") + "&assertion=" + unsigned + "." + signature }, 6);
    if (!r.ok) throw new Error("service token refused");
    return (await r.json()).access_token;
}

async function readBody(req) {
    if (req.body !== undefined && req.body !== null) return typeof req.body === "string" ? req.body : JSON.stringify(req.body);
    let size = 0; const parts = [];
    for await (const chunk of req) { size += chunk.length; if (size > MAX_BODY) return null; parts.push(chunk); }
    return Buffer.concat(parts).toString("utf8");
}

async function exists(token, path) {
    const r = await timed(`${DOCS}/${path}`, { headers: { Authorization: "Bearer " + token } }, 6);
    if (r.status === 404) return { found: false };
    if (!r.ok) throw new Error("read failed");
    return { found: true, fields: (await r.json()).fields || {} };
}

async function removeOne(token, uid, callerUid) {
    if (uid === callerUid) return "refused: your own account";
    if ((await exists(token, "admins/" + encodeURIComponent(uid))).found) return "refused: an admin account";
    const u = await exists(token, "users/" + encodeURIComponent(uid));
    // the profile may already be gone when an earlier attempt removed it first; then the Auth account is all that is left
    if (u.found) {
        const f = u.fields;
        const inBin = f.inBin && f.inBin.booleanValue === true;
        const status = f.status && f.status.stringValue;
        if (!inBin || status !== "deleted") return "refused: not in the Recycle Bin";
    }
    const r = await timed(`https://identitytoolkit.googleapis.com/v1/projects/${PROJECT}/accounts:delete`,
        { method: "POST", headers: { Authorization: "Bearer " + token, "Content-Type": "application/json" }, body: JSON.stringify({ localId: uid }) }, 8);
    if (r.ok) return "removed";
    const text = await r.text().catch(() => "");
    return /USER_NOT_FOUND/.test(text) ? "removed" : "failed";
}

module.exports = async (req, res) => {
    try {
        if (req.method !== "POST") return send(res, 405, { error: "Use POST." });
        const account = parseAccount(process.env.FIREBASE_SERVICE_ACCOUNT);
        if (!account || !account.client_email || !account.private_key) return send(res, 503, { error: "not-set-up" });
        const token0 = String((req.headers && req.headers.authorization) || "").replace(/^Bearer\s+/i, "");
        const caller = await adminUid(token0);
        if (!caller) return send(res, 401, { error: "sign-in" });
        const text = await readBody(req);
        if (text === null) return send(res, 413, { error: "too-large" });
        let body; try { body = JSON.parse(text || "{}"); } catch (e) { return send(res, 400, { error: "unreadable" }); }
        const uids = Array.from(new Set((Array.isArray(body.uids) ? body.uids : []).map(String)));
        if (uids.length === 0 || uids.length > MAX_UIDS || uids.some((u) => !/^[A-Za-z0-9_-]{3,128}$/.test(u))) return send(res, 400, { error: "bad-uids" });
        const token = await serviceToken(account);
        const results = {};
        for (const uid of uids) { try { results[uid] = await removeOne(token, uid, caller); } catch (e) { results[uid] = "failed"; } }
        return send(res, 200, { results });
    } catch (e) {
        return send(res, 500, { error: "server" });
    }
};
module.exports.removeOne = removeOne;
