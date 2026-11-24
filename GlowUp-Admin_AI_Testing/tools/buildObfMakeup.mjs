// Builds _Shared/data/obfMakeup.json: the make-up products of the Open Beauty Facts daily data dump that carry an ingredient list,
// so a batch of hundreds can be filled from one local file (no per-product call, no rate limit).
//
// Usage (the dump is data, never run; download it into its own folder first):
//   curl -L -A "GlowUp-admin-data/1.0" -o <dir>/obf.csv.gz https://static.openbeautyfacts.org/data/en.openbeautyfacts.org.products.csv.gz
//   node tools/buildObfMakeup.mjs <dir>/obf.csv.gz
// Open Beauty Facts data is under the Open Database Licence (ODbL): credit "Open Beauty Facts contributors" wherever it is shown.
// Kept: a make-up category (from _Shared/data/obfCategories.json), a barcode of 8 to 14 digits, a name, an ingredient text. Nothing else is stored.
import { readFileSync, writeFileSync } from "node:fs";
import { gunzipSync } from "node:zlib";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { makeupType } from "../_Shared/glowupRules.js";

const here = path.dirname(fileURLToPath(import.meta.url));
const source = process.argv[2];
if (!source) { console.error("Give the path of the downloaded obf.csv.gz (or the unpacked .csv)"); process.exit(1); }
const raw = readFileSync(source);
const text = (source.endsWith(".gz") ? gunzipSync(raw) : raw).toString("utf8");
const types = JSON.parse(readFileSync(path.join(here, "..", "_Shared", "data", "obfCategories.json"), "utf8")).types;

const lines = text.split("\n");
const header = lines[0].split("\t");
const at = Object.fromEntries(header.map((h, i) => [h, i]));
for (const need of ["code", "product_name", "brands_en", "brands_tags", "categories_tags", "ingredients_text"])
    if (!(need in at)) { console.error("The dump has no column " + need); process.exit(1); }

const clean = (v, most) => String(v || "").replace(/[\x00-\x1f]+/g, " ").replace(/\s+/g, " ").trim().slice(0, most);
const items = [];
for (let i = 1; i < lines.length; i++) {
    const row = lines[i].split("\t");
    if (row.length < header.length - 5) continue;
    const code = clean(row[at.code], 20);
    if (!/^\d{8,14}$/.test(code)) continue;
    const name = clean(row[at.product_name], 120);
    const ingredients = clean(row[at.ingredients_text], 3000);
    if (!name || ingredients.length < 8) continue;
    const type = String(row[at.categories_tags] || "").split(",").map((c) => types[c.trim()]).find(Boolean)
        || makeupType("", name);                       // most products sit in a general category: the site's own make-up classifier reads the name
    if (!type) continue;
    const brand = clean(String(row[at.brands_en] || "").split(",")[0] || String(row[at.brands_tags] || "").split(",")[0].replace(/^[a-z]{2}:/, "").replace(/-/g, " "), 60);
    items.push([code, name, brand, type, ingredients]);
}
items.sort((a, b) => a[0].localeCompare(b[0]));
const out = path.join(here, "..", "_Shared", "data", "obfMakeup.json");
writeFileSync(out, JSON.stringify({ source: "Open Beauty Facts data dump (ODbL)", builtAt: new Date().toISOString().slice(0, 10), columns: ["code", "name", "brand", "type", "ingredients"], items }));
console.log(`Wrote ${items.length} make-up products with ingredient lists to ${out}`);
