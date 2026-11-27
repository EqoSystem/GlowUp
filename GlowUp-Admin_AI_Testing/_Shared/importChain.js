// One-click Search and import (redesign plan, 27 November 2026). An admin types a brand, a product name or a barcode and presses
// one button; this runs the chain and hands back a plan. Nothing here writes to the database: the page saves the plan (as
// Pending) only after every step finished, so Cancel saves nothing.
//   1. Find the product        lookupProduct (FDA Philippines list, Open Beauty Facts, UPCitemdb)
//   2. Read the ingredient list the candidate's list, or the text a user confirmed in a "New product" report
//   3. Match with the Curator   reuse a known entry (name, INCI name or alias); the rest are new
//   4. Apply the rules          new names: PubChem names and hazard codes + GlowUp's name rules (fitReasons.js, ingredientFit.js)
//   5. Ready for review         three results per ingredient: Flagged, No rule matched, Not identified
// Fixed rules with sources decide, not AI: the same input gives the same answer. "No rule matched" never means safe.

export const FLAGGED = "flagged";
export const NO_RULE = "none";
export const NOT_IDENTIFIED = "unknown";
export const RESULT_WORDS = { [FLAGGED]: "Flagged", [NO_RULE]: "No rule matched", [NOT_IDENTIFIED]: "Not identified" };
export const STEPS = ["Find the product", "Read the ingredient list", "Match with the Curator", "Apply the rules", "Ready for review"];
export const MOST_NEW = 60;               // new names looked up in one import (PubChem is asked twice per name)

const keyOf = (text) => String(text || "").toLowerCase().normalize("NFKD").replace(/[̀-ͯ]/g, "").replace(/[^a-z0-9]+/g, "");

/** The names on a list as text ("Aqua, Dimethicone, Parfum ..."), split the way the site splits a pasted list. */
export function splitList(text) {
    return String(text || "").replace(/^\s*ingredients?\s*[:\-]\s*/i, "").split(/[,;\n•·]+/)
        .map((s) => s.replace(/\s+/g, " ").replace(/^[\s.*]+|[\s.*]+$/g, "").trim())
        .filter((s) => s.length >= 2 && s.length <= 120);
}

/** The Curator entry for a name read from a list (its name, INCI name or one of its other names), or null. */
const indexes = new WeakMap();
function indexOf(library) {
    let index = indexes.get(library);
    if (index) return index;
    index = new Map();
    (library || []).forEach((entry) => {
        if (!entry || entry.binned) return;
        [entry.name, entry.techName, ...(entry.aliases || [])].forEach((n) => { const k = keyOf(n); if (k && !index.has(k)) index.set(k, entry); });
    });
    if (library && typeof library === "object") indexes.set(library, index);
    return index;
}
export function findEntry(name, library) {
    const k = keyOf(name);
    return k ? indexOf(library || []).get(k) || null : null;
}

