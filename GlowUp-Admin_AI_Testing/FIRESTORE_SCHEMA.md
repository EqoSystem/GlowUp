# GlowUp database: what the website and the app share

The admin website and the mobile app use the same Firebase project (`glowup-admin-portal`) and the same Firestore database. This page lists what is stored where, who writes it, and the two setup steps the security rules need.

## Collections at a glance

| Collection | Written by | Read by | What it is |
|---|---|---|---|
| `brands` | Website (Brand & Product Inventory). The app adds 1 to `scans`, and adds a brand it meets in a scan as Pending. | App, website | Cosmetic brands |
| `products` | Website (Brand & Product Inventory). The app adds 1 to `scans`, and adds a scanned product that is not listed yet as Pending. | App, website | Products the app can recommend and recognise |
| `productIngredientSuggestions` | App | Website (Brand & Product Inventory) | Web-found lists for existing products, pending admin review |
| `ingredients` | Website (Ingredient Curator). The app adds ingredients its AI lookup finds, marked as not reviewed. | App, website | Ingredient explanations and safety details |
| `config` (one document, `lists`) | Website (Ingredient Curator, "Edit list") | App, website | The editable Allergen Family, Highlights and Skin Conditions lists |
| `pending_products`, `pending_product_images` | App (AI photo snapshot) | App users (entries only), website (Pending Snapshot Reviews) | Products the catalogue does not have, read by the AI from one photo, waiting for an admin |
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
| `logoUrl`, `imageUrl` | text | Optional web addresses (https) of the brand's official logo and of a display or pack picture. Only the address is stored, nothing is uploaded. |
| `website` | text | The brand owner's own website (https), **confirmed by an admin** on the Brand form after checking that the brand owns it (printed on the pack, the same company as on the FDA Philippines record, or linked from the brand's official accounts). Only its pages, and pages under it, are labelled "Manufacturer's own site" in the app, on the website and in Assist with AI. Empty = no page is the maker's. The rules let only an admin set it, and only together with `websiteConfirmedBy` = their own account. |
| `websiteConfirmedBy`, `websiteConfirmedAt` | text, time | Who confirmed `website`, and when. Cleared together with it. |
| `websiteSuggested` | text | An address Assist with AI or a document import PROPOSED. Never trusted: it only offers the admin a "Use this address" button on the Brand form. |
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
| `sources` | list of text | Supporting web pages. The FDA Philippines verification link is the entry that reads `FDA Philippines verification | https://...fda.gov.ph/...` (the product form has its own box for it). |
| `imageUrl` | text | Optional web address (https) of a photo of the bottle, tube or compact. |
| `ingredientIds` | list of text | Light summary kept by the website's product form: the ids of the library ingredients this product's names match. It links a product to its ingredients without a join. |
| `ingredientCount` | number | How many ingredients the list has (a light summary). |
| `ingredientNotes` | list of maps | Optional per-product overrides, one per ingredient that needs one: `{ name, id, caution, percent }`. `caution` replaces the ingredient's own caution note for THIS product only; `percent` is how much of it is in this formula (0 to 100). Empty = the ingredient's own note applies. |
| `tags` | list of text | Words a product can be found by (kind, brand, `has-shades`, `no-ingredient-list`, `fda-listed`), at most 20. Lets a filter use one query on the product alone. |
| `minAge` | number | 0 means no limit. A younger user who scans the product is warned. |
| `status` | text | `Active`, `Pending` or `Inactive`. |
| `scans` | number | The app adds 1 each time a scan is recognised as this product. |
| `source`, `reviewed` | text, yes/no | File imports use `document-import` and `false`; they are saved as Pending until checked. |
| `aiAssistModel` | text | Model used for an admin-reviewed ingredient-list suggestion. Existing ingredient lists are not overwritten. |
| `createdAt`, `updatedAt` | time | Set by the website. |

Brand & Product Inventory can extract text from PDF, Excel (`.xlsx`/`.xls`), and Word (`.docx`) files and create new matching entries. Only extracted text is sent to the admin-only AI endpoint; imported records are never activated or used to overwrite a matching existing product. Image-only/scanned PDFs and legacy `.doc` files are not supported.

