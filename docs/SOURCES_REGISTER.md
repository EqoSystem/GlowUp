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
| The nearest of six skin tones is chosen by CIE Lab colour distance (not RGB) from the middle of a selfie; lighting can mislead it, so the user keeps the last word and sees how the tone was found | `Onboarding/SkinToneMatch.cs`, `SkinToneSelfie.cs` | CIE 1976 L*a*b* colour space and colour-difference standard (from memory); the 2023 to 2024 skin tone estimation studies in section E |
| "Find missing details": the AI web search looks a product up by name and brand and brings back brand, category and ingredient list marked Needs review, with the page it came from | `Admin/CaptureReader.cs` (FillMissing), `Scan/ProductAiAdvisor.cs` | Grounded AI search for product data; the Open Beauty Facts and related-systems items in section E |

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

## E. Related literature and studies, 2023 to now (found by web search, 24 Nov 2026)

Rule of the register: only items whose **title and link were returned by a search** are listed. Authors, volume and DOI were NOT checked: open each link and copy the
full reference from the page itself before it goes into the paper. "Supports" says which part of GlowUp the item can back up.

### Foreign studies and regulations (2023 or later)

| Year | Item (as the search returned it) | Link | Supports in GlowUp |
|---|---|---|---|
| 2023 | Commission Regulation (EU) 2023/1545, amending Annex III of Regulation (EC) 1223/2009: individual labelling of more than 80 fragrance allergens (in force 16 Aug 2023; products that do not comply may not be placed on the EU market after 31 Jul 2026, products already on the market may stay until 31 Jul 2028, per the Commission page) | https://eur-lex.europa.eu/eli/reg/2023/1545 and https://single-market-economy.ec.europa.eu/sectors/cosmetics/cosmetic-products-specific-topics/fragrance-allergens-labelling_en | The fragrance allergen family and the EU allergen list in `ingredientFit.js` (which still holds the shorter list of 26: update it to the 2023 list) |
| 2024 | "Contact sensitization to fragrance mix I and fragrance mix II among European dermatitis patients: A systematic review" (Botvid et al., Contact Dermatitis, per the search summary) | https://cris.fau.de/publications/325469756 | Fragrance as an allergen family; why the app flags it |
| 2024 | "Fragrance Contact Allergy – A Review Focusing on Patch Testing" (Sukakul, Bruze, Svedman; Acta Dermato-Venereologica, per the search summary) | https://pmc.ncbi.nlm.nih.gov/articles/PMC11334351/ | Same; limits of a "fragrance" label |
| 2024 | Systematic review of cosmetic contact allergens: patch-test positivity 13% to 100% over 13 studies (reported by HealthDay; the journal article itself still has to be found) | https://www.healthday.com/dermatology-special/patch-test-positivity-rate-varies-from-13-to-100-percent-for-cosmetics | Common cosmetic allergens (nickel, fragrance mix I, cobalt, PPD, balsam of Peru) |
| recent | "What is New in Contact Allergy To Cosmetics for Physicians, Cosmetologists, and Cosmetic Users?" (PMC review; year to confirm on the page) | https://www.ncbi.nlm.nih.gov/pmc/articles/PMC12552389/ | Patch testing as the reference method; why the app is a screening aid, not a diagnosis |
| 2023 | Review of new cosmetic sensitisers seen in 2022 (Current Allergy & Clinical Immunology, June 2023, per the search summary) | https://lirias.kuleuven.be/retrieve/758240 | Sensitiser list; carmine in lipsticks |
| 2023 | Systematic review in Clinical, Cosmetic and Investigational Dermatology arguing that sensitive skin subtypes should be classified by pathogenesis (title and authors to find on the journal site) | search again: "sensitive skin subtypes pathogenesis systematic review Clinical, Cosmetic and Investigational Dermatology 2023" | The **Sensitive skin reactions** question (`SensitiveReactions.cs`) |
| 2025 | Annals of Dermatology review noting Baumann's four sensitive-skin types: allergic, rosacea, acne, stinging (per the search summary; title to confirm) | search again: "Baumann sensitive skin four types allergic rosacea acne stinging Annals of Dermatology 2025" | Same |
| 2025 | Frontiers in Medicine paper with expert recommendations on diagnostic criteria for sensitive skin (title to confirm) | https://pmc.ncbi.nlm.nih.gov/articles/PMC12419229 | Same |
| background | "Definition of Sensitive Skin: An Expert Position Paper from the Special Interest Group on Sensitive Skin of the International Forum for the Study of Itch" (before 2023; the definition most papers use) | search the title on PubMed | Why "Sensitive" is one skin type with many reactions |
| 2025 | Review in Journal of Fungi on Malassezia folliculitis ("fungal acne"): antifungals are the mainstay, relapses are common, often misdiagnosed as acne | https://mdpi.com/2309-608X/11/9/662 | Fungal-acne flag (folliculitis) from the CureSkin table |
| 2026 | "Malassezia Folliculitis Presentation, Diagnosis, and Treatment: A Review of Fungal Acne" (Journal of Drugs in Dermatology) | https://jddonline.com/articles/malassezia-folliculitis-presentation-diagnosis-treatment-review-of-fungal-acne-S1545961626P9751X | Same |
| 2024 | "Skin Tone Estimation under Diverse Lighting Conditions" (Mbatha et al., Journal of Imaging, DOI 10.3390/jimaging10050109, per the search summary): a CNN on the 10-point Monk Skin Tone Scale; accuracy is uneven across tones | https://www.ncbi.nlm.nih.gov/pmc/articles/PMC11122461/ | The selfie "Find my tone" panel and its limits (lighting changes what a camera sees) |
| 2024 | "Colorimetric skin tone scale for improved accuracy and reduced perceptual bias of human skin tone annotations" (arXiv preprint) | https://arxiv.org/pdf/2410.21005 | Measuring tone by colour, not by eye |
| 2023 | Smartphone camera distance and angle versus measured skin-tone colour (Cronin et al., per the search summary; link to find) | search again: "Cronin smartphone camera distance angle skin tone color 2023" | Why the selfie is only a suggestion |
| 2023 | "ARCosmetics: a real-time augmented reality cosmetics try-on system" (smartphone landmark stabilisation) | https://research.buaa.edu.cn/en/publications/arcosmetics-a-real-time-augmented-reality-cosmetics-try-on-system/ | AR Try-On (face landmarks, smoothing) |
| 2025 | Frontiers in Virtual Reality consumer study of cosmetic try-on apps: informativeness drives intention to use (title to confirm) | search again: "Frontiers in Virtual Reality cosmetic try-on app informativeness playfulness intention" | Why AR Try-On is a feature users value |
| 2025 to 2026 | "Mobile Application That Uses Retrieve Augment Generate with Computer Vision to Detect Potentially Harmful Cosmetic Ingredients for People with Skin Conditions" (IADT thesis, not peer reviewed) | https://onshow.iadt.ie/sites/default/files/2026-05/Laura%20Hofmanova%20Thesis.pdf | A related system: OCR plus AI to flag ingredients for skin conditions |
| 2025 | PMC review of AI-driven personalised skincare (early platforms used questionnaires; newer ones use more data) | https://pmc.ncbi.nlm.nih.gov/articles/PMC12085869/ | Related systems: personalised recommendation |
| 2022 to 2025 | Open Beauty Facts project pages: crowd-sourced data under ODbL, nightly exports, a warning that its experimental ingredient parser is not reliable for allergens | https://wiki.openfoodfacts.org/Open_Beauty_Facts and https://openfoodfacts.github.io/openfoodfacts-server/reference/api-tutorials/scanning-cosmetics-pet-food-and-other-products | Why Open Beauty Facts lists are "Detected", never "Checked", and an admin reviews them |

