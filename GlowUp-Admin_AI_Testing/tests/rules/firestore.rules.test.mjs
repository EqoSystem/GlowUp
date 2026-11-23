







import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { initializeTestEnvironment } from "@firebase/rules-unit-testing";
import { doc, getDoc, getDocs, setDoc, updateDoc, deleteDoc, collection, query, writeBatch, serverTimestamp, increment, setLogLevel, Timestamp } from "firebase/firestore";

setLogLevel("silent");

const here = path.dirname(fileURLToPath(import.meta.url));
const rules = readFileSync(path.join(here, "..", "..", "firestore.rules"), "utf8");
const [host, port] = (process.env.FIRESTORE_EMULATOR_HOST || "127.0.0.1:8085").split(":");

const env = await initializeTestEnvironment({ projectId: "demo-glowup", firestore: { rules, host, port: Number(port) } });

let checks = 0;
const failures = [];
async function attempt(operation) { try { await operation(); return null; } catch (error) { return error; } }
async function can(what, operation) { checks++; await seed(); const error = await attempt(operation); if (error) failures.push(`should be ALLOWED: ${what}\n      ${String(error.message || error).split("\n")[0]}`); }
async function cannot(what, operation) { checks++; await seed(); const error = await attempt(operation); if (!error) failures.push(`should be REFUSED: ${what}`); }


const anon = () => env.unauthenticatedContext().firestore();
const user = () => env.authenticatedContext("u1").firestore();
const other = () => env.authenticatedContext("u2").firestore();
const adminDoc = () => env.authenticatedContext("a1").firestore();
const adminClaim = () => env.authenticatedContext("c1", { admin: true }).firestore();
const notAdminClaim = () => env.authenticatedContext("c2", { admin: false }).firestore();

const now = Math.floor(Date.now() / 1000);
const day = (() => { const d = new Date(); return String(d.getUTCFullYear() * 10000 + (d.getUTCMonth() + 1) * 100 + d.getUTCDate()); })();

async function seed() {
    await env.clearFirestore();
    await env.withSecurityRulesDisabled(async (context) => {
        const db = context.firestore();
        await setDoc(doc(db, "admins/a1"), { note: "test admin" });
        await setDoc(doc(db, "brands/b1"), { name: "Examplebrand", category: "Makeup", status: "Active", scans: 3 });
        await setDoc(doc(db, "products/p1"), { name: "Example Lipstick", brandId: "b1", cosmeticType: "Lipstick", ingredients: ["Mica"], shades: ["#C24B62"], status: "Active", scans: 5 });
        await setDoc(doc(db, "ingredients/i1"), { name: "Mica", techName: "Mica", description: "A mineral", riskLevel: "safe" });
        await setDoc(doc(db, "config/lists"), { version: 1, allergens: [], conditions: [], highlights: [] });
        await setDoc(doc(db, "ingredientClassifications/other"), { name: "Other" });
        await setDoc(doc(db, "ingredientClassifications/binder"), { name: "Binder" });
        await setDoc(doc(db, "users/u1"), { firstName: "Test" });
        await setDoc(doc(db, "users/u1/scans/s1"), { productName: "x" });
        await setDoc(doc(db, "scanActivity/n1"), { productName: "x", status: "safe", scannedAtUnix: now });
        await setDoc(doc(db, "allergenSuggestions/g1"), { text: "nickel", uid: "u1", status: "pending", createdAtUnix: now });
        await setDoc(doc(db, "pending_products/snap_examplebrand__thing__u1"), { createdByUid: "u1", name: "Thing", brandName: "Examplebrand", isApproved: false });
        await setDoc(doc(db, "pending_product_images/snap_examplebrand__thing__u1"), { createdByUid: "u1", jpegBase64: "AAAA", bytes: 3, createdAtUnix: 1 });
        await setDoc(doc(db, "pending_products/snap_examplebrand__thing__u2"), { createdByUid: "u2", name: "Thing", brandName: "Examplebrand", isApproved: false });
    });
}




for (const name of ["brands/b1", "products/p1", "ingredients/i1", "config/lists"]) {
    await can(`nobody signed in reads ${name}`, () => getDoc(doc(anon(), name)));
    await can(`a plain user reads ${name}`, () => getDoc(doc(user(), name)));
}
await can("nobody signed in lists the products", () => getDocs(query(collection(anon(), "products"))));

const newBrand = { name: "New Brand", category: "Makeup", market: "Global", status: "Active", scans: 0 };
const newProduct = { name: "New Product", brandId: "b1", brandName: "Examplebrand", cosmeticType: "Foundation", ingredients: ["Mica", "Talc"], shades: ["#E0B090"], status: "Active", scans: 0 };
const newIngredient = { name: "Talc", techName: "Talc", description: "A mineral powder" };
for (const [label, who] of [["an admin from the admins list", adminDoc], ["an admin with the admin claim", adminClaim]]) {
    await can(`${label} creates a brand`, () => setDoc(doc(who(), "brands/b2"), newBrand));
    await can(`${label} creates a product`, () => setDoc(doc(who(), "products/p2"), newProduct));
    await can(`${label} creates an ingredient`, () => setDoc(doc(who(), "ingredients/i2"), newIngredient));
    await can(`${label} edits a product`, () => updateDoc(doc(who(), "products/p1"), { name: "Renamed" }));
    await can(`${label} deletes a product`, () => deleteDoc(doc(who(), "products/p1")));
    await can(`${label} writes config/lists`, () => setDoc(doc(who(), "config/lists"), { version: 2, allergens: [], conditions: [], highlights: [] }));
}
for (const [label, who] of [["nobody signed in", anon], ["a plain user", user], ["a user whose claim says admin = false", notAdminClaim]]) {
    await cannot(`${label} creates a brand named by hand`, () => setDoc(doc(who(), "brands/b2"), newBrand));
    await cannot(`${label} creates a product named by hand`, () => setDoc(doc(who(), "products/p2"), newProduct));
    await cannot(`${label} creates an ingredient named by hand`, () => setDoc(doc(who(), "ingredients/i2"), newIngredient));
    await cannot(`${label} renames a product`, () => updateDoc(doc(who(), "products/p1"), { name: "Renamed" }));
    await cannot(`${label} deletes a product`, () => deleteDoc(doc(who(), "products/p1")));
    await cannot(`${label} deletes a brand`, () => deleteDoc(doc(who(), "brands/b1")));
    await cannot(`${label} writes config/lists`, () => setDoc(doc(who(), "config/lists"), { version: 2, allergens: [], conditions: [], highlights: [] }));
}
await cannot("an admin writes a product with a name that is not text", () => setDoc(doc(adminDoc(), "products/p3"), { ...newProduct, name: 42 }));
await cannot("an admin writes a product with an image that is not https", () => setDoc(doc(adminDoc(), "products/p3"), { ...newProduct, imageUrl: "http://example.com/a.png" }));
await cannot("an admin writes a product with 301 ingredients", () => setDoc(doc(adminDoc(), "products/p3"), { ...newProduct, ingredients: Array.from({ length: 301 }, (_, i) => "n" + i) }));
await cannot("an admin sets a brand's website without confirming it", () => setDoc(doc(adminDoc(), "brands/b3"), { ...newBrand, website: "https://example.com/" }));
await can("an admin sets a brand's website and records themselves as the one who confirmed it", () => setDoc(doc(adminDoc(), "brands/b3"), { ...newBrand, website: "https://example.com/", websiteConfirmedBy: "a1" }));
await cannot("config/lists with a field that is not allowed", () => setDoc(doc(adminDoc(), "config/lists"), { version: 2, allergens: [], conditions: [], highlights: [], extra: 1 }));
await cannot("another config document", () => setDoc(doc(adminDoc(), "config/other"), { version: 1, allergens: [], conditions: [], highlights: [] }));
await cannot("even an admin deletes config/lists", () => deleteDoc(doc(adminDoc(), "config/lists")));