What an import reads: per product `brandName`, `productName`, `cosmeticType`, `description`, ingredients, shades (colours only: a shade NUMBER such as "#128" is never taken for a colour), sources, the FDA Philippines link, other names (`aliases`), `minAge`, `barcode` and a photo link (`imageUrl`); for a NEW brand its market, other names, logo and picture links, and its website as `websiteSuggested` only. Links are kept only when they are printed in the document or were opened by the web search. A record about a brand alone creates a new Pending brand. Records that are skin care or otherwise not make-up are **flagged** under the drop box and not saved; make-up whose kind is not clear is saved as Pending and flagged. For an existing product that has no ingredient list, the imported list goes to `productIngredientSuggestions` (source `document-import`) for an admin to apply.

The Ingredient Curator's import reads, per ingredient: name, label name, other names, description, technical description (`chemicalDescription`), risk level and caution note (only when the document states them), common uses, minimum age, allergen families, skin conditions (avoid / good), skin types (avoid / good), highlights, and links printed in the document (`sources`). Allergen, condition and highlight NAMES are matched to the page's own lists (`config/lists`); a name the lists do not have is left out and named in the import's summary.

The admin AI assistant drafts ingredient descriptions, product ingredient lists, and brand market/name aliases with source pages. Selected entries can be assisted in bulk, but each draft must be individually checked before the review queue's **Approve reviewed drafts** action saves it. Ingredient assistance changes only the description and its evidence; it does not generate or update risk ratings. Product ingredient assistance only fills an empty list. Gemini is tried first, then Groq, then Mistral when the preceding provider is exhausted. Groq retries with supported current models if a configured model has been retired or returns not found. Backup providers do not return web citations, so their drafts need extra checking.

The selection bar's **Approve All** action applies only to selected entries that are still waiting for review and meet the inventory's existing eligibility checks. In Brand & Product Inventory, a product needs a make-up type, a listed brand and a non-empty ingredient list. The optional `productIngredientSuggestions` collection is admin-only; if its read is denied, the brand and product catalogue remains visible and the page displays a separate Firestore-admin warning.

### `productIngredientSuggestions/{id}`

The app creates a proposal when a product already in the catalogue has no ingredient list and a web lookup finds one. It never edits the catalogue entry itself. An admin sees the proposal and its cited sources in Brand & Product Inventory; applying it only fills a still-empty list, while dismissal deletes the proposal.

| Field | Type | Notes |
|---|---|---|
| `productId`, `productName`, `brandName` | text | The existing catalogue product the proposal is for. |
| `ingredients` | list of text | The AI-found list; 3 to 80 names. |
| `sources` | list of text | Web pages supporting the lookup. |
| `source`, `status`, `reviewed` | text, text, yes/no | `web`, `Pending`, `false` when created by the app; `document-import` when an admin's file import found a list for a product that had none. |
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
| `chemicalDescription` | text | Optional. What the substance is, technically (the plain-words definition stays in `description`). |
| `highlights` | list of text | Ids of the custom highlights of `config/lists` (the built-in ones are `keyActive` and `umbrella` above). A custom highlight makes the ingredient show up among the chips of a scan result, like a key active. |
| `allergenTags` | list of text | Ids of the allergen families in `config/lists` (built in: `fragrance`, `paraben`, `sulfate`, `formaldehyde`, `lanolin`, `essentialoil`, `nut`). |
| `avoidForSkinTypes`, `goodForSkinTypes` | list of text | Any of `Oily`, `Normal`, `Dry`, `Mix`. |
| `avoidForConditions`, `goodForConditions` | list of text | Ids of the skin conditions in `config/lists` (built in: `acne`, `eczema`, `psoriasis`, `rosacea`, `seborrheic`, `melasma`, `contact`). |

Only `name`, `techName` and `description` are required. The app has its own built-in list of common ingredients. When an ingredient is also in this collection, the website's values win, and any field left empty here is filled from the app's built-in list.

