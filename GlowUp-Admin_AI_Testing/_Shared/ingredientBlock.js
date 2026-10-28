// Reads an ingredient list out of free text WITHOUT any AI and without any quota: a label, a product sheet, a pasted web page,
// or the text a PDF / Word / Excel reader gives (adminDocumentReader.js).
//
//   ingredientBlockOf(text)     the text after "Ingredients:" / "INCI:" / "Composition:" ... up to the end of the list
//   splitIngredients(block)     { ingredients, mayContain } - names split on , ; bullets (never inside brackets), "(and)" blends split,
//                               "May contain" / "+/-" colour lists kept apart, "C.I. 77891" written "CI 77891"
//   parseLabeledProducts(text)  documents that label each product ("Brand: ..." "Product: ..." "Ingredients: ..."), answered in the shape
//                               of parseImportText ({ kind: "inventory", draft, notes } or { kind: "" })
//   suggestMatches(name, known) names a typo might mean (Jaro-Winkler). SUGGESTIONS ONLY: nothing here ever accepts one by itself.
//
// Nothing here guesses a value: a product without a brand, a name or an ingredient block is left out and named in the notes.
// A "/" inside a name is kept ("Aqua/Water/Eau" stays one name): labels use it both for synonyms and for two ingredients.
// Pure code, no network (checked by tests/ingredientBlock.test.mjs).
import { parseImportText } from "./importFormat.js";
import { makeupType, normalizeName } from "./glowupRules.js";

// ---- finding the block ----------------------------------------------------------------------------------------------------
// English, French, Spanish, Filipino, Malay / Indonesian. A colon is needed, except for the capitals "INGREDIENTS" or "INCI" at the start of a line.
const LABEL_WORDS = "ingredients?|ingr[eé]dients?|ingredientes?|inci(?:\\s+list)?|composition|komposisi|bahan|sangkap";
const ANCHOR = new RegExp("(?:^|[\\s.;(\\[])(?:" + LABEL_WORDS + ")(?:\\s*\\(inci\\))?\\s*[:：]", "gi");
const ANCHOR_CAPS = /^(?:INGREDIENTS?|INCI)[ \t]+(?=\S)/m;            // "INGREDIENTS Talc, Mica ...": capitals at the start of a line, no colon
// where a list ends: another part of the label, or the next field of a product sheet
const END = new RegExp("(?:^|[\\s.;])(?:(?:directions?|how\\s+to\\s+use|usage|warnings?|caution|precautions?|barcode|fda\\b[^:\\n]{0,30}|cpnp|cpn|storage|shelf\\s+life|pao|"
    + "brand(?:\\s+name)?|product(?:\\s+name)?|shade|kind|cosmetic\\s+type|category|sources?|price|sku)\\s*[:：]"      // a field name with its colon
    + "|net\\s*(?:wt|weight|content|vol)\\b|made\\s+in\\b|manufactured\\b|distributed\\b|best\\s+before\\b|exp(?:iry)?\\.?\\s*\\d|batch\\b|lot\\s*(?:no|#))"   // label parts that come without one
    + "|\\n\\s*\\n", "i");
// the start of the "may contain" tail: "May contain", "+/-", "(+/-)", "Peut contenir", "puede contener"
const TAIL = /(?:\(?\s*\+\s*\/\s*-\s*\)?|\bmay\s+contain\b|\bpeut\s+contenir\b|\bpuede\s+contener\b)\s*:?/i;

/** The text of the ingredient list in `text` ("" when there is no "Ingredients:" mark). Stops at the next field or label part. */
export function ingredientBlockOf(text) {
    const source = String(text || "").replace(/\r\n?/g, "\n");
    ANCHOR.lastIndex = 0;
    const hit = ANCHOR.exec(source) || ANCHOR_CAPS.exec(source);
    if (!hit) return "";
    const rest = source.slice(hit.index + hit[0].length);
    const end = END.exec(rest);
    return (end ? rest.slice(0, end.index) : rest).trim();
}

// ---- splitting ------------------------------------------------------------------------------------------------------------
const BULLETS = /[•·▪●‣⁃]/;

/** Splits on the separators, but never inside ( ) or [ ]. */
function splitOutsideBrackets(text, separators) {
    const pieces = [];
    let depth = 0, current = "";
    for (const c of text) {
        if (c === "(" || c === "[") depth++;
        else if ((c === ")" || c === "]") && depth > 0) depth--;
        if (depth === 0 && separators.test(c)) { pieces.push(current); current = ""; } else current += c;
    }
    pieces.push(current);
    return pieces;
}