await can("nobody signed in reads a classification", () => getDoc(doc(anon(), "ingredientClassifications/other")));
await can("an admin adds a classification", () => setDoc(doc(adminDoc(), "ingredientClassifications/other"), { name: "Other" }));
await can("an admin adds another classification", () => setDoc(doc(adminDoc(), "ingredientClassifications/binder"), { name: "Binder" }));
await can("an admin renames the fallback", () => updateDoc(doc(adminDoc(), "ingredientClassifications/other"), { name: "Everything else" }));
await cannot("even an admin deletes the fallback", () => deleteDoc(doc(adminDoc(), "ingredientClassifications/other")));
await can("an admin deletes another classification", () => deleteDoc(doc(adminDoc(), "ingredientClassifications/binder")));
await cannot("a classification with no name", () => setDoc(doc(adminDoc(), "ingredientClassifications/x"), { name: "" }));
await cannot("a classification name that is too long", () => setDoc(doc(adminDoc(), "ingredientClassifications/x"), { name: "x".repeat(41) }));
await cannot("a classification with a field that is not allowed", () => setDoc(doc(adminDoc(), "ingredientClassifications/x"), { name: "X", extra: 1 }));
await cannot("a user adds a classification", () => setDoc(doc(user(), "ingredientClassifications/x"), { name: "X" }));
await cannot("the app (anyone not an admin) reads the TEST list", () => getDoc(doc(user(), "ingredientClassificationsTest/other")));
await can("an admin writes the TEST list", () => setDoc(doc(adminDoc(), "ingredientClassificationsTest/other"), { name: "Other" }));
await can("an admin reads the TEST list", () => getDoc(doc(adminDoc(), "ingredientClassificationsTest/other")));
await cannot("even an admin deletes the TEST fallback", () => deleteDoc(doc(adminDoc(), "ingredientClassificationsTest/other")));
await cannot("a user writes the TEST list", () => setDoc(doc(user(), "ingredientClassificationsTest/x"), { name: "X" }));
await cannot("nobody signed in adds a classification", () => setDoc(doc(anon(), "ingredientClassifications/x"), { name: "X" }));
await cannot("a user deletes a classification", () => deleteDoc(doc(user(), "ingredientClassifications/other")));




const appBrand = { name: "Seen Brand", category: "Makeup", market: "Global", status: "Pending", scans: 0, source: "scan", reviewed: false, addedBy: "u1", addedAtUnix: now };
const appProduct = { name: "Seen Lipstick", brandId: "scan_seen", brandName: "Seen Brand", cosmeticType: "Lipstick", description: "", ingredients: ["Mica", "Talc"], shades: ["#C24B62"], sources: [],
    minAge: 0, status: "Pending", scans: 0, source: "scan", reviewed: false, addedBy: "u1", addedAtUnix: now, barcode: "4800000000017" };
const appIngredient = { name: "Zinc Stearate", techName: "Zinc Stearate", description: "A white powder", riskLevel: "safe", aliases: [], sources: [], source: "ai", reviewed: false, addedBy: "u1", addedAtUnix: now };

await can("the app adds a Pending brand it met in a scan", () => setDoc(doc(user(), "brands/scan_seen"), appBrand));
await cannot("the app adds a brand that is Active", () => setDoc(doc(user(), "brands/scan_seen"), { ...appBrand, status: "Active" }));
await cannot("the app adds a brand that is marked reviewed", () => setDoc(doc(user(), "brands/scan_seen"), { ...appBrand, reviewed: true }));
await cannot("the app adds a brand under someone else's id", () => setDoc(doc(user(), "brands/scan_seen"), { ...appBrand, addedBy: "u2" }));
await cannot("the app adds a brand whose id does not start with scan_", () => setDoc(doc(user(), "brands/seen"), appBrand));
await cannot("the app adds a brand with a field it may not set", () => setDoc(doc(user(), "brands/scan_seen"), { ...appBrand, website: "https://example.com/" }));
await cannot("the app adds a brand that already has scans", () => setDoc(doc(user(), "brands/scan_seen"), { ...appBrand, scans: 50 }));

await can("the app adds a Pending make-up product", () => setDoc(doc(user(), "products/scan_seen"), appProduct));
await cannot("the app adds a product that is Active", () => setDoc(doc(user(), "products/scan_seen"), { ...appProduct, status: "Active" }));
await cannot("the app adds a hair product (shampoo)", () => setDoc(doc(user(), "products/scan_seen"), { ...appProduct, name: "Silk Shampoo", cosmeticType: "Shampoo" }));
await cannot("the app adds a product without ingredients", () => setDoc(doc(user(), "products/scan_seen"), { ...appProduct, ingredients: [] }));
await cannot("the app adds a product with 81 ingredients", () => setDoc(doc(user(), "products/scan_seen"), { ...appProduct, ingredients: Array.from({ length: 81 }, (_, i) => "n" + i) }));
await cannot("the app adds a product with a barcode that is not digits", () => setDoc(doc(user(), "products/scan_seen"), { ...appProduct, barcode: "abc" }));
await cannot("nobody signed in adds a product", () => setDoc(doc(anon(), "products/scan_seen"), appProduct));

await can("the app adds an AI-found ingredient, not reviewed", () => setDoc(doc(user(), "ingredients/ai_zinc"), appIngredient));
await cannot("the app adds an ingredient marked reviewed", () => setDoc(doc(user(), "ingredients/ai_zinc"), { ...appIngredient, reviewed: true }));
await cannot("the app adds an ingredient with a risk level that does not exist", () => setDoc(doc(user(), "ingredients/ai_zinc"), { ...appIngredient, riskLevel: "toxic" }));
await cannot("the app adds an ingredient whose id does not start with ai_", () => setDoc(doc(user(), "ingredients/zinc"), appIngredient));
await cannot("the app changes an ingredient that exists", () => updateDoc(doc(user(), "ingredients/i1"), { riskLevel: "harmful" }));


await can("the app adds 1 to a product's scans", () => updateDoc(doc(user(), "products/p1"), { scans: increment(1) }));
await can("the app adds 1 to a brand's scans", () => updateDoc(doc(user(), "brands/b1"), { scans: increment(1) }));
await cannot("the app adds 2 to a product's scans", () => updateDoc(doc(user(), "products/p1"), { scans: increment(2) }));
await cannot("the app takes 1 off a product's scans", () => updateDoc(doc(user(), "products/p1"), { scans: increment(-1) }));
await cannot("the app adds 1 to scans and changes the status", () => updateDoc(doc(user(), "products/p1"), { scans: increment(1), status: "Inactive" }));
await cannot("nobody signed in adds 1 to scans", () => updateDoc(doc(anon(), "products/p1"), { scans: increment(1) }));




await can("an admin reads their own admins entry", () => getDoc(doc(adminDoc(), "admins/a1")));
await cannot("a user reads someone else's admins entry", () => getDoc(doc(user(), "admins/a1")));
await cannot("a user makes themselves an admin", () => setDoc(doc(user(), "admins/u1"), { note: "me" }));
await cannot("even an admin writes to admins", () => setDoc(doc(adminDoc(), "admins/x"), { note: "x" }));
await cannot("even an admin by claim writes to admins", () => setDoc(doc(adminClaim(), "admins/x"), { note: "x" }));
await cannot("a collection that is not in the rules (even for an admin)", () => setDoc(doc(adminDoc(), "auditLogs/l1"), { who: "a1" }));
await cannot("reading a collection that is not in the rules", () => getDoc(doc(adminDoc(), "secrets/s1")));




await can("a user reads their own profile", () => getDoc(doc(user(), "users/u1")));
await can("a user writes their own profile", () => setDoc(doc(user(), "users/u1"), { firstName: "Changed" }));
await can("a user reads their own scans", () => getDoc(doc(user(), "users/u1/scans/s1")));
await can("a user writes their own scans", () => setDoc(doc(user(), "users/u1/scans/s2"), { productName: "y" }));
await can("a user deletes their own scan", () => deleteDoc(doc(user(), "users/u1/scans/s1")));
await cannot("another user reads a profile", () => getDoc(doc(other(), "users/u1")));
await cannot("another user writes a profile", () => setDoc(doc(other(), "users/u1"), { firstName: "Hacked" }));
await cannot("another user reads someone's scans", () => getDoc(doc(other(), "users/u1/scans/s1")));
await cannot("nobody signed in reads a profile", () => getDoc(doc(anon(), "users/u1")));
await can("an admin reads a user's profile (User Management, 20 November 2026: the owner chose full access; this was REFUSED before)", () => getDoc(doc(adminDoc(), "users/u1")));
await cannot("an admin reads a user's scans", () => getDoc(doc(adminClaim(), "users/u1/scans/s1")));




const note = { productName: "Example Lipstick", brandId: "b1", brandName: "Examplebrand", cosmeticType: "Lipstick", status: "caution", source: "scan", scannedAtUnix: now,
    ingredientCount: 12, harmfulCount: 0, cautionCount: 2, allergenFlagged: false, flaggedIngredients: ["Alcohol Denat."] };
