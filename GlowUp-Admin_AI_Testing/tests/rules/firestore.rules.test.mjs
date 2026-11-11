// Checks firestore.rules against the Firestore EMULATOR. Nothing here touches the real project (project id "demo-glowup").
//
//   Run:  npm install   (once, in this folder)
//         npm test      (starts the emulator, runs this file, stops the emulator)
//   Needs Java 11 or newer on the PATH. The last line says "ALL PASSED" or how many checks failed (exit code 1).
//
// "can" = the rules must allow it, "cannot" = the rules must refuse it. Every check starts from the same small set of documents (seed), so a
// refusal is always the rule under test and never the leftover of an earlier check (a second write to a document that exists is an update).
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { initializeTestEnvironment } from "@firebase/rules-unit-testing";
import { doc, getDoc, getDocs, setDoc, updateDoc, deleteDoc, collection, query, writeBatch, serverTimestamp, increment, setLogLevel, Timestamp } from "firebase/firestore";

setLogLevel("silent");                                   // a refused write is expected here; the client would log each one

const here = path.dirname(fileURLToPath(import.meta.url));
const rules = readFileSync(path.join(here, "..", "..", "firestore.rules"), "utf8");
const [host, port] = (process.env.FIRESTORE_EMULATOR_HOST || "127.0.0.1:8085").split(":");

const env = await initializeTestEnvironment({ projectId: "demo-glowup", firestore: { rules, host, port: Number(port) } });

let checks = 0;
const failures = [];
async function attempt(operation) { try { await operation(); return null; } catch (error) { return error; } }
async function can(what, operation) { checks++; await seed(); const error = await attempt(operation); if (error) failures.push(`should be ALLOWED: ${what}\n      ${String(error.message || error).split("\n")[0]}`); }
async function cannot(what, operation) { checks++; await seed(); const error = await attempt(operation); if (!error) failures.push(`should be REFUSED: ${what}`); }

// the people: nobody, a plain user, another plain user, an admin in the "admins" list, an admin by custom claim
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

// ---------------------------------------------------------------------------------------------
// 1. The catalogue: everybody reads, only an admin writes
// ---------------------------------------------------------------------------------------------
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

// ---- ingredient classifications: everyone reads, only an admin writes, "other" is never deleted
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

// ---------------------------------------------------------------------------------------------
// 2. What the app itself may add: always Pending, always "not reviewed", always with the user's own id
// ---------------------------------------------------------------------------------------------
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

// counting scans: exactly +1 and nothing else
await can("the app adds 1 to a product's scans", () => updateDoc(doc(user(), "products/p1"), { scans: increment(1) }));
await can("the app adds 1 to a brand's scans", () => updateDoc(doc(user(), "brands/b1"), { scans: increment(1) }));
await cannot("the app adds 2 to a product's scans", () => updateDoc(doc(user(), "products/p1"), { scans: increment(2) }));
await cannot("the app takes 1 off a product's scans", () => updateDoc(doc(user(), "products/p1"), { scans: increment(-1) }));
await cannot("the app adds 1 to scans and changes the status", () => updateDoc(doc(user(), "products/p1"), { scans: increment(1), status: "Inactive" }));
await cannot("nobody signed in adds 1 to scans", () => updateDoc(doc(anon(), "products/p1"), { scans: increment(1) }));

// ---------------------------------------------------------------------------------------------
// 3. Who is an admin
// ---------------------------------------------------------------------------------------------
await can("an admin reads their own admins entry", () => getDoc(doc(adminDoc(), "admins/a1")));
await cannot("a user reads someone else's admins entry", () => getDoc(doc(user(), "admins/a1")));
await cannot("a user makes themselves an admin", () => setDoc(doc(user(), "admins/u1"), { note: "me" }));
await cannot("even an admin writes to admins", () => setDoc(doc(adminDoc(), "admins/x"), { note: "x" }));
await cannot("even an admin by claim writes to admins", () => setDoc(doc(adminClaim(), "admins/x"), { note: "x" }));
await cannot("a collection that is not in the rules (even for an admin)", () => setDoc(doc(adminDoc(), "auditLogs/l1"), { who: "a1" }));
await cannot("reading a collection that is not in the rules", () => getDoc(doc(adminDoc(), "secrets/s1")));

// ---------------------------------------------------------------------------------------------
// 4. Private data: a user's own profile and scans
// ---------------------------------------------------------------------------------------------
await can("a user reads their own profile", () => getDoc(doc(user(), "users/u1")));
await can("a user writes their own profile", () => setDoc(doc(user(), "users/u1"), { firstName: "Changed" }));
await can("a user reads their own scans", () => getDoc(doc(user(), "users/u1/scans/s1")));
await can("a user writes their own scans", () => setDoc(doc(user(), "users/u1/scans/s2"), { productName: "y" }));
await can("a user deletes their own scan", () => deleteDoc(doc(user(), "users/u1/scans/s1")));
await cannot("another user reads a profile", () => getDoc(doc(other(), "users/u1")));
await cannot("another user writes a profile", () => setDoc(doc(other(), "users/u1"), { firstName: "Hacked" }));
await cannot("another user reads someone's scans", () => getDoc(doc(other(), "users/u1/scans/s1")));
await cannot("nobody signed in reads a profile", () => getDoc(doc(anon(), "users/u1")));
await cannot("an admin reads a user's profile", () => getDoc(doc(adminDoc(), "users/u1")));
await cannot("an admin reads a user's scans", () => getDoc(doc(adminClaim(), "users/u1/scans/s1")));

