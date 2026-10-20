# GlowUp database: what the website and the app share

The admin website and the mobile app use the same Firebase project (`glowup-admin-portal`) and the same Firestore database. This page lists what is stored where, who writes it, and the two setup steps the security rules need.

## Collections at a glance

| Collection | Written by | Read by | What it is |
|---|---|---|---|
| `brands` | Website (Brand & Product Inventory). The app adds 1 to `scans`, and adds a brand it meets in a scan as Pending. | App, website | Cosmetic brands |
| `products` | Website (Brand & Product Inventory). The app adds 1 to `scans`, and adds a scanned product that is not listed yet as Pending. | App, website | Products the app can recommend and recognise |
| `productIngredientSuggestions` | App | Website (Brand & Product Inventory) | Web-found lists for existing products, pending admin review |
| `ingredients` | Website (Ingredient Curator). The app adds ingredients its AI lookup finds, marked as not reviewed. | App, website | Ingredient explanations and safety details |
| `scanActivity` | App | Website (Dashboard) | One anonymous note per scan |
| `allergenSuggestions`, `conditionSuggestions` | App | Website (Dashboard) | Items users typed in that were not on the app's lists |
| `users/{uid}` and `users/{uid}/scans` | App | App (that user only) | A user's profile and scan history |
| `admins/{uid}` | You, by hand in the Firebase console | Rules, website | Which accounts are admins |

## Fields

### `brands/{id}`

| Field | Type | Notes |
|---|---|---|
| `name` | text | The app looks for this name on scanned labels, so write it as printed on the packaging. |
| `category` | text | Always `Makeup`: GlowUp lists make-up brands only. (Older entries may still say Skincare or Haircare until the clean-up has run.) |
| `aliases` | list of text | Optional. Other ways the brand's name is written. A brand that was merged into this one leaves its name here, so it is still found. |
| `market` | text | Defaults to Global. |
| `status` | text | `Active`, `Pending` or `Inactive`. The app uses only Active brands. |
| `scans` | number | The app adds 1 each time a scan is found to be this brand: its name is on the scanned label, or the scan is one of its products. |
| `source`, `reviewed` | text, yes/no | Entries imported from a file use `document-import` and `false` until an admin checks them. |
| `aiAssistSources`, `aiAssistModel` | lists/text | Source URLs and model for an admin-reviewed AI suggestion to market or aliases. Existing aliases are merged, not discarded. |
| `createdAt`, `updatedAt` | time | Set by the website. |

### `products/{id}`

| Field | Type | Notes |
|---|---|---|
| `name` | text | |
| `brandId` | text | The id of the brand document. |
| `brandName` | text | A copy of the brand's name. Renaming a brand on the website updates it. |
| `cosmeticType` | text | The kind of make-up, in the app's own words: Lipstick, Lip Gloss, Lip Tint, Lip Liner, Foundation, Concealer, Primer, Powder, Blush, Bronzer, Highlighter, Contour, Eyeshadow, Eyeliner, Mascara, Brow, Setting Spray. It tells AR Try-On where the colour goes. |
| `aliases` | list of text | Optional. Other names of the product: an old name, a name with the brand in front, the name of a copy that was merged into it. Search and the duplicate check look at them too. |
| `description` | text | Optional. |
| `ingredients` | list of text | The names from the packaging. |
| `shades` | list of text | Colours as `#RRGGBB`. Used by AR Try-On. |
| `sources` | list of text | Supporting web pages. An FDA Philippines verification link is saved here only when it is present in the document or a Gemini web-search result. |
| `minAge` | number | 0 means no limit. A younger user who scans the product is warned. |
| `status` | text | `Active`, `Pending` or `Inactive`. |
| `scans` | number | The app adds 1 each time a scan is recognised as this product. |
| `source`, `reviewed` | text, yes/no | File imports use `document-import` and `false`; they are saved as Pending until checked. |
| `aiAssistModel` | text | Model used for an admin-reviewed ingredient-list suggestion. Existing ingredient lists are not overwritten. |
| `createdAt`, `updatedAt` | time | Set by the website. |

Brand & Product Inventory can extract text from PDF, Excel (`.xlsx`/`.xls`), and Word (`.docx`) files and create new matching entries. Only extracted text is sent to the admin-only AI endpoint; imported records are never activated or used to overwrite a matching existing product. Image-only/scanned PDFs and legacy `.doc` files are not supported.