await can("the app adds a scan note", () => setDoc(doc(user(), "scanActivity/n2"), note));
await cannot("a scan note with a status that does not exist", () => setDoc(doc(user(), "scanActivity/n2"), { ...note, status: "unsafe" }));
await cannot("a scan note with a field it may not have (a user id)", () => setDoc(doc(user(), "scanActivity/n2"), { ...note, uid: "u1" }));
await cannot("a scan note with a product name of 201 characters", () => setDoc(doc(user(), "scanActivity/n2"), { ...note, productName: "x".repeat(201) }));
await cannot("a scan note with 51 flagged names", () => setDoc(doc(user(), "scanActivity/n2"), { ...note, flaggedIngredients: Array.from({ length: 51 }, (_, i) => "n" + i) }));
await cannot("a scan note from nobody signed in", () => setDoc(doc(anon(), "scanActivity/n2"), note));
await cannot("a user reads the scan notes", () => getDoc(doc(user(), "scanActivity/n1")));
await can("an admin reads the scan notes", () => getDoc(doc(adminDoc(), "scanActivity/n1")));
await can("an admin deletes a scan note", () => deleteDoc(doc(adminClaim(), "scanActivity/n1")));

const suggestion = { text: "nickel", uid: "u1", status: "pending", createdAtUnix: now };
await can("a user suggests an allergen under their own id", () => setDoc(doc(user(), "allergenSuggestions/g2"), suggestion));
await cannot("a user suggests an allergen under someone else's id", () => setDoc(doc(user(), "allergenSuggestions/g2"), { ...suggestion, uid: "u2" }));
await cannot("a suggestion that is already 'reviewed'", () => setDoc(doc(user(), "allergenSuggestions/g2"), { ...suggestion, status: "reviewed" }));
await cannot("a suggestion of 101 characters", () => setDoc(doc(user(), "conditionSuggestions/c2"), { ...suggestion, text: "x".repeat(101) }));
await cannot("a user reads the suggestions", () => getDoc(doc(user(), "allergenSuggestions/g1")));
await can("an admin reads the suggestions", () => getDoc(doc(adminDoc(), "allergenSuggestions/g1")));

const proposal = { productId: "p1", productName: "Example Lipstick", brandName: "Examplebrand", ingredients: ["Mica", "Talc", "Silica"], sources: ["https://example.com/p"], source: "web", status: "Pending", reviewed: false, addedBy: "u1", addedAtUnix: now };
await can("the app proposes an ingredient list for a product that has none", () => setDoc(doc(user(), "productIngredientSuggestions/s1"), proposal));
await cannot("a proposal with two ingredients", () => setDoc(doc(user(), "productIngredientSuggestions/s2"), { ...proposal, ingredients: ["Mica", "Talc"] }));
await cannot("a proposal that is already approved", () => setDoc(doc(user(), "productIngredientSuggestions/s2"), { ...proposal, status: "Approved" }));
await cannot("a user reads the proposals", () => getDoc(doc(user(), "productIngredientSuggestions/s1")));
await can("an admin reads the proposals", () => getDoc(doc(adminDoc(), "productIngredientSuggestions/s1")));





await cannot("a user stages a product (the feature is gone)", () => setDoc(doc(user(), "pending_products/snap_newbrand__glow_tint__u1"),
    { name: "Glow Tint", brandName: "Newbrand", cosmeticType: "Foundation", ingredients: [], key: "newbrand__glow_tint__u1", status: "pending", source: "snapshot", isApproved: false, hasImage: false, createdByUid: "u1", createdAtUnix: now }));
await can("a user asks for their own old entry (the live scan's look-up)", () => getDoc(doc(user(), "pending_products/snap_examplebrand__thing__u1")));
await can("a user asks for an entry that does not exist (the answer is 'not found')", () => getDoc(doc(user(), "pending_products/snap_nobody__nothing__u1")));
await cannot("a user reads another user's old entry", () => getDoc(doc(user(), "pending_products/snap_examplebrand__thing__u2")));
await cannot("a user changes their old entry", () => updateDoc(doc(user(), "pending_products/snap_examplebrand__thing__u1"), { isApproved: true }));
await can("an admin lists the old entries", () => getDocs(query(collection(adminClaim(), "pending_products"))));
await can("an admin reads an old photo", () => getDoc(doc(adminDoc(), "pending_product_images/snap_examplebrand__thing__u1")));
await cannot("a user reads their old photo", () => getDoc(doc(user(), "pending_product_images/snap_examplebrand__thing__u1")));
await cannot("a user writes a photo", () => setDoc(doc(user(), "pending_product_images/snap_examplebrand__thing__u1"), { createdByUid: "u1", jpegBase64: "AAAA", bytes: 3, createdAtUnix: 1 }));
await cannot("a user starts a daily photo counter", () => setDoc(doc(user(), "snapshot_quota/u1_20261111"), { uid: "u1", day: "20261111", count: 1, lastAt: serverTimestamp() }));
await can("an admin deletes an old entry", () => deleteDoc(doc(adminDoc(), "pending_products/snap_examplebrand__thing__u2")));




const in30 = () => Timestamp.fromMillis(Date.now() + 30 * 86400000);
const entry = (uid, more = {}) => Object.assign({ type: "product", sourceCollection: "products", sourceId: "p1", name: "Example Lipstick",
    data: { name: "Example Lipstick", binned: true }, deleted_at: serverTimestamp(), expires_at: in30(), deleted_by: uid, deleted_by_email: "a@x.test", archivedWith: "" }, more);
await can("an admin archives a product (archive entry + delete the original, one batch)", async () => {
    const db = adminDoc(), batch = writeBatch(db);
    batch.set(doc(db, "archive/products__p1"), entry("a1"));
    batch.delete(doc(db, "products/p1"));
    await batch.commit();
});
await can("an admin with the claim archives a brand", () => setDoc(doc(adminClaim(), "archive/brands__b1"), entry("c1", { type: "brand", sourceCollection: "brands", sourceId: "b1" })));
await cannot("a plain user archives", () => setDoc(doc(user(), "archive/products__p1"), entry("u1")));
await cannot("nobody signed in archives", () => setDoc(doc(anon(), "archive/products__p1"), entry("x")));
await cannot("an id that does not match the entry", () => setDoc(doc(adminDoc(), "archive/products__other"), entry("a1")));
await can("an admin archives an app report (22 Nov 2026)", () => setDoc(doc(adminDoc(), "archive/supportReports__r1"), entry("a1", { type: "report", sourceCollection: "supportReports", sourceId: "r1", data: { category: "Bug", message: "x" } })));
await cannot("a report entry from another list", () => setDoc(doc(adminDoc(), "archive/products__r1"), entry("a1", { type: "report", sourceCollection: "products", sourceId: "r1" })));
await can("an admin restores a report from the Archive (creates it again)", () => setDoc(doc(adminDoc(), "supportReports/r9"), { uid: "u1", category: "Bug", message: "Back again", status: "new", createdAtUnix: 1 }));
await cannot("a user creates a report in someone else's name", () => setDoc(doc(user(), "supportReports/r8"), { uid: "u2", category: "Bug", message: "Spoof", status: "new" }));
await cannot("a type that does not match the collection", () => setDoc(doc(adminDoc(), "archive/products__p1"), entry("a1", { type: "brand" })));
await cannot("another collection than the three", () => setDoc(doc(adminDoc(), "archive/users__u1"), entry("a1", { type: "user", sourceCollection: "users", sourceId: "u1" })));
await cannot("deleted_by is somebody else", () => setDoc(doc(adminDoc(), "archive/products__p1"), entry("someone-else")));
await cannot("expires in 5 days instead of 30", () => setDoc(doc(adminDoc(), "archive/products__p1"), entry("a1", { expires_at: Timestamp.fromMillis(Date.now() + 5 * 86400000) })));
await cannot("expires in 90 days", () => setDoc(doc(adminDoc(), "archive/products__p1"), entry("a1", { expires_at: Timestamp.fromMillis(Date.now() + 90 * 86400000) })));
await cannot("a client-chosen deleted_at", () => setDoc(doc(adminDoc(), "archive/products__p1"), entry("a1", { deleted_at: Timestamp.fromMillis(Date.now() - 86400000) })));
await cannot("an extra field", () => setDoc(doc(adminDoc(), "archive/products__p1"), entry("a1", { note: "x" })));
const putEntry = () => env.withSecurityRulesDisabled((context) => setDoc(doc(context.firestore(), "archive/products__p1"), entry("a1", { deleted_at: Timestamp.now() })));
await can("an admin reads the Archive", async () => { await putEntry(); await getDocs(query(collection(adminDoc(), "archive"))); });
await cannot("a plain user reads the Archive", async () => { await putEntry(); await getDoc(doc(user(), "archive/products__p1")); });
await cannot("nobody signed in lists the Archive", () => getDocs(query(collection(anon(), "archive"))));
await cannot("a plain user lists the Archive", async () => { await putEntry(); await getDocs(query(collection(user(), "archive"))); });
await cannot("nobody signed in reads an archive entry", async () => { await putEntry(); await getDoc(doc(anon(), "archive/products__p1")); });
await cannot("an admin changes an archive entry", async () => { await putEntry(); await updateDoc(doc(adminDoc(), "archive/products__p1"), { expires_at: in30() }); });
await can("an admin deletes an archive entry (Delete now)", async () => { await putEntry(); await deleteDoc(doc(adminDoc(), "archive/products__p1")); });
await cannot("a plain user deletes an archive entry", async () => { await putEntry(); await deleteDoc(doc(user(), "archive/products__p1")); });
await can("an admin restores: the original back, the entry gone (one batch)", async () => {
    await env.withSecurityRulesDisabled(async (context) => { await deleteDoc(doc(context.firestore(), "products/p1")); });
    await putEntry();
    const db = adminDoc(), batch = writeBatch(db);
    batch.set(doc(db, "products/p1"), { name: "Example Lipstick", brandId: "b1", cosmeticType: "Lipstick", ingredients: ["Mica"], shades: [], status: "Inactive", scans: 5, binned: true });
    batch.delete(doc(db, "archive/products__p1"));
    await batch.commit();
});
await can("an admin reads the purge log", () => getDocs(query(collection(adminDoc(), "archive_purge_log"))));
await cannot("an admin writes the purge log", () => setDoc(doc(adminDoc(), "archive_purge_log/x"), { mode: "live" }));
await cannot("a plain user reads the purge log", () => getDocs(query(collection(user(), "archive_purge_log"))));