function tidyName(raw) {
    let name = String(raw || "").normalize("NFKC").replace(/\s+/g, " ").trim();
    name = name.replace(/^(?:and|&|et|y)\s+/i, "");                         // "... , and Sodium Benzoate"
    name = name.replace(/[*†‡]+$/g, "").replace(/^[*†‡]+/, "");   // footnote and "organic" marks
    name = name.replace(/\b(?:C\.?\s?I\.?|colou?r\s+index)\s*(?:No\.?\s*)?(\d{4,5}(?::\d)?)\b/gi, "CI $1");   // C.I. 77891, CI77891, Colour Index No. 77891
    name = name.replace(/\s+(?:exp(?:iry)?\.?|best\s+before|batch|lot|mfg|net\s*(?:wt|weight))\b.*$/i, "");   // label text glued to the last name
    name = name.replace(/[.\s]+$/, "").replace(/^[\s.\-–]+/, "").trim();
    return name;
}

// a piece that is not an ingredient name: only digits / percent signs, or far too long to be a name
const notAName = (name) => name.length < 2 || name.length > 100 || !/[a-z]/i.test(name) || /^\d+([.,]\d+)?\s*%?$/.test(name);

/** The names of one list (no "may contain" tail). "Aqua (and) Glycerin" is two names; "Parfum (Fragrance)" stays one. */
function namesOf(list) {
    const flat = list.normalize("NFKC");
    const hasBullets = BULLETS.test(flat) || /^\s*[-–]\s+\S/m.test(flat);
    const lines = flat.split(/\n/).map((line) => line.trim()).filter(Boolean);
    const noSeparators = !/[,;]/.test(flat) && !BULLETS.test(flat);
    // a list with one name per line (no commas at all) is split on the line breaks; otherwise a line break is only a wrapped line
    const oneEachLine = noSeparators && lines.length >= 3 && lines.every((line) => line.length <= 60);
    let text = flat;
    if (hasBullets) text = flat.replace(/\n\s*[-–]\s+/g, "•").replace(/\n/g, " ");
    else if (oneEachLine) text = lines.join(",");
    else text = flat.replace(/\n/g, " ");
    const out = [];
    for (const piece of splitOutsideBrackets(text, /[,;•·▪●‣⁃]/)) {
        // an INCI blend: "Aqua (and) Glycerin" / "Mica (and) Titanium Dioxide"
        for (const part of piece.split(/\s*\(\s*(?:and|et|y)\s*\)\s*/i)) {
            const name = tidyName(part);
            if (!notAName(name)) out.push(name);
        }
    }
    return out;
}

const withoutRepeats = (names) => names.filter((n, i) => names.findIndex((m) => normalizeName(m) === normalizeName(n)) === i);

/**
 * { ingredients, mayContain } from the text of a list. The part after "May contain" / "+/-" goes to `mayContain`:
 * those colours are not in every batch, so they are never counted as ingredients of the product.
 */
export function splitIngredients(block) {
    const text = String(block || "").replace(/\r\n?/g, "\n");
    const tail = TAIL.exec(text);
    const main = tail ? text.slice(0, tail.index) : text;
    const maybe = tail ? text.slice(tail.index + tail[0].length) : "";
    return { ingredients: withoutRepeats(namesOf(main)), mayContain: withoutRepeats(namesOf(maybe)) };
}

// ---- documents that label each product ------------------------------------------------------------------------------------
const FIELD_LABELS = "brand(?:\\s+name)?|product(?:\\s+name)?|shade|kind|cosmetic\\s+type|category|barcode|" + LABEL_WORDS;
const FIELD = new RegExp("(?:^|[\\s.;])(" + FIELD_LABELS + ")(?:\\s*\\(inci\\))?\\s*[:：]", "gi");
const labelOf = (word) => {
    const w = word.toLowerCase().replace(/\s+/g, " ");
    if (w.startsWith("brand")) return "brand";
    if (w.startsWith("product")) return "product";
    if (w === "kind" || w === "cosmetic type" || w === "category") return "kind";
    if (w === "shade" || w === "barcode") return w;
    return "ingredients";
};
const firstLine = (value) => String(value || "").split(/\n/)[0].replace(/\s+/g, " ").trim();

/**
 * Products in a document that labels them, in any order of lines:
 *     Brand: Maybelline        (a brand line holds for the products after it, until the next brand line)
 *     Product: Superstay Matte Ink
 *     Ingredients: Alcohol Denat., ...
 * Answers like parseImportText: { kind: "inventory", draft: { products, brands }, notes } or { kind: "" } when no product
 * with a brand, a name AND an ingredient list was found. The kind of make-up is taken from the label's own "Kind:" line, otherwise from the product name.
 */
