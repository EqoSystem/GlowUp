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

// every box of the form has a guide; every source it names exists
const FIELDS = ["purpose", "description", "chemicalDescription", "aliases", "sources", "cautionNote", "commonUses", "frequentAllergy", "judgment"];
// the INCI name, the classification and the allergen family are typed or chosen: no Sources row for them
["name", "classification", "allergens"].forEach((field) => check(!f.FIELD_GUIDE[field] && f.linksFor(field, "Aqua").length === 0, `no sources for ${field}`));
FIELDS.forEach((field) => {
    check(!!f.FIELD_GUIDE[field], `guide for ${field}`);
    f.FIELD_GUIDE[field].sources.forEach(([id, look]) => check(!!f.SOURCES[id] && look.length > 10, `${field}: source ${id} and its instruction`));
});

// LINK POLICY: every address that can be made is https, on an allowed site, and never a search engine
const NAMES = ["Glycerin", "Sodium Hyaluronate", "Aqua / Water", "1-Methylhydantoin-2-Imide", "Cafe & <b>x</b>", "a".repeat(80)];
NAMES.forEach((name) => {
    f.everyGeneratedUrl(name).forEach((url) => check(f.hostAllowed(url) && !/google|bing|duckduckgo|yahoo/i.test(new URL(url).hostname), `allowed host: ${url.slice(0, 80)}`));
    FIELDS.forEach((field) => f.linksFor(field, name).forEach((l) => check(f.hostAllowed(l.url), `${field}/${l.id} link on an allowed host`)));
    f.allSourcesFor(name).forEach((l) => check(f.hostAllowed(l.url), `reference list / ${l.id} link on an allowed host`));
});
check(!f.hostAllowed("https://www.google.com/search?q=cosing"), "google is refused");
check(!f.hostAllowed("https://www.bing.com/search?q=x") && !f.hostAllowed("https://duckduckgo.com/?q=x"), "bing and duckduckgo are refused");
check(!f.hostAllowed("http://cir-safety.org/"), "http is refused");
check(!f.hostAllowed("https://cir-safety.org.evil.com/"), "a look-alike host is refused");
check(!f.hostAllowed("https://cir-safety.org@evil.com/"), "a user name in front of the host is refused");
check(f.hostAllowed("https://www.cir-safety.org/ingredients") && f.hostAllowed("https://chem.echa.europa.eu/"), "sub-sites of an allowed host are allowed");
Object.values(f.SOURCES).forEach((s) => check(!s.landing.includes("google"), `${s.label}: no search engine in the landing address`));

// link kinds and what they allow
equal(f.linksFor("purpose", "A"), [], "no links for a one-letter name");
equal(f.linksFor("purpose", "Aqua").map((l) => l.id), ["cosing", "cosmile"], "purpose sources");
equal(f.linksFor("judgment", "Aqua"), [], "judgment boxes have no source");
check(f.FIELD_GUIDE.judgment.helper === "Admin judgment. Add your evidence in Reference links.", "judgment helper text");
const byId = (field, id) => f.linksFor(field, "Aqua").find((l) => l.id === id);
check(byId("aliases", "incipedia").kind === "landing" && byId("aliases", "incipedia").copyName === true, "INCIpedia: landing page, the name is copied");
check(byId("purpose", "cosing").kind === "landing" && byId("purpose", "cosing").copyName === true, "CosIng: landing page, the name is copied");
check(byId("purpose", "cosmile").kind === "search" && byId("purpose", "cosmile").copyName === false, "COSMILE: the site's own search, no copy needed");
check(byId("purpose", "cosmile").url === "https://cosmileeurope.eu/inci/results/?q=Aqua", "COSMILE search address");
check(byId("description", "cosmeticsinfo").url === "https://www.cosmeticsinfo.org/?s=Aqua", "CosmeticsInfo search address");
check(f.SOURCES.checker.search("Aqua") === "https://cosingchecker.com/search?q=Aqua", "CosIng Checker search address");
check(byId("frequentAllergy", "fragrance").kind === "landing" && byId("frequentAllergy", "fragrance").copyName === false, "the fragrance list is a page to read: no name copied");
check(f.linksFor("purpose", "Aqua / Water").find((l) => l.id === "cosmile").url.endsWith("Aqua%20%2F%20Water"), "the name is URL-encoded");
// "+ ref" is only for the page of THIS ingredient (PubChem), never a landing or a search page
Object.keys(f.SOURCES).forEach((id) => check(f.linkOf(id, "Aqua").canRef === (id === "pubchem"), `+ ref only for PubChem's ingredient page (${id})`));
// the Reference links source list: each source once, with the boxes it serves
const all = f.allSourcesFor("Aqua");
equal(new Set(all.map((l) => l.id)).size, all.length, "each source once in the reference list");
check(all.find((l) => l.id === "cosing").usedFor.includes("Purpose") && all.find((l) => l.id === "cosing").usedFor.includes("What the chemical is"), "the list says what each source is used for");
equal(f.allSourcesFor("A"), [], "no list for a one-letter name");

