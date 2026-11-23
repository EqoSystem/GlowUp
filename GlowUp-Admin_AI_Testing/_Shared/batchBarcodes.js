// Batch barcodes (23 Nov 2026): a list of barcodes (pasted, or a .txt / .csv / Excel file) becomes a list of product records,
// each looked up the way Quick fill does it (Open Beauty Facts, then UPCitemdb). The records go through the SAME import
// pipeline as a product file: the admin checks them in "Check before saving", they are saved as Pending, every value is
// Detected (nobody has checked it) and the usual duplicate / make-up / brand rules apply. Nothing here writes to the database.
import { barcodeKey } from "./glowupRules.js";
import { gtinValid } from "./smartAdd.js";

export const MAX_BARCODES = 100;                 // one run: keeps inside the free look-up limits (UPCitemdb allows 100 a day)
const HEADER = /^(barcodes?|ean\d*|upc|gtin\d*|code|codes|sku)$/i;

// A barcode here is 8 to 14 digits, zeros kept as written. Spaces and dashes inside one code are ignored ("0 41554 23941 6").
export function parseBarcodes(text) {
    const entries = String(text || "").split(/[\r\n,;\t|]+/)
        .map((e) => e.replace(/^["'`\s]+|["'`\s]+$/g, "").trim()).filter(Boolean).filter((e) => !HEADER.test(e));
    const codes = [], invalid = [], seen = new Set();
    let duplicates = 0;
    entries.forEach((entry) => {
        const digits = entry.replace(/[\s-]/g, "");
        if (!/^\d{8,14}$/.test(digits)) { invalid.push(entry.slice(0, 40)); return; }
        const key = barcodeKey(digits) || digits.replace(/^0+/, "");
        if (seen.has(key)) { duplicates++; return; }
        seen.add(key);
        codes.push(digits);
    });
    // a file of words and prices with a few numbers in it is NOT a barcode list: at least 80% of the entries must be barcodes
    const kind = codes.length + duplicates > 0 && (codes.length + duplicates) >= 0.8 * entries.length ? "barcodes" : "none";
    return { kind, codes: codes.slice(0, MAX_BARCODES), tooMany: Math.max(0, codes.length - MAX_BARCODES), invalid, duplicates, entries: entries.length };
}

// One look-up result as an import record (the shape saveImportedProducts reads).
export function recordFromCandidate(candidate, code) {
    const c = candidate || {};
    return {
        brandName: String(c.brand || "").trim(), productName: String(c.name || "").trim(), cosmeticType: String(c.cosmeticType || "").trim(),
        ingredients: Array.isArray(c.ingredients) ? c.ingredients : [], barcode: code,
        sources: Array.isArray(c.sources) ? c.sources : [], imageUrl: c.imageUrl || "", description: "", shades: [],
        lookupFrom: c.originLabel || c.origin || "Barcode look-up"
    };
}

// Looks every code up, one after another with a short pause (the free services are shared).
//   lookup(code) -> { candidates: [...], note }   (quickFill.lookupProduct)
// Returns { records, notFound: [{ code, why }] }. A code with no name or no brand is not saved (a product needs both), so it is listed.
export async function lookupBatch(codes, lookup, { onProgress = () => {}, signal = null, gapMs = 250, wait = (ms) => new Promise((r) => setTimeout(r, ms)) } = {}) {
    const records = [], notFound = [];
    for (let i = 0; i < codes.length; i++) {
        if (signal && signal.aborted) break;
        const code = codes[i];
        onProgress(i + 1, codes.length, code);
        let answer = null;
        try { answer = await lookup(code); } catch (error) { notFound.push({ code, why: "the look-up failed (connection)" }); continue; }
        const best = answer && Array.isArray(answer.candidates) ? answer.candidates.find((c) => c && c.name) : null;
        if (!best) { notFound.push({ code, why: answer && answer.note ? answer.note : "no source knows this barcode" }); }
        else if (!best.brand) notFound.push({ code, why: `found "${best.name}" but no brand (a product needs a brand)` });
        else records.push(recordFromCandidate(best, code));
        if (i < codes.length - 1 && gapMs > 0) await wait(gapMs);
    }
    return { records, notFound };
}

// What the admin is told before and after: one calm line.
export function batchSummary(parsed, result) {
    const bits = [`${parsed.codes.length} barcode${parsed.codes.length === 1 ? "" : "s"} looked up`, `${result.records.length} found`];
    if (result.notFound.length) bits.push(`${result.notFound.length} not found or incomplete`);
    if (parsed.duplicates) bits.push(`${parsed.duplicates} repeated`);
    if (parsed.invalid.length) bits.push(`${parsed.invalid.length} not a barcode`);
    if (parsed.tooMany) bits.push(`${parsed.tooMany} left for the next run (at most ${MAX_BARCODES} at a time)`);
    const wrongDigit = parsed.codes.filter((c) => !gtinValid(c)).length;
    if (wrongDigit) bits.push(`${wrongDigit} with a check digit that does not fit (possible typing mistakes)`);
    return bits.join(", ") + ".";
}
