# Paper revisions: text to paste in (panel comments, 22 Nov 2026)

Rules for this file: nothing here is a result. Every number that must come from your respondents is a blank (`__`). Statements about studies are limited to what the sources below say; check each against the full paper before you cite it. Paste the sections into your paper; the notes in [brackets] are for you and are not part of the paper.

## 1. Purpose and Description (panel item)

GlowUp is an Android application for make-up that reads a product with the phone camera (barcode, name and ingredient list), compares the ingredients with the skin profile the user saved on the phone (skin type, skin tone, chronic skin conditions and allergens) and tells the user, product by product, whether anything is flagged. It also lets the user try a product on with augmented reality (AR). The skin profile is kept on the phone and in the user's own account; it is never sent to the AI service. A web portal lets administrators keep the product, brand and ingredient data correct. GlowUp is information, not medical advice.

Purpose: to help make-up users with skin sensitivities avoid ingredients that do not suit their skin, and to make it quick to check a product before buying it.

## 2. Studies behind the lists in the app (Review of Related Literature)

**Skin tone (Step 3).**
- The Fitzpatrick scale (Fitzpatrick, 1975) has six types and was designed around how skin reacts to ultraviolet light, not to measure pigment. A 2025 letter in the Journal of Drugs in Dermatology describes it as commonly used as a stand-in for skin tone although it was built for a different purpose. [https://jddonline.com/articles/dermatology/S1545961625P8618X]
- The Monk Skin Tone Scale (Dr Ellis Monk with Google) has ten shades and was made for computer vision and for wider coverage of skin tones. In a comparison of 517 people reported on a poster from the Medical University of Vienna, the Monk scale agreed more closely with measured pigmentation than the Fitzpatrick scale (weighted kappa 0.91 against 0.76); the authors still recommend objective measurement where possible. [Diamantidis et al., poster ID 25, AI Summit 2026: https://dermatologie.meduniwien.ac.at/fileadmin/content/OE/dermatologie/AI_in_Dermatologie/Posters_AI_Summit_2026/Poster_ID_25.pdf] [This is a poster, not a peer-reviewed paper: say so.]
- GlowUp groups these into six tiles (Fair, Light, Medium, Tan, Deep, Dark). Limitation to state: six tiles are coarser than the Monk scale, and a camera reading depends on the light.

**Skin type (Step 2).**
- Baumann (2008, Dermatologic Clinics, PMID 18555952) argues that normal / dry / oily / combination gives little information and proposes four parameters (oily or dry, resistant or sensitive, pigmented or not, wrinkled or tight) giving 16 types. A 2023 overview in Cosmetics (https://www.mdpi.com/2079-9284/10/1/14) notes that the Fitzpatrick and Baumann systems are self-report tools and that such scales remain poorly standardised. GlowUp keeps the four everyday types for simplicity and handles sensitivity through the allergen step. State this as a limitation.

**Chronic skin conditions (Step 2).**
- The list (acne, eczema / atopic dermatitis, psoriasis, rosacea, seborrheic dermatitis, melasma, contact dermatitis) follows the conditions that make-up ingredients are most often discussed for in AAD and DermNet material. [Add the exact AAD and DermNet pages you used; they are not in this file.]
- Philippine data found: psoriasis is estimated at about two million Filipinos in a 2018 conference abstract (ISPOR Europe 2018). No Philippine prevalence figures were found for acne, atopic dermatitis, rosacea or melasma in the searches made: write "not found" or search the Philippine Dermatological Society before claiming any. [https://www.ispor.org/heor-resources/presentations-database/presentation/ispor-europe-2018/psoriasis-health-care-in-the-philippines-towards-less-marginalization-and-improved-coverage]

**AR textures (finishes).**
- GlowUp offers five finishes (matte, satin, glossy, shimmer, metallic) and starts with the finish read from the product, then the user's last choice, then a guess from the kind of product, and it says which one it used.
- No study was found that tests matte against gloss rendering in AR. What was found: in a Thai study of AR-AI virtual make-up try-on, technological realism had a significant positive effect on purchase intention (beta 0.333, p < 0.001) [https://so03.tci-thaijo.org/index.php/journalcim/article/download/287491/189151/1276344] [snippet only: read the full text before citing the numbers]; a Portuguese master's thesis on lipstick and mascara try-on reported that perceived quality from the try-on did not significantly raise purchase intention [https://repositorio.iscte-iul.pt/bitstream/10071/34167/1/Master_maria_carta_gaudich.pdf]. Write the finishes as a design choice, not as a finding.

## 3. Weighted mean and verbal interpretation, per objective

Use one scale for every objective table. A common 4-point agreement scale (check it against the one your adviser approved):

| Mean | Verbal interpretation |
|---|---|
| 3.26 - 4.00 | Strongly Agree |
| 2.51 - 3.25 | Agree |
| 1.76 - 2.50 | Disagree |
| 1.00 - 1.75 | Strongly Disagree |

Per objective, one table of this form (fill from your questionnaire data only):

| Objective | Indicator | Weighted mean | Verbal interpretation |
|---|---|---|---|
| 1 ... | ... | __ | __ |
| Overall for objective 1 | | __ | __ |

Formula to show: weighted mean = sum(f x w) / N, with f the number of respondents for each rating w and N the total number of respondents.

## 4. ISO/IEC 25010:2023 (software quality)

The 2023 edition (Edition 2, published November 2023) has nine characteristics. [Only the count was confirmed by the ISO page; the names below are from the standard as generally cited: confirm them against your copy.]

| Characteristic | What to evaluate in GlowUp |
|---|---|
| Functional suitability | scans, compares with the profile, results, AR try-on do what they should |
| Performance efficiency | scan time, AR frame rate, battery and memory on the test phones |
| Compatibility | works on the Android versions and screens tested |
| Interaction capability (called usability in the 2011 edition) | easy to learn and use, clear messages, accessible controls |
| Reliability | no crash, works with a weak connection, data not lost |
| Security | sign-in, lock-out, rules, what leaves the phone |
| Maintainability | admin site, scene builders, tests |
| Flexibility (called portability in the 2011 edition) | installs on other phones, data moves with the account |
| Safety | the app does not lead to harm: it flags ingredients, says it is not medical advice, says when to see a dermatologist |

## 5. Results, Conclusions, Recommendations (panel item)

[Write these only after the respondents' data is in.] Structure:
- Results: one subsection per objective, each with its weighted-mean table (section 3) and one paragraph that reads the table in plain words.
- Conclusions: one sentence per objective, each sentence answering that objective only and pointing to its table. No sentence may contain a figure that is not in a table.
- Recommendations: one per limitation actually found (for example: six tones are coarse, a camera reading depends on light, Philippine prevalence data missing, no study of AR finishes).

## 6. Limitations to add (from this round)

- The skin tone selfie is a suggestion; lighting changes what a camera sees.
- The selfie is kept on the phone only.
- The six-digit e-mail code and the notifications are built but were not tested on a phone at the time of writing.
- Product data comes from Open Beauty Facts (edited by anyone), the FDA Philippines list as copied on 13 October 2026 and the brand's own pages; none of them is complete.

## 7. Formatting and calendar (panel items)

[Not changed here: they are in the paper file, not in this repository. Check them there against your institution's template: margins, font, spacing, heading levels, table and figure captions, list of tables, and the Gantt chart or calendar with real dates.]
