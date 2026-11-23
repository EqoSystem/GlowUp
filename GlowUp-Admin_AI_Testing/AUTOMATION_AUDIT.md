# Product capture audit and changes (23 November 2026)

Answer to *Glow Up Existing System Gap Analysis & Implementation Specification* (the "automation-first" brief).
Audit first, then only confirmed gaps were changed. Commit `8958dd8` on branch `ar-latency`. Not deployed.

## 1. Audit matrix (brief, section 3)

Status before this change. "Test" = how it was checked here.

| Area | Status | Evidence (code) | Test |
|---|---|---|---|
| Barcode scanning | Partially working | `_Shared/quickFill.js` `scanBarcode`: browser BarcodeDetector, ZXing fallback, formats EAN-13/8, UPC-A/E, Code 128, ITF; "Use a photo" fallback; one result per scan (`finished` flag). Fault: `barcodeKey` drops leading zeros and the form saved that stripped value; a 12-digit UPC was looked up without its zero. | `quickFill.scenario` (mock camera-free path), new `captureReview.scenario`. Real camera: not verifiable here. |
| Quick Fill sources | Partially working | `lookupProduct`: Open Beauty Facts live (9 s timeout, no-result message), FDA Philippines = the list shipped with the site (`_Shared/data/fdaCatalogue.json`), not the live portal. Source named per product, not per field. "Fill form" overwrote values already typed. | `quickFill.scenario` |
| Camera / OCR | Partially working | Tesseract 5.1.1 reads the ingredient list only. Raw text thrown away; no reading confidence used. Name, size, maker, warnings not read. | unit + scenario |
| Use my phone | Working | `_Shared/phoneLink.js`, `_Admin_Phone_Scan/`, rule `phoneLinks/{code}`: same admin uid only, ends after 15 min (enforced in the rule), value only adds `count + 1`, window close deletes it; phone page says "link ended" / "another account". | `phoneLink.scenario` (9 checks). Not tried on a real phone in this pass. |
| Gallery upload | Not implemented | Only one camera photo (`capture` attribute). | — |
| Manual fallback | Working | Every field editable; save needs no barcode or look-up. | `inventoryModals.scenario` (453 checks) |
| Duplicate detection | Partially working | Save refuses the same barcode or the same brand + name. No variant / close-name hint; a brand is refused only on the exact name; import copied a barcode already on another product. | unit + scenario |
| Draft / recovery | Working (new products) | Browser draft of the Add Product form, kept 7 days, offered back on open. Not for edits or the brand form. | existing |
| Review queue | Partially working | `reviewed: false` = Waiting for review; Approve and Approve with brand exist. No Reject, no Request changes. A product an admin saved as Pending did not enter the queue (no `reviewed: false`). | scenario |
| Catalog publishing | Partially working | The app shows only Active products of Active brands (`ProductRepository.Usable`, `ProductRecommender`). The rules let anyone read every product, Pending included (`products: allow read: if true`). One automatic policy exists: scanned products confirmed by the FDA list are approved by themselves (`AUTO_APPROVE_FDA`, marked `autoApproved`). | code reading |
| Audit history | Working | Every website write goes through `_Shared/adminStore.js`, which adds an `auditLog` record in the same batch; the rule forbids changing or deleting records. | scenario counts records |
| File import | Partially working | PDF, Excel, Word, CSV, TXT; saved Pending; repeats in a file noted. Rows skipped were counted, not named; barcode clash not checked; zeros dropped. | `importCheck`, new `importReport.scenario` |

## 2. Gaps fixed

