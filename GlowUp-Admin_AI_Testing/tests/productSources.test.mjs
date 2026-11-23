// New product data sources (23 Nov 2026): Open Beauty Facts category table, UPCitemdb second barcode look-up (and its
// server function), Makeup API shades. No network: answers are made up here in the shape the real services send.
import { createRequire } from "node:module";
import { readFileSync } from "node:fs";
import { typeFromCategories, outsideCategory, fromOpenBeautyFacts, gtinValid, fromUpcItemDb, upcItemPage, brandFor } from "../_Shared/smartAdd.js";
import { apiBrand, apiTypeFor, makeupApiUrl, cleanHex, codeFromShadeName, shadeMatches, newShades } from "../_Shared/shadeSources.js";

const require = createRequire(import.meta.url);
const upc = require("../api/upc-lookup.js").helpers;
const table = JSON.parse(readFileSync(new URL("../_Shared/data/obfCategories.json", import.meta.url), "utf8"));

let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };

// ---- category table ----
check(table.types["en:lipsticks"] === "Lipstick" && table.types["en:bronzing-powders"] === "Bronzer", "category table maps make-up categories");
check(!table.types["en:eye-contour-creams"] && table.notMakeup.includes("en:eye-contour-creams"), "eye-contour cream is skin care, not contour make-up");
check(table.notMakeup.includes("en:lip-balms") && table.notMakeup.includes("en:nail-polishes"), "lip balm and nail polish are not make-up");
check(!table.notMakeup.includes("en:makeup") && !table.types["en:makeup"], "the generic make-up group decides nothing");
check(typeFromCategories(["en:makeup", "en:lip-makeup", "en:lip-gloss"], table) === "Lip Gloss", "the specific category gives the type");
check(outsideCategory(["en:lip-balms"], table) === "lip balms", "a not-make-up category is named");
const balm = fromOpenBeautyFacts({ code: "4005900000000", product_name: "Soft Care", brands: "X", categories_tags: ["en:lip-balms"] }, table);
check(balm.cosmeticType === "" && /lip balms.*not make-up/.test(balm.note), "a lip balm from Open Beauty Facts is flagged, not typed");
const liner = fromOpenBeautyFacts({ code: "4005900000000", product_name: "Precision", brands: "X", categories_tags: ["en:kohl-pencils"] }, table);
check(liner.cosmeticType === "Eyeliner", "a name with no kind word gets its type from the category table");
check(fromOpenBeautyFacts({ product_name: "Velvet Lipstick", brands: "X" }).cosmeticType === "Lipstick", "without the table the words still decide");

// ---- barcodes: check digit ----
check(gtinValid("4006381333931") && gtinValid("036000291452") && gtinValid("96385074"), "valid EAN-13, UPC-A and EAN-8");
check(!gtinValid("3600531652300") && !gtinValid("4006381333932"), "a wrong last digit is refused (no free look-up wasted)");
check(!gtinValid("12345") && !gtinValid(""), "too short is not a barcode");
check(upc.gtinValid("036000291452") === gtinValid("036000291452") && upc.gtinValid("3600531652300") === false, "the server uses the same check digit rule");

// ---- UPCitemdb answers ----
const item = { title: "Maybelline Fit Me Matte + Poreless Liquid Foundation, 120 Classic Ivory", brand: "Maybelline", category: "Health & Beauty > Personal Care > Cosmetics > Makeup > Face Makeup > Foundations & Concealers",
    ean: "0041554433005", upc: "041554433005", images: ["http://insecure.example/x.jpg", "https://img.example/y.jpg"] };
const c = fromUpcItemDb(item, "041554433005", table);
check(c.name === "Fit Me Matte + Poreless Liquid Foundation, 120 Classic Ivory" && c.brand === "Maybelline", "brand taken off the front of the title");
check(c.cosmeticType === "Foundation", "type from the title and shop category");
check(c.ingredients.length === 0 && /not the ingredient list/.test(c.note), "never an ingredient list; the note says so");
check(c.barcode === "041554433005" && c.imageUrl === "https://img.example/y.jpg", "barcode with its zero; only an https picture");
check(c.sources[0] === upcItemPage("041554433005") && c.originLabel === "UPCitemdb", "the UPCitemdb page is the source");
check(fromUpcItemDb({ brand: "X" }, "1") === null, "an item without a title is skipped");
check(fromUpcItemDb({ title: "Maybelline Great Lash Lots of Lashes Mascara - 141 Very Black - 0.43 fl oz: Thickening, Lengthening, Paraben-Free", brand: "Maybelline" }, "041554239416").name === "Great Lash Lots of Lashes Mascara - 141 Very Black",
    "a real shop title (UPCitemdb, 23 Nov 2026) loses its selling words and size, keeps the shade");
check(fromUpcItemDb({ title: "NYX Butter Gloss, 8ml", brand: "NYX" }, "800897818500").name === "Butter Gloss", "a size after a comma is dropped");

const cleaned = upc.cleanItems({ items: [item, { title: "" }, { title: "<b>x</b>".repeat(80), images: ["javascript:alert(1)"] }] });
check(cleaned.length === 2 && cleaned[0].images.length === 1 && cleaned[1].title.length === 200 && cleaned[1].images.length === 0, "the server keeps only the needed fields, cut to size, https pictures only");

