




























import { db } from "../_Database/firebase-console.js";
import { auth } from "../_Database/firebase-console.js";
import { collection, doc, serverTimestamp, getDoc } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";


import { getDocs, addDoc, updateDoc, writeBatch, onListsRefreshed, onSnapshot } from "../_Shared/adminStore.js";
import {
    requireAdmin, wireSignOut, setupNavigationGuard, setupModal, escapeHtml, sameKey, toList, asArray, toWholeNumber, toDate, timeAgo,
    statusBadge, toneBadge, statusGuide, normalizeStatus, notify, describeError, setBusy, setFormError, originLabel, sourceLinks, sourceGuideHtml, COSMETIC_TYPES, STATUSES,
    productScope, notMakeup, scopeWords, categoryOutOfScope, BRAND_CATEGORY, wireSearch, dialogIsOpen
} from "../_Shared/adminCommon.js";
import { matchesSearch, compactName, barcodeKey } from "../_Shared/glowupRules.js";
import {
    productVisibility, cleanupPlan, productDuplicates, brandDuplicates, mergeProducts, mergeBrands, nameWithoutBrand
} from "../_Shared/glowupCatalog.js";
import { refreshRecommendabilityFlags } from "../_Shared/recommendabilityAdmin.js";
import { openPlanDialog, openDuplicatesDialog } from "../_Shared/adminCleanup.js";
import { mountShell, placeInHeader, paginate, renderPager, icon, BIN_ICON, openMenu, askConfirm, setBinCount, dialogFrame } from "../_Shared/adminShell.js";
import { registerDataset, registerTileDataset, datasetChanged } from "../_Shared/adminWidgets.js";
import { createOrganizer, cleanFolder, friendly } from "../_Shared/adminOrganizer.js";
import { setAssistPage, registerAssist, refreshAssist } from "../_Shared/adminAssist.js";
import { readAdminDocument } from "../_Shared/adminDocumentReader.js";
import { startImport } from "../_Shared/importReview.js";
import { parseImportText, pairShades, shadeCodeOf, ingredientsNotHere } from "../_Shared/importFormat.js";
import { parseLabeledProducts } from "../_Shared/ingredientBlock.js";
import { nearShade } from "../_Shared/listRules.js";
import { mountIngredientPicker } from "../_Shared/adminIngredientPicker.js";
import { openFacets } from "../_Shared/adminFacets.js";
import { attachCounters } from "../_Shared/charLimit.js";
import { bindActions, bindButtons, comboFor } from "../_Shared/shortcuts.js";
import { EXPORT_MAX_ROWS, EXPORT_FILE_PREFIX, toCsv, downloadCsv, today } from "../_Shared/adminExport.js";
import { setPageSettings } from "../_Shared/adminSettingsMenu.js";
import { mountSourceEditor, openApplyWindow } from "./productSources.js";
import { wireWindow } from "../_Shared/adminWindow.js";
import { applyRole, brandPageOf, groupByType, bulkAllowed } from "../_Shared/sourceEdit.js";
import { gs1Hint, VERIFIED_BY_GS1 } from "../_Shared/gs1Hint.js";
import { mountProductQuickFill } from "./productQuickFill.js";
import { mountShadeFinder } from "./productShades.js";
import { brandFor } from "../_Shared/smartAdd.js";
import {
    barcodeDigits, originEntry, originsAfterFill, uncheckedFields, markChecked, cleanOrigins, similarProducts, similarBrands,
    reviewReason, skippedLine, repeatedNames, FILL_FIELDS, STATE_WORDS, METHODS, DETECTED, NEEDS_REVIEW, VERIFIED, TYPED, REVIEW_CHANGES, waiting
} from "../_Shared/capture.js";
import { loadCatalogue } from "../_Shared/quickFill.js";

const OTHER_TYPE = "__other__";
let paintCounters = () => { };              // character counters under the form fields (charLimit.js)
const NO_BRAND = "__none__";
const NEW_BRAND = "__newbrand__";          // Quick fill found a brand that is not listed: it is added when the product is saved
let pendingBrandName = "";
let quickFill = { reset() {} };
// where each value of the open product form came from, and whether a person has checked it (capture.js); saved as fieldOrigins
let fieldOrigins = {};
// the ingredient statement exactly as the source or the photo gave it (kept as evidence; the list itself is what the app uses)
let ingredientsRaw = { text: "", from: "" };
const ORIGIN_INPUTS = {
    name: ["productNameInput"], brand: ["productBrandInput"], cosmeticType: ["productTypeInput", "productTypeOtherInput"],
    imageUrl: ["productImageInput"], barcode: ["productBarcodeInput"], fdaNumber: ["productFdaInput"], ingredients: ["productIngredientsInput"],
    shades: ["productShadeEditor"]
};
// the shade's NAME from a source (Makeup API), by colour; saved as shadeNames next to shades (a name is not a printed code)
let shadeNameByHex = {};
const MAX_SHADES = 100;
const SHADE_STRIP_FROM = 11;
const MAX_TAGS = 10;
const BATCH_LIMIT = 400;
const BRANDS_PER_PAGE = 10;
const PRODUCTS_PER_PAGE = 10;
const PRODUCTS_AT_A_TIME = 10;
const SHOWN_INGREDIENTS = 16;
const VIEW_KEY = "glowup.inventory.view.v1";


const AUTO_ADD_FDA_CATALOGUE = false;
let catalogueChecked = false;
let inventoryLiveStops = [];
let inventoryLiveReady = new Set();
let inventoryLiveRefresh = false;


const AUTO_APPROVE_FDA = true;

const fdaVerified = (p) => /FDA Philippines: notified as/i.test(p.description) && !/Notification ended/i.test(p.description);
const pageState = { page: 1, size: BRANDS_PER_PAGE };
const flatPage = { page: 1, size: PRODUCTS_PER_PAGE };
const binPage = { page: 1, size: PRODUCTS_PER_PAGE };
const closed = new Set();
const moreShown = new Map();
let shownGroups = [];




let drill = null;
let keepDrill = false;
const brandPage = { page: 1, size: PRODUCTS_PER_PAGE };
let sourceEditor = null;

let everyBrand = [];
let everyProduct = [];
let ingredientSuggestions = [];
let ingredientSuggestionsError = "";
let brands = [];
let products = [];
let binBrands = [];
let binProducts = [];
let curatedKeys = new Set();
let curatedList = [];
let ingredientPicker = null;
let curatorKnown = false;
let editingBrandId = null;
let editingProductId = null;
let formShades = [];
let formShadeCodes = [];
let assistedProductSources = [];
let brandModal = null;
let productModal = null;
let brandWindow = { reset() {} };
let productWindow = { reset() {} };
let org = null;
let openId = "";
let quick = "";

const FACTS = [
    { value: "has-shades", label: "Has shades" },
    { value: "no-ingredient-list", label: "No ingredient list" },
    { value: "fda-listed", label: "On the FDA Philippines list" }
];
let facets = { type: "", status: "", facts: [] };
const facetCount = () => (facets.type ? 1 : 0) + (facets.status ? 1 : 0) + facets.facts.length;
const facetOk = (p) => (!facets.type || sameKey(p.cosmeticType) === sameKey(facets.type)) && (!facets.status || p.status === facets.status)
    && facets.facts.every((fact) => productTags(p).includes(fact));
const view = { mode: "brands", sort: "name", layout: "rows" };

const el = (id) => document.getElementById(id);
const DASH = `<span class="muted">–</span>`;
const byName = (a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: "base" });
const many = (n, one, more) => `${n} ${n === 1 ? one : more}`;


const sure = (question, confirmLabel, danger) => {
    const parts = question.split("\n\n");
    return askConfirm({ title: parts[0], message: parts.slice(1).join(" "), confirmLabel, danger });
};


const SORTS = [
    { id: "name", label: "Name A to Z", short: "A to Z", cmp: byName },
    { id: "nameDesc", label: "Name Z to A", short: "Z to A", cmp: (a, b) => byName(b, a) },
    { id: "scans", label: "Most scanned", short: "most scanned", cmp: (a, b) => b.scans - a.scans || byName(a, b) },
    { id: "new", label: "Newest first", short: "newest", cmp: (a, b) => b.addedAt - a.addedAt || byName(a, b) },
    { id: "type", label: "Type", short: "type", cmp: (a, b) => a.cosmeticType.localeCompare(b.cosmeticType, undefined, { sensitivity: "base" }) || byName(a, b) },
    { id: "attention", label: "Needs attention first", short: "needs attention", cmp: (a, b) => weightOf(b) - weightOf(a) || byName(a, b) }
];
const sortNow = () => SORTS.find((s) => s.id === view.sort) || SORTS[0];


const QUICK = {
    noIngredients: { label: "No ingredient list", test: (p) => p.ingredients.length === 0 },
    noShades: { label: "Make-up without shades", test: (p) => p.shades.length === 0 && productScope(p.cosmeticType, p.name) === "makeup" },
    notCurated: { label: "Ingredients not in the Curator", test: (p) => notCurated(p.ingredients).length > 0 },
    fda: { label: "On the FDA Philippines list", test: (p) => p.source === "fdaph" || /FDA Philippines: notified as/i.test(p.description) }
};

document.addEventListener("DOMContentLoaded", async () => {
    try { const saved = JSON.parse(localStorage.getItem(VIEW_KEY) || "{}"); if (saved.mode === "all") view.mode = "all"; if (SORTS.some((s) => s.id === saved.sort)) view.sort = saved.sort; } catch (error) {                   }
    registerData();
    mountShell("inventory");
    paintCounters = attachCounters(["brandNameInput", "brandMarketInput", "productNameInput", "productTypeOtherInput", "productDescInput"]);
    placeInHeader(".search-container", "headerActions");
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
    brandWindow = wireWindow(el("addBrandModal"));
    productWindow = wireWindow(el("productModal"));

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
            { label: "Approve All", className: "bar-quiet", tip: "Approve only eligible selected entries after you confirm you have reviewed them.", run: approveSelectedInventory },
            { label: "Apply website or source", className: "bar-quiet", tip: "Give every selected product a web address, also those on other pages. You see how many change and confirm first.", icon: icon("layers"), run: applyToSelected }
        ],

        pickScope: () => (drill && drill.kind === "brand" && org.view.kind !== "bin" ? productsOf(drill.id).map((p) => "p:" + p.id) : null),
        onView: () => { if (!keepDrill) drill = null; fromFirstPage(); }
    });
    setAssistPage("inventory");
    registerHelpers();

    const askedFor = new URLSearchParams(window.location.search).get("q");
    if (askedFor) el("inventorySearchInput").value = askedFor;
    wireSearchExtras();
    wireSearch(el("inventorySearchInput"), el("inventorySearchClear"), fromFirstPage);


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
        { label: "Brand", icon: "tag", description: "A new brand: name, category, market, status.", keys: comboFor("inventory.addBrand"), run: () => openBrandForm(null) },
        { label: "Product", icon: "lipstick", description: "A make-up product of a listed brand.", keys: comboFor("inventory.addProduct"), disabled: brands.length === 0, run: () => openProductForm(null, brandFilterValue()) }
    ]));

    bindButtons({ "inventory.new": "#addMenuBtn", "inventory.import": "#chooseInventoryFileBtn", "inventory.filters": "#filterMenuBtn",
        "inventory.sort": "#sortMenuBtn" });
    bindActions({
        "inventory.addBrand": { run: () => openBrandForm(null) },
        "inventory.addProduct": { run: () => openProductForm(null, brandFilterValue()), isAvailable: () => (brands.length === 0 ? "Add product: add a brand first. A product needs a listed brand." : true) }
    });

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

    el("inventoryLayoutSwitch").addEventListener("click", (event) => {
        const button = event.target.closest("[data-layout]");
        if (!button) return;
        view.layout = button.dataset.layout === "cards" ? "cards" : "rows";
        saveView();
        paintLayout();
    });
    paintLayout();



    if (window.ResizeObserver) new ResizeObserver(() => {
        const width = el("inventoryHost").clientWidth;
        if (width === 0) return;
        if (document.documentElement.classList.contains("qs-open") && window.innerWidth >= 1024) return;
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
    el("productBarcodeInput").addEventListener("input", showBarcodeCheck);
    el("productNameInput").addEventListener("input", showScopeHint);
    el("productNameInput").addEventListener("input", showNameClash);
    el("productBrandInput").addEventListener("change", showNameClash);
    el("productBarcodeInput").addEventListener("input", showNameClash);
    el("productForm").addEventListener("input", saveDraft);
    el("productForm").addEventListener("change", saveDraft);
    el("productForm").addEventListener("input", onOriginEdit);
    el("productForm").addEventListener("change", onOriginEdit);
    el("productForm").addEventListener("click", onOriginClick);
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
    quickFill = mountProductQuickFill({
        brands: () => everyBrand, products: () => everyProduct,
        readForm: readProductForm,
        fill: fillProductFromQuick,
        noteOrigins: (rows, method, score) => { fieldOrigins = originsAfterFill(fieldOrigins, rows, method, score); paintOrigins(); saveDraft(); },
        noteRaw: (text, from) => { ingredientsRaw = { text: String(text || "").slice(0, 4000), from: String(from || "").slice(0, 120) }; paintRawIngredients(); saveDraft(); },
        openExisting: (product) => { productModal.close(); openProductForm(product); }
    });
    mountShadeFinder({
        button: el("productShadeFind"), status: el("productShadeFindStatus"),
        readForm: readProductForm, room: () => MAX_SHADES - formShades.length,
        addShades: (list, fromProduct) => {
            list.forEach((s) => { formShades.push(s.hex); formShadeCodes.push(s.code || ""); if (s.name) shadeNameByHex[s.hex.toUpperCase()] = s.name; });
            fieldOrigins = { ...fieldOrigins, shades: originEntry(NEEDS_REVIEW, `Makeup API: ${fromProduct}`.slice(0, 80), "lookup") };
            renderShadeEditor(); paintOrigins(); saveDraft();
        }
    });
    el("productShadeEditor").addEventListener("click", onShadeClick);
    el("productShadeEditor").addEventListener("input", onShadeInput);
    el("productShadeEditor").addEventListener("change", onShadeChange);
    el("productShadeEditor").addEventListener("keydown", onShadeKey);


    onListsRefreshed(() => { if (!dialogIsOpen()) loadInventory(); });
    const firstLoad = loadInventory();
    firstLoad.then(startInventoryLive);
    window.addEventListener("pagehide", stopInventoryLive);
    window.addEventListener("pageshow", (event) => { if (event.persisted) startInventoryLive(); });


    if (new URLSearchParams(location.search).get("add") === "product") firstLoad.then(() => openProductForm(null));
});

