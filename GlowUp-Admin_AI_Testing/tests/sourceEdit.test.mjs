

import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";

const here = path.dirname(fileURLToPath(import.meta.url));
const s = await import("data:text/javascript;base64," + Buffer.from(readFileSync(path.join(here, "..", "_Shared", "sourceEdit.js"))).toString("base64"));

let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };
const equal = (got, expected, what) => check(JSON.stringify(got) === JSON.stringify(expected), `${what} (got ${JSON.stringify(got)})`);

const WOOL = "woolworths.com.au | https://woolworths.com.au/p1", WATSONS = "watsons.com.ph | https://watsons.com.ph/p1";
const FDA = "FDA Philippines verification | https://verification.fda.gov.ph/x", DOC = "Imported document: list.pdf";
const OFFICIAL = "Official website | https://maybelline.com";


equal(s.rolesOf([WOOL, WATSONS]), { official: -1, primary: 0 }, "the first address is primary");
equal(s.rolesOf([FDA, DOC, WOOL]), { official: -1, primary: 2 }, "an FDA page and a line with no address are never primary");
equal(s.rolesOf([OFFICIAL, WOOL]), { official: 0, primary: 1 }, "the official line is not primary");
equal(s.officialOf([OFFICIAL, WOOL]), "https://maybelline.com", "the official address is found");
check(s.isLocked(FDA) && s.isLocked(DOC) && !s.isLocked(WOOL), "FDA pages and plain text are locked");


equal(s.withOfficial([WOOL, WATSONS], "https://maybelline.com"), [OFFICIAL, WOOL, WATSONS], "official goes first and every other line stays");
equal(s.withOfficial([FDA], "https://maybelline.com"), [OFFICIAL, FDA], "an FDA page stays");
equal(s.withOfficial(["Official website | https://old.com", WOOL], "https://maybelline.com"), [OFFICIAL, WOOL], "an older official line is replaced");
equal(s.withOfficial(["maybelline.com | https://maybelline.com/", WOOL], "https://maybelline.com"), [OFFICIAL, WOOL], "the same address is not kept twice");
equal(s.withOfficial([OFFICIAL, WOOL], ""), [WOOL], "an empty address removes the official line only");
equal(s.withOfficial([OFFICIAL, WOOL], "https://maybelline.com"), [OFFICIAL, WOOL], "applying the same address changes nothing");


equal(s.withPrimary([WOOL, WATSONS], "https://new.com/a"), ["new.com | https://new.com/a", WATSONS], "primary is replaced in its place");
equal(s.withPrimary([OFFICIAL, FDA], "https://new.com/a"), [OFFICIAL, "new.com | https://new.com/a", FDA], "a new primary goes right after the official line");
equal(s.withPrimary([WOOL], "https://woolworths.com.au/p1"), [WOOL], "the same primary changes nothing");
equal(s.withPrimary([WOOL, WATSONS], "https://watsons.com.ph/p1"), ["watsons.com.ph | https://watsons.com.ph/p1"], "a secondary that becomes primary is not listed twice");
equal(s.withPrimary([FDA], "https://new.com/a"), ["new.com | https://new.com/a", FDA], "an FDA page is kept beside a new primary");


equal(s.withSecondary([WOOL], "https://watsons.com.ph/p1"), [WOOL, WATSONS], "a secondary goes last");
equal(s.withSecondary([WOOL, WATSONS], "https://watsons.com.ph/p1/"), [WOOL, WATSONS], "an address the product has is not added again");


equal(s.setRow([WOOL, WATSONS], 1, ""), [WOOL], "an empty row is removed");
equal(s.setRow(["My page | https://a.com/1"], 0, "https://a.com/2"), ["My page | https://a.com/2"], "the same site keeps its title");
equal(s.setRow(["My page | https://a.com/1"], 0, "https://b.com/2"), ["b.com | https://b.com/2"], "a new site gets its own name");
equal(s.setRow([WOOL], 1, "https://new.com/a"), [WOOL, "new.com | https://new.com/a"], "a new row is added");


