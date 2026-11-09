// Brand & Product Inventory: the 'brands' and 'products' collections the mobile app reads.
// GlowUp lists MAKE-UP ONLY. Skin care, hair, body, nail and fragrance entries are never shown in the normal list:
// they wait under "Not make-up" until they are moved to the Recycle bin. A product whose kind the rule cannot tell
// (or that is of a kind the project has not decided on yet) is listed, marked, and hidden by the app until an admin
// has given it a clear kind. The rule is the app's own: ../_Shared/glowupRules.js.
//
// A brand document:   { name, category, market, status, scans, createdAt, updatedAt }
//   - the app only uses brands whose status is "Active"
//   - "scans": the app adds 1 each time a scan is found to be this brand
//     (the brand's name is on the scanned label, or the scan is one of the brand's products)
//
// A product document: { name, brandId, brandName, cosmeticType, description, ingredients[], shades[],
//                       minAge, status, scans, createdAt, updatedAt }
//   - ingredients: the names printed on the packaging; the app checks each one against the user's profile
//   - shades: colours as "#RRGGBB"; AR Try-On offers them for this product
//   - shadeCodes: the shade code printed on the pack ("119", "N20") at the same place as its colour, "" = not known
//   - "scans": the app adds 1 each time a scan is recognised as this product (its ingredient list matches)
//   - the app only uses a product when its status AND its brand's status are "Active"
//
// Entries that were NOT typed here also carry { source, reviewed, addedBy, addedAtUnix } (products: sources[] too):
//   - source "scan": the app added it by itself when a user scanned something that was not listed (id starts with "scan_")
//   - source "openbeautyfacts": imported from the Open Beauty Facts database
//   - they arrive with status "Pending" and reviewed false, so the app does not use them until an admin approves them here
//
// Arranging and removing (see ../_Shared/adminOrganizer.js):
//   - folder: the name of the folder a product was put in ("" = none). Only this website uses it.
//   - binned true = in the Recycle bin. The entry's status is set to "Inactive" at the same moment, so the app stops
//     using it at once; statusBeforeBin keeps what it was, for "Restore". A brand goes to the bin together with its
//     products (binnedWith = the brand's id). Only "Delete forever" removes a document.
import { db } from "../_Database/firebase-console.js";
import { auth } from "../_Database/firebase-console.js";
import { collection, doc, serverTimestamp } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
// reading and writing go through the site's own copy of its lists: a list is downloaded once and kept between pages,
// and after a change only the documents that were written are read again (see adminStore.js)
import { getDocs, addDoc, updateDoc, writeBatch, onListsRefreshed } from "../_Shared/adminStore.js";
import {
    requireAdmin, wireSignOut, setupNavigationGuard, setupModal, escapeHtml, sameKey, toList, asArray, toWholeNumber, toDate, timeAgo,
    statusBadge, toneBadge, statusGuide, normalizeStatus, notify, describeError, setBusy, setFormError, originLabel, sourceLinks, assistSourceLinks, sourceGuideHtml, COSMETIC_TYPES, STATUSES,
    productScope, notMakeup, scopeWords, categoryOutOfScope, BRAND_CATEGORY, wireSearch, dialogIsOpen
} from "../_Shared/adminCommon.js";
import { matchesSearch, compactName, barcodeKey } from "../_Shared/glowupRules.js";
import { productVisibility, cleanupPlan, productDuplicates, brandDuplicates, mergeProducts, mergeBrands, nameWithoutBrand } from "../_Shared/glowupCatalog.js";
import { openPlanDialog, openDuplicatesDialog } from "../_Shared/adminCleanup.js";
import { mountShell, placeInHeader, paginate, renderPager, icon, BIN_ICON, openMenu, askConfirm, setBinCount } from "../_Shared/adminShell.js";
import { registerDataset, datasetChanged } from "../_Shared/adminWidgets.js";
import { createOrganizer, cleanFolder, friendly } from "../_Shared/adminOrganizer.js";
import { setAssistPage, registerAssist, refreshAssist } from "../_Shared/adminAssist.js";
import { readAdminDocument, splitForAi } from "../_Shared/adminDocumentReader.js";
import { startImport } from "../_Shared/importReview.js";
import { parseImportText, pairShades, shadeCodeOf, ingredientsNotHere } from "../_Shared/importFormat.js";
import { parseLabeledProducts } from "../_Shared/ingredientBlock.js";
import { nearShade } from "../_Shared/listRules.js";
import { mountIngredientPicker } from "../_Shared/adminIngredientPicker.js";
import { openFacets } from "../_Shared/adminFacets.js";
import { EXPORT_MAX_ROWS, EXPORT_FILE_PREFIX, toCsv, downloadCsv, today } from "../_Shared/adminExport.js";
import { setPageSettings } from "../_Shared/adminSettingsMenu.js";
import { mountSourceEditor, openApplyWindow } from "./productSources.js";
import { applyRole, brandPageOf, groupByType, bulkAllowed } from "../_Shared/sourceEdit.js";
import { countPending, openSnapshotReview, finishSnapshot, APPROVED_DESCRIPTION } from "../_Shared/adminSnapshots.js";
import { gs1Hint, VERIFIED_BY_GS1 } from "../_Shared/gs1Hint.js";

const OTHER_TYPE = "__other__";
const NO_BRAND = "__none__";                // the group for products whose brand no longer exists
const MAX_SHADES = 100;               // no practical limit for a real range (foundations run to 40+); the database rule allows 100
const SHADE_STRIP_FROM = 11;         // from 11 shades on, the shades sit in a box that scrolls up and down
const MAX_TAGS = 10;                        // filter words kept on a product (tags), so a filter needs one cheap query and no join
const BATCH_LIMIT = 400;                    // Firestore allows 500 writes in one batch
const BRANDS_PER_PAGE = 10;                 // "Brands": this many brands at a time
const PRODUCTS_PER_PAGE = 10;               // "All products" and the Recycle bin: this many rows at a time
const PRODUCTS_AT_A_TIME = 10;              // products of one brand, until "Show more" is pressed
const SHOWN_INGREDIENTS = 16;               // names listed in a product's details
const VIEW_KEY = "glowup.inventory.view.v1";
// true = when this page opens, the products of the FDA Philippines catalogue (../_Shared/data/fdaCatalogue.json) that are not
//        in the inventory yet are added by themselves, without a question. false = only the "..." menu adds them, after a question.
const AUTO_ADD_FDA_CATALOGUE = false;      // off since 2026-10-22: a brand or product deleted for good came back at the next visit of the page
let catalogueChecked = false;               // the catalogue is looked at once per visit of the page
// true = a product the app added from a scan is approved by itself (set to Active, with its brand) when the app found it on the
//        FDA Philippines list of notified cosmetics and that notification is still valid. Everything else keeps waiting for review.
const AUTO_APPROVE_FDA = true;
/** The app writes this into the description of a scanned product it found on the FDA Philippines Verification Portal. */
const fdaVerified = (p) => /FDA Philippines: notified as/i.test(p.description) && !/Notification ended/i.test(p.description);
const pageState = { page: 1, size: BRANDS_PER_PAGE };
const flatPage = { page: 1, size: PRODUCTS_PER_PAGE };
const binPage = { page: 1, size: PRODUCTS_PER_PAGE };
const closed = new Set();                   // brands whose products are folded away
const moreShown = new Map();                // brand id -> how many of its products are shown
let shownGroups = [];                       // the brands the table currently lists (all pages)
// The two count tiles open their own view of the list (5 November 2026): null = the normal list, or
//   { kind: "brands" }          only the brands; a click on one opens it
//   { kind: "brand", id }       the products of one brand, A to Z, 10 per page
//   { kind: "types" }           every product under its cosmetic type, A to Z, all on one page
let drill = null;
let keepDrill = false;                      // the organizer goes back to "All" because a tile asked for it
const brandPage = { page: 1, size: PRODUCTS_PER_PAGE };
let sourceEditor = null;                    // the sources box of the product form (productSources.js)

let everyBrand = [];                        // every brand document, also the ones in the Recycle bin
let everyProduct = [];
let ingredientSuggestions = [];
let ingredientSuggestionsError = "";
let inventoryAiDrafts = [];
let brands = [];                            // listed (not in the bin): [{ id, name, category, market, status, scans }]
let products = [];                          // listed: [{ id, name, brandId, brandName, cosmeticType, description, ingredients, shades, minAge, status, scans, folder }]
let binBrands = [];                         // in the Recycle bin
let binProducts = [];
let curatedKeys = new Set();                // every name / technical name / alias in the Ingredient Curator
let curatedList = [];                       // the same ingredients with their ids, for the picker of the product form: [{ id, name, techName, aliases }]
let ingredientPicker = null;                // the picker of the product form (adminIngredientPicker.js)
let snapshotsWaiting = null;                // products app users sent as a photo, waiting for review (null = not counted yet)
let curatorKnown = false;
let editingBrandId = null;
let editingProductId = null;
let formShades = [];                        // the shades being edited in the open product form
let formShadeCodes = [];                    // the shade codes printed on the pack ("119", "N20"), at the same places as formShades ("" = not known)
let assistedProductSources = [];
let brandModal = null;
let productModal = null;
let org = null;                             // folders, the Recycle bin, picking and dragging
let openId = "";                            // the product whose details are open
let quick = "";                             // a filter a helper switched on: "" | a key of QUICK
// The Filters button: several choices at once. Looked up in the products already loaded (no database read).
const FACTS = [
    { value: "has-shades", label: "Has shades" },
    { value: "no-ingredient-list", label: "No ingredient list" },
    { value: "fda-listed", label: "On the FDA Philippines list" }
];
let facets = { type: "", status: "", facts: [] };
const facetCount = () => (facets.type ? 1 : 0) + (facets.status ? 1 : 0) + facets.facts.length;
const facetOk = (p) => (!facets.type || sameKey(p.cosmeticType) === sameKey(facets.type)) && (!facets.status || p.status === facets.status)
    && facets.facts.every((fact) => productTags(p).includes(fact));
const view = { mode: "brands", sort: "name", layout: "rows" };      // "Brands" or "All products", the order, rows or cards; remembered in this browser

const el = (id) => document.getElementById(id);
const DASH = `<span class="muted">–</span>`;
const byName = (a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: "base" });
const many = (n, one, more) => `${n} ${n === 1 ? one : more}`;

/** "Are you sure?" as a dialog: the first paragraph of the question is its title, the rest its text. */
const sure = (question, confirmLabel, danger) => {
    const parts = question.split("\n\n");
    return askConfirm({ title: parts[0], message: parts.slice(1).join(" "), confirmLabel, danger });
};

// the orders the Sort button offers
const SORTS = [
    { id: "name", label: "Name A to Z", short: "A to Z", cmp: byName },
    { id: "nameDesc", label: "Name Z to A", short: "Z to A", cmp: (a, b) => byName(b, a) },
    { id: "scans", label: "Most scanned", short: "most scanned", cmp: (a, b) => b.scans - a.scans || byName(a, b) },
    { id: "new", label: "Newest first", short: "newest", cmp: (a, b) => b.addedAt - a.addedAt || byName(a, b) },
    { id: "type", label: "Type", short: "type", cmp: (a, b) => a.cosmeticType.localeCompare(b.cosmeticType, undefined, { sensitivity: "base" }) || byName(a, b) },
    { id: "attention", label: "Needs attention first", short: "needs attention", cmp: (a, b) => weightOf(b) - weightOf(a) || byName(a, b) }
];
const sortNow = () => SORTS.find((s) => s.id === view.sort) || SORTS[0];

// the filters a helper can switch on
const QUICK = {
    noIngredients: { label: "No ingredient list", test: (p) => p.ingredients.length === 0 },
    noShades: { label: "Make-up without shades", test: (p) => p.shades.length === 0 && productScope(p.cosmeticType, p.name) === "makeup" },
    notCurated: { label: "Ingredients not in the Curator", test: (p) => notCurated(p.ingredients).length > 0 },
    fda: { label: "On the FDA Philippines list", test: (p) => p.source === "fdaph" || /FDA Philippines: notified as/i.test(p.description) }
};

document.addEventListener("DOMContentLoaded", async () => {
    try { const saved = JSON.parse(localStorage.getItem(VIEW_KEY) || "{}"); if (saved.mode === "all") view.mode = "all"; if (SORTS.some((s) => s.id === saved.sort)) view.sort = saved.sort; } catch (error) { /* first visit */ }
    registerData();                   // before the page frame: a saved layout may hold panels that show this data
    mountShell("inventory");
    placeInHeader(".search-container", "headerActions");      // search beside the title, Add and More beside the layout button
    wireSignOut();
    wireWebsiteAndGs1Buttons();
    setupNavigationGuard();
    await requireAdmin();

    el("productTypeInput").innerHTML =
        `<option value="">Choose a type</option>` +
        COSMETIC_TYPES.map((t) => `<option value="${escapeHtml(t)}">${escapeHtml(t)}</option>`).join("") +
        `<option value="${OTHER_TYPE}">Other (type it)</option>`;

    brandModal = setupModal(el("addBrandModal"), () => { editingBrandId = null; });
    productModal = setupModal(el("productModal"), () => { editingProductId = null; });

    org = createOrganizer({
        kind: "products", noun: "products", nounOne: "product", allLabel: "All",
        panel: el("inventoryHost").closest(".panel"), host: el("inventoryHost"), shelf: el("inventoryShelf"), shelfAdd: el("inventoryShelfAdd"), shelfEnd: el("inventoryShelfEnd"), bar: el("inventoryBar"),
        folderNames: () => everyProduct.map((p) => p.folder).filter((name) => name),
        folderOf: (key) => { const p = key.startsWith("p:") ? everyProduct.find((x) => x.id === key.slice(2)) : null; return p ? p.folder : ""; },
        titleOf: (key) => { const item = itemOf(key); return item ? item.name || "Unnamed" : ""; },
        count: (v) => (v.kind === "bin" ? binEntries().length : listed().filter((p) => v.kind !== "folder" || sameKey(p.folder) === sameKey(v.name)).length),
        binKeys: () => binEntries().map((entry) => entry.key),
        move: moveToFolder, bin: moveToBin, restore: restoreFromBin, purge: purgeFromBin,
        renameFolder: (from, to) => refolder(from, to), deleteFolder: (name) => refolder(name, ""),
        selectionActions: () => [
            { label: "Assist with AI", className: "bar-main", tip: "Draft sourced ingredient lists and brand aliases for selected entries.", icon: icon("sparkle"), run: assistSelectedInventory },
            { label: "Approve All", className: "bar-quiet", tip: "Approve only eligible selected entries after you confirm you have reviewed them.", run: approveSelectedInventory },
            { label: "Apply website or source", className: "bar-quiet", tip: "Give every selected product a web address, also those on other pages. You see how many change and confirm first.", icon: icon("layers"), run: applyToSelected }
        ],
        // one brand's view: "select all" means the whole brand, also the products on other pages
        pickScope: () => (drill && drill.kind === "brand" && org.view.kind !== "bin" ? productsOf(drill.id).map((p) => "p:" + p.id) : null),
        onView: () => { if (!keepDrill) drill = null; fromFirstPage(); }
    });
    setAssistPage("inventory");
    registerHelpers();
    el("inventoryAiDraftList").addEventListener("change", updateInventoryDraftApproval);
    el("inventoryApproveAllBtn").addEventListener("click", approveInventoryDrafts);

    const askedFor = new URLSearchParams(window.location.search).get("q");
    if (askedFor) el("inventorySearchInput").value = askedFor;                 // ?q=blush: the search comes back after a reload
    wireSearchExtras();
    wireSearch(el("inventorySearchInput"), el("inventorySearchClear"), fromFirstPage);
    // the brand dropdown also says how the list is drawn: "All brands, grouped" or "All products, one list" (it replaced the
    // Brands / All products pills, which did the same job twice)
    el("brandFilterSelect").addEventListener("change", () => {
        drill = null;
        const value = el("brandFilterSelect").value;
        view.mode = value === FLAT_LIST ? "all" : "brands";
        saveView();
        if (org.view.kind === "bin") org.show({ kind: "all" }); else fromFirstPage();
    });
    el("reviewFilterSelect").addEventListener("change", fromFirstPage);
    document.querySelectorAll(".stat[data-drill]").forEach((tile) => tile.addEventListener("click", () => openDrill({ kind: tile.dataset.drill })));
    el("brandPageSize").addEventListener("change", () => { brandPage.size = Number(el("brandPageSize").value) || PRODUCTS_PER_PAGE; brandPage.page = 1; renderInventory(); });
    document.querySelectorAll(".stat[data-show]").forEach((tile) => tile.addEventListener("click", () => {
        if (!tile.dataset.show) resetAllFilters();
        else showOnly(tile.dataset.show);
    }));
    el("addMenuBtn").addEventListener("click", (event) => openMenu(event.currentTarget, [
        { label: "Brand", icon: "tag", description: "A new brand: name, category, market, status.", run: () => openBrandForm(null) },
        { label: "Product", icon: "lipstick", description: "A make-up product of a listed brand.", disabled: brands.length === 0, run: () => openProductForm(null, brandFilterValue()) }
    ]));
    // what was behind "..." here is listed in the settings window under "This page" (5 November 2026)
    setPageSettings(() => {
        const allClosed = shownGroups.length > 0 && shownGroups.every((g) => closed.has(g.id));
        const outside = outsideCount();
        return [
            { label: allClosed ? "Open all brands" : "Close all brands", icon: "down", disabled: view.mode !== "brands" || org.view.kind === "bin", description: allClosed ? "Show every brand's products." : "Show the brands only.", run: () => { if (allClosed) closed.clear(); else shownGroups.forEach((g) => closed.add(g.id)); renderInventory(); } },
            { separator: true },
            { label: "Clean up", icon: "sparkle", description: "What is not make-up, what is there twice, kinds and names to put right. You see every change and apply one step at a time.", run: openCleanUp },
            { label: "Find duplicates", icon: "layers", description: "Products and brands that are (or may be) the same thing. You choose which one to keep.", run: () => openDuplicates("products") },
            { separator: true },
            { label: `Export to CSV (newest ${EXPORT_MAX_ROWS})`, icon: "download", description: `Saves the newest ${EXPORT_MAX_ROWS} listed products as a spreadsheet file. Nothing is read from the database: it uses what is loaded.`, run: exportProductsCsv },
            { separator: true },
            { label: "Add the FDA Philippines catalogue", icon: "download", description: "Make-up that is on the FDA Philippines list of notified cosmetics. You see how many and confirm first.", run: importFdaCatalogue },
            { label: "Trim the FDA Philippines catalogue", icon: "undo", description: "Moves FDA-list products that are no longer in the catalogue, have no ingredient list and were never scanned to the Recycle bin.", run: trimFdaCatalogue },
            { label: `Remove what is not make-up (${outside})`, icon: "ban", danger: true, disabled: outside === 0, description: "Skin care, hair, body, nail and fragrance entries go to the Recycle bin. You see the list and confirm first.", run: removeOutOfScope }
        ];
    });
    el("sortMenuBtn").addEventListener("click", (event) => openMenu(event.currentTarget, [{ heading: "Order of the list" }].concat(
        SORTS.map((s) => ({ label: s.label, checked: s.id === view.sort, run: () => { view.sort = s.id; saveView(); fromFirstPage(); } })))));
    // Rows or Cards: only how the same rows are drawn (CSS), so every click, drag, selection and the pager work the same
    el("inventoryLayoutSwitch").addEventListener("click", (event) => {
        const button = event.target.closest("[data-layout]");
        if (!button) return;
        view.layout = button.dataset.layout === "cards" ? "cards" : "rows";
        saveView();
        paintLayout();
    });
    paintLayout();
    // a narrow list (a snapped or small window, Quick settings open) draws products as cards by itself, so the table never
    // needs a sideways scroll; it goes back to the chosen look when there is room again (two limits, so it cannot flicker)
    if (window.ResizeObserver) new ResizeObserver(() => {
        const width = el("inventoryHost").clientWidth;
        if (width === 0) return;
        const narrow = width < NARROW_LIST_PX ? true : width > NARROW_LIST_PX + 40 ? false : listIsNarrow;
        if (narrow !== listIsNarrow) { listIsNarrow = narrow; paintLayout(); }
    }).observe(el("inventoryHost"));
    el("inventoryQuick").addEventListener("click", (event) => {
        const chip = event.target.closest("[data-clear]");
        if (chip && chip.dataset.clear === "drill") { openDrill(drill && drill.kind === "brand" ? { kind: "brands" } : null); return; }
        if (chip && chip.dataset.clear === "brand") { setBrandChoice(""); return; }
        if (chip && chip.dataset.clear === "facets") facets = { type: "", status: "", facts: [] }; else quick = "";
        fromFirstPage();
    });
    el("filterMenuBtn").addEventListener("click", (event) => {
        const types = Array.from(new Set(products.map((p) => p.cosmeticType).filter(Boolean))).sort((a, b) => a.localeCompare(b));
        // "Show": the brand dropdown that sat above the table until 29 October 2026 (it stays in the page, hidden, and keeps the choice)
        const shows = Array.from(el("brandFilterSelect").options).filter((o) => o.value !== "").map((o) => ({ value: o.value, label: o.textContent }));
        openFacets(event.currentTarget, {
            title: "Filters",
            fields: [
                { id: "show", label: "Show", kind: "select", anyLabel: "All brands, grouped", options: shows, value: el("brandFilterSelect").value },
                { id: "type", label: "Kind", kind: "select", options: types.map((t) => ({ value: t, label: t })), value: facets.type },
                { id: "status", label: "Status", kind: "select", options: STATUSES.map((s) => ({ value: s, label: s })), value: facets.status },
                { id: "facts", label: "Only products that", kind: "checks", options: FACTS, value: facets.facts }
            ],
            onApply: (values) => {
                facets = { type: values.type || "", status: values.status || "", facts: values.facts || [] };
                if ((values.show || "") !== el("brandFilterSelect").value) setBrandChoice(values.show || "");
                else fromFirstPage();
            }
        });
    });
    sourceEditor = mountSourceEditor({
        host: el("productSourcesBox"),
        sources: () => assistedProductSources,
        setSources: (list) => { assistedProductSources = list; },
        brand: () => brands.find((b) => b.id === el("productBrandInput").value) || null,
        setError: (text) => setFormError(el("productForm"), text),
        openApply: (ask) => openApply(ask)
    });
    el("productBrandInput").addEventListener("change", () => sourceEditor.paint());
    el("productNameInput").addEventListener("input", showScopeHint);
    el("productTypeOtherInput").addEventListener("input", showScopeHint);
    el("addBrandForm").addEventListener("submit", saveBrand);
    el("productForm").addEventListener("submit", saveProduct);
    el("chooseInventoryFileBtn").addEventListener("click", () => el("inventoryFileInput").click());
    el("inventoryDropBox").addEventListener("click", () => el("inventoryFileInput").click());
    el("inventoryFileInput").addEventListener("change", (event) => {
        const file = event.target.files && event.target.files[0];
        event.target.value = "";
        if (file) importInventoryFile(file);
    });
    el("inventoryDropBox").addEventListener("dragover", (event) => {
        event.preventDefault();
        event.currentTarget.classList.add("drag-over");
    });
    el("inventoryDropBox").addEventListener("dragleave", (event) => event.currentTarget.classList.remove("drag-over"));
    el("inventoryDropBox").addEventListener("drop", (event) => {
        event.preventDefault();
        event.currentTarget.classList.remove("drag-over");
        const file = event.dataTransfer && event.dataTransfer.files && event.dataTransfer.files[0];
        if (file) importInventoryFile(file);
    });
    el("assistProductIngredientsBtn").addEventListener("click", assistProductIngredients);
    el("inventoryTableBody").addEventListener("click", onTableClick);
    el("ingredientSuggestionsList").addEventListener("click", onIngredientSuggestionClick);
    el("inventoryTableBody").addEventListener("dblclick", onTableDoubleClick);
    el("inventoryTableBody").addEventListener("keydown", (event) => { if (event.key === "Enter" && event.target.matches("tr.product-row")) { event.preventDefault(); toggleDetails(event.target.dataset.product); } });
    el("productTypeInput").addEventListener("change", showOtherTypeBox);
    el("productBrandInput").addEventListener("change", showBrandHint);
    el("productIngredientsInput").addEventListener("input", showIngredientSummary);
    ingredientPicker = mountIngredientPicker({
        textarea: el("productIngredientsInput"), search: el("productIngredientSearch"), results: el("productIngredientResults"), pills: el("productIngredientPills"),
        library: () => curatedList,
        create: createIngredientFromPicker
    });
    el("productShadeEditor").addEventListener("click", onShadeClick);
    el("productShadeEditor").addEventListener("input", onShadeInput);
    el("productShadeEditor").addEventListener("change", onShadeChange);
    el("productShadeEditor").addEventListener("keydown", onShadeKey);

    // the list on screen came from the site's own copy; when the database turns out to hold something newer, show that
    onListsRefreshed(() => { if (!dialogIsOpen()) loadInventory(); });
    const firstLoad = loadInventory();

    // Pending snapshot reviews: one count for the badge now; the snapshots themselves only when the window is opened
    el("snapshotReviewBtn").addEventListener("click", openSnapshots);
    countPending().then((n) => { snapshotsWaiting = n; paintSnapshotCount(); })
        .catch((error) => { console.warn("Could not count the pending snapshots:", error); snapshotsWaiting = null; paintSnapshotCount(true); });
    // the Dashboard's tile opens this page with ?snapshots=open: the window opens once the lists are here
    if (new URLSearchParams(location.search).get("snapshots") === "open") firstLoad.then(openSnapshots);
    // the Dashboard's "Add product" opens this page with ?add=product: the empty product form opens once the brands are here
    if (new URLSearchParams(location.search).get("add") === "product") firstLoad.then(() => openProductForm(null));
});

