// Product capture rules for the Add Product form: what a look-up, a label photo or the phone may put into the form, where each
// value came from, and which values a person still has to check. Pure functions only (no page, no network, no database):
// the page code (productQuickFill.js, adminBrandInventory.js) calls these and paints the answers.
//
// The safety rules from the 2026-11-23 automation brief live here:
//   - a value found by a look-up, OCR, an import or the phone is "Detected" until an admin checks it ("Checked");
//   - a value the admin typed is "Typed" (a person entered it, so nothing is waiting);
//   - Quick fill never overwrites a value already in the form without the admin ticking that field;
//   - barcodes stay text with their leading zeros (comparisons use barcodeKey, which ignores the zeros);
//   - nothing here invents a value: a field the sources do not give stays empty ("Missing").
import { barcodeKey, compactName, normalizeName } from "./glowupRules.js";
import { similarity } from "./glowupCatalog.js";

// ---- verification states of one field ----
export const DETECTED = "detected";       // from a look-up, OCR, import or the phone; nobody has checked it
export const NEEDS_REVIEW = "review";     // detected, but the sources disagree or the match was weak
export const VERIFIED = "verified";       // an admin compared it with the pack or a trusted source
export const TYPED = "typed";             // an admin typed or changed it by hand
export const STATE_WORDS = {
    [DETECTED]: "Detected", [NEEDS_REVIEW]: "Needs review", [VERIFIED]: "Checked", [TYPED]: "Typed"
};
const STATES = new Set(Object.keys(STATE_WORDS));
export const waiting = (state) => state === DETECTED || state === NEEDS_REVIEW;

// How a value reached the form.
export const METHODS = {
    lookup: "database look-up", barcode: "barcode look-up", ocr: "text read from a photo", phone: "sent from the phone",
    import: "file import", manual: "typed by an admin", draft: "restored draft"
};

// The form fields Quick fill may fill, in the order the form shows them. "critical": a wrong value here misleads the app's users.
export const FILL_FIELDS = [
    { key: "name", label: "Product name", critical: true },
    { key: "brand", label: "Brand", critical: true },
    { key: "cosmeticType", label: "Cosmetic type", critical: false },
    { key: "imageUrl", label: "Product photo link", critical: false },
    { key: "barcode", label: "Barcode", critical: true },
    { key: "fdaNumber", label: "FDA registration number", critical: true },
    { key: "ingredients", label: "Ingredients", critical: true },
    { key: "shades", label: "Shades", critical: false }          // colours for AR Try-On; never filled by Quick fill itself
];
const FIELD_KEYS = new Set(FILL_FIELDS.map((f) => f.key));
export const fieldLabel = (key) => (FILL_FIELDS.find((f) => f.key === key) || { label: key }).label;

// Match strength of a look-up answer (0 to 1). A routing signal only, never proof: the starting limits of the brief,
// to be tuned against a labelled set of real GlowUp products. Critical fields are checked by a person whatever the score.
export const THRESHOLDS = Object.freeze({ strong: 0.95, check: 0.75 });
export function matchRoute(score, limits = THRESHOLDS) {
    const s = Number(score);
    if (!Number.isFinite(s)) return "unknown";
    if (s >= limits.strong) return "strong";
    if (s >= limits.check) return "check";
    return "weak";
}
export const ROUTE_WORDS = { strong: "Strong match", check: "Check the match", weak: "Weak match", unknown: "" };

const clean = (value) => String(value == null ? "" : value).replace(/\s+/g, " ").trim();

// The barcode as typed or scanned, digits only, LEADING ZEROS KEPT; "" when it is not 6 to 14 digits after the zeros.
// Store this; compare with barcodeKey (which drops the zeros, so 0012345678905 and 12345678905 are the same pack).
export function barcodeDigits(text) {
    const digits = String(text == null ? "" : text).replace(/[\s-]/g, "");
    if (!/^\d+$/.test(digits) || digits.length > 14) return "";
    return barcodeKey(digits) ? digits : "";
}