// Keep every inventory stat card tied to live Firestore data. The table is refreshed from
// the same snapshots' change notifications, so another admin's edit appears without a manual refresh.
function stopInventoryLive() {
    inventoryLiveStops.forEach((stop) => stop());
    inventoryLiveStops = [];
    inventoryLiveReady = new Set();
}

function startInventoryLive() {
    stopInventoryLive();
    const watch = (name) => inventoryLiveStops.push(onSnapshot(collection(db, name), () => {
        inventoryLiveReady.add(name);
        if (inventoryLiveReady.size < 4 || inventoryLiveRefresh || dialogIsOpen()) return;
        inventoryLiveRefresh = true;
        loadInventory().finally(() => { inventoryLiveRefresh = false; });
    }, (error) => console.error(`Error watching ${name} for inventory cards: `, error)));
    ["brands", "products", "ingredients", "productIngredientSuggestions"].forEach(watch);
}

const saveView = () => { try { localStorage.setItem(VIEW_KEY, JSON.stringify(view)); } catch (error) {                      } };
function fromFirstPage() { pageState.page = 1; flatPage.page = 1; binPage.page = 1; brandPage.page = 1; renderInventory(); }


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


function applyToSelected(keys) {
    const ids = keys.filter((key) => key.startsWith("p:")).map((key) => key.slice(2));
    const first = products.find((p) => ids.includes(p.id));
    if (!first) { notify("Select at least one product.", "error"); return; }
    const brand = brands.find((b) => b.id === first.brandId);
    if (!brand || !bulkAllowed(brand.name)) { notify("Applying to other products is not switched on for this brand yet.", "error"); return; }
    openApply({ role: "primary", url: "", brandId: first.brandId, chosen: ids });
}

function showOnly(value, keep) {
    const select = el("reviewFilterSelect");
    select.value = select.value === value && !keep ? "" : value;
    quick = "";
    drill = null;
    if (org.view.kind === "bin") org.show({ kind: "all" }); else fromFirstPage();
}

function resetAllFilters() {
    facets = { type: "", status: "", facts: [] };
    el("inventorySearchInput").value = "";
    el("brandFilterSelect").value = "";
    el("reviewFilterSelect").value = "";
    quick = "";
    org.show({ kind: "all" });
}





const madeAt = (data) => { const made = toDate(data.createdAt); return made ? Math.floor(made.getTime() / 1000) : toWholeNumber(data.addedAtUnix); };

const binFields = (data) => { const since = toDate(data.binnedAt); return { binned: data.binned === true, binnedAt: since ? since.getTime() : 0, statusBeforeBin: String(data.statusBeforeBin || ""), binnedWith: String(data.binnedWith || ""), binnedReason: String(data.binnedReason || "") }; };

function renderIngredientSuggestions() {
    datasetChanged("ingredientSuggestions");
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
                rawStatus: String(data.status || ""),
                scans: toWholeNumber(data.scans),
                source: String(data.source || "").trim().toLowerCase(),
                reviewed: data.reviewed === true,
                needsReview: data.reviewed === false,
                autoApproved: data.autoApproved === true,
                aliases: asArray(data.aliases),
                logoUrl: String(data.logoUrl || ""),


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
                ingredientNotes: Array.isArray(data.ingredientNotes) ? data.ingredientNotes : [],
                fieldOrigins: cleanOrigins(data.fieldOrigins),
                shadeNameByHex: (Array.isArray(data.shades) ? data.shades : []).reduce((map, hex, at) => {
                    const name = Array.isArray(data.shadeNames) ? String(data.shadeNames[at] || "").slice(0, 60) : "";
                    if (name && typeof hex === "string") map[hex.toUpperCase()] = name;
                    return map;
                }, {}),
                ingredientsRaw: String(data.ingredientsRaw || "").slice(0, 4000),
                ingredientsRawFrom: String(data.ingredientsRawFrom || "").slice(0, 120),
                reviewState: String(data.reviewState || ""),
                captureMethod: String(data.captureMethod || ""),
                evidenceCount: toWholeNumber(data.evidenceCount),
                possibleDuplicateOf: String(data.possibleDuplicateOf || ""),
                addedBy: String(data.addedBy || ""),
                reviewNote: String(data.reviewNote || "").slice(0, 500),
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


    curatedKeys = new Set();
    curatedList = [];
    try {
        const ingredientSnapshot = await getDocs(collection(db, "ingredients"));
        ingredientSnapshot.forEach((d) => {
            const data = d.data();
            if (data.binned === true) return;
            [data.name, data.techName, ...asArray(data.aliases)].forEach((n) => { const k = sameKey(n); if (k) curatedKeys.add(k); });
            if (data.name) curatedList.push({
                id: d.id, name: String(data.name), techName: String(data.techName || ""), aliases: asArray(data.aliases),
                riskLevel: String(data.riskLevel || ""), source: String(data.source || "").trim().toLowerCase(),
                reviewed: data.reviewed === true, generic: data.generic === true
            });
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
                console.warn("Automatic approval did not work: ", error);
            }
        }

    }

    sortOut();
    fillBrandSelects();
    fillReviewFilter();
    renderInventory();
    datasetChanged("products");
    datasetChanged("brands");



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

    if (curatorKnown) {
        try {
            await refreshRecommendabilityFlags({ brands: everyBrand, products: everyProduct, ingredients: curatedList });
        } catch (error) {
            console.error("Could not refresh recommendation eligibility: ", error);
            notify("Recommendation eligibility could not be refreshed. " + describeError(error), "error");
        }
    }

    // the FDA Philippines catalogue is applied by itself, once per visit (what is listed already is left alone)
    if (AUTO_ADD_FDA_CATALOGUE && !catalogueChecked) { catalogueChecked = true; importFdaCatalogue(true); }
}


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


function afterChange() {
    sortOut();
    fillBrandSelects();
    fillReviewFilter();
    renderInventory();
    datasetChanged("products");
    datasetChanged("brands");
}


function fillReviewFilter() {
    const select = el("reviewFilterSelect");
    const asked = new URLSearchParams(window.location.search).get("show");
    const wanted = select.dataset.ready ? select.value : (asked === "review" || asked === "out" ? asked : "");
    select.dataset.ready = "1";
    select.innerHTML = `<option value="">Everything</option><option value="review">Waiting for review (${reviewCount()})</option><option value="out">Not make-up (${outsideCount()})</option>`;
    select.value = wanted;
}


const websiteOfEntry = (entry) => ("brandId" in entry ? (brandOf(entry) || {}).website : entry.website) || "";


function originText(entry) {
    const origin = originLabel(entry.source) || (entry.needsReview ? "Added automatically" : "");
    if (!origin) return "";
    const links = sourceLinks(entry.sources || [], websiteOfEntry(entry));
    const state = entry.needsReview ? " · needs review" : entry.reviewed ? " · reviewed" : "";
    return escapeHtml(origin + state) + (links ? " · " + links : "");
}


const FLAT_LIST = "__all_products";

const brandFilterValue = () => { const value = el("brandFilterSelect").value; return value === FLAT_LIST ? "" : value; };

function setBrandChoice(value) {
    const select = el("brandFilterSelect");
    select.value = value;
    select.dispatchEvent(new Event("change"));
}

function fillBrandSelects() {
    const filter = el("brandFilterSelect");

    const wanted = filter.dataset.ready ? filter.value : (new URLSearchParams(window.location.search).get("brand") || "");
    filter.dataset.ready = "1";
    const option = (b) => `<option value="${escapeHtml(b.id)}">${escapeHtml(b.name || "Unnamed Brand")}${b.status === "Active" ? "" : " (" + escapeHtml(b.status) + ")"}</option>`;
    const offered = brands.filter((b) => !brandOut(b));
    filter.innerHTML = `<option value="">All brands, grouped</option><option value="${FLAT_LIST}">All products, one list</option>` + offered.map(option).join("");
    filter.value = offered.some((b) => b.id === wanted) ? wanted : view.mode === "all" ? FLAT_LIST : "";
    el("productBrandInput").innerHTML = `<option value="">Choose a brand</option>` + brands.map(option).join("");
}

const IMPORT_GROUP = 40;

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


async function extractInventory(documentText, say, signal) {

    const keyed = parseImportText(documentText);
    if (keyed.kind === "ingredients") return { refused: "This file lists only ingredients. " + ingredientsNotHere(keyed.draft.ingredients.length) };
    if (keyed.kind === "mixed") return { refused: `This file mixes ${many(keyed.counts.inventory, "brand or product record", "brand or product records")} with ingredient records, so nothing was imported. ` + ingredientsNotHere(keyed.counts.ingredients) };
    if (keyed.kind === "inventory") { say("Reading the file's own fields. No AI is used."); return { draft: keyed.draft, notes: keyed.notes.slice(0, 6), model: "" }; }

    const labeled = parseLabeledProducts(documentText);
    if (labeled.kind === "inventory") { say("Reading the labelled Brand / Product / Ingredients lines. No AI is used."); return { draft: labeled.draft, notes: labeled.notes.slice(0, 6), model: "" }; }

    return { refused: "This file is not in the GlowUp import format, so nothing was imported. Use the import template or labelled Brand / Product / Ingredients lines." };
}


