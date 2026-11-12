

import { parseCsv, recordsOf, toCsv } from "../tools/csvTools.mjs";
import { cosingToImport, IMPORT_KEYS as COSING_KEYS } from "../tools/cosingToImport.mjs";
import { t1ToImport, TEMPLATE_HEADERS } from "../tools/t1ToImport.mjs";
import { parseImportText } from "../_Shared/importFormat.js";
import { readFileSync } from "node:fs";

let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };
const equal = (a, b, what) => check(JSON.stringify(a) === JSON.stringify(b), `${what} (got ${JSON.stringify(a)})`);


equal(parseCsv('a,b\r\n1,"x, ""y"""\r\n\r\n2,\r\n'), [["a", "b"], ["1", 'x, "y"'], ["2", ""]], "quotes, a doubled quote, a blank line, a trailing empty cell");
equal(parseCsv("﻿a;b\n1;2\n"), [["a", "b"], ["1", "2"]], "a byte-order mark and ; as the separator");
equal(parseCsv('a\n"line one\nline two"\n'), [["a"], ["line one\nline two"]], "a line break inside quotes");
equal(recordsOf(parseCsv(" Name ,Other\nx,y\n")).records, [{ name: "x", other: "y" }], "headers are trimmed and lower-cased");
equal(toCsv(["a", "b"], [{ a: "x,y", b: 'say "hi"' }, { a: "1" }]), 'a,b\r\n"x,y","say ""hi"""\r\n1,\r\n', "writing quotes what needs it");
equal(parseCsv(toCsv(["a"], [{ a: "p\nq" }, { a: '"' }])), [["a"], ["p\nq"], ['"']], "what is written can be read back");


const COSING = `COSING Ref No,INCI name,INN name,Ph. Eur. Name,CAS No,EC No,Chem/IUPAC Name / Description,Restriction,Function,Update Date
900001,Example Substance One,Exampleine,,111-11-1,200-000-1,An invented chemical name,,"EMOLLIENT, SKIN CONDITIONING",2020-01-01
900002,Example Substance Two,,Example substance two,222-22-2,200-000-2,,III/99,PRESERVATIVE,2020-01-01
900003,example substance one,,,,,,,,
900004,,,,,,,,,
900005,Example Substance Three,,,,,,,SOLVENT,2020-01-01`;
{
    const { headers, records } = recordsOf(parseCsv(COSING));
    check(headers.includes("inci name"), "the CosIng header 'INCI name' is found");
    const all = cosingToImport(records);
    equal(all.import.map((r) => r.NAME), ["Example Substance One", "Example Substance Two", "Example Substance Three"], "every named row once; a repeat (other capitals) and a row without a name are skipped");
    equal(all.skipped, 2, "two rows skipped");
    const one = all.import[0];
    equal(one.OTHER_NAMES, "Exampleine", "the INN name becomes another name");
    equal(one.DESCRIPTION, "Cosmetic function (EU CosIng): emollient, skin conditioning.", "the function is stated as CosIng's, not as a plain description");
    equal(one.TECHNICAL_DESCRIPTION, "An invented chemical name; CAS 111-11-1", "chemical name and CAS");
    check(one.SOURCES.startsWith("EU CosIng, ref no 900001 | https://"), "the source line names the CosIng reference number: " + one.SOURCES);
    equal(all.import[1].OTHER_NAMES, "", "a Ph. Eur. name that only differs in capitals from the INCI name is not repeated as another name");
    equal(Object.keys(one), COSING_KEYS, "the columns of the import file");
    check(!("RISK_LEVEL" in one) && !("CAUTION_NOTE" in one), "a risk level or caution note is never written");
    equal(all.review.map((r) => r.INCI_NAME + "|" + r.RESTRICTION_TEXT), ["Example Substance Two|III/99"], "a restriction goes to the review file, not into a rating");
    equal(cosingToImport(records, { only: new Set(["example substance three"]) }).import.map((r) => r.NAME), ["Example Substance Three"], "--only keeps the listed names");
    equal(cosingToImport(records, { max: 1 }).import.length, 1, "--max");

    const csv = toCsv(COSING_KEYS, all.import);
    const parsed = parseImportText(csv);
    equal(parsed.kind + "|" + parsed.draft.ingredients.length, "ingredients|3", "the written CSV is read by the website's import as 3 ingredients");
    check(parsed.draft.ingredients.every((i) => i.riskLevel === ""), "none of them has a risk level after import");
}


equal(readFileSync(new URL("../tools/templates/t1_restricted_template.csv", import.meta.url), "utf8").trim().split(",").join(","), TEMPLATE_HEADERS.join(","), "the shipped template has the headers and no row");
const EU = "https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32009R1223", CIR = "https://cir-reports.cir-safety.org/example", DB = "https://www.incidecoder.com/example";
{
    const head = "inci_name,aliases,status,regulation,reference,conditions,source_url\n";
    const rows = [
        `Example Substance One,Alias A | Alias B,prohibited,Example Regulation Annex II,entry 9999,,${EU}`,
        `Example Substance Two,,restricted,Example Regulation Annex III,entry 8888,"max 1 % in rinse-off",${EU}`,
        `Example Substance Three,,restricted,Example Opinion,opinion 7777,,${CIR}`,
        `Example Substance Four,,prohibited,Example Opinion,opinion 7777,,${CIR}`,
        `Example Substance Five,,prohibited,Example Regulation Annex II,entry 6666,,${DB}`,
        `Example Substance Six,,prohibited,Example Regulation Annex II,entry 5555,,http://insecure.example/x`,
        `Example Substance Seven,,banned,Example Regulation Annex II,entry 4444,,${EU}`,
        `Example Substance Eight,,prohibited,,,,${EU}`,
        `,,prohibited,Example Regulation Annex II,entry 3333,,${EU}`
    ].join("\n");
    const { rows: out, rejects } = t1ToImport(recordsOf(parseCsv(head + rows)).records);
    equal(out.map((r) => r.NAME + "=" + r.RISK_LEVEL), ["Example Substance One=harmful", "Example Substance Two=caution", "Example Substance Three=caution"], "prohibited on a tier 1 page is harmful; restricted on a tier 1 or 2 page is caution");
    equal(out[0].OTHER_NAMES, "Alias A | Alias B", "aliases");
    equal(out[1].CAUTION_NOTE, "is restricted in cosmetics (Example Regulation Annex III, entry 8888): max 1 % in rinse-off", "the conditions are in the caution note");
    check(out[0].SOURCES.includes(EU), "the regulator page is the source");
    equal(rejects.map((r) => r.INCI_NAME || "(no name)"), ["Example Substance Four", "Example Substance Five", "Example Substance Six", "Example Substance Seven", "Example Substance Eight", "(no name)"], "harmful on a tier 2 page, on a database, an insecure address, an unknown status, no reference, no name: all rejected");
    check(rejects.every((r) => r.REASON && r.LINE >= 2), "every reject says why and where");
    const parsed = parseImportText(toCsv(["NAME", "LABEL_NAME", "OTHER_NAMES", "DESCRIPTION", "CAUTION_NOTE", "RISK_LEVEL", "SOURCES"], out));
    equal(parsed.kind + "|" + parsed.draft.ingredients.map((i) => i.riskLevel).join(","), "ingredients|harmful,caution,caution", "the written CSV is read by the website's import with the risk levels");
}

console.log("checks made: " + checks);
console.log(failed === 0 ? "ALL PASSED" : failed + " FAILED");
process.exit(failed === 0 ? 0 : 1);
