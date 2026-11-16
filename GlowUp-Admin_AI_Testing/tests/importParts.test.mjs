// Checks of the unlimited import (9 November 2026): a long file is cut into parts for the AI with nothing lost, and a file
// that mixes brands/products with ingredient details is refused with the reason.   Run: node tests/importParts.test.mjs
import { splitForAi, AI_PART_CHARACTERS, AI_TEXT_CHARACTERS } from "../_Shared/adminDocumentReader.js";
import { parseImportText, ingredientsNotHere, inventoryNotHere } from "../_Shared/importFormat.js";

let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };

const line = (i) => `Brand: Glow ${i}; Product: Tint ${i}; Ingredients: Aqua, Mica, Dimethicone\n`;
const long = Array.from({ length: 5000 }, (_, i) => line(i)).join("");
const parts = splitForAi(long);
check(parts.length > 1, "a long file is cut into several parts");
check(parts.join("") === long, "every character is in exactly one part, in order");
check(parts.every((p) => p.length <= AI_PART_CHARACTERS), "no part is longer than a part may be");
check(AI_PART_CHARACTERS <= AI_TEXT_CHARACTERS, "a part fits in what the server takes");
check(parts.slice(0, -1).every((p) => p.endsWith("\n")), "parts end at a line break, so a record is not cut in two");
check(splitForAi("short text").length === 1, "a short file is one part");
check(splitForAi("").length === 0 && splitForAi("   \n  ").length === 0, "an empty file has no parts");
const noBreaks = "x".repeat(AI_PART_CHARACTERS * 2 + 5);
check(splitForAi(noBreaks).join("") === noBreaks, "a text without line breaks is still kept whole");

const MIXED = "=== BRAND ===\nNAME: Glow\n=== PRODUCT ===\nBRAND: Glow\nNAME: Tint\n=== INGREDIENT ===\nNAME: Aqua\n=== INGREDIENT ===\nNAME: Mica";
const mixed = parseImportText(MIXED);
check(mixed.kind === "mixed" && mixed.counts.ingredients === 2 && mixed.counts.inventory === 2, "a mixed file says how many of each it has");
const why = ingredientsNotHere(2);
check(/2 ingredient records/.test(why) && /cannot be extracted here/.test(why) && /Ingredient Curator/.test(why) && /checked by an admin/.test(why), "the inventory explains why ingredient details are not read there");
check(/1 ingredient record \(/.test(ingredientsNotHere(1)), "one record is said in the singular");
check(/cannot be extracted here/.test(inventoryNotHere(3)) && /Brand & Product Inventory/.test(inventoryNotHere(3)), "the Curator explains it the other way round");

console.log(`checks made: ${checks}`);
if (failed) { console.log(`${failed} FAILED`); process.exit(1); }
console.log("ALL PASSED");