await can("an admin applies a source to products in one batch", async () => {
    const db = adminDoc(), batch = writeBatch(db);
    batch.update(doc(db, "products/p1"), { sources: ["Official website | https://example.com"], updatedAt: serverTimestamp() });
    await batch.commit();
});
await cannot("a plain user applies a source", () => updateDoc(doc(user(), "products/p1"), { sources: ["Official website | https://example.com"] }));
await cannot("nobody signed in applies a source", () => updateDoc(doc(anon(), "products/p1"), { sources: ["Official website | https://example.com"] }));
await cannot("an admin applies more sources than the rule takes", () => updateDoc(doc(adminDoc(), "products/p1"), { sources: Array.from({ length: 41 }, (_, i) => "x | https://example.com/" + i) }));


const shared = (uid) => ({ layout: { order: ["kpis"], styles: { kpis: { anim: "fade" } } }, appearance: { font: "inter" }, updatedAt: serverTimestamp(), updatedBy: uid });
await can("an admin makes the dashboard's shared default", () => setDoc(doc(adminDoc(), "siteLayouts/dashboard"), shared("a1")));
await can("an admin reads a shared default", () => getDoc(doc(adminDoc(), "siteLayouts/dashboard")));
await cannot("a plain user reads a shared default", () => getDoc(doc(user(), "siteLayouts/dashboard")));
await cannot("a plain user writes a shared default", () => setDoc(doc(user(), "siteLayouts/dashboard"), shared("u1")));
await cannot("an admin writes one for a page that does not exist", () => setDoc(doc(adminDoc(), "siteLayouts/elsewhere"), shared("a1")));
await cannot("an admin writes it in someone else's name", () => setDoc(doc(adminDoc(), "siteLayouts/dashboard"), shared("u1")));
await cannot("a shared default with an extra field", () => setDoc(doc(adminDoc(), "siteLayouts/dashboard"), Object.assign(shared("a1"), { script: "x" })));
await cannot("a shared default whose layout is not a map", () => setDoc(doc(adminDoc(), "siteLayouts/dashboard"), Object.assign(shared("a1"), { layout: "x" })));


const layouts = (extra = {}) => ({ store: JSON.stringify({ active: "default", profiles: [{ id: "echo1", name: "Echo" }] }), savedAt: 1763370000000, updatedAt: serverTimestamp(), ...extra });
await can("an admin saves their own layouts", () => setDoc(doc(adminDoc(), "adminLayouts/a1"), layouts()));
await can("an admin reads their own layouts", () => getDoc(doc(adminDoc(), "adminLayouts/a1")));
await cannot("an admin reads another admin's layouts", () => getDoc(doc(adminDoc(), "adminLayouts/a2")));
await cannot("an admin writes another admin's layouts", () => setDoc(doc(adminDoc(), "adminLayouts/a2"), layouts()));
await cannot("a plain user saves layouts", () => setDoc(doc(user(), "adminLayouts/u1"), layouts()));
await cannot("nobody signed in reads layouts", () => getDoc(doc(anon(), "adminLayouts/a1")));
await cannot("layouts with an extra field", () => setDoc(doc(adminDoc(), "adminLayouts/a1"), layouts({ script: "x" })));
await cannot("layouts that are not text", () => setDoc(doc(adminDoc(), "adminLayouts/a1"), layouts({ store: { profiles: [] } })));
await cannot("layouts larger than the rule takes", () => setDoc(doc(adminDoc(), "adminLayouts/a1"), layouts({ store: "x".repeat(900001) })));
await cannot("layouts with a made-up save time", () => setDoc(doc(adminDoc(), "adminLayouts/a1"), layouts({ updatedAt: new Date(0) })));


const keys = () => ({ items: { "inventory.addBrand": { keys: "Ctrl+Alt+KeyB", description: "Opens the Add brand form.", enabled: true } }, seeded: ["inventory.addBrand"], updatedAt: serverTimestamp() });
await can("an admin saves their own shortcuts", () => setDoc(doc(adminDoc(), "adminShortcuts/a1"), keys()));
await can("an admin reads their own shortcuts", () => getDoc(doc(adminDoc(), "adminShortcuts/a1")));
await cannot("an admin reads another admin's shortcuts", () => getDoc(doc(adminDoc(), "adminShortcuts/a2")));
await cannot("an admin writes another admin's shortcuts", () => setDoc(doc(adminDoc(), "adminShortcuts/a2"), keys()));

const fresh = () => env.authenticatedContext("u1", { auth_time: Math.floor(Date.now() / 1000) - 30 }).firestore();
const stale = () => env.authenticatedContext("u1", { auth_time: Math.floor(Date.now() / 1000) - 3600 }).firestore();
await can("the owner deletes the profile right after signing in again", () => deleteDoc(doc(fresh(), "users/u1")));
await cannot("the owner deletes the profile on an old sign-in", () => deleteDoc(doc(stale(), "users/u1")));
await cannot("another user deletes the profile", () => deleteDoc(doc(other(), "users/u1")));
await can("the owner still updates the profile on an old sign-in", () => setDoc(doc(stale(), "users/u1"), { firstName: "Changed" }));
await can("the owner deletes a scan (Recent Scans: Delete forever)", () => deleteDoc(doc(stale(), "users/u1/scans/s1")));

const report = (category) => ({ uid: "u1", category, message: "Please add a shade finder", status: "new", appVersion: "1.0", device: "Pixel", createdAtUnix: now });
await can("a report carries the sender's name", () => setDoc(doc(user(), "supportReports/r5"), Object.assign(report("Bug"), { userName: "Mark Azcueta" })));
await cannot("a report with a name that is not text", () => setDoc(doc(user(), "supportReports/r6"), Object.assign(report("Bug"), { userName: 12 })));
await can("a user sends a Suggestion report", () => setDoc(doc(user(), "supportReports/r1"), report("Suggestion")));
await can("a user sends a Bug report", () => setDoc(doc(user(), "supportReports/r2"), report("Bug")));
await cannot("a user sends a report of an unknown kind", () => setDoc(doc(user(), "supportReports/r3"), report("Spam")));
await cannot("a user sends a report in someone else's name", () => setDoc(doc(other(), "supportReports/r4"), report("Suggestion")));
await cannot("a user reads support reports", () => getDoc(doc(user(), "supportReports/r1")));

await cannot("a plain user saves shortcuts (the 403 of the endpoint)", () => setDoc(doc(user(), "adminShortcuts/u1"), keys()));
await cannot("a plain user reads shortcuts", () => getDoc(doc(user(), "adminShortcuts/u1")));
await cannot("nobody signed in reads shortcuts", () => getDoc(doc(anon(), "adminShortcuts/a1")));
await cannot("shortcuts with an extra field", () => setDoc(doc(adminDoc(), "adminShortcuts/a1"), Object.assign(keys(), { script: "x" })));
await cannot("shortcuts whose items are not a map", () => setDoc(doc(adminDoc(), "adminShortcuts/a1"), Object.assign(keys(), { items: "x" })));

