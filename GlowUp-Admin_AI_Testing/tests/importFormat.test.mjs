

import { parseImportText, hexOf, shadeCodeOf, pairShades } from "../_Shared/importFormat.js";

let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };
const equal = (a, b, what) => check(JSON.stringify(a) === JSON.stringify(b), `${what} (got ${JSON.stringify(a)})`);

const FILE = `=== BRAND ===
BRAND_NAME: Maybelline New York
OTHER_NAMES: Maybelline | Maybelline NY
MARKET: Global
OFFICIAL_WEBSITE: https://www.maybelline.com/en-us/about
WEBSITE_EVIDENCE: The About page names L'Oreal
LOGO_LINK: https://www.maybelline.com/logo.png
PICTURE_LINK: N/A

=== BRAND ===
BRAND_NAME: Nobody
OTHER_NAMES: None
MARKET: Mars
OFFICIAL_WEBSITE: http://shop.example/nobody
LOGO_LINK: N/A
PICTURE_LINK: N/A

=== PRODUCT ===
BRAND_NAME: Maybelline New York
PRODUCT_NAME: Fit Me Matte + Poreless Foundation
OTHER_NAMES: Fit Me Matte | None
COSMETIC_TYPE: foundation
DESCRIPTION: N/A
SHADES: #C8A27A | c24b62 | 128 | Warm Nude | rgb(10, 20, 30) | #C8A27A
MINIMUM_AGE: 0
BARCODE: 3600 5311-18
PRODUCT_PHOTO_LINK: https://www.maybelline.com/fitme.jpg
FDA_PH_LINK: https://verification.fda.gov.ph/products/123
FDA_REGISTRATION_NUMBER: NN-0000123456
INGREDIENTS: Aqua | Talc | aqua | Glycerin | N/A
SOURCES: https://www.maybelline.com/fit-me | http://insecure.example/a | https://retailer.example/p

=== PRODUCT ===
BRAND_NAME: Nobody
PRODUCT_NAME:
COSMETIC_TYPE: Lipstick
INGREDIENTS: Ricinus Communis Seed Oil

CONTINUE IN PART 2`;

let r = parseImportText(FILE);
equal(r.kind, "inventory", "brand and product blocks are an inventory file");
equal(r.draft.brands.map((b) => b.name), ["Maybelline New York", "Nobody"], "both brands are read");
equal(r.draft.brands[0].aliases, ["Maybelline", "Maybelline NY"], "other names split on the bar");
equal([r.draft.brands[0].market, r.draft.brands[0].websiteSuggested, r.draft.brands[0].logoUrl, r.draft.brands[0].imageUrl], ["Global", "https://www.maybelline.com/", "https://www.maybelline.com/logo.png", ""], "market, home page only, logo; N/A is empty");
equal([r.draft.brands[1].aliases, r.draft.brands[1].market, r.draft.brands[1].websiteSuggested], [[], "", ""], "None, an unknown market and an http address give nothing");
equal(r.draft.products.length, 1, "a product without a name is left out");
check(r.notes.some((n) => /no brand or no product name/.test(n)), "and the note says so");
const p = r.draft.products[0];
equal([p.brandName, p.productName, p.cosmeticType, p.description], ["Maybelline New York", "Fit Me Matte + Poreless Foundation", "Foundation", ""], "names, the exact type (case fixed), N/A description empty");
equal(p.productAliases, ["Fit Me Matte"], "None is not an alias");
equal(p.shades, ["#C8A27A", "#C24B62", "#0A141E"], "colours only: codes without # and rgb() accepted; a shade number, a name and a repeat dropped");
equal(p.ingredients, ["Aqua", "Talc", "Glycerin"], "ingredients in order, no repeat, N/A dropped");
equal(p.barcode, "360053111" + "8", "the barcode keeps its digits only");
equal(p.fdaNumber, "NN-0000123456", "the FDA registration number is read");
equal(parseImportText("=== PRODUCT ===\nBRAND_NAME: A1\nPRODUCT_NAME: B1\nFDA_REGISTRATION_NUMBER: N/A").draft.products[0].fdaNumber, "", "N/A is no number");
equal(parseImportText("=== PRODUCT ===\nBRAND_NAME: A1\nPRODUCT_NAME: B1\nFDA_REGISTRATION_NUMBER: <script>x</script>").draft.products[0].fdaNumber, "", "something that is not a number is left out");
equal([p.fdaVerifiedLink, p.imageUrl], ["https://verification.fda.gov.ph/products/123", "https://www.maybelline.com/fitme.jpg"], "FDA link and photo link");
equal(p.sources, ["https://www.maybelline.com/fit-me", "https://retailer.example/p", "https://verification.fda.gov.ph/products/123"], "sources: https only, the FDA page added");
equal([p.brandMarket, p.brandWebsiteSuggested], ["Global", "https://www.maybelline.com/"], "a product takes its brand's details from the BRAND block");


