// Checks of api/_lib/pageIngredients.js WITHOUT any network: ingredient lists come only from fetched page text,
// the brand's own site wins over shops, and unsafe addresses are never fetched.
import { createRequire } from "node:module";
const require = createRequire(import.meta.url);
const { extractIngredientList, listFrom, isOfficial, fetchable, findPageIngredients, pageText } = require("../api/_lib/pageIngredients.js");

let failed = 0, passed = 0;
const check = (ok, name) => { if (ok) passed++; else { failed++; console.log("FAIL " + name); } };
const equal = (a, b, name) => check(JSON.stringify(a) === JSON.stringify(b), name + "  (got " + JSON.stringify(a) + ")");

// ---- cutting the list out of a page
const page = `<html><head><script>var ingredients = "Aqua, Hack, Hack, Hack, Hack";</script><style>.x{}</style></head><body>
<h1>Superstay Matte Ink</h1><p>Long-lasting liquid lipstick with up to 16 hour wear.</p>
<h3>Ingredients</h3><p>DIMETHICONE, TRIMETHYLSILOXYSILICATE, ISODODECANE, ALUMINA, NYLON-611/DIMETHICONE COPOLYMER, PHENOXYETHANOL, [+/- MAY CONTAIN: CI 77491, CI 77492, CI 77499 / IRON OXIDES, CI 77891 / TITANIUM DIOXIDE].</p>
<p>Free shipping on orders over 500.</p></body></html>`;
const list = extractIngredientList(page);
check(Array.isArray(list) && list[0] === "DIMETHICONE" && list.includes("PHENOXYETHANOL"), "the list after the Ingredients heading is found");
check(list && list.includes("CI 77491") && !list.some((i) => /may contain/i.test(i)), "a may-contain lead-in is removed, its colours kept");
check(!pageText(page).includes("Hack"), "script text is never read");
equal(extractIngredientList("<p>Ingredients: Aqua, Glycerin, Mica, Talc, Silica, Dimethicone.</p>"), ["Aqua", "Glycerin", "Mica", "Talc", "Silica", "Dimethicone"], "a list on the same line as its label");
equal(extractIngredientList("<p>Our ingredients are chosen with care, so you can wear this every day, all day, with confidence, love, joy and comfort.</p>"), null, "prose that mentions ingredients is not a list");
equal(extractIngredientList("<p>Ingredients: one, two, three, four, five, six</p>"), null, "words without a single real INCI anchor are not a list");
equal(extractIngredientList("<p>Nothing here</p>"), null, "a page without a label has no list");
equal(listFrom("Aqua, Glycerin"), null, "fewer than five names is not a list");

// ---- official site first
check(isOfficial("www.maybelline.com", "Maybelline New York"), "maybelline.com is Maybelline New York's own site");
check(isOfficial("maybelline.com.ph", "Maybelline New York"), "a country site of the brand counts too");
check(isOfficial("shop.happyskin.com.ph", "Happy Skin"), "happyskin.com.ph is Happy Skin's site");
check(!isOfficial("watsons.com.ph", "Maybelline New York"), "a shop is not the brand's site");
check(!isOfficial("ulta.com", "UL"), "a two-letter brand matches nothing by accident");

// ---- never fetched
check(fetchable("https://www.maybelline.com/lip-makeup/x"), "a normal https page is fetched");
for (const bad of ["http://maybelline.com/x", "https://127.0.0.1/x", "https://localhost/x", "https://user:pw@shop.example/x", "https://intranet/x", "https://shop.example:8443/x", "https://printer.local/x", "javascript:alert(1)", "file:///etc/passwd"])
    check(!fetchable(bad), "never fetched: " + bad);

// ---- the whole search with a pretend web
const shopPage = "<p>Ingredients: Aqua, Glycerin, Mica, Talc, Silica, Dimethicone</p>";
const brandPage = "<h2>Ingredients</h2><p>Isododecane, Dimethicone, Mica, Silica, Phenoxyethanol, Tocopherol</p>";
const fetched = [];
const fakeFetch = async (url) => {
    fetched.push(url);
    const html = url.startsWith("https://html.duckduckgo.com") ? '<a class="result__a" href="https://www.maybelline.com/p/1">x</a><a class="result__a" href="http://insecure.example/">y</a>'
        : url.includes("watsons") ? shopPage : url.includes("maybelline") ? brandPage : "<p>nothing</p>";
    return { ok: true, url, headers: { get: (k) => (k === "content-type" ? "text/html; charset=utf-8" : null) }, text: async () => html };
};
const found = await findPageIngredients({
    brand: "Maybelline New York", productName: "Superstay Matte Ink", pages: ["https://www.watsons.com.ph/p/2", "https://10.0.0.1/admin"],
    fetcher: fakeFetch, resultUrl: (href) => (href.startsWith("https://") ? href : ""), kindOf: (url) => (url.includes("watsons") ? "retailer" : "other"), notes: []
});
equal([found.kind, found.site, found.ingredients[0]], ["official", "maybelline.com", "Isododecane"], "the brand's own page wins over the shop's");
check(!fetched.some((u) => u.includes("10.0.0.1") || u.startsWith("http://")), "a private or http address is never fetched");
const none = await findPageIngredients({ brand: "X Brand", productName: "Y", pages: ["https://nothing.example/a"], fetcher: fakeFetch, notes: [] });
equal(none.ingredients, [], "no page with a list: no list (nothing is made up)");
equal((await findPageIngredients({ brand: "", productName: "Y", pages: [], fetcher: fakeFetch })).tried, 0, "no brand: nothing is searched");

console.log(failed ? `${failed} FAILED, ${passed} passed` : `pageIngredients: ${passed} checks, ALL PASSED`);
if (failed) process.exit(1);
