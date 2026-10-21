# Admin website UI changes (checklist)

Scope: `GlowUp-Admin_AI_Testing/`. No Firestore reads added. No commits. One step at a time, each approved first.

## Steps
- [x] 1. Plan, wireframes, this file
- [x] 2. Shared shell (sidebar fold, logo + favicon, subtitle removal, stat strip, Recycle Bin icon)
- [x] 3. Dashboard layout
- [x] 4. Inventory layout
- [x] 5. Curator: toolbar fold, list default, right column + Quick actions
- [x] 6. Curator ingredient import box + security review
- [x] 7. Verify (node --check, tests, grep for added reads)

## Items
- [x] 1. Sidebar foldable (icons only, tooltips, localStorage, keyboard)
- [x] 2. Sidebar logo = Unity app icon (128 + 32 px in `_Shared/assets/`), also favicon on every page
- [x] 3. Subtitle under page title removed (all pages)
- [x] 4. Stat cards without box, smaller icon, number + label (Dashboard 5, Inventory 4, Curator 5)
- [x] 5. Icon-only Recycle Bin button in the page header
- [x] D1. Dashboard: Quick actions top of right column; stats left; Today's results and Most scanned brands resized
- [x] I1. Inventory: toolbar above stat strip
- [x] I2. Inventory: "Import from a file" in right column, larger
- [x] C1. Curator: toolbar foldable, state remembered
- [x] C2. Curator: list/row style by default (card toggle kept if present)
- [x] C3. Curator: right column with Quick actions (same component as Dashboard)
- [x] C4. Curator: ingredient-only import box (.docx .xlsx .pdf)
- [x] L. Saved layouts: new default positions must show (version or reset)

