// Product capture rules (_Shared/capture.js) and the Quick fill merge (_Shared/smartAdd.js): the acceptance cases of the
// 2026-11-23 automation brief that can be checked without a browser or a database.
import {
    barcodeDigits, planFill, chosenValues, originsAfterFill, uncheckedFields, markChecked, cleanOrigins, originEntry, matchRoute,
    similarProducts, similarBrands, guessPhotoRole, repeatedNames, usefulLines, reviewReason, skippedLine, DETECTED, NEEDS_REVIEW, VERIFIED, TYPED
} from "../_Shared/capture.js";
import { mergeCandidates, queryKind, fromOpenBeautyFacts, ingredientsFromText } from "../_Shared/smartAdd.js";
import { barcodeKey } from "../_Shared/glowupRules.js";

let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };

// ---- barcodes keep their leading zeros ----
check(barcodeDigits("0012345678905") === "0012345678905", "barcode with leading zeros is stored with the zeros");
check(barcodeDigits("0 0123 4567 8905") === "0012345678905", "spaces are dropped, zeros kept");
check(barcodeDigits("12345") === "", "5 digits are not a barcode");
check(barcodeDigits("12a456789") === "", "letters are not a barcode");
check(barcodeDigits("123456789012345") === "", "15 digits are too long");
check(barcodeKey(barcodeDigits("0012345678905")) === barcodeKey("12345678905"), "a 0-led barcode compares equal to the same code without zeros");
const q = queryKind("012345678905");
check(q.kind === "barcode" && q.value === "012345678905" && q.key === "12345678905", "a scanned UPC keeps its zero for the look-up, key drops it");
const offZero = fromOpenBeautyFacts({ code: "0074170375109", product_name: "Matte Lipstick", brands: "Brand" });
check(offZero.barcode === "0074170375109", "Open Beauty Facts code keeps its leading zero");

// ---- duplicate barcode and variants ----
const products = [
    { id: "a", brandId: "b1", name: "Superstay Matte Ink Liquid Lipstick", barcode: "0012345678905", shades: ["#aa0000"], status: "Active" },
    { id: "b", brandId: "b1", name: "Superstay Matte Ink Liquid Lipstick 5 ml", barcode: "", shades: [], status: "Active" },
    { id: "c", brandId: "b2", name: "Fit Me Matte Foundation", barcode: "", shades: [], status: "Pending" },
    { id: "d", brandId: "b1", name: "Lash Sensational Mascara", barcode: "", shades: [], status: "Active", binned: true }
];
const sameCode = similarProducts({ name: "Other", brandId: "b9", barcode: "12345678905" }, products);
check(sameCode.length === 1 && sameCode[0].product.id === "a" && sameCode[0].why === "same barcode", "duplicate barcode (with and without zeros) is found");
const variants = similarProducts({ name: "Superstay Matte Ink Liquid Lipstick 3 ml", brandId: "b1" }, products);
check(variants.some((v) => v.product.id === "a") && variants.some((v) => v.product.id === "b"), "same product name with a different size is shown as a variant");
check(similarProducts({ name: "Superstay Matte Ink Liquid Lipstick", brandId: "b2" }, products).length === 0, "another brand's product is not a variant");
check(!similarProducts({ name: "Lash Sensational Mascara", brandId: "b1" }, products).some((v) => v.product.id === "d"), "products in the Recycle bin are not offered");
check(!similarProducts({ id: "a", name: "Superstay Matte Ink Liquid Lipstick", brandId: "b1" }, products).some((v) => v.product.id === "a"), "a product is not its own duplicate");

// ---- duplicate brand names ----
const brands = [{ id: "1", name: "Maybelline New York", aliases: ["Maybelline"] }, { id: "2", name: "L'Oreal Paris", aliases: [] }, { id: "3", name: "Colourette", aliases: [] }];
check(similarBrands("Maybeline", brands).some((x) => x.brand.id === "1"), "a misspelled brand name finds the listed brand");
check(similarBrands("Colorette", brands).some((x) => x.brand.id === "3"), "colour/color spelling finds the listed brand");
check(similarBrands("Maybelline", brands).length === 0, "the exact name is left to the form's own refusal (not a 'similar' brand)");
check(similarBrands("Happy Skin", brands).length === 0, "a different brand is not flagged");

// ---- conflicting Quick fill sources ----
const off = { origin: "openbeautyfacts", originLabel: "Open Beauty Facts", name: "Superstay Matte Ink", brand: "Maybelline", cosmeticType: "Lipstick",
    ingredients: ["Dimethicone", "Isododecane", "Mica"], barcode: "3600531652300", fdaNumber: "", imageUrl: "", sources: ["https://world.openbeautyfacts.org/product/3600531652300"] };
const fda = { origin: "fda", originLabel: "FDA Philippines list", name: "Superstay Matte Ink", brand: "Maybelline", cosmeticType: "Lipstick",
    ingredients: ["Dimethicone", "Trimethylsiloxysilicate", "Mica"], barcode: "", fdaNumber: "NN-1000001", imageUrl: "", sources: ["https://verification.fda.gov.ph/"] };
const [merged] = mergeCandidates([off, fda]);
check(merged.fdaNumber === "NN-1000001" && merged.fieldFrom.fdaNumber === "FDA Philippines list", "the field another source gave says where it came from");
check(merged.fieldFrom.ingredients === "Open Beauty Facts", "the first source's list is kept and labelled");
check(merged.conflicts.length === 1 && merged.conflicts[0].key === "ingredients" && merged.conflicts[0].values.length === 2, "two different ingredient lists are reported as a conflict, not silently chosen");

