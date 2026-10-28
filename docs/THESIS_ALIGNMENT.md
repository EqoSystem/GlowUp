# Thesis alignment: Objectives, Scope and Limitations against the code

Checked on 28 October 2026.

**Limit of this check.** The thesis PDF (`GLOWUP3.pdf`) is **not in the repository** (`docs/` holds an unrelated game design, `Game Officialll.pdf`, and
this project's own technical documents). What the thesis promises is taken from four pages the owner sent on 13 October 2026, as recorded then:
the Objectives, the Scope (admin and user modules) and the Limitations. The Review of Related Literature, Studies and Systems could not be read, so
**no citation was checked** (section 4). Re-run this check against the PDF when it is added to `docs/thesis/`.

The code is the source of truth for what the app does. Where they differ, the suggestion is to adjust the paper's wording (rule: the paper adapts to the
app), unless a small, safe change in the app was enough.

## 1. Objectives

| Objective (as summarised) | In the code? | Evidence | Gap / note |
|---|---|---|---|
| 1. A safety feature that compares a product's ingredients with the user's sensitivities and alerts them, "cross-referencing extracted chemicals against personal and regulatory databases" | **Partly** | `Scan/ProductAnalysisEngine.cs` (profile comparison on the phone), `Scan/SafetyAlerts.cs` (alerts), ingredient entries rated from regulator pages (`source: "fda"` entries from the US FDA prohibited and restricted list; the Harmful rule of `DATA_SOURCE_POLICY.md`) | The "regulatory database" is the admin-curated ingredient list, partly sourced from regulator pages. The app does not query a regulator's database live. Wording fix in `THESIS_FIXES.md` |
| 2. A Skin Profile that stores sensitivities and allergies for automatic cross-referencing | **Yes** | Onboarding Steps 1 to 5, `Data/DataModels.cs` (`UserProfileData`), `users/{uid}`, Settings > retake (`SkinProfileResetAction.cs`) | None |
| 3. An AI recognition and recommendation system: scans make-up for a detailed ingredient analysis and suggests products for the skin profile ("ingredients and performance benefits") | **Partly** | Recognition: barcode, name, pack look, then AI (`ScanShortlist`, `PackEmbedder`, `ProductAiAdvisor`). Analysis: `ProductAnalysisEngine`. Suggestions: `ProductRecommender` (make-up only, nothing flagged for the profile; stricter with a skin condition) | The **recommendation is rule-based**, not AI. "Performance benefits" exist only as ingredient highlights (key actives, "good for" skin types and conditions); there is no performance rating of a product. Wording fix |
| 4. AR virtual try-on in real time, before purchase | **Yes** | `Scripts/AR/` (AR Foundation + ARCore 5.2.2 face mesh), guided preview when ARCore is missing | Real-time only on phones with ARCore; others get the guided preview. Already a limitation (hardware) |

## 2. Scope

### Admin modules

| Module | In the code? | Evidence |
|---|---|---|
| Login | Yes | `_Index/` (admins only: `admins/{uid}`) |
| Dashboard | Yes | `_Admin_Dashboard/` (counts and charts read from Firestore; no sample numbers found) |
| Brand Inventory | Yes, as **Brand & Product Inventory** | `_Admin_Brand_Inventory/` |
| Ingredient Master List, "simplified definitions in accessible language" | Yes, as **Ingredient Curator** | `_Admin_Ingredient_Curator/` (`description` = plain words, `chemicalDescription` = technical) |

In the code but **not in the Scope**: Recycle Bin page; file import (PDF, Word, Excel, CSV, text); Assist with AI (drafts with sources); FDA Philippines
look-up; Pending Snapshot Reviews (products users photographed); duplicate finder and clean-up; editable allergen / condition lists.

### User modules

