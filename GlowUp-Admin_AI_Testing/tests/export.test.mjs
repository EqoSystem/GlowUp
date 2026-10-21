// Checks of the CSV export (_Shared/adminExport.js): quoting, formula-injection guard, the row cap.
// No browser and no network.   Run: node tests/export.test.mjs
import { csvCell, toCsv, EXPORT_MAX_ROWS } from "../_Shared/adminExport.js";

let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };
const equal = (a, b, what) => check(JSON.stringify(a) === JSON.stringify(b), `${what} (got ${JSON.stringify(a)})`);

equal(csvCell("Mica"), "Mica", "a plain word stays as it is");
equal(csvCell("a, b"), '"a, b"', "a comma puts the text in quotes");
equal(csvCell('say "hi"'), '"say ""hi"""', "a quote is doubled");
equal(csvCell("one\ntwo"), '"one\ntwo"', "a line break is kept inside quotes");
equal(csvCell(["Water", "Mica"]), "Water; Mica", "a list is joined with semicolons");
equal(csvCell(null), "", "nothing is an empty cell");
equal(csvCell(0), "0", "zero is kept");
equal(csvCell("=HYPERLINK(\"http://x\")"), "\"'=HYPERLINK(\"\"http://x\"\")\"", "a formula is turned into plain text");
equal(csvCell("+1 shade"), "'+1 shade", "a leading plus is guarded");
equal(csvCell("-5"), "'-5", "a leading minus is guarded");
equal(csvCell("@here"), "'@here", "a leading at-sign is guarded");

const columns = [{ title: "Name", value: (r) => r.name }, { title: "Shades", value: (r) => r.shades }];
const rows = Array.from({ length: 250 }, (_, i) => ({ name: "P" + i, shades: ["#C24B62", "#000000"] }));
const csv = toCsv(columns, rows);
equal(csv.split("\r\n").length, EXPORT_MAX_ROWS + 1, "the export holds the header and at most the cap of rows");
equal(csv.split("\r\n")[0], "Name,Shades", "the header comes first");
equal(csv.split("\r\n")[1], "P0,#C24B62; #000000", "a row follows its columns");
equal(toCsv(columns, rows, 3).split("\r\n").length, 4, "a smaller limit can be given");
equal(toCsv(columns, [], 5), "Name,Shades", "no rows: only the header");

console.log("checks made: " + checks);
console.log(failed === 0 ? "ALL PASSED" : failed + " FAILED");
process.exit(failed === 0 ? 0 : 1);
