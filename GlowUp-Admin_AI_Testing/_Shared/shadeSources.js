// Shade colours from the Makeup API (makeup-api.herokuapp.com, free, no key, answers browser pages): for a brand and a
// kind of make-up it lists products with their shades (name + colour code). Pure functions only; the page code fetches.
//
// Safety: a colour from this list is a seller's swatch, not a measured colour. Shades taken from it are marked
// "Needs review" until an admin compares them with the pack (AR Try-On shows them on the user's face). A shade NAME is not
// a shade CODE: only a code printed at the start of the name ("110 Porcelain") is put in the Code box.
import { compactName, normalizeName } from "./glowupRules.js";
import { similarity } from "./glowupCatalog.js";

export const MAKEUP_API = "https://makeup-api.herokuapp.com/api/v1/products.json";
export const MAKEUP_API_LABEL = "Makeup API";

// The brands the Makeup API knows (its "brand" values), checked on 23 Nov 2026.
export const API_BRANDS = [
    "almay", "alva", "anna sui", "annabelle", "benefit", "boosh", "burt's bees", "butter london", "c'est moi", "cargo cosmetics",
    "china glaze", "clinique", "coastal classic creation", "colourpop", "covergirl", "dalish", "deciem", "dior", "dr. hauschka",
    "e.l.f.", "essie", "fenty", "glossier", "green people", "iman", "l'oreal", "lotus cosmetics usa", "maia's mineral galaxy",
    "marcelle", "marienatie", "maybelline", "milani", "mineral fusion", "misa", "mistura", "moov", "nudus", "nyx", "orly",
    "pacifica", "penny lane organics", "physicians formula", "piggy paint", "pure anada", "rejuva minerals", "revlon",
    "sally b's skin yummies", "salon perfect", "sante", "sinful colours", "smashbox", "stila", "suncoat", "w3llpeople",
    "wet n wild", "zorah", "zorah biocosmetiques"
];
const plainKey = (text) => compactName(String(text || "").normalize("NFD").replace(/[̀-ͯ]/g, ""));

// The Makeup API's name for a brand of the inventory ("Maybelline New York" -> "maybelline", "L'Oréal Paris" -> "l'oreal"), or "".
export function apiBrand(brandName) {
    const own = plainKey(brandName);
    if (!own) return "";
    const hits = API_BRANDS.filter((b) => { const k = plainKey(b); return k && (own === k || own.startsWith(k)); });
    return hits.sort((a, b) => plainKey(b).length - plainKey(a).length)[0] || "";
}

// GlowUp's make-up kind -> the Makeup API's product_type (and the category inside it, when the API has one).
const TYPES = {
    "Lipstick": { type: "lipstick" }, "Lip Gloss": { type: "lipstick", category: "lip_gloss" }, "Lip Tint": { type: "lipstick", category: "lip_stain" },
    "Lip Liner": { type: "lip_liner" }, "Foundation": { type: "foundation" }, "Concealer": { type: "foundation", category: "concealer" },
    "Powder": { type: "foundation", category: "powder" }, "Blush": { type: "blush" }, "Bronzer": { type: "bronzer" },
    "Highlighter": { type: "bronzer", category: "highlighter" }, "Contour": { type: "bronzer", category: "contour" },
    "Eyeshadow": { type: "eyeshadow" }, "Eyeliner": { type: "eyeliner" }, "Mascara": { type: "mascara" }, "Brow": { type: "eyebrow" }
};
export const apiTypeFor = (cosmeticType) => TYPES[cosmeticType] || null;

export function makeupApiUrl(brandName, cosmeticType) {
    const brand = apiBrand(brandName), kind = apiTypeFor(cosmeticType);
    if (!brand) return "";
    return `${MAKEUP_API}?brand=${encodeURIComponent(brand)}` + (kind ? `&product_type=${encodeURIComponent(kind.type)}` : "");
}

// "#c41e3a", "C41E3A", "#C41E3A,#AA0000" -> "#C41E3A"; anything else -> "".
export function cleanHex(value) {
    const first = String(value || "").split(/[,;\s]+/).map((v) => v.trim()).find(Boolean) || "";
    const hex = first.startsWith("#") ? first : "#" + first;
    return /^#[0-9a-f]{6}$/i.test(hex) ? hex.toUpperCase() : "";
}
// A shade code printed at the start of the name: "110 Porcelain" -> "110", "N20 Natural" -> "N20"; a plain name gives "".
export function codeFromShadeName(name) {
    const m = String(name || "").trim().toUpperCase().match(/^([A-Z]{0,2}\d{1,3}[A-Z]?)(?=[\s\-.,:]|$)/);
    return m ? m[1] : "";
}

// The Makeup API's products for the form's product, best name match first, each with its usable shades.
// products: the API's answer (a list). Only products with at least one valid colour are kept.
export function shadeMatches(products, productName, cosmeticType, most = 4) {
    const own = normalizeName(productName);
    const kind = apiTypeFor(cosmeticType);
    return (Array.isArray(products) ? products : [])
        .filter((p) => p && typeof p === "object" && p.name)
        .map((p) => {
            const seen = new Set();
            const shades = (Array.isArray(p.product_colors) ? p.product_colors : []).map((c) => {
                const hex = cleanHex(c && c.hex_value), name = String((c && c.colour_name) || "").replace(/\s+/g, " ").trim().slice(0, 60);
                return { hex, name, code: codeFromShadeName(name) };
            }).filter((s) => s.hex && !seen.has(s.hex) && seen.add(s.hex));
            const theirs = normalizeName(p.name);
            let score = own ? similarity(own, theirs) : 0;
            const words = own.split(" ").filter((w) => w.length > 2);
            if (words.length) score = Math.max(score, 0.9 * words.filter((w) => theirs.includes(w)).length / words.length);
            if (kind && kind.category && p.category === kind.category) score += 0.05;
            return { name: String(p.name).replace(/\s+/g, " ").trim().slice(0, 120), category: String(p.category || ""), score: Math.min(1, Math.round(score * 100) / 100), shades };
        })
        .filter((m) => m.shades.length > 0)
        .sort((a, b) => b.score - a.score)
        .slice(0, most);
}

// The shades to add: those whose colour is not in the form yet, at most `room` of them.
export function newShades(shades, current, room) {
    const have = new Set((current || []).map((h) => String(h).toUpperCase()));
    return (shades || []).filter((s) => !have.has(s.hex)).slice(0, Math.max(0, room));
}
