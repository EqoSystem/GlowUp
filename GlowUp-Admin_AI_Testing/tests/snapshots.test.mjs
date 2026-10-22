// Checks of the Pending Snapshot Reviews helpers (_Shared/adminSnapshots.js) WITHOUT Firebase: the web-only imports are replaced by empty stand-ins.
//   Run: node tests/snapshots.test.mjs
import { register } from "node:module";

register("./stubLoader.mjs", import.meta.url);

const { similarProducts, similarIngredients, httpsLinks, SIMILAR_PRODUCT, SIMILAR_INGREDIENT } = await import("../_Shared/adminSnapshots.js");

let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };
const equal = (a, b, what) => check(JSON.stringify(a) === JSON.stringify(b), `${what} (got ${JSON.stringify(a)})`);

// ---- links shown on a card
equal(httpsLinks(["https://www.maybelline.com/fit-me", "http://shop.example/a", "javascript:alert(1)", "https://a.example/" + "x".repeat(300), "https://www.maybelline.com/fit-me", 5, null]),
    ["https://www.maybelline.com/fit-me"], "only https, under 300 characters, each once; scripts, http and non-text are dropped");
equal(httpsLinks(Array.from({ length: 9 }, (_, i) => `https://s${i}.example/`)).length, 5, "at most 5 links");
equal(httpsLinks(7), [], "not text and not a list: nothing");

// ---- products that look alike
const context = {
    brands: [{ id: "b1", name: "Maybelline", aliases: [] }, { id: "b2", name: "Revlon", aliases: [] }],
    products: [
        { id: "p1", brandId: "b1", name: "Fit Me Matte Poreless Foundation", aliases: [] },
        { id: "p2", brandId: "b1", name: "Superstay Matte Ink", aliases: [] },
        { id: "p3", brandId: "b2", name: "Fit Me Matte Poreles Foundation", aliases: [] },
        { id: "p4", brandId: "b1", name: "Fit Me Matte Poreless Foundation", aliases: [], binned: true }
    ],
    library: [{ id: "i1", name: "Methylparaben", techName: "Methylparaben", aliases: ["Nipagin M"], riskLevel: "caution" }, { id: "i2", name: "Aqua", techName: "Water", aliases: [], riskLevel: "safe" }, { id: "i3", name: "Cyclopentasiloxane", techName: "Cyclopentasiloxane", aliases: [], riskLevel: "safe" }]
};
let alike = similarProducts({ brand: "Maybelline", name: "Fit Me Matte Poreles Foundation" }, context);
equal(alike.map((a) => a.name), ["Fit Me Matte Poreless Foundation"], "a one-letter slip is shown as 'looks like' (same brand only, the bin is ignored)");
check(alike[0] && alike[0].score >= SIMILAR_PRODUCT && alike[0].score < 1, "the score is between the limit and 1");
equal(similarProducts({ brand: "Maybelline", name: "Fit Me Matte Poreless Foundation" }, context), [], "the same name is not 'looks like': approving it uses the existing product");
equal(similarProducts({ brand: "Maybelline", name: "Lash Sensational Mascara" }, context), [], "a different product gives nothing");
equal(similarProducts({ brand: "Brand Not Listed", name: "Fit Me Matte Poreles Foundation" }, context).length >= 1, true, "a new brand: every brand's products are compared");
equal(similarProducts({ brand: "Maybelline", name: "" }, context), [], "no name, nothing");

// ---- ingredients that look like a library entry
const close = similarIngredients(["Cyclopentasiloxan", "Ethylparaben", "Aqua", "Glycerin", "Nipagin M"], context);
equal([...close.keys()], ["cyclopentasiloxan"], "a one-letter slip is flagged; a different paraben (ethyl), a known name and an unrelated one are not");
check(close.get("cyclopentasiloxan") && close.get("cyclopentasiloxan").name === "Cyclopentasiloxane" && close.get("cyclopentasiloxan").score >= SIMILAR_INGREDIENT, "it names the library entry it nearly equals");
equal([SIMILAR_PRODUCT, SIMILAR_INGREDIENT], [0.88, 0.93], "the limits: 0.88 for products, 0.93 for ingredients (the Ingredient Curator's own limit)");

console.log("checks made: " + checks);
console.log(failed === 0 ? "ALL PASSED" : failed + " FAILED");
process.exit(failed === 0 ? 0 : 1);
