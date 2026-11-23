// Builds _Shared/data/obfCategories.json: Open Beauty Facts category id -> GlowUp make-up type, for Quick fill.
// Each category takes the type of its own English name, or else of its nearest parent that has one
// ("en:matte-liquid-lipsticks" -> parent "en:lipsticks" -> Lipstick). Categories that are not make-up are left out.
//
// Usage (download the taxonomy into its own folder first; it is data, never run):
//   curl -o <dir>/categories.json https://static.openbeautyfacts.org/data/taxonomies/categories.json
//   node tools/obfCategories.mjs <dir>/categories.json
import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { makeupType } from "../_Shared/glowupRules.js";

const here = path.dirname(fileURLToPath(import.meta.url));
const source = process.argv[2];
if (!source) { console.error("Give the path of the downloaded categories.json"); process.exit(1); }
const taxonomy = JSON.parse(readFileSync(source, "utf8"));

const englishName = (id) => {
    const entry = taxonomy[id] || {};
    const name = (entry.name && entry.name.en) || id.replace(/^[a-z]{2}:/, "");
    return String(name).replace(/-/g, " ");
};
const ownType = (id) => makeupType("", englishName(id)) || makeupType(englishName(id), "");

function typeOf(id, seen = new Set()) {
    if (seen.has(id)) return "";
    seen.add(id);
    const own = ownType(id);
    if (own) return own;
    for (const parent of (taxonomy[id] && taxonomy[id].parents) || []) {
        const t = typeOf(parent, seen);
        if (t) return t;
    }
    return "";
}

// Checked by hand against the 199 categories of 23 Nov 2026: the word rules alone get these wrong.
const OVERRIDES = {
    "en:eye-contour-creams": "",          // a skin-care cream for the eye area, not contour make-up
    "en:bronzing-powders": "Bronzer",
    "en:eye-pencils": "Eyeliner",
    "en:dd-creams": null                  // sold as skin care with a tint: undecided, left for the admin
};
// Groups that say nothing about the kind (a product in them alone is neither make-up nor not make-up).
const GENERIC = new Set(["en:makeup", "en:lip-makeup", "en:eyes-makeup", "en:face-makeup", "en:eyes", "en:face", "en:coloured", "en:flavoured",
    "en:halloween-cosmetics", "en:accessories", "en:incorrect-product-type", "en:open-food-facts", "en:open-pet-food-facts", "en:open-products-facts", "en:body", "en:bust"]);

const out = {}, outside = [];
for (const id of Object.keys(taxonomy).sort()) {
    if (OVERRIDES[id] === null) continue;
    const t = id in OVERRIDES ? OVERRIDES[id] : typeOf(id);
    if (t) out[id] = t;
    else if (!GENERIC.has(id)) outside.push(id);     // skin, hair, body, nail, fragrance, hygiene...
}
const target = path.join(here, "../_Shared/data/obfCategories.json");
writeFileSync(target, JSON.stringify({ source: "https://static.openbeautyfacts.org/data/taxonomies/categories.json", builtAt: new Date().toISOString().slice(0, 10), types: out, notMakeup: outside }, null, 1) + "\n");
console.log(`${outside.length} not make-up; ${Object.keys(out).length} of ${Object.keys(taxonomy).length} categories mapped -> ${target}`);
