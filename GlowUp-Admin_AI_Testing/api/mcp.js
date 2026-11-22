// GlowUp's MCP server (Model Context Protocol, JSON-RPC 2.0 over HTTP POST). Read-only tools an AI can call to look things up
// instead of guessing: product_lookup (Open Beauty Facts), ingredient_lookup (PubChem), fda_check (the shipped FDA Philippines catalogue).
// Checks: POST only, a valid Firebase sign-in (a real account: email confirmed), body limit, per-user and per-IP limits, a timeout on
// every outside call, results cut to a safe size. Nothing about a skin profile is accepted: the tools take a product or ingredient name only.
// Free plan: no key, no storage; if an outside service is slow or down the tool answers with a plain "not available" text, never an error.
const fs = require("fs");
const path = require("path");

const FIREBASE_WEB_KEY = "AIzaSyAkb23xQ69a2Q4tpucGty9yDEoH0ab1JDM";
const KEY_REFERER = "https://glowup-admin-snowy.vercel.app/";
const MAX_BODY = 16 * 1024;
const PER_MINUTE = 30, IP_PER_MINUTE = 60;
const TOOL_SECONDS = 6;                               // the free plan allows 10 s per function: two lookups at most fit
const UA = "GlowUp-MCP/1.0";

const byUser = new Map(), byIp = new Map();

function send(res, status, body) {
    res.statusCode = status;
    res.setHeader("Content-Type", "application/json; charset=utf-8");
    res.setHeader("Cache-Control", "no-store");
    res.end(JSON.stringify(body));
}
const rpcError = (id, code, message) => ({ jsonrpc: "2.0", id: id === undefined ? null : id, error: { code, message } });
const rpcResult = (id, result) => ({ jsonrpc: "2.0", id, result });

async function timed(url, options, seconds) {
    const stop = new AbortController();
    const timer = setTimeout(() => stop.abort(), seconds * 1000);
    try { return await fetch(url, { ...options, signal: stop.signal }); } finally { clearTimeout(timer); }
}

async function signedInUid(idToken) {
    if (!idToken || idToken.length > 4096) return "";
    const who = await timed("https://identitytoolkit.googleapis.com/v1/accounts:lookup?key=" + FIREBASE_WEB_KEY,
        { method: "POST", headers: { "Content-Type": "application/json", Referer: KEY_REFERER }, body: JSON.stringify({ idToken }) }, 5);
    if (!who.ok) return "";
    const user = ((await who.json()).users || [])[0];
    const real = user && user.localId && !user.disabled && user.emailVerified === true && Array.isArray(user.providerUserInfo) && user.providerUserInfo.length > 0;
    return real ? user.localId : "";
}

function within(map, key, limit) {
    const minute = Math.floor(Date.now() / 60000);
    const s = map.get(key) || { minute, count: 0 };
    if (s.minute !== minute) { s.minute = minute; s.count = 0; }
    s.count++;
    map.set(key, s);
    if (map.size > 5000) map.delete(map.keys().next().value);
    return s.count <= limit;
}

const clean = (v, max) => String(v === undefined || v === null ? "" : v).replace(/[\u0000-\u001f]/g, " ").trim().slice(0, max);
const text = (s) => ({ content: [{ type: "text", text: String(s).slice(0, 4000) }] });
const unavailable = (what) => text(what + " is not available right now. Say so plainly and do not guess.");

let catalogue = null;
function fdaProducts() {
    if (catalogue) return catalogue;
    try { catalogue = JSON.parse(fs.readFileSync(path.join(__dirname, "..", "_Shared", "data", "fdaCatalogue.json"), "utf8")).products || []; }
    catch (e) { catalogue = []; }
    return catalogue;
}

const norm = (s) => String(s || "").toLowerCase().replace(/[^a-z0-9 ]+/g, " ").replace(/\s+/g, " ").trim();