const saveView = () => { try { localStorage.setItem(VIEW_KEY, JSON.stringify(view)); } catch (error) { /* private window */ } };
function fromFirstPage() { pageState.page = 1; flatPage.page = 1; binPage.page = 1; brandPage.page = 1; renderInventory(); }

/** A count tile (or the chip that goes back) chose a view of the list. Opening another brand starts on its page 1 with nothing selected. */
function openDrill(next) {
    const otherBrand = !!(drill && drill.kind === "brand" && !(next && next.kind === "brand" && next.id === drill.id));
    drill = next;
    el("reviewFilterSelect").value = "";
    el("brandFilterSelect").value = "";
    quick = "";
    if (otherBrand) org.clear();
    if (org.view.kind !== "all") { keepDrill = true; org.show({ kind: "all" }); keepDrill = false; }
    else fromFirstPage();
}

/** The sources box or the selection bar asked to apply an address: the window, and what is kept on this page afterwards. */
function openApply(ask) {
    openApplyWindow({
        ...ask,
        brands: () => brands,
        productsOf,
        confirmWebsite: (url) => websiteFields(url, null),
        onSaved: ({ role, url, written, brandId }) => {
            written.forEach((change) => {
                const product = everyProduct.find((p) => p.id === change.id);
                if (product) product.sources = change.after;
            });
            // the form behind the window keeps what the admin typed, and gets what was just saved for this product
            if (editingProductId && written.some((change) => change.id === editingProductId)) {
                assistedProductSources = applyRole(assistedProductSources, role, url);
                sourceEditor.paint();
            }
            const brand = brandId ? everyBrand.find((b) => b.id === brandId) : null;
            if (brand) { brand.website = url; brand.websiteConfirmedAt = new Date(); sourceEditor.paint(); }
            renderInventory();
        }
    });
}

/** "Apply website or source" in the bar: everything selected counts, also what is on other pages. */
function applyToSelected(keys) {
    const ids = keys.filter((key) => key.startsWith("p:")).map((key) => key.slice(2));
    const first = products.find((p) => ids.includes(p.id));
    if (!first) { notify("Select at least one product.", "error"); return; }
    const brand = brands.find((b) => b.id === first.brandId);
    if (!brand || !bulkAllowed(brand.name)) { notify("Applying to other products is not switched on for this brand yet.", "error"); return; }
    openApply({ role: "primary", url: "", brandId: first.brandId, chosen: ids });
}
/** What a count tile (or a helper) narrows the list to: "" | "review" | "out". The same choice again shows everything. */
function showOnly(value, keep) {
    const select = el("reviewFilterSelect");
    select.value = select.value === value && !keep ? "" : value;
    quick = "";
    drill = null;
    if (org.view.kind === "bin") org.show({ kind: "all" }); else fromFirstPage();
}
/** A visible "Show all" action returns to the whole inventory, not just the current search or folder. */
function resetAllFilters() {
    facets = { type: "", status: "", facts: [] };
    el("inventorySearchInput").value = "";
    el("brandFilterSelect").value = "";
    el("reviewFilterSelect").value = "";
    quick = "";
    org.show({ kind: "all" });
}

// ---------------------------------------------------------------------------------------------
// Loading
// ---------------------------------------------------------------------------------------------
/** When an entry was made, in seconds (for "Newest first"). */
const madeAt = (data) => { const made = toDate(data.createdAt); return made ? Math.floor(made.getTime() / 1000) : toWholeNumber(data.addedAtUnix); };
/** What the Recycle bin wrote on an entry. */
const binFields = (data) => { const since = toDate(data.binnedAt); return { binned: data.binned === true, binnedAt: since ? since.getTime() : 0, statusBeforeBin: String(data.statusBeforeBin || ""), binnedWith: String(data.binnedWith || ""), binnedReason: String(data.binnedReason || "") }; };

function renderIngredientSuggestions() {
    const panel = el("ingredientSuggestionsPanel");
    const list = el("ingredientSuggestionsList");
    const items = ingredientSuggestions.slice().sort((a, b) => a.productName.localeCompare(b.productName, undefined, { sensitivity: "base" }));
    panel.hidden = items.length === 0 && !ingredientSuggestionsError;
    el("ingredientSuggestionsCount").textContent = many(items.length, "suggestion", "suggestions");
    if (ingredientSuggestionsError) {
        list.innerHTML = `<p class="table-message error">Suggestions are unavailable: ${escapeHtml(ingredientSuggestionsError)} If this account should be an admin, add its UID as a document in Firestore <code>admins</code>, then publish the Firestore rules. The inventory list can still be viewed.</p>`;
        return;
    }
    list.innerHTML = items.map((suggestion) => {
        const foundProduct = everyProduct.find((product) => product.id === suggestion.productId);
        const alreadyHasList = foundProduct && foundProduct.ingredients.length > 0;
        return `<article class="suggestion-card" data-suggestion="${escapeHtml(suggestion.id)}">
            <div class="suggestion-title">${escapeHtml(suggestion.productName || "Unnamed product")}</div>
            <div class="suggestion-meta">${escapeHtml(suggestion.brandName || "Unknown brand")} · ${many(suggestion.ingredients.length, "ingredient", "ingredients")}${alreadyHasList ? " · product already has a list; this suggestion cannot replace it" : ""}</div>
            <p class="suggestion-ingredients">${escapeHtml(suggestion.ingredients.join(", "))}</p>
            <div class="suggestion-sources"><strong>Sources:</strong> ${escapeHtml(suggestion.sources.join(" · ") || "No source details")}</div>
            <div class="suggestion-actions">
                <button type="button" class="btn" data-suggestion-action="dismiss">Dismiss</button>
                <button type="button" class="primary-btn" data-suggestion-action="apply"${!foundProduct || alreadyHasList ? " disabled" : ""}>Add to catalogue</button>
            </div>
        </article>`;
    }).join("");
}

async function onIngredientSuggestionClick(event) {
    const button = event.target.closest("[data-suggestion-action]");
    if (!button) return;
    const card = button.closest("[data-suggestion]");
    const suggestion = ingredientSuggestions.find((item) => item.id === card.dataset.suggestion);
    if (!suggestion) return;
    const product = everyProduct.find((item) => item.id === suggestion.productId);
    if (button.dataset.suggestionAction === "apply") {
        if (!product || product.ingredients.length > 0) {
            notify("The product is missing or already has an ingredient list. The suggestion was not applied.", "error");
            await loadInventory();
            return;
        }
        const question = `Add the web-found ingredient list to "${product.name}"?\n\nReview the source pages above first. This replaces no existing list; it only fills an empty product list.`;
        if (!(await sure(question, "Add ingredient list", false))) return;
    } else if (!(await sure(`Dismiss the ingredient suggestion for "${suggestion.productName}"?\n\nThe proposal will be removed from this review list.`, "Dismiss", false))) return;

    button.disabled = true;
    try {
        const batch = writeBatch(db);
        if (button.dataset.suggestionAction === "apply") {
            const sources = Array.from(new Set([...(product.sources || []), ...suggestion.sources])).slice(0, 10);
            batch.update(doc(db, "products", product.id), { ingredients: suggestion.ingredients, sources, updatedAt: serverTimestamp() });
        }
        batch.delete(doc(db, "productIngredientSuggestions", suggestion.id));
        await batch.commit();
        notify(button.dataset.suggestionAction === "apply" ? "Ingredient list added to the catalogue." : "Suggestion dismissed.", "ok");
        await loadInventory();
    } catch (error) {
        console.error("Error processing ingredient suggestion: ", error);
        notify("The ingredient suggestion was not processed. " + describeError(error), "error");
        button.disabled = false;
    }
}

async function loadInventory() {
    const body = el("inventoryTableBody");
    try {
        const [brandSnapshot, productSnapshot] = await Promise.all([
            getDocs(collection(db, "brands")),
            getDocs(collection(db, "products"))
        ]);
        everyBrand = brandSnapshot.docs.map((d) => {
            const data = d.data();
            return Object.assign({
                id: d.id,
                name: data.name || "",
                category: data.category || "",
                market: data.market || "Global",
                status: normalizeStatus(data.status),
                rawStatus: String(data.status || ""),          // as stored: an empty one is NOT Active for the app (see productVisibility)
                scans: toWholeNumber(data.scans),
                source: String(data.source || "").trim().toLowerCase(),
                reviewed: data.reviewed === true,
                needsReview: data.reviewed === false,         // only entries the app or an import added carry this mark
                autoApproved: data.autoApproved === true,     // approved by this page itself (the FDA Philippines list confirmed it)
                aliases: asArray(data.aliases),               // other ways the name is written (kept when two entries are merged)
                logoUrl: String(data.logoUrl || ""),          // the brand's logo and picture: web addresses only
                // the brand's own website: "website" is set ONLY by an admin who confirmed it (then its pages are labelled
                // "Manufacturer's own site"); "websiteSuggested" is what Assist with AI or an import proposed, not trusted
                website: String(data.website || ""),
                websiteSuggested: String(data.websiteSuggested || ""),
                websiteConfirmedAt: toDate(data.websiteConfirmedAt),
                imageUrl: String(data.imageUrl || ""),
                addedAt: madeAt(data)
            }, binFields(data));
        });
        everyProduct = productSnapshot.docs.map((d) => {
            const data = d.data();
            const paired = pairShades(data.shades, data.shadeCodes);
            return Object.assign({
                id: d.id,
                name: data.name || data.productName || "",
                brandId: data.brandId || "",
                brandName: data.brandName || "",
                cosmeticType: data.cosmeticType || data.category || "",
                description: data.description || "",
                ingredients: asArray(data.ingredients),
                shades: paired.shades,
                shadeCodes: paired.codes,
                minAge: toWholeNumber(data.minAge),
                status: normalizeStatus(data.status),
                rawStatus: String(data.status || ""),
                scans: toWholeNumber(data.scans),
                source: String(data.source || "").trim().toLowerCase(),
                sources: Array.isArray(data.sources) ? data.sources.map((s) => String(s)) : [],
                reviewed: data.reviewed === true,
                needsReview: data.reviewed === false,
                autoApproved: data.autoApproved === true,
                barcode: String(data.barcode || ""),
                fdaNumber: String(data.fdaNumber || "").replace(/\s+/g, " ").trim().slice(0, 40),
                imageUrl: String(data.imageUrl || ""),
                ingredientNotes: Array.isArray(data.ingredientNotes) ? data.ingredientNotes : [],     // per-product caution and share of an ingredient
                aliases: asArray(data.aliases),
                folder: cleanFolder(data.folder),
                addedAt: madeAt(data)
            }, binFields(data));
        });
    } catch (error) {
        console.error("Error fetching the inventory from Firestore: ", error);
        body.innerHTML = `<tr><td colspan="7" class="table-message error">Could not load the inventory. ${escapeHtml(describeError(error))}</td></tr>`;
        document.querySelectorAll(".stat-value.skeleton").forEach((box) => { box.classList.remove("skeleton"); box.textContent = "–"; });
        datasetChanged("products");
        datasetChanged("brands");
        return;
    }

    // Suggestions are admin-only, but the catalogue itself is readable without that collection.
    // A missing admins/{uid} record must not blank the otherwise-readable inventory table.
    ingredientSuggestionsError = "";
    try {
        const suggestionSnapshot = await getDocs(collection(db, "productIngredientSuggestions"));
        ingredientSuggestions = suggestionSnapshot.docs.map((d) => {
            const data = d.data();
            return {
                id: d.id,
                productId: String(data.productId || ""),
                productName: String(data.productName || ""),
                brandName: String(data.brandName || ""),
                ingredients: asArray(data.ingredients),
                sources: asArray(data.sources)
            };
        });
    } catch (error) {
        console.warn("Could not load the admin-only ingredient suggestions: ", error);
        ingredientSuggestions = [];
        ingredientSuggestionsError = describeError(error);
    }
    renderIngredientSuggestions();

    // the Ingredient Curator's names, to point out ingredients it does not explain yet (optional)
    curatedKeys = new Set();
    curatedList = [];
    try {
        const ingredientSnapshot = await getDocs(collection(db, "ingredients"));
        ingredientSnapshot.forEach((d) => {
            const data = d.data();
            if (data.binned === true) return;
            [data.name, data.techName, ...asArray(data.aliases)].forEach((n) => { const k = sameKey(n); if (k) curatedKeys.add(k); });
            if (data.name) curatedList.push({ id: d.id, name: String(data.name), techName: String(data.techName || ""), aliases: asArray(data.aliases), riskLevel: String(data.riskLevel || "") });
        });
        curatedList.sort((a, b) => a.name.localeCompare(b.name));
        curatorKnown = true;
    } catch (error) {
        console.warn("Could not read the Ingredient Curator: ", error);
        curatorKnown = false;
    }

    // a scanned product the FDA Philippines list confirms is approved by itself, together with its brand
    if (AUTO_APPROVE_FDA) {
        const fresh = everyProduct.filter((p) => p.needsReview && !p.binned && p.source === "scan" && fdaVerified(p) && p.ingredients.length > 0
            && productScope(p.cosmeticType, p.name) === "makeup" && everyBrand.some((b) => b.id === p.brandId && !b.binned));
        if (fresh.length > 0) {
            const brandIds = new Set(fresh.map((p) => p.brandId));
            const freshBrands = everyBrand.filter((b) => brandIds.has(b.id) && b.needsReview);
            const stamp = () => ({ status: "Active", reviewed: true, autoApproved: true, reviewedAt: serverTimestamp(), updatedAt: serverTimestamp() });
            try {
                await runBatches(fresh.map((p) => ({ ref: doc(db, "products", p.id), data: stamp() })).concat(freshBrands.map((b) => ({ ref: doc(db, "brands", b.id), data: stamp() }))));
                fresh.concat(freshBrands).forEach((x) => { x.status = "Active"; x.rawStatus = "Active"; x.reviewed = true; x.needsReview = false; });
                notify(`${many(fresh.length, "scanned product", "scanned products")} confirmed by FDA Philippines: approved automatically.`, "ok");
            } catch (error) {
                console.warn("Automatic approval did not work: ", error);       // they simply stay "waiting for review"
            }
        }

    }

    sortOut();
    fillBrandSelects();
    fillReviewFilter();
    renderInventory();
    datasetChanged("products");           // the data panels (lists, grids, graphs) draw the new data
    datasetChanged("brands");

    // a brand the app added from a scan (still Pending) that holds products from the FDA Philippines list is approved by itself:
    // otherwise the app would keep hiding those products
    if (AUTO_APPROVE_FDA) {
        const waiting = everyBrand.filter((b) => b.needsReview && !b.binned && !categoryOutOfScope(b.category)
            && everyProduct.some((p) => p.brandId === b.id && !p.binned && p.source === "fdaph"));
        if (waiting.length > 0) {
            try {
                await runBatches(waiting.map((b) => ({ ref: doc(db, "brands", b.id), data: { status: "Active", reviewed: true, autoApproved: true, reviewedAt: serverTimestamp(), updatedAt: serverTimestamp() } })));
                waiting.forEach((b) => { b.status = "Active"; b.rawStatus = "Active"; b.reviewed = true; b.needsReview = false; });
                notify(`${waiting.map((b) => b.name).join(", ")}: on the FDA Philippines list, approved automatically.`, "ok");
                afterChange();
            } catch (error) { console.warn("Automatic approval of the brand did not work: ", error); }
        }
    }

    // the FDA Philippines catalogue is applied by itself, once per visit (what is listed already is left alone)
    if (AUTO_ADD_FDA_CATALOGUE && !catalogueChecked) { catalogueChecked = true; importFdaCatalogue(true); }
}

/** Splits everything that was loaded into what is listed and what is in the Recycle bin. */
function sortOut() {
    everyBrand.sort(byName);
    everyProduct.sort(byName);
    brands = everyBrand.filter((b) => !b.binned);
    binBrands = everyBrand.filter((b) => b.binned);
    products = everyProduct.filter((p) => !p.binned);
    binProducts = everyProduct.filter((p) => p.binned);
    setBinCount(binBrands.length + binProducts.length, "products and brands");
    if (org) org.sync(everyProduct.map((p) => "p:" + p.id).concat(everyBrand.map((b) => "b:" + b.id)));
}

/** Something was changed on this page: the lists, the table and the data panels follow at once. */
function afterChange() {
    sortOut();
    fillBrandSelects();
    fillReviewFilter();
    renderInventory();
    datasetChanged("products");
    datasetChanged("brands");
}

/** The "waiting for review" box, with how many entries are waiting. The first time, an address ending in ?show=review opens on it. */
function fillReviewFilter() {
    const select = el("reviewFilterSelect");
    const asked = new URLSearchParams(window.location.search).get("show");
    const wanted = select.dataset.ready ? select.value : (asked === "review" || asked === "out" ? asked : "");
    select.dataset.ready = "1";
    select.innerHTML = `<option value="">Everything</option><option value="review">Waiting for review (${reviewCount()})</option><option value="out">Not make-up (${outsideCount()})</option>`;
    select.value = wanted;
}

/** The CONFIRMED website whose pages count as the maker's own site: the product's brand's, or the brand's own. */
const websiteOfEntry = (entry) => ("brandId" in entry ? (brandOf(entry) || {}).website : entry.website) || "";

/** Where an entry came from and whether an admin has checked it, as HTML ("" for an entry typed here). */
function originText(entry) {
    const origin = originLabel(entry.source) || (entry.needsReview ? "Added automatically" : "");
    if (!origin) return "";
    const links = sourceLinks(entry.sources || [], websiteOfEntry(entry));
    const state = entry.needsReview ? " · needs review" : entry.reviewed ? " · reviewed" : "";
    return escapeHtml(origin + state) + (links ? " · " + links : "");
}

/** The dropdown's choice that lists every product in one list (no brand rows). */
const FLAT_LIST = "__all_products";
/** The brand the list is narrowed to ("" = every brand). */
const brandFilterValue = () => { const value = el("brandFilterSelect").value; return value === FLAT_LIST ? "" : value; };
/** Chooses in the (hidden) brand dropdown as if the admin had picked it there: its own change handler does the rest. */
function setBrandChoice(value) {
    const select = el("brandFilterSelect");
    select.value = value;
    select.dispatchEvent(new Event("change"));
}

function fillBrandSelects() {
    const filter = el("brandFilterSelect");
    // the first time, an address ending in ?brand=ID opens on that brand
    const wanted = filter.dataset.ready ? filter.value : (new URLSearchParams(window.location.search).get("brand") || "");
    filter.dataset.ready = "1";
    const option = (b) => `<option value="${escapeHtml(b.id)}">${escapeHtml(b.name || "Unnamed Brand")}${b.status === "Active" ? "" : " (" + escapeHtml(b.status) + ")"}</option>`;
    const offered = brands.filter((b) => !brandOut(b));
    filter.innerHTML = `<option value="">All brands, grouped</option><option value="${FLAT_LIST}">All products, one list</option>` + offered.map(option).join("");
    filter.value = offered.some((b) => b.id === wanted) ? wanted : view.mode === "all" ? FLAT_LIST : "";
    el("productBrandInput").innerHTML = `<option value="">Choose a brand</option>` + brands.map(option).join("");
}

const INVENTORY_AI_DAILY_LIMIT = 40;
const IMPORT_GROUP = 40;                      // imported records saved per database batch (an import itself has no limit)
const INVENTORY_AI_LIMIT_KEY = "glowup.inventory.ai.daily";
function inventoryAiCalls(add) {
    const today = new Date().toISOString().slice(0, 10);
    let state = {};
    try { state = JSON.parse(localStorage.getItem(INVENTORY_AI_LIMIT_KEY) || "{}"); } catch (error) { state = {}; }
    const count = (state.day === today ? Number(state.count) || 0 : 0) + (add ? 1 : 0);
    if (add) localStorage.setItem(INVENTORY_AI_LIMIT_KEY, JSON.stringify({ day: today, count }));
    return count;
}

/** countIt = false: a later part of one imported file (a whole file counts as ONE use of the day's limit). */
async function askInventoryAi(payload, countIt = true, signal = null) {
    if (countIt && inventoryAiCalls(false) >= INVENTORY_AI_DAILY_LIMIT) throw new Error(`Assist with AI was used ${INVENTORY_AI_DAILY_LIMIT} times today in this browser. Try again tomorrow.`);
    const user = auth.currentUser;
    if (!user) throw new Error("You are signed out. Sign in again.");
    const stop = new AbortController();
    const timer = setTimeout(() => stop.abort(), 75000);
    const onCancel = () => stop.abort();                  // Cancel on the drop box stops the question at once
    if (signal) signal.addEventListener("abort", onCancel);
    let answer;
    try {
        answer = await fetch("/api/assist-ingredient", {
            method: "POST", signal: stop.signal,
            headers: { "Content-Type": "application/json", Authorization: "Bearer " + (await user.getIdToken()) },
            body: JSON.stringify(payload)
        });
    } finally { clearTimeout(timer); if (signal) signal.removeEventListener("abort", onCancel); }
    const body = await answer.json().catch(() => ({}));
    if (!answer.ok) throw new Error(body.error || `The AI could not be reached (error ${answer.status}).`);
    if (countIt) inventoryAiCalls(true);
    return body;
}

function assistProductSourceLinks(sources) {
    return assistSourceLinks(sources, "<b>The AI named no web page. Check every ingredient against the package.</b>");
}

function paintInventoryDrafts() {
    const panel = el("inventoryAiDraftPanel");
    const list = el("inventoryAiDraftList");
    panel.hidden = inventoryAiDrafts.length === 0;
    el("inventoryAiDraftCount").textContent = many(inventoryAiDrafts.length, "draft", "drafts");
    list.innerHTML = inventoryAiDrafts.map((draft, index) => {
        const fields = draft.kind === "brand"
            ? `<label class="field">Market <input data-draft-field="market" value="${escapeHtml(draft.draft.market || draft.item.market || "")}" maxlength="40"></label>
               <label class="field">Aliases (comma separated) <input data-draft-field="aliases" value="${escapeHtml((draft.draft.aliases || []).join(", "))}" maxlength="400"></label>`
              + (draft.draft.websiteSuggested ? `<p class="field-hint">Suggested official website: <code class="small">${escapeHtml(draft.draft.websiteSuggested)}</code>${draft.draft.websiteSeenInSources ? " (a page the AI used is on it)" : " (no page the AI used is on it)"}. Approving keeps it as a SUGGESTION only: confirm it on the Brand form before its pages count as the maker's.</p>` : "")
            : `<label class="field">Ingredient list <textarea data-draft-field="ingredients" rows="4">${escapeHtml((draft.draft.ingredients || []).join(", "))}</textarea></label>`;
        const sources = draft.kind === "brand" ? assistProductSourceLinks(draft.sources) : assistProductSourceLinks(draft.sources);
        return `<article class="suggestion-card" data-draft-index="${index}">
            <div class="suggestion-title"><strong>${escapeHtml(draft.item.name || "Unnamed")} — ${draft.kind === "brand" ? "brand details" : "product ingredients"}</strong></div>
            ${fields}<p class="field-hint">AI: ${escapeHtml(draft.model || "unknown model")}${draft.searched === false ? " (no web search; verify carefully)" : ""}</p>
            <p class="field-hint">Sources: ${sources}</p>
            <label class="field-hint"><input type="checkbox" data-draft-reviewed> I checked this draft against its sources or the product packaging.</label>
        </article>`;
    }).join("");
    updateInventoryDraftApproval();
}

function updateInventoryDraftApproval() {
    if (!el("inventoryApproveAllBtn")) return;
    el("inventoryApproveAllBtn").disabled = !el("inventoryAiDraftList").querySelector("[data-draft-reviewed]:checked");
}

async function assistSelectedInventory(keys) {
    const selectedItems = keys.map((key) => ({ key, item: itemOf(key), kind: key.startsWith("b:") ? "brand" : "product" }))
        .filter(({ item }) => item && !item.binned);
    const eligible = selectedItems.filter(({ item, kind }) => {
        if (kind === "brand") return !brandOut(item) && !!item.name;
        const brand = brands.find((candidate) => candidate.id === item.brandId);
        return !isOut(item) && !isUnclear(item) && item.ingredients.length === 0 && !!brand && !!item.name;
    });
    if (!eligible.length) {
        notify("Select at least one listed brand or a make-up product that has no ingredient list.", "info");
        return;
    }

    let completed = 0;
    for (const entry of eligible) {
        const draftKey = `${entry.kind}:${entry.item.id}`;
        if (inventoryAiDrafts.some((draft) => draft.key === draftKey)) continue;
        try {
            const payload = entry.kind === "brand"
                ? { mode: "brand", name: entry.item.name, brandWebsite: entry.item.website || "" }
                : { mode: "product", brandName: entry.item.brandName, productName: entry.item.name, brandWebsite: websiteOfEntry(entry.item) };
            const body = await askInventoryAi(payload);
            if (!body.found) throw new Error(body.message || "No sourced draft was found.");
            inventoryAiDrafts.push({ key: draftKey, kind: entry.kind, item: entry.item, draft: body.draft, sources: body.sources || body.draft.sources || [], model: body.model, searched: body.searched });
            completed++;
        } catch (error) {
            console.error(`Could not draft AI assistance for ${entry.item.name}:`, error);
            notify(`AI could not assist "${entry.item.name}". ${describeError(error)}`, "error");
        }
    }
    paintInventoryDrafts();
    if (completed) notify(`${many(completed, "draft is", "drafts are")} ready. Check the sources and mark the drafts you reviewed before approving.`, "ok");
}

