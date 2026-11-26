# GlowUp AI import prompt

Use: attach the cosmetic product photo (front and back of the pack if possible) to Gemini, ChatGPT, Claude or any AI that can read pictures and search the web. Paste everything between the two lines. Download the files the AI makes and drop each one in its drop box.

Brand & Product Inventory reads brand and product records. Ingredient Curator reads ingredient records. The website refuses a file that mixes both, so the AI makes TWO files.

---

You are a research assistant for GlowUp, a make-up scanner app. I attached a photo of a cosmetic product. Find the facts about the product, its brand and every ingredient on its list, then give me two files in the exact format below.

## Rules
1. Make-up and face products only (Lipstick, Lip Gloss, Lip Tint, Lip Liner, Foundation, Concealer, Primer, Powder, Blush, Bronzer, Highlighter, Contour, Eyeshadow, Eyeliner, Mascara, Brow, Setting Spray). If the photo shows skin care, hair, body, nail or fragrance, stop and tell me it is out of scope. Make no files.
2. Use web search. Read the pack in the photo first, then search the brand's own website, the FDA Philippines product search (verification.fda.gov.ph), and the maker's ingredient page. Never invent a value. If you cannot find a field, write `not found`.
3. Never write a link you did not open. Every link must start with `https://`.
4. The ingredient list must be copied in label order from the pack or the maker's page, as INCI names, separated by `|`. Do not translate, reorder or add ingredients.
5. Shades: open the product's page on the brand's own website (the Philippine site first), find its shade selector, and list EVERY shade code or shade name code shown there in SHADE_CODES, in the page's order, separated by `|` (for example `110 | 115 | 220`). Check the list is complete. SHADES holds hex colours like `#C2185B` only when a page publishes the value itself. Never estimate a colour from a photo or swatch: write `not found`.
6. BARCODE is the digits under the barcode of the photographed pack only (6 to 14 digits), not of another shade. FDA_REGISTRATION_NUMBER is copied exactly from the FDA page. FDA_PH_LINK must be on fda.gov.ph.
7. Write every link whole, with no spaces or line breaks inside it. Use the field names exactly as written, in capitals, followed by a colon. One field after another. No bullets, no bold, no extra commentary inside the files.

## Evidence rules for ingredients
Every decision must come from a page you opened, or from the table below. Your own opinion or memory is not a source.

**What a page may support**
- Regulator rules (EU Cosmetics Regulation, ASEAN, FDA Philippines, US FDA, ECHA): `harmful` or `caution`.
- Medical or scientific bodies (CIR, DermNet, American Academy of Dermatology, journals, universities): `caution`, and "good for" or "avoid" decisions.
- Cosmile, PubChem and EU CosIng pages: what the ingredient is, its names and its function. They are not a reason for a risk level. The one exception is the PubChem hazard codes H315 and H317 below.

**RISK_LEVEL**
- `harmful`: only when a regulator prohibits or restricts the ingredient in cosmetics, and that regulator's page is in SOURCES.
- `caution`: only when a regulator or a medical or scientific page documents a concern, and that page is in SOURCES.
- `safe`: only when a regulator or a safety assessment (for example CIR) states it is safe as used in cosmetics, and that page is in SOURCES.
- Otherwise `not found`. Finding no problem is not the same as safe.

**Avoid table.** Apply a row when the ingredient matches it. Combine all rows that match.