// ---------------------------------------------------------------------------------------------
// 5. Anonymous scan notes and suggestions
// ---------------------------------------------------------------------------------------------
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

// ---------------------------------------------------------------------------------------------
// 6. Photo snapshots: removed on 11 November 2026 (photos are matched with the catalogue only). Nobody stages any more; an admin
//    may still read and delete what is left; the old daily counters are closed.
// ---------------------------------------------------------------------------------------------
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

// ---------------------------------------------------------------------------------------------
// 8. The Archive (added 29 October 2026, ARCHIVE_DESIGN.md): admins only, created once, never changed
// ---------------------------------------------------------------------------------------------
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

// bulk apply on the Inventory page: one batch that changes only the sources of the chosen products
await can("an admin applies a source to products in one batch", async () => {
    const db = adminDoc(), batch = writeBatch(db);
    batch.update(doc(db, "products/p1"), { sources: ["Official website | https://example.com"], updatedAt: serverTimestamp() });
    await batch.commit();
});
await cannot("a plain user applies a source", () => updateDoc(doc(user(), "products/p1"), { sources: ["Official website | https://example.com"] }));
await cannot("nobody signed in applies a source", () => updateDoc(doc(anon(), "products/p1"), { sources: ["Official website | https://example.com"] }));
await cannot("an admin applies more sources than the rule takes", () => updateDoc(doc(adminDoc(), "products/p1"), { sources: Array.from({ length: 41 }, (_, i) => "x | https://example.com/" + i) }));

// ---- siteLayouts: the shared default layout of a page (9 November 2026) ----
const shared = (uid) => ({ layout: { order: ["kpis"], styles: { kpis: { anim: "fade" } } }, appearance: { font: "inter" }, updatedAt: serverTimestamp(), updatedBy: uid });
await can("an admin makes the dashboard's shared default", () => setDoc(doc(adminDoc(), "siteLayouts/dashboard"), shared("a1")));
await can("an admin reads a shared default", () => getDoc(doc(adminDoc(), "siteLayouts/dashboard")));
await cannot("a plain user reads a shared default", () => getDoc(doc(user(), "siteLayouts/dashboard")));
await cannot("a plain user writes a shared default", () => setDoc(doc(user(), "siteLayouts/dashboard"), shared("u1")));
await cannot("an admin writes one for a page that does not exist", () => setDoc(doc(adminDoc(), "siteLayouts/elsewhere"), shared("a1")));
await cannot("an admin writes it in someone else's name", () => setDoc(doc(adminDoc(), "siteLayouts/dashboard"), shared("u1")));
await cannot("a shared default with an extra field", () => setDoc(doc(adminDoc(), "siteLayouts/dashboard"), Object.assign(shared("a1"), { script: "x" })));
await cannot("a shared default whose layout is not a map", () => setDoc(doc(adminDoc(), "siteLayouts/dashboard"), Object.assign(shared("a1"), { layout: "x" })));

// ---- adminShortcuts: an admin's own keyboard shortcuts (9 November 2026) ----
const keys = () => ({ items: { "inventory.addBrand": { keys: "Ctrl+Alt+KeyB", description: "Opens the Add brand form.", enabled: true } }, seeded: ["inventory.addBrand"], updatedAt: serverTimestamp() });
await can("an admin saves their own shortcuts", () => setDoc(doc(adminDoc(), "adminShortcuts/a1"), keys()));
await can("an admin reads their own shortcuts", () => getDoc(doc(adminDoc(), "adminShortcuts/a1")));
await cannot("an admin reads another admin's shortcuts", () => getDoc(doc(adminDoc(), "adminShortcuts/a2")));
await cannot("an admin writes another admin's shortcuts", () => setDoc(doc(adminDoc(), "adminShortcuts/a2"), keys()));
await cannot("a plain user saves shortcuts (the 403 of the endpoint)", () => setDoc(doc(user(), "adminShortcuts/u1"), keys()));
await cannot("a plain user reads shortcuts", () => getDoc(doc(user(), "adminShortcuts/u1")));
await cannot("nobody signed in reads shortcuts", () => getDoc(doc(anon(), "adminShortcuts/a1")));
await cannot("shortcuts with an extra field", () => setDoc(doc(adminDoc(), "adminShortcuts/a1"), Object.assign(keys(), { script: "x" })));
await cannot("shortcuts whose items are not a map", () => setDoc(doc(adminDoc(), "adminShortcuts/a1"), Object.assign(keys(), { items: "x" })));

await env.cleanup();
console.log(`checks made: ${checks}`);
if (failures.length > 0) {
    console.log("\nFAILED: " + failures.length + " check(s)\n");
    for (const failure of failures) console.log("  - " + failure);
    process.exit(1);
}
console.log("ALL PASSED");