async function saveInventoryDraft(draft, fileName, say) {
    const records = Array.isArray(draft.products) ? draft.products : [], brandRecords = Array.isArray(draft.brands) ? draft.brands : [];

    const memory = { newBrands: new Map(), plannedProducts: new Set() };
    const result = { savedProducts: 0, savedBrands: 0, suggestedLists: 0, skippedDuplicates: 0, skippedInvalid: 0, withFdaLink: 0, flagged: [] };
    const groups = Math.max(Math.ceil(records.length / IMPORT_GROUP), Math.ceil(brandRecords.length / IMPORT_GROUP), 1);
    for (let g = 0; g < groups; g++) {
        if (groups > 1) say(`Saving group ${g + 1} of ${groups}...`);
        const part = await saveImportedProducts(records.slice(g * IMPORT_GROUP, (g + 1) * IMPORT_GROUP), brandRecords.slice(g * IMPORT_GROUP, (g + 1) * IMPORT_GROUP), fileName, memory, g * IMPORT_GROUP);
        ["savedProducts", "savedBrands", "suggestedLists", "skippedDuplicates", "skippedInvalid", "withFdaLink"].forEach((k) => { result[k] += part[k]; });
        result.flagged.push(...part.flagged);
        (result.notSaved = result.notSaved || []).push(...part.notSaved);
    }
    showImportFlags(el("inventoryImportFlags"), result.flagged, result.notSaved || []);
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





function showImportFlags(box, flagged, notSaved = []) {
    if (!box) return;
    box.hidden = flagged.length === 0 && notSaved.length === 0;
    if (box.hidden) { box.innerHTML = ""; return; }
    const out = flagged.filter((f) => !f.saved), unclear = flagged.filter((f) => f.saved);
    const line = (f) => `<li><b>${escapeHtml(f.name)}</b>${f.brandName ? " (" + escapeHtml(f.brandName) + ")" : ""}${f.cosmeticType ? ", " + escapeHtml(f.cosmeticType) : ""}: ${escapeHtml(f.why)}</li>`;
    box.innerHTML = (out.length ? `<span class="chip warn">Flagged: skin care or not make-up</span> Not saved. GlowUp lists make-up only.<ul>${out.map(line).join("")}</ul>` : "")
        + (unclear.length ? `<span class="chip review">Flagged: kind not clear</span> Saved as Pending; open each one and choose its make-up kind, or move it to the Recycle bin.<ul>${unclear.map(line).join("")}</ul>` : "")
        + (notSaved.length ? `<span class="chip">Rows not saved or changed</span> Every row of the file is accounted for: fix the file and import it again (rows already saved are skipped, nothing is saved twice).<ul>${notSaved.slice(0, 60).map((n) => `<li>${escapeHtml(skippedLine(n))}</li>`).join("")}${notSaved.length > 60 ? `<li>and ${notSaved.length - 60} more</li>` : ""}</ul>` : "");
}






async function saveImportedProducts(records, brandRecords, fileName, memory, firstRow = 0) {
    const batch = writeBatch(db);
    const kept = memory || { newBrands: new Map(), plannedProducts: new Set() };
    const newBrands = new Map();
    const plannedProducts = kept.plannedProducts;
    const flagged = [], notSaved = [];
    const uid = auth.currentUser ? auth.currentUser.uid : "";
    const plannedCodes = kept.plannedCodes || (kept.plannedCodes = new Map());
    const fromFile = `File: ${String(fileName || "").trim().slice(0, 60)}`;
    let savedProducts = 0, skippedDuplicates = 0, skippedInvalid = 0, withFdaLink = 0, suggestedLists = 0, writes = 0;
    const clean = (value, most) => String(value || "").replace(/\s+/g, " ").trim().slice(0, most);
    const names = (list, most, count) => Array.from(new Map((Array.isArray(list) ? list : []).map((v) => clean(v, most)).filter(Boolean).map((v) => [sameKey(v), v])).values()).slice(0, count);
    const knownBrand = (name) => everyBrand.find((item) => [item.name, ...item.aliases].some((known) => sameKey(known) === sameKey(name)));


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

    records.slice(0, IMPORT_GROUP).forEach((record, at) => {
        const brandName = clean(record.brandName, 60);
        const name = clean(record.productName, 100);
        const cosmeticType = clean(record.cosmeticType, 60);
        const row = firstRow + at + 1;
        if (brandName.length < 2 || !name) { skippedInvalid++; notSaved.push({ row, name, brandName, why: !name ? "no product name" : "no brand name (at least 2 letters)" }); return; }
        const scope = productScope(cosmeticType, name);

        if (scope === "out" || scope === "skincare") {
            flagged.push({ name, brandName, cosmeticType, saved: false, why: scope === "skincare" ? `skin care (${scopeWords(cosmeticType, name)})` : `not make-up (${scopeWords(cosmeticType, name)})` });
            return;
        }

        let brand = knownBrand(brandName);
        if (brand && brand.binned) { skippedDuplicates++; notSaved.push({ row, name, brandName, why: `its brand "${brand.name}" is in the Recycle bin` }); return; }
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

            const queued = existing && ingredientSuggestions.some((s) => s.productId === existing.id);
            if (existing && !existing.binned && existing.ingredients.length === 0 && ingredients.length >= 3 && !queued) {
                batch.set(doc(collection(db, "productIngredientSuggestions")), {
                    productId: existing.id, productName: existing.name, brandName: brand.name, ingredients,
                    sources: sources.length ? sources : [`Imported document: ${clean(fileName, 80)}`],
                    source: "document-import", status: "Pending", reviewed: false, addedBy: uid, addedAtUnix: Math.floor(Date.now() / 1000)
                });
                writes++;
                suggestedLists++;
                notSaved.push({ row, name, brandName, why: "already listed without ingredients: its list went to Web ingredient lists to review" });
            } else { skippedDuplicates++; notSaved.push({ row, name, brandName, why: existing ? `already listed as "${existing.name}"` : "twice in this file" }); }
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
        const barcode = barcodeDigits(record.barcode || "");
        if (barcode) {
            const owner = everyProduct.find((p) => !p.binned && barcodeKey(p.barcode) === barcodeKey(barcode)) || plannedCodes.get(barcodeKey(barcode));
            if (owner) notSaved.push({ row, name, brandName, why: `saved WITHOUT its barcode ${barcode}: that barcode is already on "${owner.name}"` });
            else { data.barcode = barcode; plannedCodes.set(barcodeKey(barcode), { name }); }
        } else if (String(record.barcode || "").trim()) notSaved.push({ row, name, brandName, why: `saved without its barcode: "${clean(record.barcode, 20)}" is not 6 to 14 digits` });
        // every imported value is Detected (from the file) until an admin checks it
        data.fieldOrigins = {};
        ["name", "brand", "cosmeticType", "imageUrl", "barcode", "fdaNumber", "ingredients"].forEach((key) => {
            const value = key === "brand" ? brand.name : data[key];
            if (Array.isArray(value) ? value.length : value) data.fieldOrigins[key] = originEntry(DETECTED, fromFile, "import");
        });
        const imageUrl = cleanLink(record.imageUrl);
        if (imageUrl) data.imageUrl = imageUrl;
        batch.set(doc(collection(db, "products")), data);
        writes++;
        savedProducts++;
    });


    (Array.isArray(brandRecords) ? brandRecords : []).slice(0, IMPORT_GROUP).forEach((record) => {
        const name = clean(record.name, 60);
        if (name.length < 2) { skippedInvalid++; return; }
        if (knownBrand(name) || kept.newBrands.has(sameKey(name))) { skippedDuplicates++; return; }
        addBrand(name, record);
    });

    const result = { savedProducts, savedBrands: newBrands.size, suggestedLists, skippedDuplicates, skippedInvalid, withFdaLink, flagged, notSaved };
    if (writes === 0) return Object.assign(result, { savedBrands: 0 });
    await batch.commit();
    return result;
}

const productsOf = (brandId) => products.filter((p) => p.brandId === brandId);

const isOut = (product) => notMakeup(product.cosmeticType, product.name);

const isUnclear = (product) => productScope(product.cosmeticType, product.name) === "unclear";

const brandOut = (brand) => categoryOutOfScope(brand.category) && productsOf(brand.id).every(isOut);
const hasBrand = (product) => brands.some((b) => b.id === product.brandId);
const brandOf = (product) => brands.find((b) => b.id === product.brandId) || null;

const listed = () => products.filter((p) => !isOut(p));
const outsideCount = () => products.filter(isOut).length + brands.filter(brandOut).length;

const waits = (p) => p.needsReview || isUnclear(p);
const reviewCount = () => brands.filter((b) => b.needsReview && !brandOut(b)).length + listed().filter(waits).length;

const itemOf = (key) => (key.startsWith("b:") ? everyBrand.find((b) => b.id === key.slice(2)) : everyProduct.find((p) => p.id === key.slice(2))) || null;



function cleanShade(value) {
    let text = String(value || "").trim().toUpperCase();
    const rgb = text.match(/^RGB\(\s*(\d{1,3})\s*[, ]\s*(\d{1,3})\s*[, ]\s*(\d{1,3})\s*\)$/);
    if (rgb) return [1, 2, 3].every((i) => Number(rgb[i]) <= 255) ? "#" + [1, 2, 3].map((i) => Number(rgb[i]).toString(16).padStart(2, "0")).join("").toUpperCase() : null;

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


function notCurated(ingredientNames) {
    return curatorKnown ? ingredientNames.filter((n) => !curatedKeys.has(sameKey(n))) : [];
}






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

function weightOf(p) {
    return (isOut(p) ? 8 : 0) + (waits(p) ? 4 : 0) + (p.ingredients.length === 0 ? 2 : 0) + (brandOf(p) && brandOf(p).status === "Active" ? 0 : 1);
}





const focusBrand = () => brands.find((b) => b.id === brandFilterValue()) || null;
const focusFolder = () => (org && org.view.kind === "folder" ? org.view.name : "");

const inFocus = (p) => { const b = focusBrand(), f = focusFolder(); return (!b || p.brandId === b.id) && (!f || sameKey(p.folder) === sameKey(f)); };

function paintStats() {
    const set = (id, value) => { const box = el(id); box.classList.remove("skeleton"); box.textContent = value; };
    const brand = focusBrand(), folder = focusFolder();
    const mine = listed().filter(inFocus);

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

    const select = el("brandFilterSelect");
    if (select.value === "" && view.mode === "all") select.value = FLAT_LIST;
    if (select.value === FLAT_LIST && view.mode !== "all") select.value = "";

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

    const focus = (focusBrand() ? focusBrand().id : "") + "|" + focusFolder();
    if (focus !== lastFocus) { lastFocus = focus; datasetChanged("products"); datasetChanged("brands"); }
}
let lastFocus = "|";

const pickBox = (label) => `<button type="button" class="pick" role="checkbox" aria-checked="false" aria-label="Select ${escapeHtml(label)}"></button>`;
const pickAllBox = (what) => `<button type="button" class="pick" data-pick-all role="checkbox" aria-checked="false" aria-label="Select all shown" data-tip="Select every ${what} shown here."></button>`;


function showBarcodeCheck() {
    const note = el("productBarcodeCheck");
    if (!note) return;
    const text = el("productBarcodeInput").value.trim();
    note.textContent = text ? (barcodeKey(text) ? gs1Hint(text).text : "Not a barcode yet: 6 to 14 digits.") : "";
}


const NARROW_LIST_PX = 600;
let listIsNarrow = false;


function paintLayout() {

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






const SUGGEST_MAX = 5;
let suggestItems = [];
let suggestAt = -1;
let searchCount = null;
let nextPush = false;


const productTexts = (p) => [p.name, ...p.aliases, p.cosmeticType, p.description, p.barcode, (brandOf(p) || {}).name || p.brandName];


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


function containsNote(p) {
    const typed = el("inventorySearchInput").value;
    if (!sameKey(typed) || matchesSearch(typed, productTexts(p))) return "";
    const found = ingredientFor(p, typed);
    return found ? `<span class="contains-note">contains: ${escapeHtml(found)}</span>` : "";
}


function syncSearchUrl() {
    const url = new URL(window.location.href), typed = el("inventorySearchInput").value.trim();
    if (typed) url.searchParams.set("q", typed); else url.searchParams.delete("q");
    const push = nextPush;
    nextPush = false;
    if (url.href === window.location.href) return;
    try { window.history[push ? "pushState" : "replaceState"](null, "", url); } catch (error) {                                                             }
}


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


function openSuggest() {
    const input = el("inventorySearchInput"), box = el("inventorySuggest"), typed = input.value.trim();
    if (sameKey(typed).length < 2) { closeSuggest(); return; }
    const listed = products.filter((p) => !isOut(p));
    const foundBrands = brands.filter((b) => matchesSearch(typed, [b.name, ...b.aliases])).slice(0, SUGGEST_MAX)
        .map((b) => ({ kind: "brand", group: "Brands", text: b.name, brand: b }));
    const foundProducts = listed.filter((p) => matchesSearch(typed, [p.name, ...p.aliases, p.cosmeticType, (brandOf(p) || {}).name || p.brandName])).slice(0, SUGGEST_MAX)
        .map((p) => ({ kind: "product", group: "Products", text: p.name || "Unnamed Product", note: (brandOf(p) || {}).name || p.brandName || "", product: p }));
    const byIngredient = new Map();
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
            row.addEventListener("mousedown", (event) => event.preventDefault());
            row.addEventListener("click", () => chooseSuggestion(at));
            box.appendChild(row);
        });
    }
    box.hidden = false;
    input.setAttribute("aria-expanded", "true");
}


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
            if (open) { event.stopImmediatePropagation(); closeSuggest(); }
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

    document.addEventListener("keydown", (event) => {
        if (event.key !== "/" || event.ctrlKey || event.metaKey || event.altKey || event.defaultPrevented) return;
        const target = event.target;
        if (target && target.closest && target.closest("input, textarea, select, [contenteditable]:not([contenteditable=false])")) return;
        if (dialogIsOpen()) return;
        event.preventDefault();
        input.focus();
        input.select();
    });

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
    const term = sameKey(typed);
    const brandFilter = brandFilterValue();
    const show = el("reviewFilterSelect").value;
    const folder = now.kind === "folder" ? now.name : "";
    const flat = view.mode === "all";
    const columns = flat ? 7 : 6;
    el("inventoryTableHead").innerHTML = `<tr><th><span class="th-line">${pickAllBox("product")}Product</span></th>${flat ? "<th>Brand</th>" : ""}<th>Type</th><th class="shade-col">Shades</th><th class="text-right scan-col" data-tip="Scans the app recognised as this product.">Scans</th><th><span class="th-line">Status${statusGuide()}</span></th><th></th></tr>`;

    if (everyBrand.length === 0 && everyProduct.length === 0) {
        shownGroups = [];
        body.innerHTML = `<tr><td colspan="${columns}" class="table-message">No brands yet. Add the first brand, then its products.</td></tr>`;
        el("inventoryCountLine").textContent = "";
        setPagers(pageState, 0, "brands");
        return;
    }

    const base = (p) => (show === "out" ? isOut(p) : !isOut(p)) && (!folder || sameKey(p.folder) === sameKey(folder))
        && (show !== "review" || waits(p)) && (!quick || QUICK[quick].test(p)) && facetOk(p);


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
        const groups = [];
        sortedBrands().forEach((brand) => {
            if (brandFilter && brand.id !== brandFilter) return;
            const out = brandOut(brand);
            if (show === "out" ? false : out) return;
            const mine = productsOf(brand.id);
            const hit = !term || brandMatches(brand);
            const rows = mine.filter((p) => base(p) && (hit || productMatches(p))).sort(order);

            const belongs = show === "out" ? out : show === "review" ? brand.needsReview : !folder && !quick;
            if (rows.length === 0 && !(belongs && hit)) return;
            const lines = rows.map((p) => productRow(p, brand, false));
            if (mine.length === 0) lines.push(`<tr class="note-row" data-brand="${escapeHtml(brand.id)}"><td colspan="${columns}" class="muted small">No products yet. Point at the brand and press +.</td></tr>`);
            productsShown += rows.length;
            groups.push({ id: brand.id, head: brandRow(brand, mine.length, columns), rows: lines });
        });


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
            const folded = !term && closed.has(group.id);
            html.push(folded ? group.head.replace('class="brand-row', 'class="brand-row closed') : group.head);
            if (folded) return;
            const limit = moreShown.get(group.id) || PRODUCTS_AT_A_TIME;
            group.rows.slice(0, limit).forEach((row) => html.push(row));
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


