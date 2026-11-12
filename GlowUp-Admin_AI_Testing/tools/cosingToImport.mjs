



















import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { parseCsv, recordsOf, toCsv } from "./csvTools.mjs";

export const IMPORT_KEYS = ["NAME", "LABEL_NAME", "OTHER_NAMES", "DESCRIPTION", "TECHNICAL_DESCRIPTION", "COMMON_USES", "SOURCES"];
export const REVIEW_KEYS = ["INCI_NAME", "COSING_REF", "CAS_NO", "RESTRICTION_TEXT"];
export const DEFAULT_SOURCE_URL = "https://single-market-economy.ec.europa.eu/sectors/cosmetics/cosmetic-ingredient-database_en";

const pick = (record, names) => { for (const n of names) if (record[n]) return record[n]; return ""; };
const nameKey = (text) => String(text || "").toLowerCase().replace(/[^a-z0-9]+/g, " ").trim();
const unique = (list) => list.filter((x, i) => x && list.findIndex((y) => nameKey(y) === nameKey(x)) === i);


export function cosingToImport(records, { only = null, max = Infinity, sourceUrl = DEFAULT_SOURCE_URL } = {}) {
    const out = { import: [], review: [], skipped: 0 };
    const seen = new Set();
    for (const r of records) {
        const inci = pick(r, ["inci name", "inci_name", "inci"]);
        if (!inci) { out.skipped++; continue; }
        const key = nameKey(inci);
        if (seen.has(key) || (only && !only.has(key))) { out.skipped++; continue; }
        if (out.import.length >= max) break;
        seen.add(key);
        const ref = pick(r, ["cosing ref no", "cosing ref. no", "cosing ref", "ref no", "ref_no"]);
        const cas = pick(r, ["cas no", "cas no.", "cas number", "cas"]);
        const functions = pick(r, ["function", "functions"]).split(/[,;]/).map((x) => x.trim().toLowerCase()).filter(Boolean);
        const others = unique([pick(r, ["inn name", "inn"]), pick(r, ["ph. eur. name", "ph eur name", "ph. eur name"])]).filter((x) => nameKey(x) !== key);
        const chemical = pick(r, ["chem/iupac name / description", "chem/iupac name", "iupac name", "description"]);
        out.import.push({
            NAME: inci, LABEL_NAME: inci, OTHER_NAMES: others.join(" | "),
            DESCRIPTION: functions.length ? "Cosmetic function (EU CosIng): " + functions.join(", ") + "." : "",
            TECHNICAL_DESCRIPTION: [chemical, cas ? "CAS " + cas : ""].filter(Boolean).join("; ").slice(0, 400),
            COMMON_USES: "",
            SOURCES: `EU CosIng${ref ? ", ref no " + ref : ""} | ${sourceUrl}`
        });
        const restriction = pick(r, ["restriction", "restrictions"]);
        if (restriction) out.review.push({ INCI_NAME: inci, COSING_REF: ref, CAS_NO: cas, RESTRICTION_TEXT: restriction });
    }
    return out;
}

function main(argv) {
    const args = argv.slice(2);
    const file = args.find((a) => !a.startsWith("--"));
    const value = (name) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : ""; };
    if (!file) { console.log("Usage: node tools/cosingToImport.mjs <cosing.csv> --only names.txt | --max 300 [--out folder] [--chunk 200] [--source-url https://...]"); process.exit(2); }
    if (!value("--only") && !value("--max")) { console.log("Give --only <names.txt> (recommended) or --max <n>. The whole inventory must not be imported at once."); process.exit(2); }
    const { headers, records } = recordsOf(parseCsv(readFileSync(file, "utf8")));
    if (!headers.some((h) => ["inci name", "inci_name", "inci"].includes(h))) { console.log('No "INCI name" column. The headers found are: ' + headers.join(" | ")); process.exit(1); }
    const only = value("--only") ? new Set(readFileSync(value("--only"), "utf8").split(/\r?\n/).map(nameKey).filter(Boolean)) : null;
    const result = cosingToImport(records, { only, max: Number(value("--max")) || Infinity, sourceUrl: value("--source-url") || DEFAULT_SOURCE_URL });
    const out = path.resolve(value("--out") || "cosing-import");
    mkdirSync(out, { recursive: true });
    const chunk = Math.max(1, Number(value("--chunk")) || 200);
    for (let i = 0, n = 1; i < result.import.length; i += chunk, n++) writeFileSync(path.join(out, `cosing-import-${String(n).padStart(3, "0")}.csv`), toCsv(IMPORT_KEYS, result.import.slice(i, i + chunk)));
    if (result.review.length) writeFileSync(path.join(out, "cosing-restricted-review.csv"), toCsv(REVIEW_KEYS, result.review));
    console.log(`${result.import.length} ingredient(s) written to ${out} (${result.skipped} row(s) skipped: no INCI name, repeated, or not in --only).`);
    console.log(result.review.length ? `${result.review.length} of them carry a CosIng restriction: see cosing-restricted-review.csv. Check each against the regulation's annex before any risk level is set.` : "None of them carries a CosIng restriction.");
    console.log("Import the files in the Ingredient Curator (drop a .csv). They arrive as Pending, with no risk level.");
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) main(process.argv);
