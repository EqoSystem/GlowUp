import assert from "node:assert/strict";
import test from "node:test";
import { createRecommendabilityChecker } from "../_Shared/glowupCatalog.js";

const brand = (more = {}) => ({
    id: "brand_1", name: "Glow Test", category: "Makeup", status: "Active", binned: false, aliases: [], ...more
});
const product = (more = {}) => ({
    id: "product_1", name: "Foundation", brandId: "brand_1", brandName: "Glow Test", cosmeticType: "Foundation",
    status: "Active", binned: false, ingredients: ["Water", "Glycerin"], ...more
});
const ingredients = [
    { id: "water", name: "Water", aliases: ["Aqua"], source: "starter", reviewed: true },
    { id: "glycerin", name: "Glycerin", aliases: [], source: "starter", reviewed: true }
];

test("a complete linked makeup product with evaluable ingredients is recommendable", () => {
    const check = createRecommendabilityChecker(ingredients);
    assert.deepEqual(check(product(), brand()), { recommendable: true, reasonCodes: [] });
});

test("missing names, brand, category, ingredients, or unresolved ingredients each fail the same gate", () => {
    const check = createRecommendabilityChecker(ingredients);
    assert.ok(check(product({ name: "" }), brand()).reasonCodes.includes("product-name-missing"));
    assert.ok(check(product(), null).reasonCodes.includes("brand-missing"));
    assert.ok(check(product({ cosmeticType: "" }), brand()).reasonCodes.includes("category-missing"));
    assert.ok(check(product({ ingredients: [] }), brand()).reasonCodes.includes("ingredients-missing"));
    assert.ok(check(product({ ingredients: ["Unlisted Polymer"] }), brand()).reasonCodes.includes("ingredient-unresolved"));
});

test("archived, inactive, or non-makeup brands cannot qualify", () => {
    const check = createRecommendabilityChecker(ingredients);
    assert.ok(check(product(), brand({ binned: true })).reasonCodes.includes("brand-archived"));
    assert.ok(check(product(), brand({ status: "Inactive" })).reasonCodes.includes("brand-inactive"));
    assert.ok(check(product(), brand({ category: "Skincare" })).reasonCodes.includes("brand-category-not-makeup"));
});

test("unreviewed AI and generic ingredient entries are not evaluable", () => {
    const checkAi = createRecommendabilityChecker([{ id: "ai_1", name: "New Ingredient", source: "ai", reviewed: false }]);
    assert.ok(checkAi(product({ ingredients: ["New Ingredient"] }), brand()).reasonCodes.includes("ingredient-not-evaluable"));

    const checkGeneric = createRecommendabilityChecker([{ id: "generic_1", name: "Plant Extract", generic: true }]);
    assert.ok(checkGeneric(product({ ingredients: ["Plant Extract"] }), brand()).reasonCodes.includes("ingredient-not-evaluable"));
});