The allergen, skin type and condition ids must be spelled exactly. The website's tick-boxes write them for you.

### How the data is split, and what keeps the free tier safe

- **One master record per ingredient** (`ingredients`), written once however many products use it. **One record per product** (`products`) with only what belongs to that physical item. A product points at its ingredients by name and, as a light summary, by id (`ingredientIds`); a note that differs for one product lives on that product (`ingredientNotes`), and an empty note falls back to the ingredient's own.
- **No mass updates.** When an ingredient's warning changes, products are NOT rewritten: the app reads the product and the ingredient list and works the verdict out then, so it is always current. Only links and counts are kept on a product, never a safety verdict (a copy would go stale).
- **Reads.** The website keeps each list in the browser and downloads only what changed (adminStore.js); search, filters and CSV export work on what is loaded and cost no reads. An export holds at most the newest 100 products (`EXPORT_MAX_ROWS` in `_Shared/adminExport.js`).
- **Writes.** Public users and the app can read the catalogue; only admin accounts write it, and the app may only create Pending entries and add 1 to `scans`. The rules also refuse an admin write whose name is not text, whose lists are absurdly long, or whose `imageUrl` / `logoUrl` is not an https address.
- **Not built:** image upload to Firebase Storage (Cloud Storage needs the Blaze plan on new projects, and no Storage rules exist), so pictures are stored as web addresses; a visual-similarity search (there is no vector search in Firestore: the metadata filters do that job).

### `pending_products/{id}` (AI photo snapshots, waiting for an admin)

When a scanned product is not in `products` (and nobody staged it yet), the app offers to take ONE photo. The AI reads the brand, name, kind, shade and the printed ingredient list; the result is analysed at once and shown with a **PENDING APPROVAL** badge. The id is `snap_<brand>__<name>` (letters, digits and underscores), so every user who snaps the same product lands on the same entry. Kept apart from `products` so unverified user content never mixes with the curated catalogue.

| Field (PDF name) | Type | Notes |
|---|---|---|
| document id (`product_id`) | text | `snap_<brand>__<name>`, made by the app. |
| `brandName`, `name` (`brand_name`, `product_name`) | text | What the AI read. Field names follow the rest of this database (camelCase). |
| `cosmeticType`, `shadeHex` | text | Make-up only (the rules check the kind). |
| `ingredients` (`extracted_ingredients`) | list of text | Read from the photo only, at most 80. |
| `hasImage` (instead of `image_url`) | yes/no | The photo is in `pending_product_images` under the same id. Firebase Storage is not used: it needs the Blaze plan. |
| `isApproved` (`is_approved`) | yes/no | `false` from the app. The website sets `true` (with `status: "approved"`, `approvedProductId`, `reviewedAt`) when an admin approves. |
| `createdByUid` (`created_by_uid`) | text | The signed-in user who sent it (the rules require it to be their own id). |
| `status`, `source`, `key`, `confidence`, `model`, `createdAtUnix` | | `pending`, `snapshot`, the comparable name, how sure the AI was (0 to 1), which AI, when. |

`pending_product_images/{same id}`: `jpegBase64` (a JPG of at most about 80 KB, max 1080 px; WebP cannot be made in Unity), `bytes`, `createdByUid`, `createdAtUnix`. Only admins can read it; phones never download it. It is deleted when the entry is approved or rejected.

**Approve** (website: the Pending Snapshot Reviews window in Brand & Product Inventory) is one batch: the brand (found by name, or added as Active), the product (added as Active with `source: "snapshot"`; an existing entry without a list gets this list), every ingredient the library does not have (added with `source: "ai"`, `reviewed: false`, `via: "snapshot"`, so the Curator lists it as waiting and the app uses it as a warning at most), and the pending entry marked approved. **Reject** deletes the entry and its photo.