// ---- admin sign-up (20 November 2026): a request, approved by an existing admin ----
const asker = (verified = false, email = "new@example.com") => env.authenticatedContext("n1", { email, email_verified: verified }).firestore();
const askerOther = () => env.authenticatedContext("n2", { email: "two@example.com", email_verified: true }).firestore();
const request = (extra) => Object.assign({ name: "New Admin", email: "new@example.com", status: "pending", emailVerified: false, createdAt: serverTimestamp() }, extra || {});
const seedRequest = async (data) => {
    await seed();
    await env.withSecurityRulesDisabled(async (context) => { await setDoc(doc(context.firestore(), "adminRequests/n1"), Object.assign({ name: "New Admin", email: "new@example.com", status: "pending", emailVerified: false, createdAt: Timestamp.now() }, data || {})); });
};
async function canAfter(what, prepare, operation) { checks++; await prepare(); const error = await attempt(operation); if (error) failures.push(`should be ALLOWED: ${what}\n      ${String(error.message || error).split("\n")[0]}`); }
async function cannotAfter(what, prepare, operation) { checks++; await prepare(); const error = await attempt(operation); if (!error) failures.push(`should be REFUSED: ${what}`); }
const approve = (db, extraAdmin, extraRequest) => {
    const batch = writeBatch(db);
    batch.set(doc(db, "admins/n1"), Object.assign({ email: "new@example.com", name: "New Admin", approvedBy: "a1", approvedAt: serverTimestamp() }, extraAdmin || {}));
    batch.update(doc(db, "adminRequests/n1"), Object.assign({ status: "approved", decidedBy: "a1", decidedAt: serverTimestamp() }, extraRequest || {}));
    return batch.commit();
};

await can("a new account sends its own admin request", () => setDoc(doc(asker(), "adminRequests/n1"), request()));
await can("a confirmed account sends a request marked confirmed", () => setDoc(doc(asker(true), "adminRequests/n1"), request({ emailVerified: true })));
await cannot("a request marked confirmed while the e-mail is not", () => setDoc(doc(asker(false), "adminRequests/n1"), request({ emailVerified: true })));
await cannot("a request for someone else's account", () => setDoc(doc(askerOther(), "adminRequests/n1"), request()));
await cannot("a request with another e-mail than the account's", () => setDoc(doc(asker(), "adminRequests/n1"), request({ email: "boss@example.com" })));
await cannot("a request that is already approved", () => setDoc(doc(asker(), "adminRequests/n1"), request({ status: "approved" })));
await cannot("a request with an extra field", () => setDoc(doc(asker(), "adminRequests/n1"), request({ role: "owner" })));
await cannot("a request with a one-letter name", () => setDoc(doc(asker(), "adminRequests/n1"), request({ name: "A" })));
await cannot("a request with a made-up time", () => setDoc(doc(asker(), "adminRequests/n1"), request({ createdAt: new Date(0) })));
await cannot("nobody signed in sends a request", () => setDoc(doc(anon(), "adminRequests/n1"), request()));
await cannot("an admin sends a request for themself", () => setDoc(doc(env.authenticatedContext("a1", { email: "new@example.com" }).firestore(), "adminRequests/a1"), request()));
await canAfter("the owner reads their own request", seedRequest, () => getDoc(doc(asker(), "adminRequests/n1")));
await cannotAfter("another account reads the request", seedRequest, () => getDoc(doc(askerOther(), "adminRequests/n1")));
await cannotAfter("a plain user lists requests", seedRequest, () => getDocs(collection(asker(), "adminRequests")));
await canAfter("an admin lists requests", seedRequest, () => getDocs(collection(adminDoc(), "adminRequests")));
await canAfter("the owner marks the e-mail confirmed once it is", seedRequest, () => updateDoc(doc(asker(true), "adminRequests/n1"), { emailVerified: true }));
await cannotAfter("the owner marks it confirmed while it is not", seedRequest, () => updateDoc(doc(asker(false), "adminRequests/n1"), { emailVerified: true }));
await cannotAfter("the owner approves their own request", seedRequest, () => updateDoc(doc(asker(true), "adminRequests/n1"), { status: "approved" }));
await cannotAfter("the owner adds themself to admins", () => seedRequest({ emailVerified: true }), () => setDoc(doc(asker(true), "admins/n1"), { email: "new@example.com", name: "New Admin", approvedBy: "n1", approvedAt: serverTimestamp() }));
await canAfter("the owner withdraws their request", seedRequest, () => deleteDoc(doc(asker(), "adminRequests/n1")));
await canAfter("an admin approves a confirmed request (admin added + request approved in one batch)", () => seedRequest({ emailVerified: true }), () => approve(adminDoc()));
await cannotAfter("an admin approves a request whose e-mail is not confirmed", seedRequest, () => approve(adminDoc()));
await cannotAfter("an admin adds an admin with no request", seed, () => setDoc(doc(adminDoc(), "admins/n1"), { email: "new@example.com", name: "New Admin", approvedBy: "a1", approvedAt: serverTimestamp() }));
await cannotAfter("an admin adds an admin with another e-mail than the request", () => seedRequest({ emailVerified: true }), () => approve(adminDoc(), { email: "x@example.com" }));
await cannotAfter("an approval in someone else's name", () => seedRequest({ emailVerified: true }), () => approve(adminDoc(), { approvedBy: "a9" }, { decidedBy: "a9" }));
await cannotAfter("an admin marks a request approved without adding the admin", () => seedRequest({ emailVerified: true }), () => updateDoc(doc(adminDoc(), "adminRequests/n1"), { status: "approved", decidedBy: "a1", decidedAt: serverTimestamp() }));
await cannotAfter("an approval of an already approved request", () => seedRequest({ emailVerified: true, status: "approved" }), () => approve(adminDoc()));
await canAfter("an admin declines a request (deletes it)", seedRequest, () => deleteDoc(doc(adminDoc(), "adminRequests/n1")));
await cannotAfter("a plain user deletes someone else's request", seedRequest, () => deleteDoc(doc(askerOther(), "adminRequests/n1")));
await cannotAfter("an admin removes another admin from the website", seed, () => deleteDoc(doc(adminDoc(), "admins/a1")));
await cannotAfter("a request when the account is already an admin", async () => { await seed(); await env.withSecurityRulesDisabled(async (c) => setDoc(doc(c.firestore(), "admins/n1"), { note: "x" })); }, () => setDoc(doc(asker(), "adminRequests/n1"), request()));

