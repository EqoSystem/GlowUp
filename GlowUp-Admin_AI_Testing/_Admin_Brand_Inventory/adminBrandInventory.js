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
import {
    collection, getDocs, addDoc, updateDoc, doc, writeBatch, serverTimestamp
} from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import {
    requireAdmin, wireSignOut, setupNavigationGuard, setupModal, escapeHtml, sameKey, toList, asArray, toWholeNumber, toDate, timeAgo,
    statusPill, normalizeStatus, notify, describeError, setBusy, setFormError, originLabel, sourceLinks, COSMETIC_TYPES, STATUSES,
    productScope, notMakeup, scopeWords, categoryOutOfScope, BRAND_CATEGORY
} from "../_Shared/adminCommon.js";
import { matchesSearch, compactName, barcodeKey } from "../_Shared/glowupRules.js";
import { cleanupPlan, productDuplicates, brandDuplicates, mergeProducts, mergeBrands, nameWithoutBrand } from "../_Shared/glowupCatalog.js";
import { openPlanDialog, openDuplicatesDialog } from "../_Shared/adminCleanup.js";
import { mountShell, paginate, renderPager, icon, BIN_ICON, openMenu, askConfirm } from "../_Shared/adminShell.js";
import { registerDataset, datasetChanged } from "../_Shared/adminWidgets.js";
import { createOrganizer, cleanFolder, friendly } from "../_Shared/adminOrganizer.js";
import { setAssistPage, registerAssist, refreshAssist } from "../_Shared/adminAssist.js";

const OTHER_TYPE = "__other__";
const NO_BRAND = "__none__";                // the group for products whose brand no longer exists
const MAX_SHADES = 12;
const BATCH_LIMIT = 400;                    // Firestore allows 500 writes in one batch
const BRANDS_PER_PAGE = 10;                 // "Brands": this many brands at a time
const PRODUCTS_PER_PAGE = 10;               // "All products" and the Recycle bin: this many rows at a time
const PRODUCTS_AT_A_TIME = 10;              // products of one brand, until "Show more" is pressed
const SHOWN_INGREDIENTS = 16;               // names listed in a product's details
const VIEW_KEY = "glowup.inventory.view.v1";
// true = when this page opens, the products of the FDA Philippines catalogue (../_Shared/data/fdaCatalogue.json) that are not
//        in the inventory yet are added by themselves, without a question. false = only the "..." menu adds them, after a question.
const AUTO_ADD_FDA_CATALOGUE = true;
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

