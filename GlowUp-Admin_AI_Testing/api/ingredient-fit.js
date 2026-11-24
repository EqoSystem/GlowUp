// SOURCED allergy / skin flags for an ingredient that GlowUp's built-in rules do not know (_Shared/ingredientFit.js).
//   POST { name, aliases?, allergens: [{id,label}], conditions: [{id,label}] }   header Authorization: Bearer <admin's Firebase ID token>
//   -> { found: true, allergenTags, avoidForSkinTypes, avoidForConditions, why, sources: ["Title | https://..."] }
//   -> { found: false }   when the web search found nothing it can stand behind
// This is not a guess: Gemini answers with Google Search switched on, and the answer is USED only when (1) it names at least one page,
// (2) that page's site is one the search really opened (checked against the search's own source list, so an invented link is dropped), and
// (3) the website then checks the page against its evidence tiers (regulator, medical or scientific body). Ids are cut to the official lists
// sent with the request. Only admins may ask. Nothing about a person is accepted: an ingredient name only.
// Key: GLOWUP_WEB_AI (the admin site's key; GLOWUP_APP_AI is the fallback). Free plan: one call per ingredient, 10 per minute per admin.
const { adminUid } = require("./_adminAuth");

const MODEL = "gemini-flash-latest";
const SKIN_TYPES = ["Normal", "Dry", "Oily", "Mix", "Sensitive"];
const MAX_BODY = 16 * 1024, SECONDS = 25, PER_MINUTE = 10;
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

const cleanList = (raw) => (Array.isArray(raw) ? raw : []).slice(0, 40).map((x) => ({ id: String((x && x.id) || "").slice(0, 24), label: String((x && x.label) || "").slice(0, 40) }))
    .filter((x) => /^[A-Za-z0-9]{1,24}$/.test(x.id) && x.label);
const cleanName = (v) => String(v || "").replace(/[\x00-\x1f{}<>`]/g, " ").replace(/\s+/g, " ").trim().slice(0, 120);

function promptFor(name, aliases, allergens, conditions) {
    return [
        `Ingredient (cosmetics INCI name): ${name}` + (aliases.length ? `. Also known as: ${aliases.join(", ")}` : "") + ".",
        "Use Google Search. Find what regulators (EU CosIng / SCCS, ECHA, US FDA, ASEAN) and medical or scientific bodies (DermNet, CIR, dermatology journals) say about this ingredient in COSMETIC products.",
        "Decide ONLY from what those pages say, never from memory. Flag an item only when a page says this ingredient is an allergen or sensitiser of that kind, or that it irritates or worsens that skin type or condition. When no page supports a flag, leave it out. When unsure, return empty lists.",
        "Choose only from these ids.",
        "allergen families: " + allergens.map((a) => `${a.id} = ${a.label}`).join("; "),
        "skin types: " + SKIN_TYPES.join(", ") + " (Mix = Combination)",
        "skin conditions: " + conditions.map((c) => `${c.id} = ${c.label}`).join("; "),
        'Answer with JSON only, no other text: {"allergenTags":[ids],"avoidForSkinTypes":[types],"avoidForConditions":[ids],"why":"one plain sentence naming what the page says","sources":[{"title":"page title","url":"https://..."}]}'
    ].join("\n");
}

// the sites the search really opened (the title of a grounding chunk is the site's domain)
function openedSites(candidate) {
    const chunks = (((candidate || {}).groundingMetadata || {}).groundingChunks) || [];
    return chunks.map((c) => String((c && c.web && c.web.title) || "").toLowerCase().replace(/^www\./, "")).filter(Boolean);
}
const hostOf = (url) => { try { return new URL(url).hostname.toLowerCase().replace(/^www\./, ""); } catch (e) { return ""; } };

// Turns Gemini's answer into the safe result (exported for the test): ids cut to the lists, links kept only when the search opened their site.
function readAnswer(answer, allergens, conditions) {
    const candidate = (answer && answer.candidates && answer.candidates[0]) || null;
    const text = candidate && candidate.content && candidate.content.parts ? candidate.content.parts.map((p) => p.text || "").join("") : "";
    const json = /\{[\s\S]*\}/.exec(text);
    if (!json) return { found: false };
    let data; try { data = JSON.parse(json[0]); } catch (e) { return { found: false }; }
    const pick = (raw, allowed) => Array.from(new Set((Array.isArray(raw) ? raw : []).map(String).filter((id) => allowed.includes(id))));
    const allergenTags = pick(data.allergenTags, allergens.map((a) => a.id));
    const avoidForSkinTypes = pick(data.avoidForSkinTypes, SKIN_TYPES);
    const avoidForConditions = pick(data.avoidForConditions, conditions.map((c) => c.id));
    const opened = openedSites(candidate);
    const sources = (Array.isArray(data.sources) ? data.sources : []).slice(0, 6).map((s) => ({ title: String((s && s.title) || "").slice(0, 120), url: String((s && s.url) || "").slice(0, 400) }))
        .filter((s) => /^https:\/\//.test(s.url) && opened.includes(hostOf(s.url)))      // the exact site only: no parent or sub-domain is accepted
        .map((s) => `${s.title || hostOf(s.url)} | ${s.url}`);
    if (sources.length === 0 || (allergenTags.length + avoidForSkinTypes.length + avoidForConditions.length) === 0) return { found: false };
    return { found: true, allergenTags, avoidForSkinTypes, avoidForConditions, why: String(data.why || "").replace(/\s+/g, " ").trim().slice(0, 300), sources };
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
        const name = cleanName(body.name);
        const allergens = cleanList(body.allergens), conditions = cleanList(body.conditions);
        if (name.length < 3 || allergens.length === 0 || conditions.length === 0) return send(res, 400, { error: "bad-request" });
        const aliases = (Array.isArray(body.aliases) ? body.aliases : []).slice(0, 6).map(cleanName).filter(Boolean);
        const answer = await timed(`https://generativelanguage.googleapis.com/v1beta/models/${MODEL}:generateContent`,
            { method: "POST", headers: { "Content-Type": "application/json", "x-goog-api-key": key },
              body: JSON.stringify({ contents: [{ role: "user", parts: [{ text: promptFor(name, aliases, allergens, conditions) }] }], tools: [{ google_search: {} }], generationConfig: { temperature: 0, maxOutputTokens: 2048 } }) }, SECONDS);
        if (!answer.ok) return send(res, 502, { error: "ai" });
        return send(res, 200, readAnswer(await answer.json(), allergens, conditions));
    } catch (error) {
        return send(res, error && error.name === "AbortError" ? 504 : 502, { error: "ai" });
    }
};
module.exports.readAnswer = readAnswer;
module.exports.config = { api: { bodyParser: { sizeLimit: "16kb" } } };
