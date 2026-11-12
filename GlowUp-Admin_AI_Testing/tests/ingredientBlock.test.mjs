


import { ingredientBlockOf, splitIngredients, parseLabeledProducts, suggestMatches, similarity, SUGGESTION_THRESHOLD } from "../_Shared/ingredientBlock.js";

let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };
const equal = (a, b, what) => check(JSON.stringify(a) === JSON.stringify(b), `${what}\n      got      ${JSON.stringify(a)}\n      expected ${JSON.stringify(b)}`);


equal(ingredientBlockOf("Directions: apply.\nIngredients: Talc, Mica, Silica.\nNet wt 8 g"), "Talc, Mica, Silica.", "the block stops at the next label part");
equal(ingredientBlockOf("INGREDIENTS: Talc, Mica"), "Talc, Mica", "capitals");
equal(ingredientBlockOf("ingredient: Talc, Mica"), "Talc, Mica", "singular, lower case");
equal(ingredientBlockOf("INCI: Aqua, Glycerin"), "Aqua, Glycerin", "INCI");
equal(ingredientBlockOf("Ingredients (INCI): Aqua, Glycerin"), "Aqua, Glycerin", "Ingredients (INCI)");
equal(ingredientBlockOf("Ingrédients : Aqua, Glycerin"), "Aqua, Glycerin", "French, with a space before the colon");
equal(ingredientBlockOf("Sangkap: Aqua, Glycerin"), "Aqua, Glycerin", "Filipino");
equal(ingredientBlockOf("Komposisi: Aqua, Glycerin"), "Aqua, Glycerin", "Malay / Indonesian");
equal(ingredientBlockOf("INGREDIENTS Talc, Mica, Silica\n\nMade in Korea"), "Talc, Mica, Silica", "capitals without a colon, stops at a blank line");
equal(ingredientBlockOf("Ingredients are listed in order of amount."), "", "a sentence that only uses the word is not a block");
equal(ingredientBlockOf("Free of harmful ingredients, tested by dermatologists."), "", "no colon, no block");
equal(ingredientBlockOf(""), "", "empty text");
equal(ingredientBlockOf(null), "", "null");
equal(ingredientBlockOf("Brand: X Ingredients: Talc, Mica FDA Notification No.: 123 Product: Y"), "Talc, Mica", "the next field of a product sheet ends it");


equal(splitIngredients("Aqua, Glycerin, Mica").ingredients, ["Aqua", "Glycerin", "Mica"], "commas");
equal(splitIngredients("Aqua; Glycerin; Mica").ingredients, ["Aqua", "Glycerin", "Mica"], "semicolons");
equal(splitIngredients("• Aqua • Glycerin • Mica").ingredients, ["Aqua", "Glycerin", "Mica"], "bullets");
equal(splitIngredients("- Aqua\n- Glycerin\n- Mica").ingredients, ["Aqua", "Glycerin", "Mica"], "dash bullets, one a line");
equal(splitIngredients("Aqua\nGlycerin\nMica\nTalc").ingredients, ["Aqua", "Glycerin", "Mica", "Talc"], "one name a line, no commas");
equal(splitIngredients("Aqua, Sodium\nLaureth Sulfate, Mica").ingredients, ["Aqua", "Sodium Laureth Sulfate", "Mica"], "a name wrapped over two lines stays one name");
equal(splitIngredients("Aqua, Parfum (Fragrance, Aroma), Mica").ingredients, ["Aqua", "Parfum (Fragrance, Aroma)", "Mica"], "a comma inside brackets does not split");
equal(splitIngredients("Aqua, Glycerin [and] Mica").ingredients, ["Aqua", "Glycerin [and] Mica"], "square brackets are respected too");
equal(splitIngredients("Aqua (and) Glycerin, Mica (AND) Titanium Dioxide").ingredients, ["Aqua", "Glycerin", "Mica", "Titanium Dioxide"], "an INCI blend written with (and) is two names");
equal(splitIngredients("Aqua/Water/Eau, Mica").ingredients, ["Aqua/Water/Eau", "Mica"], "a slash is kept inside the name");
equal(splitIngredients("Aqua, Mica*, Glycerin†").ingredients, ["Aqua", "Mica", "Glycerin"], "footnote marks are dropped");
equal(splitIngredients("Aqua, and Glycerin").ingredients, ["Aqua", "Glycerin"], "a leading 'and' is dropped");
equal(splitIngredients("Aqua, Aqua, aqua, Mica").ingredients, ["Aqua", "Mica"], "a name once");
equal(splitIngredients("Aqua, 100%, 12, Mica").ingredients, ["Aqua", "Mica"], "numbers and percents are not names");
equal(splitIngredients("Aqua,, ,Mica,").ingredients, ["Aqua", "Mica"], "empty pieces");
equal(splitIngredients("").ingredients, [], "empty");
equal(splitIngredients(null).ingredients, [], "null");


equal(splitIngredients("Mica, C.I. 77891, CI77491, Colour Index No. 77492, CI 15850:1").ingredients, ["Mica", "CI 77891", "CI 77491", "CI 77492", "CI 15850:1"], "colour index numbers are written CI nnnnn");


