// The small rules behind the editable lists (allergen families, highlights, skin conditions) and the shade checks.
// No page, no database: kept apart so they can be checked (tests/lists.test.mjs).
//
// One document, config/lists, holds the three lists:
//   { version, updatedAt, allergens: [item], conditions: [item], highlights: [item] }
//   item = { id, label, keywords: [text], retired: bool }
// The ID NEVER CHANGES. Ingredients keep the ids (allergenTags, avoidForConditions, goodForConditions, highlights), so a
// rename touches only the label. A user's saved profile keeps the label text it was ticked with; the app finds the item
// again through its keywords, and a rename keeps the old label as a keyword, so saved profiles keep working.
// A retired item is hidden from the pickers (the website's and the app's) but still matches what is already saved.
import { similarity } from "./glowupCatalog.js";
import { compactName } from "./glowupRules.js";

export const MAX_ITEMS = 40;
export const MAX_LABEL = 40;
export const MAX_KEYWORDS = 8;
/** The two highlights the app has always had: ingredient fields keyActive and umbrella. They cannot be removed. */
export const BUILT_IN_HIGHLIGHTS = ["keyActive", "umbrella"];

/** The lists the website and the app start with (also used while the document does not exist yet). */
export const BUILT_IN = {
    allergens: [
        { id: "fragrance", label: "Fragrance / Parfum", keywords: ["fragrance", "parfum", "perfume"] },
        { id: "paraben", label: "Parabens", keywords: ["paraben"] },
        { id: "sulfate", label: "Sulfates (SLS / SLES)", keywords: ["sulfate", "sulphate", "sls", "sles"] },
        { id: "formaldehyde", label: "Formaldehyde releasers", keywords: ["formaldehyde"] },
        { id: "lanolin", label: "Lanolin", keywords: ["lanolin"] },
        { id: "essentialoil", label: "Essential oils", keywords: ["essential oil", "essential oils"] },
        { id: "nut", label: "Nut-derived oils", keywords: ["nut"] }
    ],
    conditions: [
        { id: "acne", label: "Acne", keywords: ["acne"] },
        { id: "eczema", label: "Eczema (Atopic Dermatitis)", keywords: ["eczema", "atopic"] },
        { id: "psoriasis", label: "Psoriasis", keywords: ["psoriasis"] },
        { id: "rosacea", label: "Rosacea", keywords: ["rosacea"] },
        { id: "seborrheic", label: "Seborrheic Dermatitis", keywords: ["seborrheic"] },
        { id: "melasma", label: "Melasma / Hyperpigmentation", keywords: ["melasma", "hyperpigmentation"] },
        { id: "contact", label: "Contact Dermatitis", keywords: ["contact"] }
    ],
    highlights: [
        { id: "keyActive", label: "Key active", keywords: [] },
        { id: "umbrella", label: "Umbrella term", keywords: [] }
    ]
};

const text = (value, most) => String(value === null || value === undefined ? "" : value).replace(/\s+/g, " ").trim().slice(0, most);

/** One item, with only what is allowed in it. Null when it has no usable id or label. */
export function cleanItem(raw) {
    const row = raw && typeof raw === "object" ? raw : {};
    const id = text(row.id, 24);
    const label = text(row.label, MAX_LABEL);
    if (!/^[A-Za-z0-9]{1,24}$/.test(id) || label.length === 0) return null;
    const seen = new Set();
    const keywords = (Array.isArray(row.keywords) ? row.keywords : []).map((k) => text(k, 30).toLowerCase())
        .filter((k) => k.length >= 2 && !seen.has(k) && seen.add(k)).slice(0, MAX_KEYWORDS);
    return { id, label, keywords, retired: row.retired === true };
}

function cleanList(raw, fallback) {
    const source = Array.isArray(raw) ? raw : fallback;
    const ids = new Set();
    return source.map(cleanItem).filter((item) => item && !ids.has(item.id.toLowerCase()) && ids.add(item.id.toLowerCase())).slice(0, MAX_ITEMS);
}

/** What a stored document (or nothing) means: three clean lists, the built-in highlights always first. */
export function normalizeLists(raw) {
    const data = raw && typeof raw === "object" ? raw : {};
    const highlights = cleanList(data.highlights, BUILT_IN.highlights);
    BUILT_IN_HIGHLIGHTS.slice().reverse().forEach((id) => {
        const at = highlights.findIndex((item) => item.id === id);
        const built = BUILT_IN.highlights.find((item) => item.id === id);
        if (at < 0) highlights.unshift(Object.assign({ retired: false }, built));
        else { const kept = highlights.splice(at, 1)[0]; kept.retired = false; highlights.unshift(kept); }
    });
    return {
        version: Math.max(0, Math.floor(Number(data.version) || 0)),
        allergens: cleanList(data.allergens, BUILT_IN.allergens),
        conditions: cleanList(data.conditions, BUILT_IN.conditions),
        highlights
    };
}