function paintBrandPageSize() {
    const select = el("brandPageSize");
    const count = drill && drill.kind === "brand" && org.view.kind !== "bin" ? productsOf(drill.id).filter((p) => !isOut(p)).length : 0;
    select.hidden = !brandPageOf(new Array(count).fill({}), 1, brandPage.size).showSizes;
    select.value = String(brandPage.size);
}


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
        head.innerHTML = `<tr><th><span class="th-line"><button type="button" class="pick" data-pick-all role="checkbox" aria-checked="false" aria-label="Select all ${whole} products in this brand" data-tip="Select all ${whole} products of this brand, also those on other pages."></button>Product <small class="muted">select all in brand: ${whole}</small></span></th><th>Type</th><th class="shade-col">Shades</th><th class="text-right scan-col">Scans</th><th>Status</th><th></th></tr>`;
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
    head.innerHTML = `<tr><th><span class="th-line">${pickAllBox("product")}Product</span></th><th>Brand</th><th>Type</th><th class="shade-col">Shades</th><th class="text-right scan-col">Scans</th><th>Status</th><th></th></tr>`;
    const groups = groupByType(products.filter((p) => base(p) && (!term || productMatches(p))));
    const total = groups.reduce((sum, group) => sum + group.rows.length, 0);
    el("inventoryCountLine").textContent = many(total, "product", "products");
    setPagers(flatPage, 0, "products");
    if (total === 0) { nothing(7, term ? `Nothing here matches "${escapeHtml(typed)}".` : "Nothing to show."); return; }
    body.innerHTML = groups.map((group) => `<tr class="type-row"><td colspan="7"><span class="type-name">${escapeHtml(group.label)}</span> <span class="muted small">${many(group.rows.length, "product", "products")}</span></td></tr>`
        + group.rows.map((p) => productRow(p, brandOf(p), true)).join("")).join("");
}


function sortedBrands() {
    const list = brands.slice();
    const needs = (b) => (b.needsReview ? 2 : 0) + (productsOf(b.id).some((p) => weightOf(p) > 0) ? 1 : 0);
    if (view.sort === "nameDesc") list.sort((a, b) => byName(b, a));
    else if (view.sort === "scans") list.sort((a, b) => b.scans - a.scans || byName(a, b));
    else if (view.sort === "new") list.sort((a, b) => b.addedAt - a.addedAt || byName(a, b));
    else if (view.sort === "attention") list.sort((a, b) => needs(b) - needs(a) || byName(a, b));
    return list;
}


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
    if (p.needsReview) buttons.push(actionButton("row-btn", "send-back-product", p.id, icon("edit"), "Send back", "Ask for corrections with a note. It stays Pending.", "It asks for the note first."));
    buttons.push(actionButton("row-btn danger", "bin-product", p.id, BIN_ICON, "Recycle bin", "Remove this product.", "It goes to the Recycle bin and can be restored."));

    return `
        <tr class="product-row${isOut(p) ? " out-row" : ""}${open ? " open" : ""}" data-product="${escapeHtml(p.id)}" data-key="p:${escapeHtml(p.id)}" data-drag tabindex="0" aria-expanded="${open}">
            <td class="name-cell"><div class="name-line">${pickBox(p.name || "product")}${toneDot(attentionOf(p, brand, !flat))}<span class="product-name">${escapeHtml(p.name || "Unnamed Product")}</span>${containsNote(p)}${p.needsReview && p.reviewState === REVIEW_CHANGES ? `<span class="chip warn">Sent back</span>` : p.needsReview && p.reviewState === "resubmitted" ? `<span class="chip review">Resubmitted</span>` : ""}${p.ingredients.length === 0 ? (`<span class="chip warn ingredient-missing">${icon("alert")}<span>No ingredient list</span></span>`) : ""}</div></td>
            ${flat ? `<td class="brand-ref">${escapeHtml(brand ? brand.name : (p.brandName || "–"))}</td>` : ""}
            <td class="type-cell">${escapeHtml(p.cosmeticType || "–")}</td>
            <td class="shade-cell">${shadeCell}</td>
            <td class="scan-count text-right">${p.scans}</td>
            <td class="status-cell">${productStatusCell(p, brand)}</td>
            <td class="text-right">${rowActions(buttons)}</td>
        </tr>` + (open ? detailRow(p, brand, flat ? 7 : 6) : "");
}





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
    const unchecked = uncheckedFields(p.fieldOrigins);
    if (unchecked.length) facts.push(["Not checked", `<span class="chip warn">${escapeHtml(unchecked.map((f) => `${f.label} (${STATE_WORDS[f.state].toLowerCase()}${f.from ? ", " + f.from : ""})`).join("; "))}</span>`]);
    if (p.reviewNote && p.reviewState) facts.push([p.reviewState === REVIEW_CHANGES ? "Sent back" : "Review note", escapeHtml(p.reviewNote)]);
    if (p.source === "staff-capture") {
        const how = { Barcode: "barcode scan", Photo: "product photos", Gallery: "gallery photos", Manual: "manual entry" }[p.captureMethod] || "the mobile app";
        const dup = p.possibleDuplicateOf ? everyProduct.find((x) => x.id === p.possibleDuplicateOf) : null;
        facts.push(["Staff capture", escapeHtml(`Captured by ${how}`) + (p.evidenceCount ? ` · <button type="button" class="text-link" data-act="capture-photos" data-id="${escapeHtml(p.id)}">Show ${many(p.evidenceCount, "photo", "photos")}</button>` : " · no photos attached")
            + (dup ? `<br><span class="chip warn">Possible duplicate of "${escapeHtml(dup.name)}": compare before approving</span>` : "")]);
    }

    const button = (cls, act, iconHtml, label, tip) => `<button type="button" class="row-btn ${cls}" data-act="${act}" data-id="${escapeHtml(p.id)}" data-tip="${escapeHtml(tip)}">${iconHtml}${label}</button>`;
    const buttons = [button("main", "edit-product", icon("edit"), "Edit", "Change the name, brand, type, ingredients, shades or status.")];
    const together = approvalFor(p, brand);
    if (together) buttons.push(button("approve", "approve-with-brand", icon("check"), together.label, together.tip));
    else if (p.needsReview && scope === "makeup") buttons.push(button("approve", "approve-product", icon("check"), "Approve", "You have checked it: set it to Active."));
    if (p.needsReview) buttons.push(button("", "send-back-product", icon("edit"), "Send back", "Ask for corrections with a note. It stays Pending."));
    buttons.push(button("", "folder-product", icon("folder"), "Folder", "Put it in a folder, or take it out."));
    buttons.push(`<span class="spacer"></span>`);
    if (p.needsReview) buttons.push(button("danger", "reject-product", BIN_ICON, "Reject", "Reject it with a reason. It goes to the Recycle bin."));
    else buttons.push(button("danger", "bin-product", BIN_ICON, "Recycle bin", "Remove it. It can be restored from the Recycle bin."));

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


async function runBatches(ops) {
    for (let i = 0; i < ops.length; i += BATCH_LIMIT) {
        const batch = writeBatch(db);
        ops.slice(i, i + BATCH_LIMIT).forEach((op) => { if (op.data) batch.update(op.ref, op.data); else batch.delete(op.ref); });
        await batch.commit();
    }
}

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


async function moveToFolder(keys, folder) {
    const { p, b } = split(keys);
    const wanted = new Map(p.filter((x) => !x.binned).map((x) => [x.id, x]));
    b.forEach((brand) => productsOf(brand.id).forEach((x) => wanted.set(x.id, x)));
    const list = Array.from(wanted.values()).filter((x) => sameKey(x.folder) !== sameKey(folder));
    if (list.length === 0) throw friendly(wanted.size === 0 ? "That brand has no products to move." : folder ? `Already in "${folder}".` : "Not in a folder.");
    const before = list.map((x) => ({ item: x, folder: x.folder }));
    const write = (changes) => saveOrReload(changes.map((c) => ({ ref: doc(db, "products", c.item.id), data: { folder: c.folder, updatedAt: serverTimestamp() } })));
    list.forEach((x) => { x.folder = folder; });
    afterChange();
    await write(list.map((x) => ({ item: x, folder })));
    return { text: named(list, "product", "products"), undo: async () => { before.forEach((c) => { c.item.folder = c.folder; }); afterChange(); await write(before); } };
}


