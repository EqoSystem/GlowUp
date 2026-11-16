// Checks the catalogue checks (_Shared/glowupCatalog.js): duplicates, what is not make-up, ingredient entries that
// are not ingredients, what a merge writes, and the clean-up plan. It works on a small made-up catalogue: nothing
// here talks to Firebase.
//
//   Run it from the website folder:   node tests/catalog.test.mjs      (Node 22.7 or newer)
//   The last line says "ALL PASSED" or how many checks failed (exit code 1).
import { fileURLToPath, pathToFileURL } from "node:url";
import path from "node:path";

const here = path.dirname(fileURLToPath(import.meta.url));
let c;
try { c = await import(pathToFileURL(path.join(here, "..", "_Shared", "glowupCatalog.js")).href); }
catch (error) { console.error("The website's rules could not be loaded (Node 22.7 or newer is needed).\n" + error.message); process.exit(1); }

let checks = 0;
const failures = [];
const check = (condition, message) => { checks++; if (!condition) failures.push(message); };
const equal = (got, expected, what) => check(JSON.stringify(got) === JSON.stringify(expected), `${what}\n      expected ${JSON.stringify(expected)}\n      got      ${JSON.stringify(got)}`);
const ids = (items) => items.map((item) => item.id).sort();

// ---------------------------------------------------------------------------------------------
// A small catalogue with one of every problem
// ---------------------------------------------------------------------------------------------
const brand = (id, name, category, more) => Object.assign({ id, name, category, status: "Active", scans: 0, source: "", reviewed: false, binned: false, aliases: [] }, more);
const product = (id, brandId, brandName, name, cosmeticType, more) => Object.assign({ id, brandId, brandName, name, cosmeticType, ingredients: [], shades: [], status: "Active", scans: 0, source: "", reviewed: false, binned: false, barcode: "", aliases: [], addedAt: 100 }, more);
const ingredient = (id, name, techName, more) => Object.assign({ id, name, techName, aliases: [], source: "", reviewed: true, binned: false }, more);

const brands = [
    brand("b_may", "Maybelline", "Makeup"),
    brand("b_lor", "L'Oreal Paris", "Makeup"),
    brand("b_lor2", "L'Oréal  PARIS", "Haircare", { source: "openbeautyfacts", scans: 3 }),
    brand("b_sun", "Sunny Care", "Skincare"),
    brand("b_test", "asdfgh", "zzz"),
    brand("b_mix", "Happy Skin", "Skincare"),
    brand("b_balm", "Balm Co", "Skincare"),
    brand("b_gone", "Old Brand", "Makeup", { binned: true })
];
const products = [
    product("p1", "b_may", "Maybelline", "Superstay Matte Ink", "Lipstick", { reviewed: true, scans: 5, ingredients: ["Dimethicone", "Mica"] }),
    product("p2", "b_may", "Maybelline", "Maybelline SuperStay Matte Ink", "Lipstick", { source: "scan", scans: 2, barcode: "0 41554 49682 9" }),
    product("p3", "b_may", "Maybelline", "Fit Me Matte Foundation", "Foundation", { barcode: "4800888141125" }),
    product("p4", "b_lor", "L'Oreal Paris", "Fit Me Concealer", "Concealer", { barcode: "04800888141125", source: "scan", ingredients: ["Water", "Glycerin"] }),
    product("p5", "b_may", "Maybelline", "Lash Sensational Mascara", "Mascara"),
    product("p6", "b_may", "Maybelline", "Lash Sensational Waterproof Mascara", "Mascara"),
    product("p7", "b_lor", "L'Oreal Paris", "L'Oréal Paris Voluminous Mascara", "Mascara"),
    product("p8", "b_lor2", "L'Oréal PARIS", "Elvive Shampoo", "Shampoo"),
    product("p9", "b_sun", "Sunny Care", "Daily Facial Cleanser", "Cleanser"),
    product("p10", "b_sun", "Sunny Care", "Spf 50", "Sunscreen"),
    product("p11", "b_mix", "Happy Skin", "Clear Setting Gel", "Moisturizer"),
    product("p12", "b_mix", "Happy Skin", "Lip Mallow Mousse", "Lip Cream"),
    product("p13", "b_balm", "Balm Co", "Tinted Lip Balm", "Lip Balm"),
    product("p14", "b_gone", "Old Brand", "Old Lipstick", "Lipstick", { binned: true }),
    product("p15", "b_lor", "L'Oreal Paris", "Precision Lip Liner", "Lipstick")
];
const ingredients = [
    ingredient("i1", "Vitamin E", "Tocopherol", { aliases: ["Tocopheryl Acetate"] }),
    ingredient("ai_1", "Tocopherol", "Tocopherol", { source: "ai", reviewed: false }),
    ingredient("i2", "Glycerin", "Glycerin"),
    ingredient("ai_2", "Hidden in \"Fragrance\"", "hidden:fragrance", { source: "ai", reviewed: false }),
    ingredient("ai_3", "Carbomer & Triethanolamine", "Carbomer, Triethanolamine", { source: "ai", reviewed: false }),
    ingredient("ai_4", "Net Wt 30 g", "Net Wt 30 g", { source: "ai", reviewed: false }),
    ingredient("i3", "1,2-Hexanediol", "1,2-Hexanediol"),
    ingredient("i4", "Iron Oxides", "CI 77491", { aliases: ["CI 77492", "CI 77499"] }),
    ingredient("i5", "Caprylic/Capric Triglyceride", "Caprylic/Capric Triglyceride"),
    ingredient("i6", "Water", "Aqua", { aliases: ["Water (Aqua)", "Eau"] })
];

