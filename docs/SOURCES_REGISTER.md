# GlowUp sources register

One place that lists **everything GlowUp uses or relies on** (data, regulations, tools, rules) and where it comes from, so the paper's
Review of Related Literature, Related Systems and Studies (foreign and local) can be updated later without hunting through the code.

**How to keep it:** whenever a data source, an outside service, a library or an ingredient/skin rule is added to the app or the website, add a row here
in the same change. Never write a citation you have not seen: put the topic under "Literature to find" instead.

**Checked** = what I could read myself in a session (licence file or documentation). "From memory" means it still has to be opened and confirmed
before it goes into the paper.

## A. Data the app and website read

| What we use it for | Source | Origin | Licence / terms | Where in GlowUp | Checked |
|---|---|---|---|---|---|
| Is a make-up product registered in the Philippines (notification number, brand list, FDA match) | FDA Philippines product notification portal (the shipped copy is `_Shared/data/fdaCatalogue.json`) | Local (PH) | Public government portal; used with an honest app name | Website: Quick fill, Batch Lookup, auto-approve of FDA-confirmed scans. App: FDA badge and source buttons | From memory (see `docs/` and memory notes: portal refuses default user agent) |
| Product names, brands, barcodes and ingredient lists for 1,075 make-up products | Open Beauty Facts data dump (`_Shared/data/obfMakeup.json`, built by `tools/buildObfMakeup.mjs`) and its live API | Foreign (community, France-based) | ODbL: credit "Open Beauty Facts contributors", share-alike for the database | Website: first look-up in Batch Lookup / Quick fill; app: barcode look-up and second opinion | Yes (dump downloaded 24 Nov 2026) |
| Barcode to product title and brand (no ingredients) | UPCitemdb free trial API, through `api/upc-lookup.js` | Foreign | Trial service: 100 look-ups a day, 6 a minute | Website: Quick fill, Batch Lookup | Yes (limits read in code comments) |
| Ingredient chemistry: other names, description, compound page | PubChem PUG REST (US National Library of Medicine, NIH) | Foreign | Public domain US government data; usage limits apply | Website: Ingredient Curator (Assist, PubChem page in Sources); `api/mcp.js` ingredient_lookup | From memory |
| Ingredient identifiers: INCI name, CosIng id, CAS, EC number, PubChem id | BEAUTEE "Cosmetic Ingredients Dataset (Lite)", github.com/beauteeru/cosmetic-ingredients-dataset | Foreign (Russia) | MIT | `_Shared/data/ingredientRefs.json` (PubChem ids) | Yes (LICENSE read 24 Nov 2026) |
| Ingredient flags: fragrance / EU allergen, drying alcohol, comedogenic rating (0 to 5), fungal-acne substrate, usual function | CureSkin "Indian Skincare Ingredient Flags" (Indian Skincare Decoder, 2026), github.com/cureskin/indian-skincare-ingredient-flags, cureskin.com/indianskincaredecoder/dataset | Foreign (India); built from Indian marketplace products and published dermatology references | CC BY 4.0: attribution required (credit is stored in the data file and in each flagged ingredient's Sources) | Website: automatic flags, empty Purpose fill (`_Shared/ingredientFit.js`) | Yes (LICENSE and README read 24 Nov 2026). Its own underlying references (Malassezia substrate lists, comedogenicity ratings) still have to be opened and cited |
| Allergen families, skin types, 30 skin conditions (15 chronic users can choose, 15 general) | Official GlowUp lists in `_Shared/listRules.js` (`config/lists`) | Project decision, panel-approved lists | Own work | Website list manager; app onboarding and matching | Project record (commit 064abe2) |

## B. Regulations and standards the rules lean on

| Rule in GlowUp | Source | Origin | Where | Checked |
|---|---|---|---|---|
| 26 fragrance allergens that must be named on a label (linalool, limonene, citral, eugenol, ...) | EU Cosmetics Regulation (EC) No 1223/2009, Annex III, and later amendments | Foreign (EU) | `_Shared/ingredientFit.js` (EU_FRAGRANCE_ALLERGENS) | From memory: open the regulation and the latest amendment before citing, the list changed in 2023 |
| Skin sensitiser classification (formaldehyde releasers, isothiazolinones, lanolin) | EU CosIng database, SCCS opinions, ECHA classification (H317 "may cause an allergic skin reaction") | Foreign (EU) | `_Shared/ingredientFit.js`, ingredient field `frequentUseAllergy` | From memory |
| Cosmetic products must be notified with the Philippine FDA | Republic Act 9711 (FDA Act of 2009) and ASEAN Cosmetic Directive | Local (PH) and regional | Why the app checks FDA status | From memory: confirm article numbers |
| Health and allergy data of a person is kept only with their consent | Data Privacy Act of 2012 (Republic Act 10173) | Local (PH) | Terms page second box (`healthConsentAtUnix`), "nothing about the profile goes to the AI" rule | From memory: confirm applicable sections |
| Barcode check digit (EAN-8, UPC-A, EAN-13, GTIN-14) | GS1 General Specifications | Foreign (standard) | `gtinValid` in `_Shared/smartAdd.js`, `_Shared/gs1Hint.js` | From memory |

## C. Rules written by the project (need a published reference each)

| Rule | Where | Literature to find |
|---|---|---|
| Sulfate cleansers irritate dry, sensitive, eczema and rosacea skin | `_Shared/ingredientFit.js` | Dermatology reviews on surfactants and the skin barrier |
| Denatured alcohol, menthol and camphor irritate sensitive and rosacea skin | same | Reviews on rosacea triggers and irritant contact dermatitis |
| Pore-clogging esters and butters for acne-prone and oily skin; yeast-feeding oils for folliculitis | same | Comedogenicity rating studies; Malassezia (fungal acne) folliculitis reviews |
| Parabens, lanolin, essential oils, nut oils as allergen families | same, `listRules.js` | Contact allergy to cosmetic ingredients (patch-test studies), fragrance allergy |
| Sensitive skin shown in different ways (redness, stinging, itching, breakouts, tightness, reacts to products) | `Scan/SensitiveReactions.cs` | Sensitive skin subtypes and the Baumann Skin Type system (from memory: four kinds of sensitive skin: acne, rosacea, burning/stinging, contact allergy); sensitive skin definitions from the International Forum for the Study of Itch |
| An unflagged ingredient is never called safe | engine and website | Wording of regulator and CIR safety assessments |

## D. Tools and services (Related Systems)

| Tool | Used for | Origin | Licence / terms | Checked |
|---|---|---|---|---|
| Unity 2022.3 (IL2CPP, Android) | The app | Foreign | Unity terms | Project record |
| Firebase Authentication and Cloud Firestore | Accounts, shared database (free plan only) | Foreign (Google) | Firebase terms | Project record |
| Vercel (static site and `api/` functions) | Website hosting and server functions | Foreign | Vercel terms (free plan) | Project record |
| Google Gemini API (web search grounding) | Sourced ingredient flags, app AI look-ups | Foreign (Google) | Gemini API terms; key stays server-side (`api/app-ai.js`, `api/ingredient-fit.js`) | Project record |
| Tavily search API (optional, `TAVILY_API_KEY`) | Finding regulator and medical pages for the two-step sourced flags | Foreign | Tavily terms; call written from memory and not yet tested | Not tested |
| Google ML Kit text recognition | Reading ingredient text on the phone | Foreign | ML Kit terms | Project record |
| ARCore, MediaPipe Face Landmarker, MediaPipe hand gestures | Live AR try-on, finger signs | Foreign | Apache 2.0 for MediaPipe (from memory); ARCore terms | Project record |
| MobileNet picture model | Recognising a pack by its look | Foreign | Apache 2.0 (from memory) | Project record |
| Makeup API (makeup-api.herokuapp.com) | Shades and colours (allowed by the site's CSP) | Foreign | Free API | From memory |
| ZXing and Tesseract.js (in the browser) | Barcode and label reading on the website | Foreign | Apache 2.0 (from memory) | From memory |

## E. Candidate related studies and systems (to search, not yet read)

Local (Philippines): studies on cosmetic use and allergic or irritant reactions among Filipinos; FDA Philippines notices on unregistered or banned cosmetics;
Filipino skin tone and skin type research; mobile cosmetic-ingredient apps made for Philippine students or consumers.

Foreign: ingredient checker apps (for example INCI Decoder style tools, Think Dirty, EWG Skin Deep, Yuka, CosDNA); Open Beauty Facts papers; studies of OCR for
ingredient labels; studies of personalised cosmetic recommendation by skin type and condition; AR virtual try-on studies; patch-test studies for cosmetic contact allergy.

Do not cite any of these until the paper itself is opened and its details confirmed.

## F. Changes to this register
- 24 Nov 2026: created. Covers everything added up to app v0.98.5 and the website deploy of 24 Nov 2026, plus the Sensitive reactions work.
