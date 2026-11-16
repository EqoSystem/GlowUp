







import { readFileSync, existsSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";

const here = path.dirname(fileURLToPath(import.meta.url));
const rulesFile = path.join(here, "..", "_Shared", "glowupRules.js");
const casesFolder = path.join(here, "..", "..", "GlowUp", "Assets", "_Project", "Editor", "Tests");


const rules = await import("data:text/javascript;base64," + Buffer.from(readFileSync(rulesFile, "utf8"), "utf8").toString("base64"));

let checks = 0;
const failures = [];
function check(condition, message) {
    checks++;
    if (!condition) failures.push(message);
}
const equal = (got, expected, what) => check(got === expected, `${what}\n      expected "${expected}", the website said "${got}"`);

function readCases(fileName) {
    const file = path.join(casesFolder, fileName);
    if (!existsSync(file)) { failures.push("The list of examples is missing: " + file); return []; }
    return readFileSync(file, "utf8").split(/\r?\n/).map((line) => line.trim()).filter((line) => line.length > 0 && !line.startsWith("#"));
}


const decode = (text) => text.replace(/\\u([0-9a-fA-F]{4})/g, (whole, code) => String.fromCharCode(parseInt(code, 16)));
const yesNo = (value) => (value ? "yes" : "no");




const scopeLines = readCases("MakeupScopeCases.txt");
for (const line of scopeLines) {
    const parts = line.split("|").map((part) => part.trim());
    check(parts.length >= 3, "A line needs at least: kind | name | what the rule must say. Line: " + line);
    if (parts.length < 3) continue;
    const [type, name, expected] = parts, expectedType = parts.length > 3 ? parts[3] : "";
    if (type === "@label") {
        equal(rules.judgeLabel(name).kind, expected, `pack text "${name}"`);
        continue;
    }
    const kind = rules.productScope(type, name);
    equal(kind, expected, `"${type}" | "${name}"`);
    if (kind === expected && expected === "makeup" && expectedType.length > 0) equal(rules.makeupType(type, name), expectedType, `the kind of "${type}" | "${name}"`);
}
check(scopeLines.length > 80, "MakeupScopeCases.txt looks cut short (" + scopeLines.length + " examples).");




const sharedLines = readCases("SharedRuleCases.txt");
for (const line of sharedLines) {
    const parts = line.split("|").map((part) => decode(part.trim()));
    const rule = parts[0];
    const needed = ["same", "squashsame", "brands", "scanid", "sourcekind"].includes(rule) ? 4 : 3;
    check(parts.length === needed, "This line does not have the columns its rule needs: " + line);
    if (parts.length !== needed) continue;
    const expected = parts[needed - 1];
    let got;
    switch (rule) {
        case "key": got = rules.normalizeName(parts[1]); break;
        case "slug": got = rules.slugName(parts[1]); break;
        case "compact": got = rules.compactName(parts[1]); break;
        case "same": got = yesNo(rules.sameName(parts[1], parts[2])); break;
        case "squashsame": {
            const a = rules.squashKey(rules.normalizeName(parts[1])), b = rules.squashKey(rules.normalizeName(parts[2]));
            got = yesNo(a.length > 0 && a === b);
            break;
        }
        case "barcode": got = rules.barcodeKey(parts[1]); break;
        case "brands": got = yesNo(rules.brandsAgree(parts[1], parts[2])); break;
        case "scanid": got = rules.scanProductId(parts[1], parts[2]); break;
        case "brandid": got = rules.scanBrandId(parts[1]); break;
        case "stoplist": got = rules.stoplistHit(parts[1]) || ""; break;
        case "shape": got = rules.wrongIngredientShape(parts[1]) ? "wrong" : "ok"; break;
        case "sourcekind": got = rules.sourceKind(parts[1], parts[2]); break;
        case "sourcelabel": got = (rules.SOURCE_KINDS[parts[1]] || {}).label || ""; break;
        default: failures.push("The list names a rule this test does not know: " + rule); continue;
    }
    equal(got, expected, line);
}
check(sharedLines.length > 60, "SharedRuleCases.txt looks cut short (" + sharedLines.length + " examples).");




for (const name of ["Hydrating Face Cream", "Vitamin C Serum", "Gentle Cleanser", "Anti-Dandruff Shampoo", "Body Lotion", "Nail Polish", "Eau De Parfum", "Sunscreen SPF 50"])
    check(rules.isNotMakeup("", name), name + " must not count as make-up");

equal(rules.productScope("", ""), "unclear", "a product without a name and a kind");
equal(rules.productScope(null, undefined), "unclear", "a product with nothing at all");
check(!rules.isMakeup("", "Absidy Beauty Base"), "a name without any make-up word is not make-up");


for (const name of ["Tinted Moisturizer", "BB Cream", "CC Cream", "Skin Tint"]) {
    equal(rules.productScope("", name), "makeup", name + " is face make-up");
    equal(rules.makeupType("", name), "Foundation", name + " is filed with the foundations");
}
for (const name of ["Lip Balm", "Lip Oil", "Tinted Sunscreen"]) equal(rules.productScope("", name), "skincare", name + " is care, not make-up");

for (const name of ["Tinted Lip Balm", "Tinted Lip Oil"]) equal(rules.productScope("", name), "unclear", name);

check(rules.isMakeup("", "All Nighter Setting Spray"), "a setting spray is make-up");
equal(rules.makeupType("", "Makeup Fixing Spray"), "Setting Spray", "the kind of a fixing spray");
check(rules.isNotMakeup("Lipstick", "Lip + Eye Make Up Remover"), "a remover is not make-up even when its kind says lipstick");
equal(rules.makeupType("Lipstick", "Instant Eye And Lip Makeup Remover"), "", "a remover has no make-up kind");

for (const kind of rules.MAKEUP_TYPES) {
    check(rules.isMakeup(kind, ""), kind + " is make-up");
    equal(rules.makeupType(kind, ""), kind, "the kind " + kind + " keeps its name");
}
for (const kind of ["Skin Care", "Hair", "Body", "Nail", "Fragrance"]) check(rules.isNotMakeup(kind, "Some Product"), `the kind "${kind}" is not make-up`);
equal(rules.productScope("Unknown", ""), "unclear", 'the kind "Unknown"');

check(rules.judgeLabel("HEAD & SHOULDERS SHAMPOO").kind === "out", "a shampoo pack is not make-up");
check(rules.judgeLabel("with moisturizer and sunscreen").kind === "unclear", "claims on a make-up pack must not decide anything");
check(rules.judgeLabel("FOUNDATION with sunscreen, washes off with facial wash").kind === "makeup", "a make-up word on the pack wins");

equal(rules.notMakeupWords(rules.judgeProduct("", "Anti-Dandruff Shampoo")), "a hair product", "the words for a shampoo");
equal(rules.notMakeupWords(rules.judgeProduct("", "Vitamin C Serum")), "a skin care product", "the words for a serum");
equal(rules.notMakeupWords(rules.judgeProduct("", "Matte Lipstick")), "", "make-up needs no such words");




const vitaminE = ["Vitamin E", "Tocopherol", "Tocopheryl Acetate"];
for (const typed of ["VITAMIN E", "vitamin e", "Vitamin E", "  vitamin   e  ", "Vitamin-E", "e vitamin"])
    check(rules.matchesSearch(typed, vitaminE), `"${typed}" finds Vitamin E`);
check(rules.matchesSearch("tocoph", vitaminE), "the first letters of another spelling find it");
check(rules.matchesSearch("vi", vitaminE) && rules.matchesSearch("v", vitaminE), "the first one or two letters find it while typing");
check(rules.matchesSearch("vitamin c", ["Vitamin C", "Ascorbic Acid"]), "one letter finds a one-letter word");
check(rules.matchesSearch("ACETATE", vitaminE), "an alias finds it, in capitals too");
check(!rules.matchesSearch("vitamin c", vitaminE), "another vitamin does not");
check(rules.matchesSearch("loreal", ["L'Or" + String.fromCharCode(0xe9) + "al Paris"]), "typed without the accent and the apostrophe");
check(rules.matchesSearch("L'OREAL", ["Loreal Paris"]), "typed with an apostrophe the entry does not have");
check(rules.matchesSearch("ink", ["Superstay Matte Ink"]), "a word from the middle or the end");
check(rules.matchesSearch("matte superstay", ["Superstay Matte Ink"]), "several words in any order");
check(rules.matchesSearch("super stay", ["Superstay Matte Ink"]), "typed as two words, written as one");
check(rules.matchesSearch("superstay", ["Super Stay Matte Ink"]), "typed as one word, written as two");
check(!rules.matchesSearch("superstay gloss", ["Superstay Matte Ink"]), "every word typed must be found");
check(rules.matchesSearch("", ["Anything"]) && rules.matchesSearch("   ", ["Anything"]), "an empty search shows everything");
check(!rules.matchesSearch("lipstick", []) && !rules.matchesSearch("lipstick", [""]), "an entry without any text is never found");
check(rules.matchesSearch("maybelline lipstick", ["Superstay Matte Ink", "Maybelline", "Lipstick"]), "words may be found in different fields (name, brand, kind)");





equal(rules.evidenceTier("Annex II | https://eur-lex.europa.eu/eli/reg/2009/1223/oj", ""), 1, "an EU regulation page is tier 1");
equal(rules.evidenceTier("FDA PH | https://verification.fda.gov.ph/x", ""), 1, "FDA Philippines is tier 1");
equal(rules.evidenceTier("US FDA | https://www.fda.gov/cosmetics/cosmetic-ingredients", ""), 1, "US FDA is tier 1");
equal(rules.evidenceTier("PubChem | https://pubchem.ncbi.nlm.nih.gov/compound/1", ""), 3, "PubChem is a database, although sourceKind labels nih.gov a regulator: tier 3");
equal(rules.evidenceTier("PubMed | https://pubmed.ncbi.nlm.nih.gov/123/", ""), 2, "PubMed (a study) is tier 2");
equal(rules.evidenceTier("CosIng | https://ec.europa.eu/growth/tools-databases/cosing/details/12345", ""), 3, "EU CosIng is a database: tier 3, not regulation");
equal(rules.evidenceTier("CosIng | https://single-market-economy.ec.europa.eu/sectors/cosmetics/cosmetic-ingredient-database_en", ""), 3, "the new CosIng address is tier 3 too");
equal(rules.evidenceTier("WHO | https://www.who.int/x", ""), 2, "who.int is tier 2");
equal(rules.evidenceTier("CIR | https://cir-reports.cir-safety.org/x", ""), 2, "CIR is tier 2");
equal(rules.evidenceTier("DermNet | https://dermnetnz.org/topics/x", ""), 2, "DermNet is tier 2");
equal(rules.evidenceTier("INCIDecoder | https://incidecoder.com/ingredients/x", ""), 3, "an ingredient database is tier 3");
equal(rules.evidenceTier("Shop | https://www.watsons.com.ph/p/1", ""), 4, "a retailer is tier 4");
equal(rules.evidenceTier("OBF | https://world.openbeautyfacts.org/product/1", ""), 4, "a public list is tier 4");
equal(rules.evidenceTier("Brand | https://www.maybelline.com.ph/x", "https://www.maybelline.com.ph"), 4, "the maker's confirmed site is tier 4");
equal(rules.evidenceTier("Blog | https://some-blog.example/x", ""), 0, "an unknown site supports nothing");
equal(rules.evidenceTier("https://fda.gov@evil.com/x", ""), 0, "a look-alike address supports nothing");
equal(rules.evidenceTier("no address at all", ""), 0, "a line without an address supports nothing");

const EU = "Annex II | https://eur-lex.europa.eu/x", CIR = "CIR | https://cir-reports.cir-safety.org/x", DB = "INCIDecoder | https://incidecoder.com/x", SHOP = "Shop | https://www.watsons.com.ph/p/1";
check(rules.evidenceProblem("harmful", [EU]) === "", "harmful is backed by a tier 1 page");
check(rules.evidenceProblem("harmful", [DB, EU]) === "", "harmful is backed when one of several pages is tier 1");
check(rules.evidenceProblem("harmful", [CIR]) !== "", "harmful is NOT backed by a tier 2 page");
check(rules.evidenceProblem("harmful", [DB]) !== "", "harmful is NOT backed by an ingredient database");
check(rules.evidenceProblem("harmful", [SHOP]) !== "", "harmful is NOT backed by a retailer");
check(rules.evidenceProblem("harmful", []) !== "" && rules.evidenceProblem("harmful", undefined) !== "", "harmful with no source is not backed");
check(rules.evidenceProblem("caution", [EU]) === "" && rules.evidenceProblem("caution", [CIR]) === "", "caution is backed by tier 1 or tier 2");
check(rules.evidenceProblem("caution", [DB]) !== "", "caution is NOT backed by an ingredient database alone");
check(rules.evidenceProblem("caution", []) !== "", "caution with no source is not backed");
check(rules.evidenceProblem("safe", []) === "" && rules.evidenceProblem("", []) === "", "safe and an empty level need no source");
equal(rules.bestEvidenceTier([SHOP, DB, CIR]), 2, "the strongest page decides");
equal(rules.bestEvidenceTier([]), 0, "no pages: no tier");

console.log(`make-up examples: ${scopeLines.length}, shared-rule examples: ${sharedLines.length}, checks made: ${checks}`);
if (failures.length > 0) {
    console.log("\nFAILED: " + failures.length + " check(s)\n");
    for (const failure of failures) console.log("  - " + failure);
    process.exit(1);
}
console.log("ALL PASSED");
