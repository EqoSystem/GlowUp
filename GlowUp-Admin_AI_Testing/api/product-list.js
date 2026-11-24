// A product's PUBLISHED ingredient list, found on the web for a product that arrived without one (staff capture, import).
//   POST { name, brand?, kind? }   header Authorization: Bearer <admin's Firebase ID token>
//   -> { found: true, brandName, cosmeticType, ingredients: [..], sources: ["Title | https://..."], confidence }
//   -> { found: false, why }       when the web search found no page it can stand behind
// Not a guess: Gemini answers with Google Search switched on, and the answer is USED only when (1) it says it is clearly ONE product, with
// confidence of 0.6 or more, (2) the list has at least 4 ingredients, (3) it names at least one page, and (4) that page's site is one the search really
// opened (an invented link is dropped) and is NOT an ingredient database (INCIDecoder, EWG, CosDNA, CosIng explain ingredients; they do not publish a pack's list).
// The website never writes the result into the product: it queues it in "Web ingredient lists to review" for an admin to approve.
// Only admins may ask. Nothing about a person is accepted: a product name and brand only.
// Key: GLOWUP_WEB_AI (the admin site's key; GLOWUP_APP_AI is the fallback). Free plan: one call per product, 10 per minute per admin.
const { adminUid } = require("./_adminAuth");

