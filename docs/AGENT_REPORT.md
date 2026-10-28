# Agent report: data-integrity pass of 28 October 2026

Branch `chore/glowup-data-integrity`, made from `feature/unity-ar-face-mesh-tryon` (b921e68). Nothing was pushed or merged; no history was rewritten;
nothing was run against the live Firebase project; the security rules were **not** published. Read `docs/ARCHITECTURE_AS_FOUND.md` first: it is the
baseline for every "already existed" verdict below.

## What I changed

| Commit | Change | How it was checked |
|---|---|---|
| `3b7a228` | Checkpoint: the uncommitted work found in the working tree (re-saved scenes, scan / product-index edits, a new test file, `docs/project-documentation/`), committed as found before any edit, as `CLAUDE.md` asks | none needed |
| `56d8d8e` | **Evidence tiers.** `glowupRules.js`: `evidenceTier`, `bestEvidenceTier`, `evidenceProblem` (T1 regulation, T2 safety and science, T3 databases, T4 packaging side). Ingredient Curator: AI entries are auto-approved only when a source of the right tier backs the rating (an ingredient database alone no longer counts); a new Harmful rating needs a regulator page to be saved or approved; bulk approve leaves unbacked Harmful entries out; a filter lists Harmful entries without a regulator page | `node tests/rules.test.mjs` (650 checks, incl. 30 new); syntax check of the page |
| `e277f0b` | **Local import without AI quota.** `_Shared/ingredientBlock.js`: finds the ingredient block (English, French, Spanish, Filipino, Malay), splits outside brackets, splits `(and)` blends, keeps `May contain` / `+/-` apart, writes `CI nnnnn`, Jaro-Winkler suggestions (suggestions only); reads documents labelled `Brand:` / `Product:` / `Ingredients:`. Brand & Product Inventory tries it before the AI | `tests/ingredientBlock.test.mjs` (64 checks) |
| `9ac3941` | **Rules.** `isAdmin()` also accepts the custom claim `admin: true`; `pending_products` readable only by the sender and admins; size limits on `scanActivity`; `storage.rules` closes Storage (not listed in `firebase.json`); **138 rules checks in the Firestore emulator** (`tests/rules`) | Ran in the emulator: all pass. Run against the previous rules, 11 checks fail, exactly the changed behaviour, so the checks are not vacuous |
| `03ae5ef` | `reviewedBy` (the approving admin's UID) on every manual approval in both pages | website tests; syntax check |
| `1d159bc` | **App fix:** an allergen typed as "Alcohol" flagged Cetearyl, Cetyl, Stearyl ... Alcohol (fatty alcohols that only share the word); now only drying alcohols and Benzyl Alcohol match it. Two NUnit tests added. **`tools/ScannerEval`**: runs the app's real `IngredientTextParser` on labelled examples and prints precision, recall and flag recall; 12 made-up examples in `tests/fixtures/ingredient-labels` | Both app assemblies compile with Unity's own compiler (csc from the 2022.3.62f3 install); the nine allergen cases **ran** against the compiled app assembly in a .NET 8 harness outside Unity (all as expected). The NUnit tests were **not run in the Editor**. ScannerEval runs: 1.00 on the made-up set (which says nothing about real packs) |
| `15bd96f` | Import dialogs accept **.csv and .txt**. PubChem and EU CosIng pages are tier 3 even though their hosts are regulators. `tools/cosingToImport.mjs`, `tools/t1ToImport.mjs` (+ an **empty** template), `tools/seedDev.mjs` (emulator only, dry run by default), `tests/run-all.mjs`, root `package.json` (`npm test`, `npm run test:rules`, `emulators`, `seed:dev`, `eval:scanner`) | `tests/tools.test.mjs` (29 checks, invented names only), `importFormat` CSV checks; seed script run against the emulator (dry run, refusals, write, second write leaves documents alone) |
| `a05e5d3` | `docs/ARCHITECTURE_AS_FOUND.md`, `docs/DATA_SOURCE_POLICY.md`; in-app privacy policy section 8 now describes Delete My Account (which already existed) and what it leaves behind | read against the code |
| `062a6d1` | `docs/DATA_MODEL.md`, `ADMIN_GUIDE.md`, `PRIVACY_NOTES.md`, `THESIS_ALIGNMENT.md`, `THESIS_FIXES.md`, `ARCHITECTURE.md`, root `README.md`; website wording "Technical / Scientific Name" -> "INCI Name (as printed on labels)" | Unity menu names in the README checked in the code |
| `c2608f0` | **Scanned PDFs** are read with tesseract.js in the browser (at most 10 pages, one at a time, progress in the status line, nothing uploaded) | In an isolated headless Chrome: an image-only PDF was read in 1.5 s and parsed into a product without AI; a text PDF still used its text layer; CSV read; a blank scan gave the new error message |

All website checks: `npm test` -> 13 files, ALL PASSED. Rules: `npm run test:rules` -> 138 checks, ALL PASSED.

## Already existed (not changed)

| Brief item | Where it is | How I verified |
|---|---|---|
| Ingredient fields (INCI name, aliases, description, caution note, common uses, rating, sources, review state) | `ingredients`: `techName`, `aliases`, `description`, `cautionNote`, `commonUses`, `riskLevel`, `sources`, `reviewed`, `source` | `FIRESTORE_SCHEMA.md` and the Curator form code; full mapping in `DATA_MODEL.md` |
| Ingredient groups with keywords | `config/lists` allergen families + `allergenTags` | `ListsRepository.cs`, `adminLists.js` |
| Drying vs fatty alcohols kept apart | built-in table, `IngredientKnowledge.cs` | read the entries (the typed-allergen fault was fixed, see above) |
| Unknown ingredient never counts as safe | `ProductAnalysisEngine.cs` ("Not checked", AI-only entries never clear a name) | read the code and the existing tests |
| Unresolved-ingredient queue, most used first | Curator, "ingredients that products in the inventory list have no entry here yet" (`fromProducts`) | read the code |
| Approval workflow; imports land as Pending | `reviewed`, `Approve`, `source: "document-import"` | read the code |
| User-contributed unknown products for admin review | `pending_products` + Pending Snapshot Reviews | rules and `adminSnapshots.js` |
| Shade codes resolved **within a product**, unique per product | `shadeCodes` beside `shades`; `ShadeCode.cs`; the form refuses a repeated code | read the form code (`addShadeFromDraft`, the code edit handler) |
| Scan-to-try-on with the product's own shades, and a manual product search in AR | `ArTryOnFlowController.cs` | read the code |
| Local text extraction of PDF / Excel / Word in the browser | `adminDocumentReader.js` (pdf.js, SheetJS, mammoth) | read the code |
| Batched writes (at most 400 per batch) | `BATCH_LIMIT = 400`, `runBatches` | read the code |
| On-device OCR in the app | ML Kit through `GlowUpTextReader.java` | read the code; nothing cloud-based or quota-limited in that path |
| Glare / dark / blurry / moving frames dropped; several frames merged | `FrameQuality.cs`, `ScanTextMerger.cs`, `ScanGuidance.cs` | read the code |
| Offline persistence | app: `PersistenceEnabled = true` (100 MB) + `SyncQueue`; website: `adminStore.js` | read `FirebaseBootstrap.cs` |
| Camera features do not overlap | finger signs only on Dashboard, Recent, Settings, Result, and release the selfie camera for the scanner and AR (`FingerSignNavigator.activeScenes`) | read the code |
| Consent screen, Delete My Account, minimum age 13, "not a medical device" | Terms scene + policy; `AccountActions.cs`; Steps 1 and 5; Terms section 3 and result notes | read the code |
| No "Digital Medical ID" wording | none in the app or the website | searched |
| Live dashboard numbers | `adminDashboard.js` reads Firestore; no sample or hard-coded values | searched for mock / sample / random |
| No hard-coded admin credentials | the only "password" constant is Firebase's provider id `"password"` | searched (values never printed) |
| No query needs a composite index | every query filters on one field | searched app and website; so no `firestore.indexes.json` was added |
| Duplicate brands / ingredients with other capitals | `sameKey`, the Curator's duplicate check on save, "Find duplicates" and "Clean up" | read the code |

## Existed but improved

- **Evidence rules**: a written rule existed (`IMPORT_FORMAT.md`: "harmful = prohibited or restricted by a regulator"), and auto-approval looked for a credible source of any kind; now the tier is enforced in code (save, approve, auto-approve) and tested.
- **File import**: structured files were read without AI; free-form product sheets, CSV, text and scanned PDFs went to the AI or were refused. Now read locally.
- **Rules**: admin check, pending entries, scan notes (see above). The website's login gate still uses `admins/{uid}`.
- **Allergen matching**: the typed "Alcohol" fault.
- **Privacy policy**: deletion section corrected.
- **Approvals**: who approved is recorded.

## Decisions I made on your behalf

1. **No renaming, no parallel collections.** The brief's schema (`inciName`, `severity`, `ingredientGroups`, `products/{id}/shades`, `importJobs`, `auditLogs`, `verificationStatus`) was mapped onto what exists (`DATA_MODEL.md`). No migration was needed, so no migration script was written.
2. **No `aliasesNormalized` / single-query lookup.** The app matches the whole (small) ingredient list on the phone, offline; a query per name would cost reads and fail offline.
3. **Restricted -> Caution, prohibited -> Harmful** in the regulation importer (the old import guide said "harmful = prohibited or restricted").
4. **Two-admin approval not built** (would stall a small team); `reviewedBy` + tier rules instead.
5. **No Firebase Storage**: it needs the paid plan; packaging photos stay in the existing `pending_product_images` documents; `storage.rules` closes Storage.
6. **Custom claim added beside, not instead of, `admins/{uid}`**, so the current admin keeps working before anyone sets a claim.
7. **`hexSource`, `undertone`, `finish` not added**: nothing would read them. The swatch-to-hex helper was not built (it needs a page change I could only check by hand in a browser).
8. **Synthetic fixtures only** for the scanner evaluation, clearly marked; no real product data was written from memory.
9. **No CI file**: the repository has no remote, and the brief asked for CI only if one exists.
10. **Minimum age stays 13**, matching the Terms; the full birthday stays (needed for current-age checks). Both are listed as decisions for the team in `PRIVACY_NOTES.md`.
11. The thesis alignment uses the owner's four-page summary of 13 October (the PDF is not in the repository).

## Thesis alignment gaps (summary of `THESIS_ALIGNMENT.md`)

- Objective 1 (regulatory cross-referencing): **partly**; the regulatory part is the curated ingredient list with cited regulators, not a live regulator database.
- Objective 3 (AI recommendation, "performance benefits"): **partly**; recommendations are rule-based; benefits are highlights only.
- Objectives 2 and 4, every Scope module: **yes**. Many built features are not in the Scope (Recycle Bin, imports, Assist with AI, snapshots, Settings, finger signs, offline).
- Limitations: all true; "informational only" is not shown on the AR screen itself.
- RRL / RRS citations: **not checked** (no PDF). Likely claims "FDA-notified database only" (not supported) and "colour database" (partly) need rewording; see `THESIS_FIXES.md`.

## Blocked / not done

- **Thesis PDF not in the repository**: no citation check; the owner's earlier corrections (authors, Adawiyah year, "Carreon et a", reference order, Santiago-Rivera) are listed as unconfirmed in `THESIS_FIXES.md` section B.
- **CosIng download**: not attempted; only the importer exists. A person downloads the file from the European Commission.
- **NUnit tests in the Unity Editor**: not run (the two new tests compile; the logic ran outside Unity).
- **Page-level browser test** of the Curator and Inventory pages after these changes: not run. The shared modules (document reader, parser) were run in a real browser; the page edits were syntax-checked only.
- **Graphify install step**: Graphify was already installed and the graph exists; it was updated at the end (`graphify update .`). `pip` was not needed.
- **App UI wording** (consent for health data, AR "approximate colours" note, rating words): needs builder changes and a scene rebuild in Unity, which I cannot run or check visually.
- **Performance on a phone**: not measured (no device). The code shows no overlap of camera features; no new on-device model was added.

## Needs a human

1. **Publish the rules**: Firebase console > Firestore > Rules > paste `GlowUp-Admin_AI_Testing/firestore.rules` > Publish. Run `npm run test:rules` first.
2. **Fill the contact details** in `Legal/PrivacyPolicyAndTerms.txt` (two `[add your support email here]`), and name a data protection officer.
3. **Rotate or move the app's Gemini key**: it is compiled into the APK (`SecureProfileStore.cs`, ignored by Git, never in history). Restrict it in Google Cloud, or move the phone's AI calls behind a Vercel function.
4. **Restrict the Firebase web / Android API keys** (they are public by design, in `firebase-console.js`, `api/assist-ingredient.js`, `google-services.json`) and **turn on App Check** (no App Check code exists yet).
5. Optionally **set the `admin` custom claim** with the Admin SDK for scripts.
6. **Deploy the website** (`npx vercel --prod` from `GlowUp-Admin_AI_Testing/`) to get the import, OCR and evidence changes live, then try one import of each kind.
7. **Rebuild the app** in Unity and run the EditMode tests (Test Runner); the alcohol fix and the privacy text are in the app.
8. Decide: separate consent for health data, parent confirmation for 13 to 17, birthday vs year and month, deleting a user's suggestions and snapshots with their account (`PRIVACY_NOTES.md` section 4).
9. Add **real** fixtures to `tests/fixtures/ingredient-labels` (OCR text from the phone, list typed from the pack) and run `npm run eval:scanner`.
10. Fill `tools/templates/t1_restricted_template.csv` from the regulation's annexes; review `cosing-restricted-review.csv` if CosIng is imported.
11. Update the thesis text (`THESIS_FIXES.md`).

## Open risks

- The new rules are tested in the emulator only. Before publishing, check that the current app build still reads its own staged products (it reads by its own id, which the new rule allows; a missing entry answers "not found").
- The Harmful rule blocks **new** Harmful ratings without a regulator page; old Harmful entries without one stay in use until an admin fixes them (they are listed).
- tesseract.js and its English data load from jsDelivr / projectnaptha on first use; OCR accuracy on real scans is unmeasured, and admins must compare the result with the pack.
- `parseLabeledProducts` takes the make-up kind from the product name when no `Kind:` line exists; an unclear name leaves it empty and the record is saved Pending and flagged.
- The `node_modules` of `tests/rules` (firebase-tools 13, about 300 MB) is ignored by Git and by Vercel.

## How to run and test

From the repository root:

```
npm test                                                   # 13 website check files
npm --prefix GlowUp-Admin_AI_Testing/tests/rules install   # once
npm run test:rules                                         # 138 rules checks in the emulator (Java 11+ on PATH)
npm run emulators                                          # emulator for trying things (port 8085)
npm run seed:dev                                           # dry run; add --write with FIRESTORE_EMULATOR_HOST=127.0.0.1:8085
npm run eval:scanner                                       # label-reader precision / recall (.NET 8 SDK, Unity compiled once)
```

Java on this computer: Unity's JDK 11 at `C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK/bin`
(the globally installed firebase-tools 15 needs Java 21; the rules folder pins firebase-tools 13, which works with Java 11).
App tests: Unity > Window > General > Test Runner > EditMode > Run All.
