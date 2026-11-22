// 6-digit e-mail code for the admin sign-up (and the app's sign-up), sent from a Gmail address.
//   POST { action: "send",   idToken }        -> the signed-in, not yet confirmed account gets a 6-digit code by e-mail
//   POST { action: "verify", idToken, code }  -> a right code marks the account's e-mail as confirmed (Firebase Auth emailVerified = true)
// No code is stored: it is an HMAC of the account id and a 10-minute window (a code is good for the window it was made in and the one after).
// Wrong guesses are counted per account in Firestore (otpAttempts/{uid}, written only by this function with the service account; the rules give
// clients no access), 5 per 15 minutes; when the count cannot be read or written the answer is "no" (fail closed).
// Environment variables (Vercel only, never in the repository): GLOWUP_OTP_SECRET (long random text), GMAIL_USER, GMAIL_APP_PASSWORD
// (a Google "app password" of that Gmail account), FIREBASE_SERVICE_ACCOUNT (the service-account JSON, one line).
// Until all of them exist the function answers 503 "not set up" and the pages keep using Firebase's confirmation link.
const crypto = require("crypto");
const tls = require("tls");

const PROJECT = "glowup-admin-portal";
const FIREBASE_WEB_KEY = "AIzaSyAkb23xQ69a2Q4tpucGty9yDEoH0ab1JDM";
const KEY_REFERER = "https://glowup-admin-snowy.vercel.app/";
const WINDOW_MS = 10 * 60 * 1000;
const MAX_WRONG = 5, LOCK_MS = 15 * 60 * 1000;
const SEND_EVERY_MS = 45 * 1000, SEND_PER_HOUR = 5;
const MAX_BODY = 8 * 1024;

const sentAt = new Map();              // uid -> [timestamps] (best effort per instance; the e-mail service has its own limits)

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

// ---- the code ----
function codeFor(secret, uid, windowIndex) {
    const h = crypto.createHmac("sha256", secret).update(uid + "|" + windowIndex).digest();
    return String(h.readUInt32BE(0) % 1000000).padStart(6, "0");
}
function currentWindow(now) { return Math.floor((now === undefined ? Date.now() : now) / WINDOW_MS); }
function codeIsRight(secret, uid, typed, now) {
    const t = String(typed || "").trim();
    if (!/^\d{6}$/.test(t)) return false;
    const w = currentWindow(now);
    let ok = false;
    for (const idx of [w, w - 1]) {
        const a = Buffer.from(codeFor(secret, uid, idx)), b = Buffer.from(t);
        if (a.length === b.length && crypto.timingSafeEqual(a, b)) ok = true;
    }
    return ok;
}

// ---- Gmail (SMTP over TLS, port 465) ----
function smtpSend({ user, pass, to, subject, text }, connect) {
    return new Promise((resolve, reject) => {
        const socket = (connect || ((o) => tls.connect(o)))({ host: "smtp.gmail.com", port: 465, servername: "smtp.gmail.com" });
        let buffer = "", step = 0, done = false;
        const finish = (error) => { if (done) return; done = true; try { socket.end(); } catch (e) { } error ? reject(error) : resolve(); };
        const timer = setTimeout(() => finish(new Error("mail timeout")), 8000);
        const b64 = (s) => Buffer.from(s, "utf8").toString("base64");
        const clean = (s) => String(s).replace(/[\r\n]+/g, " ");
        const body = ["From: GlowUp <" + clean(user) + ">", "To: " + clean(to), "Subject: " + clean(subject), "MIME-Version: 1.0", "Content-Type: text/plain; charset=utf-8", "", String(text).replace(/^\./gm, "..")].join("\r\n");
        const script = [
            [220, "EHLO glowupapp.xyz"], [250, "AUTH LOGIN"], [334, b64(user)], [334, b64(pass)], [235, "MAIL FROM:<" + clean(user) + ">"],
            [250, "RCPT TO:<" + clean(to) + ">"], [250, "DATA"], [354, body + "\r\n."], [250, "QUIT"]
        ];
        socket.on("data", (chunk) => {
            buffer += chunk.toString("utf8");
            let lines = buffer.split("\r\n"); buffer = lines.pop();
            for (const line of lines) {
                if (/^\d{3}-/.test(line)) continue;                            // a continuation line of a multi-line answer
                const code = Number(line.slice(0, 3));
                if (step >= script.length) { clearTimeout(timer); finish(); return; }
                if (code !== script[step][0]) { clearTimeout(timer); finish(new Error("mail refused: " + code)); return; }
                const next = script[step][1]; step++;
                socket.write(next + "\r\n");
            }
        });
        socket.on("error", (e) => { clearTimeout(timer); finish(e); });
        socket.on("close", () => { clearTimeout(timer); if (!done) finish(step >= script.length ? undefined : new Error("mail closed early")); });
    });
}