1. **Barcode zeros.** Stored as text with leading zeros (`barcodeDigits`), compared without them (`barcodeKey`). A 12-digit UPC is retried as 13-digit EAN on Open Beauty Facts. The app already compares with `ProductIndex.BarcodeKey`, so it needs no change.
2. **Field provenance.** Each value put in the form by a look-up, photo, phone or file is **Detected** with its source; **Needs review** when sources disagree, the match is weak or the photo was hard to read; **Checked** after "Mark checked"; **Typed** when the admin types it. Saved as `fieldOrigins`.
3. **Quick fill never overwrites.** A filled form gets a comparison window: field, form value, source value, a tick. Values already there are not ticked. Where Open Beauty Facts and the FDA list disagree, both are shown. Fields no source gives stay empty.
4. **Match strength** on each look-up result: Strong (≥ 0.95), Check (0.75–0.94), Weak. A routing signal only; critical fields still need a person.
5. **OCR evidence.** The text as read is kept (`ingredientsRaw`) and shown under the list. Low reading confidence (< 75) marks the list Needs review. Front/back photos show their text with "Use as name".
6. **Gallery photos.** Up to 8 at once, each labelled (ingredient list, barcode, front, back, shade). Read on the computer; not stored.
7. **Save gate.** Saving Active with unchecked values asks: "I checked them: save Active", "Save as Pending for review" or go back. Approve marks them checked.
8. **Review actions.** Send back (note required, stays Pending, row says "Sent back", next save = "Resubmitted") and Reject (reason required, goes to the Recycle bin with "Rejected: …", restorable). Pending saves now enter Waiting for review; a brand made inside a Pending product starts Pending.
9. **Duplicates.** Possible variants of the same brand listed under the name (brand, barcode, kind, shades, status), never merged. Close brand names (Maybeline / Colorette) ask before a new brand is made, and show in the product's brand hint. Names pasted twice in the ingredient list are named.
10. **Import report.** Every product entry not saved, or saved without its barcode, is named with its number and reason. A barcode already on another product is not copied. Imported values are Detected, from "File: name".

## 3. Files changed

- New: `_Shared/capture.js` (pure rules), `tests/capture.test.mjs`, `tests/responsive/captureReview.scenario.mjs`, `tests/responsive/importReport.scenario.mjs`.
- Changed: `_Admin_Brand_Inventory/adminBrandInventory.js`, `.html`, `productQuickFill.js`; `_Shared/quickFill.js`, `smartAdd.js`, `adminShell.js` (image icon), `adminCommon.css`; `tests/responsive/quickFill.scenario.mjs`; `FIRESTORE_SCHEMA.md`; `CLAUDE.md`.

## 4. Database, rules, API

New optional product fields: `fieldOrigins`, `ingredientsRaw`, `ingredientsRawFrom`, `reviewState`, `reviewNote`, `reviewNoteBy`, `reviewNoteAt` (see `FIRESTORE_SCHEMA.md`). Old products work unchanged; nothing to migrate. Rollback = revert the commit; the extra fields are ignored by the app.

**No rule change and no new API.** The admin write rule already allows these fields.

## 5. Configuration and credentials

None new. Open Beauty Facts and PubChem need no key; the CSP already allows them and `blob:` image previews.

## 6. Tests run

- `node tests/run-all.mjs`: 34 files pass (new `capture.test.mjs`: 49 checks).
- Browser rig (Playwright + mock Firebase, desktop 1280 and phone 390): `quickFill` 58, `captureReview` 38, `importReport` 18, `inventoryModals` 453, `phoneLink` 9, `importCheck` 24, `deleteImport` 8, `sourceLinks` 119, `csp` 19: all pass.
- Open Beauty Facts was answered by the test, not the live site. No live Firebase write.

## 7. Known limits and what is not built

- **Pending products are readable by anyone** through the database (the app hides them). Closing this needs a rule change plus an app query change and a new APK; waiting for the owner's decision.
- **No server pipeline, background jobs or stored photos.** The free plan has no Cloud Functions and no Storage. The steps run in the admin's browser; each source sits behind its own adapter in `quickFill.js`, and a failed source does not stop the others.
- **FDA Philippines** comes from the list shipped with the site, not the live portal (the portal is slow and refuses automated clients).
- **OCR** reads ingredient lists well only on sharp, straight photos. Size, maker and warnings are not mapped (the schema has no fields for them).
- **Not measured:** precision/recall, false-duplicate rate, time saved. These need a labelled set of real GlowUp packs. The 0.95 / 0.75 limits are starting values, not proven accuracy.
- Not done (lower priority in the brief): brand logo/photo preview, product photo preview, a stepper layout, drafts for edits and for the brand form, caching of look-up answers.
- Real camera, real phone and live Open Beauty Facts were not tried in this pass.