// ---- the form is never overwritten without a tick ----
const plan = planFill(merged, { name: "Superstay Matte Ink", brand: "", cosmeticType: "Liquid lipstick", imageUrl: "", barcode: "", fdaNumber: "", ingredients: [] });
const row = (k) => plan.find((r) => r.key === k);
check(row("name").kind === "same" && !row("name").apply, "a value both agree on is left alone");
check(row("brand").kind === "new" && row("brand").apply, "an empty field is filled");
check(row("cosmeticType").kind === "conflict" && !row("cosmeticType").apply, "a different value already in the form is NOT ticked");
check(row("ingredients").sourcesDisagree, "the row shows that the sources disagree");
check(row("imageUrl").kind === "absent" && !row("imageUrl").apply, "a value no source gave stays empty (Missing, never guessed)");
const values = chosenValues(plan, merged);
check(!("cosmeticType" in values) && values.brand === "Maybelline" && Array.isArray(values.ingredients), "only ticked values go into the form");
const zeroPlan = planFill({ barcode: "0012345678905", origins: ["X"] }, {});
check(chosenValues(zeroPlan, { barcode: "0012345678905" }).barcode === "0012345678905", "a filled barcode keeps its zeros");
check(planFill({ barcode: "0012345678905" }, { barcode: "12345678905" }).find((r) => r.key === "barcode").kind === "same", "the same barcode with and without zeros is not a conflict");

// ---- verification states ----
let origins = originsAfterFill({}, plan, "lookup", 1);
check(origins.brand.state === DETECTED && origins.brand.from === "Open Beauty Facts", "a filled value is Detected, with its source");
check(origins.ingredients.state === NEEDS_REVIEW, "a list the sources disagree on Needs review");
check(!origins.cosmeticType, "an unticked field gets no origin");
check(originsAfterFill({}, [{ key: "name", apply: true, from: "X" }], "lookup", 0.4).name.state === NEEDS_REVIEW, "a weak match Needs review");
check(uncheckedFields(origins).map((f) => f.key).join() === "brand,barcode,fdaNumber,ingredients", "the unchecked fields are listed in form order");
origins = markChecked(origins, ["brand", "ingredients"]);
check(origins.brand.state === VERIFIED && origins.ingredients.state === VERIFIED && origins.barcode.state === DETECTED, "marking checked changes only the named fields");
const dirty = cleanOrigins({ name: { state: "hacked", from: "x".repeat(500), method: "evil", atUnix: "5" }, notAField: { state: "verified" }, brand: "text" });
check(Object.keys(dirty).join() === "name" && dirty.name.state === DETECTED && dirty.name.from.length === 80 && dirty.name.method === "lookup" && dirty.name.atUnix === 5, "stored origins are cleaned: known keys, states and lengths only");
check(originEntry(TYPED, "Admin", "manual").state === TYPED, "a typed value is Typed");
check(uncheckedFields({ name: originEntry(TYPED, "Admin", "manual") }).length === 0, "a typed value waits for nobody");

// ---- match strength (routing signal only) ----
check(matchRoute(1) === "strong" && matchRoute(0.95) === "strong", "0.95 and up is a strong match");
check(matchRoute(0.8) === "check" && matchRoute(0.75) === "check", "0.75 to 0.94 asks for a check");
check(matchRoute(0.6) === "weak" && matchRoute(undefined) === "unknown", "below 0.75 is weak; no score is unknown");

// ---- pasted ingredients and OCR misreads ----
check(repeatedNames("Aqua, Glycerin\nDimethicone\nMica, mica").join() === "Mica", "a name pasted twice (any case, comma or new line) is reported");
check(repeatedNames("Aqua, Glycerin\nDimethicone").length === 0, "a clean pasted list has no repeats");
const misread = ingredientsFromText("INGREDIENTS: Dimethic0ne, lsododecane, Mica");
check(misread.includes("Dimethic0ne") && misread.includes("lsododecane"), "an OCR misread is kept as read (never silently corrected or dropped)");

// ---- photos, review, import report ----
check(guessPhotoRole("IMG_ingredients.jpg") === "ingredients" && guessPhotoRole("barcode-1.png") === "barcode" && guessPhotoRole("front.jpg") === "front", "photo roles are guessed from the file name");
check(guessPhotoRole("IMG_001.jpg", 0) === "ingredients" && guessPhotoRole("IMG_002.jpg", 1) === "back", "unnamed photos get a first guess the admin can change");
check(usefulLines("A\n\nMaybelline\nSUPERSTAY 24H\n#$%").join("|") === "Maybelline|SUPERSTAY 24H", "front text keeps the readable lines only");
check(reviewReason("  no  ") === "" && reviewReason("Ingredient list is from another shade") === "Ingredient list is from another shade", "a reject or send-back reason must be written (5 to 500 characters)");
check(skippedLine({ row: 7, name: "Lip Tint", brandName: "X", why: "already listed" }) === 'Row 7: "Lip Tint" of X: already listed', "each import row not saved is named with its row number");
check(skippedLine({ row: 3, name: "", brandName: "", why: "no product name" }) === "Row 3: (no product name): no product name", "a row without a name is still reported");

console.log(`checks made: ${checks}`);
console.log(failed === 0 ? "ALL PASSED" : `${failed} FAILED`);
process.exit(failed === 0 ? 0 : 1);