export function parseLabeledProducts(text) {
    const source = String(text || "").replace(/\r\n?/g, "\n");
    const marks = [...source.matchAll(FIELD)].map((m) => ({ label: labelOf(m[1]), start: m.index, valueFrom: m.index + m[0].length }));
    if (marks.length === 0) return { kind: "", notes: [] };
    const fields = marks.map((mark, i) => {
        const raw = source.slice(mark.valueFrom, i + 1 < marks.length ? marks[i + 1].start : source.length);
        return { label: mark.label, value: mark.label === "ingredients" ? ingredientValue(raw) : firstLine(raw) };
    });
    const records = [], notes = [];
    let brand = "", current = null;
    for (const f of fields) {
        if (f.label === "brand") { brand = f.value.slice(0, 60); current = null; }
        else if (f.label === "product") { current = { brand, name: f.value.slice(0, 100), kind: "", barcode: "", ingredients: null }; records.push(current); }
        else if (current) {
            if (f.label === "ingredients" && current.ingredients === null) current.ingredients = f.value;
            else if (f.label === "kind" && !current.kind) current.kind = f.value;
            else if (f.label === "barcode" && !current.barcode) current.barcode = f.value;
        }
    }
    const lines = [];
    records.forEach((r, i) => {
        const names = r.ingredients ? splitIngredients(r.ingredients).ingredients : [];
        if (r.brand.length < 2 || !r.name) { notes.push(`Product ${i + 1} in the file has no brand or no name: left out.`); return; }
        if (names.length < 3) { notes.push(`"${r.name}" of ${r.brand} has no ingredient list of at least 3 names: left out.`); return; }
        const kind = r.kind || makeupType("", r.name);
        lines.push("=== PRODUCT ===", "BRAND_NAME: " + r.brand, "PRODUCT_NAME: " + r.name);
        if (kind) lines.push("COSMETIC_TYPE: " + kind);
        if (r.barcode) lines.push("BARCODE: " + r.barcode);
        lines.push("INGREDIENTS: " + names.map((n) => n.replace(/[|]/g, "/")).join(" | "));
    });
    if (lines.length === 0) return { kind: "", notes };
    const parsed = parseImportText(lines.join("\n"));
    return parsed.kind === "inventory" ? { ...parsed, notes: notes.concat(parsed.notes) } : { kind: "", notes };
}

/** The list inside a labelled field: stops where the next part of the label starts (the field itself already ends at the next label). */
function ingredientValue(raw) {
    const end = END.exec(raw);
    return (end ? raw.slice(0, end.index) : raw).trim();
}

// ---- suggestions for names that are not known -----------------------------------------------------------------------------
function jaro(a, b) {
    if (a === b) return 1;
    if (!a.length || !b.length) return 0;
    const window = Math.max(0, Math.floor(Math.max(a.length, b.length) / 2) - 1);
    const aHit = new Array(a.length).fill(false), bHit = new Array(b.length).fill(false);
    let matches = 0;
    for (let i = 0; i < a.length; i++) {
        for (let j = Math.max(0, i - window); j < Math.min(b.length, i + window + 1); j++) {
            if (!bHit[j] && a[i] === b[j]) { aHit[i] = bHit[j] = true; matches++; break; }
        }
    }
    if (matches === 0) return 0;
    let swaps = 0, k = 0;
    for (let i = 0; i < a.length; i++) {
        if (!aHit[i]) continue;
        while (!bHit[k]) k++;
        if (a[i] !== b[k]) swaps++;
        k++;
    }
    return (matches / a.length + matches / b.length + (matches - swaps / 2) / matches) / 3;
}

/** Jaro-Winkler similarity of two keys, 0 to 1. */
export function similarity(a, b) {
    const j = jaro(a, b);
    let prefix = 0;
    while (prefix < 4 && prefix < a.length && prefix < b.length && a[prefix] === b[prefix]) prefix++;
    return j + prefix * 0.1 * (1 - j);
}

/** At or above this a name is OFFERED as a possible match for an unknown one. A starting value, checked only on the small example set in tests/ingredientBlock.test.mjs: re-check it on real labels before relying on it. */
export const SUGGESTION_THRESHOLD = 0.9;

/**
 * Known names that an unknown `name` may mean, best first (at most `most`). An exact match (same key) is not a suggestion: the caller
 * has it already. Suggestions are only OFFERED to a person; nothing in GlowUp accepts one by itself.
 */
export function suggestMatches(name, known, most = 3) {
    const key = normalizeName(name);
    if (!key) return [];
    return (Array.isArray(known) ? known : [])
        .map((candidate) => ({ name: candidate, key: normalizeName(candidate) }))
        .filter((c) => c.key && c.key !== key)
        .map((c) => ({ name: c.name, score: similarity(key, c.key) }))
        .filter((c) => c.score >= SUGGESTION_THRESHOLD)
        .sort((x, y) => y.score - x.score)
        .slice(0, most);
}