**Reads and quotas.** The app looks for a staged entry only after the catalogue has no match: its own copy first (0 reads), then one read of the entry by id; a miss is remembered for 30 minutes. A photo costs one AI request at most (the scan screen's AI budget), the button locks for 5 seconds, and the same product later comes from the phone's copy (0 reads, 0 AI). The website reads one count for the badge when the Inventory opens; the waiting entries (at most 50) only when the review window is opened, and a photo only when its card is on screen (1 read). The Dashboard tile is one count query.

### `config/lists` (one document, written by the website only)

| Field | Type | Notes |
|---|---|---|
| `version` | number | Goes up by 1 on every save. The website refuses to save over a newer version. |
| `updatedAt` | time | Set by the server on every save. |
| `allergens`, `conditions`, `highlights` | list of items | At most 40 each. An item is `{ id, label, keywords, retired }`. |

- The **id never changes** (letters and digits). Ingredients store ids, so renaming an item changes only its `label`.
- `keywords` are the words the app looks for in what a user saved (a user saves the label text they ticked). A rename keeps the old label as a keyword, so saved profiles keep matching. Highlights have no keywords.
- `retired: true` hides an item from the pickers (website and app) but it still matches what is already saved. An item can be deleted only when no ingredient uses it.
- `highlights` always starts with the built-in `keyActive` and `umbrella` (the ingredient fields of the same name). Other highlight ids are stored in the ingredient's `highlights` list.
- While the document does not exist the website and the app use their built-in lists (the ones above), so nothing breaks before the first save.
- Cost: 1 read per website page load that shows the lists, and about 1 read per app list check (the app keeps a copy).

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

### What kind of page a source is (shown, never stored)

Every `sources` entry stays `page title | https://address`. The kind of page is **worked out when it is shown**, the same way in the app, on the website and in the Assist with AI server, so older entries are labelled too and nothing new is written to the database. Do not put the label into the stored text.

| Kind | Label shown | Which pages |
|---|---|---|
| `regulator` | Regulator / government | EU, US FDA, FDA Philippines, NIH, WHO, ASEAN, TGA, Health Canada, gov.uk, ECHA; any `.gov` / `.gov.xx` site |
| `medical` | Medical / scientific body | CIR, AAD, DermNet, Contact Dermatitis Society, ScienceDirect, Wiley, Springer, Nature, JAMA, BMJ, Mayo Clinic, Cleveland Clinic; any `.edu` / `.edu.xx` site |
| `maker` | Manufacturer's own site | ONLY the brand's confirmed `website` and pages under it (confirmed `https://www.maybelline.com.ph` makes `shop.maybelline.com.ph` the maker's too). A site named like the brand is never enough on its own: without a confirmed website no page is the maker's. |
| `reference` | Ingredient database | Personal Care Products Council, INCI Beauty, INCIDecoder, Paula's Choice dictionary, EWG, CosmeticsInfo |
| `public` | Public list (anyone can edit) | Open Beauty Facts |
| `retailer` | Retailer listing | Large shops by their whole address (watsons.com.ph, shopee.ph, lazada.com.ph, sephora.com, mercurydrug.com ...): `SOURCE_RETAILERS` / `ListSources.RetailerSites` |
| `other` | Unverified site | Everything else, and every address that is not plain: a user name in front of the site (`https://fda.gov@evil.com`), a backslash, or letters outside a-z 0-9 . - in the site's name. Such addresses are also never shown as links. |

A search engine's pass-through address (Google's grounding link, `google.com/url`, `bing.com/ck`) is recognised by its own site, never by words elsewhere in the address; it is then labelled by its page title, and only when that title is a plain site name.

Each label has one line of advice (hover it on the website), and every list of links ends with a caution line for careful readers. The rule lives in `_Shared/glowupRules.js` (`sourceKind`), the app's `ListSources.SourceKind` and `api/assist-ingredient.js`; all three are tested against the `sourcekind` / `sourcelabel` lines of the app's `Editor/Tests/SharedRuleCases.txt`.

Assist with AI's backup search keeps only the kinds that fit the task: an ingredient's description rests on `regulator`, `medical` and `reference` pages; one product's ingredient list (and a brand's details) on `regulator`, `maker`, `retailer` and `public` pages. An ingredient database never counts as proof of what one product contains (the app's `ListSources` rule).

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