const sourceOf = (line) => {
    const text = String(line || "");
    const at = text.lastIndexOf("|");
    const url = (at >= 0 ? text.slice(at + 1) : text).trim();
    return { title: (at >= 0 ? text.slice(0, at) : "").trim(), url: /^https:\/\//i.test(url) ? url : "" };
};

/**
 * What a Curator entry means for the app. Flagged = an allergen family, a skin type caution or a caution/harmful risk level.
 * An entry nobody could identify (no PubChem page, no classification, marked identified:false) is Not identified.
 */
export function judgeEntry(entry) {
    if (!entry) return { result: NOT_IDENTIFIED, cautionFor: [], why: "No match in the Curator or PubChem", source: null };
    if (entry.identified === false) return { result: NOT_IDENTIFIED, cautionFor: [], why: "No match in the Curator or PubChem", source: null };
    const allergens = entry.allergenTags || [], types = entry.avoidForSkinTypes || [];
    const risky = entry.riskLevel === "caution" || entry.riskLevel === "harmful";
    const sources = (entry.sources || []).map(sourceOf).filter((s) => s.url);
    if (allergens.length || types.length || risky) {
        const cautionFor = [...types, ...allergens.map((a) => `allergen: ${a}`)];
        if (risky && cautionFor.length === 0) cautionFor.push(entry.riskLevel === "harmful" ? "everyone (harmful)" : "everyone (caution)");
        const ruled = sources.find((s) => !/pubchem/i.test(s.url)) || sources[0] || null;
        return { result: FLAGGED, cautionFor, why: entry.cautionNote || (ruled ? ruled.title || "Rule with a source" : "GlowUp rule, no source page yet"), source: ruled };
    }
    return { result: NO_RULE, cautionFor: [], why: "Checked. This does not mean safe", source: sources[0] || null };
}

/** One row per name on the list, problems first (Not identified, then Flagged, then No rule matched). */
export function reviewRows(names, library) {
    const order = { [NOT_IDENTIFIED]: 0, [FLAGGED]: 1, [NO_RULE]: 2 };
    const seen = new Set();
    const rows = [];
    (names || []).forEach((name, at) => {
        const k = keyOf(name);
        if (!k || seen.has(k)) return;
        seen.add(k);
        const entry = findEntry(name, library);
        rows.push({ at, name, entry, ...judgeEntry(entry) });
    });
    return rows.sort((a, b) => order[a.result] - order[b.result] || a.at - b.at);
}

export function countResults(rows) {
    const counts = { all: rows.length, [FLAGGED]: 0, [NO_RULE]: 0, [NOT_IDENTIFIED]: 0 };
    rows.forEach((r) => { counts[r.result]++; });
    return counts;
}

/**
 * Runs steps 1 to 4 and returns the plan. deps:
 *   lookupProduct(text) -> { candidates }      library: Curator entries     draftIngredient(name) -> a new Curator entry (async)
 *   fallback: { brand, name, barcode, ingredientText } typed by a user in a report (used when the look-up finds less)
 * onStep(index, words) reports progress; signal (AbortSignal) stops between steps. Throws an Error with .stopped on Cancel.
 */
export async function runImport(query, deps, onStep = () => {}, signal = null) {
    const stop = () => { if (signal && signal.aborted) { const e = new Error("Cancelled"); e.stopped = true; throw e; } };
    const fallback = deps.fallback || {};
    onStep(0, STEPS[0]);
    const found = String(query || "").trim() ? await deps.lookupProduct(query) : { candidates: [] };
    stop();
    const best = (found.candidates || [])[0] || null;
    const product = {
        name: (best && best.name) || fallback.name || "",
        brand: (best && best.brand) || fallback.brand || "",
        barcode: (best && best.barcode) || fallback.barcode || "",
        cosmeticType: (best && best.cosmeticType) || "",
        fdaNumber: (best && best.fdaNumber) || "",
        sources: (best && best.sources) || [],
        foundBy: best ? best.originLabel || best.origin || "" : "",
        note: (best && best.note) || ""
    };
    if (!product.name) { const e = new Error("No product was found for that search."); e.notFound = true; throw e; }

    onStep(1, STEPS[1]);
    let names = best && Array.isArray(best.ingredients) && best.ingredients.length ? best.ingredients.slice() : [];
    let listFrom = names.length ? product.foundBy : "";
    if (!names.length && fallback.ingredientText) { names = splitList(fallback.ingredientText); listFrom = names.length ? "a user report (not verified)" : ""; }
    stop();

    onStep(2, STEPS[2]);
    const known = [], fresh = [];
    const seen = new Set();
    names.forEach((n) => {
        const k = keyOf(n);
        if (!k || seen.has(k)) return;
        seen.add(k);
        (findEntry(n, deps.library) ? known : fresh).push(n);
    });
    stop();

    onStep(3, STEPS[3]);
    const drafts = [];
    for (const name of fresh.slice(0, MOST_NEW)) {
        stop();
        try { drafts.push(await deps.draftIngredient(name)); }
        catch (error) { drafts.push({ name, identified: false, sources: [], aliases: [], allergenTags: [], avoidForSkinTypes: [] }); }
    }
    fresh.slice(MOST_NEW).forEach((name) => drafts.push({ name, identified: false, sources: [], aliases: [], allergenTags: [], avoidForSkinTypes: [] }));
    stop();

    onStep(4, STEPS[4]);
    const rows = reviewRows(names, (deps.library || []).concat(drafts));
    return { product, names, listFrom, known: known.length, fresh: fresh.length, drafts, rows, counts: countResults(rows) };
}