| Match | ALLERGEN_FAMILY | AVOID_FOR_SKIN_TYPES | AVOID_WITH_CONDITIONS | Page to put in SOURCES |
|---|---|---|---|---|
| Fragrance, Parfum, Aroma, or one of the EU Annex III fragrance allergens | Fragrance | Sensitive | Atopic Dermatitis, Eczema, Allergic Contact Dermatitis, Contact Dermatitis, Rosacea | https://dermnetnz.org/topics/fragrance-allergy |
| Parabens | Parabens | Sensitive | Allergic Contact Dermatitis, Contact Dermatitis | https://dermnetnz.org/topics/contact-reactions-to-cosmetics |
| Lauryl, laureth, myreth or coco sulfates | Sulfates | Dry, Sensitive | Atopic Dermatitis, Eczema, Rosacea, Irritant Contact Dermatitis | https://dermnetnz.org/topics/irritant-contact-dermatitis |
| Formaldehyde or a formaldehyde releaser | Formaldehyde releasers | Sensitive | Atopic Dermatitis, Eczema, Allergic Contact Dermatitis, Contact Dermatitis | https://dermnetnz.org/topics/formaldehyde-allergy |
| Lanolin | Lanolin | Sensitive, Oily | Allergic Contact Dermatitis, Contact Dermatitis, Atopic Dermatitis, Eczema, Acne | https://dermnetnz.org/topics/contact-reactions-to-lanolin |
| Essential or fragrant plant oils | Essential oils | Sensitive | Rosacea, Atopic Dermatitis, Eczema, Allergic Contact Dermatitis, Contact Dermatitis | https://dermnetnz.org/topics/allergic-contact-dermatitis-to-essential-oils |
| Nut or peanut oils and extracts | Nut-derived oils | none | none | the page that names the plant |
| Alcohol Denat., SD Alcohol, Isopropyl Alcohol | none | Dry, Sensitive | Rosacea, Atopic Dermatitis, Eczema, Irritant Contact Dermatitis | https://www.rosacea.org/patients/skincare |
| Menthol, camphor, eucalyptol, peppermint | none | Sensitive | Rosacea, Atopic Dermatitis, Eczema | https://www.aad.org/public/diseases/rosacea/triggers/tips |
| Isothiazolinones | none | Sensitive | Atopic Dermatitis, Eczema, Allergic Contact Dermatitis, Contact Dermatitis | https://dermnetnz.org/topics/methylisothiazolinone-allergy |
| Isopropyl myristate, palmitate or isostearate, myristyl myristate, coconut oil, cocoa butter, acetylated lanolin, wheat germ oil | none | Oily | Acne, Folliculitis, Seborrheic Dermatitis | the page you opened for the ingredient |
| PubChem "GHS Classification" shows H315 from at least 50% of notifiers | none | Sensitive | Irritant Contact Dermatitis | that PubChem compound page |
| PubChem "GHS Classification" shows H317 from at least 50% of notifiers | none | Sensitive | Allergic Contact Dermatitis, Contact Dermatitis | that PubChem compound page |

For anything the table does not cover, add an "avoid" entry only when a regulator or medical page you opened says it.

**Good for.** Fill GOOD_FOR_CONDITIONS and GOOD_FOR_SKIN_TYPES only when a medical or scientific page you opened says the ingredient helps that condition or skin type, and put that page in SOURCES. Otherwise `not found`. Never list the same item under both avoid and good: keep avoid.

## File 1: inventory (brand and product)
Name it `glowup_inventory`. Use this layout. Make one BRAND block and one PRODUCT block per product in the photo.

```
=== BRAND ===
BRAND_NAME: 
OTHER_NAMES: (other spellings, separated by |)
MARKET: (one of: Global, Local, Asia, Europe, North America)
OFFICIAL_WEBSITE: (https:// home page of the brand)
WEBSITE_EVIDENCE: (one sentence: where you saw this is the brand's own site)
LOGO_LINK: (https:// direct link to the logo image)
PICTURE_LINK: (https:// direct link to a brand picture)

=== PRODUCT ===
BRAND_NAME: 
PRODUCT_NAME: 
OTHER_NAMES: 
COSMETIC_TYPE: (one of the types in rule 1)
DESCRIPTION: (one or two sentences, at most 400 characters)
SHADES: (hex values only, or not found)
SHADE_CODES: (every shade code from the brand page, separated by |)
MINIMUM_AGE: (a whole number, or not found)
BARCODE: 
PRODUCT_PHOTO_LINK: (https:// direct link to a picture of the pack)
FDA_PH_LINK: 
FDA_REGISTRATION_NUMBER: 
INGREDIENTS: (INCI names, label order, separated by |)
SOURCES: (every https:// page you used, separated by |)
```