equal(s.checkUrl("  https://a.com/x "), { url: "https://a.com/x" }, "a web address is taken");
equal(s.checkUrl(""), { url: "" }, "an empty box is allowed");
for (const bad of ["http://a.com", "javascript:alert(1)", "a.com", "https://user:pw@a.com", "https://fda.gov@evil.com", "https://a b.com", "https://localhost", "https://a.com/" + "x".repeat(300)])
    check(!!s.checkUrl(bad).error, "refused: " + bad.slice(0, 40));


const products = [{ id: "a", name: "A", sources: [WOOL] }, { id: "b", name: "B", sources: [OFFICIAL] }, { id: "c", name: "C" }, { id: "d", name: "D", sources: Array.from({ length: 40 }, (_, i) => `x | https://x.com/${i}`) }];
const plan = s.planApply(products, new Set(["a", "b", "c", "d"]), "official", "https://maybelline.com");
equal(plan.change.map((c) => c.id), ["a", "c"], "only products that really change are written");
equal(plan.same.map((p) => p.id), ["b"], "a product that has it already is not written");
equal(plan.full.map((p) => p.id), ["d"], "a product the rules would refuse is held back");
equal(plan.change[1].after, [OFFICIAL], "a product with no sources gets the line");
equal(s.planApply(products, ["a"], "official", "https://m.com").change.map((c) => c.id), ["a"], "products not chosen are not looked at");
equal(products[0].sources, [WOOL], "the plan does not change the products themselves");


const many = Array.from({ length: 1001 }, (_, i) => i);
equal(s.chunks(many).map((c) => c.length), [400, 400, 201], "1001 writes in three batches");
check(s.chunks(many).every((c) => c.length <= 500), "no batch over Firestore's limit");
equal(s.chunks([]), [], "nothing to write: no batch");


check(s.bulkAllowed("Maybelline New York"), "the pilot brand is allowed");
check(s.bulkAllowed("  maybelline new york "), "whatever the letter case");
check(!s.bulkAllowed("Careline"), "another brand waits");
check(s.bulkAllowed("Careline", []), "an empty pilot list means every brand");


const brand = Array.from({ length: 27 }, (_, i) => ({ name: "n" + String(26 - i).padStart(2, "0") }));
const first = s.brandPageOf(brand, 1, 10), last = s.brandPageOf(brand, 3, 10);
equal(first.rows.map((p) => p.name)[0], "n00", "page 1 starts at the first name of the whole brand");
equal(last.rows.map((p) => p.name), ["n20", "n21", "n22", "n23", "n24", "n25", "n26"], "page 3 ends at the last name");
equal([first.rows.length, first.pages, first.showPager, first.showSizes], [10, 3, true, true], "27 products: 10 per page, a pager and sizes");
equal(s.brandPageOf(brand, 9, 10).page, 3, "a page past the end falls back to the last");
const ten = s.brandPageOf(brand.slice(0, 10), 1, 10), eleven = s.brandPageOf(brand.slice(0, 11), 1, 10);
equal([ten.showPager, ten.showSizes], [false, false], "10 products: no pager, no sizes");
equal([eleven.showPager, eleven.showSizes], [true, true], "11 products: a pager and sizes");
equal([s.brandPageOf(brand, 1, 50).showPager, s.brandPageOf(brand, 1, 50).showSizes], [false, true], "27 at 50 per page: the sizes stay, so it can be set back");


const typed = s.groupByType([{ name: "z", cosmeticType: "Lipstick" }, { name: "a", cosmeticType: "lipstick" }, { name: "m", cosmeticType: "BB Cream" }, { name: "q", cosmeticType: "" }]);
equal(typed.map((g) => g.label), ["BB Cream", "Lipstick", "No type set"], "groups A to Z, no type last, letter case does not split a group");
equal(typed[1].rows.map((p) => p.name), ["a", "z"], "products A to Z inside a group");

console.log("checks made: " + checks);
console.log(failed === 0 ? "ALL PASSED" : failed + " FAILED");
process.exit(failed === 0 ? 0 : 1);
