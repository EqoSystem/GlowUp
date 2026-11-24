// Automatic ingredient lists for products that arrived without one (24 Nov 2026). Pure helpers; the page (adminBrandInventory.js) does the calls.
// A product waits for a list when it is waiting for review, has no ingredients, no web look-up has been tried for it, and no suggestion is queued.
// The result is NEVER written into the product: it is queued in "Web ingredient lists to review" (productIngredientSuggestions) for an admin.
export const LISTS_PER_LOAD = 3;                    // one call each: the free plan and the 10-per-minute limit of api/product-list.js

export function waitingForList(products, suggestions, isMakeupish) {
    const queued = new Set((suggestions || []).map((s) => s.productId));
    return (products || []).filter((p) => p && !p.binned && p.needsReview && !p.listAiChecked && (p.ingredients || []).length === 0
        && String(p.name || "").trim().length >= 3 && !queued.has(p.id) && (!isMakeupish || isMakeupish(p)));
}

export function suggestionFrom(product, found, uid, nowUnix) {
    return {
        productId: product.id,
        productName: String(product.name || "").slice(0, 100),
        brandName: String(product.brandName || found.brandName || "").slice(0, 60),
        ingredients: (found.ingredients || []).slice(0, 80),
        sources: (found.sources || []).slice(0, 6),
        source: "web-ai", status: "Pending", reviewed: false, addedBy: uid || "", addedAtUnix: nowUnix || Math.floor(Date.now() / 1000)
    };
}