The admin AI assistant drafts ingredient descriptions, product ingredient lists, and brand market/name aliases with source pages. Selected entries can be assisted in bulk, but each draft must be individually checked before the review queue's **Approve reviewed drafts** action saves it. Ingredient assistance changes only the description and its evidence; it does not generate or update risk ratings. Product ingredient assistance only fills an empty list. Gemini is tried first, then Groq, then Mistral when the preceding provider is exhausted. Groq retries with supported current models if a configured model has been retired or returns not found. Backup providers do not return web citations, so their drafts need extra checking.

The selection bar's **Approve All** action applies only to selected entries that are still waiting for review and meet the inventory's existing eligibility checks. In Brand & Product Inventory, a product needs a make-up type, a listed brand and a non-empty ingredient list. The optional `productIngredientSuggestions` collection is admin-only; if its read is denied, the brand and product catalogue remains visible and the page displays a separate Firestore-admin warning.

### `productIngredientSuggestions/{id}`

The app creates a proposal when a product already in the catalogue has no ingredient list and a web lookup finds one. It never edits the catalogue entry itself. An admin sees the proposal and its cited sources in Brand & Product Inventory; applying it only fills a still-empty list, while dismissal deletes the proposal.

| Field | Type | Notes |
|---|---|---|
| `productId`, `productName`, `brandName` | text | The existing catalogue product the proposal is for. |
| `ingredients` | list of text | The AI-found list; 3 to 80 names. |
| `sources` | list of text | Web pages supporting the lookup. |
| `source`, `status`, `reviewed` | text, text, yes/no | Always `web`, `Pending`, `false` when created by the app. |
| `addedBy`, `addedAtUnix` | text, number (seconds) | The signed-in app user and proposal time. |

The app recognises a scan as a product by comparing ingredient lists: at least 4 ingredients must be the same, at least 70% of the product's ingredients must be in the scan, and at least 60% of the scanned ingredients must belong to the product. These limits are Inspector fields on the app's `LoadingSceneController`. A recognised scan takes the product's name, brand, type and shades.

The app offers a product to a user only when all of these are true: the product is Active, its brand is Active, it has ingredients, the user is at least `minAge`, and the app's analysis of the ingredients comes out safe for that user's profile.

### `ingredients/{id}`

| Field | Type | Notes |
|---|---|---|
| `name` | text | The friendly name users read. |
| `techName` | text | The name printed on labels. Scans are matched against it. |
| `description` | text | The user-friendly definition. |
| `aliases` | list of text | Other spellings found on labels. |
| `riskLevel` | text | `safe`, `caution`, `harmful`, or empty to let the app decide. |
| `cautionNote` | text | Finishes the sentence "It ...", for example `can clog pores`. |
| `commonUses` | text | For example `serums, moisturisers`. |
| `minAge` | number | 0 means no limit. |
| `keyActive` | yes/no | Shown as a highlighted chip in results. |
| `umbrella` | yes/no | A term that can hide other ingredients, such as Fragrance. |
| `allergenTags` | list of text | Any of `fragrance`, `paraben`, `sulfate`, `formaldehyde`, `lanolin`, `essentialoil`, `nut`. |
| `avoidForSkinTypes`, `goodForSkinTypes` | list of text | Any of `Oily`, `Normal`, `Dry`, `Mix`. |
| `avoidForConditions`, `goodForConditions` | list of text | Any of `acne`, `eczema`, `psoriasis`, `rosacea`, `seborrheic`, `melasma`, `contact`. |

Only `name`, `techName` and `description` are required. The app has its own built-in list of common ingredients. When an ingredient is also in this collection, the website's values win, and any field left empty here is filled from the app's built-in list.

The allergen, skin type and condition words must be spelled exactly as above. The website's tick-boxes write them for you.

### Entries that were not typed on the website

A brand, product or ingredient that the app added by itself, or that came from a data import, carries extra fields. An entry typed on the website has none of them.

| Field | Type | Notes |
|---|---|---|
| `source` | text | Where the entry came from. See the table below. |
| `reviewed` | yes/no | `false` until an admin approves the entry or opens it and saves it on the website. |
| `reviewedAt` | time | Set by the website when it is approved. |
| `addedBy` | text | The id of the app user whose scan added it. Only on entries the app added. |
| `addedAtUnix` | number (seconds) | When it was added. |
| `sources` | list of text | Web pages the information came from, each as `page title | address`. On ingredients and imported products. |
| `fdaUnii` | text | Ingredients only. The US FDA substance registry's code for the name, when the registry lists it. It shows the name is a registered substance. It says nothing about safety. |
| `webSearched`, `model`, `aiAssistModel` | yes/no, text | AI-found ingredients record the web search and model. A bulk AI description assist records its model separately, keeps the sources, and changes only the description and review state. |
| `barcode` | text | Products only: the digits under the barcode. On imported products, and on products the app added from a scan in which the camera saw the barcode. The app's scan screen finds a product by it and reuses its saved ingredient list. |