// ---- Admin Management (22 Nov 2026): admins look after other admins ----
const seedAdmins = async (extra) => {
    await seed();
    await env.withSecurityRulesDisabled(async (context) => {
        const d = context.firestore();
        await setDoc(doc(d, "admins/m1"), Object.assign({ email: "m1@example.com", name: "Second Admin", approvedBy: "a1", approvedAt: Timestamp.now() }, extra || {}));
        await setDoc(doc(d, "admins/m2"), { email: "m2@example.com", name: "Owner", approvedBy: "a1", approvedAt: Timestamp.now(), protected: true });
    });
};
const archivePatch = (who, to) => ({ status: to, inBin: to === "deleted", statusChangedAt: serverTimestamp(), statusChangedBy: who });
await canAfter("an admin lists the admins", seedAdmins, () => getDocs(query(collection(adminDoc(), "admins"))));
await cannotAfter("a plain user lists the admins", seedAdmins, () => getDocs(query(collection(user(), "admins"))));
await canAfter("an admin archives another admin", seedAdmins, () => updateDoc(doc(adminDoc(), "admins/m1"), archivePatch("a1", "archived")));
await canAfter("an admin sends another admin to the bin", seedAdmins, () => updateDoc(doc(adminDoc(), "admins/m1"), Object.assign(archivePatch("a1", "deleted"), { statusBefore: "active" })));
await canAfter("an admin restores another admin", () => seedAdmins({ status: "archived" }), () => updateDoc(doc(adminDoc(), "admins/m1"), archivePatch("a1", "active")));
await canAfter("an admin renames another admin", seedAdmins, () => updateDoc(doc(adminDoc(), "admins/m1"), { name: "Renamed Admin" }));
await cannotAfter("an admin changes their OWN status", seedAdmins, () => updateDoc(doc(adminDoc(), "admins/a1"), archivePatch("a1", "archived")));
await cannotAfter("an admin changes another admin's e-mail", seedAdmins, () => updateDoc(doc(adminDoc(), "admins/m1"), { email: "x@example.com" }));
await cannotAfter("an admin sets a status that does not exist", seedAdmins, () => updateDoc(doc(adminDoc(), "admins/m1"), archivePatch("a1", "banned")));
await cannotAfter("a status change that names another admin as the one who did it", seedAdmins, () => updateDoc(doc(adminDoc(), "admins/m1"), archivePatch("m1", "archived")));
await cannotAfter("a status change with a made-up time", seedAdmins, () => updateDoc(doc(adminDoc(), "admins/m1"), { status: "archived", inBin: false, statusChangedAt: Timestamp.fromMillis(1000), statusChangedBy: "a1" }));
await cannotAfter("an admin adds a protected flag to another admin", seedAdmins, () => updateDoc(doc(adminDoc(), "admins/m1"), { protected: false }));
await cannotAfter("an admin changes a protected admin", seedAdmins, () => updateDoc(doc(adminDoc(), "admins/m2"), archivePatch("a1", "archived")));
await cannotAfter("a plain user changes an admin", seedAdmins, () => updateDoc(doc(user(), "admins/m1"), archivePatch("u1", "archived")));
await cannotAfter("an archived admin cannot change brands", () => seedAdmins().then(() => env.withSecurityRulesDisabled(async (c) => updateDoc(doc(c.firestore(), "admins/a1"), { status: "archived" }))), () => setDoc(doc(adminDoc(), "brands/b9"), newBrand));
await cannotAfter("an archived admin cannot change another admin", () => seedAdmins().then(() => env.withSecurityRulesDisabled(async (c) => updateDoc(doc(c.firestore(), "admins/a1"), { status: "deleted" }))), () => updateDoc(doc(adminDoc(), "admins/m1"), archivePatch("a1", "archived")));
await canAfter("an archived admin still reads their own admins entry", () => seedAdmins().then(() => env.withSecurityRulesDisabled(async (c) => updateDoc(doc(c.firestore(), "admins/a1"), { status: "archived" }))), () => getDoc(doc(adminDoc(), "admins/a1")));
await canAfter("an admin deletes an admin that is in the bin", () => seedAdmins({ status: "deleted", inBin: true }), () => deleteDoc(doc(adminDoc(), "admins/m1")));
await cannotAfter("an admin deletes an active admin", seedAdmins, () => deleteDoc(doc(adminDoc(), "admins/m1")));
await cannotAfter("an admin deletes an archived admin", () => seedAdmins({ status: "archived" }), () => deleteDoc(doc(adminDoc(), "admins/m1")));
await cannotAfter("an admin deletes themself", () => seedAdmins().then(() => env.withSecurityRulesDisabled(async (c) => updateDoc(doc(c.firestore(), "admins/a1"), { status: "deleted" }))), () => deleteDoc(doc(adminDoc(), "admins/a1")));
await cannotAfter("an admin deletes a protected admin in the bin", async () => { await seedAdmins(); await env.withSecurityRulesDisabled(async (c) => updateDoc(doc(c.firestore(), "admins/m2"), { status: "deleted" })); }, () => deleteDoc(doc(adminDoc(), "admins/m2")));
await canAfter("an admin marks their own last-seen time", seedAdmins, () => updateDoc(doc(adminDoc(), "admins/a1"), { lastActiveAt: serverTimestamp() }));
await cannotAfter("an admin marks a made-up last-seen time", seedAdmins, () => updateDoc(doc(adminDoc(), "admins/a1"), { lastActiveAt: Timestamp.fromMillis(1000) }));
await cannotAfter("an admin writes another field on their own entry", seedAdmins, () => updateDoc(doc(adminDoc(), "admins/a1"), { lastActiveAt: serverTimestamp(), name: "Me" }));
await cannotAfter("an admin marks another admin's last-seen time", seedAdmins, () => updateDoc(doc(adminDoc(), "admins/m1"), { lastActiveAt: serverTimestamp() }));
await cannotAfter("a plain user marks a last-seen time on an admin entry", seedAdmins, () => updateDoc(doc(user(), "admins/m1"), { lastActiveAt: serverTimestamp() }));

// ---- User Management (20 November 2026): admins read every profile and change only the managed fields ----
const seedUsers = async (extra) => {
    await seed();
    await env.withSecurityRulesDisabled(async (context) => {
        const db = context.firestore();
        await setDoc(doc(db, "users/u2"), Object.assign({ fullName: "Other User", email: "other@example.com", skinType: "Dry", createdAt: Timestamp.now() }, extra || {}));
        await setDoc(doc(db, "users/a1"), { fullName: "Admin Self", email: "admin@example.com" });
    });
};
const bin = (extra) => Object.assign({ status: "deleted", inBin: true, statusBefore: "active", statusChangedAt: serverTimestamp(), statusChangedBy: "a1" }, extra || {});
await canAfter("an admin reads another user's profile", seedUsers, () => getDoc(doc(adminDoc(), "users/u2")));
await canAfter("an admin lists all users", seedUsers, () => getDocs(collection(adminDoc(), "users")));
await cannotAfter("a plain user reads another user's profile", seedUsers, () => getDoc(doc(user(), "users/u2")));
await cannotAfter("a plain user lists the users", seedUsers, () => getDocs(collection(user(), "users")));
await cannotAfter("an admin reads a user's scans", seedUsers, () => getDoc(doc(adminDoc(), "users/u1/scans/s1")));
await canAfter("an admin archives a user", seedUsers, () => updateDoc(doc(adminDoc(), "users/u2"), { status: "archived", inBin: false, statusChangedAt: serverTimestamp(), statusChangedBy: "a1" }));
await canAfter("an admin moves a user to the Recycle Bin", seedUsers, () => updateDoc(doc(adminDoc(), "users/u2"), bin()));
await canAfter("an admin edits name, username and role", seedUsers, () => updateDoc(doc(adminDoc(), "users/u2"), { fullName: "New Name", username: "newname", role: "admin" }));
await cannotAfter("an admin changes a user's e-mail", seedUsers, () => updateDoc(doc(adminDoc(), "users/u2"), { email: "x@example.com" }));
await cannotAfter("an admin changes a user's skin profile", seedUsers, () => updateDoc(doc(adminDoc(), "users/u2"), { skinType: "Oily" }));
await cannotAfter("an admin sets an unknown status", seedUsers, () => updateDoc(doc(adminDoc(), "users/u2"), { status: "vip" }));
await cannotAfter("an admin sets an unknown role", seedUsers, () => updateDoc(doc(adminDoc(), "users/u2"), { role: "owner" }));
await cannotAfter("an admin records another admin as the one who changed it", seedUsers, () => updateDoc(doc(adminDoc(), "users/u2"), { status: "archived", statusChangedBy: "a9" }));
await cannotAfter("an admin changes their own account here", seedUsers, () => updateDoc(doc(adminDoc(), "users/a1"), { status: "archived" }));
await cannotAfter("an admin changes a user with a name 200 characters long", seedUsers, () => updateDoc(doc(adminDoc(), "users/u2"), { fullName: "x".repeat(200) }));
await cannotAfter("a plain user changes another user", seedUsers, () => updateDoc(doc(other(), "users/u2"), { status: "archived" }));
await canAfter("an admin deletes a user that is in the Recycle Bin", () => seedUsers(bin({ statusChangedAt: Timestamp.now() })), () => deleteDoc(doc(adminDoc(), "users/u2")));
await cannotAfter("an admin deletes a user that is not in the Recycle Bin", seedUsers, () => deleteDoc(doc(adminDoc(), "users/u2")));
await canAfter("a user still updates their own profile (the app's merge)", seedUsers, () => setDoc(doc(user(), "users/u1"), { skinType: "Oily", skinTone: "Medium" }, { merge: true }));
await cannotAfter("a user sets their own status", seedUsers, () => setDoc(doc(user(), "users/u1"), { status: "active" }, { merge: true }));
await cannotAfter("a user sets their own role", seedUsers, () => setDoc(doc(user(), "users/u1"), { role: "admin" }, { merge: true }));
await cannotAfter("a user creates a profile that already carries a status", () => env.clearFirestore(), () => setDoc(doc(env.authenticatedContext("u7").firestore(), "users/u7"), { firstName: "New", status: "active" }));
await canAfter("a user creates a plain profile", () => env.clearFirestore(), () => setDoc(doc(env.authenticatedContext("u7").firestore(), "users/u7"), { firstName: "New", email: "n@example.com" }));
const banUser = async () => { await seedUsers(); await env.withSecurityRulesDisabled(async (c) => setDoc(doc(c.firestore(), "users/u1"), { firstName: "Test", status: "banned" })); };
await cannotAfter("a banned user changes their own profile", banUser, () => setDoc(doc(user(), "users/u1"), { skinType: "Oily" }, { merge: true }));
await canAfter("a banned user can still read their own profile", banUser, () => getDoc(doc(user(), "users/u1")));
await cannotAfter("a banned user unbans themself", banUser, () => setDoc(doc(user(), "users/u1"), { status: "active" }, { merge: true }));
await canAfter("an admin unbans a user", banUser, () => updateDoc(doc(adminDoc(), "users/u1"), { status: "active", inBin: false, statusChangedAt: serverTimestamp(), statusChangedBy: "a1" }));
await canAfter("an admin saves the shared layout of the users page", seed, () => setDoc(doc(adminDoc(), "siteLayouts/users"), { layout: {}, appearance: {}, updatedAt: serverTimestamp(), updatedBy: "a1" }));