// ---- Google sign-in for the function itself (service account) ----
let tokenCache = { token: "", until: 0 };
async function serviceToken(account) {
    if (tokenCache.token && Date.now() < tokenCache.until) return tokenCache.token;
    const now = Math.floor(Date.now() / 1000);
    const b = (o) => Buffer.from(JSON.stringify(o)).toString("base64url");
    const unsigned = b({ alg: "RS256", typ: "JWT" }) + "." + b({ iss: account.client_email, scope: "https://www.googleapis.com/auth/cloud-platform", aud: "https://oauth2.googleapis.com/token", iat: now, exp: now + 3000 });
    const signature = crypto.createSign("RSA-SHA256").update(unsigned).sign(account.private_key).toString("base64url");
    const r = await timed("https://oauth2.googleapis.com/token", { method: "POST", headers: { "Content-Type": "application/x-www-form-urlencoded" },
        body: "grant_type=" + encodeURIComponent("urn:ietf:params:oauth:grant-type:jwt-bearer") + "&assertion=" + unsigned + "." + signature }, 6);
    if (!r.ok) throw new Error("service token refused");
    const j = await r.json();
    tokenCache = { token: j.access_token, until: Date.now() + 50 * 60 * 1000 };
    return tokenCache.token;
}

async function signedInUser(idToken) {
    if (!idToken || idToken.length > 4096) return null;
    const who = await timed("https://identitytoolkit.googleapis.com/v1/accounts:lookup?key=" + FIREBASE_WEB_KEY,
        { method: "POST", headers: { "Content-Type": "application/json", Referer: KEY_REFERER }, body: JSON.stringify({ idToken }) }, 6);
    if (!who.ok) return null;
    const u = ((await who.json()).users || [])[0];
    return u && u.localId && u.email && !u.disabled ? { uid: u.localId, email: u.email, verified: u.emailVerified === true } : null;
}

// wrong guesses: { count, since } in otpAttempts/{uid}
async function attemptsAllow(token, uid, now) {
    const url = `https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents/otpAttempts/${encodeURIComponent(uid)}`;
    const r = await timed(url, { headers: { Authorization: "Bearer " + token } }, 5);
    if (r.status === 404) return true;
    if (!r.ok) return false;                                                   // fail closed
    const f = (await r.json()).fields || {};
    const count = Number((f.count || {}).integerValue || 0), since = Number((f.since || {}).integerValue || 0);
    return !(count >= MAX_WRONG && now - since < LOCK_MS);
}
async function attemptsAdd(token, uid, now, reset) {
    const base = `https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents/otpAttempts/${encodeURIComponent(uid)}`;
    let count = 1, since = now;
    if (!reset) {
        const r = await timed(base, { headers: { Authorization: "Bearer " + token } }, 5);
        if (r.ok) {
            const f = (await r.json()).fields || {};
            const c = Number((f.count || {}).integerValue || 0), s = Number((f.since || {}).integerValue || 0);
            if (now - s < LOCK_MS) { count = c + 1; since = s; }
        }
    } else count = 0;
    await timed(base, { method: "PATCH", headers: { Authorization: "Bearer " + token, "Content-Type": "application/json" },
        body: JSON.stringify({ fields: { count: { integerValue: String(count) }, since: { integerValue: String(since) } } }) }, 5);
}