| `source` | On | Document id starts with | Meaning |
|---|---|---|---|
| `ai` | ingredients | `ai_` | The app's AI lookup found it during an online scan. |
| `scan` | products, brands | `scan_` | A user scanned a product (or a brand name) that was not in the inventory. |
| `starter` | ingredients | `starter_` | Copied from the app's built-in ingredient list on 9 October 2026. |
| `fda` | ingredients | `fda_` | From the US FDA page "Prohibited & Restricted Ingredients in Cosmetics". |
| `openbeautyfacts` | products, brands | `obf_` | Imported from Open Beauty Facts on 9 October 2026. |

How the app treats them:

- **A product or brand that is waiting for review** always has status `Pending`, so the app does not recommend it or recognise scans as it. The app only counts scans on it. It becomes usable when an admin presses Approve on the website, which sets it to `Active`.
- **An AI-found ingredient that is waiting for review** is used with care. The app shows its description with the note "Found by the AI lookup; not yet reviewed". It uses the entry's warnings. It never counts the entry as proof that an ingredient is safe: an AI answer of "safe" is shown as "not checked" until an admin approves it. It never replaces what the app's built-in list or an admin's entry says.
- **Starter and FDA ingredients** are trusted like entries typed on the website, because they hold the same text the app already used before they were copied here.

Open Beauty Facts is an open database of product labels typed in by volunteers, so its ingredient lists can contain mistakes. Compare each list with the packaging before approving a product. Its data is published under the Open Database License, which asks that the source is named. The website shows the source link on every imported product.

