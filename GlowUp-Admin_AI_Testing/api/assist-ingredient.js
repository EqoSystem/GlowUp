// "Assist with AI" of the Ingredient Curator: drafts ONE ingredient entry with Gemini and web search.
//
// It runs on the server (a Vercel function), so the AI key never reaches the browser:
//   - the key is the environment variable GEMINI_API_KEY (Vercel > Project > Settings > Environment Variables);
//     it is in no file of this project;
//   - only a signed-in ADMIN gets an answer: the caller's Firebase sign-in is checked with Google, and then the
//     database is asked, as that account, for its own entry in "admins" (the database rules allow exactly that);
//   - the AI is sent the ingredient's name and nothing else. No user data is involved.
//
// The answer is a DRAFT. Nothing is saved here: the page fills the form, the admin reads it against the sources and saves.
//
// Optional environment variables: GEMINI_MODELS (comma-separated, tried in order), and BACKUP services used only when Gemini
// cannot answer (a free quota used up). The order is fixed: Gemini, then Groq (GROQ_API_KEY), then Mistral (MISTRAL_API_KEY);
// OpenRouter (OPENROUTER_API_KEY) comes last and only when a key for it is ever added. GROQ_MODEL, MISTRAL_MODEL and
// OPENROUTER_MODEL change the model. A backup cannot search the web, so its draft has no sources.

const PROJECT = "glowup-admin-portal";
const FIREBASE_WEB_KEY = "AIzaSyAkb23xQ69a2Q4tpucGty9yDEoH0ab1JDM";      // the website's public Firebase key (it is in every page); NOT the AI key
const AI_SECONDS = 40;              // the longest ONE call to an AI may take
const TOTAL_SECONDS = 50;           // all the calls together: Vercel stops this function at 60 seconds, the answer must be out before that
const BACKUP_SECONDS = 10;          // kept back for each service that is still to come, so a slow Gemini cannot use up the time of Groq and Mistral
const LEAST_SECONDS = 3;            // with less time than this left for it, a call is not started

const RISK = ["safe", "caution", "harmful"];
const ALLERGENS = ["fragrance", "paraben", "sulfate", "formaldehyde", "lanolin", "essentialoil", "nut"];
const SKIN_TYPES = ["Oily", "Normal", "Dry", "Mix"];
const CONDITIONS = ["acne", "eczema", "psoriasis", "rosacea", "seborrheic", "melasma", "contact"];

// Pages that count as credible for WHAT AN INGREDIENT IS AND DOES: regulators, scientific bodies, medical references.
// (A page of another site is still shown, marked "other", so the admin can judge it.)
const CREDIBLE = [
    "europa.eu", "fda.gov", "fda.gov.ph", "nih.gov", "who.int", "cir-safety.org", "cir-reports.cir-safety.org", "aseancosmetics.org", "asean.org",
    "health.gov.au", "tga.gov.au", "canada.ca", "gov.uk", "echa.europa.eu", "cosmeticsinfo.org", "aad.org", "dermnetnz.org", "contactderm.org",
    "sciencedirect.com", "wiley.com", "springer.com", "nature.com", "jamanetwork.com", "bmj.com", "mayoclinic.org", "clevelandclinic.org",
    "personalcarecouncil.org", "incibeauty.com", "incidecoder.com", "paulaschoice.com", "ewg.org"
];

function send(res, status, body) {
    res.statusCode = status;
    res.setHeader("Content-Type", "application/json; charset=utf-8");
    res.setHeader("Cache-Control", "no-store");
    res.end(JSON.stringify(body));
}

async function withTimeout(url, options, seconds) {
    const stop = new AbortController();
    const timer = setTimeout(() => stop.abort(), seconds * 1000);
    try { return await fetch(url, { ...options, signal: stop.signal }); } finally { clearTimeout(timer); }
}

