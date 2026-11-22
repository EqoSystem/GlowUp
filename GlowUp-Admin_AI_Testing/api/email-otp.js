// 6-digit e-mail code for the admin sign-up (and the app's sign-up), sent from a Gmail address.
//   POST { action: "send",   idToken }        -> the signed-in, not yet confirmed account gets a 6-digit code by e-mail
//   POST { action: "verify", idToken, code }  -> a right code marks the account's e-mail as confirmed (Firebase Auth emailVerified = true)
// No code is stored: it is an HMAC of the account id, the account's e-mail address and a 10-minute window (a code is good for the window it was
// made in and the one after; a code made for one e-mail address is useless once the account has another).
// Every count is an ATOMIC Firestore increment (documents:commit with a field transform) written only by this function with the service account
// (the rules give clients no access): wrong guesses are counted BEFORE the code is looked at, 5 per account per 10-minute window; e-mails sent
// are counted per account (1 per 45 s, 5 per hour), per recipient address (5 per hour) and in total (150 per hour). When a count cannot be
// written the answer is "no" (fail closed). Free plan: a few small writes per request, far inside the daily free quota.
// Environment variables (Vercel only, never in the repository): GLOWUP_OTP_SECRET (long random text), GMAIL_USER, GMAIL_APP_PASSWORD
// (a Google "app password" of that Gmail account), FIREBASE_SERVICE_ACCOUNT (the service-account JSON, one line).
// Until all of them exist the function answers 503 "not set up" and the pages keep using Firebase's confirmation link.
const crypto = require("crypto");
const tls = require("tls");

const PROJECT = "glowup-admin-portal";
const FIREBASE_WEB_KEY = "AIzaSyAkb23xQ69a2Q4tpucGty9yDEoH0ab1JDM";
const KEY_REFERER = "https://glowup-admin-snowy.vercel.app/";
const WINDOW_MS = 10 * 60 * 1000;
const MAX_WRONG = 5;
const SEND_EVERY_MS = 45 * 1000, SEND_PER_HOUR = 5, SEND_PER_ADDRESS_HOUR = 5, SEND_TOTAL_HOUR = 150;
const MAX_BODY = 8 * 1024;

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
function codeFor(secret, uid, email, windowIndex) {
    const h = crypto.createHmac("sha256", secret).update(uid + "|" + String(email || "").toLowerCase() + "|" + windowIndex).digest();
    return String(h.readUInt32BE(0) % 1000000).padStart(6, "0");
}
function currentWindow(now) { return Math.floor((now === undefined ? Date.now() : now) / WINDOW_MS); }
function codeIsRight(secret, uid, email, typed, now) {
    const t = String(typed || "").trim();
    if (!/^\d{6}$/.test(t)) return false;
    const w = currentWindow(now);
    let ok = false;
    for (const idx of [w, w - 1]) {
        const a = Buffer.from(codeFor(secret, uid, email, idx)), b = Buffer.from(t);
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

// An atomic counter: otpCounters/{id} gets +1 in ONE commit (the field transform creates the document when it is not there) and the new value comes
// back in the same answer, so two requests at the same moment can never both see "4". Returns the new count, or 0 when the write failed (callers say no).
async function bump(token, id) {
    const name = `projects/${PROJECT}/databases/(default)/documents/otpCounters/${encodeURIComponent(id)}`;
    try {
        const r = await timed(`https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents:commit`,
            { method: "POST", headers: { Authorization: "Bearer " + token, "Content-Type": "application/json" },
              body: JSON.stringify({ writes: [{ transform: { document: name, fieldTransforms: [{ fieldPath: "count", increment: { integerValue: "1" } }] } }] }) }, 5);
        if (!r.ok) return 0;
        const j = await r.json();
        const v = (((j.writeResults || [])[0] || {}).transformResults || [])[0];
        return v && v.integerValue ? Number(v.integerValue) : 0;
    } catch (e) { return 0; }
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

        const hour = Math.floor(now / 3600000), win = currentWindow(now);
        if (body.action === "send") {
            const token = await serviceToken(cfg.account);
            const addressKey = crypto.createHash("sha256").update(user.email.toLowerCase()).digest("hex").slice(0, 32);
            const fast = await bump(token, `fast_${user.uid}_${Math.floor(now / SEND_EVERY_MS)}`);
            if (fast === 0) return send(res, 502, { error: "server" });
            if (fast > 1) return send(res, 429, { error: "wait", seconds: Math.ceil(SEND_EVERY_MS / 1000) });
            const mine = await bump(token, `hour_${user.uid}_${hour}`), theirs = await bump(token, `addr_${addressKey}_${hour}`), all = await bump(token, `all_${hour}`);
            if (mine === 0 || theirs === 0 || all === 0) return send(res, 502, { error: "server" });
            if (mine > SEND_PER_HOUR || theirs > SEND_PER_ADDRESS_HOUR || all > SEND_TOTAL_HOUR) return send(res, 429, { error: "too-many" });
            const code = codeFor(cfg.secret, user.uid, user.email, win);
            await smtpSend({ user: cfg.user, pass: cfg.pass, to: user.email, subject: "Your GlowUp code: " + code,
                text: "Your GlowUp confirmation code is " + code + ".\r\nIt works for about 10 minutes. If you did not ask for it, ignore this e-mail: nobody can use it without your password.\r\n" });
            return send(res, 200, { ok: true });
        }
        if (body.action === "verify") {
            const token = await serviceToken(cfg.account);
            const tries = await bump(token, `try_${user.uid}_${win}`);          // counted first: a guess is spent whether or not it is right
            if (tries === 0) return send(res, 502, { error: "server" });
            if (tries > MAX_WRONG) return send(res, 429, { error: "locked" });
            if (!codeIsRight(cfg.secret, user.uid, user.email, body.code, now)) return send(res, 200, { ok: false });
            if (!(await markVerified(token, user.uid))) return send(res, 502, { error: "could-not-confirm" });
            return send(res, 200, { ok: true });
        }
        return send(res, 400, { error: "action" });
    } catch (error) {
        return send(res, 500, { error: "server" });
    }
};
module.exports.codeFor = codeFor;
module.exports.bump = bump;
module.exports.codeIsRight = codeIsRight;
module.exports.smtpSend = smtpSend;
module.exports.config = { api: { bodyParser: { sizeLimit: "8kb" } } };