Neither the US FDA nor FDA Philippines publishes a list of cosmetic products with their ingredients that a program can read. For a product sold in the Philippines, an admin can look up its notification by hand on the [FDA Philippines Verification Portal](https://verification.fda.gov.ph/).

### Folders and the Recycle bin (website only)

The Brand & Product Inventory and the Ingredient Curator let an admin arrange entries in folders and remove them to a Recycle bin. Both are plain fields on the entry itself, so every admin sees the same arrangement on every computer.

| Field | On | Notes |
|---|---|---|
| `folder` | products, ingredients | The name of the folder the entry is in (30 characters at most). Empty or missing = no folder. An entry is in one folder at most. The app ignores it. |
| `binned` | brands, products, ingredients | `true` = in the Recycle bin. |
| `binnedAt` | brands, products, ingredients | When it went there. |
| `statusBeforeBin` | brands, products | The status it had. "Restore" puts it back. |
| `binnedWith` | products | The id of the brand it went to the bin with. Restoring that brand restores these products too. |
| `binnedReason` | brands, products, ingredients | Optional. Why it is in the bin, when a tool put it there ("Not make-up: a hair product.", "The same product as ...: merged into it."). Restore clears it. |

- Moving a brand or a product to the bin also sets its `status` to `Inactive` in the same write, so every build of the app stops using it at once.
- An ingredient has no status. App builds from 2026-10-10 on leave a `binned` ingredient out; an older build keeps reading it until "Delete forever".
- Only "Delete forever" (or "Empty bin") deletes a document. A folder that is still empty is remembered only in the browser that made it.

### Make-up only

GlowUp lists make-up only. Skin care, hair, body, nail and fragrance products are not added, not shown and not recommended.

The rule is written twice, word for word, and both copies are tested against the same lists of examples (`MakeupScopeCases.txt` and `SharedRuleCases.txt` in the app's `Assets/_Project/Editor/Tests`):

- the app: `ProductScope.cs` (with `TextKey.cs`, `ProductIndex.cs`, `IngredientStoplist.cs`)
- the website: `_Shared/glowupRules.js`. Run `node tests/rules.test.mjs` in the website folder after changing either side.

What the rule says about a product, from its kind and its name:

| Answer | Meaning | What happens |
|---|---|---|
| make-up | foundation, concealer, powder, blush, bronzer, highlighter, contour, eyeshadow, eyeliner, mascara, brow products, lipstick, lip gloss, lip tint, lip liner, primer, setting spray | Listed. The app checks it, recommends it and offers AR Try-On. |
| skin care / other | moisturizer, serum, cleanser, sunscreen, make-up remover, shampoo, body lotion, nail polish, perfume ... | The website refuses to save it. Existing ones wait under "Not make-up" until an admin moves them to the Recycle bin. The app does not add one from a scan. |
| not clear | a name and kind that say nothing clear, or a kind the project has not decided on yet (tinted moisturizer, skin tint, BB and CC cream, lip balm, lip oil, tinted sunscreen) | The website saves it only as Pending. The app hides it until an admin has given it a clear kind. |

The security rules still hold the older, looser check (`faceProduct()` in `firestore.rules`): it is tightened to the same list of kinds when the next rule change is published.

On the Brand & Product Inventory page, "Clean up" lists every change the rules propose (what is not make-up, what is there twice, kinds and names to put right) and applies one step at a time after the admin confirms. "Find duplicates" lists entries that are, or may be, the same thing. Neither deletes anything: what is removed goes to the Recycle bin. `tools/cleanupReport.mjs` makes the same list from a saved copy of the data without touching the database.

### `scanActivity/{id}`

Written by the app after every scan. It holds no user id, name or email.

| Field | Type |
|---|---|
| `productName`, `brandId`, `brandName`, `cosmeticType` | text |
| `status` | `safe`, `caution` or `harmful` |
| `source` | `scan` or `manual` (typed in) |
| `scannedAtUnix` | number (seconds) |
| `ingredientCount`, `harmfulCount`, `cautionCount` | number |
| `allergenFlagged` | yes/no: one of the user's own allergens was found |
| `flaggedIngredients` | list of text (names only) |

The Dashboard's "Total Scans Today" counts notes since midnight on the computer showing the dashboard. "Flagged Allergens" counts today's notes where `allergenFlagged` is yes.

### `allergenSuggestions/{id}` and `conditionSuggestions/{id}`

| Field | Type | Notes |
|---|---|---|
| `text` | text | What the user typed. |
| `uid` | text | The user who typed it. |
| `status` | text | `pending` from the app. The Dashboard changes it to `reviewed` or `dismissed`. |
| `createdAtUnix` | number (seconds) | |
| `reviewedAt` | time | Set by the Dashboard. |

## Admin accounts

The website and the app share one list of sign-in accounts. Without the two steps below, anyone who creates an account in the app can also sign in to the admin website.

Both steps were done on 9 October 2026 for the account `admin07@gmail.com`. They are kept here for adding another admin later, or for setting up a new Firebase project.

1. **Add yourself to `admins`.** In the Firebase console open Authentication > Users and copy the User UID of your admin account. Then open Firestore Database > Start collection, name it `admins`, and use that UID as the Document ID. Add one field, for example `note` = `owner`. Repeat for every admin.
2. **Publish the rules.** Open Firestore Database > Rules, paste the whole of [firestore.rules](firestore.rules), and press Publish.

Do step 1 first. If you publish the rules before your account is in `admins`, the website can still show data but every Save and Delete is refused with "permission denied".

In [_Shared/adminCommon.js](_Shared/adminCommon.js), `REQUIRE_ADMIN_RECORD` is `true`: the website turns away accounts that are not in `admins` at the login page. The rules protect the data without this setting; it only makes the refusal clearer.

## What the rules allow

| Who | Can do |
|---|---|
| Anyone, signed in or not | Read `brands`, `products`, `ingredients` |
| A signed-in app user | Read and write their own `users/{uid}` and its `scans`. Add 1 to a brand's or a product's `scans`. Add a `scanActivity` note. Add a suggestion under their own id. Add (never change or delete) an ingredient `ai_...`, a product `scan_...` or a brand `scan_...`, and only marked with their own id, `reviewed` false and, for products and brands, status `Pending`. Create a `productIngredientSuggestions` proposal, but not read or change it. |
| An admin | Add, edit and delete `brands`, `products`, `ingredients`. Read and delete `scanActivity`. Read and process product ingredient suggestions. |
| Nobody (console only) | Change `admins` |

An admin account cannot read users' profiles or scan histories. Everything not listed is refused.

The rules in [firestore.rules](firestore.rules) were previously checked on the local Firestore emulator (126 checks) and published to the project on 9 October 2026. Publish this updated file before using AI ingredient suggestions; until then, Firestore will reject the app's new suggestions.