/** The caller's account id when the sign-in is real and the account is an admin; otherwise "". */
async function adminUid(idToken) {
    if (!idToken) return "";
    const who = await withTimeout("https://identitytoolkit.googleapis.com/v1/accounts:lookup?key=" + FIREBASE_WEB_KEY,
        { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ idToken }) }, 10);
    if (!who.ok) return "";
    const user = ((await who.json()).users || [])[0];
    if (!user || !user.localId) return "";
    // asked AS THAT ACCOUNT: the rules let an account read only its own entry in "admins"
    const entry = await withTimeout(`https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents/admins/${encodeURIComponent(user.localId)}`,
        { headers: { Authorization: "Bearer " + idToken } }, 10);
    return entry.ok ? user.localId : "";
}

function question(name) {
    return [
        "You help the administrators of a make-up safety app write ONE entry of its ingredient list.",
        `INGREDIENT (as printed on cosmetic labels): ${name}`,
        "Search the web and use credible sources only: regulators (EU CosIng and SCCS, US FDA, FDA Philippines / ASEAN Cosmetic Directive),",
        "the Cosmetic Ingredient Review (cir-safety.org), PubChem / NIH, dermatology references (DermNet, AAD), peer-reviewed papers.",
        "Do not use shops, blogs or forums. If the sources do not say something, leave that field empty: never guess.",
        "Answer with ONE JSON object and nothing else, with exactly these fields:",
        '{ "name": plain everyday name (for example "Vitamin E"; the label name itself when there is no everyday name),',
        '  "techName": the INCI name as printed on labels,',
        '  "aliases": [other spellings or names on labels, at most 6],',
        '  "description": one or two plain sentences a shopper understands: what it is and what it does in make-up (at most 300 characters),',
        '  "riskLevel": "safe" | "caution" | "harmful" | "" ("harmful" only when a regulator restricts or bans it or it is a well-documented strong sensitiser; "caution" for a known common irritant or allergen; "" when the sources do not allow a judgement),',
        '  "cautionNote": finishes the sentence "It ..." (for example "can clog pores"); "" when there is nothing to warn about (at most 120 characters),',
        '  "commonUses": the kinds of make-up it is commonly in (for example "foundations, lipsticks"), at most 120 characters,',
        '  "keyActive": true only for an ingredient shoppers look for by name (for example niacinamide),',
        '  "umbrella": true only for a word that can hide other ingredients (for example "Fragrance", "Parfum", "Aroma"),',
        `  "allergenTags": [any of ${JSON.stringify(ALLERGENS)} that this ingredient belongs to],`,
        `  "avoidForSkinTypes": [any of ${JSON.stringify(SKIN_TYPES)}], "goodForSkinTypes": [any of ${JSON.stringify(SKIN_TYPES)}],`,
        `  "avoidForConditions": [any of ${JSON.stringify(CONDITIONS)}], "goodForConditions": [any of ${JSON.stringify(CONDITIONS)}],`,
        '  "minAge": 0 unless a regulator sets an age limit,',
        '  "confidence": "high" | "medium" | "low",',
        '  "notFound": true when you could not find this ingredient in credible sources (then leave the other fields empty) }',
        "Leave the skin type and condition lists empty unless a credible source supports the claim."
    ].join("\n");
}

/** The first complete { ... } in the AI's text, as an object (null when there is none). */
function firstObject(text) {
    const start = text.indexOf("{");
    if (start < 0) return null;
    let depth = 0, inText = false, escaped = false;
    for (let i = start; i < text.length; i++) {
        const c = text[i];
        if (inText) { if (escaped) escaped = false; else if (c === "\\") escaped = true; else if (c === '"') inText = false; continue; }
        if (c === '"') inText = true;
        else if (c === "{") depth++;
        else if (c === "}" && --depth === 0) { try { return JSON.parse(text.slice(start, i + 1)); } catch (error) { return null; } }
    }
    return null;
}

const text = (value, most) => String(value === null || value === undefined ? "" : value).replace(/\s+/g, " ").trim().slice(0, most);
const pick = (value, allowed) => (Array.isArray(value) ? value : []).map((v) => allowed.find((a) => a.toLowerCase() === String(v).trim().toLowerCase())).filter((v, i, all) => v && all.indexOf(v) === i);