// One field's value as comparable text.
function asText(key, value) {
    if (Array.isArray(value)) return value.map(clean).filter(Boolean).join(", ");
    return clean(value);
}
function sameValue(key, a, b) {
    if (key === "barcode") return barcodeKey(a) !== "" && barcodeKey(a) === barcodeKey(b);
    if (key === "ingredients") {
        const x = (Array.isArray(a) ? a : String(a || "").split(/[,\n]/)).map(compactName).filter(Boolean);
        const y = (Array.isArray(b) ? b : String(b || "").split(/[,\n]/)).map(compactName).filter(Boolean);
        return x.length === y.length && x.every((n, at) => n === y[at]);
    }
    if (key === "imageUrl") return clean(a) === clean(b);
    return compactName(a) === compactName(b);
}

// What Quick fill would do to the form, one row per field:
//   "new"      the form is empty there and the source has a value: ticked;
//   "same"     both say the same: nothing to do;
//   "conflict" the form already has a different value: NOT ticked (the admin decides);
//   "absent"   the source has no value: the form keeps what it has (a missing value is never guessed).
// current: the form's values with the same keys (brand as its name, ingredients as a list or text).
export function planFill(candidate, current) {
    const c = candidate || {}, now = current || {};
    const fieldFrom = c.fieldFrom || {};
    const conflictKeys = new Set((c.conflicts || []).map((x) => x.key));
    return FILL_FIELDS.map((f) => {
        const proposed = c[f.key], had = now[f.key];
        const proposedText = asText(f.key, proposed), currentText = asText(f.key, had);
        let kind;
        if (!proposedText) kind = "absent";
        else if (!currentText) kind = "new";
        else if (sameValue(f.key, proposed, had)) kind = "same";
        else kind = "conflict";
        return {
            key: f.key, label: f.label, critical: f.critical, kind, apply: kind === "new",
            current: currentText, proposed: proposedText,
            from: fieldFrom[f.key] || (c.origins && c.origins[0]) || c.originLabel || "",
            sourcesDisagree: conflictKeys.has(f.key)
        };
    });
}

// The parts of the candidate the admin chose, ready for the form's fill function (only the ticked keys are present).
export function chosenValues(rows, candidate) {
    const out = {};
    (rows || []).forEach((row) => { if (row.apply && FIELD_KEYS.has(row.key)) out[row.key] = candidate[row.key]; });
    if (out.barcode !== undefined) out.barcode = barcodeDigits(out.barcode) || "";
    return out;
}

// One field's origin record (what the database keeps in products/{id}.fieldOrigins).
export function originEntry(state, from, method, atUnix = Math.floor(Date.now() / 1000)) {
    return {
        state: STATES.has(state) ? state : DETECTED,
        from: clean(from).slice(0, 80),
        method: METHODS[method] ? method : "lookup",
        atUnix: Math.max(0, Math.floor(Number(atUnix) || 0))
    };
}

// The origins after a fill: each applied key becomes Detected (Needs review when the sources disagreed on it, or the match was weak).
export function originsAfterFill(origins, rows, method, score) {
    const out = { ...(origins || {}) };
    const weak = matchRoute(score) === "weak";
    (rows || []).filter((r) => r.apply).forEach((r) => {
        out[r.key] = originEntry(r.sourcesDisagree || weak ? NEEDS_REVIEW : DETECTED, r.from, method);
    });
    return out;
}

// The fields still waiting for a person, in form order, with their labels.
export function uncheckedFields(origins) {
    const o = origins || {};
    return FILL_FIELDS.filter((f) => o[f.key] && waiting(o[f.key].state)).map((f) => ({ key: f.key, label: f.label, critical: f.critical, ...o[f.key] }));
}

// Marks fields as checked by the admin (the rest stay as they are).
export function markChecked(origins, keys) {
    const out = { ...(origins || {}) };
    (keys || []).forEach((k) => { if (out[k]) out[k] = { ...out[k], state: VERIFIED }; });
    return out;
}

// Only known keys, known states and short texts go to the database (and an old record's junk is dropped on read).
export function cleanOrigins(raw) {
    const out = {};
    if (!raw || typeof raw !== "object") return out;
    for (const key of Object.keys(raw)) {
        if (!FIELD_KEYS.has(key) || !raw[key] || typeof raw[key] !== "object") continue;
        const r = raw[key];
        out[key] = originEntry(STATES.has(r.state) ? r.state : DETECTED, r.from, r.method, r.atUnix);
    }
    return out;
}

// ---- duplicates and variants (shown, never merged or blocked here) ----

