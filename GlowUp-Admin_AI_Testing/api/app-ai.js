// The app's AI requests. The Unity app no longer holds an AI key: it sends its Gemini "generateContent"
// request here with the user's Firebase sign-in token, and this function adds the key (a Vercel environment variable only)
// and passes the request on. The answer comes back unchanged, so the app reads it as before.
// Checks: POST only, a valid Firebase sign-in, an allowed model name, a body size limit, only the known request fields,
// and a per-user request limit. Nothing about the request is stored or logged.
const FIREBASE_WEB_KEY = "AIzaSyAkb23xQ69a2Q4tpucGty9yDEoH0ab1JDM";
const KEY_REFERER = "https://glowup-admin-snowy.vercel.app/";   // the same allowed referrer the admin AI function uses
const MAX_BODY_BYTES = 4 * 1024 * 1024;            // Vercel accepts at most 4.5 MB; the app sends up to a few photos
// only the models in the app's GeminiApiConfig model chain
const MODELS = new Set(["gemini-flash-latest", "gemini-3.5-flash", "gemini-3.6-flash", "gemini-3.8-flash"]);
const MAX_OUTPUT_TOKENS = 8192;
const PER_MINUTE = 20, PER_DAY = 400;               // per user, per server instance (best effort on the free plan)
const IP_PER_MINUTE = 60;
const SECONDS = 55;
const LOOKUP_SECONDS = 5;                            // the MCP lookups get this long at most; late or failed = the request goes on without them
const mcp = require("./mcp");

const seen = new Map();                              // uid -> { minute, minuteCount, day, dayCount }
const byIp = new Map();                              // ip -> { minute, count }

function send(res, status, body) {
    res.statusCode = status;
    res.setHeader("Content-Type", "application/json; charset=utf-8");
    res.setHeader("Cache-Control", "no-store");
    res.end(typeof body === "string" ? body : JSON.stringify(body));
}

async function withTimeout(url, options, seconds) {
    const stop = new AbortController();
    const timer = setTimeout(() => stop.abort(), seconds * 1000);
    try { return await fetch(url, { ...options, signal: stop.signal }); } finally { clearTimeout(timer); }
}

async function signedInUid(idToken) {
    if (!idToken || idToken.length > 4096) return "";
    const who = await withTimeout("https://identitytoolkit.googleapis.com/v1/accounts:lookup?key=" + FIREBASE_WEB_KEY,
        { method: "POST", headers: { "Content-Type": "application/json", Referer: KEY_REFERER }, body: JSON.stringify({ idToken }) }, 10);
    if (!who.ok) return "";
    const user = ((await who.json()).users || [])[0];
    // a real GlowUp account only: email confirmed, signed in with a provider (no anonymous accounts), not disabled
    const real = user && user.localId && !user.disabled && user.emailVerified === true && Array.isArray(user.providerUserInfo) && user.providerUserInfo.length > 0;
    return real ? user.localId : "";
}

function ipWithinLimit(ip) {
    const minute = Math.floor(Date.now() / 60000);
    const s = byIp.get(ip) || { minute, count: 0 };
    if (s.minute !== minute) { s.minute = minute; s.count = 0; }
    s.count++;
    byIp.set(ip, s);
    if (byIp.size > 5000) byIp.delete(byIp.keys().next().value);
    return s.count <= IP_PER_MINUTE;
}

// rebuilds the request from what the app really sends: text and JPG/PNG parts, web search as the only tool, temperature
function cleanRequest(incoming) {
    if (!incoming || !Array.isArray(incoming.contents) || incoming.contents.length === 0 || incoming.contents.length > 8) return null;
    const contents = [];
    for (const c of incoming.contents) {
        if (!c || !Array.isArray(c.parts) || c.parts.length === 0 || c.parts.length > 12) return null;
        const parts = [];
        for (const p of c.parts) {
            if (p && typeof p.text === "string") parts.push({ text: p.text.slice(0, 60000) });
            else if (p && (p.inline_data || p.inlineData)) {
                const d = p.inline_data || p.inlineData;
                const mime = String(d.mime_type || d.mimeType || "");
                if (!/^image\/(jpeg|png)$/.test(mime) || typeof d.data !== "string") return null;
                parts.push({ inline_data: { mime_type: mime, data: d.data } });
            } else return null;
        }
        contents.push({ role: c.role === "model" ? "model" : "user", parts });
    }
    const body = { contents };
    if (Array.isArray(incoming.tools) && incoming.tools.some((t) => t && (t.google_search || t.googleSearch))) body.tools = [{ google_search: {} }];
    const temperature = Number(incoming.generationConfig && incoming.generationConfig.temperature);
    body.generationConfig = { maxOutputTokens: MAX_OUTPUT_TOKENS };
    if (Number.isFinite(temperature)) body.generationConfig.temperature = Math.min(1, Math.max(0, temperature));
    return body;
}

