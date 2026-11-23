// Second barcode look-up for Quick fill: UPCitemdb's free trial service, asked by the server because UPCitemdb does not
// answer browser pages of other websites (CORS). Only a signed-in admin may ask (same check as the other functions).
//
// Free trial (no key): 100 look-ups a day and 6 a minute, counted per internet address. Every admin shares this server's
// address, so: a mistyped barcode (wrong check digit) is refused here without asking, answers are kept for a day (found or
// not), and each admin may ask at most 6 a minute. UPCitemdb gives a title, brand, shop category and pictures: never ingredients.
const { adminUid } = require("./_adminAuth.js");

const TRIAL = "https://api.upcitemdb.com/prod/trial/lookup?upc=";
const AGENT = "GlowUpAdmin/1.0 (admin barcode look-up)";
const TIMEOUT_MS = 8000;
const PER_MINUTE = 6;
const CACHE_MS = 24 * 3600 * 1000;
const CACHE_MAX = 500;

function send(res, status, body) {
    res.statusCode = status;
    res.setHeader("Content-Type", "application/json; charset=utf-8");
    res.setHeader("Cache-Control", "no-store");
    res.end(JSON.stringify(body));
}

// GS1 check digit (EAN-8, UPC-A, EAN-13, GTIN-14); the same rule as gtinValid in _Shared/smartAdd.js
function gtinValid(code) {
    const digits = String(code || "").replace(/\D/g, "");
    if (![8, 12, 13, 14].includes(digits.length)) return false;
    let sum = 0;
    for (let i = digits.length - 2, weight = 3; i >= 0; i--, weight = weight === 3 ? 1 : 3) sum += Number(digits[i]) * weight;
    return (10 - (sum % 10)) % 10 === Number(digits[digits.length - 1]);
}

const text = (value, most) => String(value == null ? "" : value).replace(/\s+/g, " ").trim().slice(0, most);
// Only the fields Quick fill uses, cut to size; picture links only when https.
function cleanItems(json) {
    const items = json && Array.isArray(json.items) ? json.items : [];
    return items.slice(0, 3).map((item) => ({
        title: text(item.title, 200), brand: text(item.brand, 80), category: text(item.category, 200),
        ean: text(item.ean, 14).replace(/\D/g, ""), upc: text(item.upc, 14).replace(/\D/g, ""),
        images: (Array.isArray(item.images) ? item.images : []).map(String).filter((u) => /^https:\/\//i.test(u) && u.length <= 400).slice(0, 3)
    })).filter((item) => item.title);
}

const cache = new Map();
function remember(code, body) {
    if (cache.size >= CACHE_MAX) cache.delete(cache.keys().next().value);
    cache.set(code, { at: Date.now(), body });
}
const recent = new Map();
function allowed(uid, now = Date.now()) {
    const list = (recent.get(uid) || []).filter((t) => now - t < 60000);
    if (list.length >= PER_MINUTE) { recent.set(uid, list); return false; }
    list.push(now); recent.set(uid, list);
    return true;
}

async function lookup(code, fetcher = fetch) {
    const kept = cache.get(code);
    if (kept && Date.now() - kept.at < CACHE_MS) return { status: 200, body: { ...kept.body, cached: true } };
    const stop = new AbortController();
    const timer = setTimeout(() => stop.abort(), TIMEOUT_MS);
    let answer;
    try { answer = await fetcher(TRIAL + code, { headers: { "User-Agent": AGENT, Accept: "application/json" }, signal: stop.signal }); }
    catch (error) { return { status: 504, body: { error: "UPCitemdb did not answer in time. Try again, or go on without it." } }; }
    finally { clearTimeout(timer); }
    if (answer.status === 429) return { status: 429, body: { error: "The free UPCitemdb look-ups for today are used up (100 a day). Try again tomorrow, or go on without it." } };
    const json = await answer.json().catch(() => null);
    if (answer.status === 400 && json && json.code === "INVALID_UPC") { const body = { found: 0, items: [] }; remember(code, body); return { status: 200, body }; }
    if (!answer.ok || !json) return { status: 502, body: { error: "UPCitemdb answered with an error. Go on without it." } };
    const items = cleanItems(json);
    const body = { found: items.length, items };
    remember(code, body);
    return { status: 200, body };
}

module.exports = async (req, res) => {
    try {
        if (req.method !== "POST") return send(res, 405, { error: "Use POST." });
        const header = String(req.headers.authorization || "");
        const uid = await adminUid(header.startsWith("Bearer ") ? header.slice(7) : "");
        if (!uid) return send(res, 401, { error: "Only a signed-in admin can do this. Sign in again." });
        const body = typeof req.body === "string" ? JSON.parse(req.body || "{}") : (req.body || {});
        const code = String(body.code || "").replace(/\D/g, "").slice(0, 14);
        if (!gtinValid(code)) return send(res, 400, { error: "This is not a valid barcode: its last digit does not match. Check the digits under the bars." });
        if (!allowed(uid)) return send(res, 429, { error: "Too many barcode look-ups in a minute. Wait a moment." });
        const answer = await lookup(code);
        return send(res, answer.status, answer.body);
    } catch (error) {
        console.error("upc-lookup:", error);
        return send(res, 500, { error: "The barcode look-up could not finish. Go on without it." });
    }
};

module.exports.helpers = { gtinValid, cleanItems, lookup, allowed, cache, recent };