async function markVerified(token, uid) {
    const r = await timed(`https://identitytoolkit.googleapis.com/v1/projects/${PROJECT}/accounts:update`,
        { method: "POST", headers: { Authorization: "Bearer " + token, "Content-Type": "application/json" }, body: JSON.stringify({ localId: uid, emailVerified: true }) }, 6);
    return r.ok;
}

function config() {
    const secret = process.env.GLOWUP_OTP_SECRET, user = process.env.GMAIL_USER, pass = process.env.GMAIL_APP_PASSWORD;
    let account = null;
    try { account = JSON.parse(process.env.FIREBASE_SERVICE_ACCOUNT || ""); } catch (e) { account = null; }
    if (!secret || secret.length < 24 || !user || !pass || !account || !account.client_email || !account.private_key) return null;
    return { secret, user, pass, account };
}

async function readBody(req) {
    if (req.body !== undefined && req.body !== null) return typeof req.body === "string" ? req.body : JSON.stringify(req.body);
    let size = 0; const parts = [];
    for await (const chunk of req) { size += chunk.length; if (size > MAX_BODY) return null; parts.push(chunk); }
    return Buffer.concat(parts).toString("utf8");
}

module.exports = async (req, res) => {
    try {
        if (req.method !== "POST") return send(res, 405, { error: "Use POST." });
        const cfg = config();
        if (!cfg) return send(res, 503, { error: "not-set-up" });
        const text = await readBody(req);
        if (text === null || text.length > MAX_BODY) return send(res, 413, { error: "too-large" });
        let body; try { body = JSON.parse(text || "{}"); } catch (e) { return send(res, 400, { error: "unreadable" }); }
        const user = await signedInUser(String(body.idToken || ""));
        if (!user) return send(res, 401, { error: "sign-in" });
        if (user.verified) return send(res, 200, { ok: true, already: true });
        const now = Date.now();

        if (body.action === "send") {
            const list = (sentAt.get(user.uid) || []).filter((t) => now - t < 3600000);
            if (list.length && now - list[list.length - 1] < SEND_EVERY_MS) return send(res, 429, { error: "wait", seconds: Math.ceil((SEND_EVERY_MS - (now - list[list.length - 1])) / 1000) });
            if (list.length >= SEND_PER_HOUR) return send(res, 429, { error: "too-many" });
            list.push(now); sentAt.set(user.uid, list); if (sentAt.size > 5000) sentAt.delete(sentAt.keys().next().value);
            const code = codeFor(cfg.secret, user.uid, currentWindow(now));
            await smtpSend({ user: cfg.user, pass: cfg.pass, to: user.email, subject: "Your GlowUp code: " + code,
                text: "Your GlowUp confirmation code is " + code + ".\r\nIt works for about 10 minutes. If you did not ask for it, ignore this e-mail: nobody can use it without your password.\r\n" });
            return send(res, 200, { ok: true });
        }
        if (body.action === "verify") {
            const token = await serviceToken(cfg.account);
            if (!(await attemptsAllow(token, user.uid, now))) return send(res, 429, { error: "locked" });
            if (!codeIsRight(cfg.secret, user.uid, body.code, now)) { await attemptsAdd(token, user.uid, now, false); return send(res, 200, { ok: false }); }
            if (!(await markVerified(token, user.uid))) return send(res, 502, { error: "could-not-confirm" });
            await attemptsAdd(token, user.uid, now, true).catch(() => { });
            return send(res, 200, { ok: true });
        }
        return send(res, 400, { error: "action" });
    } catch (error) {
        return send(res, 500, { error: "server" });
    }
};
module.exports.codeFor = codeFor;
module.exports.codeIsRight = codeIsRight;
module.exports.smtpSend = smtpSend;
module.exports.config = { api: { bodyParser: { sizeLimit: "8kb" } } };