| Module | In the code? | Evidence |
|---|---|---|
| Authentication | Yes | `LogInScene`, `Scripts/Auth/` (e-mail and password with e-mail confirmation, Google, Facebook) |
| 5-step Skin Profile Onboarding | Yes | `OnboardingStep1Scene` to `OnboardingStep5Scene`, then Terms |
| Skin Profile | Yes | Settings (account info, retake the quiz) |
| User Dashboard | Yes | `UserDashboardScene` |
| Recent Scan Activities | Yes | `RecentScanActivitiesScene` |
| AI Smart Analysis, "simplified safety ratings based on personal sensitivity profiles" | Yes | `AiSmartAnalysisScene`, `LoadingScene`, `AiSmartAnalysisResultScene` (safe / caution / harmful per ingredient, "Not checked" for unknown names) |
| AI Virtual Try-On, scan-to-try-on, and "manually search for a product if they don't have the physical product" | Yes | `ARTryOnScene`: opened from a result, or a product searched by name (`msgNothingFound` "No make-up found for that search") |

In the code but **not in the Scope**: Settings page (theme, sound, AR quality, frame-rate cap, notifications), Delete My Account, finger-sign navigation,
typed ingredient entry, product photo snapshot for unknown products, FDA Philippines badge, offline use with later sync.

## 3. Limitations

| Limitation | True, handled, shown? | Evidence |
|---|---|---|
| Hardware (camera, AR support) | True and handled | Min Android SDK 23; AR falls back to a guided preview without ARCore |
| Lighting | Handled | `FrameQuality` drops dark, glare, blurry and moving frames and the scan screen says why; flashlight during a scan |
| Data accuracy depends on database updates | True | The app syncs changes; an entry is only as good as its review (`reviewed`, `reviewedBy`) |
| Alerts limited to ingredients stored in the database | True and **shown** | A name that is not in the table is "Not checked", never "safe"; the result says how many names were checked |
| Informational only, not medical advice | Shown | Terms section 3; notes on the dashboard and the result screen. **Not** shown on the AR screen itself (see `PRIVACY_NOTES.md`, 4.8) |
| Admin verification limited by third-party sources | True; now governed | `DATA_SOURCE_POLICY.md`: which kind of source may support which claim, enforced for Harmful |

Missing from the paper's Limitations, but true in the code: OCR cannot be promised to read every ingredient on curved or glossy packs (see
`tools/ScannerEval/README.md`); shade colours on screen are approximate; FDA Philippines status proves notification, not ingredients; the app's own AI
calls need the internet and a free quota.

## 4. Review of Related Literature, Studies and Systems

**Blocked**: the PDF is not in the repository, so in-text citations and reference entries could not be compared, and no source could be checked.
Nothing was invented or "fixed". When the PDF is added, check: (a) every in-text citation has a reference entry with the same author, year and title,
and the reverse; (b) the sentences that say what GlowUp **does** against the table below.

Claims about GlowUp that are likely in those sections (from the owner's brief), and what the code supports:

| Claim | Verdict | What the code does |
|---|---|---|
| The database is restricted to FDA-notified cosmetics | **Not supported** | Products are not required to be FDA-notified. The app checks the FDA Philippines portal per product and shows the answer as a badge; admins can link the verification page |
| Cross-referencing against a curated ingredient and colour database | **Partly** | The ingredient database is curated (review, sources, tiers). Colours are per product (`shades` + shade codes), not a separate colour database |
| Reading packaging text with AI text recognition | **Supported, with a wording note** | On-device text recognition (Google ML Kit, a machine-learning OCR); a generative AI (Gemini) is used only when the app cannot name the product or a list is missing |
| Personalised shade matching | **Partly** | `ShadeAdvisor.Recommend(skinTone, area)` suggests shades from the skin tone the user chose in onboarding; the camera does not measure skin colour |

## 5. Small fixes made in the app because of this check

- Website wording: "Technical / Scientific Name" now reads **"INCI Name (as printed on labels)"** in the Ingredient Curator and its pickers (the field holds the label name).
- Privacy policy, section 8: describes Delete My Account (it already existed) instead of "contact support".