/** Only what the form can hold, and only the values it knows. */
function tidy(raw, asked) {
    const avoidTypes = pick(raw.avoidForSkinTypes, SKIN_TYPES), avoidConditions = pick(raw.avoidForConditions, CONDITIONS);
    return {
        name: text(raw.name, 80) || asked,
        techName: text(raw.techName, 120) || asked,
        aliases: (Array.isArray(raw.aliases) ? raw.aliases : []).map((a) => text(a, 80)).filter(Boolean).slice(0, 6),
        description: text(raw.description, 300),
        riskLevel: RISK.includes(String(raw.riskLevel || "").toLowerCase()) ? String(raw.riskLevel).toLowerCase() : "",
        cautionNote: text(raw.cautionNote, 120).replace(/^it\s+/i, "").replace(/\.$/, ""),
        commonUses: text(raw.commonUses, 120),
        keyActive: raw.keyActive === true,
        umbrella: raw.umbrella === true,
        allergenTags: pick(raw.allergenTags, ALLERGENS),
        avoidForSkinTypes: avoidTypes,
        goodForSkinTypes: pick(raw.goodForSkinTypes, SKIN_TYPES).filter((t) => !avoidTypes.includes(t)),          // "avoid" wins over "good"
        avoidForConditions: avoidConditions,
        goodForConditions: pick(raw.goodForConditions, CONDITIONS).filter((t) => !avoidConditions.includes(t)),
        minAge: Number.isInteger(raw.minAge) && raw.minAge > 0 && raw.minAge < 100 ? raw.minAge : 0,
        confidence: ["high", "medium", "low"].includes(raw.confidence) ? raw.confidence : "low"
    };
}

const hostOf = (url) => { try { return new URL(url).hostname.replace(/^www\./, "").toLowerCase(); } catch (error) { return ""; } };
const isCredible = (host) => CREDIBLE.some((d) => host === d || host.endsWith("." + d)) || /\.gov(\.[a-z]{2})?$/.test(host) || /\.edu(\.[a-z]{2})?$/.test(host);

/** The search links come as Google redirect addresses: this follows one to the real page (its own address when that fails). */
async function realAddress(url) {
    if (!/vertexaisearch\.cloud\.google\.com/i.test(url)) return url;
    try {
        const answer = await withTimeout(url, { method: "GET", redirect: "manual" }, 6);
        const next = answer.headers.get("location");
        return next && /^https?:\/\//i.test(next) ? next : url;
    } catch (error) { return url; }
}

