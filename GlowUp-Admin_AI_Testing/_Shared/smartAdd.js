// Quick fill for the admin forms: turns what an admin has in hand (a barcode, a product name, a photo of the label) into
// form values, so the admin does not open other websites first. Pure functions only (no page, no network): the page
// code in quickFill.js fetches, this file decides. Nothing here guesses a safety rating.
import { ingredientBlockOf, splitIngredients } from "./ingredientBlock.js";
import { makeupType, judgeProduct, barcodeKey, compactName, normalizeName } from "./glowupRules.js";
import { similarity } from "./glowupCatalog.js";

export const OFF_HOST = "https://world.openbeautyfacts.org";
export const OFF_FIELDS = "code,product_name,product_name_en,brands,ingredients_text_en,ingredients_text,categories_tags,image_front_url,url";
export const offProductUrl = (code) => `${OFF_HOST}/api/v2/product/${encodeURIComponent(code)}.json?fields=${OFF_FIELDS}`;
export const offSearchUrl = (text) => `${OFF_HOST}/cgi/search.pl?search_terms=${encodeURIComponent(String(text || "").trim())}&search_simple=1&action=process&json=1&page_size=8&fields=${OFF_FIELDS}`;
export const offPageUrl = (code) => `${OFF_HOST}/product/${encodeURIComponent(code)}`;
export const pubchemSynonymsUrl = (name) => `https://pubchem.ncbi.nlm.nih.gov/rest/pug/compound/name/${encodeURIComponent(String(name || "").trim())}/synonyms/JSON`;
export const pubchemDescriptionUrl = (name) => `https://pubchem.ncbi.nlm.nih.gov/rest/pug/compound/name/${encodeURIComponent(String(name || "").trim())}/description/JSON`;
export const pubchemPage = (cid) => `https://pubchem.ncbi.nlm.nih.gov/compound/${Number(cid)}`;
export const FDA_PORTAL = "https://verification.fda.gov.ph";

const clean = (value) => String(value == null ? "" : value).replace(/\s+/g, " ").trim();

// What the admin typed: a barcode (6 to 14 digits, spaces allowed) or words. A barcode keeps its leading zeros in "value"
// (Open Beauty Facts stores 0-led codes with the zeros); "key" is the zero-less form used to compare two barcodes.
export function queryKind(text) {
    const typed = clean(text);
    if (!typed) return { kind: "empty", value: "", key: "" };
    const digits = typed.replace(/[\s-]/g, "");
    if (/^\d{6,14}$/.test(digits) && barcodeKey(digits)) return { kind: "barcode", value: digits, key: barcodeKey(digits) };
    return { kind: "name", value: typed, key: "" };
}

// A list as printed ("Aqua, Glycerin, ...") or a whole label photo read by OCR: the ingredient names in order.
export function ingredientsFromText(text) {
    const source = String(text || "");
    const block = ingredientBlockOf(source) || source;
    const names = (splitIngredients(block).ingredients || []).map(clean).filter((n) => n.length >= 2 && n.length <= 70 && /[a-z]/i.test(n));
    const seen = new Set();
    return names.filter((n) => { const k = n.toLowerCase(); if (seen.has(k)) return false; seen.add(k); return true; });
}

function typeFor(name, categoryWords) {
    const fromName = makeupType("", name);
    if (fromName) return fromName;
    for (const word of categoryWords) { const t = makeupType(word, name); if (t) return t; }
    return "";
}

// One Open Beauty Facts product (the API's "product" object) as a candidate, or null when it has no name.
// The category table built from Open Beauty Facts' own category list (tools/obfCategories.mjs -> _Shared/data/obfCategories.json):
// { types: { "en:lipsticks": "Lipstick", ... }, notMakeup: ["en:lip-balms", ...] }. Optional: without it the words decide.
export function typeFromCategories(tags, table) {
    const types = (table && table.types) || {};
    for (const tag of tags || []) { const t = types[String(tag)]; if (t) return t; }
    return "";
}
export function outsideCategory(tags, table) {
    const outside = new Set((table && table.notMakeup) || []);
    const hit = (tags || []).map(String).find((t) => outside.has(t));
    return hit ? hit.replace(/^[a-z]{2}:/, "").replace(/-/g, " ") : "";
}

