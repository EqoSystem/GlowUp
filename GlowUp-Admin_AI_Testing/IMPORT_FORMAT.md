# GlowUp import file format

The website reads this format as it is (`_Shared/importFormat.js`): **no AI is used**, nothing is guessed, and a value that is
`N/A`, `None` or empty stays empty. A document that labels each product with `Brand:`, `Product:` and `Ingredients:` lines (a product sheet, a
PDF or Word export of one) is also read without AI by `_Shared/ingredientBlock.js`: a product needs a brand, a name and at least 3 ingredient names,
its make-up kind comes from a `Kind:` line or from the product name, and a product that lacks any of them is left out and named in the import's
notes. Any other document is still read by the AI as before.

- Word / PDF: one block per record, started by `=== BRAND ===`, `=== PRODUCT ===` or `=== INGREDIENT ===`; `KEY: value` lines; lists with ` | `.
- Excel: one sheet; row 1 = the KEYS; one record per row.
- One kind of record per file: brands and products together (Brand & Product Inventory), or ingredients alone (Ingredient Curator).
- At most 40 products and 40 brands per file; ingredients are saved in groups of 60. Keep a file under about 25,000 characters.

## Brand
`BRAND_NAME`, `OTHER_NAMES`, `MARKET` (Global | Local | Asia | Europe | North America), `OFFICIAL_WEBSITE` (an admin still confirms it),
`WEBSITE_EVIDENCE` (not stored), `LOGO_LINK`, `PICTURE_LINK`

## Product
`BRAND_NAME`, `PRODUCT_NAME`, `OTHER_NAMES`, `COSMETIC_TYPE` (one of the 17 make-up types), `DESCRIPTION`, `SHADES` (`#RRGGBB` only),
`MINIMUM_AGE`, `BARCODE`, `PRODUCT_PHOTO_LINK`, `FDA_PH_LINK` (a fda.gov.ph page), `FDA_REGISTRATION_NUMBER` (the FDA Philippines notification number; letters, digits and . , # ( ) _ - /), `INGREDIENTS` (INCI, printed order), `SOURCES` (https pages)

## Ingredient
`NAME`, `LABEL_NAME`, `OTHER_NAMES`, `DESCRIPTION`, `TECHNICAL_DESCRIPTION`, `CAUTION_NOTE`, `COMMON_USES`, `MINIMUM_AGE`,
**`RISK_LEVEL` (safe | caution | harmful)**, `ALLERGEN_FAMILY`, `AVOID_WITH_CONDITIONS`, `GOOD_FOR_CONDITIONS`, `AVOID_FOR_SKIN_TYPES`,
`GOOD_FOR_SKIN_TYPES`, `HIGHLIGHTS`, `SOURCES`

`RISK_LEVEL` matters: an ingredient without one is treated by the app as "safe" unless its built-in table knows it. Fill it only when a
regulator or medical source states it (harmful = prohibited or restricted by a regulator for cosmetics; caution = a documented concern
such as an allergen or irritant at normal use; safe = no concern stated). Leave it `N/A` when no source says.

Words the lists accept: allergen families Fragrance, Parabens, Sulfates, Formaldehyde releasers, Lanolin, Essential oils, Nut-derived oils;
conditions Acne, Eczema, Psoriasis, Rosacea, Seborrheic Dermatitis, Melasma, Contact Dermatitis; skin types Oily, Normal, Dry, Mix;
highlights Key active, Umbrella term. A word that matches no list item is left out and named in the import report.

Imported products and ingredients arrive as **Pending / waiting for review** (source ai, not reviewed). The app uses an unreviewed
ingredient as a warning at most, never as proof of safety.

## Batch barcodes (23 November 2026)

In Brand & Product Inventory, under the file drop, **Batch barcodes: paste a list** (or drop a .txt / .csv / Excel file that holds only
barcodes). One barcode per line or separated by commas, 8 to 14 digits, zeros kept; at most 100 per run. Each code is looked up like Quick fill
(Open Beauty Facts, then UPCitemdb through api/upc-lookup), then the found products go through the same "Check before saving" step and the same
rules as a product file (Pending, duplicates, make-up only, brand created Pending). Values are saved as Detected with the origin
"Barcode look-up: <source>". A barcode nobody knows, or one with no brand, is listed with its reason and not saved. A file is taken as a barcode list only when
at least 80% of its entries are barcodes (_Shared/batchBarcodes.js). In Excel, format the barcode column as Text so leading zeros survive.