/** The pages the AI's search really used: real addresses, credible ones first, each once. */
async function sourcesOf(candidate) {
    const chunks = (candidate.groundingMetadata && candidate.groundingMetadata.groundingChunks) || [];
    const found = chunks.map((c) => c && c.web).filter((w) => w && /^https?:\/\//i.test(w.uri || "")).slice(0, 10);
    const pages = await Promise.all(found.map(async (w) => {
        const url = await realAddress(w.uri);
        const host = hostOf(url);
        const resolved = !/vertexaisearch\.cloud\.google\.com/i.test(url);
        const site = resolved ? host : text(w.title, 80).toLowerCase();          // the title of an unresolved link is the site's name
        return { title: resolved ? (text(w.title, 80) && text(w.title, 80).toLowerCase() !== host ? text(w.title, 80) : host) : site, url, site, credible: isCredible(site) };
    }));
    const seen = new Set();
    return pages.filter((p) => p.url && !seen.has(p.url) && seen.add(p.url)).sort((a, b) => Number(b.credible) - Number(a.credible)).slice(0, 8);
}

// ---- the AI services, in the order they are tried -----------------------------------------------------------------
// 1. GEMINI first. WITH web search (the only one that can name the pages it used), over each model in GEMINI_MODELS;
//    then WITHOUT web search (a quota for search can be used up while plain answers still work);
// 2. GROQ, when Gemini has run out;
// 3. MISTRAL, when Groq has run out too;
// 4. OpenRouter last, and only when a key for it is ever added (the project owner asked for Gemini, Groq, Mistral).
// A service without a key is passed over. The backups cannot search the web: their answer comes from the model's
// memory, and the page says so and gives no sources. Nothing is ever invented as a source.
const BACKUPS = [
    { id: "groq", label: "Groq", key: "GROQ_API_KEY", url: "https://api.groq.com/openai/v1/chat/completions", model: () => process.env.GROQ_MODEL || "llama-3.3-70b-versatile" },
    { id: "mistral", label: "Mistral", key: "MISTRAL_API_KEY", url: "https://api.mistral.ai/v1/chat/completions", model: () => process.env.MISTRAL_MODEL || "mistral-small-latest" },
    { id: "openrouter", label: "OpenRouter", key: "OPENROUTER_API_KEY", url: "https://openrouter.ai/api/v1/chat/completions", model: () => process.env.OPENROUTER_MODEL || "meta-llama/llama-3.3-70b-instruct:free" }
];

/** How many seconds the next call may take: what is left of the whole time, less what is kept back for each service still to come. */
function share(secondsLeft, stillToCome) {
    return Math.min(AI_SECONDS, secondsLeft - stillToCome * BACKUP_SECONDS);
}

async function askGemini(key, name, withSearch, notes, time) {
    const models = String(process.env.GEMINI_MODELS || "gemini-flash-latest,gemini-3.6-flash,gemini-3.5-flash").split(",").map((m) => m.trim()).filter(Boolean);
    for (const model of models) {
        const seconds = time();
        if (seconds < LEAST_SECONDS) { notes.push(`Gemini${withSearch ? " with search" : ""}: no time left for it`); break; }
        let answer;
        try {
            answer = await withTimeout(`https://generativelanguage.googleapis.com/v1beta/models/${encodeURIComponent(model)}:generateContent`, {
                method: "POST",
                headers: { "Content-Type": "application/json", "x-goog-api-key": key },
                body: JSON.stringify({ contents: [{ role: "user", parts: [{ text: question(name) }] }], ...(withSearch ? { tools: [{ google_search: {} }] } : {}), generationConfig: { temperature: 0.1 } })
            }, seconds);
        } catch (error) { notes.push(`Gemini ${model}: no answer in ${Math.round(seconds)} seconds`); continue; }
        if (answer.status === 429) { notes.push(`Gemini ${model}${withSearch ? " with search" : ""}: free quota used up`); continue; }
        if (!answer.ok) { notes.push(`Gemini ${model}: error ${answer.status}`); continue; }
        const body = await answer.json();
        const candidate = (body.candidates || [])[0];
        const said = candidate && candidate.content && Array.isArray(candidate.content.parts) ? candidate.content.parts.map((p) => p.text || "").join("") : "";
        const object = firstObject(said);
        if (!object) { notes.push(`Gemini ${model}: the answer could not be read`); continue; }
        return { object, sources: withSearch ? await sourcesOf(candidate) : [], model: "Gemini " + model, searched: withSearch };
    }
    return null;
}

async function askBackup(service, name, notes, seconds) {
    const key = process.env[service.key];
    if (!key) return null;
    if (seconds < LEAST_SECONDS) { notes.push(`${service.label}: no time left for it`); return null; }
    let answer;
    try {
        answer = await withTimeout(service.url, {
            method: "POST",
            headers: { "Content-Type": "application/json", Authorization: "Bearer " + key },
            body: JSON.stringify({ model: service.model(), temperature: 0.1, messages: [{ role: "user", content: question(name) }] })
        }, seconds);
    } catch (error) { notes.push(`${service.label}: no answer in ${Math.round(seconds)} seconds`); return null; }
    if (answer.status === 429) { notes.push(`${service.label}: free quota used up`); return null; }
    if (!answer.ok) { notes.push(`${service.label}: error ${answer.status}`); return null; }
    const body = await answer.json();
    const said = body.choices && body.choices[0] && body.choices[0].message ? String(body.choices[0].message.content || "") : "";
    const object = firstObject(said);
    if (!object) { notes.push(`${service.label}: the answer could not be read`); return null; }
    return { object, sources: [], model: `${service.label} ${service.model()}`, searched: false };
}

/** Search links the admin can open to check an entry. They are NOT pages the AI used: they only start a search on a credible site. */
function lookupLinks(name) {
    const q = encodeURIComponent(name);
    return [
        { title: "PubChem (NIH): search for it", url: `https://pubchem.ncbi.nlm.nih.gov/#query=${q}` },
        { title: "Cosmetic Ingredient Review: search its site", url: `https://www.google.com/search?q=${encodeURIComponent("site:cir-safety.org " + name)}` },
        { title: "EU CosIng and SCCS: search their sites", url: `https://www.google.com/search?q=${encodeURIComponent("site:europa.eu " + name + " cosmetic ingredient")}` }
    ];
}

async function ask(key, name) {
    const notes = [];
    const until = Date.now() + TOTAL_SECONDS * 1000;
    const left = () => (until - Date.now()) / 1000;
    const backups = BACKUPS.filter((b) => process.env[b.key]);                 // in their fixed order: Groq, Mistral, (OpenRouter)
    if (key) {
        const time = () => share(left(), backups.length);
        const withSearch = await askGemini(key, name, true, notes, time);
        if (withSearch) return { ...withSearch, notes };
        const plain = await askGemini(key, name, false, notes, time);
        if (plain) return { ...plain, notes };
    }
    for (let i = 0; i < backups.length; i++) {
        const got = await askBackup(backups[i], name, notes, share(left(), backups.length - 1 - i));
        if (got) return { ...got, notes };
    }
    const error = new Error(notes.length > 0 ? "Every AI that is set up said no: " + brief(notes) + ". Try again later, or write the entry by hand." : "No AI is set up.");
    throw error;
}

/** The notes as one short reason per service, in the order they were asked: "Gemini: free quota used up; Groq: error 401". */
function brief(notes) {
    const by = new Map();
    notes.forEach((note) => {
        const at = note.indexOf(": "), service = note.split(/[ :]/)[0], why = at < 0 ? note : note.slice(at + 2);
        if (!by.has(service)) by.set(service, new Set());
        by.get(service).add(why);
    });
    return [...by].map(([service, whys]) => `${service}: ${[...whys].join(", ")}`).join("; ");
}

module.exports = async (req, res) => {
    try {
        if (req.method !== "POST") return send(res, 405, { error: "Use POST." });
        const key = process.env.GEMINI_API_KEY;
        if (!key && !BACKUPS.some((b) => process.env[b.key])) return send(res, 503, { error: "The AI is not set up yet: add GEMINI_API_KEY in Vercel > Project > Settings > Environment Variables, then deploy again." });

        const header = String(req.headers.authorization || "");
        const uid = await adminUid(header.startsWith("Bearer ") ? header.slice(7) : "");
        if (!uid) return send(res, 401, { error: "Only a signed-in admin can use Assist with AI. Sign in again." });

        const body = typeof req.body === "string" ? JSON.parse(req.body || "{}") : (req.body || {});
        const name = text(body.name, 120);
        if (name.length < 2 || !/[a-z0-9]/i.test(name)) return send(res, 400, { error: "No ingredient name was sent." });

        const { object, sources, model, searched, notes } = await ask(key, name);
        const lookups = lookupLinks(name);
        if (object.notFound === true) return send(res, 200, { found: false, sources, lookups, model, searched, notes, message: "The AI found nothing credible about this name. It may be a misread label or a trade name." });
        const credible = sources.filter((s) => s.credible).length;
        return send(res, 200, { found: true, draft: tidy(object, name), sources, lookups, credibleSources: credible, model, searched, notes });
    } catch (error) {
        return send(res, 502, { error: text(error && error.message, 300) || "Assist with AI failed." });
    }
};

// for the checks in tests/ (pure helpers; no network)
module.exports.helpers = { firstObject, tidy, isCredible, hostOf, question, lookupLinks, share, order: () => ["Gemini"].concat(BACKUPS.map((b) => b.label)) };