let everyBrand = [];                        // every brand document, also the ones in the Recycle bin
let everyProduct = [];
let brands = [];                            // listed (not in the bin): [{ id, name, category, market, status, scans }]
let products = [];                          // listed: [{ id, name, brandId, brandName, cosmeticType, description, ingredients, shades, minAge, status, scans, folder }]
let binBrands = [];                         // in the Recycle bin
let binProducts = [];
let curatedKeys = new Set();                // every name / technical name / alias in the Ingredient Curator
let curatorKnown = false;
let editingBrandId = null;
let editingProductId = null;
let formShades = [];                        // the shades being edited in the open product form
let brandModal = null;
let productModal = null;
let org = null;                             // folders, the Recycle bin, picking and dragging
let openId = "";                            // the product whose details are open
let quick = "";                             // a filter a helper switched on: "" | a key of QUICK
const view = { mode: "brands", sort: "name" };      // "Brands" or "All products", and the order; remembered in this browser

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
    wireSignOut();
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
        panel: el("inventoryHost").closest(".panel"), host: el("inventoryHost"), shelf: el("inventoryShelf"), bar: el("inventoryBar"),
        folderNames: () => everyProduct.map((p) => p.folder).filter((name) => name),
        folderOf: (key) => { const p = key.startsWith("p:") ? everyProduct.find((x) => x.id === key.slice(2)) : null; return p ? p.folder : ""; },
        titleOf: (key) => { const item = itemOf(key); return item ? item.name || "Unnamed" : ""; },
        count: (v) => (v.kind === "bin" ? binEntries().length : listed().filter((p) => v.kind !== "folder" || sameKey(p.folder) === sameKey(v.name)).length),
        binKeys: () => binEntries().map((entry) => entry.key),
        move: moveToFolder, bin: moveToBin, restore: restoreFromBin, purge: purgeFromBin,
        renameFolder: (from, to) => refolder(from, to), deleteFolder: (name) => refolder(name, ""),
        onView: () => { fromFirstPage(); }
    });
    setAssistPage("inventory");
    registerHelpers();

    wireSearch(el("inventorySearchInput"), el("inventorySearchClear"), fromFirstPage);
    el("brandFilterSelect").addEventListener("change", fromFirstPage);
    el("reviewFilterSelect").addEventListener("change", fromFirstPage);
    document.querySelectorAll(".stat[data-show]").forEach((tile) => tile.addEventListener("click", () => showOnly(tile.dataset.show)));
    el("addMenuBtn").addEventListener("click", (event) => openMenu(event.currentTarget, [
        { label: "Brand", icon: "tag", description: "A new brand: name, category, market, status.", run: () => openBrandForm(null) },
        { label: "Product", icon: "lipstick", description: "A make-up product of a listed brand.", disabled: brands.length === 0, run: () => openProductForm(null, el("brandFilterSelect").value) }
    ]));
    el("moreMenuBtn").addEventListener("click", (event) => {
        const allClosed = shownGroups.length > 0 && shownGroups.every((g) => closed.has(g.id));
        const outside = outsideCount();
        openMenu(event.currentTarget, [
            { label: allClosed ? "Open all brands" : "Close all brands", icon: "down", disabled: view.mode !== "brands" || org.view.kind === "bin", description: allClosed ? "Show every brand's products." : "Show the brands only.", run: () => { if (allClosed) closed.clear(); else shownGroups.forEach((g) => closed.add(g.id)); renderInventory(); } },
            { separator: true },
            { label: "Clean up", icon: "sparkle", description: "What is not make-up, what is there twice, kinds and names to put right. You see every change and apply one step at a time.", run: openCleanUp },
            { label: "Find duplicates", icon: "layers", description: "Products and brands that are (or may be) the same thing. You choose which one to keep.", run: () => openDuplicates("products") },
            { separator: true },
            { label: "Add the FDA Philippines catalogue", icon: "download", description: "Make-up that is on the FDA Philippines list of notified cosmetics. You see how many and confirm first.", run: importFdaCatalogue },
            { label: "Trim the FDA Philippines catalogue", icon: "undo", description: "Moves FDA-list products that are no longer in the catalogue, have no ingredient list and were never scanned to the Recycle bin.", run: trimFdaCatalogue },
            { label: `Remove what is not make-up (${outside})`, icon: "ban", danger: true, disabled: outside === 0, description: "Skin care, hair, body, nail and fragrance entries go to the Recycle bin. You see the list and confirm first.", run: removeOutOfScope }
        ]);
    });
    el("sortMenuBtn").addEventListener("click", (event) => openMenu(event.currentTarget, [{ heading: "Order of the list" }].concat(
        SORTS.map((s) => ({ label: s.label, checked: s.id === view.sort, run: () => { view.sort = s.id; saveView(); fromFirstPage(); } })))));
    el("viewSwitch").addEventListener("click", (event) => {
        const button = event.target.closest("[data-mode]");
        if (!button) return;
        view.mode = button.dataset.mode === "all" ? "all" : "brands";
        saveView();
        if (org.view.kind === "bin") org.show({ kind: "all" }); else fromFirstPage();
    });
    el("inventoryQuick").addEventListener("click", () => { quick = ""; fromFirstPage(); });
    el("productNameInput").addEventListener("input", showScopeHint);
    el("productTypeOtherInput").addEventListener("input", showScopeHint);
    el("addBrandForm").addEventListener("submit", saveBrand);
    el("productForm").addEventListener("submit", saveProduct);
    el("inventoryTableBody").addEventListener("click", onTableClick);
    el("inventoryTableBody").addEventListener("dblclick", onTableDoubleClick);
    el("inventoryTableBody").addEventListener("keydown", (event) => { if (event.key === "Enter" && event.target.matches("tr.product-row")) { event.preventDefault(); toggleDetails(event.target.dataset.product); } });
    el("productTypeInput").addEventListener("change", showOtherTypeBox);
    el("productBrandInput").addEventListener("change", showBrandHint);
    el("productIngredientsInput").addEventListener("input", showIngredientSummary);
    el("productShadeEditor").addEventListener("click", onShadeClick);
    el("productShadeEditor").addEventListener("input", onShadeInput);

    loadInventory();
});

const saveView = () => { try { localStorage.setItem(VIEW_KEY, JSON.stringify(view)); } catch (error) { /* private window */ } };
function fromFirstPage() { pageState.page = 1; flatPage.page = 1; binPage.page = 1; renderInventory(); }
/** What a count tile (or a helper) narrows the list to: "" | "review" | "out". The same choice again shows everything. */
function showOnly(value, keep) {
    const select = el("reviewFilterSelect");
    select.value = select.value === value && !keep ? "" : value;
    quick = "";
    if (org.view.kind === "bin") org.show({ kind: "all" }); else fromFirstPage();
}