// ---- New22 (20 November 2026): replies to users, announcements, Maintenance Mode ----
const replyNote = (extra) => Object.assign({ kind: "reply", title: "Reply to your bug report", body: "Fixed in the next version.", reportId: "r1", read: false, createdAt: serverTimestamp(), createdBy: "a1" }, extra || {});
await can("an admin sends a reply to a user", () => setDoc(doc(adminDoc(), "users/u1/notifications/n1"), replyNote()));
await cannot("a plain user writes a notification to another user", () => setDoc(doc(other(), "users/u1/notifications/n1"), replyNote({ createdBy: "u2" })));
await cannot("a user writes a notification to themself", () => setDoc(doc(user(), "users/u1/notifications/n1"), replyNote({ createdBy: "u1" })));
await cannot("a reply 600 characters long", () => setDoc(doc(adminDoc(), "users/u1/notifications/n1"), replyNote({ body: "x".repeat(600) })));
await cannot("a reply in another admin's name", () => setDoc(doc(adminDoc(), "users/u1/notifications/n1"), replyNote({ createdBy: "a9" })));
const seedNote = async () => { await seed(); await env.withSecurityRulesDisabled(async (c) => setDoc(doc(c.firestore(), "users/u1/notifications/n1"), { kind: "reply", title: "t", body: "b", read: false, createdAt: Timestamp.now(), createdBy: "a1" })); };
await canAfter("the user reads their notifications", seedNote, () => getDocs(collection(user(), "users/u1/notifications")));
await canAfter("the user marks one read", seedNote, () => updateDoc(doc(user(), "users/u1/notifications/n1"), { read: true }));
await cannotAfter("the user changes the text of one", seedNote, () => updateDoc(doc(user(), "users/u1/notifications/n1"), { body: "changed" }));
await canAfter("the user deletes one", seedNote, () => deleteDoc(doc(user(), "users/u1/notifications/n1")));
await cannotAfter("another user reads them", seedNote, () => getDocs(collection(other(), "users/u1/notifications")));
// New23: announcements are the app's change notices (a saved product or ingredient); Maintenance Mode was removed, so systemConfig is closed
const ann = (extra) => Object.assign({ kind: "ingredient", title: "Ingredient updated", body: "Niacinamide was updated.", createdAt: serverTimestamp(), createdBy: "a1" }, extra || {});
await can("an admin posts a change notice for an ingredient", () => setDoc(doc(adminDoc(), "announcements/x1"), ann()));
await can("an admin posts a change notice for a product", () => setDoc(doc(adminDoc(), "announcements/x1"), ann({ kind: "product", title: "Product updated" })));
await cannot("a plain user posts a change notice", () => setDoc(doc(user(), "announcements/x1"), ann({ createdBy: "u1" })));
await cannot("a notice of an unknown kind", () => setDoc(doc(adminDoc(), "announcements/x1"), ann({ kind: "maintenance" })));
await cannot("a notice with an extra field", () => setDoc(doc(adminDoc(), "announcements/x1"), ann({ startsAt: Timestamp.fromMillis(Date.now()) })));
await cannot("a notice title over 80 characters", () => setDoc(doc(adminDoc(), "announcements/x1"), ann({ title: "x".repeat(81) })));
await cannot("a notice text over 300 characters", () => setDoc(doc(adminDoc(), "announcements/x1"), ann({ body: "x".repeat(301) })));
await canAfter("a signed-in user reads notices", seed, () => getDocs(collection(user(), "announcements")));
await cannotAfter("nobody signed in reads notices", seed, () => getDocs(collection(anon(), "announcements")));
await cannot("an admin writes systemConfig (removed)", () => setDoc(doc(adminDoc(), "systemConfig/maintenanceMode"), { isActive: true }));
await cannotAfter("nobody reads systemConfig (removed)", seed, () => getDoc(doc(anon(), "systemConfig/maintenanceMode")));

await canAfter("a user saves their profile photo", seedUsers, () => setDoc(doc(user(), "users/u1"), { photoData: "data:image/jpeg;base64,/9j/4AAQSkZJRgABAQ==" }, { merge: true }));
await canAfter("a user marks themself active (Online now, 22 Nov 2026)", seedUsers, () => setDoc(doc(user(), "users/u1"), { lastActiveAt: serverTimestamp() }, { merge: true }));
await cannotAfter("a user marks someone else active", seedUsers, () => setDoc(doc(other(), "users/u1"), { lastActiveAt: serverTimestamp() }, { merge: true }));
await cannotAfter("a profile photo that is not a JPEG", seedUsers, () => setDoc(doc(user(), "users/u1"), { photoData: "data:image/svg+xml;base64,PHN2Zz4=" }, { merge: true }));
await cannotAfter("a profile photo over 200 KB", seedUsers, () => setDoc(doc(user(), "users/u1"), { photoData: "data:image/jpeg;base64," + "A".repeat(200100) }, { merge: true }));
await cannotAfter("a profile photo labelled JPEG whose content is not a JPEG (New23)", seedUsers, () => setDoc(doc(user(), "users/u1"), { photoData: "data:image/jpeg;base64,PHN2ZyBvbmxvYWQ9YWxlcnQoMSk+" }, { merge: true }));
await cannotAfter("a user sets another user's photo", seedUsers, () => setDoc(doc(other(), "users/u1"), { photoData: "data:image/jpeg;base64,AAAA" }, { merge: true }));

// age check (panel revision 22 Nov 2026)
await can("a user saves age 13 and a birthday", () => setDoc(doc(user(), "users/u1"), { age: 13, birthday: "2013-01-05" }, { merge: true }));
await can("a user saves age 0 (not filled in yet)", () => setDoc(doc(user(), "users/u1"), { age: 0, birthday: "" }, { merge: true }));
await cannot("a user saves age 12", () => setDoc(doc(user(), "users/u1"), { age: 12 }, { merge: true }));
await cannot("a user saves age 150", () => setDoc(doc(user(), "users/u1"), { age: 150 }, { merge: true }));
await cannot("a user saves an age that is not a number", () => setDoc(doc(user(), "users/u1"), { age: "20" }, { merge: true }));
await cannot("a user saves a birthday in another format", () => setDoc(doc(user(), "users/u1"), { birthday: "05/01/2000" }, { merge: true }));
await cannot("a new profile with age 10", () => setDoc(doc(other(), "users/u2"), { firstName: "Kid", age: 10 }));
await can("a new profile with age 25", () => setDoc(doc(other(), "users/u2"), { firstName: "Adult", age: 25, birthday: "2001-03-04" }));

// inbox state in the account (users/{uid}/inboxState/state)
await can("a user saves which announcements they read", () => setDoc(doc(user(), "users/u1/inboxState/state"), { read: ["a1", "a2"] }, { merge: true }));
await can("a user saves which announcements they removed", () => setDoc(doc(user(), "users/u1/inboxState/state"), { hidden: ["a1"], read: ["a2"] }));
await cannot("a user saves inbox state under another name", () => setDoc(doc(user(), "users/u1/inboxState/other"), { read: ["a1"] }));
await cannot("a user saves inbox state with an extra field", () => setDoc(doc(user(), "users/u1/inboxState/state"), { read: [], note: "x" }));
await cannot("a user saves 301 ids", () => setDoc(doc(user(), "users/u1/inboxState/state"), { read: Array.from({ length: 301 }, (_, i) => "i" + i) }));
await cannot("another user writes someone's inbox state", () => setDoc(doc(other(), "users/u1/inboxState/state"), { read: ["a1"] }));
await cannot("another user reads someone's inbox state", () => getDoc(doc(other(), "users/u1/inboxState/state")));
await can("a user reads their own inbox state", () => getDoc(doc(user(), "users/u1/inboxState/state")));

