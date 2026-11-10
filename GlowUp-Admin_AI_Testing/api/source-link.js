// Admin-only help with the SOURCE LINK beside a box of the Ingredient form (10 November 2026). Two things, one request per admin click:
//
//   mode "check"  { url, inci, cas }  Is the page there, and does it name the ingredient?  -> { result: "checked" | "unconfirmed" | "dead" | "cantcheck", message }
//                 SSRF-safe: https only; only the trusted sources' sites (sourceLinkRules.mjs); every host is resolved and refused when it is a
//                 private, loopback or link-local address; redirects are followed by hand (at most 3), each one checked again; 5 seconds;
//                 at most 1.5 MB read. The page text is only searched for the name, never stored or sent back.
//   mode "cosing" { name }            The CosIng Checker index (cosingchecker.com/api/v1, public, no key, 120 requests a minute): up to 5
//                 ingredients, the exact INCI name first, each with a statement to review and its own page. Answers are kept 24 hours; a 429
//                 answer is passed on with its Retry-After. "Unofficial index of EU CosIng data" (CC BY 4.0): the admin verifies at CosIng.
//
// Only a signed-in admin is answered (the same check as Assist with AI). Each admin may make 20 requests a minute (counted per server instance).

const dns = require("dns").promises;
const { adminUid } = require("./assist-ingredient.js").helpers;

const CHECK_SECONDS = 5;
const MAX_BYTES = 1500000;
const MAX_REDIRECTS = 3;
const PER_MINUTE = 20;
const COSING_API = "https://cosingchecker.com/api/v1/ingredients/";
const COSING_CACHE_MS = 24 * 3600 * 1000;
const AGENT = "GlowUpAdmin/1.0 (admin-started source check)";

let rules = null;
const loadRules = async () => rules || (rules = await import("../_Shared/sourceLinkRules.mjs"));

function send(res, status, body) {
    res.statusCode = status;
    res.setHeader("Content-Type", "application/json; charset=utf-8");
    res.setHeader("Cache-Control", "no-store");
    res.end(JSON.stringify(body));
}

// ---- a small per-admin limit (each server instance keeps its own count; enough to stop a runaway page)
const recent = new Map();
function allowed(uid, now = Date.now()) {
    const list = (recent.get(uid) || []).filter((t) => now - t < 60000);
    if (list.length >= PER_MINUTE) { recent.set(uid, list); return false; }
    list.push(now); recent.set(uid, list);
    return true;
}

/** Every address of `host` is public (resolved now, so a name that points inside a network is refused). */
async function publicHost(host, lookup = dns.lookup) {
    const r = await loadRules();
    try {
        const all = await lookup(host, { all: true });
        return Array.isArray(all) && all.length > 0 && all.every((a) => !r.isPrivateAddress(a.address));
    } catch (error) { return false; }
}

/** Reads at most MAX_BYTES of a response's text. */
async function readSome(response) {
    if (!response.body || !response.body.getReader) return (await response.text()).slice(0, MAX_BYTES);
    const reader = response.body.getReader();
    const chunks = []; let size = 0;
    while (size < MAX_BYTES) {
        const { done, value } = await reader.read();
        if (done) break;
        chunks.push(value); size += value.length;
    }
    try { await reader.cancel(); } catch (error) { /* already finished */ }
    return Buffer.concat(chunks.map((c) => Buffer.from(c))).toString("utf8").slice(0, MAX_BYTES);
}

/** The check of one link. `fetcher` and `lookup` are replaceable for the tests. */
async function checkLink(url, inci, cas, fetcher = fetch, lookup = dns.lookup) {
    const r = await loadRules();
    let verdict = r.checkSourceUrl(url);
    if (!verdict.ok || verdict.empty) return { result: "cantcheck", message: verdict.error || "No address to check." };
    const stop = new AbortController();
    const timer = setTimeout(() => stop.abort(), CHECK_SECONDS * 1000);
    try {
        let address = verdict.value;
        for (let hop = 0; hop <= MAX_REDIRECTS; hop++) {
            const parsed = new URL(address);
            if (parsed.protocol !== "https:") return { result: "cantcheck", message: "The page sends to an address that is not https." };
            if (!r.checkable(parsed.hostname)) return { result: "cantcheck", message: "Only pages of the trusted sources can be checked." };
            if (!(await publicHost(parsed.hostname, lookup))) return { result: "cantcheck", message: "This address cannot be checked." };
            const response = await fetcher(address, { redirect: "manual", signal: stop.signal, headers: { "User-Agent": AGENT, Accept: "text/html,text/plain" } });
            if (response.status >= 300 && response.status < 400) {
                const next = response.headers.get("location");
                if (!next) return { result: "dead", message: "Link not working." };
                address = new URL(next, address).toString();
                continue;
            }
            if (!response.ok) return { result: "dead", message: "Link not working." };
            const text = await readSome(response);
            if (r.pageMentions(text, inci, cas)) return { result: "checked", message: "Checked: the page names this ingredient." };
            return { result: "unconfirmed", message: "Reachable, content not confirmed (the page may need JavaScript, or it is about something else)." };
        }
        return { result: "dead", message: "Link not working (too many redirects)." };
    } catch (error) {
        return { result: "dead", message: stop.signal.aborted ? "Link not working (no answer in 5 seconds)." : "Link not working." };
    } finally { clearTimeout(timer); }
}

