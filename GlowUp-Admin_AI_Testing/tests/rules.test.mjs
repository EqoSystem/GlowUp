// Checks the website's copy of the shared rules (_Shared/glowupRules.js) against the same lists of examples the
// mobile app is tested against, so the website and the app always decide the same way.
//
//   Run it from the website folder:   node tests/rules.test.mjs
//   It prints what was checked. The last line says "ALL PASSED" or how many checks failed (exit code 1).
//
// The examples live with the app: GlowUp/Assets/_Project/Editor/Tests/MakeupScopeCases.txt and SharedRuleCases.txt.
// Nothing here talks to Firebase and nothing is uploaded: the "tests" folder is listed in .vercelignore.
import { readFileSync, existsSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";

const here = path.dirname(fileURLToPath(import.meta.url));
const rulesFile = path.join(here, "..", "_Shared", "glowupRules.js");
const casesFolder = path.join(here, "..", "..", "GlowUp", "Assets", "_Project", "Editor", "Tests");

// the rules file is a browser module; loading its text this way works with every version of Node
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

// in the lists a special character is written as a backslash, the letter u and its code (four digits)
const decode = (text) => text.replace(/\\u([0-9a-fA-F]{4})/g, (whole, code) => String.fromCharCode(parseInt(code, 16)));
const yesNo = (value) => (value ? "yes" : "no");

// ---------------------------------------------------------------------------------------------
// 1. The make-up rule: every example in MakeupScopeCases.txt
// ---------------------------------------------------------------------------------------------
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

// ---------------------------------------------------------------------------------------------
// 2. The name key, barcodes and ids: every example in SharedRuleCases.txt
// ---------------------------------------------------------------------------------------------
const sharedLines = readCases("SharedRuleCases.txt");
for (const line of sharedLines) {
    const parts = line.split("|").map((part) => decode(part.trim()));
    const rule = parts[0];
    const needed = ["same", "squashsame", "brands", "scanid"].includes(rule) ? 4 : 3;
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
        default: failures.push("The list names a rule this test does not know: " + rule); continue;
    }
    equal(got, expected, line);
}
check(sharedLines.length > 60, "SharedRuleCases.txt looks cut short (" + sharedLines.length + " examples).");

// ---------------------------------------------------------------------------------------------
// 3. The same extra checks the app's MakeupScopeTests.cs makes
// ---------------------------------------------------------------------------------------------
for (const name of ["Hydrating Face Cream", "Vitamin C Serum", "Gentle Cleanser", "Anti-Dandruff Shampoo", "Body Lotion", "Nail Polish", "Eau De Parfum", "Sunscreen SPF 50"])
    check(rules.isNotMakeup("", name), name + " must not count as make-up");

equal(rules.productScope("", ""), "unclear", "a product without a name and a kind");
equal(rules.productScope(null, undefined), "unclear", "a product with nothing at all");
check(!rules.isMakeup("", "Absidy Beauty Base"), "a name without any make-up word is not make-up");

// kinds the project has not decided on yet stay unclear (change this together with the app when they are decided)
for (const name of ["Tinted Moisturizer", "BB Cream", "CC Cream", "Lip Balm", "Lip Oil", "Skin Tint", "Tinted Sunscreen"])
    equal(rules.productScope("", name), "unclear", name);

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

// ---------------------------------------------------------------------------------------------
console.log(`make-up examples: ${scopeLines.length}, shared-rule examples: ${sharedLines.length}, checks made: ${checks}`);
if (failures.length > 0) {
    console.log("\nFAILED: " + failures.length + " check(s)\n");
    for (const failure of failures) console.log("  - " + failure);
    process.exit(1);
}
console.log("ALL PASSED");