## Notes
- Saved layouts: `adminLayouts.js` POSITIONS_VERSION = 2. A saved page older than that loses saved order and sizes only; raise it when default positions change again.
- Bin badge counts only what a page holds: Inventory = products and brands, Curator = ingredients, Dashboard = no badge.
- Ingredient import: new mode `ingredientImport` in `api/assist-ingredient.js`; entries saved as source "ai", reviewed false (the app's existing Pending state), plus `via: "document-import"`.
- Not checked in a browser yet. Pre-existing: 2 failing checks in tests/assist.test.mjs (search redirect, failure order), same before these changes.

## Round 2
- [x] R1. Stat cards: one shared style, one row from 1024 px, two per row below, card box back, bare icon
- [x] R2. Curator: Search and filters above the stat row; "..." and "+ Ingredient" in its header row
- [x] R3. Curator right column: Import ingredients on top, Quick actions below (both foldable)
- [x] R4. Inventory toolbar in two groups; notes collapsed into one info badge; stat cards under the toolbar
- [x] R5. Refresh data: icon only, 2-minute pause, 6 per hour (adminStore.js constants), tooltip shows the wait
- [x] R6. Ingredient modal: the two helper sentences are info badges (hover, focus, click; Esc closes)
- POSITIONS_VERSION = 3.

## Round 3 (from "New.pdf") and the lists
- [x] P1. Inventory toolbar: row 1 search, brand filter, notes badge, `...`, Add; row 2 sort and Brands/All products. Folder chips (All, New folder) and the Recycle bin moved into the Inventory title row.
- [x] P2. Notes badge opens a floating, draggable window (one row per note, serious ones first and highlighted, action buttons kept; Esc or the X closes it).
- [x] P3. Curator: Quick actions and Import pinned to the right column (`data-right`), the ingredient list sits under the stat row on the left.
- [x] P4. Shades: nothing is added by itself. "+ Shade" opens a row with a colour box and a hex field; each shade has a colour box, an editable code, move earlier/later and remove; near-duplicate warning; import and typing accept #abc and rgb(r, g, b).
- [x] P5. Ingredient form: Allergen Family, Skin Conditions and Highlights have an "Edit list" button (add, rename, reorder, retire/restore, delete, look-alike checks). One document `config/lists`; custom highlights are stored on the ingredient (`highlights`).
- [x] P6. Mobile app: `ListsRepository.cs` reads `config/lists` (1 read per list check, copy on the phone, built-in lists as fallback); onboarding steps 2 and 4 show the website's lists; analysis matches allergens and conditions through the list's words; a custom highlight makes an ingredient show among the result chips.
- firestore.rules: new `config/{docId}` block (read: everyone, write: admins, only `lists`). NOT published: paste the file in the Firebase console.
- POSITIONS_VERSION = 4. Compile check of Assembly-CSharp with Unity's csc: no errors.
- Not done (still hard-coded in the app): the per-condition alert switches in Settings (`SafetyAlerts.ConditionKeys`), the Skin types list, the AI assist prompt lists.

## Round 4 (from "New3.pdf")
Website (`GlowUp-Admin_AI_Testing/`):
- [x] N1. Inventory list header: row 1 title and count; row 2 folders (All, New folder), the Recycle bin right after them, the pager at the far right (`.subhead`).
- [x] N2. Ingredient picker in the product form (`_Shared/adminIngredientPicker.js`): search the library as you type, click = removable pill (solid = in the library, dashed = not yet), "+ Create new ingredient" opens a small form and saves it to the library at once. The text box stays (paste a whole list, Assist with AI). Each pill has a note button: product-specific caution and share (%).
- [x] N3. Database (all optional fields, nothing mass-written): brands `logoUrl`, `imageUrl`; products `imageUrl`, FDA link box (stored in `sources`), `ingredientIds`, `ingredientCount`, `ingredientNotes`, `tags`; ingredients `chemicalDescription`, reference links (`sources`). Forms have the boxes. FIRESTORE_SCHEMA.md explains the split and why no verdict is copied onto a product.
- [x] N4. Filters button (Inventory): kind, status and facts at once, "Apply filters" (`_Shared/adminFacets.js`, no reads). Export to CSV in the `...` menu (`_Shared/adminExport.js`, newest 100 rows, `EXPORT_MAX_ROWS`, no reads, formula-safe cells).
- [x] N5. firestore.rules: `adminCatalogueWrite()` (name is text, list sizes, https-only picture links) on every admin write to brands, products, ingredients. NOT published: paste the file in the Firebase console.
- Not built: image upload to Firebase Storage (needs the Blaze plan on a new project; no Storage rules exist), visual-similarity search, a stored `is_safe` summary on products (it would go stale; the app works the verdict out live).
Mobile app (`GlowUp/Assets/_Project/`):
- [x] M1. Start Scan button (`HoldToScanButton`, `SmartScanFlowController`): locked from the tap until the result is ready (no taps, dimmed, "Starting..." / "Analyzing..." on it, ring keeps moving), debounce, tips when stuck (light, clean lens, steady hand, type it in) with a pulsing Type-it-in button. Every number and word is an Inspector field. Scan scene builder adds the Canvas Group (`dimGroup`).
- [x] M2. AR Try-On quality: `ArQualitySettings` (Automatic / High / Lighter, limits in the PerformanceProfile asset), Settings row `ArQualitySetting`, `AR Quality Controller` object (`ArAdaptiveQuality`: both pipelines' numbers, frame-rate check that moves a slow phone to Lighter).
- [x] M3. AR objects, each its own root object with its numbers in the Inspector: `AR Light Adapter` (skin colour, highlight follows the room, low-light hint), `AR Face Hold` (last known good, prediction, fade-out of a lost face), `AR Mouth Mask` (keeps colour off teeth). Shader `FaceMakeupBlend` got `_MouthMask` / `_MouthOpen`.
- Already there, not rebuilt: dense 468-point mesh (ARCore), multiply blend with soft edges, ARCore guided fallback.
- Not possible here: MediaPipe face landmarker for weak phones (needs a model file and plug-in), camera ISO / exposure control (AR Foundation 5.2 has none), hair and hand occlusion (ARCore gives no depth or segmentation).
- To see it: in the Unity Editor run the builders (GlowUp > Build Scene > AR Try-On, Settings, AI Smart Analysis), then build the APK. Compile check of both assemblies with Unity's csc: no errors.

## Round 5 (AI_Camera_Snapshot_and_Staging_Architecture.pdf, and two follow-up notes)
- [x] S1. App: "Scan Snapshot" object (`ScanSnapshotController`) and its pop-up in the scan scene. Catalogue miss (and no staged entry), or a stuck scan: "Product not in catalog yet! Tap to take a quick photo". Button locks at once, "Analyzing Packaging..." with a spinner, 5 s cooldown, one AI request (scan budget). Photo max 1080 px, JPG squeezed under 80 KB (Unity cannot write WebP).
- [x] S2. App: `ProductAiAdvisor.ReadPack` (brand, name, kind, shade, ingredient list read from the photo only), `PendingProductRepository` (phone copy first, then 1 read by id, misses remembered 30 min), staging via SyncQueue. The result is analysed at once with listFrom "pending"; result screen shows the PENDING APPROVAL badge (`PendingApprovalBadge`, built by the result scene builder). A staged product is not also added to `products`.
- [x] S3. Other users: a catalogue miss looks for a staged entry before the web search, and uses it (badge shown).
- [x] S4. Website: Pending Snapshot Reviews (first a page; since round 6 a window inside the Inventory, see R6), Dashboard tile (1 count read). Photo next to the reading, every box correctable, Approve (brand, product Active, missing ingredients as waiting for review, entry marked approved, photo deleted) or Reject.
- [x] S5. firestore.rules: `pending_products` (signed-in read, own create only, pending, make-up only), `pending_product_images` (admin read). NOT published.
- Not built: Firebase Storage and WebP (Storage needs the Blaze plan; Unity has no WebP encoder), SQLite (the app's JSON cache does the same job).
- [x] K1. Skeletons only in data containers: the static shell (menu, titles, panel headings, fixed labels) now draws at once on every visit (pageFrame.js no longer hides the page for browsers it does not know). Curator stat tiles are fixed HTML; only their numbers are skeletons. No read was added or changed.
- [x] K2. Inventory list header mirrors the Ingredient Curator: line 1 title, count, Rows / Cards, "1-10 of N", pager; line 2 folder pills (All, New folder) and the Recycle bin; the list's helper is a full-width strip again. Cards is CSS on the same rows, so every handler and the pager are unchanged.

## Round 6 (screenshot: Pending Snapshot Reviews into the Inventory)
- [x] R6. The sidebar entry and the separate page are gone. Brand & Product Inventory has a "Pending snapshots" panel in the right column (under Import) with a "Pending Snapshot Reviews" button and a count badge (1 count read when the page opens).
- [x] R6a. The button opens a floating window over the page (dimmed, blurred background): one snapshot at a time, "Reviewing snapshot 2 of 5" and a progress bar; photo on the left, what the AI read on the right; each field has a green "Looks right" or an amber flag with the reason; ingredients as pills (green safe, amber caution, red harmful, dashed new to the library).
- [x] R6b. Approve & Publish, Edit & Approve (unlocks the boxes, then Save & Approve), Reject (asks first); previous / next to skip; the card slides out, the badge goes down, the next card comes in; "All caught up" at the end. Esc closes, arrow keys move, Tab stays in the window.
- [x] R6c. Reads: nothing until the window opens; then one query (max 50, `MAX_LOADED` in `_Shared/adminSnapshots.js`) and the photo of the card on screen only. Brands, products and ingredients come from the page's own lists. The Dashboard tile opens the Inventory with the window open (`?snapshots=open`). POSITIONS_VERSION = 5.

## Round 7 (2026-10-22: AI Scan Result readability review, source labels for app, website and server)
- [x] R7a. App result screen (`ScanResultSceneBuilder.cs`, run by "12 AI Scan Result" and by "All Scenes" / "All App Scenes"): every button and tappable row at least 132 units high (about 48 dp), chips and source buttons 120; body text 38, second lines 32, nothing below 28 (sizes are constants at the top of the builder); the product name wraps instead of shrinking.
- [x] R7b. One way to the FDA Philippines portal ("Verify on FDA Philippines"); the second button inside the badge's details is gone. The badge keeps its second line when the FDA answer arrives late, so the screen does not jump.
- [x] R7c. "Where this list came from" is one closed block in Key ingredients: the source line, the second-source line, the pages as buttons (site + kind of page) and one line of advice per kind shown, then the caution line for careful readers.
- [x] R7d. Ingredient chips show an icon in the coloured disc (check, warning, cross, i): never colour alone.
- [x] R7e. One pink next step: "Find a dermatologist" is an outline button for a result with nothing flagged, pink only for "use with care" / "flagged". "See safer alternatives" appears under the verdict for those results.
- [x] R7f. Buttons that leave the app (Verify on FDA, source pages, Find a dermatologist) carry the new "opens a website" icon (`IconFactory.ExternalLink`).
- [x] R7g. Source kinds shared by the app (`ListSources.SourceKind`), the website (`sourceKind` in `_Shared/glowupRules.js`) and the Assist with AI server, tested against the same `sourcekind` / `sourcelabel` lines of `SharedRuleCases.txt`. Labels: Regulator / government, Medical / scientific body, Manufacturer's own site, Ingredient database, Public list (anyone can edit), Retailer listing, Unverified site. Nothing new is stored; see FIRESTORE_SCHEMA.md.
- [x] R7h. Website: every saved source link (Inventory rows and review notes, Ingredient Curator proof lines) and every Assist with AI answer shows its kind label with the advice on hover (`sourceLinks` / `assistSourceLinks` in `_Shared/adminCommon.js`). Assist with AI's backup search keeps only kinds that fit the task (product lists: maker, retailer, public list, regulator; ingredient descriptions: regulator, medical, ingredient database).
