// Checks _Shared/fieldSources.js (pure logic; nothing is sent anywhere).
//   Run it from the website folder:   node tests/fieldSources.test.mjs
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";

const here = path.dirname(fileURLToPath(import.meta.url));
const f = await import("data:text/javascript;base64," + Buffer.from(readFileSync(path.join(here, "..", "_Shared", "fieldSources.js"))).toString("base64"));

let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };
const equal = (got, expected, what) => check(JSON.stringify(got) === JSON.stringify(expected), `${what} (got ${JSON.stringify(got)})`);

// every box of the form has a guide; every source it names exists; every link is https
["name", "classification", "purpose", "description", "chemicalDescription", "aliases", "sources", "cautionNote", "commonUses", "allergens", "judgment"].forEach((field) => {
    check(!!f.FIELD_GUIDE[field], `guide for ${field}`);
    f.FIELD_GUIDE[field].sources.forEach(([id, look]) => check(!!f.SOURCES[id] && look.length > 10, `${field}: source ${id} and its instruction`));
    f.linksFor(field, "Sodium Hyaluronate").forEach((l) => check(/^https:\/\//.test(l.url) && (l.ref === "" || /^https:\/\//.test(l.ref)), `${field}/${l.id}: https links`));
});
equal(f.linksFor("name", "A"), [], "no links for a one-letter name");
equal(f.linksFor("name", "Aqua").map((l) => l.id), ["cosing", "incipedia", "cosmile"], "INCI name sources, in order");
equal(f.linksFor("purpose", "Aqua").map((l) => l.id), ["cosing", "cosmile"], "purpose sources");
equal(f.linksFor("judgment", "Aqua"), [], "judgment boxes have no source");
check(f.FIELD_GUIDE.judgment.helper === "Admin judgment. Add your evidence in Reference links.", "judgment helper text");
check(f.linksFor("name", "Aqua").find((l) => l.id === "incipedia").deep === false, "INCIpedia has no deep link (the name is copied)");
check(f.linksFor("name", "Aqua").find((l) => l.id === "cosmile").deep === true, "COSMILE links straight to the search");
check(f.linksFor("name", "Aqua / Water").find((l) => l.id === "cosmile").url.endsWith("Aqua%20%2F%20Water"), "the name is URL-encoded");

// Reference links: dedupe, never overwrite, label | address
let r = f.addReference("", "PubChem (NIH)", "https://pubchem.ncbi.nlm.nih.gov/#query=Aqua");
equal(r, { text: "PubChem (NIH) | https://pubchem.ncbi.nlm.nih.gov/#query=Aqua", added: true, reason: "" }, "first reference");
r = f.addReference(r.text, "CIR safety report", "https://www.cir-safety.org/");
equal(r.text, "PubChem (NIH) | https://pubchem.ncbi.nlm.nih.gov/#query=Aqua\nCIR safety report | https://www.cir-safety.org/", "second one goes below, first stays");
equal(f.addReference(r.text, "Other label", "HTTPS://WWW.CIR-SAFETY.ORG").reason, "exists", "same address (case, no slash) is not added twice");
equal(f.addReference(r.text, "x", "https://www.cir-safety.org/").text, r.text, "text unchanged when it exists");
equal(f.addReference("My own note | https://a.com/x", "CIR", "https://b.com").text, "My own note | https://a.com/x\nCIR | https://b.com", "an existing line is kept untouched");
equal(f.addReference("", "CIR", "http://insecure.com").reason, "bad", "only https");
equal(f.addReference("", "CIR", "javascript:alert(1)").reason, "bad", "no script address");
equal(f.addReference("a | https://a.com", "Pipe | Label", "https://b.com").text, "a | https://a.com\nPipe Label | https://b.com", "a | in the label cannot break the line format");
equal(f.addReference("x".repeat(890), "CIR", "https://www.cir-safety.org/").reason, "full", "the box limit is respected");

// paste: purpose
equal(f.pasteResult("purpose", "  PRESERVATIVE\n").note, f.NOTES.functionTerm, "all-capitals purpose warns");
equal(f.pasteResult("purpose", "HUMECTANT, SKIN CONDITIONING").note, f.NOTES.functionTerm, "several function terms warn");
equal(f.pasteResult("purpose", "Helps keep skin moist.").note, "", "a plain sentence does not warn");
equal(f.pasteResult("purpose", "Keeps\n\n  skin   moist ").text, "Keeps skin moist", "line breaks and spaces collapse");
equal(f.insertPaste("purpose", "", 0, 0, "x".repeat(150), 120).value.length, 120, "purpose is held to 120 characters");
equal(f.insertPaste("purpose", "", 0, 0, "x".repeat(150), 120).note, "Cut to 120 characters.", "and says so");
// paste: description / chemical
equal(f.pasteResult("description", "a".repeat(301)).note, f.NOTES.largePaste, "over 300 characters: notice");
equal(f.pasteResult("description", "a".repeat(300)).note, "", "300 is fine");
equal(f.pasteResult("chemical", "a".repeat(400)).note, "", "no notice for the chemical box");
equal(f.insertPaste("description", "Hello world", 5, 5, " big\n   and  bold ", 600).value, "Hello big and bold world", "a pasted edge keeps one space, no gluing");
equal(f.insertPaste("description", "Hello", 5, 5, "world", 600).value, "Helloworld", "no space invented where the paste had none");
equal(f.insertPaste("description", "abc XYZ def", 4, 7, "q", 600).value, "abc q def", "the selection is replaced");
// paste: aliases
equal(f.normalizeNameList("Water; Eau,\nAqua , water,, ,EAU"), "Water, Eau, Aqua", "list: separators, trim, no repeat (case-insensitive), no empty");
equal(f.insertPaste("aliases", "Eau", 3, 3, "\nWater;Aqua;eau", 300).value, "Eau, Water, Aqua", "pasted list joins the existing one without repeats");
// paste: INCI name
equal(f.pasteResult("name", " Aqua / Water ").note, f.NOTES.inciList, "slash warns");
equal(f.pasteResult("name", "Aqua, Water").note, f.NOTES.inciList, "comma warns");
equal(f.pasteResult("name", "  Sodium   Hyaluronate\n").text, "Sodium Hyaluronate", "trim and collapse");
equal(f.pasteResult("name", "Aqua/Water").note, "", "a slash with no spaces inside a name (not the \" / \" list) does not warn");

console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