{
    const a = splitIngredients("Talc, Mica, Silica. May contain: CI 77491, CI 77492, CI 77891");
    equal(a.ingredients, ["Talc", "Mica", "Silica"], "may contain: the main list");
    equal(a.mayContain, ["CI 77491", "CI 77492", "CI 77891"], "may contain: the colours of the tail");
    const b = splitIngredients("Talc, Mica, Silica, +/- CI 77491, CI 77891");
    equal(b.ingredients, ["Talc", "Mica", "Silica"], "+/-: the main list");
    equal(b.mayContain, ["CI 77491", "CI 77891"], "+/-: the tail");
    const c = splitIngredients("Talc, Mica (+/-) CI 77491");
    equal(c.ingredients, ["Talc", "Mica"], "(+/-): the main list");
    equal(c.mayContain, ["CI 77491"], "(+/-): the tail");
    equal(splitIngredients("Talc, Mica, Peut contenir : CI 77491").mayContain, ["CI 77491"], "French tail");
}


equal(splitIngredients("Talc, Mica, Phenoxyethanol EXP 01/2028").ingredients, ["Talc", "Mica", "Phenoxyethanol"], "expiry text glued to the last name is cut off");


{
    const doc = `Brand: Examplebrand
Product: Velvet Matte Lipstick
Kind: Lipstick
Ingredients: Ricinus Communis Seed Oil, Octyldodecanol, Candelilla Cera, Mica, CI 77891
FDA Notification No.: 1234

Product: Soft Blur Powder
Ingredients: Talc, Mica, Silica, Zinc Stearate, Phenoxyethanol

Brand: Otherbrand
Product: Plump Gloss
Ingredients: Polybutene, Hydrogenated Polyisobutene, Parfum
Product: Mystery Item
Barcode: 4800000000017
`;
    const out = parseLabeledProducts(doc);
    equal(out.kind, "inventory", "labelled products: read");
    const products = out.draft.products;
    equal(products.length, 3, "labelled products: three have brand, name and a list");
    equal(products[0].brandName + "|" + products[0].productName + "|" + products[0].cosmeticType, "Examplebrand|Velvet Matte Lipstick|Lipstick", "the first product, kind from its own line");
    equal(products[0].ingredients, ["Ricinus Communis Seed Oil", "Octyldodecanol", "Candelilla Cera", "Mica", "CI 77891"], "its ingredients");
    equal(products[1].brandName + "|" + products[1].productName, "Examplebrand|Soft Blur Powder", "a brand line holds for the products after it");
    equal(products[1].cosmeticType, "Powder", "the kind comes from the product name when no line gives it");
    equal(products[2].brandName + "|" + products[2].productName + "|" + products[2].cosmeticType, "Otherbrand|Plump Gloss|Lip Gloss", "a new brand line starts a new brand");
    check(out.notes.some((n) => n.includes("Mystery Item")), "a product without a list is named in the notes: " + JSON.stringify(out.notes));
}
equal(parseLabeledProducts("Ingredients: Talc, Mica, Silica").kind, "", "a list without a brand and a product name is not a product");
equal(parseLabeledProducts("Nothing useful here.").kind, "", "no labels at all");
equal(parseLabeledProducts("Brand: A\nProduct: B\nIngredients: Talc, Mica").kind, "", "a brand of one letter and a list of two names is left out");
equal(parseLabeledProducts("Brand: Examplebrand\nProduct: Plain Thing\nIngredients: Talc, Mica, Silica").draft.products[0].cosmeticType, "", "a kind that cannot be told stays empty (nothing is guessed)");
equal(parseLabeledProducts("Brand: Examplebrand Product: One Lipstick Ingredients: Talc, Mica, Silica Product: Two Powder Ingredients: Kaolin, Mica, Silica").draft.products.length, 2, "labels on one line, as a PDF reader may give them");


const known = ["Glycerin", "Tocopherol", "Phenoxyethanol", "Titanium Dioxide", "Dimethicone", "Sodium Hyaluronate"];
equal(suggestMatches("Glycerine", known).map((s) => s.name), ["Glycerin"], "a spelling variant is suggested");
equal(suggestMatches("Phenoxyethanoll", known).map((s) => s.name), ["Phenoxyethanol"], "a typo is suggested");
equal(suggestMatches("Titanium Dioxid", known).map((s) => s.name), ["Titanium Dioxide"], "a cut-off name is suggested");
equal(suggestMatches("Glycerin", known), [], "an exact match is not a suggestion");
equal(suggestMatches("GLYCERIN", known), [], "an exact match in capitals is not a suggestion");
equal(suggestMatches("Sodium Lauryl Sulfate", known), [], "an unrelated name gets no suggestion");
equal(suggestMatches("Tocopheryl Acetate", known), [], "a different substance is not suggested as a typo");
equal(suggestMatches("", known), [], "empty name");
equal(suggestMatches("Glycerine", []), [], "nothing known");
check(similarity("abc", "abc") === 1 && similarity("abc", "xyz") === 0, "similarity: equal is 1, nothing in common is 0");
check(SUGGESTION_THRESHOLD >= 0.88 && SUGGESTION_THRESHOLD < 1, "the threshold is documented in the code and high");

console.log(`checks made: ${checks}`);
if (failed > 0) { console.log(`${failed} FAILED`); process.exit(1); }
console.log("ALL PASSED");