const reply = (status, body) => async () => ({ status, ok: status >= 200 && status < 300, json: async () => body });
upc.cache.clear();
let r = await upc.lookup("041554433005", reply(200, { code: "OK", total: 1, items: [item] }));
check(r.status === 200 && r.body.found === 1 && r.body.items[0].brand === "Maybelline", "a found item is passed on");
r = await upc.lookup("041554433005", async () => { throw new Error("must not be asked again"); });
check(r.status === 200 && r.body.cached === true, "a second ask for the same barcode is answered from memory (saves the daily limit)");
r = await upc.lookup("4006381333931", reply(429, { code: "TOO_FAST" }));
check(r.status === 429 && /100 a day/.test(r.body.error), "the daily limit is explained");
r = await upc.lookup("96385074", reply(400, { code: "INVALID_UPC" }));
check(r.status === 200 && r.body.found === 0, "INVALID_UPC = nothing found, not an error");
r = await upc.lookup("036000291452", async () => { throw Object.assign(new Error("abort"), { name: "AbortError" }); });
check(r.status === 504, "no answer in time gives a clear error");
upc.recent.clear();
let allowedCount = 0;
for (let i = 0; i < 8; i++) if (upc.allowed("admin1", 1000)) allowedCount++;
check(allowedCount === 6, "an admin may ask 6 times a minute");

// ---- Makeup API shades ----
check(apiBrand("Maybelline New York") === "maybelline" && apiBrand("L'Oréal Paris") === "l'oreal" && apiBrand("NYX Professional Makeup") === "nyx", "inventory brands are matched to the API's names");
check(apiBrand("Happy Skin") === "" && apiBrand("") === "", "a brand the API does not know gives nothing");
check(apiBrand("Zorah Biocosmetiques") === "zorah biocosmetiques", "the longest matching API brand wins");
check(apiTypeFor("Lip Gloss").type === "lipstick" && apiTypeFor("Lip Gloss").category === "lip_gloss" && apiTypeFor("Setting Spray") === null, "make-up kinds map to the API's types");
check(makeupApiUrl("Maybelline New York", "Lipstick") === "https://makeup-api.herokuapp.com/api/v1/products.json?brand=maybelline&product_type=lipstick", "the request address");
check(makeupApiUrl("Happy Skin", "Lipstick") === "", "no request for an unknown brand");
check(cleanHex("c41e3a") === "#C41E3A" && cleanHex("#AC7773, #BD7A80") === "#AC7773" && cleanHex("red") === "" && cleanHex("#12345") === "", "colour codes are cleaned or refused");
check(codeFromShadeName("110 Porcelain") === "110" && codeFromShadeName("N20 Natural") === "N20" && codeFromShadeName("Nude Thrill") === "" && codeFromShadeName("2020 Vision Gold") === "", "only a code printed at the start of a name becomes a shade code");
const api = [
    { name: "Maybelline Color Sensational Vivid Matte Liquid Lip Colour", category: "lipstick", product_colors: [{ hex_value: "#AC7773", colour_name: "Nude Thrill " }, { hex_value: "#ac7773", colour_name: "Same colour" }, { hex_value: "bad", colour_name: "x" }] },
    { name: "Maybelline Color Sensational Lipstick", category: "", product_colors: [{ hex_value: "#C41E3A", colour_name: "645 Red Revival" }] },
    { name: "No Colours Lipstick", product_colors: [] }
];
const m = shadeMatches(api, "Color Sensational Vivid Matte Liquid Lip Colour", "Lipstick");
check(m.length === 2 && m[0].name.includes("Vivid Matte"), "the closest product name comes first; products without colours are dropped");
check(m[0].shades.length === 1 && m[0].shades[0].hex === "#AC7773" && m[0].shades[0].name === "Nude Thrill" && m[0].shades[0].code === "", "repeated and invalid colours are dropped; a plain name is not a code");
check(m[1].shades[0].code === "645", "a leading number becomes the shade code");
check(newShades([{ hex: "#AC7773" }, { hex: "#C41E3A" }, { hex: "#111111" }], ["#ac7773"], 1).map((s) => s.hex).join() === "#C41E3A", "colours already in the form are skipped and the room limit holds");

// ---- brand of a look-up answer: exact, or one brand whose name starts with it ----
const inv = [{ id: "m", name: "Maybelline New York", aliases: [] }, { id: "e1", name: "Ever Bilena", aliases: [] }, { id: "e2", name: "Ever Bilena Advance", aliases: [] }, { id: "n", name: "NYX", aliases: [] }];
check(brandFor("Maybelline", inv).id === "m", "a short brand name finds the one listed brand that starts with it");
check(brandFor("Maybelline New York Cosmetics", inv).id === "m", "a longer name that starts with a listed brand finds it");
check(brandFor("Ever Bilena", inv).id === "e1", "the exact name wins over a longer one");
check(brandFor("Ever", inv) === null, "too short to trust (under 5 letters)");
check(brandFor("Ever Bilena Advance Pro", inv) === null, "two listed brands are both prefixes: ambiguous, the admin chooses");
check(brandFor("Ever Bil", inv) === null, "two listed brands could match: ambiguous, the admin chooses");
check(brandFor("NYX Professional Makeup", inv) === null, "a 3-letter brand is not matched by prefix (left for the admin)");

console.log(`checks made: ${checks}`);
console.log(failed === 0 ? "ALL PASSED" : `${failed} FAILED`);
process.exit(failed === 0 ? 0 : 1);