async function refolder(from, to) {
    const list = everyProduct.filter((x) => sameKey(x.folder) === sameKey(from));
    list.forEach((x) => { x.folder = to; });
    afterChange();
    await saveOrReload(list.map((x) => ({ ref: doc(db, "products", x.id), data: { folder: to, updatedAt: serverTimestamp() } })));
}


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


async function purgeFromBin(keys) {
    const { p, b } = split(keys);
    const refs = new Map(), names = [];
    b.filter((brand) => brand.binned).forEach((brand) => {
        const riders = everyProduct.filter((x) => x.binned && x.brandId === brand.id);
        names.push(`${brand.name || "Unnamed brand"} (brand${riders.length > 0 ? ", with " + many(riders.length, "product", "products") : ""})`);
        riders.forEach((x) => refs.set("products/" + x.id, doc(db, "products", x.id)));
    });
    p.filter((x) => x.binned).forEach((x) => { if (!refs.has("products/" + x.id)) { names.push(`${x.brandName ? x.brandName + ": " : ""}${x.name || "Unnamed product"}`); refs.set("products/" + x.id, doc(db, "products", x.id)); } });
    b.filter((brand) => brand.binned).forEach((brand) => refs.set("brands/" + brand.id, doc(db, "brands", brand.id)));
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





// While typing: say at once when the product (same brand and name, or the same barcode) is in the inventory already, so the admin
// opens it instead of adding a second one. The save also refuses it; this only tells sooner.
function showNameClash() {
    const note = el("productNameClash");
    if (!note) return;
    const name = el("productNameInput").value.trim(), brandId = el("productBrandInput").value;
    const code = barcodeKey(el("productBarcodeInput").value.trim());
    const same = everyProduct.find((p) => p.id !== editingProductId && !p.binned
        && ((code && barcodeKey(p.barcode) === code) || (name.length >= 3 && brandId && p.brandId === brandId && [p.name, ...p.aliases].some((known) => compactName(known) === compactName(name)))));
    showSimilarProducts(same);
    if (!same) { note.hidden = true; note.textContent = ""; return; }
    note.textContent = `Already listed: "${same.name}" of ${brandNameOf(same)}${same.status !== "Active" ? " (" + same.status + ")" : ""}. Close this and open it from the list instead of adding it twice.`;
    note.hidden = false;
}

// Products that may be a variant (another shade, size or version) of the one in the form: shown so the admin can compare,
// never merged or blocked. The exact same barcode or brand + name is the clash note above (and is refused on save).
function showSimilarProducts(exact) {
    const box = el("productSimilar");
    if (!box) return;
    const found = similarProducts({ id: editingProductId || "", name: el("productNameInput").value, brandId: el("productBrandInput").value, barcode: "" }, everyProduct)
        .filter((x) => !exact || x.product.id !== exact.id);
    if (found.length === 0) { box.hidden = true; box.replaceChildren(); return; }
    box.innerHTML = `<b>Similar products already listed.</b> If this is another shade, size or version, keep the names different and save; if it is the same product, open the one listed instead.<ul>`
        + found.map(({ product: p, why }) => `<li><b>${escapeHtml(p.name)}</b> · ${escapeHtml(brandNameOf(p))} · ${escapeHtml(p.cosmeticType || "kind not set")}`
            + `${p.barcode ? " · barcode " + escapeHtml(p.barcode) : ""} · ${many(p.shades.length, "shade", "shades")} · ${escapeHtml(p.status)} <span class="muted">(${escapeHtml(why)})</span></li>`).join("")
        + "</ul>";
    box.hidden = false;
}

// ---- where each value came from (provenance) and whether a person checked it ----
function readProductForm() {
    const brand = brands.find((b) => b.id === el("productBrandInput").value);
    const type = el("productTypeInput").value === OTHER_TYPE ? el("productTypeOtherInput").value.trim() : el("productTypeInput").value;
    return {
        name: el("productNameInput").value.trim(),
        brand: brand ? brand.name : (el("productBrandInput").value === NEW_BRAND ? pendingBrandName : ""),
        cosmeticType: type, imageUrl: el("productImageInput").value.trim(), barcode: el("productBarcodeInput").value.trim(),
        fdaNumber: el("productFdaInput").value.trim(), ingredients: toList(el("productIngredientsInput").value),
        shades: formShades.slice()
    };
}
const originKeyOf = (id) => Object.keys(ORIGIN_INPUTS).find((key) => ORIGIN_INPUTS[key].includes(id)) || "";
// The admin changed a detected value by hand: it is now theirs ("Typed"), so it no longer waits for a check.
function onOriginEdit(event) {
    const key = originKeyOf(event.target && event.target.id);
    if (!key || !fieldOrigins[key] || fieldOrigins[key].state === TYPED) return;
    fieldOrigins = { ...fieldOrigins, [key]: originEntry(TYPED, "Admin", "manual") };
    paintOrigins();
}
function onOriginClick(event) {
    const button = event.target.closest("[data-origin-check]");
    if (!button) return;
    fieldOrigins = markChecked(fieldOrigins, [button.dataset.originCheck]);
    paintOrigins();
    saveDraft();
}
// A small label beside each field: "Detected · Open Beauty Facts [Mark checked]", "Needs review", "Checked".
function paintOrigins() {
    Object.keys(ORIGIN_INPUTS).forEach((key) => {
        const input = el(ORIGIN_INPUTS[key][0]);
        const row = input && input.closest(".field") ? input.closest(".field").querySelector(".label-row") : null;
        if (!row) return;
        let chip = row.querySelector(".origin-chip");
        const o = fieldOrigins[key];
        if (!o || (o.state === TYPED)) { if (chip) chip.remove(); return; }
        if (!chip) { chip = document.createElement("span"); row.appendChild(chip); }
        chip.className = `origin-chip origin-${o.state}`;
        chip.innerHTML = `${escapeHtml(STATE_WORDS[o.state])}${o.from ? " · " + escapeHtml(o.from) : ""}`
            + (waiting(o.state) ? ` <button type="button" class="text-link" data-origin-check="${key}" aria-label="Mark the ${escapeHtml(key)} as checked against the pack">Mark checked</button>` : "");
        chip.title = "";
    });
    const left = uncheckedFields(fieldOrigins);
    const note = el("productOriginNote");
    if (note) {
        note.hidden = left.length === 0;
        note.textContent = left.length ? `${many(left.length, "value came", "values came")} from a look-up, a photo or the phone and ${left.length === 1 ? "is" : "are"} not checked yet: ${left.map((f) => f.label.toLowerCase()).join(", ")}. Compare ${left.length === 1 ? "it" : "them"} with the pack, then press Mark checked.` : "";
    }
}
function paintRawIngredients() {
    const box = el("productIngredientsRaw");
    if (!box) return;
    box.hidden = !ingredientsRaw.text;
    el("productIngredientsRawFrom").textContent = ingredientsRaw.from ? `As given by ${ingredientsRaw.from}` : "As given by the source";
    el("productIngredientsRawText").textContent = ingredientsRaw.text;
}
// The fields that have a value but no origin yet were typed by the admin: record that too, so every value has a known origin.
function originsForSave() {
    const now = readProductForm(), out = { ...fieldOrigins };
    FILL_FIELDS.forEach((f) => {
        const has = Array.isArray(now[f.key]) ? now[f.key].length > 0 : !!now[f.key];
        if (!has) delete out[f.key];
        else if (!out[f.key]) out[f.key] = originEntry(TYPED, "Admin", "manual");
    });
    return out;
}
// Asked when an Active save still has values nobody checked: "checked" (mark them and save Active), "pending" or null (cancel).
function askUncheckedSave(left) {
    return new Promise((resolve) => {
        const finish = (answer) => { frame.close(); resolve(answer); };
        const frame = dialogFrame("Values not checked yet", () => finish(null));
        const intro = document.createElement("p");
        intro.className = "modal-intro";
        intro.textContent = "These values came from a look-up, a photo or the phone. The app shows Active products to its users, so compare them with the pack first.";
        const list = document.createElement("ul");
        list.className = "confirm-list";
        left.forEach((f) => { const li = document.createElement("li"); li.textContent = `${f.label}: ${STATE_WORDS[f.state]}${f.from ? " (" + f.from + ")" : ""}`; list.appendChild(li); });
        const actions = document.createElement("div");
        actions.className = "modal-actions";
        const make = (cls, text, answer) => { const b = document.createElement("button"); b.type = "button"; b.className = cls; b.textContent = text; b.addEventListener("click", () => finish(answer)); return b; };
        actions.append(make("cancel-btn", "Go back", null), make("btn", "Save as Pending for review", "pending"), make("save-btn", "I checked them: save Active", "checked"));
        frame.box.append(intro, list, actions);
    });
}

// A new product is kept as a draft in this browser while it is typed (never sent anywhere), so a closed tab or a crash does not lose
// the work. It is offered back the next time Add Product opens, and removed when the product is saved or the admin throws it away.
const DRAFT_KEY = "glowup.inventory.productDraft.v1";
const DRAFT_IDS = ["productNameInput", "productTypeInput", "productTypeOtherInput", "productDescInput", "productIngredientsInput", "productImageInput", "productFdaInput", "productBarcodeInput", "productMinAgeInput"];
let draftBrandName = "";
function saveDraft() {
    if (editingProductId || el("productModal").style.display === "none") return;
    try {
        const draft = { at: Date.now(), brandId: el("productBrandInput").value, brandName: pendingBrandName };
        DRAFT_IDS.forEach((id) => { draft[id] = el(id).value; });
        draft.shades = formShades.slice(0, 100); draft.codes = formShadeCodes.slice(0, 100);
        draft.origins = fieldOrigins; draft.raw = ingredientsRaw; draft.shadeNames = shadeNameByHex;
        if (!draft.productNameInput.trim() && !draft.productIngredientsInput.trim() && !draft.productBarcodeInput.trim()) { localStorage.removeItem(DRAFT_KEY); return; }
        localStorage.setItem(DRAFT_KEY, JSON.stringify(draft));
    } catch (error) {                                                        }
}
function readDraft() {
    try { const d = JSON.parse(localStorage.getItem(DRAFT_KEY) || "null"); return d && Date.now() - d.at < 7 * 86400000 ? d : null; } catch (error) { return null; }
}
function clearDraft() { try { localStorage.removeItem(DRAFT_KEY); } catch (error) {                                       } }
function showDraftNote(product) {
    const note = el("productDraftNote");
    const draft = product ? null : readDraft();
    if (!draft) { note.hidden = true; note.replaceChildren(); return; }
    note.replaceChildren();
    note.append(`A draft from ${new Date(draft.at).toLocaleString()} is saved in this browser: "${draft.productNameInput || "no name yet"}". `);
    const use = document.createElement("button"); use.type = "button"; use.className = "text-link"; use.textContent = "Restore it";
    const drop = document.createElement("button"); drop.type = "button"; drop.className = "text-link"; drop.textContent = "Throw it away";
    use.addEventListener("click", () => { restoreDraft(draft); note.hidden = true; });
    drop.addEventListener("click", () => { clearDraft(); note.hidden = true; });
    note.append(use, " · ", drop);
    note.hidden = false;
}
function restoreDraft(draft) {
    DRAFT_IDS.forEach((id) => { if (typeof draft[id] === "string") el(id).value = draft[id]; });
    if (draft.brandId && brands.some((b) => b.id === draft.brandId)) el("productBrandInput").value = draft.brandId;
    else if (draft.brandName) fillProductFromQuick({ brand: draft.brandName }, { partial: true });
    formShades = Array.isArray(draft.shades) ? draft.shades.filter((s) => typeof s === "string") : [];
    formShadeCodes = formShades.map((_, at) => (Array.isArray(draft.codes) && typeof draft.codes[at] === "string" ? draft.codes[at] : ""));
    fieldOrigins = cleanOrigins(draft.origins);
    shadeNameByHex = draft.shadeNames && typeof draft.shadeNames === "object" ? Object.fromEntries(Object.entries(draft.shadeNames).filter(([k, v]) => /^#[0-9A-F]{6}$/.test(k) && typeof v === "string").map(([k, v]) => [k, v.slice(0, 60)])) : {};
    ingredientsRaw = draft.raw && typeof draft.raw.text === "string" ? { text: draft.raw.text.slice(0, 4000), from: String(draft.raw.from || "").slice(0, 120) } : { text: "", from: "" };
    paintOrigins(); paintRawIngredients();
    renderShadeEditor(); showOtherTypeBox(); showBrandHint(); showIngredientSummary(); showBarcodeCheck(); showNameClash(); paintCounters();
}

function showScopeHint() {
    const hint = el("productScopeHint");
    const type = el("productTypeInput").value === OTHER_TYPE ? el("productTypeOtherInput").value : el("productTypeInput").value;
    if (!type.trim()) { hint.textContent = "Only make-up products are accepted, even from a brand that also sells skin care."; hint.className = "field-hint"; return; }
    const name = el("productNameInput").value, scope = productScope(type, name);
    hint.textContent = scope === "makeup" ? "Make-up: the app checks it and offers AR Try-On."
        : scope === "unclear" ? `The app cannot tell that this is make-up: ${scopeWords(type, name)}. It can only be saved as Pending, and the app hides it until its kind is clear.`
        : `Not make-up (${scopeWords(type, name)}): it cannot be saved here.`;
    hint.className = scope === "makeup" ? "field-hint" : "field-hint warn";
}


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
    finally { await loadInventory(); }
    return ops.length;
}




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







const slug = (text) => sameKey(text).replace(/[^a-z0-9]+/g, "_").replace(/^_+|_+$/g, "").slice(0, 80);






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


    const scopeOfEntry = (e) => productScope(e.type, e.name);
    const entries = (Array.isArray(catalogue.products) ? catalogue.products : []).filter((e) => e && e.name && e.brand && e.type
        && (scopeOfEntry(e) === "makeup" || (!byItself && scopeOfEntry(e) === "unclear")));



    const brandIdOf = new Map();

    const standing = (b) => (b.binned ? 0 : b.source && !b.reviewed ? 1 : b.source ? 2 : 3);
    everyBrand.slice().sort((a, b) => standing(a) - standing(b)).forEach((b) => [b.name, ...b.aliases].forEach((known) => { if (compactName(known)) brandIdOf.set(compactName(known), b.id); }));
    const newBrands = new Map();


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
            const clear = scopeOfEntry(e) === "makeup";
            sets.push({
                ref: doc(db, "products", "fda_" + slug(e.fdaNumber || e.brand + "_" + e.name)),
                data: {
                    name: e.name, brandId: brandIdOf.get(compactName(e.brand)), brandName: e.brand, cosmeticType: e.type, description: note,
                    ingredients: Array.isArray(e.ingredients) ? e.ingredients.slice(0, 80) : [], shades: [], minAge: 0, status: clear ? "Active" : "Pending", scans: 0,
                    source: "fdaph", reviewed: clear, sources: [portal].concat(e.ingredientSource ? [e.ingredientSource] : []), fdaNumber: e.fdaNumber || "", createdAt: serverTimestamp()
                }
            });
        });

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