// the shared log-in lock (loginLocks): works with nobody signed in
const LK = "a".repeat(64);
const lockAt = (extra) => async () => { await seed(); await env.withSecurityRulesDisabled(async (c) => { await setDoc(doc(c.firestore(), "loginLocks/" + LK), Object.assign({ fails: 1, untilUnix: 0, updatedAt: Timestamp.now() }, extra || {})); }); };
const lk = (fails, untilUnix) => ({ fails, untilUnix, updatedAt: serverTimestamp() });
const inFive = () => Math.floor(Date.now() / 1000) + 300;
await can("the first wrong password is counted (nobody signed in)", () => setDoc(doc(anon(), "loginLocks/" + LK), lk(1, 0)));
await cannot("a first count that already locks", () => setDoc(doc(anon(), "loginLocks/" + LK), lk(3, inFive())));
await cannot("a lock under a short key", () => setDoc(doc(anon(), "loginLocks/abc"), lk(1, 0)));
await cannot("a lock with an extra field", () => setDoc(doc(anon(), "loginLocks/" + LK), Object.assign(lk(1, 0), { note: "x" })));
await canAfter("the second wrong password is counted", lockAt({ fails: 1 }), () => setDoc(doc(anon(), "loginLocks/" + LK), lk(2, 0)));
await canAfter("the third wrong password sets a five-minute lock", lockAt({ fails: 2 }), () => setDoc(doc(anon(), "loginLocks/" + LK), lk(3, inFive())));
await cannotAfter("the third wrong password with a one-minute lock", lockAt({ fails: 2 }), () => setDoc(doc(anon(), "loginLocks/" + LK), lk(3, Math.floor(Date.now() / 1000) + 60)));
await cannotAfter("the third wrong password with a one-day lock", lockAt({ fails: 2 }), () => setDoc(doc(anon(), "loginLocks/" + LK), lk(3, Math.floor(Date.now() / 1000) + 86400)));
await cannotAfter("a count that skips a step", lockAt({ fails: 1 }), () => setDoc(doc(anon(), "loginLocks/" + LK), lk(3, inFive())));
await cannotAfter("a count that goes down", lockAt({ fails: 2 }), () => setDoc(doc(anon(), "loginLocks/" + LK), lk(1, 0)));
await cannotAfter("restarting a lock that has not ended", lockAt({ fails: 3, untilUnix: inFive() }), () => setDoc(doc(anon(), "loginLocks/" + LK), lk(1, 0)));
await canAfter("restarting a lock that has ended", lockAt({ fails: 3, untilUnix: Math.floor(Date.now() / 1000) - 10 }), () => setDoc(doc(anon(), "loginLocks/" + LK), lk(1, 0)));
await canAfter("anyone reads a lock by its key", lockAt({ fails: 3, untilUnix: inFive() }), () => getDoc(doc(anon(), "loginLocks/" + LK)));
await cannotAfter("nobody lists the locks", lockAt(), () => getDocs(collection(anon(), "loginLocks")));
await cannotAfter("nobody signed in removes a lock", lockAt(), () => deleteDoc(doc(anon(), "loginLocks/" + LK)));
await canAfter("a signed-in person removes their lock after a right password", lockAt(), () => deleteDoc(doc(user(), "loginLocks/" + LK)));

// "Use my phone" links (phoneLinks)
const link = (extra) => Object.assign({ uid: "a1", what: "barcode", kind: "", value: "", count: 0, createdAt: serverTimestamp(), expiresAtUnix: Math.floor(Date.now() / 1000) + 900 }, extra || {});
const seedLink = (extra) => async () => { await seed(); await env.withSecurityRulesDisabled(async (c) => { await setDoc(doc(c.firestore(), "phoneLinks/abcdefghjk23"), Object.assign({ uid: "a1", what: "barcode", kind: "", value: "", count: 0, expiresAtUnix: Math.floor(Date.now() / 1000) + 900 }, extra || {})); }); };
await can("an admin opens a phone link", () => setDoc(doc(adminDoc(), "phoneLinks/abcdefghjk23"), link()));
await cannot("a user opens a phone link", () => setDoc(doc(user(), "phoneLinks/abcdefghjk23"), link({ uid: "u1" })));
await cannot("an admin opens a link in another admin's name", () => setDoc(doc(adminDoc(), "phoneLinks/abcdefghjk23"), link({ uid: "a9" })));
await cannot("a link that lasts an hour", () => setDoc(doc(adminDoc(), "phoneLinks/abcdefghjk23"), link({ expiresAtUnix: Math.floor(Date.now() / 1000) + 3600 })));
await cannot("a link made with a value already in it", () => setDoc(doc(adminDoc(), "phoneLinks/abcdefghjk23"), link({ value: "123" })));
await cannot("a link with a short guessable code", () => setDoc(doc(adminDoc(), "phoneLinks/abc"), link()));
await canAfter("the admin's phone sends a barcode", seedLink(), () => updateDoc(doc(adminDoc(), "phoneLinks/abcdefghjk23"), { kind: "barcode", value: "3600531652300", count: 1, sentAt: serverTimestamp() }));
await canAfter("the admin's phone says it is connected", seedLink(), () => updateDoc(doc(adminDoc(), "phoneLinks/abcdefghjk23"), { phoneSeen: true }));
await cannotAfter("the phone sends after the link ended", seedLink({ expiresAtUnix: Math.floor(Date.now() / 1000) - 5 }), () => updateDoc(doc(adminDoc(), "phoneLinks/abcdefghjk23"), { kind: "barcode", value: "1", count: 1, sentAt: serverTimestamp() }));
await cannotAfter("the phone skips the count", seedLink(), () => updateDoc(doc(adminDoc(), "phoneLinks/abcdefghjk23"), { kind: "barcode", value: "1", count: 5, sentAt: serverTimestamp() }));
await cannotAfter("the phone changes the owner", seedLink(), () => updateDoc(doc(adminDoc(), "phoneLinks/abcdefghjk23"), { uid: "a9" }));
await cannotAfter("a user reads an admin's link", seedLink(), () => getDoc(doc(user(), "phoneLinks/abcdefghjk23")));
await cannotAfter("a user writes into an admin's link", seedLink(), () => updateDoc(doc(user(), "phoneLinks/abcdefghjk23"), { kind: "barcode", value: "1", count: 1, sentAt: serverTimestamp() }));
await canAfter("the admin reads and deletes their link", seedLink(), async () => { await getDoc(doc(adminDoc(), "phoneLinks/abcdefghjk23")); await deleteDoc(doc(adminDoc(), "phoneLinks/abcdefghjk23")); });
await cannotAfter("nobody lists the links", seedLink(), () => getDocs(collection(adminDoc(), "phoneLinks")));

// Staff capture from the mobile app (23 Nov 2026): the submission is a Pending product; its photos go to captureEvidence
const capture = (extra = {}) => Object.assign({ name: "Glow Tint", brandId: "", brandName: "Testbrand", cosmeticType: "Lip Tint", ingredients: ["Aqua", "Mica"],
    shades: [], sources: [], tags: ["staff-capture"], status: "Pending", reviewed: false, source: "staff-capture", addedBy: "a1", addedAtUnix: 1, scans: 0,
    fieldOrigins: { name: { state: "detected", from: "Open Beauty Facts", method: "barcode", atUnix: 1 } }, barcode: "0012345678905", captureId: "cap_a1aaaa_1" }, extra);
const evidence = (extra = {}) => Object.assign({ by: "a1", atUnix: 1, productId: "cap_a1aaaa_1", photos: [{ role: "Front", jpeg: "QUJD" }] }, extra);
await can("an admin's phone submits a staff capture (Pending)", () => setDoc(doc(adminDoc(), "products/cap_a1aaaa_1"), capture()));
await cannot("a normal user cannot submit a staff capture", () => setDoc(doc(user(), "products/cap_a1aaaa_1"), capture({ addedBy: "u1" })));
await can("an admin attaches capture photos", () => setDoc(doc(adminDoc(), "captureEvidence/cap_a1aaaa_1"), evidence()));
await cannot("a normal user cannot attach capture photos", () => setDoc(doc(user(), "captureEvidence/cap_a1aaaa_1"), evidence({ by: "u1" })));
await cannot("photos in another admin's name", () => setDoc(doc(adminDoc(), "captureEvidence/cap_a1aaaa_1"), evidence({ by: "a9" })));
await cannot("more than 3 photos", () => setDoc(doc(adminDoc(), "captureEvidence/cap_a1aaaa_1"), evidence({ photos: [1, 2, 3, 4] })));
await cannot("photos under an id that is not a capture", () => setDoc(doc(adminDoc(), "captureEvidence/p1"), evidence({ productId: "p1" })));
await cannot("extra fields with the photos", () => setDoc(doc(adminDoc(), "captureEvidence/cap_a1aaaa_1"), evidence({ public: true })));
const seedEvidence = async () => { await seed(); await env.withSecurityRulesDisabled((c) => setDoc(doc(c.firestore(), "captureEvidence/cap_a1aaaa_1"), evidence())); };
await canAfter("an admin reviewer reads the photos", seedEvidence, () => getDoc(doc(adminDoc(), "captureEvidence/cap_a1aaaa_1")));
await cannotAfter("a normal user cannot read the photos", seedEvidence, () => getDoc(doc(user(), "captureEvidence/cap_a1aaaa_1")));
await cannotAfter("nobody signed in reads the photos", seedEvidence, () => getDoc(doc(anon(), "captureEvidence/cap_a1aaaa_1")));

await env.cleanup();
console.log(`checks made: ${checks}`);
if (failures.length > 0) {
    console.log("\nFAILED: " + failures.length + " check(s)\n");
    for (const failure of failures) console.log("  - " + failure);
    process.exit(1);
}
console.log("ALL PASSED");