// Reference data from GlowUp's own MCP tools (api/mcp.js): the app names a product (or its barcode) in ?lookup= and the answer of
// Open Beauty Facts and the FDA Philippines list is added to the question as a plain text part. Fallback by design: when a tool is slow,
// down or finds nothing, the request goes on exactly as the app sent it. Only the product words the app put in ?lookup= are used:
// the skin profile never reaches a tool.
async function referenceText(lookup) {
    const term = String(lookup || "").replace(/[\x00-\x1f]/g, " ").trim().slice(0, 120);
    if (term.length < 2) return "";
    const give = async (name, args) => {
        try { const r = await Promise.race([mcp.TOOLS[name].run(args), new Promise((resolve) => setTimeout(() => resolve(null), LOOKUP_SECONDS * 1000))]); return r && r.content ? r.content[0].text : ""; }
        catch (e) { return ""; }
    };
    const [off, fda] = await Promise.all([give("product_lookup", { query: term }), /^\d{8,14}$/.test(term) ? "" : give("fda_check", { name: term })]);
    const useful = (t) => t && !/not available|no match|Give a|does not prove/.test(t);
    const parts = [];
    if (useful(off)) parts.push("Open Beauty Facts:\n" + off);
    if (useful(fda)) parts.push("FDA Philippines list:\n" + fda);
    return parts.length ? "REFERENCE DATA from GlowUp's own database look-ups (may be incomplete or about a similar product; check it against what you find, never copy it blindly):\n" + parts.join("\n\n") : "";
}

function withinLimit(uid) {
    const now = Date.now(), minute = Math.floor(now / 60000), day = Math.floor(now / 86400000);
    const s = seen.get(uid) || { minute, minuteCount: 0, day, dayCount: 0 };
    if (s.minute !== minute) { s.minute = minute; s.minuteCount = 0; }
    if (s.day !== day) { s.day = day; s.dayCount = 0; }
    if (s.minuteCount >= PER_MINUTE || s.dayCount >= PER_DAY) { seen.set(uid, s); return false; }
    s.minuteCount++; s.dayCount++;
    seen.set(uid, s);
    if (seen.size > 5000) seen.delete(seen.keys().next().value);
    return true;
}

async function readBody(req) {
    if (req.body !== undefined && req.body !== null) {
        const text = typeof req.body === "string" ? req.body : JSON.stringify(req.body);
        return text.length > MAX_BODY_BYTES ? null : text;
    }
    let size = 0; const parts = [];
    for await (const chunk of req) { size += chunk.length; if (size > MAX_BODY_BYTES) return null; parts.push(chunk); }
    return Buffer.concat(parts).toString("utf8");
}

module.exports = async (req, res) => {
    try {
        if (req.method !== "POST") return send(res, 405, { error: { message: "Use POST." } });
        // its own key (GLOWUP_APP_AI); until that variable exists, the admin site's key keeps the app's AI working
        const key = process.env.GLOWUP_APP_AI || process.env.GLOWUP_WEB_AI;
        const ip = String(req.headers["x-forwarded-for"] || "").split(",")[0].trim() || "unknown";
        if (!ipWithinLimit(ip)) return send(res, 429, { error: { message: "Too many AI requests. Wait a minute and try again." } });
        if (!key) return send(res, 503, { error: { message: "The AI is not set up on the server." } });

        const header = String(req.headers.authorization || "");
        const uid = await signedInUid(header.startsWith("Bearer ") ? header.slice(7) : "");
        if (!uid) return send(res, 401, { error: { message: "Sign in again to use the AI." } });

        const model = String((req.query && req.query.model) || "");
        if (!MODELS.has(model)) return send(res, 400, { error: { message: "Unknown AI model." } });
        if (!withinLimit(uid)) return send(res, 429, { error: { message: "Too many AI requests. Wait a minute and try again." } });

        const text = await readBody(req);
        if (text === null) return send(res, 413, { error: { message: "The request is too large." } });
        let incoming;
        try { incoming = JSON.parse(text || "{}"); } catch (e) { return send(res, 400, { error: { message: "The request could not be read." } }); }
        const body = cleanRequest(incoming);
        if (!body) return send(res, 400, { error: { message: "The request has an unexpected form." } });

        const extra = await referenceText(req.query && req.query.lookup);
        if (extra) body.contents[body.contents.length - 1].parts.push({ text: extra });

        const answer = await withTimeout(`https://generativelanguage.googleapis.com/v1beta/models/${encodeURIComponent(model)}:generateContent`,
            { method: "POST", headers: { "Content-Type": "application/json", "x-goog-api-key": key }, body: JSON.stringify(body) }, SECONDS);
        const out = await answer.text();
        // the service's own refusal of the key must not look like "your sign-in was refused" to the app
        const status = answer.status === 401 || answer.status === 403 ? 502 : answer.status;
        return send(res, status, out);
    } catch (error) {
        const timedOut = error && error.name === "AbortError";
        return send(res, timedOut ? 504 : 502, { error: { message: timedOut ? "The AI took too long to answer." : "The AI could not be reached." } });
    }
};

module.exports.config = { api: { bodyParser: { sizeLimit: "4mb" } } };