// Brand names from the FDA Philippines list that are not in the inventory yet: typing offers them, spelled as notified.
async function fillBrandSuggestions() {
    const list = el("brandNameSuggestions");
    if (!list || list.dataset.ready) return;
    list.dataset.ready = "1";
    const data = await loadCatalogue();
    const listed = new Set(everyBrand.flatMap((b) => [b.name, ...(b.aliases || [])]).map(compactName));
    const names = Array.from(new Set((data.products || []).map((p) => String(p.brand || "").trim()).filter(Boolean)))
        .filter((n) => !listed.has(compactName(n))).sort((a, b) => a.localeCompare(b));
    list.replaceChildren(...names.map((n) => { const o = document.createElement("option"); o.value = n; return o; }));
}

function openBrandForm(brand) {
    fillBrandSuggestions().catch(() => {});
    const form = el("addBrandForm");
    form.reset();
    setFormError(form, "");
    editingBrandId = brand ? brand.id : null;
    el("brandModalTitle").textContent = brand ? "Edit Brand" : "Add New Brand";
    el("brandNameInput").value = brand ? brand.name : "";
    el("brandMarketInput").value = brand ? brand.market : "Global";
    paintCounters();
    // the suggestions also offer every market already used by a listed brand (for example "Philippines" from the FDA list)
    const marketList = el("brandMarketOptions");
    const offered = new Set([...marketList.options].map((o) => o.value.toLowerCase()));
    everyBrand.map((b) => String(b.market || "").trim()).filter((m) => m && !offered.has(m.toLowerCase())).forEach((m) => {
        offered.add(m.toLowerCase());
        const option = document.createElement("option");
        option.value = m;
        marketList.appendChild(option);
    });
    el("brandStatusInput").value = brand && STATUSES.includes(brand.status) ? brand.status : "Active";
    el("brandLogoInput").value = brand ? brand.logoUrl : "";
    el("brandImageInput").value = brand ? brand.imageUrl : "";
    paintWebsiteBox(brand);
    showReviewNote(el("brandReviewNote"), brand, "brand");
    brandModal.open();
    brandWindow.reset();
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

    const duplicate = everyBrand.find((b) => b.id !== editingBrandId && [b.name, ...b.aliases].some((known) => compactName(known) === compactName(name)));
    if (duplicate) { setFormError(form, duplicate.binned ? `"${duplicate.name}" is in the Recycle bin. Restore it from there instead.` : `"${duplicate.name}" is already in the inventory.`); el("brandNameInput").focus(); return; }
    setFormError(form, "");
    const close = editingBrandId ? [] : similarBrands(name, everyBrand);
    if (close.length && !(await askConfirm({ title: "A brand with a close name is listed", message: `Is "${name}" a different brand from these? Brands are never merged by themselves: if it is the same brand, cancel and add its products to the listed one.`, items: close.map((x) => `${x.brand.name} (${x.brand.status})`), confirmLabel: "It is a different brand", cancelLabel: "Cancel" }))) return;

    setBusy(saveButton, true, "Saving...");
    try {
        if (editingBrandId) {
            const before = brands.find((b) => b.id === editingBrandId);
            const renamed = before && before.name !== name;
            const mine = renamed ? everyProduct.filter((p) => p.brandId === editingBrandId) : [];


            const brandData = { name, category: BRAND_CATEGORY, market, status, logoUrl, imageUrl, ...websiteFields(website, before), updatedAt: serverTimestamp() };
            if (before && before.needsReview) { brandData.reviewed = true; brandData.reviewedAt = serverTimestamp(); }
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


function websiteFields(website, before) {
    const old = before ? before.website : "";
    if (website === old) return {};
    if (!website) return { website: "", websiteConfirmedBy: "", websiteConfirmedAt: null };
    return { website, websiteConfirmedBy: auth.currentUser ? auth.currentUser.uid : "", websiteConfirmedAt: serverTimestamp() };
}


function plainLink(url) {
    try { const parsed = new URL(url); return !parsed.username && !parsed.password && !url.includes(String.fromCharCode(92)); } catch (error) { return false; }
}


function gs1Line(barcode) {
    const hint = gs1Hint(barcode);
    if (!hint.text) return "";
    return `<span class="field-hint">${escapeHtml(hint.text)}</span>`
        + (hint.valid ? ` <button type="button" class="row-btn" data-gs1="${escapeHtml(String(barcode).replace(/[^0-9]/g, ""))}" data-tip="Copies the barcode and opens GS1's free search page (about 30 searches a day). Paste it there to see which company licenses it.">Check on Verified by GS1</button>` : "");
}


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





function openProductForm(product, brandId) {
    const form = el("productForm");
    form.reset();
    setFormError(form, "");
    editingProductId = product ? product.id : null;
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
    paintCounters();
    el("productIngredientsInput").value = product ? product.ingredients.join(", ") : "";
    el("productImageInput").value = product ? product.imageUrl : "";
    el("productFdaInput").value = product ? product.fdaNumber : "";
    el("productBarcodeInput").value = product ? product.barcode : "";
    showBarcodeCheck();
    el("productIngredientSearch").value = "";
    if (ingredientPicker) ingredientPicker.setNotes(product ? product.ingredientNotes : []);
    el("productMinAgeInput").value = product && product.minAge > 0 ? product.minAge : "";
    el("productStatusInput").value = product && STATUSES.includes(product.status) ? product.status : "Active";
    formShades = product ? product.shades.slice() : [];
    formShadeCodes = product ? formShades.map((_, at) => (product.shadeCodes || [])[at] || "") : [];
    shadeAdding = false; shadeMessage = "";
    sourceEditor.reset();
    sourceEditor.paint();
    pendingBrandName = "";
    quickFill.reset();
    fieldOrigins = product ? { ...product.fieldOrigins } : {};
    shadeNameByHex = product ? { ...product.shadeNameByHex } : {};
    ingredientsRaw = product && product.ingredientsRaw ? { text: product.ingredientsRaw, from: product.ingredientsRawFrom } : { text: "", from: "" };
    paintOrigins();
    paintRawIngredients();
    el("productQuickFill").hidden = !!product && !product.needsReview;            // also for a product waiting for review: complete it from the sources
    showNameClash();
    showDraftNote(product);

    showOtherTypeBox();
    showBrandHint();
    showIngredientSummary();
    renderShadeEditor();
    showReviewNote(el("productReviewNote"), product, "product");
    productModal.open();
    productWindow.reset();

    if (brands.length === 0) setFormError(form, "There are no brands yet. Close this, add a brand first, then add the brand's products.");
}


// Puts what Quick fill found into the form. partial: only the parts given are changed (a barcode, a photographed list).
function fillProductFromQuick(found, { partial } = {}) {
    const has = (key) => found[key] !== undefined && found[key] !== null && !(Array.isArray(found[key]) && found[key].length === 0) && found[key] !== "";
    if (has("name")) el("productNameInput").value = found.name.slice(0, 100);
    if (has("brand") || !partial) {
        const key = compactName(found.brand || "");
        const listed = key ? brandFor(found.brand, brands) : null;           // exact, or one brand whose name starts with it
        const select = el("productBrandInput");
        const stale = select.querySelector(`option[value="${NEW_BRAND}"]`);
        if (stale) stale.remove();
        pendingBrandName = "";
        if (listed) select.value = listed.id;
        else if (key) {
            pendingBrandName = found.brand.replace(/\s+/g, " ").trim().slice(0, 60);
            const option = document.createElement("option");
            option.value = NEW_BRAND;
            option.textContent = `New brand: ${pendingBrandName} (added when you save)`;
            select.appendChild(option);
            select.value = NEW_BRAND;
        }
        showBrandHint();
        sourceEditor.paint();
    }
    if (has("cosmeticType")) {
        const known = COSMETIC_TYPES.find((t) => sameKey(t) === sameKey(found.cosmeticType));
        el("productTypeInput").value = known || OTHER_TYPE;
        el("productTypeOtherInput").value = known ? "" : found.cosmeticType;
        showOtherTypeBox();
    }
    if (has("ingredients")) { el("productIngredientsInput").value = found.ingredients.join(", "); el("productIngredientsInput").dispatchEvent(new Event("input", { bubbles: true })); showIngredientSummary(); }
    if (has("barcode")) { el("productBarcodeInput").value = found.barcode; showBarcodeCheck(); }
    if (has("fdaNumber")) el("productFdaInput").value = found.fdaNumber;
    if (has("imageUrl") && !el("productImageInput").value) el("productImageInput").value = found.imageUrl;
    if (has("sources")) { assistedProductSources = Array.from(new Set(assistedProductSources.concat(found.sources))).slice(0, 10); sourceEditor.paint(); }
    showScopeHint();
    showNameClash();                 // the name or barcode may now match a listed product (or a variant of one)
    paintCounters();
}


function showReviewNote(note, entry, kind) {
    if (!entry || !entry.needsReview) { note.textContent = ""; note.style.display = "none"; return; }
    const origin = originLabel(entry.source) || (entry.source ? "Added automatically" : "Saved as Pending by an admin");
    const sentBack = entry.reviewState === REVIEW_CHANGES && entry.reviewNote ? `<b>Sent back for changes:</b> ${escapeHtml(entry.reviewNote)}<br>` : "";
    const links = sourceLinks(entry.sources || [], websiteOfEntry(entry));
    note.innerHTML = sentBack + `<b>${escapeHtml(origin)}.</b> Nobody has approved this ${kind} yet. `
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
    if (el("productBrandInput").value === NEW_BRAND && pendingBrandName) {
        const close = similarBrands(pendingBrandName, everyBrand);
        hint.textContent = close.length
            ? `"${pendingBrandName}" is not listed, but ${close.map((x) => `"${x.brand.name}"`).join(" or ")} is close. If it is the same brand, choose it in the list instead; otherwise saving adds "${pendingBrandName}" as a new brand.`
            : `"${pendingBrandName}" is not listed yet. Saving the product adds the brand too (market Global); edit the brand later for its logo and website.`;
        hint.className = close.length ? "field-hint warn" : "field-hint";
    } else if (brand && brand.status !== "Active") {
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
    const repeats = repeatedNames(el("productIngredientsInput").value);
    if (repeats.length) text += ` Listed twice: ${repeats.slice(0, 5).join(", ")}${repeats.length > 5 ? " and more" : ""}. Each is kept once; check the pack in case one is a different ingredient.`;
    hint.textContent = text;
    hint.className = missing.length > 0 ? "field-hint warn" : "field-hint";
}


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


function libraryIdOf(name) {
    const key = sameKey(name);
    const found = curatedList.find((entry) => [entry.name, entry.techName, ...entry.aliases].some((n) => sameKey(n) === key));
    return found ? found.id : "";
}


function productTags(product) {
    const slug = (text) => String(text || "").toLowerCase().normalize("NFD").replace(/[\u0300-\u036f]/g, "").replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 30);
    const tags = [slug(product.cosmeticType), slug(product.brandName)];
    if (product.shades.length > 0) tags.push("has-shades");
    if (product.ingredients.length === 0) tags.push("no-ingredient-list");
    if ((product.sources || []).some((s) => /fda\.gov\.ph/i.test(s))) tags.push("fda-listed");
    return Array.from(new Set(tags.filter(Boolean))).slice(0, MAX_TAGS);
}


function cleanLink(value) {
    const text = String(value || "").trim();
    if (!text) return "";
    try { const url = new URL(text); return url.protocol === "https:" && text.length <= 300 ? url.href : null; } catch (error) { return null; }
}



function cleanFdaNumber(value) {
    const text = String(value == null ? "" : value).replace(/\s+/g, " ").trim();
    if (text === "") return "";
    return /^[A-Za-z0-9][A-Za-z0-9 .,#()_\-\/]{0,39}$/.test(text) ? text : null;
}

const fdaLinkOf = (sources) => {
    const entry = (sources || []).find((s) => /fda\.gov\.ph/i.test(s));
    return entry ? (entry.split("|").pop() || "").trim() : "";
};






let shadeAdding = false, shadeMessage = "", shadeDraft = "#C24B62", shadeNearOk = "";

function renderShadeEditor() {
    // a hex colour only: a stored value never reaches the style attribute as other CSS (url(), etc.)
    const swatch = (shade) => `background:${/^#[0-9a-f]{3,8}$/i.test(String(shade || "").trim()) ? String(shade).trim() : "transparent"}`;
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


async function saveProduct(event) {
    event.preventDefault();
    const form = el("productForm");
    const saveButton = el("saveProductBtn");
    if (saveButton.disabled) return;

    const clean = (id) => el(id).value.replace(/\s+/g, " ").trim();
    const name = clean("productNameInput");
    const newBrand = el("productBrandInput").value === NEW_BRAND && pendingBrandName ? pendingBrandName : "";
    let brand = brands.find((b) => b.id === el("productBrandInput").value) || (newBrand ? { id: "", name: newBrand } : null);
    const cosmeticType = el("productTypeInput").value === OTHER_TYPE ? clean("productTypeOtherInput") : el("productTypeInput").value;
    const description = el("productDescInput").value.trim();
    const ingredients = toList(el("productIngredientsInput").value);
    const minAgeText = el("productMinAgeInput").value.trim();
    const minAge = toWholeNumber(minAgeText);
    const statusChosen = el("productStatusInput").value;
    const { shades, codes: shadeCodes } = pairShades(formShades, formShadeCodes, MAX_SHADES);
    const imageUrl = cleanLink(el("productImageInput").value), fdaNumber = cleanFdaNumber(el("productFdaInput").value);
    // the barcode is kept as text with its leading zeros; two barcodes are compared without them (barcodeKey)
    const barcodeText = el("productBarcodeInput").value.trim(), barcode = barcodeText ? barcodeDigits(barcodeText) : "";

    if (imageUrl === null) { setFormError(form, "The photo link must be a web address that starts with https://"); el("productImageInput").focus(); return; }
    if (fdaNumber === null) { setFormError(form, "The FDA registration number may have letters, digits and . , # ( ) _ - / only (at most 40 characters)."); el("productFdaInput").focus(); return; }
    if (barcodeText && !barcode) { setFormError(form, "A barcode is 6 to 14 digits (the number under the bars). Leave it empty if you do not have it."); el("productBarcodeInput").focus(); return; }
    const sameCode = barcode ? everyProduct.find((p) => p.id !== editingProductId && !p.binned && barcodeKey(p.barcode) === barcodeKey(barcode)) : null;
    if (sameCode) { setFormError(form, `The barcode ${barcode} is already on "${sameCode.name}" of ${brandNameOf(sameCode)}. A barcode belongs to one product.`); el("productBarcodeInput").focus(); return; }
    if (!name) { setFormError(form, "Type the product name."); el("productNameInput").focus(); return; }
    if (!brand) { setFormError(form, "Choose the brand."); el("productBrandInput").focus(); return; }
    if (!cosmeticType) { setFormError(form, "Choose the cosmetic type."); el("productTypeInput").focus(); return; }
    const scope = productScope(cosmeticType, name);
    if (scope === "skincare" || scope === "out") { setFormError(form, `GlowUp lists make-up only. This reads like ${scopeWords(cosmeticType, name)}, so it cannot be saved. If that is wrong, check the name and the kind.`); el("productTypeInput").focus(); return; }
    if (scope === "unclear" && statusChosen === "Active") { setFormError(form, `The app cannot tell that this is make-up: ${scopeWords(cosmeticType, name)}. Set Status to Pending to keep it for later, or correct the name or the kind.`); el("productStatusInput").focus(); return; }
    if (ingredients.length === 0) { setFormError(form, "Paste the ingredient list. The app cannot check or recommend a product without it."); el("productIngredientsInput").focus(); return; }
    if (minAgeText !== "" && (!/^\d+$/.test(minAgeText) || minAge > 99)) { setFormError(form, "Minimum age must be a whole number from 0 to 99."); el("productMinAgeInput").focus(); return; }
    const duplicate = everyProduct.find((p) => p.id !== editingProductId && p.brandId === brand.id && [p.name, ...p.aliases].some((known) => compactName(known) === compactName(name)));
    if (duplicate) { setFormError(form, duplicate.binned ? `"${duplicate.name}" of ${brand.name} is in the Recycle bin. Restore it from there instead.` : `${brand.name} already has a product named "${duplicate.name}".`); el("productNameInput").focus(); return; }
    setFormError(form, "");

    // values from a look-up, a photo or the phone that nobody checked: an Active save asks first (Pending keeps them waiting)
    let status = statusChosen;
    const left = uncheckedFields(fieldOrigins);
    if (status === "Active" && left.length > 0) {
        const answer = await askUncheckedSave(left);
        if (!answer) return;
        if (answer === "checked") fieldOrigins = markChecked(fieldOrigins, left.map((f) => f.key));
        else status = "Pending";
        paintOrigins();
    }


    const sources = assistedProductSources.slice();
    const notes = ingredientPicker ? ingredientPicker.getNotes() : [];
    const data = {
        name, brandId: brand.id, brandName: brand.name, cosmeticType, description,
        ingredients, shades, minAge, status, imageUrl, fdaNumber, barcode,
        fieldOrigins: originsForSave(),
        ingredientsRaw: ingredientsRaw.text, ingredientsRawFrom: ingredientsRaw.text ? ingredientsRaw.from : "",

        shadeCodes: shadeCodes.some(Boolean) ? shadeCodes : [],
        shadeNames: shades.some((h) => shadeNameByHex[String(h).toUpperCase()]) ? shades.map((h) => shadeNameByHex[String(h).toUpperCase()] || "") : [],

        ingredientIds: Array.from(new Set(ingredients.map(libraryIdOf).filter(Boolean))),
        ingredientCount: ingredients.length,
        ingredientNotes: notes.map((n) => ({ name: n.name, id: libraryIdOf(n.name), caution: n.caution, percent: n.percent })),
        tags: productTags({ cosmeticType, brandName: brand.name, shades, ingredients, sources }),
        updatedAt: serverTimestamp()
    };
    if (sources.length > 0 || editingProductId) data.sources = Array.from(new Set(sources)).slice(0, 10);
    const before = products.find((p) => p.id === editingProductId);
    // Active = approved by the admin who saves it. Pending = a submission waiting in "Waiting for review" (also a new one),
    // so another admin can approve it, send it back or reject it. A product sent back and saved again is "resubmitted".
    if (status === "Active" && (!before || before.needsReview)) {
        data.reviewed = true; data.reviewedAt = serverTimestamp(); data.reviewedBy = auth.currentUser ? auth.currentUser.uid : "";
        if (before && before.reviewState) data.reviewState = "";
    } else if (status === "Pending" && (!before || before.needsReview || before.status !== "Pending")) {
        data.reviewed = false;
        if (before && before.reviewState === REVIEW_CHANGES) data.reviewState = "resubmitted";
    }

    setBusy(saveButton, true, "Saving...");
    try {
        if (newBrand && !brand.id) {
            const twin = everyBrand.find((b) => [b.name, ...(b.aliases || [])].some((known) => compactName(known) === compactName(newBrand)));
            if (twin && twin.binned) throw new Error(`"${twin.name}" is in the Recycle bin. Restore the brand from there first.`);
            const madeBrand = twin ? { id: twin.id } : await addDoc(collection(db, "brands"), {
                // a brand made from inside the product form follows the product: Active only when the product is approved now
                name: newBrand, category: BRAND_CATEGORY, market: "Global", status: status === "Active" ? "Active" : "Pending", reviewed: status === "Active", logoUrl: "", imageUrl: "",
                ...websiteFields("", null), scans: 0, createdAt: serverTimestamp()
            });
            brand = { id: madeBrand.id, name: newBrand };
            data.brandId = brand.id;
            pendingBrandName = "";
        }
        if (editingProductId) {
            await updateDoc(doc(db, "products", editingProductId), data);
            notify(status === "Pending" ? "Product saved as Pending: it waits for review." : "Product saved.", "ok");
        } else {
            const folder = org.view.kind === "folder" ? org.view.name : "";
            const made = await addDoc(collection(db, "products"), { ...data, scans: 0, folder, createdAt: serverTimestamp() });
            notify((status === "Pending" ? "Product saved as Pending: it is in Waiting for review and the app does not show it yet." : "Product added.") + (folder ? ` Folder "${folder}".` : ""), status === "Pending" ? "info" : "ok");
        }
        if (!editingProductId) clearDraft();
        productModal.close();
        await loadInventory();
    } catch (error) {
        console.error("Error saving product: ", error);
        setFormError(form, "The product was not saved. " + describeError(error));
    } finally {
        setBusy(saveButton, false);
    }
}





const approval = () => ({ status: "Active", reviewed: true, reviewedBy: auth.currentUser ? auth.currentUser.uid : "", reviewedAt: serverTimestamp(), updatedAt: serverTimestamp() });

async function approveBrand(brand, button) {
    const waiting = productsOf(brand.id).filter((p) => p.needsReview).length;
    const question = `Approve "${brand.name}"?\n\nThe brand becomes Active: the app then looks for its name on scanned labels.`
        + (waiting > 0 ? `\n\nIts ${waiting} product(s) waiting for review stay Pending until you approve each one.` : "");
    if (!(await sure(question, "Approve", false))) return;

    button.disabled = true;

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


    let question = `Approve "${product.name}"?\n\nIt becomes Active: the app can recognise it in scans and recommend it to the users it suits. Check its name, type and ingredient list first.`;
    const unchecked = uncheckedFields(product.fieldOrigins);
    if (unchecked.length) question += `\n\nNot checked yet (from a look-up, a photo or a file): ${unchecked.map((f) => f.label.toLowerCase()).join(", ")}. Approving marks them as checked by you.`;
    if (brand.status !== "Active") question += `\n\nIts brand "${brand.name}" is ${brand.status} and is NOT approved by this. The app hides the product until the brand is approved too.`;
    if (!(await sure(question, "Approve", false))) return;

    button.disabled = true;
    try {
        const checked = markChecked(product.fieldOrigins, Object.keys(product.fieldOrigins));
        await updateDoc(doc(db, "products", product.id), { ...approval(), fieldOrigins: checked, reviewState: "" });
        Object.assign(product, { status: "Active", rawStatus: "Active", reviewed: true, needsReview: false, fieldOrigins: checked, reviewState: "" });
        afterChange();
        if (brand.status !== "Active") notify(`Product approved. ${BRAND_HINT} ("${brand.name}" is ${brand.status}.)`, "info");
        else notify("Product approved.", "ok");
    } catch (error) {
        console.error("Error approving product: ", error);
        notify("The product was not approved. " + describeError(error), "error");
        button.disabled = false;
    }
}







function approvalFor(p, brand) {
    if (!brand || isOut(p) || isUnclear(p) || brandOut(brand) || brand.status !== "Pending") return null;
    if (p.needsReview) return { product: true, brand: true, label: "Approve with brand", tip: `You have checked this product and its brand "${brand.name}": set both to Active.` };
    if (p.status === "Active") return { product: false, brand: true, label: "Approve brand", tip: `The product is approved but its brand "${brand.name}" is not, so the app hides the product. Approve the brand.` };
    return null;
}


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




// The photos a staff member took in the mobile app (captureEvidence/{productId}, admins only). Small JPEGs, read on demand.
async function showCapturePhotos(product) {
    const frame = dialogFrame(`Capture photos: ${product.name || "product"}`, () => frame.close());
    frame.box.classList.add("wide");
    const holder = document.createElement("div");
    holder.className = "capture-photos";
    holder.textContent = "Loading the photos...";
    const close = document.createElement("button");
    close.type = "button"; close.className = "cancel-btn"; close.textContent = "Close";
    close.addEventListener("click", () => frame.close());
    const actions = document.createElement("div"); actions.className = "modal-actions"; actions.append(close);
    frame.box.append(holder, actions);
    try {
        const snap = await getDoc(doc(db, "captureEvidence", product.id));
        const photos = snap.exists() && Array.isArray(snap.data().photos) ? snap.data().photos : [];
        holder.replaceChildren();
        if (photos.length === 0) { holder.textContent = "No photos were attached to this capture."; return; }
        photos.slice(0, 3).forEach((p) => {
            const b64 = String(p && p.jpeg || "");
            if (!/^[A-Za-z0-9+/=]+$/.test(b64)) return;                        // only plain base64 JPEG data is shown
            const fig = document.createElement("figure");
            const img = document.createElement("img");
            img.src = "data:image/jpeg;base64," + b64; img.alt = `${String(p.role || "Photo")} photo of ${product.name || "the product"}`; img.loading = "lazy";
            const cap = document.createElement("figcaption"); cap.textContent = String(p.role || "Photo");
            fig.append(img, cap); holder.append(fig);
        });
    } catch (error) {
        holder.textContent = "The photos could not be read. " + describeError(error);
    }
}

// "Send back": the product stays Pending and waits; the note says what to correct. Whoever edits and saves it resubmits it.
// "Reject": a reason is required; the product goes to the Recycle bin with that reason (restorable, and in the activity log).
function askReason({ title, intro, label, confirmLabel, danger }) {
    return new Promise((resolve) => {
        const finish = (answer) => { frame.close(); resolve(answer); };
        const frame = dialogFrame(title, () => finish(null));
        frame.box.insertAdjacentHTML("beforeend", `<p class="modal-intro"></p>
            <label class="small" for="reviewReasonInput"></label>
            <textarea id="reviewReasonInput" rows="3" maxlength="500"></textarea>
            <p class="field-hint" role="alert"></p>
            <div class="modal-actions"><button type="button" class="cancel-btn" data-act="cancel">Cancel</button><button type="button" class="${danger ? "btn danger solid" : "save-btn"}" data-act="ok"></button></div>`);
        frame.box.querySelector(".modal-intro").textContent = intro;
        frame.box.querySelector("label").textContent = label;
        frame.box.querySelector('[data-act="ok"]').textContent = confirmLabel;
        const input = frame.box.querySelector("textarea"), error = frame.box.querySelector('[role="alert"]');
        frame.box.querySelector('[data-act="cancel"]').addEventListener("click", () => finish(null));
        frame.box.querySelector('[data-act="ok"]').addEventListener("click", () => {
            const reason = reviewReason(input.value);
            if (!reason) { error.textContent = "Write the reason (5 to 500 characters)."; input.focus(); return; }
            finish(reason);
        });
        setTimeout(() => input.focus(), 0);
    });
}
async function sendBackProduct(product, button) {
    const reason = await askReason({ title: `Send back "${product.name}"`, intro: "It stays Pending and the app does not show it. The note is shown to whoever opens it next, so they can correct it and save it again.", label: "What should be corrected?", confirmLabel: "Send back" });
    if (!reason) return;
    button.disabled = true;
    try {
        await updateDoc(doc(db, "products", product.id), { status: "Pending", reviewed: false, reviewState: REVIEW_CHANGES, reviewNote: reason, reviewNoteBy: auth.currentUser ? auth.currentUser.uid : "", reviewNoteAt: serverTimestamp(), updatedAt: serverTimestamp() });
        notify("Sent back with your note. It stays in Waiting for review.", "ok");
        await loadInventory();
    } catch (error) {
        notify("It was not sent back. " + describeError(error), "error");
        button.disabled = false;
    }
}
async function rejectProduct(product, button) {
    const reason = await askReason({ title: `Reject "${product.name}"`, intro: "It goes to the Recycle bin with your reason, so the decision stays on record. It can be restored from there.", label: "Why is it rejected?", confirmLabel: "Reject", danger: true });
    if (!reason) return;
    button.disabled = true;
    try {
        await updateDoc(doc(db, "products", product.id), {
            binned: true, binnedAt: serverTimestamp(), statusBeforeBin: product.status, status: "Inactive", binnedWith: "", binnedReason: "Rejected: " + reason,
            reviewed: false, reviewState: "rejected", reviewNote: reason, reviewNoteBy: auth.currentUser ? auth.currentUser.uid : "", reviewNoteAt: serverTimestamp(), updatedAt: serverTimestamp()
        });
        notify("Rejected. It is in the Recycle bin with your reason.", "ok");
        await loadInventory();
    } catch (error) {
        notify("It was not rejected. " + describeError(error), "error");
        button.disabled = false;
    }
}

function toggleBrand(id) {
    if (closed.has(id)) closed.delete(id); else closed.add(id);
    renderInventory();
}

function onTableClick(event) {
    const button = event.target.closest("button");

    if (drill && drill.kind === "brands" && org.view.kind !== "bin") {
        const row = event.target.closest("tr.brand-row");
        if (row && (!button || button.dataset.act === "toggle")) { openDrill({ kind: "brand", id: row.dataset.brand }); return; }
    }

    if (drill && drill.kind === "brand" && event.target.closest("tr.brand-row") && (!button || button.dataset.act === "toggle")) return;
    if (!button) {

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
    else if (act === "send-back-product" && product) sendBackProduct(product, button);
    else if (act === "capture-photos" && product) showCapturePhotos(product);
    else if (act === "reject-product" && product) rejectProduct(product, button);
    else if (act === "edit-product" && product) openProductForm(product, null);
    else if (act === "folder-product" && product) org.chooseFolder(button, ["p:" + product.id]);
    else if (act === "bin-product" && product) org.toBin(["p:" + product.id]);
}


function onTableDoubleClick(event) {
    if (event.target.closest("button, a, .detail-box")) return;
    const row = event.target.closest("tr.product-row");
    const product = row ? products.find((p) => p.id === row.dataset.product) : null;
    if (product) openProductForm(product, null);
}




function registerHelpers() {
    const ready = () => everyBrand.length + everyProduct.length > 0;
    const setQuick = (name) => { quick = quick === name ? "" : name; el("reviewFilterSelect").value = ""; el("inventorySearchInput").value = ""; el("brandFilterSelect").value = ""; if (org.view.kind !== "all") org.show({ kind: "all" }); else fromFirstPage(); };

    const showLabel = (on) => (on ? "Show all" : "Show");
    const reviewing = () => el("reviewFilterSelect").value === "review";

    registerAssist("counts", () => {
        if (!ready()) return [];
        const out = outsideCount(), review = reviewCount();
        const lines = [];
        if (out > 0) lines.push({ tone: "bad", text: `${many(out, "entry is", "entries are")} not make-up. GlowUp lists make-up only.`, label: "Clean up", run: openCleanUp, tip: "You see every entry with the reason, and apply one step at a time. Nothing is deleted." });
        if (review > 0) lines.push({ tone: "info", text: `Next: ${many(review, "entry waits", "entries wait")} for your review. The app does not use ${review === 1 ? "it" : "them"} until you approve ${review === 1 ? "it" : "them"}.`, label: reviewing() ? "Show all" : "Show them", run: () => reviewing() ? resetAllFilters() : showOnly("review") });
        if (lines.length === 0) lines.push({ tone: "ok", text: "Everything listed is make-up and has been checked." });

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




function registerData() {
    const SCOPE_NAMES = { makeup: "Make-up", unclear: "Kind not clear", skincare: "Skin care (not listed)", out: "Hair, body, nail, fragrance (not listed)" };
    const toneOfStatus = (status) => (status === "Active" ? "ok" : status === "Pending" ? "warn" : "muted");

    const search = (text) => { el("reviewFilterSelect").value = ""; el("brandFilterSelect").value = ""; el("inventorySearchInput").value = text; quick = ""; if (org.view.kind !== "all") org.show({ kind: "all" }); else fromFirstPage(); };


    const chooseBrand = (name) => {
        const brand = brands.find((b) => sameKey(b.name) === sameKey(name));
        el("reviewFilterSelect").value = ""; el("inventorySearchInput").value = ""; quick = "";
        el("brandFilterSelect").value = brand ? brand.id : "";
        if (org.view.kind === "bin") org.show({ kind: "all" }); else fromFirstPage();
    };


    registerTileDataset("inventoryCounts", {
        label: "Counts", noun: "counts", nounOne: "count", valueNoun: "Number", selector: ".stat-row .stat", watch: ".stat-row",
        map: (tile) => ({ title: ((tile.querySelector(".stat-title") || {}).textContent || tile.getAttribute("data-tip-title") || "").trim(), value: parseFloat(((tile.querySelector(".stat-value") || {}).textContent || "0").replace(/[^\d.]/g, "")) || 0, tone: "info" }),
        open: (tile) => tile.click()
    });
    registerDataset("ingredientSuggestions", {
        label: "AI ingredient suggestions", noun: "suggestions", nounOne: "suggestion",
        items: () => ingredientSuggestions,
        map: (s) => ({ id: s.id, title: s.productName || "Unnamed product", subtitle: s.brandName, meta: many(s.ingredients.length, "ingredient", "ingredients"), tone: "pink" }),
        dimensions: [{ id: "brand", label: "Brand", of: (s) => s.brandName || null, none: "No brand" }],
        measures: [{ id: "ingredients", label: "Ingredients found", noun: "ingredients", of: (s) => s.ingredients.length }]
    });

    registerDataset("products", {
        label: "Products", noun: "products", nounOne: "product",
        items: () => products.filter(inFocus),
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
