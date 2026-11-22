import { readFileSync } from "node:fs";
import {
    queryKind, ingredientsFromText, fromOpenBeautyFacts, fromFdaCatalogue, searchCatalogue, mergeCandidates, brandFor, existingProduct,
    aliasesFromPubchem, descriptionFromPubchem, suggestClassification, friendlyStarter, scoreCandidate
} from "../_Shared/smartAdd.js";

let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };

check(queryKind("3600531652300").kind === "barcode", "13 digits are a barcode");
check(queryKind("3600 5316 52300").kind === "barcode", "spaces inside a barcode are fine");
check(queryKind("Superstay Matte Ink").kind === "name", "words are a name");
check(queryKind("   ").kind === "empty", "blank is empty");
check(queryKind("12345").kind === "name", "5 digits are too short for a barcode");

const label = "LIPSTICK 3.5 g\nINGREDIENTS: Ricinus Communis Seed Oil, Mica, Cera Alba, CI 77491, Parfum, Mica\nMade in France";
const names = ingredientsFromText(label);
check(names[0] === "Ricinus Communis Seed Oil" && names.includes("CI 77491"), "a label photo gives the list after INGREDIENTS");
check(names.filter((n) => n === "Mica").length === 1, "a name read twice is kept once");
check(!names.some((n) => /made in/i.test(n)), "text after the list is not an ingredient");
check(ingredientsFromText("Aqua, Glycerin, Dimethicone").length === 3, "a pasted list without a heading still works");

const off = fromOpenBeautyFacts({ code: "3600531652300", product_name: "Superstay Matte Ink Liquid Lipstick", brands: "Maybelline, L'Oreal",
    ingredients_text_en: "Dimethicone, Isododecane, Trimethylsiloxysilicate", categories_tags: ["en:lipsticks"], image_front_url: "https://images.openbeautyfacts.org/x.jpg" });
check(off.brand === "Maybelline" && off.cosmeticType === "Lipstick", "Open Beauty Facts: first brand, kind from the name");
check(off.ingredients.length === 3 && off.barcode === "3600531652300", "Open Beauty Facts: list and barcode");
check(off.sources[0] === "https://world.openbeautyfacts.org/product/3600531652300", "Open Beauty Facts page is the source");
check(fromOpenBeautyFacts({ code: "1" }) === null, "a product without a name is skipped");
check(fromOpenBeautyFacts({ product_name: "X", image_front_url: "http://insecure" }).imageUrl === "", "only https pictures are kept");

const catalogue = JSON.parse(readFileSync(new URL("../_Shared/data/fdaCatalogue.json", import.meta.url), "utf8"));
const first = catalogue.products[0];
const hits = searchCatalogue(`${first.brand} ${first.name}`, catalogue);
check(hits.length > 0 && hits[0].name === first.name && hits[0].fdaNumber === first.fdaNumber, "the FDA list is found by brand and name");
check(hits[0].ingredients.length === first.ingredients.length, "the FDA list brings its ingredients");
check(searchCatalogue("zzqx nothing like it", catalogue).length === 0, "nonsense finds nothing");
check(scoreCandidate("absidy translucent powder", fromFdaCatalogue(first)) >= 0.55, "partial words still match");

const noList = { ...off, ingredients: [], fdaNumber: "", origins: undefined };
const fda = { origin: "fda", originLabel: "FDA Philippines list", name: "Superstay Matte Ink Liquid Lipstick", brand: "Maybelline", cosmeticType: "Lipstick",
    ingredients: ["A", "B", "C", "D"], barcode: "", fdaNumber: "NN-1", imageUrl: "", sources: ["https://verification.fda.gov.ph/"] };
const merged = mergeCandidates([noList, fda]);
check(merged.length === 1 && merged[0].ingredients.length === 4 && merged[0].fdaNumber === "NN-1" && merged[0].barcode === "3600531652300", "same product from two places is merged, gaps filled");
check(merged[0].sources.length === 2 && merged[0].origins.length === 2, "both sources are kept");
check(mergeCandidates([off, { ...fda, brand: "Other", name: "Different" }]).length === 2, "different products stay apart");

const brands = [{ id: "b1", name: "Maybelline", aliases: ["Maybelline New York"] }, { id: "b2", name: "Old", aliases: [], binned: true }];
check(brandFor("maybelline new york", brands).id === "b1", "a brand is found by another name");
check(brandFor("Old", brands) === null, "a binned brand is not offered");
const products = [{ id: "p1", brandId: "b1", name: "Superstay Matte Ink Liquid Lipstick", aliases: [], barcode: "" }, { id: "p2", brandId: "b9", name: "Z", aliases: [], barcode: "3600531652300" }];
check(existingProduct(off, products, brands).id === "p2", "the same barcode means the same product");
check(existingProduct({ ...off, barcode: "" }, products, brands).id === "p1", "the same brand and name means the same product");

const syn = { InformationList: { Information: [{ CID: 753, Synonym: ["glycerol", "Glycerin", "56-81-5", "Glycerine", "1,2,3-Propanetriol", "UNII-PDC6A3C0OX", "DTXSID9020663", "glycerin"] }] } };
const a = aliasesFromPubchem(syn, "Glycerin");
check(a.cid === 753 && a.aliases.includes("glycerol") && a.aliases.includes("Glycerine"), "PubChem other names are kept");
check(!a.aliases.some((x) => /56-81-5|UNII|DTXSID/.test(x)) && !a.aliases.some((x) => x.toLowerCase() === "glycerin"), "registry numbers and the own name are left out");
const desc = descriptionFromPubchem({ InformationList: { Information: [{ CID: 753, Title: "Glycerin" }, { CID: 753, Description: "Glycerin is a triol with a structure of propane substituted at positions 1, 2 and 3 by hydroxy groups. " + "x".repeat(700) }] } });
check(desc.cid === 753 && desc.text.startsWith("Glycerin is a triol") && desc.text.length <= 610, "PubChem description is cut at a sentence end");
check(descriptionFromPubchem(null).text === "", "no answer, no description");

const classes = [{ id: "pres", name: "Preservative" }, { id: "col", name: "Colorant / Pigment" }, { id: "hum", name: "Humectant" }, { id: "other", name: "Other" }];
check(suggestClassification("Phenoxyethanol", "", classes).id === "pres", "a preservative is recognised");
check(suggestClassification("CI 77491", "", classes).id === "col", "a CI number is a colorant");
check(suggestClassification("Glycerin", "", classes).id === "hum", "glycerin is a humectant");
check(suggestClassification("Unknownium", "", classes) === null, "nothing known, nothing chosen");
check(friendlyStarter("Glycerin", "Humectant") === "Glycerin is a humectant used in make-up.", "a plain starter sentence");
check(friendlyStarter("X", "Other") === "", "no sentence for Other");

console.log(`checks made: ${checks}`);
console.log(failed === 0 ? "ALL PASSED" : `${failed} FAILED`);
process.exit(failed === 0 ? 0 : 1);