const MODEL = "gemini-flash-latest";
const MAX_BODY = 8 * 1024, SECONDS = 28, PER_MINUTE = 10, MIN_CONFIDENCE = 0.6, MIN_INGREDIENTS = 4, MAX_INGREDIENTS = 80;
const DATABASES = ["incidecoder.com", "ewg.org", "cosdna.com", "ec.europa.eu", "skincarisma.com", "paulaschoice.com"];
const seen = new Map();

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
function withinLimit(uid) {
    const minute = Math.floor(Date.now() / 60000);
    const s = seen.get(uid) || { minute, count: 0 };
    if (s.minute !== minute) { s.minute = minute; s.count = 0; }
    s.count++; seen.set(uid, s);
    if (seen.size > 2000) seen.delete(seen.keys().next().value);
    return s.count <= PER_MINUTE;
}
async function readBody(req) {
    if (req.body !== undefined && req.body !== null) return typeof req.body === "string" ? req.body : JSON.stringify(req.body);
    let size = 0; const parts = [];
    for await (const chunk of req) { size += chunk.length; if (size > MAX_BODY) return null; parts.push(chunk); }
    return Buffer.concat(parts).toString("utf8");
}
const clean = (v, n) => String(v || "").replace(/[\x00-\x1f{}<>`]/g, " ").replace(/\s+/g, " ").trim().slice(0, n);
const hostOf = (url) => { try { return new URL(url).hostname.toLowerCase().replace(/^www\./, ""); } catch (e) { return ""; } };
const isDatabase = (url) => DATABASES.some((d) => hostOf(url) === d || hostOf(url).endsWith("." + d));

function promptFor(name, brand, kind) {
    return [
        "A make-up admin captured a product but has no ingredient list for it.",
        `PRODUCT: ${name}` + (brand ? `\nBRAND: ${brand}` : "") + (kind ? `\nKIND: ${kind}` : ""),
        "STEP 1, SEARCH THE WEB (Google Search) for this exact make-up product: the brand's own website or store first, then large retailers (for the Philippines also watsons.com.ph), then Open Beauty Facts.",
        "STEP 2, \"same\" is true only when the words clearly mean ONE product you found (different shades of one product are the same product). When they fit several products, or it is not make-up, answer same false.",
        "STEP 3, when it is clear: copy its full ingredient (INCI) list exactly as that page prints it, in the printed order. NEVER rebuild a list from memory or from a similar product. Ingredient DATABASES (INCIDecoder, EWG, CosDNA, CosIng) explain what an ingredient is: do not take the list from them. When no credible page shows the list, leave \"ingredients\" empty.",
        'Answer with JSON only, no other text: {"same":true,"brandName":"","productName":"","cosmeticType":"Foundation|Lipstick|Blush|Concealer|Mascara|Eyeliner|Eyeshadow|Powder|Primer|Brow|Highlighter|Bronzer|Setting spray|Other make-up","ingredients":["..."],"confidence":0.0,"sources":[{"title":"page title","url":"https://..."}]}'
    ].join("\n");
}

function openedSites(candidate) {
    const chunks = (((candidate || {}).groundingMetadata || {}).groundingChunks) || [];
    return chunks.map((c) => String((c && c.web && c.web.title) || "").toLowerCase().replace(/^www\./, "")).filter(Boolean);
}

// Turns Gemini's answer into the safe result (exported for the test).
function readAnswer(answer) {
    const candidate = (answer && answer.candidates && answer.candidates[0]) || null;
    const text = candidate && candidate.content && candidate.content.parts ? candidate.content.parts.map((p) => p.text || "").join("") : "";
    const json = /\{[\s\S]*\}/.exec(text);
    if (!json) return { found: false, why: "no-answer" };
    let data; try { data = JSON.parse(json[0]); } catch (e) { return { found: false, why: "no-answer" }; }
    if (data.same !== true) return { found: false, why: "not-clear" };
    const confidence = Number(data.confidence);
    if (!(confidence >= MIN_CONFIDENCE)) return { found: false, why: "low-confidence" };
    const seenNames = new Set();
    const ingredients = (Array.isArray(data.ingredients) ? data.ingredients : []).map((x) => clean(x, 100)).filter((x) => x.length >= 2)
        .filter((x) => { const k = x.toLowerCase(); if (seenNames.has(k)) return false; seenNames.add(k); return true; }).slice(0, MAX_INGREDIENTS);
    if (ingredients.length < MIN_INGREDIENTS) return { found: false, why: "no-list" };
    const opened = openedSites(candidate);
    const sources = (Array.isArray(data.sources) ? data.sources : []).slice(0, 6).map((s) => ({ title: clean(s && s.title, 120), url: String((s && s.url) || "").slice(0, 400) }))
        .filter((s) => /^https:\/\//.test(s.url) && !isDatabase(s.url) && opened.includes(hostOf(s.url)))        // the exact site the search opened, never a database
        .map((s) => `${s.title || hostOf(s.url)} | ${s.url}`);
    if (sources.length === 0) return { found: false, why: "no-source" };
    return { found: true, brandName: clean(data.brandName, 60), cosmeticType: clean(data.cosmeticType, 40), ingredients, sources, confidence };
}

module.exports = async (req, res) => {
    try {
        if (req.method !== "POST") return send(res, 405, { error: "Use POST." });
        const key = process.env.GLOWUP_WEB_AI || process.env.GLOWUP_APP_AI;
        if (!key) return send(res, 503, { error: "not-set-up" });
        const uid = await adminUid(String((req.headers && req.headers.authorization) || "").replace(/^Bearer\s+/i, ""));
        if (!uid) return send(res, 401, { error: "sign-in" });
        if (!withinLimit(uid)) return send(res, 429, { error: "wait" });
        const text = await readBody(req);
        if (text === null) return send(res, 413, { error: "too-large" });
        let body; try { body = JSON.parse(text || "{}"); } catch (e) { return send(res, 400, { error: "unreadable" }); }
        const name = clean(body.name, 120), brand = clean(body.brand, 60), kind = clean(body.kind, 40);
        if (name.length < 3) return send(res, 400, { error: "bad-request" });
        const answer = await timed(`https://generativelanguage.googleapis.com/v1beta/models/${MODEL}:generateContent`,
            { method: "POST", headers: { "Content-Type": "application/json", "x-goog-api-key": key },
              body: JSON.stringify({ contents: [{ role: "user", parts: [{ text: promptFor(name, brand, kind) }] }], generationConfig: { temperature: 0, maxOutputTokens: 4096 }, tools: [{ google_search: {} }] }) }, SECONDS);
        if (!answer.ok) return send(res, 502, { error: "ai" });
        return send(res, 200, readAnswer(await answer.json()));
    } catch (error) {
        return send(res, error && error.name === "AbortError" ? 504 : 502, { error: "ai" });
    }
};
module.exports.readAnswer = readAnswer;
module.exports.config = { api: { bodyParser: { sizeLimit: "8kb" } } };