const FLAT = FILE.replace(/\n+/g, " ");
const flat = parseImportText(FLAT);
equal(flat.draft.products[0].ingredients, ["Aqua", "Talc", "Glycerin"], "the same file with every line break lost reads the same");
equal(flat.draft.brands[0].aliases, ["Maybelline", "Maybelline NY"], "also for brands");


const INGREDIENTS = `=== INGREDIENT ===
NAME: Methylparaben
LABEL_NAME: Methylparaben
OTHER_NAMES: Nipagin M | None
DESCRIPTION: A preservative.
TECHNICAL_DESCRIPTION: N/A
CAUTION_NOTE: It may irritate sensitive skin.
COMMON_USES: Foundations, lotions
MINIMUM_AGE: 0
RISK_LEVEL: Caution
ALLERGEN_FAMILY: Parabens
AVOID_WITH_CONDITIONS: Eczema (Atopic Dermatitis) | Rosacea
GOOD_FOR_CONDITIONS: None
AVOID_FOR_SKIN_TYPES: Dry | Mix | Greasy
GOOD_FOR_SKIN_TYPES: None
HIGHLIGHTS: Key active | Umbrella term
SOURCES: https://cir-safety.org/x | not a link

=== INGREDIENT ===
NAME: Methylparaben
=== INGREDIENT ===
NAME: Aqua
RISK_LEVEL: very bad`;
r = parseImportText(INGREDIENTS);
equal(r.kind, "ingredients", "ingredient blocks are an ingredient file");
equal(r.draft.ingredients.map((i) => i.name), ["Methylparaben", "Aqua"], "a name used twice is read once");
const m = r.draft.ingredients[0];
equal([m.riskLevel, m.cautionNote, m.aliases, m.chemicalDescription, m.avoidSkinTypes, m.allergens, m.avoidConditions, m.highlights, m.sources],
    ["caution", "may irritate sensitive skin", ["Nipagin M"], "", ["Dry", "Mix"], ["Parabens"], ["Eczema (Atopic Dermatitis)", "Rosacea"], ["Key active", "Umbrella term"], ["https://cir-safety.org/x"]],
    "the risk level, the caution note without 'It', skin types only from the four, lists, https sources");
equal(r.draft.ingredients[1].riskLevel, "", "a risk level that is not safe, caution or harmful is empty (the Curator's 'app decides')");


const SHEET = `SHEET: Ingredients
NAME,LABEL_NAME,OTHER_NAMES,DESCRIPTION,RISK_LEVEL,SOURCES
Glycerin,Glycerin,N/A,"Keeps skin soft, holds water",safe,https://cosing.example/glycerin
Talc,Talc,"Magnesium silicate | French chalk",A powder,caution,N/A
`;
r = parseImportText(SHEET);
equal(r.kind, "ingredients", "a sheet with ingredient keys is an ingredient file");
equal(r.draft.ingredients.map((i) => [i.name, i.description, i.riskLevel, i.aliases]), [["Glycerin", "Keeps skin soft, holds water", "safe", []], ["Talc", "A powder", "caution", ["Magnesium silicate", "French chalk"]]], "rows become records; quoted commas stay in one cell");
r = parseImportText(`SHEET: Products
BRAND_NAME,PRODUCT_NAME,COSMETIC_TYPE,INGREDIENTS
Revlon,ColorStay,Foundation,Aqua | Talc
`);
equal([r.kind, r.draft.products.length, r.draft.products[0].ingredients], ["inventory", 1, ["Aqua", "Talc"]], "a product sheet");


equal(parseImportText("Brand: Revlon\nProduct: ColorStay\nIngredients: Aqua, Talc").kind, "", "free text is left to the AI");
equal(parseImportText("").kind, "", "empty text");
equal(parseImportText(FILE + "\n=== INGREDIENT ===\nNAME: Aqua").kind, "mixed", "brands and ingredients in one file are refused");
equal([hexOf("#128"), hexOf("128"), hexOf("120"), hexOf("#abc"), hexOf("Nude")], ["", "", "", "#AABBCC", ""], "shade numbers and names are never colours");


