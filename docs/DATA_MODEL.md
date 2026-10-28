# Data model: the brief's schema mapped onto what GlowUp already stores

The field-by-field description of the live database is `GlowUp-Admin_AI_Testing/FIRESTORE_SCHEMA.md` and is the reference. This file answers a different
question: *the data-integrity brief of 28 October 2026 proposed a schema; which of it already exists, under which name, and what was added or left out, and why.*
The rule followed was: existing collections, field names and screens win; nothing was renamed; no parallel collection was created.

## `ingredients/{id}`

| The brief proposed | In GlowUp | Verdict |
|---|---|---|
| `inciName` | `techName` (the name printed on labels; scans are matched against it). `name` is the friendly name | **Exists** under another name. Not renamed |
| `inciNameNormalized`, `aliasesNormalized` (for a single `array-contains` query) | Not stored. The app and the website load the whole ingredient list and match on the device with the shared `normalizeName` / `TextKey.Normalize` (about 140 entries; it must also work offline) | **Not added, on purpose.** A query per scanned name would cost one read per name and would not work offline. Revisit if the list grows past a few thousand entries |
| `aliases` | `aliases` | Exists |
| `friendlyDescription` | `description` (plus `chemicalDescription` for the technical text) | Exists |
| `cautionNote` | `cautionNote` | Exists |
| `commonlyUsedIn` | `commonUses` | Exists |
| `groupIds` -> `ingredientGroups` | `allergenTags` (ids of the allergen families) -> `config/lists` (`allergens`: fragrance, paraben, sulfate, formaldehyde, lanolin, essentialoil, nut, plus any an admin adds). Each family has `keywords` that a user's saved allergen is matched against | **Exists**, as `config/lists`. No new `ingredientGroups` collection |
| "Alcohol must be split into drying alcohols and fatty alcohols" | The built-in ingredient table keeps them apart (Alcohol Denat. and Isopropyl Alcohol are drying; Cetearyl, Cetyl, Stearyl ... Alcohol are fatty). **One fault was found and fixed:** an allergen the user typed as "Alcohol" matched every name containing the word, including the fatty alcohols (`ProductAnalysisEngine.MatchAllergen`) | Exists; fault fixed |
| `severity` (`info` / `caution` / `avoid`) | `riskLevel` (`safe` / `caution` / `harmful`, or empty to let the app decide) | **Exists** under other words. `harmful` = `avoid` |
| `evidence: [{ tier, sourceName, ref, url, accessedAt }]` | `sources`: a list of lines `page title | https://address`. The kind and the tier are **worked out when shown** from the address (`sourceKind`, `evidenceTier`), so older entries are covered and nothing can disagree with the address | **Exists** in its own form. The tier rule is new and enforced (see `DATA_SOURCE_POLICY.md`). `accessedAt` is not stored |
| `status` (`draft` / `needs_review` / `verified` / `deprecated`) | `reviewed` (yes/no) + `source` (where it came from) + `autoApproved` + `binned` (Recycle bin). An entry the app's AI found is `source: "ai"`, `reviewed: false` = needs review; `binned: true` = removed | **Exists** as flags |
| `createdBy`, `reviewedBy[]` | `addedBy` (the app user whose scan found it); **`reviewedBy`** (new: the approving admin's UID) | `reviewedBy` **added** as a single UID, not a list |
| `updatedAt`, `version` | `updatedAt`; `version` only on `config/lists` | Exists / not needed |

## `brands/{id}` and `products/{id}`

| The brief proposed | In GlowUp | Verdict |
|---|---|---|
| brand `market` (`local` / `international` / `mixed`) | `market` (Global, Local, Asia, Europe, North America, ...) | Exists, richer |
| brand `officialWebsite` | `website`, **confirmed by an admin** (`websiteConfirmedBy`, `websiteConfirmedAt`); `websiteSuggested` for a proposal that is never trusted | Exists, and stricter than the brief |
| brand `fdaCompanyNote`, `notes` | Not stored | Not added: no screen or rule would use them |
| product `category` | `cosmeticType` (the 17 make-up types) | Exists |
| product `barcode` | `barcode` (digits; the app finds a product by it) | Exists |
| product `ingredientsRaw`, `ingredientIds`, `unresolvedIngredients` | `ingredients` (the cleaned list as printed), `ingredientIds` (the ids of the library entries the names match), `ingredientCount`. "Unresolved" is **computed**: the Curator lists the names that products use and the library does not explain yet, most used first | Exists. The raw text is not kept (only the cleaned list) |
| product `ingredientSource` (`packaging_photo` / `brand_site` / `official_store` / `ecommerce_listing` / `user_scan`) | `sources` (pages), `source` (`scan`, `snapshot`, `document-import`, `openbeautyfacts`, ...). A photo of the pack is kept for a snapshot in `pending_product_images` until an admin approves or rejects it. The app shows where a list came from and how many sources agree (`ListSources`) | **Exists** in another form. Firebase Storage is not used (it needs the paid plan) |
| product `verificationStatus` | `reviewed` + the kind of the pages in `sources`. A list that comes only from a shop or a public list is labelled so (`retailer`, `public`) and is never counted as the manufacturer's | Exists as a derived label |
| product `fda { notificationNo, checkedAt, result }` | A source line `FDA Philippines verification | https://...fda.gov.ph/...`; the app checks the FDA Philippines portal by itself and shows the answer as a badge (**legality only**; never used as an ingredient source). The notification number is read by the file import (`FDA_REGISTRATION_NUMBER`) | Exists, with no separate `fda` map |
| `products/{id}/shades/{shadeId}` (`shadeCode`, `hex`, `hexSource`, `undertone`, `finish`) | Two lists on the product, at the same places: `shades` (`#RRGGBB`) and `shadeCodes` (as printed). A shade code is only meaningful **inside one product**: the form refuses a second shade with the same code in a product, and the app looks a code up among that product's own shades (`ShadeCode.cs`) | **Exists**. `hexSource`, `undertone` and `finish` were **not added**: nothing in the app would read them (the finish comes from the make-up type) |
| `importJobs`, `auditLogs` | Not created. An import writes only `Pending`, `reviewed: false` entries (that is the preview and the audit: nothing is live until an admin approves it). Who approved an entry is `reviewedBy` | **Not created.** A collection nothing reads would be dead weight |
| `pendingProducts` | `pending_products` (+ `pending_product_images`): products the catalogue lacks, read by the AI from one photo, waiting for an admin; the sender's own entry is the only one the app uses | **Exists** |
| `users/{uid}`, `users/{uid}/scans/{id}` | Same names | Exists. Readable and writable by that user only; an admin cannot read them |

## What the rules allow

`firestore.rules` is described in `FIRESTORE_SCHEMA.md` ("What the rules allow"). Changes made on 28 October 2026 (not yet published, see the report):

- `isAdmin()` also accepts the custom claim `admin: true` (set with the Firebase Admin SDK). The `admins/{uid}` document still works, and the website's login gate still uses it.
- `pending_products` can be read only by the account that sent the entry and by admins (before, every signed-in account could read every entry). Asking for an entry that does not exist still answers "not found".
- `scanActivity` notes have size limits (200 characters for names, at most 50 flagged names).
- `storage.rules` closes Firebase Storage completely (Storage is not used).

`GlowUp-Admin_AI_Testing/tests/rules` runs 138 checks of these rules in the Firestore emulator (`npm run test:rules`).