const TOOLS = {
    product_lookup: {
        description: "Look up a make-up product on Open Beauty Facts by barcode (8 to 14 digits) or by name. Returns name, brand, and the ingredient list if the database has one.",
        inputSchema: { type: "object", properties: { query: { type: "string", description: "A barcode or a product name" } }, required: ["query"], additionalProperties: false },
        async run(args) {
            const q = clean(args.query, 120);
            if (!q) return text("Give a barcode or a product name.");
            const barcode = /^\d{8,14}$/.test(q);
            const url = barcode ? `https://world.openbeautyfacts.org/api/v2/product/${q}.json?fields=product_name,brands,ingredients_text`
                : `https://world.openbeautyfacts.org/cgi/search.pl?search_terms=${encodeURIComponent(q)}&search_simple=1&action=process&json=1&page_size=3&fields=product_name,brands,ingredients_text`;
            let data;
            try { const r = await timed(url, { headers: { "User-Agent": UA } }, TOOL_SECONDS); if (!r.ok) return unavailable("Open Beauty Facts"); data = await r.json(); }
            catch (e) { return unavailable("Open Beauty Facts"); }
            const list = barcode ? (data && data.product ? [data.product] : []) : ((data && data.products) || []);
            if (list.length === 0) return text("Open Beauty Facts has no match for \"" + q + "\".");
            return text(list.slice(0, 3).map((p) => `${clean(p.brands, 80)} - ${clean(p.product_name, 120)}\nIngredients: ${clean(p.ingredients_text, 1200) || "not listed"}`).join("\n\n"));
        }
    },
    ingredient_lookup: {
        description: "Look up a cosmetic ingredient on PubChem by name. Returns its formula, molecular weight, synonyms and PubChem page. Information only, not medical advice.",
        inputSchema: { type: "object", properties: { name: { type: "string", description: "An ingredient name, for example Niacinamide" } }, required: ["name"], additionalProperties: false },
        async run(args) {
            const n = clean(args.name, 80);
            if (!n) return text("Give an ingredient name.");
            const base = "https://pubchem.ncbi.nlm.nih.gov/rest/pug/compound/name/" + encodeURIComponent(n);
            try {
                const r = await timed(base + "/property/MolecularFormula,MolecularWeight,IUPACName/JSON", { headers: { "User-Agent": UA } }, TOOL_SECONDS);
                if (r.status === 404) return text("PubChem has no compound named \"" + n + "\".");
                if (!r.ok) return unavailable("PubChem");
                const p = ((await r.json()).PropertyTable || {}).Properties;
                const c = p && p[0];
                if (!c) return text("PubChem has no compound named \"" + n + "\".");
                return text(`${n}: formula ${clean(c.MolecularFormula, 40)}, molecular weight ${clean(c.MolecularWeight, 20)}, IUPAC name ${clean(c.IUPACName, 200)}.\nhttps://pubchem.ncbi.nlm.nih.gov/compound/${Number(c.CID) || ""}`);
            } catch (e) { return unavailable("PubChem"); }
        }
    },
    fda_check: {
        description: "Check a product name (and optionally its brand) against GlowUp's copy of the FDA Philippines notified-cosmetics list. A hit means the product was notified to the FDA, not that it is safe for a given skin.",
        inputSchema: { type: "object", properties: { name: { type: "string" }, brand: { type: "string" } }, required: ["name"], additionalProperties: false },
        async run(args) {
            const name = norm(clean(args.name, 120)), brand = norm(clean(args.brand, 80));
            if (!name) return text("Give a product name.");
            const words = name.split(" ").filter((w) => w.length > 1);
            const hits = fdaProducts().map((p) => {
                const hay = norm(p.name + " " + (p.fdaName || ""));
                const score = words.length ? words.filter((w) => hay.includes(w)).length / words.length : 0;
                const okBrand = !brand || norm(p.brand).includes(brand) || brand.includes(norm(p.brand));
                return { p, score: okBrand ? score : 0 };
            }).filter((h) => h.score >= 0.8).sort((a, b) => b.score - a.score).slice(0, 3);
            if (hits.length === 0) return text("No close match in the FDA Philippines list GlowUp holds (gathered 13 October 2026). That does not prove it is unregistered.");
            return text(hits.map((h) => `${clean(h.p.brand, 60)} - ${clean(h.p.name, 120)}: FDA number ${clean(h.p.fdaNumber, 40)}, ${clean(h.p.company, 80)}`).join("\n"));
        }
    }
};

module.exports = async (req, res) => {
    try {
        if (req.method !== "POST") return send(res, 405, rpcError(null, -32600, "Use POST."));
        const ip = String(req.headers["x-forwarded-for"] || "").split(",")[0].trim() || "unknown";
        if (!within(byIp, ip, IP_PER_MINUTE)) return send(res, 429, rpcError(null, -32000, "Too many requests."));
        const header = String(req.headers.authorization || "");
        const uid = await signedInUid(header.startsWith("Bearer ") ? header.slice(7) : "");
        if (!uid) return send(res, 401, rpcError(null, -32001, "Sign in again."));
        if (!within(byUser, uid, PER_MINUTE)) return send(res, 429, rpcError(null, -32000, "Too many requests."));

        let msg = req.body;
        if (typeof msg === "string") { if (msg.length > MAX_BODY) return send(res, 413, rpcError(null, -32600, "Too large.")); try { msg = JSON.parse(msg); } catch (e) { msg = null; } }
        if (!msg || typeof msg !== "object" || Array.isArray(msg) || JSON.stringify(msg).length > MAX_BODY) return send(res, 400, rpcError(null, -32700, "Unreadable request."));

        const id = msg.id;
        if (id === undefined) { res.statusCode = 202; return res.end(); }          // a notification (for example notifications/initialized)
        switch (msg.method) {
            case "initialize":
                return send(res, 200, rpcResult(id, { protocolVersion: "2025-03-26", capabilities: { tools: {} }, serverInfo: { name: "glowup", version: "1.0.0" } }));
            case "ping":
                return send(res, 200, rpcResult(id, {}));
            case "tools/list":
                return send(res, 200, rpcResult(id, { tools: Object.entries(TOOLS).map(([name, t]) => ({ name, description: t.description, inputSchema: t.inputSchema })) }));
            case "tools/call": {
                const params = msg.params || {};
                const tool = Object.prototype.hasOwnProperty.call(TOOLS, params.name) ? TOOLS[params.name] : null;
                if (!tool) return send(res, 200, rpcError(id, -32602, "Unknown tool."));
                const args = params.arguments && typeof params.arguments === "object" ? params.arguments : {};
                return send(res, 200, rpcResult(id, await tool.run(args)));
            }
            default:
                return send(res, 200, rpcError(id, -32601, "Method not found."));
        }
    } catch (error) {
        return send(res, 500, rpcError(null, -32603, "The server could not answer."));
    }
};
module.exports.TOOLS = TOOLS;
module.exports.config = { api: { bodyParser: { sizeLimit: "16kb" } } };