// ---------------------------------------------------------------------------------------------
// Loading
// ---------------------------------------------------------------------------------------------
/** When an entry was made, in seconds (for "Newest first"). */
const madeAt = (data) => { const made = toDate(data.createdAt); return made ? Math.floor(made.getTime() / 1000) : toWholeNumber(data.addedAtUnix); };
/** What the Recycle bin wrote on an entry. */
const binFields = (data) => { const since = toDate(data.binnedAt); return { binned: data.binned === true, binnedAt: since ? since.getTime() : 0, statusBeforeBin: String(data.statusBeforeBin || ""), binnedWith: String(data.binnedWith || ""), binnedReason: String(data.binnedReason || "") }; };

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
                scans: toWholeNumber(data.scans),
                source: String(data.source || "").trim().toLowerCase(),
                reviewed: data.reviewed === true,
                needsReview: data.reviewed === false,         // only entries the app or an import added carry this mark
                aliases: asArray(data.aliases),               // other ways the name is written (kept when two entries are merged)
                addedAt: madeAt(data)
            }, binFields(data));
        });
        everyProduct = productSnapshot.docs.map((d) => {
            const data = d.data();
            return Object.assign({
                id: d.id,
                name: data.name || data.productName || "",
                brandId: data.brandId || "",
                brandName: data.brandName || "",
                cosmeticType: data.cosmeticType || data.category || "",
                description: data.description || "",
                ingredients: asArray(data.ingredients),
                shades: asArray(data.shades).map(cleanShade).filter((s) => s !== null),
                minAge: toWholeNumber(data.minAge),
                status: normalizeStatus(data.status),
                scans: toWholeNumber(data.scans),
                source: String(data.source || "").trim().toLowerCase(),
                sources: Array.isArray(data.sources) ? data.sources.map((s) => String(s)) : [],
                reviewed: data.reviewed === true,
                needsReview: data.reviewed === false,
                barcode: String(data.barcode || ""),
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

    // the Ingredient Curator's names, to point out ingredients it does not explain yet (optional)
    curatedKeys = new Set();
    try {
        const ingredientSnapshot = await getDocs(collection(db, "ingredients"));
        ingredientSnapshot.forEach((d) => {
            const data = d.data();
            if (data.binned === true) return;
            [data.name, data.techName, ...asArray(data.aliases)].forEach((n) => { const k = sameKey(n); if (k) curatedKeys.add(k); });
        });
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
                fresh.concat(freshBrands).forEach((x) => { x.status = "Active"; x.reviewed = true; x.needsReview = false; });
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
                waiting.forEach((b) => { b.status = "Active"; b.reviewed = true; b.needsReview = false; });
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

/** Where an entry came from and whether an admin has checked it, as HTML ("" for an entry typed here). */
function originText(entry) {
    const origin = originLabel(entry.source) || (entry.needsReview ? "Added automatically" : "");
    if (!origin) return "";
    const links = sourceLinks(entry.sources || []);
    const state = entry.needsReview ? " · needs review" : entry.reviewed ? " · reviewed" : "";
    return escapeHtml(origin + state) + (links ? " · " + links : "");
}

function fillBrandSelects() {
    const filter = el("brandFilterSelect");
    // the first time, an address ending in ?brand=ID opens on that brand
    const wanted = filter.dataset.ready ? filter.value : (new URLSearchParams(window.location.search).get("brand") || "");
    filter.dataset.ready = "1";
    const option = (b) => `<option value="${escapeHtml(b.id)}">${escapeHtml(b.name || "Unnamed Brand")}${b.status === "Active" ? "" : " (" + escapeHtml(b.status) + ")"}</option>`;
    const offered = brands.filter((b) => !brandOut(b));
    filter.innerHTML = `<option value="">All brands</option>` + offered.map(option).join("");
    filter.value = offered.some((b) => b.id === wanted) ? wanted : "";
    el("productBrandInput").innerHTML = `<option value="">Choose a brand</option>` + brands.map(option).join("");
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
function cleanShade(value) {
    let text = String(value || "").trim().toUpperCase();
    if (text && text[0] !== "#") text = "#" + text;
    return /^#[0-9A-F]{6}$/.test(text) ? text : null;
}

/** The listed ingredients the Ingredient Curator has no entry for. */
function notCurated(ingredientNames) {
    return curatorKnown ? ingredientNames.filter((n) => !curatedKeys.has(sameKey(n))) : [];
}

/** What is wrong with a product, for the small dot in front of its name: { tone, text } or null. */
function attentionOf(p, brand) {
    const notes = [];
    let tone = "";
    if (isOut(p)) { tone = "bad"; notes.push(`Not make-up: ${scopeWords(p.cosmeticType, p.name)}.`); }
    else if (isUnclear(p)) { tone = "warn"; notes.push(`The app hides it: ${scopeWords(p.cosmeticType, p.name)}. Edit it and choose its kind.`); }
    if (p.needsReview) { tone = tone || "info"; notes.push("Waiting for review."); }
    if (p.ingredients.length === 0) { tone = tone || "warn"; notes.push("No ingredient list: the app cannot check it."); }
    if (brand && brand.status !== "Active") { tone = tone || "warn"; notes.push(`Its brand is ${brand.status}: the app hides it.`); }
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
const focusBrand = () => brands.find((b) => b.id === el("brandFilterSelect").value) || null;
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
}

function paintToolbar() {
    const inBin = org.view.kind === "bin";
    el("sortNow").textContent = sortNow().short;
    el("viewSwitch").querySelectorAll("[data-mode]").forEach((button) => { const on = !inBin && button.dataset.mode === view.mode; button.classList.toggle("on", on); button.setAttribute("aria-pressed", String(on)); });
    el("inventoryQuick").innerHTML = quick && !inBin ? `<button type="button" class="filter-chip" data-tip="Showing only these products. Click to show all again.">${escapeHtml(QUICK[quick].label)}${icon("x")}</button>` : "";
}

function renderInventory() {
    paintStats();
    paintToolbar();
    if (org.view.kind === "bin") renderBin(); else renderList(org.view);
    org.refresh();
    refreshAssist();
    // the data panels (lists, grids, graphs) follow the same brand and folder
    const focus = (focusBrand() ? focusBrand().id : "") + "|" + focusFolder();
    if (focus !== lastFocus) { lastFocus = focus; datasetChanged("products"); datasetChanged("brands"); }
}
let lastFocus = "|";

const pickBox = (label) => `<button type="button" class="pick" role="checkbox" aria-checked="false" aria-label="Select ${escapeHtml(label)}"></button>`;
const pickAllBox = (what) => `<button type="button" class="pick" data-pick-all role="checkbox" aria-checked="false" aria-label="Select all shown" data-tip="Select every ${what} shown here."></button>`;

function setPagers(state, total, noun) {
    renderPager(el("inventoryPager"), state, total, renderInventory, noun);
    renderPager(el("inventoryPagerTop"), state, total, renderInventory, noun);
}

function renderList(now) {
    const body = el("inventoryTableBody");
    const typed = el("inventorySearchInput").value;
    const term = sameKey(typed);                            // "" when nothing is searched for
    const brandFilter = el("brandFilterSelect").value;
    const show = el("reviewFilterSelect").value;            // "" | "review" | "out"
    const folder = now.kind === "folder" ? now.name : "";
    const flat = view.mode === "all";
    const columns = flat ? 7 : 6;
    el("inventoryTableHead").innerHTML = `<tr><th><span class="th-line">${pickAllBox("product")}Product</span></th>${flat ? "<th>Brand</th>" : ""}<th>Type</th><th>Shades</th><th class="text-right" data-tip="Scans the app recognised as this product.">Scans</th><th data-tip="The app only uses Active products of Active brands.">Status</th><th></th></tr>`;

    if (everyBrand.length === 0 && everyProduct.length === 0) {
        shownGroups = [];
        body.innerHTML = `<tr><td colspan="${columns}" class="table-message">No brands yet. Add the first brand, then its products.</td></tr>`;
        el("inventoryCountLine").textContent = "";
        setPagers(pageState, 0, "brands");
        return;
    }

    const base = (p) => (show === "out" ? isOut(p) : !isOut(p)) && (!folder || sameKey(p.folder) === sameKey(folder))
        && (show !== "review" || waits(p)) && (!quick || QUICK[quick].test(p));
    // Search: any letter case, with or without accents and punctuation, parts of words, several words in any order.
    // A product is also found by its other names, its kind, its barcode and its brand ("maybelline lipstick").
    const productMatches = (p) => matchesSearch(typed, [p.name, ...p.aliases, p.cosmeticType, p.description, p.barcode, (brandOf(p) || {}).name || p.brandName]);
    const brandMatches = (b) => !!b && matchesSearch(typed, [b.name, ...b.aliases, b.category, b.market]);
    const order = sortNow().cmp;
    const html = [];
    let productsShown = 0;

    if (flat) {
        shownGroups = [];
        const list = products.filter((p) => base(p) && (!brandFilter || p.brandId === brandFilter) && (!term || productMatches(p))).sort(order);
        productsShown = list.length;
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

    const nothing = term ? `Nothing here matches "${escapeHtml(typed)}".`
        : quick ? "No product like that. Click the chip above to show all again."
        : folder ? `"${escapeHtml(folder)}" is empty. Drag products onto its chip above.`
        : show === "review" ? "Nothing is waiting for review."
        : show === "out" ? "Every listed product is make-up."
        : "Nothing to show.";
    body.innerHTML = html.length > 0 ? html.join("") : `<tr><td colspan="${columns}" class="table-message">${nothing}</td></tr>`;
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
const toneDot = (note) => (note ? `<span class="tone-dot tone-${note.tone}" data-tip="${escapeHtml(note.text)}"></span>` : "");

function brandRow(brand, productCount, columns) {
    const about = [brand.category, brand.market].filter((t) => t).map(escapeHtml).join(" · ");
    const out = brandOut(brand);
    const buttons = [];
    if (brand.needsReview && !out) buttons.push(actionButton("row-btn approve", "approve-brand", brand.id, icon("check"), "Approve", "You have checked this brand: set it to Active.", "It asks first. Its products stay as they are."));
    buttons.push(actionButton("", "add-product", brand.id, icon("plus"), "Add product", "Add a product to this brand.", "Opens the product form with this brand chosen."));
    buttons.push(actionButton("", "edit-brand", brand.id, icon("edit"), "Edit brand", "Change the name, category, market or status.", "Opens the brand form."));
    buttons.push(actionButton("row-btn danger", "bin-brand", brand.id, BIN_ICON, "Recycle bin", "Remove this brand together with its products.", "They go to the Recycle bin and can be restored."));
    const fdaBrand = brand.source === "fdaph" || productsOf(brand.id).some(QUICK.fda.test);
    const flags = (fdaBrand ? `<span class="chip good" data-tip="Products of this brand were checked on the FDA Philippines list of notified cosmetics.">FDA Philippines</span>` : "") + (brand.needsReview ? `<span class="chip review" data-tip="${escapeHtml(originLabel(brand.source) || "Added automatically")}. Nobody has checked it yet.">needs review</span>` : "")
        + (out ? `<span class="chip bad">not a make-up brand</span>` : "");
    return `
        <tr class="brand-row${out ? " out-row" : ""}" data-brand="${escapeHtml(brand.id)}" data-key="b:${escapeHtml(brand.id)}" data-drag>
            <td colspan="${columns}"><div class="brand-line">
                <button type="button" class="brand-toggle" data-act="toggle" data-brand="${escapeHtml(brand.id)}" aria-label="Open or close this brand">${icon("down")}</button>
                <span class="brand-title">${escapeHtml(brand.name || "Unnamed Brand")}</span>
                ${about ? `<span class="brand-about">${about}</span>` : ""}${flags}
                <span class="spacer"></span>
                ${rowActions(buttons)}
                <span class="brand-meta">${many(productCount, "product", "products")} · ${many(brand.scans, "scan", "scans")}</span>
                ${statusPill(brand.status)}
            </div></td>
        </tr>`;
}

function productRow(p, brand, flat) {
    const open = p.id === openId;
    const shadeCell = p.shades.length === 0 ? DASH
        : p.shades.slice(0, 5).map((s) => `<span class="swatch" style="background:${escapeHtml(s)}" title="${escapeHtml(s)}"></span>`).join("") + (p.shades.length > 5 ? `<span class="muted small">+${p.shades.length - 5}</span>` : "");
    const buttons = [];
    if (p.needsReview && !isOut(p) && !isUnclear(p)) buttons.push(actionButton("row-btn approve", "approve-product", p.id, icon("check"), "Approve", "You have checked this product: set it to Active.", "It asks first. The app then uses it."));
    buttons.push(actionButton("row-btn danger", "bin-product", p.id, BIN_ICON, "Recycle bin", "Remove this product.", "It goes to the Recycle bin and can be restored."));

    return `
        <tr class="product-row${isOut(p) ? " out-row" : ""}${open ? " open" : ""}" data-product="${escapeHtml(p.id)}" data-key="p:${escapeHtml(p.id)}" data-drag tabindex="0" aria-expanded="${open}">
            <td class="name-cell"><div class="name-line">${pickBox(p.name || "product")}${toneDot(attentionOf(p, brand))}<span class="product-name">${escapeHtml(p.name || "Unnamed Product")}</span></div></td>
            ${flat ? `<td class="brand-ref">${escapeHtml(brand ? brand.name : (p.brandName || "–"))}</td>` : ""}
            <td class="type-cell">${escapeHtml(p.cosmeticType || "–")}</td>
            <td class="shade-cell">${shadeCell}</td>
            <td class="scan-count text-right">${p.scans}</td>
            <td class="status-cell">${statusPill(p.status)}</td>
            <td class="text-right">${rowActions(buttons)}</td>
        </tr>` + (open ? detailRow(p, brand, flat ? 7 : 6) : "");
}

/** Everything else about a product: shown under its row when the row is clicked. */
function detailRow(p, brand, columns) {
    const scope = productScope(p.cosmeticType, p.name);
    const kind = scope === "makeup" ? "Make-up. The app checks it and offers AR Try-On."
        : scope === "unclear" ? `The app hides it: ${scopeWords(p.cosmeticType, p.name)}. Press Edit and choose its kind; if it is not make-up, remove it.`
        : `Not make-up (${scopeWords(p.cosmeticType, p.name)}). GlowUp does not list it: move it to the Recycle bin.`;
    const facts = [["Kind", escapeHtml(kind)]];
    if (p.aliases.length > 0) facts.push(["Other names", escapeHtml(p.aliases.join(", "))]);
    if (p.barcode) facts.push(["Barcode", `<code class="small">${escapeHtml(p.barcode)}</code>`]);
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
    if (p.shades.length > 0) facts.push(["Shades", p.shades.map((s) => `<span class="swatch" style="background:${escapeHtml(s)}"></span><code class="small">${escapeHtml(s)}</code>`).join(" &nbsp;")]);
    if (p.minAge > 0) facts.push(["Minimum age", `${p.minAge}+`]);
    if (p.description) facts.push(["Description", escapeHtml(p.description)]);
    if (p.folder) facts.push(["Folder", escapeHtml(p.folder)]);
    const origin = originText(p);
    if (origin) facts.push(["Added", origin]);

    const button = (cls, act, iconHtml, label, tip) => `<button type="button" class="row-btn ${cls}" data-act="${act}" data-id="${escapeHtml(p.id)}" data-tip="${escapeHtml(tip)}">${iconHtml}${label}</button>`;
    const buttons = [button("main", "edit-product", icon("edit"), "Edit", "Change the name, brand, type, ingredients, shades or status.")];
    if (p.needsReview && scope === "makeup") buttons.push(button("approve", "approve-product", icon("check"), "Approve", "You have checked it: set it to Active."));
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
            { title: "Products the rule cannot place", text: "Kinds that are not decided yet (lip balm, lip oil, skin tint, BB cream ...) or names that say nothing clear. The app hides them. Edit each one: give it its kind, or move it to the Recycle bin.",
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
    const origin = (item) => originLabel(item.source) || "Typed on this website";
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

/**
 * A search box: the list follows a moment after the typing stops (not on every key), Esc or the X empties it.
 */
function wireSearch(input, clear, onChange) {
    let timer = 0;
    const show = () => { if (clear) clear.hidden = input.value.length === 0; };
    const now = () => { window.clearTimeout(timer); show(); onChange(); };
    input.addEventListener("input", () => { show(); window.clearTimeout(timer); timer = window.setTimeout(onChange, 160); });
    input.addEventListener("keydown", (event) => { if (event.key === "Escape" && input.value) { event.stopPropagation(); input.value = ""; now(); } else if (event.key === "Enter") now(); });
    if (clear) clear.addEventListener("click", () => { input.value = ""; now(); input.focus(); });
    show();
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
    org.toBin(emptyBrands.map((b) => "b:" + b.id).concat(extra.filter((p) => !brandIds.has(p.brandId)).map((p) => "p:" + p.id)));
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
    if (fresh.length === 0) { if (!byItself) notify("Everything from the FDA Philippines catalogue is in the inventory already.", "ok"); return; }
    const withList = fresh.filter((e) => Array.isArray(e.ingredients) && e.ingredients.length > 0).length;
    if (!byItself && !(await askConfirm({
        title: `Add ${many(fresh.length, "product", "products")} and ${many(newBrands.size, "brand", "brands")}?`,
        message: `They are on the FDA Philippines list of notified cosmetics (gathered ${catalogue.gathered || "recently"}). ${withList} come with an ingredient list; the others get theirs when a user scans them. Make-up is added as Active, marked as coming from the FDA list; ${fresh.filter((e) => scopeOfEntry(e) === "unclear").length} whose kind is not clear are added as Pending for you to look at.`,
        items: fresh.slice(0, 12).map((e) => `${e.brand}: ${e.name} (${e.type})`).concat(fresh.length > 12 ? [`and ${fresh.length - 12} more`] : []),
        confirmLabel: "Add them"
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
        for (let i = 0; i < sets.length; i += BATCH_LIMIT) {
            const batch = writeBatch(db);
            sets.slice(i, i + BATCH_LIMIT).forEach((s) => batch.set(s.ref, s.data));
            await batch.commit();
        }
        notify(`${many(fresh.length, "product", "products")} and ${many(newBrands.size, "brand", "brands")} added from the FDA Philippines list.`, "ok");
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
            const brandData = { name, category: BRAND_CATEGORY, market, status, updatedAt: serverTimestamp() };
            if (before && before.needsReview) { brandData.reviewed = true; brandData.reviewedAt = serverTimestamp(); }     // an admin who opens it and saves has checked it
            const writes = [{ ref: doc(db, "brands", editingBrandId), data: brandData }];
            mine.forEach((p) => writes.push({ ref: doc(db, "products", p.id), data: { brandName: name, updatedAt: serverTimestamp() } }));
            await runBatches(writes);
            notify(renamed && mine.length > 0 ? `Brand saved. Its ${many(mine.length, "product now shows", "products now show")} the new name.` : "Brand saved.", "ok");
        } else {
            await addDoc(collection(db, "brands"), { name, category: BRAND_CATEGORY, market, status, scans: 0, createdAt: serverTimestamp() });
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

// ---------------------------------------------------------------------------------------------
// Product form
// ---------------------------------------------------------------------------------------------
/** product = null for a new one; brandId = the brand a new product starts with. */
function openProductForm(product, brandId) {
    const form = el("productForm");
    form.reset();
    setFormError(form, "");
    editingProductId = product ? product.id : null;
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
    el("productMinAgeInput").value = product && product.minAge > 0 ? product.minAge : "";
    el("productStatusInput").value = product && STATUSES.includes(product.status) ? product.status : "Active";
    formShades = product ? product.shades.slice() : [];

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
    const links = sourceLinks(entry.sources || []);
    note.innerHTML = `<b>${escapeHtml(origin)}.</b> Nobody has checked this ${kind} yet. `
        + (kind === "product" ? "Compare the name, brand, type and ingredient list with the packaging, " : "Check the name and category, ")
        + "set Status to <b>Active</b> if the app should use it, then save: <b>saving marks it as reviewed</b>."
        + (links ? "<br>Source: " + links : "");
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

// ----- shades -----
function renderShadeEditor() {
    const items = formShades.map((shade, index) => `
        <span class="shade-item">
            <input type="color" value="${escapeHtml(shade.toLowerCase())}" data-index="${index}" title="Change this shade">
            <code>${escapeHtml(shade)}</code>
            <button type="button" data-remove="${index}" title="Remove this shade">×</button>
        </span>`).join("");
    const add = formShades.length < MAX_SHADES ? `<button type="button" class="row-btn" id="addShadeBtn">${icon("plus")}Shade</button>` : "";
    el("productShadeEditor").innerHTML = items + add;
}

function onShadeClick(event) {
    const button = event.target.closest("button");
    if (!button) return;
    if (button.id === "addShadeBtn") {
        if (formShades.length < MAX_SHADES) formShades.push("#C24B62");
    } else if (button.hasAttribute("data-remove")) {
        formShades.splice(Number(button.getAttribute("data-remove")), 1);
    } else return;
    renderShadeEditor();
}

function onShadeInput(event) {
    const input = event.target;
    if (input.type !== "color") return;
    const shade = cleanShade(input.value);
    const index = Number(input.getAttribute("data-index"));
    if (shade === null || !(index >= 0 && index < formShades.length)) return;
    formShades[index] = shade;
    const code = input.parentElement.querySelector("code");
    if (code) code.textContent = shade;                 // no full re-draw: the colour picker stays open
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
    const shades = Array.from(new Set(formShades.map(cleanShade).filter((s) => s !== null)));

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

    const data = {
        name, brandId: brand.id, brandName: brand.name, cosmeticType, description,
        ingredients, shades, minAge, status, updatedAt: serverTimestamp()
    };
    const before = products.find((p) => p.id === editingProductId);
    if (before && before.needsReview) { data.reviewed = true; data.reviewedAt = serverTimestamp(); }     // an admin who opens it and saves has checked it

    setBusy(saveButton, true, "Saving...");
    try {
        if (editingProductId) {
            await updateDoc(doc(db, "products", editingProductId), data);           // "scans" and the folder are left as they are
            notify("Product saved.", "ok");
        } else {
            const folder = org.view.kind === "folder" ? org.view.name : "";          // added while a folder is open: it goes into that folder
            await addDoc(collection(db, "products"), { ...data, scans: 0, folder, createdAt: serverTimestamp() });
            notify(folder ? `Product added to "${folder}".` : "Product added.", "ok");
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
const approval = () => ({ status: "Active", reviewed: true, reviewedAt: serverTimestamp(), updatedAt: serverTimestamp() });

async function approveBrand(brand, button) {
    const waiting = productsOf(brand.id).filter((p) => p.needsReview).length;
    const question = `Approve "${brand.name}"?\n\nThe brand becomes Active: the app then looks for its name on scanned labels.`
        + (waiting > 0 ? `\n\nIts ${waiting} product(s) waiting for review stay Pending until you approve each one.` : "");
    if (!(await sure(question, "Approve", false))) return;

    button.disabled = true;
    try {
        await updateDoc(doc(db, "brands", brand.id), approval());
        notify("Brand approved.", "ok");
        await loadInventory();
    } catch (error) {
        console.error("Error approving brand: ", error);
        notify("The brand was not approved. " + describeError(error), "error");
        button.disabled = false;
    }
}

async function approveProduct(product, button) {
    const brand = brands.find((b) => b.id === product.brandId);
    if (isOut(product) || isUnclear(product)) { notify(`"${product.name}" cannot be approved: ${scopeWords(product.cosmeticType, product.name)}. Correct its kind with Edit, or remove it.`, "error"); return; }
    if (!brand) { notify(`"${product.name}" has no brand. Open it with Edit, choose its brand and save.`, "error"); return; }
    if (product.ingredients.length === 0) { notify(`"${product.name}" has no ingredient list, so the app cannot check it. Open it with Edit and paste the list.`, "error"); return; }

    const brandToo = brand.needsReview;             // its brand arrived the same way and is still waiting: approve them together
    let question = `Approve "${product.name}"?\n\nIt becomes Active: the app can recognise it in scans and recommend it to the users it suits. Check its name, type and ingredient list first.`;
    if (brandToo) question += `\n\nIts brand "${brand.name}" is also waiting for review and will be approved with it.`;
    if (!(await sure(question, "Approve", false))) return;

    button.disabled = true;
    try {
        const batch = writeBatch(db);
        batch.update(doc(db, "products", product.id), approval());
        if (brandToo) batch.update(doc(db, "brands", brand.id), approval());
        await batch.commit();
        if (!brandToo && brand.status !== "Active") notify(`Product approved. Its brand "${brand.name}" is ${brand.status}, so the app keeps hiding it until the brand is Active.`, "info");
        else notify(brandToo ? "Product and its brand approved." : "Product approved.", "ok");
        await loadInventory();
    } catch (error) {
        console.error("Error approving product: ", error);
        notify("The product was not approved. " + describeError(error), "error");
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
    else if (act === "edit-product" && product) openProductForm(product, null);
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
        if (review > 0) lines.push({ tone: "info", text: `Next: ${many(review, "entry waits", "entries wait")} for your review. The app does not use ${review === 1 ? "it" : "them"} until you approve ${review === 1 ? "it" : "them"}.`, label: reviewing() ? "Show all" : "Show them", run: () => showOnly("review") });
        if (lines.length === 0) lines.push({ tone: "ok", text: "Everything listed is make-up and has been checked." });
        // the brands that were checked on the FDA Philippines list
        const checked = brands.filter((b) => b.source === "fdaph" || productsOf(b.id).some(QUICK.fda.test));
        const onList = listed().filter(QUICK.fda.test).length;
        if (onList > 0) lines.push({ tone: "ok", text: `${many(onList, "product", "products")} of ${many(checked.length, "brand", "brands")} are on the FDA Philippines list: ${checked.slice(0, 8).map((b) => b.name).join(", ")}${checked.length > 8 ? ` and ${checked.length - 8} more` : ""}.`, label: quick === "fda" ? "Show all" : "Show them", run: () => setQuick("fda") });
        return lines;
    });

    registerAssist("tools", () => {
        if (!ready()) return [];
        const parts = [];
        const typed = el("inventorySearchInput").value.trim(), brand = brands.find((b) => b.id === el("brandFilterSelect").value), show = el("reviewFilterSelect").value, now = org.view;
        if (typed) parts.push(`search "${typed}"`);
        if (brand) parts.push(`brand ${brand.name}`);
        if (now.kind === "folder") parts.push(`folder "${now.name}"`);
        if (now.kind === "bin") parts.push("the Recycle bin");
        if (show === "review") parts.push("waiting for review");
        if (show === "out") parts.push("not make-up");
        if (quick) parts.push(QUICK[quick].label.toLowerCase());
        if (parts.length > 0) return [{ tone: "info", text: `Showing only: ${parts.join(" · ")}.`, label: "Show everything", run: () => { el("inventorySearchInput").value = ""; el("brandFilterSelect").value = ""; el("reviewFilterSelect").value = ""; quick = ""; if (org.view.kind !== "all") org.show({ kind: "all" }); else fromFirstPage(); } }];
        return [
            { text: `Sorted by ${sortNow().short}, ${view.mode === "all" ? "every product in one list" : "grouped by brand"}. Search also finds a type, for example "lipstick".` },
            { text: "Drag a row onto a folder or the bin. Ctrl+click picks several rows; Shift+click picks a range." }
        ];
    });

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
        if (review > 0) lines.push({ tone: "info", text: `${many(review, "entry waits", "entries wait")} for review.`, label: showLabel(reviewing()), run: () => showOnly("review") });
        if (noShades > 0) lines.push({ tone: "warn", text: `${many(noShades, "make-up product has", "make-up products have")} no shades: AR Try-On has nothing to show for ${noShades === 1 ? "it" : "them"}.`, label: showLabel(quick === "noShades"), run: () => setQuick("noShades") });
        if (missing > 0) lines.push({ tone: "info", text: `${many(missing, "product lists", "products list")} ingredients the Curator does not explain yet.`, label: showLabel(quick === "notCurated"), run: () => setQuick("notCurated") });
        if (inBin > 0) lines.push({ text: `${many(inBin, "entry is", "entries are")} in the Recycle bin.`, label: "Open the bin", run: () => org.show({ kind: "bin" }) });
        lines.push({ text: "Click a product for its details. Double-click it to edit. Drag rows onto a folder or the bin." });
        return lines;
    });
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
