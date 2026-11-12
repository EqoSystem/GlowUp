



import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { matchesSearch } from "../_Shared/glowupRules.js";

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const read = (file) => readFileSync(path.join(root, file), "utf8");
let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };


const product = (name, brand, type, ingredients) => [name, [], type, "", "", brand, ...ingredients];
const items = [
    product("Superstay Matte Ink", "Maybelline", "Lipstick", ["Alcohol Denat.", "Isododecane"]),
    product("Soft Pinch Liquid Blush", "Rare Beauty", "Blush", ["Niacinamide", "Glycerin"]),
    product("Fit Me Foundation", "Maybelline", "Foundation", ["Water", "Niacinamide"])
];
const find = (typed) => items.filter((texts) => matchesSearch(typed, texts)).map((texts) => texts[0]);

check(find("maybel").length === 2, "a part of a word finds the brand (maybel -> Maybelline)");
check(find("MAYBELLINE").length === 2, "letter case does not matter");
check(find("blush").join() === "Soft Pinch Liquid Blush", "the cosmetic type is searched");
check(find("niacinamide").length === 2, "the ingredients are searched");
check(find("niacin").length === 2, "a part of an ingredient is found");
check(find("zzz").length === 0, "nothing matching gives an empty list");
check(find("maybelline niacinamide").join() === "Fit Me Foundation", "words from the brand and an ingredient work together");


const js = read("_Admin_Brand_Inventory/adminBrandInventory.js");
const html = read("_Admin_Brand_Inventory/adminBrandInventory.html");
const css = read("_Admin_Brand_Inventory/adminBrandInventory.css");
check(/\[\.\.\.productTexts\(p\), \.\.\.\(p\.ingredients \|\| \[\]\)\]/.test(js), "the list search looks in the ingredients too");
check(/products\.filter\(\(p\) => base\(p\)/.test(js), "the flat list filters the whole products array, not one page");
check(/paginate\(list, flatPage\)/.test(js) && /paginate\(groups, pageState\)/.test(js), "the search narrows the list first; the pager counts the result");
check(/SUGGEST_MAX = 5/.test(js) && /group: "Brands"/.test(js) && /group: "Products"/.test(js) && /group: "Ingredients"/.test(js), "the drop-down has Brands, Products and Ingredients, 5 each");
check(/sameKey\(typed\)\.length < 2/.test(js), "the drop-down starts at 2 letters");
check(/ArrowDown/.test(js) && /ArrowUp/.test(js) && /event\.key === "Escape"/.test(js) && /event\.key === "Enter"/.test(js), "arrow keys, Enter and Esc are handled");
check(/No results for "\$\{typed\}"/.test(js), 'an empty search says No results for "..."');
check(/contains: \$\{escapeHtml\(found\)\}/.test(js), 'a row found by an ingredient says "contains: ..."');
check(/searchParams\.set\("q"/.test(js) && /pushState/.test(js) && /popstate/.test(js) && /get\("q"\)/.test(js), "?q= is written, read, and followed by Back");
check(/event\.key !== "\/"/.test(js) && /input, textarea, select/.test(js), 'the "/" key focuses the box except inside another field');
check(/inventorySuggest/.test(html) && /inventorySearchStatus/.test(html) && /search-suggest/.test(css), "the markup and the style for the drop-down and the results line exist");
const searchBlock = js.slice(js.indexOf("function openSuggest()"), js.indexOf("function renderList("));
check(searchBlock.length > 0 && !/getDocs|getCountFromServer|onSnapshot/.test(searchBlock), "the search block makes no database call");

console.log(`\ninventorySearch: ${checks} checks made`);
console.log(failed === 0 ? "ALL PASSED" : `${failed} FAILED`);
process.exit(failed === 0 ? 0 : 1);