// ---------------------------------------------------------------------------------------------
// 1. How alike two names are
// ---------------------------------------------------------------------------------------------
equal(c.editDistance("kitten", "sitting"), 3, "edit distance");
equal(c.similarity("Super Stay", "SuperStay"), 1, "spaces do not matter");
equal(c.similarity("L'Oréal", "loreal"), 1, "accents and punctuation do not matter");
check(c.similarity("Complexion Blur Translucent Powder", "Complexion Blur Translucent Perfecting Powder") >= 0.85, "one extra word is still nearly the same");
check(c.similarity("Hoola Matte Bronzer", "Hoola Lite Light Matte Bronzer") < 0.85, "two extra words are not");
equal(c.similarity("", "Lipstick"), 0, "nothing is like nothing");

// ---------------------------------------------------------------------------------------------
// 2. The brand at the front of a name
// ---------------------------------------------------------------------------------------------
equal(c.nameWithoutBrand("Maybelline SuperStay Matte Ink", "Maybelline"), "SuperStay Matte Ink", "brand taken off");
equal(c.nameWithoutBrand("L’oreal Paris Infallible Lipstick 420", "L'Oreal Paris"), "Infallible Lipstick 420", "brand written another way");
equal(c.nameWithoutBrand("Fit Me Foundation", "Maybelline"), "Fit Me Foundation", "no brand in front: unchanged");
equal(c.nameWithoutBrand("Maybelline", "Maybelline"), "Maybelline", "nothing would be left: unchanged");
equal(c.nameWithoutBrand("Happy Hour Lip Tint", "Happy Skin"), "Happy Hour Lip Tint", "only part of the brand in front: unchanged");
equal(c.nameWithoutBrand("Clinique - Pop Lip Colour", "Clinique"), "Pop Lip Colour", "a dash after the brand goes too");

// ---------------------------------------------------------------------------------------------
// 3. Make-up only
// ---------------------------------------------------------------------------------------------
const scope = c.scopeReport(products);
equal(ids(scope.out.map((e) => e.product)), ["p8"], "the shampoo is out");
equal(ids(scope.skincare.map((e) => e.product)), ["p10", "p11", "p9"], "skin care");
equal(ids(scope.unclear.map((e) => e.product)), ["p13"], "a tinted lip balm is care and colour at once: an admin decides");
check(!scope.makeup.some((e) => e.product.id === "p14"), "what is in the Recycle bin is left out");
const sure = (id) => scope.skincare.concat(scope.out).find((e) => e.product.id === id).sure;
check(sure("p9") === true, "a cleanser says so in its name: sure");
check(sure("p8") === true, "a shampoo says so in its name: sure");
check(sure("p10") === false, "'Spf 50' filed as Sunscreen: only a vague word in the name, so a person looks");
check(sure("p11") === false, "'Clear Setting Gel' filed as Moisturizer may be a brow gel: a person looks");