// ---- CosIng Checker, kept 24 hours per name
const cosingCache = new Map();
async function cosingSearch(name, fetcher = fetch, now = Date.now()) {
    const r = await loadRules();
    const key = String(name || "").trim().toLowerCase();
    if (key.length < 2) return { status: 400, body: { error: "Enter the INCI name first." } };
    const kept = cosingCache.get(key);
    if (kept && now - kept.at < COSING_CACHE_MS) return { status: 200, body: { ...kept.body, cached: true } };
    const stop = new AbortController();
    const timer = setTimeout(() => stop.abort(), 8000);
    try {
        const response = await fetcher(COSING_API + "?q=" + encodeURIComponent(name.trim()) + "&per_page=30", { signal: stop.signal, headers: { "User-Agent": AGENT, Accept: "application/json" } });
        if (response.status === 429) {
            const wait = Math.min(600, Math.max(1, parseInt(response.headers.get("retry-after") || "60", 10) || 60));
            return { status: 429, body: { error: `The CosIng index is busy. Try again in ${wait} seconds.`, retryAfter: wait } };
        }
        if (!response.ok) return { status: 502, body: { error: "The CosIng index did not answer." } };
        const data = await response.json();
        const wanted = key.toUpperCase();
        const list = (Array.isArray(data.results) ? data.results : []).filter((x) => x && x.inci_name);
        list.sort((a, b) => (String(b.inci_name).toUpperCase() === wanted) - (String(a.inci_name).toUpperCase() === wanted) || String(a.inci_name).length - String(b.inci_name).length);
        const results = list.slice(0, 5).map((x) => ({
            inci: String(x.inci_name), cas: String(x.cas_number || ""), ec: String(x.ec_number || ""), ref: String(x.ref_number || ""),
            statement: r.statementOf(x), page: r.cosingCheckerPage(x.url), exact: String(x.inci_name).toUpperCase() === wanted
        }));
        const body = { results, attribution: "Data: CosIng Checker (CC BY 4.0)", label: "Unofficial index of EU CosIng data. Verify at the official source." };
        cosingCache.set(key, { at: now, body });
        return { status: 200, body };
    } catch (error) {
        return { status: 502, body: { error: stop.signal.aborted ? "The CosIng index did not answer in time." : "The CosIng index could not be reached." } };
    } finally { clearTimeout(timer); }
}

module.exports = async (req, res) => {
    try {
        if (req.method !== "POST") return send(res, 405, { error: "Use POST." });
        const header = String(req.headers.authorization || "");
        const uid = await adminUid(header.startsWith("Bearer ") ? header.slice(7) : "");
        if (!uid) return send(res, 401, { error: "Only a signed-in admin can do this. Sign in again." });
        if (!allowed(uid)) return send(res, 429, { error: "Too many checks in a minute. Wait a moment.", retryAfter: 30 });
        const body = typeof req.body === "string" ? JSON.parse(req.body || "{}") : (req.body || {});
        const mode = String(body.mode || "");
        if (mode === "check") return send(res, 200, await checkLink(String(body.url || "").slice(0, 700), String(body.inci || "").slice(0, 120), String(body.cas || "").slice(0, 20)));
        if (mode === "cosing") { const answer = await cosingSearch(String(body.name || "").slice(0, 120)); return send(res, answer.status, answer.body); }
        return send(res, 400, { error: "Unknown request." });
    } catch (error) {
        console.error("source-link:", error);
        return send(res, 500, { error: "Something went wrong. Try again." });
    }
};

module.exports.helpers = { checkLink, cosingSearch, allowed, publicHost, cosingCache, recent };
