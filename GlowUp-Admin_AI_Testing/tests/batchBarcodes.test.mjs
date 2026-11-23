// Batch barcodes (_Shared/batchBarcodes.js): the list is read exactly, only a real list of barcodes is taken, look-ups run one
// after another, and a code nobody knows (or one with no brand) is reported, never invented.
import { parseBarcodes, recordFromCandidate, lookupBatch, batchSummary, MAX_BARCODES } from "../_Shared/batchBarcodes.js";

let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };

// ---- reading the list ----
let p = parseBarcodes("0041554239416\n3600531652294, 0012345678905;4901234567894");
check(p.kind === "barcodes" && p.codes.length === 4, "newline, comma and semicolon all separate codes");
check(p.codes[0] === "0041554239416", "leading zeros are kept exactly as written");
p = parseBarcodes("Barcode\n041554239416\n041554239416\n0041554239416");
check(p.codes.length === 1 && p.duplicates === 2, "a header word is ignored and the same code (with or without a leading zero) counts once");
p = parseBarcodes("0 41554 23941 6\n4901234-567894");
check(p.codes.length === 2 && p.codes[0] === "041554239416" && p.codes[1] === "4901234567894", "spaces and dashes inside one code are ignored");
p = parseBarcodes("Maybelline Great Lash 12.99\nLipstick 450\n041554239416");
check(p.kind === "none", "a file of product words with one number is NOT a barcode list");
p = parseBarcodes("12345\n041554239416\n3600531652294\n4901234567894\n0012345678905\n8001234567890");
check(p.kind === "barcodes" && p.invalid.length === 1 && p.codes.length === 5, "a short number is reported as not a barcode");
check(parseBarcodes("").kind === "none" && parseBarcodes("   \n  ").kind === "none", "an empty text is not a list");
const many = Array.from({ length: MAX_BARCODES + 7 }, (_, i) => String(1000000000000 + i * 7919)).join("\n");
p = parseBarcodes(many);
check(p.codes.length === MAX_BARCODES && p.tooMany === 7, "at most " + MAX_BARCODES + " codes per run, the rest is counted");

// ---- the record the import pipeline saves ----
const r = recordFromCandidate({ brand: "Maybelline", name: "Great Lash", cosmeticType: "Mascara", ingredients: ["Aqua"], sources: ["https://x.test"], originLabel: "Open Beauty Facts" }, "0041554239416");
check(r.brandName === "Maybelline" && r.productName === "Great Lash" && r.barcode === "0041554239416" && r.lookupFrom === "Open Beauty Facts", "a candidate becomes an import record that keeps the barcode digits and says where it came from");

// ---- look-ups ----
const answers = {
    "1": { candidates: [{ name: "Great Lash", brand: "Maybelline", cosmeticType: "Mascara", originLabel: "UPCitemdb" }] },
    "2": { candidates: [] , note: "UPCitemdb: no match" },
    "3": { candidates: [{ name: "Mystery Tint", brand: "" }] },
    "4": null
};
const seen = [];
const out = await lookupBatch(["1", "2", "3", "4", "5"], async (code) => { seen.push(code); if (code === "5") throw new Error("offline"); return answers[code]; }, { gapMs: 0 });
check(seen.join(",") === "1,2,3,4,5", "every code is looked up, in order");
check(out.records.length === 1 && out.records[0].productName === "Great Lash", "only the code with a name AND a brand becomes a record");
check(out.notFound.length === 4, "the other four are listed with a reason (" + out.notFound.map((n) => n.code).join(",") + ")");
check(out.notFound.find((n) => n.code === "2").why.includes("UPCitemdb"), "the source's own note is kept as the reason");
check(/no brand/.test(out.notFound.find((n) => n.code === "3").why), "a product with no brand says so");
check(/connection/.test(out.notFound.find((n) => n.code === "5").why), "a failed look-up says it was the connection");
const stop = new AbortController();
let calls = 0;
await lookupBatch(["1", "2", "3"], async () => { calls++; stop.abort(); return { candidates: [] }; }, { gapMs: 0, signal: stop.signal });
check(calls === 1, "cancelling stops the run after the look-up in progress");

// ---- the line the admin reads ----
const line = batchSummary(parseBarcodes("041554239416\n041554239416\nabc12345\n3600531652294"), { records: [1], notFound: [1] });
check(/2 barcodes looked up, 1 found, 1 not found or incomplete, 1 repeated, 1 not a barcode/.test(line), "the summary says what happened in one line: " + line);

console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