equal(c.typeFixes(products).map((f) => [f.product.id, f.to]), [["p12", "Lipstick"]], "'Lip Cream' becomes the app's word Lipstick");
const kinds = c.kindChecks(products);
check(kinds.some((k) => k.product.id === "p15" && k.hint === "Lip Liner"), "a lip liner filed as Lipstick is pointed out");
check(!kinds.some((k) => k.product.id === "p1"), "a lipstick filed as Lipstick is not");

const brandPlan = new Map(c.brandReport(brands, products).map((e) => [e.brand.id, e]));
equal(brandPlan.get("b_may").action + "/" + brandPlan.get("b_may").category, "keep/", "a make-up brand with make-up stays as it is");
equal(brandPlan.get("b_mix").action + "/" + brandPlan.get("b_mix").category, "keep/Makeup", "a Skincare brand with make-up becomes a Makeup brand");
equal(brandPlan.get("b_sun").action, "bin", "a brand without any make-up goes to the bin");
equal(brandPlan.get("b_test").action, "bin", "an empty brand that is not a make-up brand goes to the bin");
equal(brandPlan.get("b_balm").action, "review", "a brand with only undecided kinds waits for a person");
check(!brandPlan.has("b_gone"), "a brand in the Recycle bin is left out");

// ---------------------------------------------------------------------------------------------
// 4. The same thing twice
// ---------------------------------------------------------------------------------------------
equal(c.brandDuplicates(brands).map((g) => ids(g.items)), [["b_lor", "b_lor2"]], "the two L'Oreal entries are one brand");
equal(c.brandDuplicates(brands)[0].keep.id, "b_lor", "the entry typed on the website is kept, not the import");

const duplicates = c.productDuplicates(products, brands, { threshold: 0.85 });
const sureGroups = duplicates.sure.map((g) => ids(g.items)).sort();
equal(sureGroups, [["p1", "p2"], ["p3", "p4"]], "same brand and name, and same barcode");
equal(duplicates.sure.find((g) => g.items.some((p) => p.id === "p1")).keep.id, "p1", "the reviewed entry with an ingredient list is kept");
equal(duplicates.sure.find((g) => g.items.some((p) => p.id === "p3")).reason, "The same barcode", "the reason is told");
equal(duplicates.possible.map((g) => ids(g.items)), [["p5", "p6"]], "a waterproof version is only 'possibly the same'");
equal(c.productDuplicates(products, brands, { threshold: 0.99 }).possible.length, 0, "a higher threshold lists fewer");
check(!duplicates.sure.some((g) => g.items.some((p) => p.id === "p14")), "the Recycle bin is not looked at");

const merged = c.mergeProducts(products[0], [products[1]]);
equal(merged.keepChanges, { aliases: ["Maybelline SuperStay Matte Ink"], scans: 7, barcode: "0 41554 49682 9" }, "the kept product takes the name, the scans and the barcode");
equal(merged.bin.map((b) => b.item.id), ["p2"], "the copy goes to the bin");
equal(c.mergeProducts(products[2], [products[3]]).keepChanges.ingredients, ["Water", "Glycerin"], "a kept product without a list takes the copy's list");

const mergedBrand = c.mergeBrands(brands[1], [brands[2]], products);
equal(mergedBrand.keepChanges.scans, 3, "the kept brand takes the scans");
equal(mergedBrand.moveProducts.map((m) => [m.product.id, m.brandId]), [["p8", "b_lor"]], "the other entry's products move to the kept brand");
check(!("aliases" in mergedBrand.keepChanges) || mergedBrand.keepChanges.aliases.length === 1, "the other spelling is kept as an alias");

