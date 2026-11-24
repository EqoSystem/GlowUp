// Builds _Shared/data/ingredientRefs.json: a reference table of cosmetic ingredients that the website uses to flag ingredients automatically
// (fragrance / EU allergen, drying alcohol, comedogenic rating, fungal-acne substrate) and to fill an empty "Purpose".
//
// Sources (data, never run; download each into its own folder first):
//   curl -L -o <dir>/cureskin.csv https://raw.githubusercontent.com/cureskin/indian-skincare-ingredient-flags/main/ingredient-flags.csv   (CC BY 4.0, CureSkin)
//   curl -L -o <dir>/beautee.csv  https://raw.githubusercontent.com/beauteeru/cosmetic-ingredients-dataset/main/ingredients.csv           (MIT, BEAUTEE: PubChem ids)
//   node tools/buildIngredientRefs.mjs <dir>/cureskin.csv <dir>/beautee.csv
// Blank cells in the CureSkin data mean "not assessed", never "safe": they stay blank (null) here and produce no flag.
import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { parseCsv, recordsOf } from "./csvTools.mjs";

const [cureFile, beauteeFile] = process.argv.slice(2);
if (!cureFile) { console.error("Give the CureSkin csv, then (optional) the BEAUTEE csv"); process.exit(1); }
const key = (name) => String(name || "").toLowerCase().replace(/[^a-z0-9]+/g, " ").trim();
const num = (v) => { const t = String(v === undefined ? "" : v).trim(); return t === "" || !/^\d+$/.test(t) ? null : Number(t); };

const pubchem = new Map();
if (beauteeFile) recordsOf(parseCsv(readFileSync(beauteeFile, "utf8"))).records.forEach((r) => { const cid = num(r.pubchem_cid); if (cid) pubchem.set(key(r.name), cid); });

// item: [function, fungalAcneTrigger, comedogenic, fragrance, euAllergen, alcohol ("d" drying | "f" fatty | ""), pregnancyCaution, pubchemCid]
const items = {};
recordsOf(parseCsv(readFileSync(cureFile, "utf8"))).records.forEach((r) => {
    const k = key(r.inci_name);
    if (!k) return;
    const alcohol = String(r.alcohol_type || "").trim().toLowerCase();
    items[k] = [String(r.function || "").trim().slice(0, 80), num(r.fa_trigger), num(r.comedogenic), num(r.fragrance) === 1 ? 1 : 0, num(r.eu_allergen) === 1 ? 1 : 0,
        alcohol === "drying" ? "d" : alcohol === "fatty" ? "f" : "", num(r.pregnancy_caution) === 1 ? 1 : 0, pubchem.get(k) || null];
});
const out = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "_Shared", "data", "ingredientRefs.json");
writeFileSync(out, JSON.stringify({
    credits: [
        "Indian Skincare Ingredient Flags, CureSkin Indian Skincare Decoder (2026). CC BY 4.0. https://cureskin.com/indianskincaredecoder/dataset",
        "Cosmetic Ingredients Dataset (Lite), BEAUTEE. MIT licence. https://github.com/beauteeru/cosmetic-ingredients-dataset"
    ],
    builtAt: new Date().toISOString().slice(0, 10),
    columns: ["function", "fungalAcne", "comedogenic", "fragrance", "euAllergen", "alcohol", "pregnancyCaution", "pubchemCid"],
    items
}));
console.log(`Wrote ${Object.keys(items).length} ingredients (${Object.values(items).filter((i) => i[7]).length} with a PubChem id) to ${out}`);