export function fromOpenBeautyFacts(p, table) {
    if (!p || typeof p !== "object") return null;
    const name = clean(p.product_name_en || p.product_name);
    if (!name) return null;
    const brand = clean(String(p.brands || "").split(",")[0]);
    const tags = Array.isArray(p.categories_tags) ? p.categories_tags : [];
    const categories = tags.map((t) => String(t).replace(/^[a-z]{2}:/, "").replace(/-/g, " "));
    const fromTable = typeFromCategories(tags, table);
    const outside = fromTable ? "" : outsideCategory(tags, table);
    const ingredients = ingredientsFromText(p.ingredients_text_en || p.ingredients_text || "");
    const code = clean(p.code);
    const image = /^https:\/\//i.test(String(p.image_front_url || "")) ? String(p.image_front_url) : "";
    return {
        origin: "openbeautyfacts", originLabel: "Open Beauty Facts",
        name, brand, cosmeticType: fromTable || typeFor(name, categories), ingredients,
        ingredientsText: clean(p.ingredients_text_en || p.ingredients_text).slice(0, 4000),
        outsideCategory: outside,
        barcode: barcodeKey(code) ? code.replace(/\D/g, "") : "", fdaNumber: "", imageUrl: image,
        sources: code ? [offPageUrl(code)] : [],
        note: [ingredients.length ? "" : "No ingredient list on Open Beauty Facts.", outside ? `Open Beauty Facts files it under "${outside}" (not make-up).` : ""].filter(Boolean).join(" ")
    };
}

// ---- UPCitemdb (second barcode look-up, through the website's own api/upc-lookup function) ----

// A barcode whose last digit matches the GS1 check digit (EAN-8, UPC-A, EAN-13, GTIN-14). A mistyped code fails here,
// so it never uses up the free look-ups (100 a day).
export function gtinValid(code) {
    const digits = String(code || "").replace(/\D/g, "");
    if (![8, 12, 13, 14].includes(digits.length)) return false;
    let sum = 0;
    for (let i = digits.length - 2, weight = 3; i >= 0; i--, weight = weight === 3 ? 1 : 3) sum += Number(digits[i]) * weight;
    return (10 - (sum % 10)) % 10 === Number(digits[digits.length - 1]);
}
export const upcItemPage = (code) => `https://www.upcitemdb.com/upc/${encodeURIComponent(String(code || "").replace(/\D/g, ""))}`;