// ---------------------------------------------------------------------------------------------
// 5. Ingredient entries
// ---------------------------------------------------------------------------------------------
equal(ids(c.ingredientIssues(ingredients).map((e) => e.ingredient)), ["ai_2", "ai_3", "ai_4"], "a note, joined names and label text are not ingredients");
for (const id of ["i3", "i4", "i5", "i6"]) equal(c.ingredientProblem(ingredients.find((i) => i.id === id)), "", `"${ingredients.find((i) => i.id === id).name}" is a real ingredient entry`);
const twice = c.ingredientDuplicates(ingredients);
equal(twice.map((g) => ids(g.items)), [["ai_1", "i1"]], "the AI made an entry for an ingredient that was there already");
equal(twice[0].keep.id, "i1", "the admin's entry is kept");
check(twice[0].aiCopy === true, "and it is marked as an AI copy");
equal(c.mergeIngredients(ingredients[0], [ingredients[1]]).keepChanges, {}, "its names were known already: nothing to add");
// the order of trust decides which entry is kept
const trusted = (source, more) => ingredient("x", "Glycerin", "Glycerin", Object.assign({ source, reviewed: false }, more));
equal([trusted(""), trusted("starter"), trusted("ai", { reviewed: true }), trusted("ai", { reviewed: true, autoApproved: true }), trusted("ai")].map(c.trustOf), [8, 6, 5, 2, 0], "typed here, starter, approved by a person, approved by itself, not checked");
equal(c.pickKeeper([ingredient("ai_g", "Glycerine", "Glycerin", { source: "ai", reviewed: true, autoApproved: true }), ingredient("starter_g", "Glycerin", "Glycerin", { source: "starter", reviewed: false })]).id, "starter_g", "the app's own starter entry is kept, not an AI entry that approved itself");
equal(c.ingredientLookalikes([ingredient("a", "Niacinamide", "Niacinamide"), ingredient("b", "Niacinamid", "Niacinamid"), ingredient("m", "Methylparaben", "Methylparaben"), ingredient("p", "Propylparaben", "Propylparaben")], 0.9).map((g) => ids(g.items)), [["a", "b"]], "a name one letter off is listed; another paraben is not");
const use = c.ingredientUse(ingredients, products);
equal([use.get("i2"), use.get("i6"), use.get("i1")], [1, 1, undefined], "how many product lists use an entry");

// ---------------------------------------------------------------------------------------------
// 6. The clean-up plan
// ---------------------------------------------------------------------------------------------
const before = JSON.stringify([brands, products, ingredients]);
const plan = c.cleanupPlan(brands, products, ingredients, { threshold: 0.85 });
equal(JSON.stringify([brands, products, ingredients]), before, "making a plan changes nothing in the lists it was given");
equal(plan.steps.map((s) => s.id), ["notMakeup", "notMakeupCheck", "brands", "brandDuplicates", "productDuplicates", "types", "names", "definitions", "markdown", "safetyClaims", "joinedNames", "friendlyNames", "ingredients"], "the steps, in order");
check(plan.steps.every((s) => s.changes.every((ch) => ch.action === "bin" || ch.action === "update")), "a plan never deletes: it only bins or updates");
const step = (id) => plan.steps.find((s) => s.id === id).changes;
equal(step("notMakeup").map((ch) => ch.docId).sort(), ["p8", "p9"], "clearly not make-up");
equal(step("notMakeupCheck").map((ch) => ch.docId).sort(), ["p10", "p11"], "probably not make-up: for a person to check");
equal(step("brands").filter((ch) => ch.action === "bin").map((ch) => ch.docId).sort(), ["b_lor2", "b_sun", "b_test"], "brands left without make-up");
equal(step("brands").filter((ch) => ch.action === "update").map((ch) => [ch.docId, ch.fields.category]), [["b_mix", "Makeup"]], "the Skincare brand with make-up becomes a Makeup brand");
equal(step("brandDuplicates").length, 0, "the second L'Oreal entry went to the bin already, so there is nothing left to merge");
equal(step("productDuplicates").filter((ch) => ch.action === "bin").map((ch) => ch.docId).sort(), ["p2", "p4"], "the copies go to the bin");
equal(step("types").map((ch) => [ch.docId, ch.fields.cosmeticType]), [["p12", "Lipstick"]], "kinds in the app's own words");
equal(step("names").map((ch) => [ch.docId, ch.fields.name]), [["p7", "Voluminous Mascara"]], "the brand comes off the front of a name");
equal(step("names")[0].fields.aliases, ["L'Oréal Paris Voluminous Mascara"], "and the old name is kept as an alias");
equal(step("ingredients").filter((ch) => ch.action === "bin").map((ch) => ch.docId).sort(), ["ai_1", "ai_2", "ai_3", "ai_4"], "ingredient entries that go to the bin");
equal(ids(plan.forReview.unclear.map((e) => e.product)), ["p13"], "the tinted lip balm waits for a decision");
equal(plan.forReview.brands.map((e) => e.brand.id), ["b_balm"], "and so does its brand");
equal(plan.after, { brands: 4, products: 8, ingredients: 6, makeup: 7 }, "what is left afterwards");
equal(c.planTotals(plan).bin, 13, "entries to the Recycle bin in all");