### Local (Philippines)

| Year | Item (as the search returned it) | Link | Supports in GlowUp |
|---|---|---|---|
| 2025 | FDA Philippines Circular No. 2025-002 (March 2025) amending the ASEAN Cosmetic Directive; reported by a third-party news site, so confirm on the official page | https://www.fda.gov.ph (cosmetics circulars) | Why the app checks FDA status; ingredient restrictions |
| 2018 | FDA Circular No. 2018-009 (updated ASEAN Cosmetic Directive annexes) and the product information file guideline (before 2023, background) | https://www.fda.gov.ph/?p=63733 and related FDA pages | Same |
| 2022 | "Cosmetic preservative labeling in Philippine products in accordance with Philippine regulations" (Contact Dermatitis, 2022: before 2023, background; found that MCI/MI, formaldehyde and parabens are common, some labels were wrong, and Philippine rules do not cap preservatives as EU rules do) | https://lup.lub.lu.se/record/8dbaa3fb-9d02-47ec-a749-787429137d4b | Why a label can be wrong, and why the app never calls an unflagged ingredient safe |
| 2012 | "Cosmetic intolerance syndrome in a Filipino woman: A case report" (Journal of the Philippine Dermatological Society; before 2023, background) | https://search.bvsalud.org/gim/resource/en/wpr-633029 | Fragrance, formaldehyde and nickel reactions in a Filipino patient |

### Still missing (search again before the paper is finished)
- A Philippine **peer-reviewed study from 2023 or later** on cosmetic allergy or contact dermatitis: none came up. Try PubMed and the Journal of the Philippine Dermatological Society archive.
- A **peer-reviewed accuracy study of OCR on cosmetic ingredient labels** (2023 or later): none came up, only app store listings and one thesis.
- A **peer-reviewed study of Open Beauty Facts data quality**: none came up.
- A **Philippine thesis or study on a skincare or cosmetic ingredient app** (2023 or later): only press and product pages came up.
- A **Filipino skin tone or skin type study**: not searched yet.
- The **underlying references of the CureSkin flags** (Malassezia substrate lists, comedogenicity ratings): open the CureSkin page and cite those directly.

## F. Changes to this register
- 24 Nov 2026: created. Covers everything added up to app v0.98.5 and the website deploy of 24 Nov 2026, plus the Sensitive reactions work.
- 24 Nov 2026 (later): section E filled with 2023-or-later literature found by web search (titles and links only; full references still to be copied from each page); rows added for the Find my tone panel, the Find missing details button and the Sensitive reactions panel.