equal([shadeCodeOf("119"), shadeCodeOf(" n20 "), shadeCodeOf("No. 12A"), shadeCodeOf("Shade 130"), shadeCodeOf("#02"), shadeCodeOf("NOIR")],
    ["119", "N20", "12A", "130", "02", "NOIR"], "codes as printed on packs");
equal([shadeCodeOf(""), shadeCodeOf("N/A"), shadeCodeOf("a code that is far too long to be one"), shadeCodeOf("<b>1</b>"), shadeCodeOf(null)],
    ["", "", "", "", ""], "no code, or not a code");
equal(pairShades(["#e8c9a5", "Nude", "#C24B62", "#E8C9A5"], ["119", "999", "", "120"]),
    { shades: ["#E8C9A5", "#C24B62"], codes: ["119", ""] }, "a name is dropped with its code; a repeated colour keeps its first place");
equal(pairShades(["#E8C9A5", "#E8C9A5"], ["", "120"]), { shades: ["#E8C9A5"], codes: ["120"] }, "a repeated colour takes the code it was missing");
equal(pairShades(["#E8C9A5"], undefined), { shades: ["#E8C9A5"], codes: [""] }, "an old product has no codes");
equal(pairShades("nothing", 5), { shades: [], codes: [] }, "not lists");
equal(pairShades(["#111111", "#222222", "#333333"], ["1", "2", "3"], 2), { shades: ["#111111", "#222222"], codes: ["1", "2"] }, "at most as many as allowed");


{
    const csv = "NAME,LABEL_NAME,OTHER_NAMES,RISK_LEVEL,SOURCES\nGlycerin,Glycerin,Glycerine | Glycerol,,\n\"Alcohol Denat., test\",Alcohol Denat.,,caution,https://eur-lex.europa.eu/x\n";
    const out = parseImportText(csv);
    equal(out.kind, "ingredients", "a plain CSV with ingredient keys is read as ingredients");
    equal(out.draft.ingredients.map((i) => i.name), ["Glycerin", "Alcohol Denat., test"], "csv: the names, a quoted comma stays inside the name");
    equal(out.draft.ingredients[0].aliases, ["Glycerine", "Glycerol"], "csv: a list in one cell");
    equal(out.draft.ingredients[1].riskLevel + "|" + out.draft.ingredients[1].sources.length, "caution|1", "csv: the risk level and the source link");
    equal(parseImportText("BRAND_NAME,PRODUCT_NAME,COSMETIC_TYPE,INGREDIENTS\nExamplebrand,Velvet Lipstick,Lipstick,Mica | Talc | Silica\n").draft.products.length, 1, "csv: a product row");
}

console.log("checks made: " + checks);
equal(parseImportText("=== INGREDIENT ===\nNAME: Linalool\nAVOID_FOR_SKIN_TYPES: Sensitive | Dry").draft.ingredients[0].avoidSkinTypes,
    ["Sensitive", "Dry"], "the Sensitive skin type is read");
const wrapped = parseImportText("=== PRODUCT ===\nBRAND_NAME: Ab\nPRODUCT_NAME: Cd\nPRODUCT_PHOTO_LINK: https://a.example.com/x\n y.jpg?h=1\n 2\nSOURCES: https://a.example.com/lumi-f\n oundation |\n https://b.example.com/\nINGREDIENTS: Talc").draft.products[0];
equal([wrapped.imageUrl, wrapped.sources], ["https://a.example.com/xy.jpg?h=12", ["https://a.example.com/lumi-foundation", "https://b.example.com/"]], "a link a PDF broke over lines is joined again");
const coded = parseImportText("=== PRODUCT ===\nBRAND_NAME: Ab\nPRODUCT_NAME: Cd\nDESCRIPTION: Liquid foundation.\nSHADES: not found\nSHADE_CODES: 110 | 115 |\n 220\nINGREDIENTS: Talc").draft.products[0];
equal([coded.description, coded.shades], ["Liquid foundation. Shade codes: 110, 115, 220", []], "shade codes without colours are kept in the description");
console.log(failed === 0 ? "ALL PASSED" : failed + " FAILED");
process.exit(failed === 0 ? 0 : 1);