// a later step sees what an earlier one changed: the kept product is renamed AND keeps the alias it got from the merge
const twoSteps = c.cleanupPlan(
    [brand("b", "Clinique", "Makeup")],
    [product("a", "b", "Clinique", "Clinique Pop Lip Colour", "Lipstick", { reviewed: true, barcode: "20714739485" }), product("z", "b", "Clinique", "Pop Tint Stick", "Lipstick", { source: "scan", barcode: "020714739485" })],
    [], {});
const renamed = twoSteps.steps.find((s) => s.id === "names").changes[0];
equal([renamed.docId, renamed.fields.name, renamed.fields.aliases], ["a", "Pop Lip Colour", ["Pop Tint Stick", "Clinique Pop Lip Colour"]], "the rename keeps the alias from the merge");
// an alias that is the new name itself is dropped
const sameName = c.cleanupPlan([brand("b", "Clinique", "Makeup")],
    [product("a", "b", "Clinique", "Clinique Pop Lip Colour", "Lipstick", { reviewed: true }), product("z", "b", "Clinique", "Pop  Lip Colour", "Lipstick", { source: "scan" })], [], {});
equal(sameName.steps.find((s) => s.id === "names").changes[0].fields.aliases, ["Clinique Pop Lip Colour"], "an alias that is now the name itself is not kept twice");

equal(c.cleanupPlan(brands, products, ingredients, { only: ["ingredients"] }).steps.map((s) => s.id), ["ingredients"], "a plan can be limited to some steps");

// ---------------------------------------------------------------------------------------------
// productVisibility: the same rule as the app (ProductRepository.Usable / BrandRepository.IsSwitchedOff)
// ---------------------------------------------------------------------------------------------
const seen = (p, b) => c.productVisibility(p, b).reason;
const lip = (more) => product("pv", "b_pv", "Brand", "Matte Lipstick", "Lipstick", more);
const okBrand = (more) => brand("b_pv", "Brand", "Makeup", more);
equal(seen(lip(), okBrand()), "live", "an Active make-up product of an Active brand is live");
equal(seen(lip(), okBrand({ status: "Pending" })), "brand-pending", "approved product, Pending brand: hidden");
equal(seen(lip(), okBrand({ status: "Inactive" })), "brand-not-active", "approved product, Inactive brand: hidden");
equal(seen(lip(), okBrand({ binned: true })), "brand-binned", "a brand in the Recycle bin hides its products");
equal(seen(lip(), null), "live", "a missing brand does not hide the product (the app only hides on a found brand)");
equal(seen(lip({ status: "" }), okBrand()), "product-not-active", "an empty product status is NOT Active");
equal(seen(lip({ status: "Active", rawStatus: "" }), okBrand()), "product-not-active", "an empty stored status counts even when the page shows Active");
equal(seen(lip(), okBrand({ status: "Active", rawStatus: "" })), "brand-not-active", "an empty brand status is NOT Active");
equal(seen(lip({ status: " active " }), okBrand({ status: "ACTIVE" })), "live", "the app trims and ignores capitals in a status");
equal(seen(lip({ status: "Pending" }), okBrand()), "product-not-active", "a Pending product is hidden");
equal(seen(lip({ binned: true }), okBrand()), "binned", "a product in the Recycle bin is hidden");
equal(seen(product("pv2", "b_pv", "Brand", "Face Wash Gel", "Cleanser"), okBrand({ status: "Pending" })), "not-makeup", "a non-make-up product says so, even under a Pending brand");
equal(c.productVisibility(product("pv3", "b_pv", "Brand", "Face Wash Gel", "Cleanser"), okBrand()).live, false, "a non-make-up product is not live");

// ---------------------------------------------------------------------------------------------
console.log(`checks made: ${checks}`);
if (failures.length > 0) {
    console.log("\nFAILED: " + failures.length + " check(s)\n");
    for (const failure of failures) console.log("  - " + failure);
    process.exit(1);
}
console.log("ALL PASSED");
