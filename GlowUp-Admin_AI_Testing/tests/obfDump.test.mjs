// The local Open Beauty Facts make-up copy: found by barcode (zeros ignored) and by name, with a real ingredient list. No network.
import { readFileSync } from "node:fs";
import { findInObfDump, searchObfDump } from "../_Shared/smartAdd.js";
import { parseBarcodes } from "../_Shared/batchBarcodes.js";
let checks = 0, failed = 0;
const ok = (c, t) => { checks++; if (!c) { failed++; console.log("FAIL: " + t); } };
const dump = JSON.parse(readFileSync(new URL("../_Shared/data/obfMakeup.json", import.meta.url), "utf8"));
ok(dump.items.length > 500, "the copy holds hundreds of make-up products (" + dump.items.length + ")");
ok(dump.items.every((r) => r.length === 5 && /^\d{8,14}$/.test(r[0]) && r[4].length >= 8), "every row has a barcode, a name and an ingredient text");

const sample = dump.items.slice(0, 200).filter((r) => r[2]);
let hits = 0, withList = 0;
sample.forEach((r) => { const c = findInObfDump(r[0], dump); if (c && c.name === r[1]) hits++; if (c && c.ingredients.length > 0) withList++; });
ok(hits === sample.length, `a barcode finds its own row (${hits}/${sample.length})`);
ok(withList >= sample.length * 0.95, `the found product carries a split ingredient list (${withList}/${sample.length})`);

const ean = dump.items.find((r) => r[0].length === 13 && r[0][0] === "0");
if (ean) ok(findInObfDump(ean[0].slice(1), dump) !== null, "a 12-digit UPC finds its 13-digit EAN row");
ok(findInObfDump("9999999999994", dump) === null, "an unknown barcode finds nothing");

const m = dump.items.find((r) => /mascara/i.test(r[1]) && r[2]);
const byName = searchObfDump(`${m[2]} ${m[1]}`, dump, 3);
ok(byName.length > 0 && byName[0].barcode === m[0], "a typed brand and name finds the product");
ok(searchObfDump("completely unrelated words xyz", dump).length === 0, "an unrelated text finds nothing");

// a batch of 100 barcodes is answered here in well under a second
const codes = parseBarcodes(dump.items.slice(300, 400).map((r) => r[0]).join("\n")).codes;
const t = Date.now(); const found = codes.map((c) => findInObfDump(c, dump)).filter(Boolean);
ok(codes.length === 100 && found.length === 100 && Date.now() - t < 1500, `100 barcodes looked up locally (${found.length} found, ${Date.now() - t} ms)`);
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED of ${checks}`);
process.exit(failed === 0 ? 0 : 1);
