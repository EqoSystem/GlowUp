// Turns a filled-in copy of tools/templates/t1_restricted_template.csv into an ingredient import file for the Ingredient Curator (CSV import).
//
//   node tools/t1ToImport.mjs <filled-template.csv> [--out file.csv]
//
// The template is EMPTY on purpose. A person fills it from the regulation's own annex (for example EU Regulation 1223/2009, Annex II for
// prohibited and Annex III for restricted substances, or the ASEAN Cosmetic Directive annexes adopted by FDA Philippines) and writes the annex and
// entry number in the "reference" column. Nothing is copied from memory, and this script adds no ingredient.
//
// Rules (docs/DATA_SOURCE_POLICY.md):
//   status "prohibited"  -> RISK_LEVEL harmful   (needs a tier 1 page in "source_url")
//   status "restricted"  -> RISK_LEVEL caution   (needs a tier 1 or tier 2 page), CAUTION_NOTE says it is restricted and under what conditions
// A row whose source_url is missing, not https, or not a page of the right tier is NOT written; it goes to the rejects file with the reason.
import { readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { parseCsv, recordsOf, toCsv } from "./csvTools.mjs";
import { evidenceProblem } from "../_Shared/glowupRules.js";

export const TEMPLATE_HEADERS = ["inci_name", "aliases", "status", "regulation", "reference", "conditions", "source_url"];
export const IMPORT_KEYS = ["NAME", "LABEL_NAME", "OTHER_NAMES", "DESCRIPTION", "CAUTION_NOTE", "RISK_LEVEL", "SOURCES"];

/** { rows, rejects } from the template's records. */
export function t1ToImport(records) {
    const rows = [], rejects = [];
    records.forEach((r, i) => {
        const line = i + 2;                                    // the header is line 1
        const name = r.inci_name, status = (r.status || "").toLowerCase(), url = r.source_url;
        const reject = (why) => rejects.push({ LINE: line, INCI_NAME: name, REASON: why });
        if (!name) return reject("no inci_name");
        if (status !== "prohibited" && status !== "restricted") return reject('status must be "prohibited" or "restricted"');
        if (!r.regulation || !r.reference) return reject("regulation and reference (annex and entry number) are required");
        if (!/^https:\/\/\S+$/.test(url || "")) return reject("source_url must be an https address");
        const risk = status === "prohibited" ? "harmful" : "caution";
        const problem = evidenceProblem(risk, [`${r.regulation} | ${url}`]);
        if (problem) return reject(problem);
        rows.push({
            NAME: name, LABEL_NAME: name, OTHER_NAMES: (r.aliases || "").split(/[|;]/).map((x) => x.trim()).filter(Boolean).join(" | "),
            DESCRIPTION: "",
            CAUTION_NOTE: status === "prohibited" ? `is prohibited in cosmetics (${r.regulation}, ${r.reference})`
                : `is restricted in cosmetics (${r.regulation}, ${r.reference})${r.conditions ? ": " + r.conditions : ""}`,
            RISK_LEVEL: risk,
            SOURCES: `${r.regulation}, ${r.reference} | ${url}`
        });
    });
    return { rows, rejects };
}

function main(argv) {
    const args = argv.slice(2);
    const file = args.find((a) => !a.startsWith("--"));
    if (!file) { console.log("Usage: node tools/t1ToImport.mjs <filled-template.csv> [--out file.csv]"); process.exit(2); }
    const outIndex = args.indexOf("--out");
    const out = path.resolve(outIndex >= 0 ? args[outIndex + 1] : "t1-import.csv");
    const { headers, records } = recordsOf(parseCsv(readFileSync(file, "utf8")));
    const missing = TEMPLATE_HEADERS.filter((h) => !headers.includes(h));
    if (missing.length) { console.log("Missing column(s): " + missing.join(", ") + ". Start from tools/templates/t1_restricted_template.csv."); process.exit(1); }
    const { rows, rejects } = t1ToImport(records);
    writeFileSync(out, toCsv(IMPORT_KEYS, rows));
    if (rejects.length) writeFileSync(out.replace(/\.csv$/i, "") + "-rejects.csv", toCsv(["LINE", "INCI_NAME", "REASON"], rejects));
    console.log(`${rows.length} row(s) written to ${out}; ${rejects.length} rejected${rejects.length ? " (see the -rejects file)" : ""}.`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) main(process.argv);