/** A new id from a label: letters and digits only, lower case, never one that is taken or reserved. */
export function slugId(label, taken) {
    const used = new Set((taken || []).map((id) => String(id).toLowerCase()));
    BUILT_IN_HIGHLIGHTS.forEach((id) => used.add(id.toLowerCase()));
    let base = String(label || "").toLowerCase().normalize("NFD").replace(/[̀-ͯ]/g, "").replace(/[^a-z0-9]/g, "").slice(0, 20);
    if (base.length < 2) base = "item";
    let id = base, n = 2;
    while (used.has(id)) { id = base + n; n++; }
    return id;
}

/**
 * Whether `label` may join `items` (or, with exceptId, stay in it under a new name).
 *   error    - not allowed: empty, or the same name (or id) as another item
 *   warnings - allowed after a look: a very similar name, or a keyword that another item already uses
 */
export function checkItem(items, label, keywords, exceptId) {
    const name = text(label, 200);
    const key = compactName(name);
    if (!name) return { error: "Type a name first.", warnings: [] };
    if (name.length > MAX_LABEL) return { error: `Use at most ${MAX_LABEL} characters.`, warnings: [] };
    const warnings = [];
    for (const other of (items || []).filter((item) => item.id !== exceptId)) {
        const otherKey = compactName(other.label);
        if (otherKey === key || other.id.toLowerCase() === key) {
            return { error: `"${other.label}" is in this list already${other.retired ? " (retired: restore it instead)" : ""}.`, warnings: [] };
        }
        const near = similarity(name, other.label) >= 0.8 || (key.length >= 4 && otherKey.length >= 4 && (key.includes(otherKey) || otherKey.includes(key)));
        if (near) warnings.push(`It looks like "${other.label}".`);
        const shared = (keywords || []).find((k) => compactName(k) === otherKey || (other.keywords || []).some((own) => compactName(own) === compactName(k)));
        if (shared) warnings.push(`The word "${shared}" already stands for "${other.label}".`);
    }
    return { error: "", warnings: warnings.filter((line, index, all) => all.indexOf(line) === index) };
}

/** A renamed item keeps its old label as a keyword, so what users saved under the old name is still found. */
export function keepOldName(item, oldLabel) {
    const old = text(oldLabel, 30).toLowerCase();
    if (!old || item.keywords === undefined) return item;
    if (compactName(old) !== compactName(item.label) && !item.keywords.includes(old)) item.keywords = item.keywords.concat(old).slice(-MAX_KEYWORDS);
    return item;
}

/** How many ingredients point at an item. `ingredients` = the website's ingredient objects. */
export function usageCount(kind, id, ingredients) {
    const same = (list) => (Array.isArray(list) ? list : []).some((value) => String(value).toLowerCase() === String(id).toLowerCase());
    return (ingredients || []).filter((ingredient) => {
        if (kind === "allergens") return same(ingredient.allergenTags);
        if (kind === "conditions") return same(ingredient.avoidForConditions) || same(ingredient.goodForConditions);
        if (kind === "highlights") return id === "keyActive" ? ingredient.keyActive === true : id === "umbrella" ? ingredient.umbrella === true : same(ingredient.highlights);
        return false;
    }).length;
}

// ---- shades -----------------------------------------------------------------------------------------------------

const channels = (hex) => [1, 3, 5].map((at) => parseInt(String(hex).slice(at, at + 2), 16));

/** How far apart two "#RRGGBB" colours are (straight line through red, green, blue: 0 = the same, about 441 = black and white). */
export function colourDistance(a, b) {
    const x = channels(a), y = channels(b);
    return Math.round(Math.sqrt((x[0] - y[0]) ** 2 + (x[1] - y[1]) ** 2 + (x[2] - y[2]) ** 2));
}

/** The shade in `shades` that is almost, but not exactly, this colour (within `limit`), or "". */
export function nearShade(shades, hex, limit = 18) {
    let best = "", bestDistance = Infinity;
    (shades || []).forEach((other) => {
        if (String(other).toUpperCase() === String(hex).toUpperCase()) return;
        const distance = colourDistance(other, hex);
        if (distance <= limit && distance < bestDistance) { best = other; bestDistance = distance; }
    });
    return best;
}