async function approveInventoryDrafts() {
    const checked = Array.from(el("inventoryAiDraftList").querySelectorAll("[data-draft-reviewed]:checked"))
        .map((checkbox) => Number(checkbox.closest("[data-draft-index]").dataset.draftIndex))
        .filter((index) => Number.isInteger(index) && inventoryAiDrafts[index]);
    const drafts = checked.map((index) => ({ index, entry: inventoryAiDrafts[index], card: el("inventoryAiDraftList").querySelector(`[data-draft-index="${index}"]`) }));
    if (!drafts.length) return;
    if (!(await sure(`Approve ${many(drafts.length, "reviewed draft", "reviewed drafts")}?`, "Approve All", false))) return;
    const ops = [];
    const approvedDrafts = [];
    const approvedBrandIds = new Set();
    drafts.forEach(({ entry, card }) => {
        const item = entry.item;
        if (entry.kind === "brand") {
            const brand = everyBrand.find((candidate) => candidate.id === item.id && !candidate.binned);
            if (!brand || brandOut(brand)) return;
            const aliases = (card.querySelector('[data-draft-field="aliases"]').value || "").split(",").map((value) => value.trim()).filter(Boolean);
            const mergedAliases = Array.from(new Map(brand.aliases.concat(aliases).filter((alias) => sameKey(alias) !== sameKey(brand.name)).map((alias) => [sameKey(alias), alias])).values());
            const market = card.querySelector('[data-draft-field="market"]').value.trim();
            const data = { aliases: mergedAliases, aiAssistModel: entry.model || "", aiAssistSources: entry.sources.map((source) => source.url).filter(Boolean), updatedAt: serverTimestamp() };
            if (!brand.market && market) data.market = market;
            if (!brand.website && entry.draft && entry.draft.websiteSuggested) data.websiteSuggested = entry.draft.websiteSuggested;
            if (brand.needsReview) Object.assign(data, approval());
            ops.push({ ref: doc(db, "brands", brand.id), data });
            approvedBrandIds.add(brand.id);
        } else {
            const product = everyProduct.find((candidate) => candidate.id === item.id && !candidate.binned);
            const brand = product && brands.find((candidate) => candidate.id === product.brandId);
            const ingredients = card.querySelector('[data-draft-field="ingredients"]').value.split(/[,;\n]/).map((value) => value.trim()).filter(Boolean);
            if (!product || product.ingredients.length || isOut(product) || isUnclear(product) || !brand || !ingredients.length) return;
            const data = { ingredients: Array.from(new Set(ingredients)), aiAssistModel: entry.model || "", sources: Array.from(new Set(product.sources.concat(entry.sources.map((source) => `${source.title || source.site || "AI source"} | ${source.url}`).filter((value) => !value.endsWith(" | "))))), updatedAt: serverTimestamp() };
            if (product.needsReview) Object.assign(data, approval());
            ops.push({ ref: doc(db, "products", product.id), data });
            if (brand.needsReview && !approvedBrandIds.has(brand.id)) {
                ops.push({ ref: doc(db, "brands", brand.id), data: approval() });
                approvedBrandIds.add(brand.id);
            }
        }
        approvedDrafts.push(entry);
    });
    if (!ops.length) {
        notify("No drafts were saved. Confirm the selected records still meet the review requirements.", "error");
        return;
    }
    try {
        const uniqueOps = Array.from(ops.reduce((byPath, op) => {
            byPath.set(op.ref.path, Object.assign(byPath.get(op.ref.path) || { ref: op.ref, data: {} }, { data: Object.assign({}, byPath.get(op.ref.path)?.data || {}, op.data) }));
            return byPath;
        }, new Map()).values());
        await saveOrReload(uniqueOps);
        inventoryAiDrafts = inventoryAiDrafts.filter((entry) => !approvedDrafts.includes(entry));
        paintInventoryDrafts();
        notify(`${many(approvedDrafts.length, "reviewed draft was", "reviewed drafts were")} saved.`, "ok");
    } catch (error) {
        notify("The reviewed drafts were not saved. " + describeError(error), "error");
    }
}

async function approveSelectedInventory(keys) {
    const selectedItems = keys.map((key) => ({ key, item: itemOf(key) })).filter(({ item }) => item && !item.binned && item.needsReview);
    const eligibleBrands = selectedItems.filter(({ key, item }) => key.startsWith("b:") && !brandOut(item));
    const eligibleProducts = selectedItems.filter(({ key, item }) => {
        if (!key.startsWith("p:") || isOut(item) || isUnclear(item) || !item.ingredients.length) return false;
        return brands.some((brand) => brand.id === item.brandId && !brand.binned);
    });
    const ops = eligibleBrands.map(({ item }) => ({ ref: doc(db, "brands", item.id), data: approval() }));
    const brandIds = new Set(eligibleBrands.map(({ item }) => item.id));
    eligibleProducts.forEach(({ item }) => {
        ops.push({ ref: doc(db, "products", item.id), data: approval() });
        const brand = brands.find((candidate) => candidate.id === item.brandId);
        if (brand && brand.needsReview && !brandIds.has(brand.id)) {
            ops.push({ ref: doc(db, "brands", brand.id), data: approval() });
            brandIds.add(brand.id);
        }
    });
    if (!ops.length) {
        notify("No selected entries are eligible: products need a make-up type, a brand, and an ingredient list.", "info");
        return;
    }
    const names = eligibleBrands.map(({ item }) => item.name).concat(eligibleProducts.map(({ item }) => item.name));
    if (!(await sure(`Approve ${many(names.length, "reviewed entry", "reviewed entries")}?\n\nOnly waiting-for-review entries are included. Products without a complete ingredient list or valid make-up type are excluded.`, "Approve All", false))) return;
    try {
        await saveOrReload(ops);
        notify(`${many(names.length, "entry was", "entries were")} approved.`, "ok");
    } catch (error) {
        notify("The selected entries were not approved. " + describeError(error), "error");
    }
}