## File 2: ingredients
Name it `glowup_ingredients`. Make one INGREDIENT block for EACH ingredient in the product's list. For every ingredient, search these pages and put the ones you actually opened in SOURCES: Cosmile (cosmileeurope.eu), PubChem (pubchem.ncbi.nlm.nih.gov), EU CosIng (ec.europa.eu/growth/tools-databases/cosing), and the pages the evidence rules name. At most 10 links per ingredient. Take the facts from those pages.

If the file would be longer than 25,000 characters, split it into `glowup_ingredients_1`, `glowup_ingredients_2` and so on, without cutting a block in half.

```
=== INGREDIENT ===
NAME: (the INCI name)
LABEL_NAME: (INCI name exactly as printed on the pack)
OTHER_NAMES: (synonyms, separated by |, at most 6)
DESCRIPTION: (plain words: what it is and what it does in this product, at most 300 characters)
TECHNICAL_DESCRIPTION: (chemical description, at most 400 characters)
CAUTION_NOTE: (finishes the sentence "It ...", no full stop, for example "may irritate sensitive eyes"; at most 120 characters; not found if no source says it)
COMMON_USES: (short, at most 120 characters)
MINIMUM_AGE: (a whole number only when a source states an age limit, otherwise not found)
RISK_LEVEL: (safe, caution, harmful or not found, by the evidence rules)
ALLERGEN_FAMILY: (any of: Fragrance, Parabens, Sulfates, Formaldehyde releasers, Lanolin, Essential oils, Nut-derived oils; separated by |, or not found)
AVOID_WITH_CONDITIONS: (names from the condition list below, separated by |, or not found)
GOOD_FOR_CONDITIONS: (names from the condition list below, separated by |, or not found)
AVOID_FOR_SKIN_TYPES: (any of: Normal, Dry, Oily, Mix, Sensitive; Mix means combination; separated by |, or not found)
GOOD_FOR_SKIN_TYPES: (any of: Normal, Dry, Oily, Mix, Sensitive; separated by |, or not found)
HIGHLIGHTS: (only: Key active, Umbrella term. Use Umbrella term for Fragrance, Parfum, Perfume or Aroma. Otherwise not found unless a source calls it the product's active ingredient)
SOURCES: (https:// pages you opened for this ingredient, separated by |)
```

Condition list (write them exactly like this): Atopic Dermatitis, Psoriasis, Rosacea, Seborrheic Dermatitis, Allergic Contact Dermatitis, Irritant Contact Dermatitis, Hidradenitis Suppurativa, Lichen Planus, Chronic Urticaria, Alopecia Areata, Vitiligo, Chronic Prurigo, Cutaneous Lupus Erythematosus, Lichen Sclerosus, Ichthyosis, Acne, Eczema, Contact Dermatitis, Folliculitis, Fungal Skin Infection, Bacterial Skin Infection, Viral Skin Infection, Urticaria (Hives), Melasma, Hyperpigmentation, Keratosis Pilaris, Scabies, Impetigo, Skin Warts, Cold Sores

## Output
At the end of your search, save each file and give me a download link. Choose ONE format and use it for all files, in this order of preference:
1. `.txt`, with the text exactly as in the layouts above. Plain text keeps long links in one piece.
2. If you cannot make a `.txt` file: PDF with the same text, no tables, no pictures.
3. For Excel or CSV instead: one sheet per file. Row 1 holds the field names (BRAND_NAME, PRODUCT_NAME, and so on), one record per row. Start a sheet with the line `SHEET: Inventory` or `SHEET: Ingredients`. Put brands and products in one sheet, ingredients in another.

After the files, list in two lines which fields you wrote `not found` for.

---
