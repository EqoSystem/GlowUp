# Paper revisions: text to paste in (panel comments, 22 Nov 2026)

Rules for this file: nothing here is a result. Every number that must come from your respondents is a blank (`__`). Statements about studies are limited to what the sources below say; check each against the full paper before you cite it. Paste the sections into your paper; the notes in [brackets] are for you and are not part of the paper.

## 1. Purpose and Description (panel item)

GlowUp is an Android application for make-up that reads a product with the phone camera (barcode, name and ingredient list), compares the ingredients with the skin profile the user saved on the phone (skin type, skin tone, chronic skin conditions and allergens) and tells the user, product by product, whether anything is flagged. It also lets the user try a product on with augmented reality (AR). The skin profile is kept on the phone and in the user's own account; it is never sent to the AI service. A web portal lets administrators keep the product, brand and ingredient data correct. GlowUp is information, not medical advice.

Purpose: to help make-up users with skin sensitivities avoid ingredients that do not suit their skin, and to make it quick to check a product before buying it.

## 2. Studies behind the lists in the app (Review of Related Literature)

[Rule for this section: every source is from 2024 to 2027. Older classics (the 1975 Fitzpatrick paper, Baumann 2008) are NOT cited; the scales are named through the recent papers that discuss them. Year shown in brackets after each source; check each against the full text before you cite it.]

**Skin tone (Step 3).**
- A 2025 letter in the *Journal of Drugs in Dermatology* reports a large multi-ethnic comparison of the Fitzpatrick and Monk skin tone scales and notes that the Fitzpatrick scale, built to describe how skin reacts to the sun, is commonly used as a stand-in for skin tone although it was made for another purpose. [2025, https://jddonline.com/articles/dermatology/S1545961625P8618X]
- Diamantidis et al. compared the two scales with measured pigmentation in 517 people: the 10-shade Monk scale agreed more closely than the 6-type Fitzpatrick scale (weighted kappa 0.91 against 0.76), and large disagreements were more common with Fitzpatrick; the authors still recommend objective measurement where possible. [2026, poster ID 25, AI in Dermatology Summit, Medical University of Vienna, https://dermatologie.meduniwien.ac.at/fileadmin/content/OE/dermatologie/AI_in_Dermatologie/Posters_AI_Summit_2026/Poster_ID_25.pdf] [A poster, not a journal article: say so in the text.]
- GlowUp groups skin tones into six tiles (Fair, Light, Medium, Tan, Deep, Dark) and can suggest one from a selfie. Limitations to state: six tiles are coarser than the 10-shade Monk scale, and a camera reading changes with the light.

**Skin type (Step 2).**
- Brzozowska et al. created and validated a Polish version of the Baumann Skin Type Questionnaire with 103 participants aged 18 to 35, covering the oily/dry and sensitive/resistant dimensions; the Baumann approach holds that the everyday labels (normal, dry, oily, combination) say little on their own and that self-labelling is unreliable. [2025, *Postępy Dermatologii i Alergologii*, https://www.termedia.pl/Baumann-Skin-Type-Questionnaire-BSTQ-creation-and-validation-of-the-Polish-language-version-part-one,7,55953,1,0.html]
- A 2024 article in the *Journal of Cosmetic Science* explains that in the Baumann system "combination" is not a true skin type: people who call themselves combination usually see their skin move between dry and oily with the season. [*J. Cosmet. Sci.* vol. 75 no. 5, https://library.scconline.org/v075n05/98] [CHECK THE YEAR before citing: one search listed this article as 2023. If it is older than 2024, drop this point.]
- GlowUp keeps the four everyday types for simplicity, offers a short "Find my skin type" quiz instead of self-labelling, and handles sensitivity through the allergen step. State the simplification as a limitation.

**Chronic skin conditions (Step 2).**
- Recent Global Burden of Disease analyses show that inflammatory skin diseases remain a large and growing burden: global acne incidence rose by more than 60% between 1990 and 2023, and atopic dermatitis is the largest contributor to skin-disease disability worldwide. [2026, *Clinical, Cosmetic and Investigational Dermatology* vol. 19, https://www.dovepress.com/diverging-global-patterns-and-temporal-trends-in-inflammatory-and-infe-peer-reviewed-fulltext-article-CCID; Healio news report of the GBD 2023 acne analysis, 4 Nov 2026, https://www.healio.com/news/dermatology/20261104/global-acne-rates-increased-more-than-60-since-1990]
- A GBD 2021 study of immune-related skin diseases (1991 to 2021) reports that women are affected disproportionately. [2025, PMC12644009, https://www.ncbi.nlm.nih.gov/pmc/articles/PMC12644009/]
- In people under 20, psoriasis and atopic dermatitis burden rises with a country's sociodemographic index, while acne is concentrated in lower-index regions. [2025, PMC12814692, https://www.ncbi.nlm.nih.gov/pmc/articles/PMC12814692/]
- Philippine prevalence figures from 2024 to 2027 were NOT found for acne, atopic dermatitis, psoriasis, rosacea, seborrheic dermatitis or melasma. Write that as a gap, or add a 2024+ Philippine Dermatological Society source if you find one. Rosacea, seborrheic dermatitis, melasma and contact dermatitis are not covered by these GBD papers: keep them on the list as conditions that make-up ingredients are known to affect, and cite a 2024+ dermatology source for each before submitting.

**AR textures (finishes) and virtual try-on.**
- GlowUp offers five finishes (matte, satin, glossy, shimmer, metallic). It starts with the finish read from the product, then the user's last choice, then a guess from the kind of product, and it says on screen which one it used. Present this as a design choice: no 2024 to 2027 study was found that compares matte and gloss rendering in AR.
- Micheletto et al. studied cosmetic virtual try-on apps and tested perceived informativeness and ease of use as predictors of trust, doubt, make-up involvement, perceived diagnosticity and behavioural intention, with enjoyment as a further factor. [2025, *Frontiers in Virtual Reality*, https://www.frontiersin.org/journals/virtual-reality/articles/10.3389/frvir.2025.1515937/full]
- Kumpan et al. found that technological realism of AR-AI virtual make-up try-on had a significant positive effect on purchase intention (beta 0.333, p < 0.001), while ease of use had a small negative effect and trust and transparency had none. [2025, accepted June 2025, https://so03.tci-thaijo.org/index.php/journalcim/article/download/287491/189151/1276344] [Read the full text before citing the numbers.]
- Realism is the common thread: this supports GlowUp's choice to render the finish (shine, sparkle) rather than a flat colour.

**Software quality.** ISO/IEC 25010:2023 (published November 2023) is the standard the panel asked for. It is a standard, not a study, so the 2024 to 2027 rule does not apply to it; say so if asked.

[Removed from the earlier draft because they are older than 2024: Fitzpatrick 1975/1988, Baumann 2008 (*Dermatologic Clinics*), the 2023 *Cosmetics* review, the 2022 Monk scale release, the 2018 ISPOR abstract on psoriasis in the Philippines.]

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