// PubChem: the page of this ingredient
equal(f.pubchemLookupUrl("Sodium  Hyaluronate"), "https://pubchem.ncbi.nlm.nih.gov/rest/pug/compound/name/Sodium%20Hyaluronate/cids/TXT", "PubChem lookup address");
equal(f.firstCompoundId("753\n1234\n"), 753, "first compound number");
equal(f.firstCompoundId(""), 0, "empty answer = none");
equal(f.firstCompoundId("<html>Not found</html>"), 0, "an error page is not a number");
equal(f.firstCompoundId("0"), 0, "0 is not a compound");
equal(f.pubchemPageUrl(753), "https://pubchem.ncbi.nlm.nih.gov/compound/753", "PubChem page address");
check(f.hostAllowed(f.pubchemPageUrl(753)) && f.hostAllowed(f.pubchemLookupUrl("x")), "PubChem addresses are allowed");

// bare addresses in Reference links get their label when the box is left
equal(f.labelBareUrls("https://www.cir-safety.org/ingredients"), "Cosmetic Ingredient Review | https://www.cir-safety.org/ingredients", "CIR label");
equal(f.labelBareUrls("https://pubchem.ncbi.nlm.nih.gov/compound/753"), "PubChem (NIH) | https://pubchem.ncbi.nlm.nih.gov/compound/753", "PubChem label");
equal(f.labelBareUrls("https://cosmileeurope.eu/inci/results/?q=Aqua"), "COSMILE Europe | https://cosmileeurope.eu/inci/results/?q=Aqua", "COSMILE label");
equal(f.labelBareUrls("https://ec.europa.eu/growth/tools-databases/cosing/"), "EU CosIng | https://ec.europa.eu/growth/tools-databases/cosing/", "CosIng label");
equal(f.labelBareUrls("https://single-market-economy.ec.europa.eu/x"), "European Commission | https://single-market-economy.ec.europa.eu/x", "the longest host wins");
equal(f.labelBareUrls("My note | https://www.cir-safety.org/x"), "My note | https://www.cir-safety.org/x", "a line with a label is left alone");
equal(f.labelBareUrls("https://unknown-site.example/x"), "https://unknown-site.example/x", "an unknown site is left alone");
equal(f.labelBareUrls("http://www.cir-safety.org/x"), "http://www.cir-safety.org/x", "http is not touched (it is refused on save)");
equal(f.labelBareUrls("a\nhttps://cosingchecker.com/search?q=x\n\nb"), "a\nCosIng Checker (unofficial) | https://cosingchecker.com/search?q=x\n\nb", "other lines and blank lines stay");
equal(f.labelBareUrls("https://cir-safety.org@evil.com/"), "https://cir-safety.org@evil.com/", "a user name before the host is left alone");


equal(f.linksFor("frequentAllergy", "Aqua").map((l) => l.id), ["echa", "pubchem", "fragrance"], "allergy sources");
equal(f.pasteResult("allergy", "  a  b ").text, "a b", "allergy paste is tidied");
equal(f.pasteResult("allergy", "a".repeat(301)).note, f.NOTES.largePaste, "allergy large paste notice");

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