// One UPCitemdb item as a candidate. It gives a title, a brand, a shop category and pictures: never an ingredient list.
export function fromUpcItemDb(item, code, table) {
    if (!item || typeof item !== "object") return null;
    const brand = clean(item.brand);
    let name = clean(item.title);
    if (!name) return null;
    if (brand && name.toLowerCase().startsWith(brand.toLowerCase() + " ")) name = name.slice(brand.length + 1).trim();
    // shop titles add selling words after a colon and the size ("... - 141 Very Black - 0.43 fl oz: Thickening, ..."): drop both
    name = name.split(/:\s/)[0].replace(/\s*[\-,]\s*\d+([\.,]\d+)?\s*(fl\.?\s*oz|oz|ml|g)\b\.?/gi, "").trim();
    const categoryWords = String(item.category || "").split(/[>,&]/).map(clean).filter(Boolean);
    const barcode = String(code || item.ean || item.upc || "").replace(/\D/g, "");
    const image = (Array.isArray(item.images) ? item.images : []).find((u) => /^https:\/\//i.test(String(u))) || "";
    return {
        origin: "upcitemdb", originLabel: "UPCitemdb",
        name: name.slice(0, 100), brand, cosmeticType: typeFor(name, categoryWords), ingredients: [], ingredientsText: "",
        barcode: barcodeKey(barcode) ? barcode : "", fdaNumber: "", imageUrl: image,
        sources: barcode ? [upcItemPage(barcode)] : [],
        note: "UPCitemdb gives the name and the brand, not the ingredient list." + (item.category ? ` Shop category: ${clean(item.category).slice(0, 80)}.` : "")
    };
}

// One entry of the FDA Philippines catalogue the site ships with (_Shared/data/fdaCatalogue.json) as a candidate.
export function fromFdaCatalogue(entry) {
    if (!entry || !entry.name) return null;
    const name = clean(entry.name), brand = clean(entry.brand);
    return {
        origin: "fda", originLabel: "FDA Philippines list",
        name, brand, cosmeticType: typeFor(name, [entry.type || ""]) || (makeupType(entry.type || "", name) || ""),
        ingredients: (entry.ingredients || []).map(clean).filter(Boolean),
        ingredientsText: (entry.ingredients || []).map(clean).filter(Boolean).join(", ").slice(0, 4000),
        barcode: "", fdaNumber: clean(entry.fdaNumber), imageUrl: "",
        sources: [FDA_PORTAL + "/"], note: entry.validUntil ? `Notified until ${clean(entry.validUntil)}.` : ""
    };
}

// How well a candidate fits the words typed (0 to 1), brand words count too.
export function scoreCandidate(typed, candidate) {
    const query = normalizeName(typed);
    if (!query || !candidate) return 0;
    const whole = normalizeName(`${candidate.brand} ${candidate.name}`), nameOnly = normalizeName(candidate.name);
    if (compactName(whole) === compactName(query) || compactName(nameOnly) === compactName(query)) return 1;
    const words = query.split(" ").filter((w) => w.length > 1);
    const hits = words.filter((w) => whole.includes(w)).length;
    const cover = words.length ? hits / words.length : 0;
    return Math.max(cover * 0.9, similarity(query, whole), similarity(query, nameOnly));
}

export function searchCatalogue(typed, catalogue, most = 5) {
    const list = (catalogue && catalogue.products) || [];
    return list.map(fromFdaCatalogue).filter(Boolean)
        .map((c) => ({ c, s: scoreCandidate(typed, c) }))
        .filter((x) => x.s >= 0.55).sort((a, b) => b.s - a.s).slice(0, most).map((x) => ({ ...x.c, score: Math.round(x.s * 100) / 100 }));
}

// Fills the gaps of the best candidate from the others that are the same product (same barcode, or same brand and a close name),
// so a barcode hit without a list still gets the FDA list and number. The sources of every part used are kept, and so is
// which source gave each field (fieldFrom) and where two sources disagree (conflicts): the admin sees both, nothing is chosen silently.
const MERGED_FIELDS = ["name", "brand", "cosmeticType", "barcode", "fdaNumber", "imageUrl", "ingredients"];
function sameField(key, a, b) {
    if (key === "barcode") return barcodeKey(a) === barcodeKey(b);
    if (key === "ingredients") return a.length === b.length && a.every((n, at) => compactName(n) === compactName(b[at]));
    if (key === "imageUrl") return a === b;
    return compactName(a) === compactName(b);
}
const hasField = (v) => (Array.isArray(v) ? v.length > 0 : !!v);
export function mergeCandidates(list) {
    const items = (list || []).filter(Boolean);
    if (items.length === 0) return [];
    const same = (a, b) => (barcodeKey(a.barcode) && barcodeKey(a.barcode) === barcodeKey(b.barcode))
        || (compactName(a.brand) && compactName(a.brand) === compactName(b.brand) && similarity(normalizeName(a.name), normalizeName(b.name)) >= 0.85);
    const groups = [];
    for (const item of items) {
        const group = groups.find((g) => same(g[0], item));
        if (group) group.push(item); else groups.push([item]);
    }
    return groups.map((group) => {
        const out = { ...group[0], sources: [], origins: [], fieldFrom: {}, conflicts: [] };
        for (const part of group) {
            for (const key of MERGED_FIELDS) {
                const theirs = part[key];
                if (!hasField(theirs)) continue;
                if (!hasField(out[key])) { out[key] = Array.isArray(theirs) ? theirs.slice() : theirs; out.fieldFrom[key] = part.originLabel; continue; }
                if (!out.fieldFrom[key]) out.fieldFrom[key] = part.originLabel;
                if (part !== group[0] && !sameField(key, out[key], theirs)) {
                    let c = out.conflicts.find((x) => x.key === key);
                    if (!c) { c = { key, values: [{ from: out.fieldFrom[key], value: Array.isArray(out[key]) ? out[key].join(", ") : String(out[key]) }] }; out.conflicts.push(c); }
                    c.values.push({ from: part.originLabel, value: Array.isArray(theirs) ? theirs.join(", ") : String(theirs) });
                }
            }
            out.sources.push(...(part.sources || []));
            out.origins.push(part.originLabel);
        }
        out.sources = Array.from(new Set(out.sources)).slice(0, 10);
        out.origins = Array.from(new Set(out.origins));
        out.makeup = judgeProduct(out.cosmeticType, out.name).kind;
        return out;
    });
}

// The brand of the inventory a candidate belongs to (by name or other name), or null: then a new brand is offered.
// A strong match is the exact name or other name; or else ONE brand whose name begins with the other ("Maybelline" and
// "Maybelline New York"), at least 5 letters long. Two or more such brands = ambiguous = null (the admin chooses).
export function brandFor(name, brands) {
    const key = compactName(name);
    if (!key) return null;
    const live = (brands || []).filter((b) => !b.binned);
    const exact = live.find((b) => [b.name, ...(b.aliases || [])].some((n) => compactName(n) === key));
    if (exact) return exact;
    const prefix = live.filter((b) => [b.name, ...(b.aliases || [])].some((n) => {
        const k = compactName(n);
        return k && Math.min(k.length, key.length) >= 5 && (k.startsWith(key) || key.startsWith(k));
    }));
    return prefix.length === 1 ? prefix[0] : null;
}

// Where to read more about a candidate on the maker's own pages: the brand's CONFIRMED website from the inventory (an admin checked that
// the brand owns it) and, for any brand, the FDA Philippines portal. Only https addresses are returned. The search address is a
// guess at the site's own search page (most shops use ?q=): it may land on a page that says "not found", so it is labelled as a search.
export function officialLinks(candidate, brands) {
    const brand = brandFor(candidate && candidate.brand, brands);
    const links = [];
    let site = "";
    try { const url = new URL(brand && brand.website ? brand.website : ""); if (url.protocol === "https:") site = url.origin; } catch (error) { site = ""; }
    if (site) {
        links.push({ label: "Brand's official site", url: site + "/" });
        const typed = encodeURIComponent(String((candidate && candidate.name) || "").trim());
        if (typed) links.push({ label: "Search the official site", url: `${site}/search?q=${typed}` });
    }
    links.push({ label: "FDA Philippines portal", url: FDA_PORTAL + "/" });
    return links;
}

// A product of the inventory that is the same as the candidate (barcode, or brand + name), or null.
export function existingProduct(candidate, products, brands) {
    if (!candidate) return null;
    const code = barcodeKey(candidate.barcode || "");
    const byCode = code ? (products || []).find((p) => !p.binned && barcodeKey(p.barcode || "") === code) : null;
    if (byCode) return byCode;
    const brand = brandFor(candidate.brand, brands);
    if (!brand) return null;
    return (products || []).find((p) => !p.binned && p.brandId === brand.id && [p.name, ...(p.aliases || [])].some((n) => compactName(n) === compactName(candidate.name))) || null;
}

// ---- ingredient quick fill (PubChem answers) ----

// PubChem's synonym list: the INCI-looking names first, no registry numbers, at most `most`.
export function aliasesFromPubchem(json, name, most = 6) {
    const info = json && json.InformationList && json.InformationList.Information && json.InformationList.Information[0];
    const list = info && Array.isArray(info.Synonym) ? info.Synonym : [];
    const own = compactName(name);
    const out = [];
    for (const raw of list) {
        const s = clean(raw);
        if (!s || s.length > 50 || compactName(s) === own) continue;
        if (/^\d+-\d+-\d$/.test(s) || /^[A-Z0-9]{10}$/.test(s) || /\d{5,}/.test(s) || /^(EC|UNII|CHEBI|DTXSID|NSC|SCHEMBL|AKOS|MFCD)\b/i.test(s)) continue;
        if (out.some((o) => compactName(o) === compactName(s))) continue;
        out.push(s);
        if (out.length >= most) break;
    }
    return { cid: info ? Number(info.CID) || 0 : 0, aliases: out };
}

// PubChem's first description text (technical wording), cut to `most` characters at a sentence end.
export function descriptionFromPubchem(json, most = 600) {
    const list = (json && json.InformationList && json.InformationList.Information) || [];
    const hit = list.find((i) => i && typeof i.Description === "string" && i.Description.length > 20);
    if (!hit) return { cid: list[0] ? Number(list[0].CID) || 0 : 0, text: "" };
    let text = clean(hit.Description);
    if (text.length > most) { const cut = text.slice(0, most); const end = cut.lastIndexOf(". "); text = end > 80 ? cut.slice(0, end + 1) : cut.trim() + "..."; }
    return { cid: Number(hit.CID) || 0, text };
}

// A first classification from words in the name and the technical text. Only a suggestion: the admin sees it chosen and can change it.
const CLASS_WORDS = [
    ["Preservative", /\b(paraben|phenoxyethanol|benzoate|sorbate|chlorphenesin|ethylhexylglycerin|isothiazolinone|dmdm|imidazolidinyl|benzyl alcohol|caprylyl glycol)\b/i],
    ["Colorant / Pigment", /\b(ci \d{5}|iron oxide|titanium dioxide|ultramarine|mica|carmine|red \d+|yellow \d+|blue \d+|lake\b|chromium oxide|manganese violet|bismuth oxychloride)\b/i],
    ["Fragrance", /\b(parfum|fragrance|aroma|limonene|linalool|citral|geraniol|eugenol|coumarin|citronellol|hexyl cinnamal|benzyl salicylate)\b/i],
    ["Emollient", /\b(dimethicone|cyclopentasiloxane|squalane|caprylic|triglyceride|isododecane|isononyl|octyldodecanol|ester|butter|oil\b|lanolin|petrolatum|paraffin|wax)\b/i],
    ["Humectant", /\b(glycerin|glycerol|hyaluronate|hyaluronic|propanediol|butylene glycol|propylene glycol|sorbitol|panthenol|urea)\b/i],
    ["Antioxidant", /\b(tocopherol|tocopheryl|ascorbic|ascorbyl|bht|bha|ferulic|retinyl)\b/i],
    ["Thickener", /\b(silica|cellulose|xanthan|carbomer|bentonite|hectorite|starch|gum\b|acrylates)\b/i],
    ["Emulsifier", /\b(polysorbate|lecithin|ceteareth|peg-\d+|steareth|glyceryl stearate|sorbitan)\b/i],
    ["Solvent", /\b(aqua|water|alcohol denat|ethanol|isopropyl alcohol)\b/i],
    ["UV filter", /\b(avobenzone|octinoxate|octocrylene|oxybenzone|homosalate|zinc oxide|ethylhexyl methoxycinnamate)\b/i]
];
export function suggestClassification(name, text, classifications) {
    const words = `${name || ""} ${text || ""}`;
    for (const [label, pattern] of CLASS_WORDS) {
        if (!pattern.test(words)) continue;
        const found = (classifications || []).find((c) => compactName(c.name) === compactName(label) || compactName(c.name).startsWith(compactName(label).slice(0, 6)));
        if (found) return found;
    }
    return null;
}

// Plain words for the user-friendly definition box, built from what is known for sure (the classification), never a safety claim.
export function friendlyStarter(name, classificationName) {
    const kind = clean(classificationName).toLowerCase();
    if (!kind || kind === "other") return "";
    const article = /^[aeiou]/.test(kind) ? "an" : "a";
    return `${clean(name)} is ${article} ${kind} used in make-up.`;
}

// ---- the Open Beauty Facts make-up dump that ships with the site (_Shared/data/obfMakeup.json, built by tools/buildObfMakeup.mjs) ----
// A row is [code, name, brand, type, ingredient text]. Looking a batch of hundreds up here needs no network and has no rate limit.
export function fromObfDump(row) {
    if (!Array.isArray(row) || row.length < 5) return null;
    const [code, name, brand, type, text] = row.map((v) => clean(v));
    if (!name) return null;
    const ingredients = ingredientsFromText(text);
    return {
        origin: "openbeautyfacts", originLabel: "Open Beauty Facts (local copy)",
        name, brand, cosmeticType: type, ingredients, ingredientsText: text.slice(0, 4000), outsideCategory: "",
        barcode: code.replace(/\D/g, ""), fdaNumber: "", imageUrl: "", sources: code ? [offPageUrl(code)] : [],
        note: ingredients.length ? "" : "No ingredient list on Open Beauty Facts."
    };
}

// The row for a barcode (zeros ignored, so a 12-digit UPC-A finds its 13-digit EAN form), or null.
export function findInObfDump(code, dump) {
    const key = barcodeKey(code);
    const items = (dump && dump.items) || [];
    if (!key) return null;
    if (!dump.byKey) Object.defineProperty(dump, "byKey", { value: new Map(items.map((row) => [barcodeKey(row[0]), row]).filter(([k]) => k)), enumerable: false });
    const row = dump.byKey.get(key);
    return row ? fromObfDump(row) : null;
}

export function searchObfDump(typed, dump, most = 5) {
    return ((dump && dump.items) || []).map(fromObfDump).filter(Boolean)
        .map((c) => ({ c, s: scoreCandidate(typed, c) }))
        .filter((x) => x.s >= 0.6).sort((a, b) => b.s - a.s).slice(0, most).map((x) => ({ ...x.c, score: Math.round(x.s * 100) / 100 }));
}
