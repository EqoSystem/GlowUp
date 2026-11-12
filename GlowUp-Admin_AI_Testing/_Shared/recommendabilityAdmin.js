import { db } from "../_Database/firebase-console.js";
import { collection, deleteField, doc, serverTimestamp } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { getDocs, writeBatch } from "./adminStore.js";
import { createRecommendabilityChecker, productVisibility } from "./glowupCatalog.js";

const BATCH_LIMIT = 400;

export async function refreshRecommendabilityFlags(input = {}) {
    const [brandSnapshot, productSnapshot, ingredientSnapshot] = await Promise.all([
        input.brands ? null : getDocs(collection(db, "brands")),
        input.products ? null : getDocs(collection(db, "products")),
        input.ingredients ? null : getDocs(collection(db, "ingredients"))
    ]);
    const brands = input.brands || brandSnapshot.docs.map((snapshot) => ({ id: snapshot.id, ...snapshot.data(), rawStatus: snapshot.data().status }));
    const products = input.products || productSnapshot.docs.map((snapshot) => ({ id: snapshot.id, ...snapshot.data(), rawStatus: snapshot.data().status }));
    const ingredients = input.ingredients || ingredientSnapshot.docs.map((snapshot) => ({ id: snapshot.id, ...snapshot.data() }));
    const brandById = new Map(brands.filter(Boolean).map((brand) => [brand.id, brand]));
    const check = createRecommendabilityChecker(ingredients);
    const byId = new Map();
    const writes = [];
    let eligibleCount = 0;
    let hiddenCount = 0;

    products.forEach((product) => {
        if (!product || !product.id) return;
        const result = check(product, brandById.get(product.brandId));
        byId.set(product.id, result);
        if (result.recommendable) eligibleCount++;
        else if (productVisibility(product, brandById.get(product.brandId)).live) hiddenCount++;

        const removeLegacyFlag = Object.prototype.hasOwnProperty.call(product, "alternativeEligible");
        if (product.recommendable !== result.recommendable || removeLegacyFlag)
            writes.push({ id: product.id, recommendable: result.recommendable, removeLegacyFlag });
    });

    for (let start = 0; start < writes.length; start += BATCH_LIMIT) {
        const batch = writeBatch(db);
        writes.slice(start, start + BATCH_LIMIT).forEach((write) => {
            const changes = { recommendable: write.recommendable, updatedAt: serverTimestamp() };
            if (write.removeLegacyFlag) changes.alternativeEligible = deleteField();
            batch.update(doc(db, "products", write.id), changes);
        });
        await batch.commit();
    }

    return { eligibleCount, hiddenCount, byId, updatedCount: writes.length };
}