// Other products that may be the same product or a variant of it: same barcode at another product, or the same brand with a
// close name (a shade, a size or a version of the same line). Exact same brand + name is refused by the form itself.
export function similarProducts({ id = "", name = "", brandId = "", barcode = "" }, products, most = 4) {
    const own = normalizeName(name), code = barcodeKey(barcode);
    const out = [];
    for (const p of products || []) {
        if (!p || p.binned || p.id === id) continue;
        if (code && barcodeKey(p.barcode) === code) { out.push({ product: p, why: "same barcode", score: 1 }); continue; }
        if (!own || own.length < 3 || !brandId || p.brandId !== brandId) continue;
        const theirs = normalizeName(p.name);
        if (!theirs || compactName(theirs) === compactName(own)) continue;
        const score = Math.max(similarity(own, theirs), (theirs.includes(own) || own.includes(theirs)) && Math.min(own.length, theirs.length) >= 6 ? 0.8 : 0);
        if (score >= 0.75) out.push({ product: p, why: "close name, same brand", score });
    }
    return out.sort((a, b) => b.score - a.score).slice(0, most);
}

// Brands whose name is close to a new brand's name (spelling, spacing, "&" vs "and"); the exact name is refused by the form itself.
export function similarBrands(name, brands, id = "", most = 3) {
    const own = normalizeName(name);
    if (!own || own.length < 2) return [];
    return (brands || [])
        .filter((b) => b && !b.binned && b.id !== id)
        .map((b) => ({ brand: b, score: Math.max(...[b.name, ...(b.aliases || [])].map((n) => (compactName(n) === compactName(own) ? 1 : similarity(own, normalizeName(n))))) }))
        .filter((x) => x.score >= 0.8 && x.score < 1)
        .sort((a, b) => b.score - a.score).slice(0, most);
}

// ---- photos from the gallery ----
export const PHOTO_ROLES = [
    { id: "ingredients", label: "Ingredient list" },
    { id: "barcode", label: "Barcode" },
    { id: "front", label: "Front of the pack" },
    { id: "back", label: "Back of the pack" },
    { id: "shade", label: "Shade label" }
];
// A first guess from the file name ("IMG_ingredients.jpg", "barcode-2.png"); the admin can change it.
export function guessPhotoRole(fileName, at = 0) {
    const n = String(fileName || "").toLowerCase();
    if (/ingred|inci|compos/.test(n)) return "ingredients";
    if (/bar.?code|ean|upc/.test(n)) return "barcode";
    if (/front/.test(n)) return "front";
    if (/back|rear/.test(n)) return "back";
    if (/shade|swatch|colou?r/.test(n)) return "shade";
    return at === 0 ? "ingredients" : "back";
}

// Text read from a front or back photo: the lines worth showing (no blank or one-letter noise), at most `most`.
export function usefulLines(text, most = 12) {
    return String(text || "").split(/\r?\n/).map(clean).filter((l) => l.length >= 3 && /[a-z]{2}/i.test(l)).slice(0, most);
}

// ---- review decisions ----
export const REVIEW_CHANGES = "changes";   // sent back to be corrected; stays Pending
export function reviewReason(text) {
    const t = clean(text);
    return t.length >= 5 && t.length <= 500 ? t : "";
}

// ---- file import: one line per row that was NOT saved, so no row disappears without a word ----
export function skippedLine({ row, name, brandName, why }) {
    const what = clean(name) ? `"${clean(name).slice(0, 60)}"` : "(no product name)";
    return `Row ${Number(row) || "?"}: ${what}${clean(brandName) ? " of " + clean(brandName).slice(0, 40) : ""}: ${why}`;
}

// ---- ingredient lists typed or pasted in the form ----
// Names that appear more than once in a pasted list (the form keeps each once): shown, so a repeat is never dropped without a word.
export function repeatedNames(text) {
    const counts = new Map();
    String(text || "").split(/[,;\n]+/).map(clean).filter(Boolean).forEach((n) => {
        const k = n.toLowerCase();
        counts.set(k, { name: (counts.get(k) || { name: n }).name, n: ((counts.get(k) || {}).n || 0) + 1 });
    });
    return Array.from(counts.values()).filter((x) => x.n > 1).map((x) => x.name);
}