async function assistProductIngredients() {
    const button = el("assistProductIngredientsBtn"), note = el("productAiNote");
    const brand = brands.find((item) => item.id === el("productBrandInput").value);
    const productName = el("productNameInput").value.trim();
    note.hidden = false;
    if (!brand || !productName) {
        note.textContent = "Choose a brand and enter the exact product name first.";
        return;
    }
    setBusy(button, true, "Searching...");
    note.textContent = "Searching for the exact product and ingredient list. This may take up to a minute.";
    try {
        const body = await askInventoryAi({ mode: "product", brandName: brand.name, productName, brandWebsite: brand.website || "" });
        if (!dialogIsOpen() || el("productNameInput").value.trim() !== productName || el("productBrandInput").value !== brand.id) return;
        if (!body.found || !body.draft || !Array.isArray(body.draft.ingredients) || body.draft.ingredients.length === 0) {
            note.innerHTML = `<b>No ingredient-list draft was found.</b> ${escapeHtml(body.message || "Check the package or try another spelling.")}<br>${assistProductSourceLinks(body.sources)}`;
            return;
        }
        el("productIngredientsInput").value = body.draft.ingredients.join(", ");
        if (ingredientPicker) ingredientPicker.refresh();
        const sources = (body.sources || []).filter((page) => page && /^https:\/\//i.test(page.url || ""))
            .map((page) => `${page.title || page.site || page.url} | ${page.url}`);      // the kind label is worked out when shown, never stored
        if (body.draft.fdaVerifiedLink && /^https:\/\/([a-z0-9-]+\.)*fda\.gov\.ph\//i.test(body.draft.fdaVerifiedLink))
            sources.unshift(`FDA Philippines verification | ${body.draft.fdaVerifiedLink}`);
        assistedProductSources = Array.from(new Set(assistedProductSources.concat(sources))).slice(0, 10);
        sourceEditor.paint();
        showIngredientSummary();
        note.innerHTML = `<b>AI draft only; nothing is saved yet.</b> Compare the list against the package before saving.`
            + (body.searched === false ? ` <b>${escapeHtml(body.model || "A backup AI")} answered without web search; it gave no source pages.</b>` : "")
            + `<br><b>Sources:</b><br>${assistProductSourceLinks(body.sources)}`
            + (body.notes && body.notes.length ? `<br><span class="field-hint">Provider notes: ${escapeHtml(body.notes.join("; "))}.</span>` : "");
    } catch (error) {
        const reason = error && error.name === "AbortError" ? "The AI took too long." : (error && error.message) || "Something went wrong.";
        note.textContent = `Assist with AI did not work. ${reason}`;
    } finally {
        setBusy(button, false);
    }
}

/**
 * The import drop box (importReview.js): read, then check every record in a window, then save. Nothing reaches the
 * database before the admin has checked it and pressed Save.
 */
function importInventoryFile(file) {
    showImportFlags(el("inventoryImportFlags"), []);
    startImport({
        dropbox: el("inventoryDropBox"), choose: el("chooseInventoryFileBtn"), status: el("inventoryImportStatus"), file,
        read: (f, say) => readAdminDocument(f, say),
        extract: extractInventory,
        review: INVENTORY_REVIEW,
        save: (draft, say) => saveInventoryDraft(draft, file.name, say)
    });
}

const INVENTORY_REVIEW = {
    title: "Check before saving",
    lists: [
        {
            key: "products", label: "Products", nounOne: "product", noun: "products",
            titleOf: (r) => [r.brandName, r.productName].filter(Boolean).join(": "),
            noteOf: (r) => {
                const scope = productScope(r.cosmeticType || "", r.productName || "");
                if (scope === "out" || scope === "skincare") return "Not make-up: will not be saved";
                const brand = everyBrand.find((b) => [b.name, ...b.aliases].some((n) => sameKey(n) === sameKey(r.brandName || "")));
                if (brand && everyProduct.some((p) => p.brandId === brand.id && [p.name, ...p.aliases].some((n) => compactName(n) === compactName(r.productName || "")))) return "Already listed";
                return "";
            },
            fields: [
                { key: "brandName", label: "Brand" }, { key: "productName", label: "Product" }, { key: "cosmeticType", label: "Cosmetic type" },
                { key: "ingredients", label: "Ingredients (one per line, in printed order)", kind: "lines", wide: true },
                { key: "shades", label: "Shade colours (#RRGGBB, one per line)", kind: "lines" },
                { key: "description", label: "Description", kind: "area" }
            ]
        },
        {
            key: "brands", label: "Brands", nounOne: "brand", noun: "brands",
            titleOf: (r) => r.name,
            noteOf: (r) => (everyBrand.some((b) => [b.name, ...b.aliases].some((n) => sameKey(n) === sameKey(r.name || ""))) ? "Already listed" : ""),
            fields: [
                { key: "name", label: "Brand" },
                { key: "market", label: "Market", kind: "select", options: [["", "Not given"], ["Global", "Global"], ["Local", "Local"], ["Asia", "Asia"], ["Europe", "Europe"], ["North America", "North America"]] },
                { key: "aliases", label: "Other names (one per line)", kind: "lines" }
            ]
        }
    ]
};

/** What a file's text holds, for the check window: { draft: { products, brands }, notes, model, refused }. Nothing is saved here. */
async function extractInventory(documentText, say, signal) {
    // a file in the GlowUp import format is read as it is, with no AI; any other document is read by the AI
    const keyed = parseImportText(documentText);
    if (keyed.kind === "ingredients") return { refused: "This file lists only ingredients. " + ingredientsNotHere(keyed.draft.ingredients.length) };
    if (keyed.kind === "mixed") return { refused: `This file mixes ${many(keyed.counts.inventory, "brand or product record", "brand or product records")} with ingredient records, so nothing was imported. ` + ingredientsNotHere(keyed.counts.ingredients) };
    if (keyed.kind === "inventory") { say("Reading the file's own fields. No AI is used."); return { draft: keyed.draft, notes: keyed.notes.slice(0, 6), model: "" }; }
    // a document that labels each product ("Brand:", "Product:", "Ingredients:") is read here too, with no AI and no quota
    const labeled = parseLabeledProducts(documentText);
    if (labeled.kind === "inventory") { say("Reading the labelled Brand / Product / Ingredients lines. No AI is used."); return { draft: labeled.draft, notes: labeled.notes.slice(0, 6), model: "" }; }
    // no size limit: the AI reads the file in parts, one after the other; a part that fails is named, the others still count
    const parts = splitForAi(documentText);
    const found = { products: [], brands: [] }, failedParts = [];
    let model = "", ingredientRecords = 0;
    for (let i = 0; i < parts.length; i++) {
        if (signal && signal.aborted) break;
        say(parts.length > 1 ? `Asking the AI about part ${i + 1} of ${parts.length}. The file itself is not uploaded.` : "Asking the AI to pick out brands and products. The file itself is not uploaded.");
        try {
            const answer = await askInventoryAi({ mode: "import", documentText: parts[i] }, i === 0, signal);
            found.products.push(...((answer.draft && answer.draft.products) || []));
            found.brands.push(...((answer.draft && answer.draft.brands) || []));
            ingredientRecords += Number(answer.ingredientRecords) || 0;
            model = model || answer.model || "";
        } catch (error) {
            if (i === 0 || (signal && signal.aborted)) throw error;
            failedParts.push(`part ${i + 1} (${error && error.name === "AbortError" ? "the AI took too long" : (error && error.message) || "it failed"})`);
        }
    }
    const notes = (failedParts.length ? [`Not read: ${failedParts.join("; ")}. Import the file again later for those parts; what is saved now is then skipped as a duplicate.`] : [])
        .concat(ingredientRecords > 0 ? [ingredientsNotHere(ingredientRecords)] : []);
    return { draft: found, notes, model: model || "the AI" };
}

/** Writes the checked records (NEW entries only, all Pending) in groups; answers what was saved. */
async function saveInventoryDraft(draft, fileName, say) {
    const records = Array.isArray(draft.products) ? draft.products : [], brandRecords = Array.isArray(draft.brands) ? draft.brands : [];
    // no limit on records: saved in groups of IMPORT_GROUP, one database batch each, remembering what earlier groups added
    const memory = { newBrands: new Map(), plannedProducts: new Set() };
    const result = { savedProducts: 0, savedBrands: 0, suggestedLists: 0, skippedDuplicates: 0, skippedInvalid: 0, withFdaLink: 0, flagged: [] };
    const groups = Math.max(Math.ceil(records.length / IMPORT_GROUP), Math.ceil(brandRecords.length / IMPORT_GROUP), 1);
    for (let g = 0; g < groups; g++) {
        if (groups > 1) say(`Saving group ${g + 1} of ${groups}...`);
        const part = await saveImportedProducts(records.slice(g * IMPORT_GROUP, (g + 1) * IMPORT_GROUP), brandRecords.slice(g * IMPORT_GROUP, (g + 1) * IMPORT_GROUP), fileName, memory);
        ["savedProducts", "savedBrands", "suggestedLists", "skippedDuplicates", "skippedInvalid", "withFdaLink"].forEach((k) => { result[k] += part[k]; });
        result.flagged.push(...part.flagged);
    }
    showImportFlags(el("inventoryImportFlags"), result.flagged);
    const flaggedOut = result.flagged.filter((f) => !f.saved).length;
    const skipped = result.skippedDuplicates + result.skippedInvalid;
    if (result.savedProducts === 0 && result.savedBrands === 0 && result.suggestedLists === 0) {
        const nothing = `Nothing new was saved: ${result.skippedDuplicates} already listed, ${flaggedOut} not make-up (see below), ${result.skippedInvalid} incomplete.`;
        notify(nothing, "warn");
        return nothing;
    }
    const summary = `Saved ${many(result.savedProducts, "product", "products")} as Pending for review`
        + (result.savedBrands ? ` and ${many(result.savedBrands, "new brand", "new brands")}` : "")
        + (result.suggestedLists ? `; ${many(result.suggestedLists, "ingredient list was", "ingredient lists were")} sent to the review queue for products that had none` : "")
        + (flaggedOut ? `; ${flaggedOut} not make-up and NOT saved (see below)` : "")
        + (skipped ? `; skipped ${skipped} already listed or incomplete` : "")
        + `. ${result.withFdaLink} with an FDA Philippines source link.`;
    notify(summary, flaggedOut ? "warn" : "ok");
    await loadInventory();
    return summary;
}

/**
 * The records an import FLAGGED, under the drop box: skin care and other non-make-up records (not saved: GlowUp lists
 * make-up only) and make-up whose kind the rule cannot tell (saved as Pending; the app hides it until its kind is set).
 */
function showImportFlags(box, flagged) {
    if (!box) return;
    box.hidden = flagged.length === 0;
    if (flagged.length === 0) { box.innerHTML = ""; return; }
    const out = flagged.filter((f) => !f.saved), unclear = flagged.filter((f) => f.saved);
    const line = (f) => `<li><b>${escapeHtml(f.name)}</b>${f.brandName ? " (" + escapeHtml(f.brandName) + ")" : ""}${f.cosmeticType ? ", " + escapeHtml(f.cosmeticType) : ""}: ${escapeHtml(f.why)}</li>`;
    box.innerHTML = (out.length ? `<span class="chip warn">Flagged: skin care or not make-up</span> Not saved. GlowUp lists make-up only.<ul>${out.map(line).join("")}</ul>` : "")
        + (unclear.length ? `<span class="chip review">Flagged: kind not clear</span> Saved as Pending; open each one and choose its make-up kind, or move it to the Recycle bin.<ul>${unclear.map(line).join("")}</ul>` : "");
}

/**
 * Writes what an import found. NEW entries only, all Pending and not reviewed; an existing entry is never changed. The
 * one exception goes through the existing review queue: an existing product WITHOUT an ingredient list gets the imported
 * list as a suggestion (productIngredientSuggestions), which an admin applies or dismisses.
 */
async function saveImportedProducts(records, brandRecords, fileName, memory) {
    const batch = writeBatch(db);
    const kept = memory || { newBrands: new Map(), plannedProducts: new Set() };
    const newBrands = new Map();                    // made by THIS group (written in its batch)
    const plannedProducts = kept.plannedProducts;
    const flagged = [];
    const uid = auth.currentUser ? auth.currentUser.uid : "";
    let savedProducts = 0, skippedDuplicates = 0, skippedInvalid = 0, withFdaLink = 0, suggestedLists = 0, writes = 0;
    const clean = (value, most) => String(value || "").replace(/\s+/g, " ").trim().slice(0, most);
    const names = (list, most, count) => Array.from(new Map((Array.isArray(list) ? list : []).map((v) => clean(v, most)).filter(Boolean).map((v) => [sameKey(v), v])).values()).slice(0, count);
    const knownBrand = (name) => everyBrand.find((item) => [item.name, ...item.aliases].some((known) => sameKey(known) === sameKey(name)));

    /** A new Pending brand from what the document says about it (its website only as a SUGGESTION). */
    const addBrand = (name, info) => {
        const ref = doc(collection(db, "brands"));
        const brand = { id: ref.id, name, ref };
        newBrands.set(sameKey(name), brand);
        kept.newBrands.set(sameKey(name), brand);
        const data = {
            name, category: "Makeup", market: clean(info.market, 40) || "Global", status: "Pending", scans: 0,
            aliases: names(info.aliases, 60, 6).filter((alias) => sameKey(alias) !== sameKey(name)),
            source: "document-import", reviewed: false, createdAt: serverTimestamp(), updatedAt: serverTimestamp()
        };
        const logoUrl = cleanLink(info.logoUrl), imageUrl = cleanLink(info.imageUrl), suggested = cleanLink(info.websiteSuggested);
        if (logoUrl) data.logoUrl = logoUrl;
        if (imageUrl) data.imageUrl = imageUrl;
        if (suggested && plainLink(suggested)) data.websiteSuggested = suggested;
        batch.set(ref, data);
        writes++;
        return brand;
    };

    records.slice(0, IMPORT_GROUP).forEach((record) => {
        const brandName = clean(record.brandName, 60);
        const name = clean(record.productName, 100);
        const cosmeticType = clean(record.cosmeticType, 60);
        if (brandName.length < 2 || !name) { skippedInvalid++; return; }
        const scope = productScope(cosmeticType, name);
        // skin care and other non-make-up records are FLAGGED and not saved (make-up only)
        if (scope === "out" || scope === "skincare") {
            flagged.push({ name, brandName, cosmeticType, saved: false, why: scope === "skincare" ? `skin care (${scopeWords(cosmeticType, name)})` : `not make-up (${scopeWords(cosmeticType, name)})` });
            return;
        }

        let brand = knownBrand(brandName);
        if (brand && brand.binned) { skippedDuplicates++; return; }
        if (!brand) brand = kept.newBrands.get(sameKey(brandName)) || addBrand(brandName, { market: record.brandMarket, aliases: record.brandAliases, logoUrl: record.brandLogoUrl, imageUrl: record.brandImageUrl, websiteSuggested: record.brandWebsiteSuggested });

        const ingredients = (Array.isArray(record.ingredients) ? record.ingredients : []).map((item) => String(item).trim().slice(0, 100)).filter(Boolean).slice(0, 80);
        const sources = Array.from(new Set((Array.isArray(record.sources) ? record.sources : []).filter((source) => /^https:\/\//i.test(String(source))))).slice(0, 10);
        if (record.fdaVerifiedLink && /^https:\/\/([a-z0-9-]+\.)*fda\.gov\.ph\//i.test(record.fdaVerifiedLink)) {
            const link = `FDA Philippines verification | ${record.fdaVerifiedLink}`;
            if (!sources.includes(link) && sources.length < 10) sources.unshift(link);
        }

        const key = brand.id + "|" + compactName(name);
        const existing = everyProduct.find((item) => item.brandId === brand.id && [item.name, ...item.aliases].some((known) => compactName(known) === compactName(name)));
        if (plannedProducts.has(key) || existing) {
            // an existing product with NO ingredient list: the imported list goes to the review queue (once)
            const queued = existing && ingredientSuggestions.some((s) => s.productId === existing.id);
            if (existing && !existing.binned && existing.ingredients.length === 0 && ingredients.length >= 3 && !queued) {
                batch.set(doc(collection(db, "productIngredientSuggestions")), {
                    productId: existing.id, productName: existing.name, brandName: brand.name, ingredients,
                    sources: sources.length ? sources : [`Imported document: ${clean(fileName, 80)}`],
                    source: "document-import", status: "Pending", reviewed: false, addedBy: uid, addedAtUnix: Math.floor(Date.now() / 1000)
                });
                writes++;
                suggestedLists++;
            } else skippedDuplicates++;
            return;
        }
        plannedProducts.add(key);
        if (scope === "unclear") flagged.push({ name, brandName, cosmeticType, saved: true, why: scopeWords(cosmeticType, name) || "its make-up kind is not clear" });
        if (sources.some((s) => /fda\.gov\.ph/i.test(s))) withFdaLink++;
        const shades = Array.from(new Set((Array.isArray(record.shades) ? record.shades : []).map(cleanShade).filter((shade) => shade !== null))).slice(0, MAX_SHADES);
        const data = {
            name, brandId: brand.id, brandName: brand.name, cosmeticType, description: String(record.description || "").slice(0, 400),
            aliases: names(record.productAliases, 100, 6).filter((alias) => compactName(alias) !== compactName(name)),
            ingredients, shades, sources, minAge: Number.isInteger(record.minAge) && record.minAge > 0 && record.minAge < 100 ? record.minAge : 0,
            fdaNumber: cleanFdaNumber(record.fdaNumber) || "",
            status: "Pending", scans: 0, folder: "",
            source: "document-import", reviewed: false, createdAt: serverTimestamp(), updatedAt: serverTimestamp()
        };
        const barcode = barcodeKey(record.barcode || "");
        if (barcode) data.barcode = barcode;
        const imageUrl = cleanLink(record.imageUrl);
        if (imageUrl) data.imageUrl = imageUrl;
        batch.set(doc(collection(db, "products")), data);
        writes++;
        savedProducts++;
    });

    // records about a brand only: a NEW brand at most (an existing brand is never changed by an import)
    (Array.isArray(brandRecords) ? brandRecords : []).slice(0, IMPORT_GROUP).forEach((record) => {
        const name = clean(record.name, 60);
        if (name.length < 2) { skippedInvalid++; return; }
        if (knownBrand(name) || kept.newBrands.has(sameKey(name))) { skippedDuplicates++; return; }
        addBrand(name, record);
    });

    const result = { savedProducts, savedBrands: newBrands.size, suggestedLists, skippedDuplicates, skippedInvalid, withFdaLink, flagged };
    if (writes === 0) return Object.assign(result, { savedBrands: 0 });
    await batch.commit();
    return result;
}

const productsOf = (brandId) => products.filter((p) => p.brandId === brandId);
/** Skin care, hair, body, nail, fragrance and the like: not what GlowUp lists. */
const isOut = (product) => notMakeup(product.cosmeticType, product.name);
/** The rule cannot tell that it is make-up (or its kind is not decided yet): listed, but the app hides it. */
const isUnclear = (product) => productScope(product.cosmeticType, product.name) === "unclear";
/** A brand that is not a make-up brand and has nothing but such products (or none at all). */
const brandOut = (brand) => categoryOutOfScope(brand.category) && productsOf(brand.id).every(isOut);
const hasBrand = (product) => brands.some((b) => b.id === product.brandId);
const brandOf = (product) => brands.find((b) => b.id === product.brandId) || null;
/** The products the normal list shows: make-up (and what may be make-up) that is not in the bin. */
const listed = () => products.filter((p) => !isOut(p));
const outsideCount = () => products.filter(isOut).length + brands.filter(brandOut).length;
/** Waiting for an admin: what the app or an import added, and products whose kind is not clear. */
const waits = (p) => p.needsReview || isUnclear(p);
const reviewCount = () => brands.filter((b) => b.needsReview && !brandOut(b)).length + listed().filter(waits).length;
/** The brand or product a key of the organiser ("b:id" / "p:id") stands for, also when it is in the bin. */
const itemOf = (key) => (key.startsWith("b:") ? everyBrand.find((b) => b.id === key.slice(2)) : everyProduct.find((p) => p.id === key.slice(2))) || null;

/** "#c24b62", "C24B62" -> "#C24B62"; anything that is not a colour -> null. */
/** "#C24B62", "c24b62", "#C46" or "rgb(194, 75, 98)" become "#C24B62"; anything else is null. */
function cleanShade(value) {
    let text = String(value || "").trim().toUpperCase();
    const rgb = text.match(/^RGB\(\s*(\d{1,3})\s*[, ]\s*(\d{1,3})\s*[, ]\s*(\d{1,3})\s*\)$/);
    if (rgb) return [1, 2, 3].every((i) => Number(rgb[i]) <= 255) ? "#" + [1, 2, 3].map((i) => Number(rgb[i]).toString(16).padStart(2, "0")).join("").toUpperCase() : null;
    // a SHADE NUMBER is never taken for a colour ("#128", "128"): the same rule as normalShade in the Assist with AI server
    if (text && text[0] !== "#") {
        if (!/^[0-9A-F]{6}$/.test(text) || !/[A-F]/.test(text)) return null;
        text = "#" + text;
    }
    if (/^#[0-9A-F]{3}$/.test(text)) {
        if (/^#[0-9]{3}$/.test(text)) return null;
        text = "#" + text[1] + text[1] + text[2] + text[2] + text[3] + text[3];
    }
    return /^#[0-9A-F]{6}$/.test(text) ? text : null;
}

/** The listed ingredients the Ingredient Curator has no entry for. */
function notCurated(ingredientNames) {
    return curatorKnown ? ingredientNames.filter((n) => !curatedKeys.has(sameKey(n))) : [];
}

/**
 * What is wrong with a product BESIDES its status, for the small dot in front of its name: { tone, text } or null.
 * The status has its own badge in the row, so "waiting for review" is not said a second time here; and where the
 * brand's own row stands above the product (`brandShown`), the brand's status is not repeated either.
 */
function attentionOf(p, brand, brandShown) {
    const notes = [];
    let tone = "";
    if (isOut(p)) { tone = "bad"; notes.push(`Not make-up: ${scopeWords(p.cosmeticType, p.name)}.`); }
    else if (isUnclear(p)) { tone = "warn"; notes.push(`The app hides it: ${scopeWords(p.cosmeticType, p.name)}. Edit it and choose its kind.`); }
    if (p.needsReview && p.status !== "Pending") { tone = tone || "info"; notes.push("Nobody has checked it yet."); }
    if (p.ingredients.length === 0) { tone = tone || "warn"; notes.push("No ingredient list: the app cannot check it."); }
    if (brand && brand.status !== "Active" && !brandShown) { tone = tone || "warn"; notes.push(`Its brand is ${brand.status}: the app hides it.`); }
    if (!brand) { tone = tone || "warn"; notes.push("Its brand is gone."); }
    return notes.length > 0 ? { tone, text: notes.join(" ") } : null;
}
/** How much a product needs attention (for "Needs attention first"). */
function weightOf(p) {
    return (isOut(p) ? 8 : 0) + (waits(p) ? 4 : 0) + (p.ingredients.length === 0 ? 2 : 0) + (brandOf(p) && brandOf(p).status === "Active" ? 0 : 1);
}

// ---------------------------------------------------------------------------------------------
// The counts, the toolbar and the table
// ---------------------------------------------------------------------------------------------
/** The brand chosen in the toolbar (or null), and the folder that is open (or ""): every panel narrows to them. */
const focusBrand = () => brands.find((b) => b.id === brandFilterValue()) || null;
const focusFolder = () => (org && org.view.kind === "folder" ? org.view.name : "");
/** True for a product inside what the page is narrowed to (the chosen brand and the open folder). */
const inFocus = (p) => { const b = focusBrand(), f = focusFolder(); return (!b || p.brandId === b.id) && (!f || sameKey(p.folder) === sameKey(f)); };

function paintStats() {
    const set = (id, value) => { const box = el(id); box.classList.remove("skeleton"); box.textContent = value; };
    const brand = focusBrand(), folder = focusFolder();
    const mine = listed().filter(inFocus);
    // the count tiles follow the brand that is chosen and the folder that is open
    set("statBrands", brand ? 1 : folder ? new Set(mine.map((p) => p.brandId)).size : brands.filter((b) => !brandOut(b)).length);
    set("statProducts", mine.length);
    set("statReview", brand || folder ? mine.filter(waits).length + (brand && brand.needsReview ? 1 : 0) : reviewCount());
    set("statOut", brand || folder ? products.filter((p) => isOut(p) && inFocus(p)).length : outsideCount());
    const where = brand ? brand.name : folder ? `"${folder}"` : "";
    [["statBrands", "Brands"], ["statProducts", "Products"], ["statReview", "Waiting for review"], ["statOut", "Not make-up"]].forEach(([id, title]) => {
        const label = el(id).parentElement.querySelector(".stat-title");
        if (label) label.textContent = where ? `${title} · ${where}` : title;
    });
    const show = org.view.kind === "bin" ? "" : el("reviewFilterSelect").value;
    document.querySelectorAll(".stat[data-show]").forEach((tile) => tile.classList.toggle("on", show !== "" && tile.dataset.show === show));
    document.querySelectorAll(".stat[data-drill]").forEach((tile) => tile.classList.toggle("on", !!drill && org.view.kind !== "bin" && (tile.dataset.drill === drill.kind || (tile.dataset.drill === "brands" && drill.kind === "brand"))));
}

function paintToolbar() {
    const inBin = org.view.kind === "bin";
    el("sortNow").textContent = sortNow().short;
    // the dropdown always shows how the list is drawn (a search or a count tile sets it back to "every brand")
    const select = el("brandFilterSelect");
    if (select.value === "" && view.mode === "all") select.value = FLAT_LIST;
    if (select.value === FLAT_LIST && view.mode !== "all") select.value = "";
    // the brand chosen in Filters ("Show") is said on a chip, because the dropdown that showed it is hidden now
    const brandChosen = brandFilterValue() ? brands.find((b) => b.id === brandFilterValue()) : null;
    el("inventoryQuick").innerHTML = (brandChosen && !inBin ? `<button type="button" class="filter-chip" data-clear="brand" data-tip="Showing one brand only. Click to show every brand again.">${escapeHtml(brandChosen.name || "Unnamed Brand")}${icon("x")}</button>` : "")
        + (quick && !inBin ? `<button type="button" class="filter-chip" data-clear="quick" data-tip="Showing only these products. Click to show all again.">${escapeHtml(QUICK[quick].label)}${icon("x")}</button>` : "")
        + (facetCount() > 0 && !inBin ? `<button type="button" class="filter-chip" data-clear="facets" data-tip="Filters are on. Click to switch them off.">Filters (${facetCount()})${icon("x")}</button>` : "");
    if (!inBin && drill) {
        const brand = drill.kind === "brand" ? brands.find((b) => b.id === drill.id) : null;
        const label = drill.kind === "brand" ? "All brands" : drill.kind === "brands" ? "Brands" : "By type";
        el("inventoryQuick").insertAdjacentHTML("afterbegin", `<button type="button" class="filter-chip" data-clear="drill" data-tip="${drill.kind === "brand" ? "Back to the list of brands." : "Back to the whole inventory."}">${drill.kind === "brand" ? icon("left") : ""}${label}${drill.kind === "brand" ? "" : icon("x")}</button>`);
        if (brand) el("inventoryQuick").insertAdjacentHTML("beforeend", `<span class="drill-name">${escapeHtml(brand.name || "Unnamed Brand")}</span>`);
    }
    // a view has its own order (A to Z), so the Sort button has nothing to say there
    el("sortMenuBtn").hidden = !!drill && !inBin;
    el("filterNow").textContent = facetCount() > 0 ? " " + facetCount() : "";
    el("filterMenuBtn").classList.toggle("on", facetCount() > 0);
}

function renderInventory() {
    paintStats();
    paintLayout();
    paintToolbar();
    if (org.view.kind === "bin") renderBin(); else renderList(org.view);
    paintSearchStatus();
    syncSearchUrl();
    org.refresh();
    refreshAssist();
    // the data panels (lists, grids, graphs) follow the same brand and folder
    const focus = (focusBrand() ? focusBrand().id : "") + "|" + focusFolder();
    if (focus !== lastFocus) { lastFocus = focus; datasetChanged("products"); datasetChanged("brands"); }
}
let lastFocus = "|";

const pickBox = (label) => `<button type="button" class="pick" role="checkbox" aria-checked="false" aria-label="Select ${escapeHtml(label)}"></button>`;
const pickAllBox = (what) => `<button type="button" class="pick" data-pick-all role="checkbox" aria-checked="false" aria-label="Select all shown" data-tip="Select every ${what} shown here."></button>`;

// ----- pending snapshot reviews -----
/** The badge on the Pending Snapshot Reviews button. */
function paintSnapshotCount(failed) {
    const count = el("snapshotCount"), hint = el("snapshotReviewHint");
    count.classList.remove("skeleton");
    count.textContent = failed ? "–" : String(snapshotsWaiting || 0);
    el("snapshotReviewBtn").classList.toggle("has-waiting", (snapshotsWaiting || 0) > 0);
    hint.textContent = failed ? "The count could not be read. Open it to look anyway."
        : snapshotsWaiting > 0 ? `${many(snapshotsWaiting, "snapshot waits", "snapshots wait")}. One at a time: the photo next to what the AI read.`
        : "Nothing is waiting. New photos from the app arrive here.";
}

/** The lists the review window works with: the ones this page already holds (no extra read). */
const snapshotContext = () => ({
    brands: everyBrand.map((b) => ({ id: b.id, name: b.name, aliases: b.aliases, binned: b.binned === true })),
    products: everyProduct.map((p) => ({ id: p.id, brandId: p.brandId, name: p.name, aliases: p.aliases, ingredients: p.ingredients, binned: p.binned === true })),
    library: curatedList
});
let snapshotInForm = null;            // { entry } while the Add Product form is open on a snapshot's values

/** Opens the review window over this page. */
function openSnapshots() {
    openSnapshotReview({
        context: snapshotContext,
        onOpenForm: openSnapshotInForm,
        onLoaded: (n) => { if (snapshotsWaiting === null || n > snapshotsWaiting) { snapshotsWaiting = n; paintSnapshotCount(); } },
        onProcessed: () => { snapshotsWaiting = Math.max(0, (snapshotsWaiting || 1) - 1); paintSnapshotCount(); },
        onClose: async (approved) => { if (approved > 0) await loadInventory(); }
    });
}

/**
 * "Open in Add Product form": the brand is added first when it is new (after a question), then the SAME form as for a product added by
 * hand opens with the snapshot's values in it. Saving the form saves the product and closes the snapshot (see saveProduct).
 */
async function openSnapshotInForm(entry, v) {
    const key = compactName(v.brand);
    let brand = key ? everyBrand.find((b) => !b.binned && [b.name, ...b.aliases].some((n) => compactName(n) === key)) : null;
    if (!brand) {
        if (!(await sure(`"${v.brand}" is not a brand in the inventory yet.\n\nAdd it as an Active brand and open the product form?`, "Add brand", false))) return;
        try {
            const created = await addDoc(collection(db, "brands"), {
                name: v.brand, category: "Makeup", market: "Global", status: "Active", scans: 0, source: "snapshot", reviewed: true,
                reviewedAt: serverTimestamp(), createdAt: serverTimestamp(), updatedAt: serverTimestamp()
            });
            await loadInventory();
            brand = everyBrand.find((b) => b.id === created.id) || null;
        } catch (error) {
            console.error("Error adding the brand of a snapshot: ", error);
            notify("The brand was not added. " + describeError(error), "error");
            return;
        }
        if (!brand) { notify("The brand was added, but the list did not show it yet. Reload the page and open the snapshot again.", "error"); return; }
    }
    if (brand.binned) { notify(`The brand "${brand.name}" is in the Recycle bin. Restore it first.`, "error"); return; }
    openProductForm(null, brand.id);
    el("productNameInput").value = v.name;
    const known = COSMETIC_TYPES.find((t) => sameKey(t) === sameKey(v.kind));
    el("productTypeInput").value = known ? known : OTHER_TYPE;
    el("productTypeOtherInput").value = known ? "" : v.kind;
    el("productDescInput").value = APPROVED_DESCRIPTION;
    el("productIngredientsInput").value = v.ingredients.join(", ");
    formShades = v.shade ? [v.shade] : [];
    showOtherTypeBox(); showIngredientSummary(); renderShadeEditor();
    const note = el("productAiNote");
    note.textContent = "From an app user's photo. Pending approval, unverified, AI-generated: compare every field with the packaging before you save.";
    note.hidden = false;
    snapshotInForm = { entry };
}

/** Under this width the list is drawn as cards whatever the switch says (the table needs about 640 px). */
const NARROW_LIST_PX = 600;
let listIsNarrow = false;

/** Rows or Cards on the inventory list (the switch in its title line). */
function paintLayout() {
    // the Recycle bin keeps its table: cards are for products
    el("inventoryHost").classList.toggle("as-cards", (view.layout === "cards" || listIsNarrow) && !(org && org.view.kind === "bin"));
    el("inventoryLayoutSwitch").querySelectorAll("[data-layout]").forEach((button) => {
        const on = button.dataset.layout === view.layout;
        button.classList.toggle("on", on);
        button.setAttribute("aria-pressed", String(on));
    });
}

function setPagers(state, total, noun) {
    renderPager(el("inventoryPager"), state, total, renderInventory, noun);
    renderPager(el("inventoryPagerTop"), state, total, renderInventory, noun);
}

// ---------------------------------------------------------------------------------------------
// Search (Gmail style). The box searches EVERY listed product, on every page of the list: name, other names, brand, type,
// barcode, description and ingredients; any letter case, parts of words. While you type (2+ letters) a drop-down offers Brands,
// Products and Ingredients (5 each). The words are kept in the address (?q=), so Back and a reload keep the search.
// ---------------------------------------------------------------------------------------------
const SUGGEST_MAX = 5;
let suggestItems = [];                      // the options in the drop-down: { kind, group, text, note?, brand?, product? }
let suggestAt = -1;                         // the highlighted option, -1 = none
let searchCount = null;                     // products found by the last search of the list (null = no search, or a view without a count)
let nextPush = false;                       // the next address change is a step Back can return to (Enter, a suggestion, clear)

/** The texts of a product the search looks in (ingredients are added by productMatches). */
const productTexts = (p) => [p.name, ...p.aliases, p.cosmeticType, p.description, p.barcode, (brandOf(p) || {}).name || p.brandName];

/** The ingredient that made a product match: the whole search first, then each word of it. "" when none. */
function ingredientFor(p, typed) {
    const list = p.ingredients || [];
    const whole = list.find((name) => matchesSearch(typed, name));
    if (whole) return whole;
    for (const word of typed.split(/\s+/).filter((w) => w.length > 1)) {
        const hit = list.find((name) => matchesSearch(word, name));
        if (hit) return hit;
    }
    return "";
}

/** "contains: niacinamide" on a row that was found only through its ingredients. */
function containsNote(p) {
    const typed = el("inventorySearchInput").value;
    if (!sameKey(typed) || matchesSearch(typed, productTexts(p))) return "";
    const found = ingredientFor(p, typed);
    return found ? `<span class="contains-note">contains: ${escapeHtml(found)}</span>` : "";
}

/** Keeps ?q= equal to the box. Typing replaces the address; Enter, a suggestion and clearing add a step for Back. */
function syncSearchUrl() {
    const url = new URL(window.location.href), typed = el("inventorySearchInput").value.trim();
    if (typed) url.searchParams.set("q", typed); else url.searchParams.delete("q");
    const push = nextPush;
    nextPush = false;
    if (url.href === window.location.href) return;
    try { window.history[push ? "pushState" : "replaceState"](null, "", url); } catch (error) { /* an address that cannot change: the search still works */ }
}

/** "12 results for "blush"" with an X that clears the search. Hidden when nothing is searched. */
function paintSearchStatus() {
    const status = el("inventorySearchStatus"), typed = el("inventorySearchInput").value.trim();
    const show = !!typed && searchCount !== null && org.view.kind !== "bin";
    status.hidden = !show;
    if (!show) return;
    el("inventorySearchStatusText").textContent = searchCount === 0 ? `No results for "${typed}"` : `${many(searchCount, "result", "results")} for "${typed}"`;
}

function closeSuggest() {
    const input = el("inventorySearchInput");
    el("inventorySuggest").hidden = true;
    suggestAt = -1;
    input.setAttribute("aria-expanded", "false");
    input.removeAttribute("aria-activedescendant");
}

function paintSuggestActive() {
    const input = el("inventorySearchInput");
    el("inventorySuggest").querySelectorAll("[role=option]").forEach((row, at) => {
        const on = at === suggestAt;
        row.classList.toggle("active", on);
        row.setAttribute("aria-selected", String(on));
        if (on) { input.setAttribute("aria-activedescendant", row.id); row.scrollIntoView({ block: "nearest" }); }
    });
    if (suggestAt < 0) input.removeAttribute("aria-activedescendant");
}

/** The drop-down for what is typed: up to 5 brands, 5 products, 5 ingredients; "No results" when there are none. */
function openSuggest() {
    const input = el("inventorySearchInput"), box = el("inventorySuggest"), typed = input.value.trim();
    if (sameKey(typed).length < 2) { closeSuggest(); return; }
    const listed = products.filter((p) => !isOut(p));
    const foundBrands = brands.filter((b) => matchesSearch(typed, [b.name, ...b.aliases])).slice(0, SUGGEST_MAX)
        .map((b) => ({ kind: "brand", group: "Brands", text: b.name, brand: b }));
    const foundProducts = listed.filter((p) => matchesSearch(typed, [p.name, ...p.aliases, p.cosmeticType, (brandOf(p) || {}).name || p.brandName])).slice(0, SUGGEST_MAX)
        .map((p) => ({ kind: "product", group: "Products", text: p.name || "Unnamed Product", note: (brandOf(p) || {}).name || p.brandName || "", product: p }));
    const byIngredient = new Map();         // key -> { text, count }
    listed.forEach((p) => (p.ingredients || []).forEach((name) => {
        if (!matchesSearch(typed, name)) return;
        const key = sameKey(name), row = byIngredient.get(key) || { text: name, count: 0 };
        row.count++;
        byIngredient.set(key, row);
    }));
    const foundIngredients = Array.from(byIngredient.values()).sort((a, b) => b.count - a.count || a.text.localeCompare(b.text)).slice(0, SUGGEST_MAX)
        .map((row) => ({ kind: "ingredient", group: "Ingredients", text: row.text, note: many(row.count, "product", "products") }));
    suggestItems = [...foundBrands, ...foundProducts, ...foundIngredients];
    suggestAt = -1;
    box.replaceChildren();
    if (suggestItems.length === 0) {
        const none = document.createElement("div");
        none.className = "sug-none";
        none.textContent = `No results for "${typed}"`;
        box.appendChild(none);
    } else {
        let group = "";
        suggestItems.forEach((item, at) => {
            if (item.group !== group) {
                group = item.group;
                const head = document.createElement("div");
                head.className = "sug-head";
                head.textContent = group;
                box.appendChild(head);
            }
            const row = document.createElement("div");
            row.className = "sug-item";
            row.id = "invSug" + at;
            row.setAttribute("role", "option");
            row.setAttribute("aria-selected", "false");
            const name = document.createElement("span");
            name.className = "sug-text";
            name.textContent = item.text;
            row.appendChild(name);
            if (item.note) { const note = document.createElement("span"); note.className = "sug-note"; note.textContent = item.note; row.appendChild(note); }
            row.addEventListener("mousedown", (event) => event.preventDefault());       // the box keeps the focus
            row.addEventListener("click", () => chooseSuggestion(at));
            box.appendChild(row);
        });
    }
    box.hidden = false;
    input.setAttribute("aria-expanded", "true");
}

/** A brand shows its products, an ingredient the products that contain it, a product opens (all by searching for it). */
function chooseSuggestion(at) {
    const item = suggestItems[at];
    if (!item) return;
    el("inventorySearchInput").value = item.text;
    el("inventorySearchClear").hidden = false;
    closeSuggest();
    nextPush = true;
    drill = null;
    fromFirstPage();
    if (item.kind === "product" && item.product && openId !== item.product.id) toggleDetails(item.product.id);
}

function wireSearchExtras() {
    const input = el("inventorySearchInput"), box = el("inventorySuggest");
    input.setAttribute("role", "combobox");
    input.setAttribute("aria-autocomplete", "list");
    input.setAttribute("aria-expanded", "false");
    input.setAttribute("aria-controls", "inventorySuggest");
    // these run BEFORE the box's own Enter / Esc handling (wireSearch), so they are added first
    input.addEventListener("input", openSuggest);
    input.addEventListener("keydown", (event) => {
        const open = !box.hidden, count = suggestItems.length;
        if (event.key === "ArrowDown" || event.key === "ArrowUp") {
            event.preventDefault();
            if (box.hidden) openSuggest();
            if (suggestItems.length === 0) return;
            suggestAt = event.key === "ArrowDown" ? (suggestAt + 1) % suggestItems.length : (suggestAt <= 0 ? suggestItems.length - 1 : suggestAt - 1);
            paintSuggestActive();
        } else if (event.key === "Enter") {
            if (open && suggestAt >= 0 && suggestAt < count) { event.preventDefault(); event.stopImmediatePropagation(); chooseSuggestion(suggestAt); return; }
            closeSuggest();
            nextPush = true;
        } else if (event.key === "Escape") {
            if (open) { event.stopImmediatePropagation(); closeSuggest(); }          // the first Esc closes the drop-down, the next one clears the box
            else if (input.value) nextPush = true;
        }
    });
    input.addEventListener("blur", closeSuggest);
    el("inventorySearchClear").addEventListener("click", () => { nextPush = true; closeSuggest(); });
    el("inventorySearchStatusClear").addEventListener("click", () => {
        input.value = "";
        el("inventorySearchClear").hidden = true;
        nextPush = true;
        closeSuggest();
        fromFirstPage();
        input.focus();
    });
    // "/" anywhere on the page jumps to the box, unless you are already typing in a field or a dialog is open
    document.addEventListener("keydown", (event) => {
        if (event.key !== "/" || event.ctrlKey || event.metaKey || event.altKey || event.defaultPrevented) return;
        const target = event.target;
        if (target && target.closest && target.closest("input, textarea, select, [contenteditable]:not([contenteditable=false])")) return;
        if (dialogIsOpen()) return;
        event.preventDefault();
        input.focus();
        input.select();
    });
    // Back / Forward: the box follows the address
    window.addEventListener("popstate", () => {
        input.value = new URLSearchParams(window.location.search).get("q") || "";
        el("inventorySearchClear").hidden = input.value.length === 0;
        closeSuggest();
        drill = null;
        fromFirstPage();
    });
}

function renderList(now) {
    searchCount = null;
    const body = el("inventoryTableBody");
    const typed = el("inventorySearchInput").value;
    const term = sameKey(typed);                            // "" when nothing is searched for
    const brandFilter = brandFilterValue();
    const show = el("reviewFilterSelect").value;            // "" | "review" | "out"
    const folder = now.kind === "folder" ? now.name : "";
    const flat = view.mode === "all";
    const columns = flat ? 7 : 6;
    el("inventoryTableHead").innerHTML = `<tr><th><span class="th-line">${pickAllBox("product")}Product</span></th>${flat ? "<th>Brand</th>" : ""}<th>Type</th><th>Shades</th><th class="text-right" data-tip="Scans the app recognised as this product.">Scans</th><th><span class="th-line">Status${statusGuide()}</span></th><th></th></tr>`;

    if (everyBrand.length === 0 && everyProduct.length === 0) {
        shownGroups = [];
        body.innerHTML = `<tr><td colspan="${columns}" class="table-message">No brands yet. Add the first brand, then its products.</td></tr>`;
        el("inventoryCountLine").textContent = "";
        setPagers(pageState, 0, "brands");
        return;
    }

    const base = (p) => (show === "out" ? isOut(p) : !isOut(p)) && (!folder || sameKey(p.folder) === sameKey(folder))
        && (show !== "review" || waits(p)) && (!quick || QUICK[quick].test(p)) && facetOk(p);
    // Search: any letter case, with or without accents and punctuation, parts of words, several words in any order.
    // A product is also found by its other names, its kind, its barcode, its brand ("maybelline lipstick") and its ingredients.
    const productMatches = (p) => matchesSearch(typed, [...productTexts(p), ...(p.ingredients || [])]);
    const brandMatches = (b) => !!b && matchesSearch(typed, [b.name, ...b.aliases, b.category, b.market]);
    paintBrandPageSize();
    if (drill && now.kind === "all") { renderDrill({ base, productMatches, brandMatches, term, typed }); return; }
    const order = sortNow().cmp;
    const html = [];
    let productsShown = 0;

    if (flat) {
        shownGroups = [];
        const list = products.filter((p) => base(p) && (!brandFilter || p.brandId === brandFilter) && (!term || productMatches(p))).sort(order);
        productsShown = list.length;
        searchCount = term ? productsShown : null;
        paginate(list, flatPage).forEach((p) => html.push(productRow(p, brandOf(p), true)));
        el("inventoryCountLine").textContent = list.length === 0 ? "" : many(list.length, "product", "products") + (folder ? ` in "${folder}"` : "");
        setPagers(flatPage, list.length, "products");
    } else {
        const groups = [];                                  // one per brand: { id, head, rows[] }
        sortedBrands().forEach((brand) => {
            if (brandFilter && brand.id !== brandFilter) return;
            const out = brandOut(brand);
            if (show === "out" ? false : out) return;                                  // such a brand only shows under "Not make-up or face"
            const mine = productsOf(brand.id);
            const hit = !term || brandMatches(brand);
            const rows = mine.filter((p) => base(p) && (hit || productMatches(p))).sort(order);
            // does the brand itself belong in this view, also without a product to show?
            const belongs = show === "out" ? out : show === "review" ? brand.needsReview : !folder && !quick;
            if (rows.length === 0 && !(belongs && hit)) return;
            const lines = rows.map((p) => productRow(p, brand, false));
            if (mine.length === 0) lines.push(`<tr class="note-row" data-brand="${escapeHtml(brand.id)}"><td colspan="${columns}" class="muted small">No products yet. Point at the brand and press +.</td></tr>`);
            productsShown += rows.length;
            groups.push({ id: brand.id, head: brandRow(brand, mine.length, columns), rows: lines });
        });

        // products that point to a brand that was deleted outside this page
        const orphans = products.filter((p) => !hasBrand(p) && base(p) && (!term || productMatches(p))).sort(order);
        if (orphans.length > 0 && !brandFilter) {
            productsShown += orphans.length;
            groups.push({
                id: NO_BRAND,
                head: `<tr class="brand-row orphan-row" data-brand="${NO_BRAND}"><td colspan="${columns}"><div class="brand-line"><button type="button" class="brand-toggle" data-act="toggle" data-brand="${NO_BRAND}" aria-label="Open or close">${icon("down")}</button><span class="brand-title">Products without a brand</span><span class="brand-about">Their brand is gone. Edit each one and choose a brand.</span><span class="spacer"></span><span class="brand-meta">${many(orphans.length, "product", "products")}</span></div></td></tr>`,
                rows: orphans.map((p) => productRow(p, null, false))
            });
        }

        shownGroups = groups;
        searchCount = term ? productsShown : null;
        paginate(groups, pageState).forEach((group) => {
            const folded = !term && closed.has(group.id);           // a search always shows what it found
            html.push(folded ? group.head.replace('class="brand-row', 'class="brand-row closed') : group.head);
            if (folded) return;
            const limit = moreShown.get(group.id) || PRODUCTS_AT_A_TIME;
            group.rows.slice(0, limit).forEach((row) => html.push(row));        // one entry per product (its open details come with it)
            const left = group.rows.length - limit;
            if (left > 0) html.push(`<tr class="more-row"><td colspan="${columns}"><button type="button" class="row-btn" data-act="more" data-brand="${escapeHtml(group.id)}">${icon("down")}Show ${Math.min(PRODUCTS_AT_A_TIME, left)} more (${left} left)</button></td></tr>`);
        });
        el("inventoryCountLine").textContent = groups.length === 0 ? "" : `${many(groups.length, "brand", "brands")} · ${many(productsShown, "product", "products")}${folder ? ` in "${folder}"` : ""}`;
        setPagers(pageState, groups.length, "brands");
    }

    const nothing = term ? `No results for "${escapeHtml(typed)}".`
        : quick ? "No product like that. Click the chip above to show all again."
        : folder ? `"${escapeHtml(folder)}" is empty. Drag products onto its chip above.`
        : show === "review" ? "Nothing is waiting for review."
        : show === "out" ? "Every listed product is make-up."
        : "Nothing to show.";
    body.innerHTML = html.length > 0 ? html.join("") : `<tr><td colspan="${columns}" class="table-message">${nothing}</td></tr>`;
}

/** The size box beside the pager: only for one brand with more than 10 products. It keeps its value when it is hidden. */
function paintBrandPageSize() {
    const select = el("brandPageSize");
    const count = drill && drill.kind === "brand" && org.view.kind !== "bin" ? productsOf(drill.id).filter((p) => !isOut(p)).length : 0;
    select.hidden = !brandPageOf(new Array(count).fill({}), 1, brandPage.size).showSizes;
    select.value = String(brandPage.size);
}

/** The views the two count tiles open. Search and Filters still narrow them; "base" is the same test as in the normal list. */
function renderDrill({ base, productMatches, brandMatches, term, typed }) {
    const body = el("inventoryTableBody"), head = el("inventoryTableHead");
    shownGroups = [];
    const nothing = (columns, text) => { body.innerHTML = `<tr><td colspan="${columns}" class="table-message">${text}</td></tr>`; };
    if (drill.kind === "brands") {
        head.innerHTML = `<tr><th colspan="6"><span class="th-line">${pickAllBox("brand")}Brand</span></th></tr>`;
        const list = brands.filter((b) => !brandOut(b) && (!term || brandMatches(b))).sort(byName);
        el("inventoryCountLine").textContent = many(list.length, "brand", "brands");
        setPagers(pageState, list.length, "brands");
        if (list.length === 0) { nothing(6, term ? `No brand matches "${escapeHtml(typed)}".` : "No brands yet."); return; }
        body.innerHTML = paginate(list, pageState).map((b) => brandRow(b, productsOf(b.id).length, 6).replace('class="brand-row', 'class="brand-row closed')).join("");
        return;
    }
    if (drill.kind === "brand") {
        const brand = brands.find((b) => b.id === drill.id);
        if (!brand) { openDrill({ kind: "brands" }); return; }
        const whole = productsOf(brand.id).length;
        head.innerHTML = `<tr><th><span class="th-line"><button type="button" class="pick" data-pick-all role="checkbox" aria-checked="false" aria-label="Select all ${whole} products in this brand" data-tip="Select all ${whole} products of this brand, also those on other pages."></button>Product <small class="muted">select all in brand: ${whole}</small></span></th><th>Type</th><th>Shades</th><th class="text-right">Scans</th><th>Status</th><th></th></tr>`;
        const list = productsOf(brand.id).filter((p) => base(p) && (!term || productMatches(p)));
        const page = brandPageOf(list, brandPage.page, brandPage.size);
        brandPage.page = page.page;
        el("inventoryCountLine").textContent = many(list.length, "product", "products");
        setPagers(brandPage, page.total, "products");
        const heading = brandRow(brand, whole, 6).replace('class="brand-row', 'class="brand-row drill-head');
        const rows = page.rows.map((p) => productRow(p, brand, false));
        body.innerHTML = heading + (rows.join("") || `<tr class="note-row"><td colspan="6" class="table-message">${term ? `Nothing in this brand matches "${escapeHtml(typed)}".` : "This brand has no products to show here."}</td></tr>`);
        return;
    }
    head.innerHTML = `<tr><th><span class="th-line">${pickAllBox("product")}Product</span></th><th>Brand</th><th>Type</th><th>Shades</th><th class="text-right">Scans</th><th>Status</th><th></th></tr>`;
    const groups = groupByType(products.filter((p) => base(p) && (!term || productMatches(p))));
    const total = groups.reduce((sum, group) => sum + group.rows.length, 0);
    el("inventoryCountLine").textContent = many(total, "product", "products");
    setPagers(flatPage, 0, "products");                     // this view is one long list: no pager
    if (total === 0) { nothing(7, term ? `Nothing here matches "${escapeHtml(typed)}".` : "Nothing to show."); return; }
    body.innerHTML = groups.map((group) => `<tr class="type-row"><td colspan="7"><span class="type-name">${escapeHtml(group.label)}</span> <span class="muted small">${many(group.rows.length, "product", "products")}</span></td></tr>`
        + group.rows.map((p) => productRow(p, brandOf(p), true)).join("")).join("");
}

/** The brands in the order the Sort button asks for (the orders that only fit products keep the brands A to Z). */
function sortedBrands() {
    const list = brands.slice();
    const needs = (b) => (b.needsReview ? 2 : 0) + (productsOf(b.id).some((p) => weightOf(p) > 0) ? 1 : 0);
    if (view.sort === "nameDesc") list.sort((a, b) => byName(b, a));
    else if (view.sort === "scans") list.sort((a, b) => b.scans - a.scans || byName(a, b));
    else if (view.sort === "new") list.sort((a, b) => b.addedAt - a.addedAt || byName(a, b));
    else if (view.sort === "attention") list.sort((a, b) => needs(b) - needs(a) || byName(a, b));
    return list;
}

/** A small button with only an icon; pointing at it says what it does. */
const actionButton = (classes, act, id, iconHtml, title, tip, preview) =>
    `<button type="button" class="icon-btn small ${classes}" data-act="${act}" data-id="${escapeHtml(id)}" aria-label="${escapeHtml(title)}" data-tip-title="${escapeHtml(title)}" data-tip="${escapeHtml(tip)}"${preview ? ` data-tip-preview="${escapeHtml(preview)}"` : ""}>${iconHtml}</button>`;
const rowActions = (buttons) => `<div class="btn-group row-actions">${buttons.join("")}</div>`;
const toneDot = (note) => (note ? `<span class="tone-dot tone-${note.tone}" role="img" aria-label="${escapeHtml(note.text)}" data-tip="${escapeHtml(note.text)}"></span>` : "");

function brandRow(brand, productCount, columns) {
    const about = [brand.category, brand.market].filter((t) => t).map(escapeHtml).join(" · ");
    const out = brandOut(brand);
    const buttons = [];
    if (brand.status === "Pending" && !out) buttons.push(`<button type="button" class="row-btn approve" data-act="approve-brand" data-id="${escapeHtml(brand.id)}" data-tip-title="Approve brand" data-tip="You have checked this brand: set it to Active. Its products are approved separately.">${icon("check")}Approve brand</button>`);
    buttons.push(actionButton("", "add-product", brand.id, icon("plus"), "Add product", "Add a product to this brand.", "Opens the product form with this brand chosen."));
    buttons.push(actionButton("", "edit-brand", brand.id, icon("edit"), "Edit brand", "Change the name, category, market or status.", "Opens the brand form."));
    buttons.push(actionButton("row-btn danger", "bin-brand", brand.id, BIN_ICON, "Recycle bin", "Remove this brand together with its products.", "They go to the Recycle bin and can be restored."));
    const fdaBrand = brand.source === "fdaph" || productsOf(brand.id).some(QUICK.fda.test);
    const flags = (fdaBrand ? `<span class="chip good" data-tip="Products of this brand were checked on the FDA Philippines list of notified cosmetics.">FDA Philippines</span>` : "") + (brand.needsReview && brand.status !== "Pending" ? `<span class="chip review" data-tip="${escapeHtml(originLabel(brand.source) || "Added automatically")}. Nobody has checked it yet.">needs review</span>` : "")
        + (out ? `<span class="chip bad">not a make-up brand</span>` : "");
    return `
        <tr class="brand-row${out ? " out-row" : ""}" data-brand="${escapeHtml(brand.id)}" data-key="b:${escapeHtml(brand.id)}" data-drag>
            <td colspan="${columns}"><div class="brand-line">
                <button type="button" class="brand-toggle" data-act="toggle" data-brand="${escapeHtml(brand.id)}" aria-label="Open or close this brand">${icon("down")}</button>
                ${pickBox(brand.name || "brand")}<span class="brand-title">${escapeHtml(brand.name || "Unnamed Brand")}</span>
                ${about ? `<span class="brand-about">${about}</span>` : ""}${flags}
                <span class="spacer"></span>
                ${rowActions(buttons)}
                <span class="brand-meta">${many(productCount, "product", "products")} · ${many(brand.scans, "scan", "scans")}</span>
                ${statusBadge(brand.status)}
            </div></td>
        </tr>`;
}

function productRow(p, brand, flat) {
    const open = p.id === openId;
    const shadeCell = p.shades.length === 0 ? DASH
        : p.shades.slice(0, 5).map((s, at) => `<span class="swatch" style="background:${escapeHtml(s)}" title="${escapeHtml(((p.shadeCodes || [])[at] ? (p.shadeCodes[at] + " · ") : "") + s)}"></span>`).join("") + (p.shades.length > 5 ? `<span class="muted small">+${p.shades.length - 5}</span>` : "");
    const buttons = [];
    const together = approvalFor(p, brand);
    if (together) buttons.push(actionButton("row-btn approve", "approve-with-brand", p.id, icon("check"), together.label, together.tip, "It asks first. Both are saved together or not at all."));
    else if (p.needsReview && !isOut(p) && !isUnclear(p)) buttons.push(actionButton("row-btn approve", "approve-product", p.id, icon("check"), "Approve", "You have checked this product: set it to Active.", "It asks first. The app then uses it."));
    buttons.push(actionButton("row-btn danger", "bin-product", p.id, BIN_ICON, "Recycle bin", "Remove this product.", "It goes to the Recycle bin and can be restored."));

    return `
        <tr class="product-row${isOut(p) ? " out-row" : ""}${open ? " open" : ""}" data-product="${escapeHtml(p.id)}" data-key="p:${escapeHtml(p.id)}" data-drag tabindex="0" aria-expanded="${open}">
            <td class="name-cell"><div class="name-line">${pickBox(p.name || "product")}${toneDot(attentionOf(p, brand, !flat))}<span class="product-name">${escapeHtml(p.name || "Unnamed Product")}</span>${containsNote(p)}${p.ingredients.length === 0 ? (isOut(p)
                ? `<span class="chip warn ingredient-missing">${icon("alert")}<span>No ingredient list</span></span>`
                : `<button type="button" class="chip warn ingredient-missing" data-act="assist-product" data-id="${escapeHtml(p.id)}" aria-label="No ingredient list. Find ingredients for ${escapeHtml(p.name || "product")} with AI." data-tip="Search the web for a draft ingredient list, then check it against the product package.">${icon("alert")}<span>No ingredient list</span><span class="ingredient-missing-cta">Find with AI</span></button>`) : ""}</div></td>
            ${flat ? `<td class="brand-ref">${escapeHtml(brand ? brand.name : (p.brandName || "–"))}</td>` : ""}
            <td class="type-cell">${escapeHtml(p.cosmeticType || "–")}</td>
            <td class="shade-cell">${shadeCell}</td>
            <td class="scan-count text-right">${p.scans}</td>
            <td class="status-cell">${productStatusCell(p, brand)}</td>
            <td class="text-right">${rowActions(buttons)}</td>
        </tr>` + (open ? detailRow(p, brand, flat ? 7 : 6) : "");
}

/**
 * The status of a product as the app sees it (productVisibility: the app's own rule). A product that is not Active keeps its
 * status badge; an Active one says whether the app really shows it.
 */
function productStatusCell(p, brand) {
    const { live, reason } = productVisibility(p, brand);
    if (live) return toneBadge("Live", "ok", "The app shows this product.");
    if (reason === "not-makeup") return toneBadge("Hidden (not make-up)", "bad", "The app lists make-up only, so it hides this product.");
    if (reason === "brand-pending") return toneBadge("Approved · Hidden (brand pending)", "warn", "The product is approved, but its brand is not, so the app hides it.") + `<div class="field-hint warn">${BRAND_HINT}</div>`;
    if (reason === "brand-not-active" || reason === "brand-binned") return toneBadge("Approved · Hidden (brand " + (reason === "brand-binned" ? "in the Recycle bin" : "not active") + ")", "warn", "The product is approved, but its brand is switched off, so the app hides it.");
    if (reason === "product-not-active" && p.status === "Active") return toneBadge("Hidden (no status)", "warn", "The product has no status in the database, so the app hides it. Edit it and save.");
    return statusBadge(p.status);
}
const BRAND_HINT = "Approve the brand too so users can see this product.";

/** Everything else about a product: shown under its row when the row is clicked. */
function detailRow(p, brand, columns) {
    const scope = productScope(p.cosmeticType, p.name);
    const kind = scope === "makeup" ? "Make-up. The app checks it and offers AR Try-On."
        : scope === "unclear" ? `The app hides it: ${scopeWords(p.cosmeticType, p.name)}. Press Edit and choose its kind; if it is not make-up, remove it.`
        : `Not make-up (${scopeWords(p.cosmeticType, p.name)}). GlowUp does not list it: move it to the Recycle bin.`;
    const facts = [["Kind", escapeHtml(kind)]];
    if (p.aliases.length > 0) facts.push(["Other names", escapeHtml(p.aliases.join(", "))]);
    if (p.barcode) facts.push(["Barcode", `<code class="small">${escapeHtml(p.barcode)}</code> ${gs1Line(p.barcode)}`]);
    if (!brand) facts.push(["Brand", `<span class="chip warn">its brand is gone: press Edit and choose a brand</span>`]);
    else facts.push(["Brand", escapeHtml(brand.name) + (brand.status !== "Active" ? ` <span class="chip warn">brand ${escapeHtml(brand.status.toLowerCase())}: the app hides its products</span>` : "")]);

    const missing = new Set(notCurated(p.ingredients).map(sameKey));
    if (p.ingredients.length === 0) facts.push(["Ingredients", `<span class="chip warn">none listed: the app cannot check or recommend it</span>`]);
    else {
        const chips = p.ingredients.slice(0, SHOWN_INGREDIENTS).map((name) => (missing.has(sameKey(name))
            ? `<span class="chip warn" data-tip="Not in the Ingredient Curator yet.">${escapeHtml(name)}</span>` : `<span class="chip">${escapeHtml(name)}</span>`)).join("");
        const rest = p.ingredients.length - SHOWN_INGREDIENTS;
        facts.push(["Ingredients", `<span class="muted">${p.ingredients.length} listed${missing.size > 0 ? ` · ${missing.size} not in the Ingredient Curator (amber)` : ""}</span><br>${chips}${rest > 0 ? `<span class="muted small">and ${rest} more</span>` : ""}`]);
    }
    if (p.shades.length > 0) {
        const coded = (p.shadeCodes || []).filter(Boolean).length;
        facts.push(["Shades", p.shades.map((s, at) => `<span class="swatch" style="background:${escapeHtml(s)}"></span>${(p.shadeCodes || [])[at] ? `<strong class="small">${escapeHtml(p.shadeCodes[at])}</strong> ` : ""}<code class="small">${escapeHtml(s)}</code>`).join(" &nbsp;")
            + (coded === 0 ? `<br><span class="chip warn" data-tip="Brands print a shade code on the pack (such as 119 or N20), not a colour. Press Edit and type the code next to each colour: the app then finds the colour from the code it reads on a scanned pack.">no shade codes</span>` : "")]);
    }
    if (p.minAge > 0) facts.push(["Minimum age", `${p.minAge}+`]);
    if (p.description) facts.push(["Description", escapeHtml(p.description)]);
    if (p.folder) facts.push(["Folder", escapeHtml(p.folder)]);
    const origin = originText(p);
    if (origin) facts.push(["Added", origin]);

    const button = (cls, act, iconHtml, label, tip) => `<button type="button" class="row-btn ${cls}" data-act="${act}" data-id="${escapeHtml(p.id)}" data-tip="${escapeHtml(tip)}">${iconHtml}${label}</button>`;
    const buttons = [button("main", "edit-product", icon("edit"), "Edit", "Change the name, brand, type, ingredients, shades or status.")];
    if (p.ingredients.length === 0 && !isOut(p)) buttons.push(button("", "assist-product", icon("sparkle"), "Assist with AI", "Search for a draft ingredient list, then check it against the product package."));
    const together = approvalFor(p, brand);
    if (together) buttons.push(button("approve", "approve-with-brand", icon("check"), together.label, together.tip));
    else if (p.needsReview && scope === "makeup") buttons.push(button("approve", "approve-product", icon("check"), "Approve", "You have checked it: set it to Active."));
    buttons.push(button("", "folder-product", icon("folder"), "Folder", "Put it in a folder, or take it out."));
    buttons.push(`<span class="spacer"></span>`, button("danger", "bin-product", BIN_ICON, "Recycle bin", "Remove it. It can be restored from the Recycle bin."));

    return `
        <tr class="detail-row" data-for="${escapeHtml(p.id)}"><td colspan="${columns}">
            <div class="detail-box">
                <dl class="detail-grid">${facts.map(([label, value]) => `<dt>${label}</dt><dd>${value}</dd>`).join("")}</dl>
                <div class="detail-actions">${buttons.join("")}</div>
            </div>
        </td></tr>`;
}

function toggleDetails(productId) {
    openId = openId === productId ? "" : productId;
    renderInventory();
    if (openId) { const row = el("inventoryTableBody").querySelector(`tr.product-row[data-product="${CSS.escape(openId)}"]`); if (row) row.focus({ preventScroll: true }); }
}

// ---------------------------------------------------------------------------------------------
// The Recycle bin
// ---------------------------------------------------------------------------------------------
/** What the bin lists: every brand in it, and every product that did not go there together with its brand. */
function binEntries() {
    const brandIds = new Set(binBrands.map((b) => b.id));
    const entries = binBrands.map((b) => ({ key: "b:" + b.id, item: b, brand: true, riders: binProducts.filter((p) => p.binnedWith === b.id).length }));
    binProducts.forEach((p) => { if (!(p.binnedWith && brandIds.has(p.binnedWith))) entries.push({ key: "p:" + p.id, item: p, brand: false, riders: 0 }); });
    return entries.sort((a, b) => b.item.binnedAt - a.item.binnedAt || byName(a.item, b.item));
}

function renderBin() {
    const body = el("inventoryTableBody");
    const typed = el("inventorySearchInput").value, term = sameKey(typed);
    shownGroups = [];
    el("inventoryTableHead").innerHTML = `<tr><th><span class="th-line">${pickAllBox("entry")}In the Recycle bin</span></th><th>Kind</th><th>Brand</th><th>Removed</th><th></th></tr>`;
    const all = binEntries();
    const list = all.filter((entry) => matchesSearch(typed, [entry.item.name, ...entry.item.aliases, entry.item.brandName, entry.item.binnedReason]));
    el("inventoryCountLine").textContent = all.length === 0 ? "" : many(list.length, "entry", "entries") + " · not used by the app";
    setPagers(binPage, list.length, "entries");
    if (list.length === 0) {
        body.innerHTML = `<tr><td colspan="5" class="table-message">${term ? `Nothing in the Recycle bin matches "${escapeHtml(typed)}".` : "The Recycle bin is empty. Drag a row onto the bin to remove it."}</td></tr>`;
        return;
    }
    body.innerHTML = paginate(list, binPage).map((entry) => {
        const item = entry.item;
        const brandName = entry.brand ? "" : ((everyBrand.find((b) => b.id === item.brandId) || {}).name || item.brandName || "");
        const kind = entry.brand ? `Brand${entry.riders > 0 ? ` <span class="muted">· with ${many(entry.riders, "product", "products")}</span>` : ""}` : `Product <span class="muted">· ${escapeHtml(item.cosmeticType || "no type")}</span>`;
        const buttons = [
            actionButton("row-btn approve", "restore", entry.key, icon("undo"), "Restore", entry.brand ? "Back to the list, with the products that went with it." : "Back to the list. The app uses it again as before."),
            actionButton("row-btn danger", "purge", entry.key, icon("trash"), "Delete forever", "Delete it from the database.", "It asks first. This cannot be undone.")
        ];
        return `
            <tr class="bin-row" data-key="${escapeHtml(entry.key)}" data-drag tabindex="0">
                <td class="name-cell"><div class="name-line">${pickBox(item.name || "entry")}<span class="product-name">${escapeHtml(item.name || "Unnamed")}</span></div>${item.binnedReason ? `<div class="muted small bin-why">${escapeHtml(item.binnedReason)}</div>` : ""}</td>
                <td>${kind}</td>
                <td class="brand-ref">${brandName ? escapeHtml(brandName) : DASH}</td>
                <td class="muted">${item.binnedAt ? escapeHtml(timeAgo(new Date(item.binnedAt))) : DASH}</td>
                <td class="text-right">${rowActions(buttons)}</td>
            </tr>`;
    }).join("");
}

/** Updates and deletes, in batches the database accepts. ops = [{ ref, data }] (no data = delete). */
async function runBatches(ops) {
    for (let i = 0; i < ops.length; i += BATCH_LIMIT) {
        const batch = writeBatch(db);
        ops.slice(i, i + BATCH_LIMIT).forEach((op) => { if (op.data) batch.update(op.ref, op.data); else batch.delete(op.ref); });
        await batch.commit();
    }
}
/** Saves what the page already shows. When the database refuses, the page shows what is really there again. */
async function saveOrReload(ops) {
    try { await runBatches(ops); }
    catch (error) {
        console.error("Saving failed: ", error);
        await loadInventory();
        throw new Error(describeError(error));
    }
}
const split = (keys) => {
    const p = [], b = [];
    Array.from(new Set(keys)).forEach((key) => { const item = itemOf(key); if (item) (key.startsWith("b:") ? b : p).push(item); });
    return { p, b };
};
const named = (items, one, more) => (items.length === 1 ? `"${items[0].name || "Unnamed"}"` : many(items.length, one, more));

/** Puts products (or all products of a brand) into a folder. folder "" takes them out. */
async function moveToFolder(keys, folder) {
    const { p, b } = split(keys);
    const wanted = new Map(p.filter((x) => !x.binned).map((x) => [x.id, x]));
    b.forEach((brand) => productsOf(brand.id).forEach((x) => wanted.set(x.id, x)));
    const list = Array.from(wanted.values()).filter((x) => sameKey(x.folder) !== sameKey(folder));
    if (list.length === 0) throw friendly(wanted.size === 0 ? "That brand has no products to move." : folder ? `Already in "${folder}".` : "Not in a folder.");
    const before = list.map((x) => ({ item: x, folder: x.folder }));
    const write = (changes) => saveOrReload(changes.map((c) => ({ ref: doc(db, "products", c.item.id), data: { folder: c.folder, updatedAt: serverTimestamp() } })));
    list.forEach((x) => { x.folder = folder; });                 // shown at once; the database follows
    afterChange();
    await write(list.map((x) => ({ item: x, folder })));
    return { text: named(list, "product", "products"), undo: async () => { before.forEach((c) => { c.item.folder = c.folder; }); afterChange(); await write(before); } };
}

/** A folder gets another name, or (to = "") is taken off everything that is in it. */
async function refolder(from, to) {
    const list = everyProduct.filter((x) => sameKey(x.folder) === sameKey(from));
    list.forEach((x) => { x.folder = to; });
    afterChange();
    await saveOrReload(list.map((x) => ({ ref: doc(db, "products", x.id), data: { folder: to, updatedAt: serverTimestamp() } })));
}

/** Moves products and brands (a brand with its products) to the Recycle bin: the app stops using them at once. */
async function moveToBin(keys) {
    const { p, b } = split(keys);
    const goneBrands = new Set(b.map((x) => x.id));
    const ops = [], changed = [];
    const put = (item, collectionName, withBrand) => {
        if (item.binned) return;
        changed.push(item);
        ops.push({ ref: doc(db, collectionName, item.id), data: { binned: true, binnedAt: serverTimestamp(), statusBeforeBin: item.status, status: "Inactive", binnedWith: withBrand || "", updatedAt: serverTimestamp() } });
        Object.assign(item, { binned: true, binnedAt: Date.now(), statusBeforeBin: item.status, status: "Inactive", binnedWith: withBrand || "" });
    };
    let riders = 0;
    b.forEach((brand) => { productsOf(brand.id).forEach((x) => { riders++; put(x, "products", brand.id); }); put(brand, "brands", ""); });
    const single = p.filter((x) => !goneBrands.has(x.brandId) && !x.binned);
    single.forEach((x) => put(x, "products", ""));
    if (ops.length === 0) throw friendly("Nothing to remove.");
    if (changed.some((x) => x.id === openId)) openId = "";
    afterChange();
    await saveOrReload(ops);
    const parts = [];
    if (b.length > 0) parts.push(named(b, "brand", "brands") + (riders > 0 ? ` (with ${many(riders, "product", "products")})` : ""));
    if (single.length > 0) parts.push(named(single, "product", "products"));
    return { text: parts.join(" and "), undo: () => restoreFromBin(keys) };
}

/** Brings entries back from the Recycle bin with the status they had. A product never comes back without its brand. */
async function restoreFromBin(keys, folder) {
    const { p, b } = split(keys);
    const ops = [], seen = new Set();
    const back = (item, collectionName, extra) => {
        if (!item.binned || seen.has(collectionName + "/" + item.id)) return;
        seen.add(collectionName + "/" + item.id);
        const status = STATUSES.includes(item.statusBeforeBin) ? item.statusBeforeBin : "Active";
        const data = Object.assign({ binned: false, status, statusBeforeBin: "", binnedWith: "", binnedReason: "", updatedAt: serverTimestamp() }, extra);
        ops.push({ ref: doc(db, collectionName, item.id), data });
        Object.assign(item, { binned: false, status, statusBeforeBin: "", binnedWith: "", binnedReason: "", binnedAt: 0 }, extra ? { folder: extra.folder } : null);
    };
    let riders = 0, withBrand = 0;
    b.forEach((brand) => { everyProduct.filter((x) => x.binned && x.binnedWith === brand.id).forEach((x) => { riders++; back(x, "products"); }); back(brand, "brands"); });
    const single = p.filter((x) => x.binned);
    single.forEach((x) => {
        const itsBrand = everyBrand.find((brand) => brand.id === x.brandId);
        if (itsBrand && itsBrand.binned) { withBrand++; back(itsBrand, "brands"); }
        back(x, "products", folder ? { folder } : null);
    });
    if (ops.length === 0) throw friendly("Nothing to restore.");
    afterChange();
    await saveOrReload(ops);
    const parts = [];
    if (b.length > 0) parts.push(named(b, "brand", "brands") + (riders > 0 ? ` (with ${many(riders, "product", "products")})` : ""));
    if (single.length > 0) parts.push(named(single, "product", "products") + (withBrand > 0 ? " (and its brand)" : ""));
    return { text: parts.join(" and ") };
}

/** "Delete forever": asks with the list of names, then deletes the documents. Resolves true when it was done. */
async function purgeFromBin(keys) {
    const { p, b } = split(keys);
    const refs = new Map(), names = [];
    b.filter((brand) => brand.binned).forEach((brand) => {
        const riders = everyProduct.filter((x) => x.binned && x.brandId === brand.id);
        names.push(`${brand.name || "Unnamed brand"} (brand${riders.length > 0 ? ", with " + many(riders.length, "product", "products") : ""})`);
        riders.forEach((x) => refs.set("products/" + x.id, doc(db, "products", x.id)));
    });
    p.filter((x) => x.binned).forEach((x) => { if (!refs.has("products/" + x.id)) { names.push(`${x.brandName ? x.brandName + ": " : ""}${x.name || "Unnamed product"}`); refs.set("products/" + x.id, doc(db, "products", x.id)); } });
    b.filter((brand) => brand.binned).forEach((brand) => refs.set("brands/" + brand.id, doc(db, "brands", brand.id)));       // the brands themselves go last
    if (refs.size === 0) return false;
    if (!(await askConfirm({
        title: `Delete forever: ${many(names.length, "entry", "entries")}?`,
        message: "They are deleted from the database. This cannot be undone.",
        items: names, confirmLabel: "Delete forever", danger: true
    }))) return false;
    try {
        await runBatches(Array.from(refs.values()).map((ref) => ({ ref })));
        notify(`${many(names.length, "entry", "entries")} deleted for good.`, "ok");
    } catch (error) {
        console.error("Error deleting entries: ", error);
        notify("Not everything was deleted. " + describeError(error), "error");
    }
    await loadInventory();
    return true;
}

// ---------------------------------------------------------------------------------------------
// Make-up and face products only
// ---------------------------------------------------------------------------------------------
/** The line under the type box of the product form: what the app will do with a product of this kind. */
function showScopeHint() {
    const hint = el("productScopeHint");
    const type = el("productTypeInput").value === OTHER_TYPE ? el("productTypeOtherInput").value : el("productTypeInput").value;
    if (!type.trim()) { hint.textContent = ""; return; }
    const name = el("productNameInput").value, scope = productScope(type, name);
    hint.textContent = scope === "makeup" ? "Make-up: the app checks it and offers AR Try-On."
        : scope === "unclear" ? `The app cannot tell that this is make-up: ${scopeWords(type, name)}. It can only be saved as Pending, and the app hides it until its kind is clear.`
        : `Not make-up (${scopeWords(type, name)}): it cannot be saved here.`;
    hint.className = scope === "makeup" ? "field-hint" : "field-hint warn";
}

/** Moves every listed product that is not make-up, and the brands that are left without anything else, to the Recycle bin. */
async function removeOutOfScope() {
    const gone = products.filter(isOut);
    const goneIds = new Set(gone.map((p) => p.id));
    const emptyBrands = brands.filter((b) => categoryOutOfScope(b.category) && productsOf(b.id).every((p) => goneIds.has(p.id)));
    if (gone.length + emptyBrands.length === 0) { notify("Every listed product is make-up. Nothing to remove.", "ok"); return; }

    const names = gone.map((p) => `${p.brandName ? p.brandName + ": " : ""}${p.name} (${scopeWords(p.cosmeticType, p.name)})`).concat(emptyBrands.map((b) => `brand: ${b.name} (${b.category || "no category"})`));
    if (!(await askConfirm({
        title: `Move ${many(gone.length, "product", "products")} and ${many(emptyBrands.length, "brand", "brands")} to the Recycle bin?`,
        message: "They are not make-up. The app stops using them at once. Nothing is deleted: they can be restored from the Recycle bin. \"Clean up\" in the same menu shows the same list with a reason for each one.",
        items: names, confirmLabel: "Move them"
    }))) return;

    try {
        const brandIds = new Set(emptyBrands.map((b) => b.id));
        await applyChanges(emptyBrands.map((b) => ({ collection: "brands", docId: b.id, action: "bin", fields: { binnedWith: "" }, reason: "It has no make-up products." }))
            .concat(gone.map((p) => ({ collection: "products", docId: p.id, action: "bin", fields: { binnedWith: brandIds.has(p.brandId) ? p.brandId : "" }, reason: `Not make-up: ${scopeWords(p.cosmeticType, p.name)}.` }))));
        notify(`${many(gone.length, "product", "products")} and ${many(emptyBrands.length, "brand", "brands")} moved to the Recycle bin.`, "ok");
        el("reviewFilterSelect").value = "";
        renderInventory();
    } catch (error) {
        console.error("Error moving entries to the Recycle bin: ", error);
        notify("Not everything was moved. " + describeError(error), "error");
    }
}

// ---------------------------------------------------------------------------------------------
// Writing a list of changes (from "Clean up", "Find duplicates" and "Remove what is not make-up")
// ---------------------------------------------------------------------------------------------
/**
 * Writes changes as glowupCatalog.js describes them: { collection: "brands" | "products", docId, action: "bin" | "update",
 * fields, reason }. "bin" moves the entry to the Recycle bin exactly as dragging it there does (it is switched off for
 * the app and keeps the status it had, for Restore), and notes why. Nothing is deleted. The page then loads its data again.
 */
async function applyChanges(changes) {
    const find = (change) => (change.collection === "brands" ? everyBrand : everyProduct).find((item) => item.id === change.docId);
    const ops = [];
    changes.forEach((change) => {
        const item = find(change);
        if (!item || (change.collection !== "brands" && change.collection !== "products")) return;
        if (change.action === "bin") {
            if (item.binned) return;
            ops.push({ ref: doc(db, change.collection, item.id), data: {
                binned: true, binnedAt: serverTimestamp(), statusBeforeBin: item.status, status: "Inactive", binnedWith: (change.fields && change.fields.binnedWith) || "",
                binnedReason: String(change.reason || "").slice(0, 200), updatedAt: serverTimestamp() } });
        } else if (change.action === "update" && change.fields) {
            ops.push({ ref: doc(db, change.collection, item.id), data: Object.assign({}, change.fields, { updatedAt: serverTimestamp() }) });
        }
    });
    if (ops.length === 0) return 0;
    try { await runBatches(ops); }
    finally { await loadInventory(); }           // also after a failure: the page then shows what is really there
    return ops.length;
}

// ---------------------------------------------------------------------------------------------
// Clean up, and Find duplicates (the lists come from ../_Shared/glowupCatalog.js, the dialogs from adminCleanup.js)
// ---------------------------------------------------------------------------------------------
const CLEANUP_STEPS = ["notMakeup", "notMakeupCheck", "brands", "brandDuplicates", "productDuplicates", "types", "names"];

function openCleanUp() {
    openPlanDialog({
        title: "Clean up the inventory",
        intro: "GlowUp holds make-up only. These are the changes the rules propose for what is listed now. Open a step to see every entry, take the tick off what should stay, and apply the step. Nothing is deleted: what is removed goes to the Recycle bin.",
        makePlan: () => cleanupPlan(everyBrand, everyProduct, [], { only: CLEANUP_STEPS }),
        apply: (changes) => applyChanges(changes),
        notes: (plan) => [
            { title: "Products the rule cannot place", text: "Kinds that are care and colour at once (a tinted lip balm, a tinted lip oil) or names that say nothing clear. The app hides them. Edit each one: give it its kind, or move it to the Recycle bin.",
                items: plan.forReview.unclear.map((e) => `${e.product.name} (${brandNameOf(e.product)}): kind "${e.product.cosmeticType || "none"}"${e.judged.word ? `, the word "${e.judged.word}"` : ""}`),
                action: { label: "Show them in the list", run: () => showOnly("review", true) } },
            { title: "Kinds worth a second look", text: "The kind stored with these was often filled in by an import and may be wrong (a lip liner filed as Lipstick). The kind decides where AR Try-On puts the colour. Edit the ones that are wrong.",
                items: plan.forReview.kinds.map((e) => `${e.product.name} (${brandNameOf(e.product)}): ${e.why}`) },
            { title: "Possibly the same product", text: "The same brand and nearly the same name. Often these are real variants (a shade, a refill, a waterproof version).",
                items: plan.forReview.possibleDuplicates.map((g) => g.items.map((p) => p.name).join("  /  ")),
                action: { label: "Open Find duplicates", run: () => openDuplicates("products") } }
        ]
    });
}
const brandNameOf = (product) => (everyBrand.find((b) => b.id === product.brandId) || {}).name || product.brandName || "no brand";

function openDuplicates(startWith) {
    const when = (item) => (item.addedAt ? "added " + timeAgo(new Date(item.addedAt * 1000)) : "");
    const origin = (item) => originLabel(item.source) || "Origin not recorded";
    const sections = [
        {
            id: "products", label: "Products",
            find: (threshold) => productDuplicates(everyProduct, everyBrand, { threshold }),
            describe: (p) => ({
                title: p.name, badges: [{ text: p.status, tone: p.status === "Active" ? "good" : p.status === "Pending" ? "warn" : "" }].concat(p.needsReview ? [{ text: "needs review", tone: "review" }] : []),
                lines: [`${brandNameOf(p)} · ${p.cosmeticType || "no kind"} · ${many(p.ingredients.length, "ingredient", "ingredients")} · ${many(p.scans, "scan", "scans")}${p.barcode ? " · barcode " + p.barcode : ""}`,
                    [origin(p), when(p)].filter((t) => t).join(" · ")]
            }),
            explainMerge: (keep, others) => {
                const merged = mergeProducts(keep, others), lines = [`"${keep.name}" (${brandNameOf(keep)}) is kept.`];
                if (merged.keepChanges.aliases) lines.push(`It also answers to: ${merged.keepChanges.aliases.join(", ")}.`);
                if (merged.keepChanges.scans !== undefined) lines.push(`Its scans become ${merged.keepChanges.scans} (the scans are added up).`);
                if (merged.keepChanges.ingredients) lines.push(`It takes the ingredient list of a copy (${merged.keepChanges.ingredients.length} ingredients), because it has none.`);
                if (merged.keepChanges.barcode) lines.push(`It takes the barcode ${merged.keepChanges.barcode}.`);
                others.forEach((p) => lines.push(`"${p.name}" goes to the Recycle bin.`));
                return lines;
            },
            merge: (keep, others) => {
                const merged = mergeProducts(keep, others);
                return applyChanges((Object.keys(merged.keepChanges).length > 0 ? [{ collection: "products", docId: keep.id, action: "update", fields: merged.keepChanges }] : [])
                    .concat(merged.bin.map((gone) => ({ collection: "products", docId: gone.item.id, action: "bin", fields: { binnedWith: "" }, reason: gone.reason }))));
            },
            remove: (others, keep) => applyChanges(others.map((p) => ({ collection: "products", docId: p.id, action: "bin", fields: { binnedWith: "" }, reason: `Removed as a copy of "${keep.name}".` })))
        },
        {
            id: "brands", label: "Brands",
            find: () => ({ sure: brandDuplicates(everyBrand), possible: [] }),
            describe: (b) => ({
                title: b.name, badges: [{ text: b.status, tone: b.status === "Active" ? "good" : b.status === "Pending" ? "warn" : "" }],
                lines: [`${b.category || "no category"} · ${many(productsOf(b.id).length, "product", "products")} · ${many(b.scans, "scan", "scans")}`, [origin(b), when(b)].filter((t) => t).join(" · ")]
            }),
            explainMerge: (keep, others) => {
                const merged = mergeBrands(keep, others, products), lines = [`"${keep.name}" is kept.`];
                if (merged.keepChanges.aliases) lines.push(`It also answers to: ${merged.keepChanges.aliases.join(", ")}.`);
                if (merged.keepChanges.scans !== undefined) lines.push(`Its scans become ${merged.keepChanges.scans}.`);
                if (merged.moveProducts.length > 0) lines.push(`${many(merged.moveProducts.length, "product moves", "products move")} to it.`);
                others.forEach((b) => lines.push(`The entry "${b.name}" goes to the Recycle bin.`));
                return lines;
            },
            merge: (keep, others) => {
                const merged = mergeBrands(keep, others, products);
                return applyChanges((Object.keys(merged.keepChanges).length > 0 ? [{ collection: "brands", docId: keep.id, action: "update", fields: merged.keepChanges }] : [])
                    .concat(merged.moveProducts.map((move) => ({ collection: "products", docId: move.product.id, action: "update", fields: { brandId: move.brandId, brandName: move.brandName } })))
                    .concat(merged.bin.map((gone) => ({ collection: "brands", docId: gone.item.id, action: "bin", fields: { binnedWith: "" }, reason: gone.reason }))));
            }
        }
    ];
    if (startWith === "brands") sections.reverse();
    openDuplicatesDialog({
        title: "Find duplicates",
        intro: "Entries that are the same thing, or may be. Choose which one to keep, then merge the others into it. Nothing is merged or removed by itself, and nothing is deleted.",
        threshold: 0.85, sections
    });
}

// ---------------------------------------------------------------------------------------------
// The FDA Philippines catalogue: make-up gathered from the FDA's public Verification Portal
// (https://verification.fda.gov.ph), kept in ../_Shared/data/fdaCatalogue.json. Each entry carries its
// notification number and, where Open Beauty Facts has it, its ingredient list. Nothing is added
// until the admin confirms; what is in the inventory already (also in the Recycle bin) is left alone.
// ---------------------------------------------------------------------------------------------
const slug = (text) => sameKey(text).replace(/[^a-z0-9]+/g, "_").replace(/^_+|_+$/g, "").slice(0, 80);

/**
 * The catalogue was made smaller (fewer brands, more of them with an ingredient list). What an earlier, bigger catalogue added
 * and is no longer in the file goes to the Recycle bin, together with brands that are left without products. Only entries that
 * came from the FDA list, have no ingredient list and were never scanned are touched; they can be restored from the bin.
 */
async function trimFdaCatalogue() {
    let catalogue;
    try {
        const answer = await fetch("../_Shared/data/fdaCatalogue.json", { cache: "no-store" });
        if (!answer.ok) throw new Error("the catalogue file was not found");
        catalogue = await answer.json();
    } catch (error) { notify("The catalogue could not be read. " + error.message, "error"); return; }
    const kept = new Set((catalogue.products || []).map((e) => sameKey(e.brand) + "|" + sameKey(e.name)));
    const extra = products.filter((p) => p.source === "fdaph" && p.ingredients.length === 0 && p.scans === 0 && !kept.has(sameKey(p.brandName) + "|" + sameKey(p.name)));
    if (extra.length === 0) { notify("Nothing to trim: every FDA-list product is in the catalogue, has an ingredient list, or was scanned.", "ok"); return; }
    const going = new Set(extra.map((p) => p.id));
    const emptyBrands = brands.filter((b) => b.source === "fdaph" && productsOf(b.id).length > 0 && productsOf(b.id).every((p) => going.has(p.id)));
    if (!(await askConfirm({
        title: `Move ${many(extra.length, "product", "products")} and ${many(emptyBrands.length, "brand", "brands")} to the Recycle bin?`,
        message: "They came from the FDA Philippines list, have no ingredient list, were never scanned and are not in the smaller catalogue. You can restore them from the Recycle bin.",
        items: extra.slice(0, 12).map((p) => `${p.brandName}: ${p.name}`).concat(extra.length > 12 ? [`and ${extra.length - 12} more`] : []), confirmLabel: "Move them"
    }))) return;
    const brandIds = new Set(emptyBrands.map((b) => b.id));
    org.toBin(emptyBrands.map((b) => "b:" + b.id).concat(extra.filter((p) => !brandIds.has(p.brandId)).map((p) => "p:" + p.id)), true);
}

async function importFdaCatalogue(byItself) {
    byItself = byItself === true;
    let catalogue;
    try {
        const answer = await fetch("../_Shared/data/fdaCatalogue.json", { cache: "no-store" });
        if (!answer.ok) throw new Error("the catalogue file was not found");
        catalogue = await answer.json();
    } catch (error) { if (!byItself) notify("The catalogue could not be read. " + error.message, "error"); return; }
    // make-up only. An entry whose kind is not clear is added too when an admin asks for the catalogue (as Pending, to be
    // looked at); the automatic pass leaves it out.
    const scopeOfEntry = (e) => productScope(e.type, e.name);
    const entries = (Array.isArray(catalogue.products) ? catalogue.products : []).filter((e) => e && e.name && e.brand && e.type
        && (scopeOfEntry(e) === "makeup" || (!byItself && scopeOfEntry(e) === "unclear")));

    // brands: the ones already listed are used (also under another spelling they are known by), the others are added.
    // A listed brand wins over one that is in the Recycle bin.
    const brandIdOf = new Map();
    // (the last one set wins: first what is in the bin, then imported entries, then the ones typed or approved here)
    const standing = (b) => (b.binned ? 0 : b.source && !b.reviewed ? 1 : b.source ? 2 : 3);
    everyBrand.slice().sort((a, b) => standing(a) - standing(b)).forEach((b) => [b.name, ...b.aliases].forEach((known) => { if (compactName(known)) brandIdOf.set(compactName(known), b.id); }));
    const newBrands = new Map();
    // what is there already, also in the Recycle bin, under any of its names and with or without the brand in front:
    // a product the clean-up renamed or merged must not come back as a new one
    const keyOf = (brand, name) => compactName(brand) + "|" + compactName(nameWithoutBrand(name, brand));
    const have = new Set();
    everyProduct.forEach((p) => [p.name, ...p.aliases].forEach((known) => { have.add(keyOf(brandNameOf(p), known)); have.add(keyOf(p.brandName, known)); }));
    const fresh = [];
    entries.forEach((e) => {
        if (have.has(keyOf(e.brand, e.name))) return;
        have.add(keyOf(e.brand, e.name));
        if (!brandIdOf.has(compactName(e.brand)) && !newBrands.has(compactName(e.brand))) newBrands.set(compactName(e.brand), { id: "fda_" + slug(e.brand), name: e.brand });
        fresh.push(e);
    });
    // A product that is in the inventory already but still has NO ingredient list gets the list the catalogue file now
    // has for it. Only products that came from this list and that nobody has given a list: nothing an admin typed, an
    // import approved or a scan read is ever overwritten.
    const listed = new Map();
    everyProduct.forEach((p) => { if (!p.binned) [p.name, ...p.aliases].forEach((known) => { listed.set(keyOf(brandNameOf(p), known), p); listed.set(keyOf(p.brandName, known), p); }); });
    const fills = [];
    entries.forEach((e) => {
        if (!Array.isArray(e.ingredients) || e.ingredients.length === 0) return;
        const p = listed.get(keyOf(e.brand, e.name));
        if (p && p.source === "fdaph" && p.ingredients.length === 0 && !fills.some((f) => f.product.id === p.id)) fills.push({ product: p, entry: e });
    });
    if (fresh.length === 0 && fills.length === 0) { if (!byItself) notify("Everything from the FDA Philippines catalogue is in the inventory already.", "ok"); return; }
    const withList = fresh.filter((e) => Array.isArray(e.ingredients) && e.ingredients.length > 0).length;
    if (!byItself && !(await askConfirm({
        title: fresh.length > 0 ? `Add ${many(fresh.length, "product", "products")} and ${many(newBrands.size, "brand", "brands")}?` : `Fill in the ingredient list of ${many(fills.length, "product", "products")}?`,
        message: `They are on the FDA Philippines list of notified cosmetics (gathered ${catalogue.gathered || "recently"}). ${withList} come with an ingredient list; the others get theirs when a user scans them. Make-up is added as Active, marked as coming from the FDA list; ${fresh.filter((e) => scopeOfEntry(e) === "unclear").length} whose kind is not clear are added as Pending for you to look at.`,
        items: fresh.slice(0, 12).map((e) => `${e.brand}: ${e.name} (${e.type})`).concat(fresh.length > 12 ? [`and ${fresh.length - 12} more`] : [])
            .concat(fills.length > 0 ? [`${many(fills.length, "product", "products")} already listed without an ingredient list get the list the catalogue now has (each names its source page)`] : []),
        confirmLabel: fresh.length > 0 ? "Add them" : "Fill them in"
    }))) return;

    try {
        const portal = "FDA Philippines Verification Portal | https://verification.fda.gov.ph";
        const sets = [];
        newBrands.forEach((b) => {
            brandIdOf.set(compactName(b.name), b.id);
            sets.push({ ref: doc(db, "brands", b.id), data: { name: b.name, category: BRAND_CATEGORY, market: "Philippines", status: "Active", scans: 0, source: "fdaph", reviewed: true, createdAt: serverTimestamp() } });
        });
        fresh.forEach((e) => {
            const note = `FDA Philippines: notified as "${e.fdaName || e.name}" (${e.fdaNumber})` + (e.company ? `, by ${e.company}` : "") + "." + (e.validUntil ? ` Valid until ${e.validUntil}.` : "") + (e.note ? " " + e.note : "");
            const clear = scopeOfEntry(e) === "makeup";          // a kind that is not clear waits as Pending for an admin
            sets.push({
                ref: doc(db, "products", "fda_" + slug(e.fdaNumber || e.brand + "_" + e.name)),
                data: {
                    name: e.name, brandId: brandIdOf.get(compactName(e.brand)), brandName: e.brand, cosmeticType: e.type, description: note,
                    ingredients: Array.isArray(e.ingredients) ? e.ingredients.slice(0, 80) : [], shades: [], minAge: 0, status: clear ? "Active" : "Pending", scans: 0,
                    source: "fdaph", reviewed: clear, sources: [portal].concat(e.ingredientSource ? [e.ingredientSource] : []), fdaNumber: e.fdaNumber || "", createdAt: serverTimestamp()
                }
            });
        });
        // the lists for products that had none: only the list and where it came from are written, the rest of the entry stays
        fills.forEach((f) => sets.push({
            ref: doc(db, "products", f.product.id), update: true,
            data: { ingredients: f.entry.ingredients.slice(0, 80), sources: f.product.sources.concat(f.entry.ingredientSource && !f.product.sources.includes(f.entry.ingredientSource) ? [f.entry.ingredientSource] : []) }
        }));
        for (let i = 0; i < sets.length; i += BATCH_LIMIT) {
            const batch = writeBatch(db);
            sets.slice(i, i + BATCH_LIMIT).forEach((s) => (s.update ? batch.update(s.ref, s.data) : batch.set(s.ref, s.data)));
            await batch.commit();
        }
        const told = [];
        if (fresh.length > 0) told.push(`${many(fresh.length, "product", "products")} and ${many(newBrands.size, "brand", "brands")} added from the FDA Philippines list`);
        if (fills.length > 0) told.push(`${many(fills.length, "ingredient list", "ingredient lists")} filled in`);
        notify(told.join("; ") + ".", "ok");
    } catch (error) {
        console.error("Error adding the catalogue: ", error);
        notify("Not everything was added. " + describeError(error), "error");
    }
    await loadInventory();
}

// ---------------------------------------------------------------------------------------------
// Brand form
// ---------------------------------------------------------------------------------------------
function openBrandForm(brand) {
    const form = el("addBrandForm");
    form.reset();
    setFormError(form, "");
    editingBrandId = brand ? brand.id : null;
    el("brandModalTitle").textContent = brand ? "Edit Brand" : "Add New Brand";
    el("brandNameInput").value = brand ? brand.name : "";
    el("brandCategoryInput").value = BRAND_CATEGORY;           // every brand is a make-up brand; saving one that had another category corrects it
    el("brandMarketInput").value = brand ? brand.market : "Global";
    el("brandStatusInput").value = brand && STATUSES.includes(brand.status) ? brand.status : "Active";
    el("brandLogoInput").value = brand ? brand.logoUrl : "";
    el("brandImageInput").value = brand ? brand.imageUrl : "";
    paintWebsiteBox(brand);
    showReviewNote(el("brandReviewNote"), brand, "brand");
    brandModal.open();
}

async function saveBrand(event) {
    event.preventDefault();
    const form = el("addBrandForm");
    const saveButton = el("saveBrandBtn");
    if (saveButton.disabled) return;

    const name = el("brandNameInput").value.replace(/\s+/g, " ").trim();
    const market = el("brandMarketInput").value.replace(/\s+/g, " ").trim() || "Global";
    const status = el("brandStatusInput").value;
    const logoUrl = cleanLink(el("brandLogoInput").value), imageUrl = cleanLink(el("brandImageInput").value);

    if (logoUrl === null) { setFormError(form, "The logo link must be a web address that starts with https://"); el("brandLogoInput").focus(); return; }
    const website = websiteFromForm(form);
    if (website === null) return;
    if (imageUrl === null) { setFormError(form, "The picture link must be a web address that starts with https://"); el("brandImageInput").focus(); return; }
    if (!name) { setFormError(form, "Type the brand name."); el("brandNameInput").focus(); return; }
    if (name.length < 2) { setFormError(form, "The brand name is too short."); el("brandNameInput").focus(); return; }
    // the same brand written another way ("L'Oreal" / "LOREAL", an accent, a space) is the same brand
    const duplicate = everyBrand.find((b) => b.id !== editingBrandId && [b.name, ...b.aliases].some((known) => compactName(known) === compactName(name)));
    if (duplicate) { setFormError(form, duplicate.binned ? `"${duplicate.name}" is in the Recycle bin. Restore it from there instead.` : `"${duplicate.name}" is already in the inventory.`); el("brandNameInput").focus(); return; }
    setFormError(form, "");

    setBusy(saveButton, true, "Saving...");
    try {
        if (editingBrandId) {
            const before = brands.find((b) => b.id === editingBrandId);
            const renamed = before && before.name !== name;
            const mine = renamed ? everyProduct.filter((p) => p.brandId === editingBrandId) : [];

            // the brand and (when it was renamed) the brand name stored on each of its products, together
            const brandData = { name, category: BRAND_CATEGORY, market, status, logoUrl, imageUrl, ...websiteFields(website, before), updatedAt: serverTimestamp() };
            if (before && before.needsReview) { brandData.reviewed = true; brandData.reviewedAt = serverTimestamp(); }     // an admin who opens it and saves has checked it
            const writes = [{ ref: doc(db, "brands", editingBrandId), data: brandData }];
            mine.forEach((p) => writes.push({ ref: doc(db, "products", p.id), data: { brandName: name, updatedAt: serverTimestamp() } }));
            await runBatches(writes);
            notify(renamed && mine.length > 0 ? `Brand saved. Its ${many(mine.length, "product now shows", "products now show")} the new name.` : "Brand saved.", "ok");
        } else {
            await addDoc(collection(db, "brands"), { name, category: BRAND_CATEGORY, market, status, logoUrl, imageUrl, ...websiteFields(website, null), scans: 0, createdAt: serverTimestamp() });
            notify("Brand added. Point at its row and press + to list its products.", "ok");
        }
        brandModal.close();
        await loadInventory();
    } catch (error) {
        console.error("Error saving brand: ", error);
        setFormError(form, "The brand was not saved. " + describeError(error));
    } finally {
        setBusy(saveButton, false);
    }
}

/**
 * The "Official website" box of the Brand form. The confirmed address is the only one whose pages are labelled
 * "Manufacturer's own site" (here, in the app and in Assist with AI). A suggestion from Assist with AI or an import is
 * shown with a "Use this address" button; the barcodes of the brand's products are shown with their GS1 hints.
 */
function paintWebsiteBox(brand) {
    const input = el("brandWebsiteInput"), confirm = el("brandWebsiteConfirm"), state = el("brandWebsiteState"), hints = el("brandWebsiteHints");
    input.value = brand ? brand.website : "";
    input.dataset.saved = input.value;
    confirm.checked = false;
    const lines = [];
    if (brand && brand.website) lines.push(`<span class="chip good">Confirmed</span> ${brand.websiteConfirmedAt ? "on " + escapeHtml(brand.websiteConfirmedAt.toLocaleDateString()) : "by an admin"}. Its pages are labelled "Manufacturer's own site".`);
    else lines.push(`<span class="chip warn">Not confirmed</span> No page counts as this brand's own site until an admin confirms its address.`);
    if (brand && brand.websiteSuggested && brand.websiteSuggested !== brand.website)
        lines.push(`Suggested by AI or an import (not checked): <code class="small">${escapeHtml(brand.websiteSuggested)}</code> <button type="button" class="row-btn" data-website-use="${escapeHtml(brand.websiteSuggested)}">Use this address</button>`);
    state.innerHTML = lines.join("<br>");
    const barcodes = brand ? Array.from(new Set(everyProduct.filter((p) => p.brandId === brand.id && !p.binned && p.barcode).map((p) => p.barcode))).slice(0, 4) : [];
    hints.innerHTML = barcodes.length
        ? "Barcodes of its products (a hint about the company, not proof of the website):<br>" + barcodes.map((code) => `<code class="small">${escapeHtml(code)}</code> ${gs1Line(code)}`).join("<br>")
        : "";
}

/** The website typed in the box: "" (none), the address, or null after showing what is wrong (not https, or not confirmed). */
function websiteFromForm(form) {
    const input = el("brandWebsiteInput");
    const value = cleanLink(input.value);
    if (value === null) { setFormError(form, "The official website must be a web address that starts with https://"); input.focus(); return null; }
    if (value && !plainLink(value)) { setFormError(form, "Use the plain address of the website (no user name or backslash in it)."); input.focus(); return null; }
    if (value && value !== input.dataset.saved && !el("brandWebsiteConfirm").checked) {
        setFormError(form, "Tick the box under the website to confirm you checked that this brand owns it, or clear the address.");
        el("brandWebsiteConfirm").focus();
        return null;
    }
    return value;
}

/** The website fields to save: a new or changed address records who confirmed it and when; a cleared one removes both. */
function websiteFields(website, before) {
    const old = before ? before.website : "";
    if (website === old) return {};
    if (!website) return { website: "", websiteConfirmedBy: "", websiteConfirmedAt: null };
    return { website, websiteConfirmedBy: auth.currentUser ? auth.currentUser.uid : "", websiteConfirmedAt: serverTimestamp() };
}

/** True for an address with no user name and no backslash (the same "plain address" the source labels require). */
function plainLink(url) {
    try { const parsed = new URL(url); return !parsed.username && !parsed.password && !url.includes(String.fromCharCode(92)); } catch (error) { return false; }
}

/** A barcode's GS1 hint and a button that opens Verified by GS1 (the barcode is copied first, to paste there). */
function gs1Line(barcode) {
    const hint = gs1Hint(barcode);
    if (!hint.text) return "";
    return `<span class="field-hint">${escapeHtml(hint.text)}</span>`
        + (hint.valid ? ` <button type="button" class="row-btn" data-gs1="${escapeHtml(String(barcode).replace(/[^0-9]/g, ""))}" data-tip="Copies the barcode and opens GS1's free search page (about 30 searches a day). Paste it there to see which company licenses it.">Check on Verified by GS1</button>` : "");
}

/** The two buttons above, wherever they are on the page. */
function wireWebsiteAndGs1Buttons() {
    document.addEventListener("click", async (event) => {
        const use = event.target.closest("[data-website-use]");
        if (use) { el("brandWebsiteInput").value = use.dataset.websiteUse; el("brandWebsiteConfirm").focus(); return; }
        const gs1 = event.target.closest("[data-gs1]");
        if (!gs1) return;
        event.preventDefault();
        event.stopPropagation();
        try { await navigator.clipboard.writeText(gs1.dataset.gs1); notify(`Barcode ${gs1.dataset.gs1} copied. Paste it on the Verified by GS1 page.`, "ok"); }
        catch (error) { notify(`Copy the barcode ${gs1.dataset.gs1}, then paste it on the Verified by GS1 page.`, "info"); }
        window.open(VERIFIED_BY_GS1, "_blank", "noopener,noreferrer");
    }, true);
}

// ---------------------------------------------------------------------------------------------
// Product form
// ---------------------------------------------------------------------------------------------
/** product = null for a new one; brandId = the brand a new product starts with. */
function openProductForm(product, brandId) {
    const form = el("productForm");
    form.reset();
    setFormError(form, "");
    el("productAiNote").hidden = true;
    el("productAiNote").textContent = "";
    editingProductId = product ? product.id : null;
    snapshotInForm = null;
    assistedProductSources = product ? product.sources.slice() : [];
    el("productModalTitle").textContent = product ? "Edit Product" : "Add Product";

    el("productNameInput").value = product ? product.name : "";
    const wantedBrand = product ? product.brandId : (brandId || "");
    el("productBrandInput").value = brands.some((b) => b.id === wantedBrand) ? wantedBrand : "";

    const type = product ? product.cosmeticType : "";
    const known = COSMETIC_TYPES.find((t) => sameKey(t) === sameKey(type));
    el("productTypeInput").value = known ? known : (type ? OTHER_TYPE : "");
    el("productTypeOtherInput").value = known ? "" : type;

    el("productDescInput").value = product ? product.description : "";
    el("productIngredientsInput").value = product ? product.ingredients.join(", ") : "";
    el("productImageInput").value = product ? product.imageUrl : "";
    el("productFdaInput").value = product ? product.fdaNumber : "";
    el("productIngredientSearch").value = "";
    if (ingredientPicker) ingredientPicker.setNotes(product ? product.ingredientNotes : []);
    el("productMinAgeInput").value = product && product.minAge > 0 ? product.minAge : "";
    el("productStatusInput").value = product && STATUSES.includes(product.status) ? product.status : "Active";
    formShades = product ? product.shades.slice() : [];
    formShadeCodes = product ? formShades.map((_, at) => (product.shadeCodes || [])[at] || "") : [];
    shadeAdding = false; shadeMessage = "";
    sourceEditor.reset();
    sourceEditor.paint();

    showOtherTypeBox();
    showBrandHint();
    showIngredientSummary();
    renderShadeEditor();
    showReviewNote(el("productReviewNote"), product, "product");
    productModal.open();

    if (brands.length === 0) setFormError(form, "There are no brands yet. Close this, add a brand first, then add the brand's products.");
}

/** The blue note at the top of a form that is open on an entry nobody has checked yet. */
function showReviewNote(note, entry, kind) {
    if (!entry || !entry.needsReview) { note.textContent = ""; note.style.display = "none"; return; }
    const origin = originLabel(entry.source) || "Added automatically";
    const links = sourceLinks(entry.sources || [], websiteOfEntry(entry));
    note.innerHTML = `<b>${escapeHtml(origin)}.</b> Nobody has checked this ${kind} yet. `
        + (kind === "product" ? "Compare the name, brand, type and ingredient list with the packaging, " : "Check the name and category, ")
        + "set Status to <b>Active</b> if the app should use it, then save: <b>saving marks it as reviewed</b>."
        + (links ? "<br>Source: " + links + sourceGuideHtml() : "");
    note.style.display = "";
}

function showOtherTypeBox() {
    el("productTypeOtherInput").style.display = el("productTypeInput").value === OTHER_TYPE ? "" : "none";
    showScopeHint();
}

function showBrandHint() {
    const brand = brands.find((b) => b.id === el("productBrandInput").value);
    const hint = el("productBrandHint");
    if (brand && brand.status !== "Active") {
        hint.textContent = `This brand is ${brand.status}. The app hides its products until the brand is Active.`;
        hint.className = "field-hint warn";
    } else {
        hint.textContent = "Brand not in the list? Close this and add the brand first.";
        hint.className = "field-hint";
    }
}

function showIngredientSummary() {
    const names = toList(el("productIngredientsInput").value);
    const hint = el("productIngredientsHint");
    if (names.length === 0) {
        hint.textContent = "Separate the ingredients with commas (or put one on each line).";
        hint.className = "field-hint";
        return;
    }
    const missing = notCurated(names);
    let text = `${names.length} ingredient${names.length === 1 ? "" : "s"}.`;
    if (curatorKnown && missing.length === 0) text += " All of them are in the Ingredient Curator.";
    else if (missing.length > 0) {
        const shown = missing.slice(0, 6).join(", ") + (missing.length > 6 ? ` and ${missing.length - 6} more` : "");
        text += ` ${missing.length} not in the Ingredient Curator yet: ${shown}. The app may still know them from its built-in list; add them to the Curator to control the wording.`;
    }
    if (names.length < 4) text += " The app needs at least 4 ingredients to recognise a scan as this product.";
    hint.textContent = text;
    hint.className = missing.length > 0 ? "field-hint warn" : "field-hint";
}

/** The newest products as a CSV file (capped at EXPORT_MAX_ROWS; built from the loaded list, so it costs no reads). */
function exportProductsCsv() {
    const newest = listed().slice().sort((a, b) => (b.addedAt || 0) - (a.addedAt || 0));
    const columns = [
        { title: "Brand", value: (p) => p.brandName }, { title: "Product", value: (p) => p.name }, { title: "Kind", value: (p) => p.cosmeticType },
        { title: "Shades", value: (p) => p.shades }, { title: "Shade codes", value: (p) => p.shadeCodes || [] }, { title: "Minimum age", value: (p) => p.minAge }, { title: "Status", value: (p) => p.status },
        { title: "Scans", value: (p) => p.scans }, { title: "Ingredients", value: (p) => p.ingredients },
        { title: "FDA registration number", value: (p) => p.fdaNumber }, { title: "FDA link", value: (p) => fdaLinkOf(p.sources) }, { title: "Photo link", value: (p) => p.imageUrl }
    ];
    downloadCsv(`${EXPORT_FILE_PREFIX}-products-${today()}.csv`, toCsv(columns, newest));
    notify(`${Math.min(EXPORT_MAX_ROWS, newest.length)} products saved to a file.${newest.length > EXPORT_MAX_ROWS ? " The export holds the newest " + EXPORT_MAX_ROWS + " only." : ""}`, "ok");
}

// ----- ingredients picked in the product form -----
/** A new ingredient made from the product form: saved in the library at once, so the next product finds it too. */
async function createIngredientFromPicker(fields) {
    const data = {
        name: fields.name, description: fields.description, aliases: fields.aliases || [], classificationId: "", purpose: "",
        riskLevel: ["safe", "caution", "harmful"].includes(fields.riskLevel) ? fields.riskLevel : "",
        cautionNote: "", commonUses: "", minAge: 0, keyActive: false, umbrella: false, highlights: [],
        allergenTags: [], avoidForSkinTypes: [], goodForSkinTypes: [], avoidForConditions: [], goodForConditions: [],
        folder: "", createdAt: serverTimestamp(), updatedAt: serverTimestamp()
    };
    const made = await addDoc(collection(db, "ingredients"), data);
    const entry = { id: made.id, name: data.name, techName: "", aliases: data.aliases };
    curatedList.push(entry);
    curatedList.sort((a, b) => a.name.localeCompare(b.name));
    [data.name, ...data.aliases].forEach((n) => { const k = sameKey(n); if (k) curatedKeys.add(k); });
    curatorKnown = true;
    notify(`"${data.name}" is in the ingredient library now.`, "ok");
    return entry;
}

/** The library entry of a name as written on a product ("" when the library does not have it). */
function libraryIdOf(name) {
    const key = sameKey(name);
    const found = curatedList.find((entry) => [entry.name, entry.techName, ...entry.aliases].some((n) => sameKey(n) === key));
    return found ? found.id : "";
}

/** Words a product can be found by (kept on the product itself): its kind, its brand and a few facts. */
function productTags(product) {
    const slug = (text) => String(text || "").toLowerCase().normalize("NFD").replace(/[\u0300-\u036f]/g, "").replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 30);
    const tags = [slug(product.cosmeticType), slug(product.brandName)];
    if (product.shades.length > 0) tags.push("has-shades");
    if (product.ingredients.length === 0) tags.push("no-ingredient-list");
    if ((product.sources || []).some((s) => /fda\.gov\.ph/i.test(s))) tags.push("fda-listed");
    return Array.from(new Set(tags.filter(Boolean))).slice(0, MAX_TAGS);
}

/** A web address typed into a form: "" when empty, null when it is not an https address, else the clean address. */
function cleanLink(value) {
    const text = String(value || "").trim();
    if (!text) return "";
    try { const url = new URL(text); return url.protocol === "https:" && text.length <= 300 ? url.href : null; } catch (error) { return null; }
}

/** The FDA Philippines page of a product, from its sources ("FDA Philippines verification | https://...") */
/** An FDA registration (notification) number: letters, digits and . , # ( ) _ - / and single spaces, at most 40 characters; "" when empty, null when it is not one. */
function cleanFdaNumber(value) {
    const text = String(value == null ? "" : value).replace(/\s+/g, " ").trim();
    if (text === "") return "";
    return /^[A-Za-z0-9][A-Za-z0-9 .,#()_\-\/]{0,39}$/.test(text) ? text : null;
}

const fdaLinkOf = (sources) => {
    const entry = (sources || []).find((s) => /fda\.gov\.ph/i.test(s));
    return entry ? (entry.split("|").pop() || "").trim() : "";
};

// ----- shades -----
// Each shade is "#RRGGBB" (the app and AR Try-On read them in this order) and may have its SHADE CODE, the number or letters the brand
// prints on the pack ("119", "N20", "12A"). The app reads that code from the pack and finds the colour with it. The admin adds one by
// typing a hex code or picking a colour (nothing is added by itself), edits one by its picker, its hex or its code, moves it earlier or
// later (the code goes with it), or removes it.
let shadeAdding = false, shadeMessage = "", shadeDraft = "#C24B62", shadeNearOk = "";

function renderShadeEditor() {
    const swatch = (shade) => `background:${escapeHtml(shade)}`;
    const items = formShades.map((shade, index) => `
        <span class="shade-item" data-index="${index}">
            <label class="shade-swatch" style="${swatch(shade)}" ><input type="color" class="shade-picker" value="${escapeHtml(shade.toLowerCase())}" data-index="${index}" aria-label="Pick the colour of shade ${index + 1}"></label>
            <input type="text" class="shade-hex" value="${escapeHtml(shade)}" maxlength="22" spellcheck="false" autocomplete="off" data-index="${index}" aria-label="Hex code of shade ${index + 1}">
            <input type="text" class="shade-code" value="${escapeHtml(formShadeCodes[index] || "")}" maxlength="16" spellcheck="false" autocomplete="off" placeholder="Code" data-index="${index}" aria-label="Shade code of shade ${index + 1}, as printed on the pack">
            <button type="button" data-move="-1" data-index="${index}" aria-label="Move shade ${index + 1} earlier"${index === 0 ? " disabled" : ""}>‹</button>
            <button type="button" data-move="1" data-index="${index}" aria-label="Move shade ${index + 1} later"${index === formShades.length - 1 ? " disabled" : ""}>›</button>
            <button type="button" data-remove="${index}" aria-label="Remove shade ${index + 1}">×</button>
        </span>`).join("");
    let tail;
    if (shadeAdding && formShades.length < MAX_SHADES) {
        tail = `<span class="shade-item shade-new">
            <label class="shade-swatch" style="${swatch(shadeDraft)}" ><input type="color" class="shade-picker" id="newShadePicker" value="${escapeHtml(shadeDraft.toLowerCase())}" aria-label="Pick a colour for the new shade"></label>
            <input type="text" class="shade-hex" id="newShadeHex" value="${escapeHtml(shadeDraft)}" maxlength="22" spellcheck="false" autocomplete="off" placeholder="#C24B62" aria-label="Hex code of the new shade">
            <input type="text" class="shade-code" id="newShadeCode" value="" maxlength="16" spellcheck="false" autocomplete="off" placeholder="Code" aria-label="Shade code of the new shade, as printed on the pack">
            <button type="button" class="row-btn" id="confirmShadeBtn">Add</button>
            <button type="button" id="cancelShadeBtn" aria-label="Cancel adding a shade">×</button>
        </span>`;
    } else if (formShades.length < MAX_SHADES) tail = `<button type="button" class="row-btn" id="addShadeBtn">${icon("plus")}Shade</button>`;
    else tail = `<span class="field-hint">A product can have up to ${MAX_SHADES} shades.</span>`;
    const note = shadeMessage ? `<span class="field-hint shade-msg" role="alert">${escapeHtml(shadeMessage)}</span>` : "";
    // many shades: a box that scrolls up and down, so the form stays short; the add box stays below it
    const strip = formShades.length >= SHADE_STRIP_FROM;
    el("productShadeEditor").innerHTML = (strip ? `<div class="shade-strip" role="group" aria-label="${formShades.length} shades, scroll to see them all">${items}</div><span class="field-hint">${formShades.length} shades. Scroll the list to see them all.</span>` : items) + tail + note;
    if (strip && shadeAdding) { const row = el("productShadeEditor").querySelector(".shade-strip"); if (row) row.scrollTop = row.scrollHeight; }
}

function addShadeFromDraft() {
    const field = el("newShadeHex"), codeField = el("newShadeCode");
    const shade = cleanShade(field ? field.value : "");
    const typedCode = codeField ? codeField.value.trim() : "", code = shadeCodeOf(typedCode);
    if (shade === null) { shadeMessage = "Type a colour such as #C24B62, or pick one with the colour box. A shade number such as 119 goes in the Code box."; renderShadeEditor(); return; }
    if (typedCode && !code) { shadeMessage = "That is not a shade code. Use the letters and digits printed on the pack, such as 119 or N20."; renderShadeEditor(); return; }
    if (code && formShadeCodes.some((other) => other === code)) { shadeMessage = "Code " + code + " belongs to another shade of this product already."; renderShadeEditor(); return; }
    if (formShades.includes(shade)) { shadeMessage = shade + " is in the list already."; renderShadeEditor(); return; }
    if (formShades.length >= MAX_SHADES) return;
    // a colour that is almost one already in the list: said once, and the same colour again means "add anyway"
    const near = nearShade(formShades, shade);
    if (near && shadeNearOk !== shade) { shadeNearOk = shade; shadeMessage = shade + " looks almost like " + near + ", which is in the list. Press Add again to add it anyway."; renderShadeEditor(); return; }
    shadeNearOk = "";
    formShades.push(shade);
    formShadeCodes.push(code);
    shadeDraft = shade;
    shadeMessage = "";
    shadeAdding = formShades.length < MAX_SHADES;
    renderShadeEditor();
    const next = el("newShadeHex");
    if (next) next.focus();
}

function onShadeClick(event) {
    const button = event.target.closest("button");
    if (!button) return;
    if (button.id === "addShadeBtn") { shadeAdding = true; shadeMessage = ""; renderShadeEditor(); const field = el("newShadeHex"); if (field) field.select(); return; }
    if (button.id === "confirmShadeBtn") { addShadeFromDraft(); return; }
    if (button.id === "cancelShadeBtn") { shadeAdding = false; shadeMessage = ""; renderShadeEditor(); return; }
    if (button.hasAttribute("data-remove")) { const at = Number(button.getAttribute("data-remove")); formShades.splice(at, 1); formShadeCodes.splice(at, 1); }
    else if (button.hasAttribute("data-move")) {
        const from = Number(button.getAttribute("data-index")), to = from + Number(button.getAttribute("data-move"));
        if (to < 0 || to >= formShades.length) return;
        formShades.splice(to, 0, formShades.splice(from, 1)[0]);
        formShadeCodes.splice(to, 0, formShadeCodes.splice(from, 1)[0] || "");
    } else return;
    shadeMessage = "";
    renderShadeEditor();
}

/** A colour picked with the colour box: the code and the swatch follow, without a full re-draw (the picker stays open). */
function onShadeInput(event) {
    const input = event.target;
    if (input.classList.contains("shade-picker")) {
        const shade = cleanShade(input.value);
        if (shade === null) return;
        const item = input.closest(".shade-item");
        const hex = item.querySelector(".shade-hex");
        if (hex) hex.value = shade;
        input.parentElement.style.background = shade;
        if (input.id === "newShadePicker") shadeDraft = shade;
        else {
            const index = Number(input.getAttribute("data-index"));
            if (index >= 0 && index < formShades.length) formShades[index] = shade;
        }
    } else if (input.id === "newShadeHex") {
        const shade = cleanShade(input.value);
        if (shade !== null) { shadeDraft = shade; const swatchBox = input.closest(".shade-item"); swatchBox.querySelector(".shade-swatch").style.background = shade; swatchBox.querySelector(".shade-picker").value = shade.toLowerCase(); }
    }
}

/** A code typed into an existing shade: kept when it is a colour, put back (with a note) when it is not. */
function onShadeChange(event) {
    const input = event.target;
    if (input.classList.contains("shade-code") && input.id !== "newShadeCode") { onShadeCodeChange(input); return; }
    if (!input.classList.contains("shade-hex") || input.id === "newShadeHex") return;
    const index = Number(input.getAttribute("data-index"));
    if (!(index >= 0 && index < formShades.length)) return;
    const shade = cleanShade(input.value);
    if (shade === null) shadeMessage = "That is not a colour. Use a code such as #C24B62.";
    else if (formShades.some((other, at) => at !== index && other === shade)) shadeMessage = shade + " is in the list already.";
    else { formShades[index] = shade; shadeMessage = ""; }
    renderShadeEditor();
}

/** A shade code typed into an existing shade: kept when it is a code (or empty), put back with a note when it is not or another shade has it. */
function onShadeCodeChange(input) {
    const index = Number(input.getAttribute("data-index"));
    if (!(index >= 0 && index < formShades.length)) return;
    const typed = input.value.trim(), code = shadeCodeOf(typed);
    if (typed && !code) shadeMessage = "That is not a shade code. Use the letters and digits printed on the pack, such as 119 or N20.";
    else if (code && formShadeCodes.some((other, at) => at !== index && other === code)) shadeMessage = "Code " + code + " belongs to another shade of this product already.";
    else { formShadeCodes[index] = code; shadeMessage = ""; }
    renderShadeEditor();
}

function onShadeKey(event) {
    if (event.key !== "Enter" || !(event.target.classList.contains("shade-hex") || event.target.classList.contains("shade-code"))) return;
    event.preventDefault();
    if (event.target.id === "newShadeHex" || event.target.id === "newShadeCode") addShadeFromDraft(); else event.target.blur();
}

// ----- saving -----
async function saveProduct(event) {
    event.preventDefault();
    const form = el("productForm");
    const saveButton = el("saveProductBtn");
    if (saveButton.disabled) return;

    const clean = (id) => el(id).value.replace(/\s+/g, " ").trim();
    const name = clean("productNameInput");
    const brand = brands.find((b) => b.id === el("productBrandInput").value);
    const cosmeticType = el("productTypeInput").value === OTHER_TYPE ? clean("productTypeOtherInput") : el("productTypeInput").value;
    const description = el("productDescInput").value.trim();
    const ingredients = toList(el("productIngredientsInput").value);
    const minAgeText = el("productMinAgeInput").value.trim();
    const minAge = toWholeNumber(minAgeText);
    const status = el("productStatusInput").value;
    const { shades, codes: shadeCodes } = pairShades(formShades, formShadeCodes, MAX_SHADES);
    const imageUrl = cleanLink(el("productImageInput").value), fdaNumber = cleanFdaNumber(el("productFdaInput").value);

    if (imageUrl === null) { setFormError(form, "The photo link must be a web address that starts with https://"); el("productImageInput").focus(); return; }
    if (fdaNumber === null) { setFormError(form, "The FDA registration number may have letters, digits and . , # ( ) _ - / only (at most 40 characters)."); el("productFdaInput").focus(); return; }
    if (!name) { setFormError(form, "Type the product name."); el("productNameInput").focus(); return; }
    if (!brand) { setFormError(form, "Choose the brand."); el("productBrandInput").focus(); return; }
    if (!cosmeticType) { setFormError(form, "Choose the cosmetic type."); el("productTypeInput").focus(); return; }
    const scope = productScope(cosmeticType, name);
    if (scope === "skincare" || scope === "out") { setFormError(form, `GlowUp lists make-up only. This reads like ${scopeWords(cosmeticType, name)}, so it cannot be saved. If that is wrong, check the name and the kind.`); el("productTypeInput").focus(); return; }
    if (scope === "unclear" && status === "Active") { setFormError(form, `The app cannot tell that this is make-up: ${scopeWords(cosmeticType, name)}. Set Status to Pending to keep it for later, or correct the name or the kind.`); el("productStatusInput").focus(); return; }
    if (ingredients.length === 0) { setFormError(form, "Paste the ingredient list. The app cannot check or recommend a product without it."); el("productIngredientsInput").focus(); return; }
    if (minAgeText !== "" && (!/^\d+$/.test(minAgeText) || minAge > 99)) { setFormError(form, "Minimum age must be a whole number from 0 to 99."); el("productMinAgeInput").focus(); return; }
    const duplicate = everyProduct.find((p) => p.id !== editingProductId && p.brandId === brand.id && [p.name, ...p.aliases].some((known) => compactName(known) === compactName(name)));
    if (duplicate) { setFormError(form, duplicate.binned ? `"${duplicate.name}" of ${brand.name} is in the Recycle bin. Restore it from there instead.` : `${brand.name} already has a product named "${duplicate.name}".`); el("productNameInput").focus(); return; }
    setFormError(form, "");

    // the FDA page lives in the sources, as "FDA Philippines verification | https://..." (the convention of the import and of the scan)
    const sources = assistedProductSources.slice();          // links already kept (also an FDA page) stay as they are
    const notes = ingredientPicker ? ingredientPicker.getNotes() : [];
    const data = {
        name, brandId: brand.id, brandName: brand.name, cosmeticType, description,
        ingredients, shades, minAge, status, imageUrl, fdaNumber,
        // the codes printed on the pack, at the same places as the shades: the app reads a code on a scanned pack and finds the colour (ShadeCode.cs)
        shadeCodes: shadeCodes.some(Boolean) ? shadeCodes : [],
        // light summaries kept on the product so that nothing needs a join: the links to the library, how many, and the words a filter uses
        ingredientIds: Array.from(new Set(ingredients.map(libraryIdOf).filter(Boolean))),
        ingredientCount: ingredients.length,
        ingredientNotes: notes.map((n) => ({ name: n.name, id: libraryIdOf(n.name), caution: n.caution, percent: n.percent })),
        tags: productTags({ cosmeticType, brandName: brand.name, shades, ingredients, sources }),
        updatedAt: serverTimestamp()
    };
    if (sources.length > 0 || editingProductId) data.sources = Array.from(new Set(sources)).slice(0, 10);
    const before = products.find((p) => p.id === editingProductId);
    if (before && before.needsReview) { data.reviewed = true; data.reviewedAt = serverTimestamp(); }     // an admin who opens it and saves has checked it

    setBusy(saveButton, true, "Saving...");
    try {
        if (editingProductId) {
            await updateDoc(doc(db, "products", editingProductId), data);           // "scans" and the folder are left as they are
            notify("Product saved.", "ok");
        } else {
            const folder = org.view.kind === "folder" ? org.view.name : "";          // added while a folder is open: it goes into that folder
            const made = await addDoc(collection(db, "products"), { ...data, scans: 0, folder, createdAt: serverTimestamp() });
            notify(folder ? `Product added to "${folder}".` : "Product added.", "ok");
            if (snapshotInForm) {
                const waiting = snapshotInForm.entry;
                snapshotInForm = null;
                try {
                    await finishSnapshot(waiting, made.id, { brand: brand.name, name, kind: cosmeticType, shade: shades[0] || "", ingredients }, snapshotContext());
                    snapshotsWaiting = Math.max(0, (snapshotsWaiting || 1) - 1);
                    paintSnapshotCount();
                } catch (failure) {
                    console.error("Closing the snapshot failed: ", failure);
                    notify("The product was saved, but its snapshot was not closed. Open Pending Snapshot Reviews and reject it. " + describeError(failure), "error");
                }
            }
        }
        productModal.close();
        await loadInventory();
    } catch (error) {
        console.error("Error saving product: ", error);
        setFormError(form, "The product was not saved. " + describeError(error));
    } finally {
        setBusy(saveButton, false);
    }
}

// ---------------------------------------------------------------------------------------------
// Approving what the app added by itself (from a scan) or what came from a data import
// ---------------------------------------------------------------------------------------------
// reviewedBy = the admin account that pressed Approve (who verified it); an automatic approval has none
const approval = () => ({ status: "Active", reviewed: true, reviewedBy: auth.currentUser ? auth.currentUser.uid : "", reviewedAt: serverTimestamp(), updatedAt: serverTimestamp() });

async function approveBrand(brand, button) {
    const waiting = productsOf(brand.id).filter((p) => p.needsReview).length;
    const question = `Approve "${brand.name}"?\n\nThe brand becomes Active: the app then looks for its name on scanned labels.`
        + (waiting > 0 ? `\n\nIts ${waiting} product(s) waiting for review stay Pending until you approve each one.` : "");
    if (!(await sure(question, "Approve", false))) return;

    button.disabled = true;
    // the badge changes at once; if the write fails, the old state comes back
    const before = { status: brand.status, rawStatus: brand.rawStatus, reviewed: brand.reviewed, needsReview: brand.needsReview };
    Object.assign(brand, { status: "Active", rawStatus: "Active", reviewed: true, needsReview: false });
    afterChange();
    try {
        await updateDoc(doc(db, "brands", brand.id), approval());
        notify("Brand approved.", "ok");
    } catch (error) {
        console.error("Error approving brand: ", error);
        Object.assign(brand, before);
        afterChange();
        notify("The brand was not approved. " + describeError(error), "error");
    }
}

async function approveProduct(product, button) {
    const brand = brands.find((b) => b.id === product.brandId);
    if (isOut(product) || isUnclear(product)) { notify(`"${product.name}" cannot be approved: ${scopeWords(product.cosmeticType, product.name)}. Correct its kind with Edit, or remove it.`, "error"); return; }
    if (!brand) { notify(`"${product.name}" has no brand. Open it with Edit, choose its brand and save.`, "error"); return; }
    if (product.ingredients.length === 0) { notify(`"${product.name}" has no ingredient list, so the app cannot check it. Open it with Edit and paste the list.`, "error"); return; }

    // a product and its brand are approved separately: approving one never approves the other
    let question = `Approve "${product.name}"?\n\nIt becomes Active: the app can recognise it in scans and recommend it to the users it suits. Check its name, type and ingredient list first.`;
    if (brand.status !== "Active") question += `\n\nIts brand "${brand.name}" is ${brand.status} and is NOT approved by this. The app hides the product until the brand is approved too.`;
    if (!(await sure(question, "Approve", false))) return;

    button.disabled = true;
    try {
        await updateDoc(doc(db, "products", product.id), approval());
        Object.assign(product, { status: "Active", rawStatus: "Active", reviewed: true, needsReview: false });
        afterChange();
        if (brand.status !== "Active") notify(`Product approved. ${BRAND_HINT} ("${brand.name}" is ${brand.status}.)`, "info");
        else notify("Product approved.", "ok");
    } catch (error) {
        console.error("Error approving product: ", error);
        notify("The product was not approved. " + describeError(error), "error");
        button.disabled = false;
    }
}

/**
 * What the one-button approval of a product row can do: approve the product AND its brand, or only the brand of a product that is
 * Active already. The app uses a product only while its brand is Active too, so a product whose brand still waits stays invisible
 * ("Approved · Hidden (brand pending)"): the app then says the product is not in the catalogue, ingredient list or not. null = nothing
 * to offer (the plain Approve button, or no button).
 */
function approvalFor(p, brand) {
    if (!brand || isOut(p) || isUnclear(p) || brandOut(brand) || brand.status !== "Pending") return null;
    if (p.needsReview) return { product: true, brand: true, label: "Approve with brand", tip: `You have checked this product and its brand "${brand.name}": set both to Active.` };
    if (p.status === "Active") return { product: false, brand: true, label: "Approve brand", tip: `The product is approved but its brand "${brand.name}" is not, so the app hides the product. Approve the brand.` };
    return null;
}

/** The product and its brand become Active together (one write: both or none). */
async function approveWithBrand(product, button) {
    const brand = brands.find((b) => b.id === product.brandId);
    const plan = brand ? approvalFor(product, brand) : null;
    if (!plan) { notify(`"${product.name}" has nothing waiting to be approved together with its brand.`, "info"); return; }
    if (plan.product && product.ingredients.length === 0) { notify(`"${product.name}" has no ingredient list, so the app cannot check it. Open it with Edit and paste the list.`, "error"); return; }

    const unknown = plan.product ? notCurated(product.ingredients).length : 0;
    const question = plan.product
        ? `Approve "${product.name}" and its brand "${brand.name}"?\n\nBoth become Active: the app can then find the product, with its ingredient list, when a user scans it. Check the name, the kind and the ingredient list first.`
          + (unknown > 0 ? `\n\n${many(unknown, "ingredient is", "ingredients are")} not in the Ingredient Curator yet. They stay as they are: the app shows the list and tells the user that there is no information about them yet.` : "")
        : `Approve the brand "${brand.name}"?\n\nIt becomes Active: the app can then find "${product.name}" when a user scans it.`;
    if (!(await sure(question, plan.product ? "Approve both" : "Approve brand", false))) return;

    button.disabled = true;
    const ops = [];
    if (plan.product) ops.push({ ref: doc(db, "products", product.id), data: approval() });
    ops.push({ ref: doc(db, "brands", brand.id), data: approval() });
    try {
        await runBatches(ops);
        if (plan.product) Object.assign(product, { status: "Active", rawStatus: "Active", reviewed: true, needsReview: false });
        Object.assign(brand, { status: "Active", rawStatus: "Active", reviewed: true, needsReview: false });
        afterChange();
        notify(plan.product ? "Product and brand approved." : "Brand approved.", "ok");
    } catch (error) {
        console.error("Error approving product and brand: ", error);
        notify("Nothing was approved. " + describeError(error), "error");
        button.disabled = false;
    }
}

// ---------------------------------------------------------------------------------------------
// Clicks in the table
// ---------------------------------------------------------------------------------------------
function toggleBrand(id) {
    if (closed.has(id)) closed.delete(id); else closed.add(id);
    renderInventory();
}

function onTableClick(event) {
    const button = event.target.closest("button");
    // the list of brands: a click on a brand opens its products (its own buttons keep working)
    if (drill && drill.kind === "brands" && org.view.kind !== "bin") {
        const row = event.target.closest("tr.brand-row");
        if (row && (!button || button.dataset.act === "toggle")) { openDrill({ kind: "brand", id: row.dataset.brand }); return; }
    }
    // one brand's view: its heading stays open
    if (drill && drill.kind === "brand" && event.target.closest("tr.brand-row") && (!button || button.dataset.act === "toggle")) return;
    if (!button) {
        // a click on a row itself: a product shows (or hides) its details, a brand opens or closes
        if (event.target.closest("a, .detail-box")) return;
        const productRowEl = event.target.closest("tr.product-row"), brandRowEl = event.target.closest("tr.brand-row");
        if (productRowEl) toggleDetails(productRowEl.dataset.product);
        else if (brandRowEl) toggleBrand(brandRowEl.dataset.brand);
        return;
    }
    const act = button.dataset.act, id = button.dataset.id || "";
    if (act === "toggle") { toggleBrand(button.dataset.brand); return; }
    if (act === "more") { const brandId = button.dataset.brand; moreShown.set(brandId, (moreShown.get(brandId) || PRODUCTS_AT_A_TIME) + PRODUCTS_AT_A_TIME); renderInventory(); return; }
    if (act === "restore") { org.restore([id]); return; }
    if (act === "purge") { org.purge([id]); return; }

    const brand = brands.find((b) => b.id === id), product = products.find((p) => p.id === id);
    if (act === "approve-brand" && brand) approveBrand(brand, button);
    else if (act === "add-product" && brand) openProductForm(null, brand.id);
    else if (act === "edit-brand" && brand) openBrandForm(brand);
    else if (act === "bin-brand" && brand) org.toBin(["b:" + brand.id]);
    else if (act === "approve-product" && product) approveProduct(product, button);
    else if (act === "approve-with-brand" && product) approveWithBrand(product, button);
    else if (act === "edit-product" && product) openProductForm(product, null);
    else if (act === "assist-product" && product) { openProductForm(product, null); assistProductIngredients(); }
    else if (act === "folder-product" && product) org.chooseFolder(button, ["p:" + product.id]);
    else if (act === "bin-product" && product) org.toBin(["p:" + product.id]);
}

/** A double click on a product opens it to be edited. */
function onTableDoubleClick(event) {
    if (event.target.closest("button, a, .detail-box")) return;
    const row = event.target.closest("tr.product-row");
    const product = row ? products.find((p) => p.id === row.dataset.product) : null;
    if (product) openProductForm(product, null);
}

// ---------------------------------------------------------------------------------------------
// Helpers: the line in each panel that says what needs attention (see adminAssist.js)
// ---------------------------------------------------------------------------------------------
function registerHelpers() {
    const ready = () => everyBrand.length + everyProduct.length > 0;
    const setQuick = (name) => { quick = quick === name ? "" : name; el("reviewFilterSelect").value = ""; el("inventorySearchInput").value = ""; el("brandFilterSelect").value = ""; if (org.view.kind !== "all") org.show({ kind: "all" }); else fromFirstPage(); };
    /** The button of a line that narrows the list: "Show", or "Show all" while the list is narrowed to it already. */
    const showLabel = (on) => (on ? "Show all" : "Show");
    const reviewing = () => el("reviewFilterSelect").value === "review";

    registerAssist("counts", () => {
        if (!ready()) return [];
        const out = outsideCount(), review = reviewCount();
        const lines = [];
        if (out > 0) lines.push({ tone: "bad", text: `${many(out, "entry is", "entries are")} not make-up. GlowUp lists make-up only.`, label: "Clean up", run: openCleanUp, tip: "You see every entry with the reason, and apply one step at a time. Nothing is deleted." });
        if (review > 0) lines.push({ tone: "info", text: `Next: ${many(review, "entry waits", "entries wait")} for your review. The app does not use ${review === 1 ? "it" : "them"} until you approve ${review === 1 ? "it" : "them"}.`, label: reviewing() ? "Show all" : "Show them", run: () => reviewing() ? resetAllFilters() : showOnly("review") });
        if (lines.length === 0) lines.push({ tone: "ok", text: "Everything listed is make-up and has been checked." });
        // the brands that were checked on the FDA Philippines list
        const checked = brands.filter((b) => b.source === "fdaph" || productsOf(b.id).some(QUICK.fda.test));
        const onList = listed().filter(QUICK.fda.test).length;
        if (onList > 0) lines.push({ tone: "ok", text: `${many(onList, "product", "products")} of ${many(checked.length, "brand", "brands")} are on the FDA Philippines list: ${checked.slice(0, 8).map((b) => b.name).join(", ")}${checked.length > 8 ? ` and ${checked.length - 8} more` : ""}.`, label: quick === "fda" ? "Show all" : "Show them", run: () => setQuick("fda") });
        return lines;
    }, { notes: "notesList" });

    registerAssist("tools", () => {
        if (!ready()) return [];
        const parts = [];
        const typed = el("inventorySearchInput").value.trim(), brand = brands.find((b) => b.id === brandFilterValue()), show = el("reviewFilterSelect").value, now = org.view;
        if (typed) parts.push(`search "${typed}"`);
        if (brand) parts.push(`brand ${brand.name}`);
        if (now.kind === "folder") parts.push(`folder "${now.name}"`);
        if (now.kind === "bin") parts.push("the Recycle bin");
        if (show === "review") parts.push("waiting for review");
        if (show === "out") parts.push("not make-up");
        if (quick) parts.push(QUICK[quick].label.toLowerCase());
        if (parts.length > 0) return [{ tone: "info", text: `Showing only: ${parts.join(" · ")}.`, label: "Show everything", run: resetAllFilters }];
        return [
            { text: `Sorted by ${sortNow().short}, ${view.mode === "all" ? "every product in one list" : "grouped by brand"}. Search also finds a type, for example "lipstick".` },
            { text: "Drag a row onto a folder or the bin. Ctrl+click picks several rows; Shift+click picks a range." }
        ];
    }, { notes: "notesList" });

    registerAssist("table", () => {
        if (!ready()) return [];
        const lines = [];
        const all = listed();
        const count = (name) => all.filter(QUICK[name].test).length;
        const none = count("noIngredients"), noShades = count("noShades"), missing = count("notCurated"), inBin = binEntries().length, review = reviewCount();
        if (org.view.kind === "bin") {
            lines.push(inBin > 0 ? { tone: "info", text: `${many(inBin, "entry", "entries")} in the Recycle bin. The app does not use them. Restore brings one back as it was; Delete forever is final.` }
                : { tone: "ok", text: "The Recycle bin is empty." });
            return lines;
        }
        if (none > 0) lines.push({ tone: "warn", text: `${many(none, "product has", "products have")} no ingredient list, so the app cannot check or recommend ${none === 1 ? "it" : "them"}.`, label: showLabel(quick === "noIngredients"), run: () => setQuick("noIngredients") });
        if (review > 0) lines.push({ tone: "info", text: `${many(review, "entry waits", "entries wait")} for review.`, label: showLabel(reviewing()), run: () => reviewing() ? resetAllFilters() : showOnly("review") });
        if (noShades > 0) lines.push({ tone: "warn", text: `${many(noShades, "make-up product has", "make-up products have")} no shades: AR Try-On has nothing to show for ${noShades === 1 ? "it" : "them"}.`, label: showLabel(quick === "noShades"), run: () => setQuick("noShades") });
        if (missing > 0) lines.push({ tone: "info", text: `${many(missing, "product lists", "products list")} ingredients the Curator does not explain yet.`, label: showLabel(quick === "notCurated"), run: () => setQuick("notCurated") });
        if (inBin > 0) lines.push({ text: `${many(inBin, "entry is", "entries are")} in the Recycle bin.`, label: "Open the bin", run: () => org.show({ kind: "bin" }) });
        lines.push({ text: "Click a product for its details. Double-click it to edit. Drag rows onto a folder or the bin." });
        return lines;
    }, { notes: "notesList" });
}

// ---------------------------------------------------------------------------------------------
// The data this page offers to data panels (lists, rows, grids and graphs; see adminWidgets.js)
// ---------------------------------------------------------------------------------------------
function registerData() {
    const SCOPE_NAMES = { makeup: "Make-up", unclear: "Kind not clear", skincare: "Skin care (not listed)", out: "Hair, body, nail, fragrance (not listed)" };
    const toneOfStatus = (status) => (status === "Active" ? "ok" : status === "Pending" ? "warn" : "muted");
    /** A click on a group in a graph narrows the table to it. */
    const search = (text) => { el("reviewFilterSelect").value = ""; el("brandFilterSelect").value = ""; el("inventorySearchInput").value = text; quick = ""; if (org.view.kind !== "all") org.show({ kind: "all" }); else fromFirstPage(); };

    /** A click on a brand in a graph or list chooses it in the toolbar: the table, the counts and the other data panels follow. */
    const chooseBrand = (name) => {
        const brand = brands.find((b) => sameKey(b.name) === sameKey(name));
        el("reviewFilterSelect").value = ""; el("inventorySearchInput").value = ""; quick = "";
        el("brandFilterSelect").value = brand ? brand.id : "";
        if (org.view.kind === "bin") org.show({ kind: "all" }); else fromFirstPage();
    };

    registerDataset("products", {
        label: "Products", noun: "products", nounOne: "product",
        items: () => products.filter(inFocus),                  // the brand that is chosen and the folder that is open narrow the data panels too
        map: (p) => ({
            id: p.id, title: p.name || "Unnamed Product", subtitle: [p.brandName, p.cosmeticType].filter((t) => t).join(" · "), meta: p.needsReview ? "needs review" : p.status,
            tone: isOut(p) ? "bad" : isUnclear(p) ? "warn" : p.needsReview ? "info" : toneOfStatus(p.status), value: p.scans, valueLabel: p.scans === 1 ? "scan" : "scans", swatches: p.shades
        }),
        dimensions: [
            { id: "type", label: "Cosmetic type", of: (p) => p.cosmeticType || null, none: "No type", pick: (k) => search(k === "No type" ? "" : k) },
            { id: "brand", label: "Brand", of: (p) => p.brandName || null, none: "No brand", pick: (k) => chooseBrand(k) },
            { id: "status", label: "Status", of: (p) => p.status, keys: STATUSES, tone: toneOfStatus },
            { id: "scope", label: "Make-up or not", of: (p) => productScope(p.cosmeticType, p.name), keys: ["makeup", "unclear", "skincare", "out"], labelOf: (k) => SCOPE_NAMES[k] || k, tone: (k) => (k === "out" || k === "skincare" ? "bad" : k === "unclear" ? "warn" : null), pick: (k) => showOnly(k === "out" || k === "skincare" ? "out" : k === "unclear" ? "review" : "", true) },
            { id: "review", label: "Checked or not", of: (p) => (waits(p) ? "Waiting for review" : "Checked"), keys: ["Waiting for review", "Checked"], tone: (k) => (k === "Checked" ? "ok" : "info"), pick: (k) => showOnly(k === "Checked" ? "" : "review", true) },
            { id: "shades", label: "Has shades", of: (p) => (p.shades.length > 0 ? "With shades" : "No shades"), keys: ["With shades", "No shades"] },
            { id: "folder", label: "Folder", of: (p) => p.folder || null, none: "No folder", pick: (k) => org.show(k === "No folder" ? { kind: "all" } : { kind: "folder", name: k }) }
        ],
        measures: [{ id: "scans", label: "Scans", noun: "scans", of: (p) => p.scans }],
        open: (p) => openProductForm(p, null),
        // a tile that is removed goes to the Recycle bin (it can be restored there)
        bin: (rows) => org.toBin(rows.map((p) => "p:" + p.id))
    });

    registerDataset("brands", {
        label: "Brands", noun: "brands", nounOne: "brand",
        items: () => (focusBrand() ? [focusBrand()] : brands),
        map: (b) => ({
            id: b.id, title: b.name || "Unnamed Brand", subtitle: [b.category, b.market].filter((t) => t).join(" · "), meta: b.needsReview ? "needs review" : b.status,
            tone: brandOut(b) ? "bad" : b.needsReview ? "info" : toneOfStatus(b.status), value: productsOf(b.id).length, valueLabel: productsOf(b.id).length === 1 ? "product" : "products"
        }),
        dimensions: [
            { id: "brand", label: "Brand", of: (b) => b.name || "Unnamed Brand", pick: (k) => chooseBrand(k) },
            { id: "category", label: "Category", of: (b) => b.category || null, none: "No category", pick: (k) => search(k === "No category" ? "" : k) },
            { id: "market", label: "Market", of: (b) => b.market || null, none: "No market" },
            { id: "status", label: "Status", of: (b) => b.status, keys: STATUSES, tone: toneOfStatus }
        ],
        measures: [
            { id: "products", label: "Products listed", noun: "products", of: (b) => productsOf(b.id).length },
            { id: "scans", label: "Scans", noun: "scans", of: (b) => b.scans }
        ],
        open: (b) => openBrandForm(b),
        bin: (rows) => org.toBin(rows.map((b) => "b:" + b.id))
    });
}
