// Operations Dashboard: live numbers from the same database the mobile app uses.
//   brands / products / ingredients  - how many there are
//   scanActivity                     - one small anonymous note per scan made in the app
//                                      { productName, brandId, brandName, cosmeticType, status, source, scannedAtUnix,
//                                        ingredientCount, harmfulCount, cautionCount, allergenFlagged, flaggedIngredients[] }
//   allergenSuggestions / conditionSuggestions - items users typed in because they were not on the app's lists
//                                      { text, uid, status: "pending", createdAtUnix }
//   reviewed == false                - on a brand / product: the app (from a scan) or an import added it, nobody checked it yet
//                                      on an ingredient with source "ai": found by the app's AI lookup, not checked yet
import { db } from "../_Database/firebase-console.js";
import {
    collection, query, where, orderBy, limit, doc, serverTimestamp
} from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
// the brand list comes from the site's own copy when another page has read it already (see adminStore.js);
// getCountFromServer and onSnapshot come from there too, so that every read is counted in the console
import { getDocs, updateDoc, writeBatch, onListsRefreshed, getCountFromServer, onSnapshot } from "../_Shared/adminStore.js";
import {
    requireAdmin, wireSignOut, setupNavigationGuard, escapeHtml, asArray, toWholeNumber, timeAgo, formatWhen, startOfTodayUnix,
    notify, describeError, toneBadge
} from "../_Shared/adminCommon.js";
import { mountShell, paginate, renderPager, icon } from "../_Shared/adminShell.js";
import { registerDataset, datasetChanged } from "../_Shared/adminWidgets.js";
import { setAssistPage, registerAssist, refreshAssist } from "../_Shared/adminAssist.js";
import { setPanelQuiet, setPanelSummary } from "../_Shared/adminFold.js";

const RECENT_ITEMS = 120;             // how many recent scans are fetched (the list, the grid and the graphs all use them)
const PAGE_SIZE = 8;                  // how many rows a panel shows at a time
const activityPage = { page: 1, size: PAGE_SIZE };
const suggestionPage = { page: 1, size: PAGE_SIZE };
let recentScans = [];
const TOP_BRANDS = 5;
const TODAY_LIMIT = 200;              // the live "today" listener reads at most this many scans; the exact total comes from one count
const COUNT_EVERY_MS = 60 * 1000;     // that count is asked for at most once a minute
let lastCountAt = 0;                  // when it was last asked for
let exactToday = null;                // its answer (null = not asked yet, or the day has fewer scans than the limit)

const el = (id) => document.getElementById(id);
let stopToday = null;                 // switches the "today" listener off (it is restarted when the date changes)
let todayStart = 0;
let suggestions = [];                 // [{ id, collection, kind, text, createdAtUnix }]
let allBrands = [];                   // [{ id, name, category, status, scans }]
let todayResults = { safe: 0, caution: 0, harmful: 0 };
let activityFilter = "";              // "" | "safe" | "caution" | "harmful" | "flagged": what the activity list is narrowed to
let openScanId = "";                  // the scan whose details are open in the activity list
// what the helper lines read (null = not loaded yet)
let scansToday = null, flaggedToday = 0;
let waitingInventory = null, waitingIngredients = null;
let suggestionsLoaded = false;
const many = (n, one, more) => `${n} ${n === 1 ? one : more}`;

const STATUS_NAMES = { safe: "Safe", caution: "Use with care", harmful: "Harmful" };
const statusOf = (a) => (a.status === "safe" || a.status === "harmful" ? a.status : "caution");
const toneOf = (status) => ({ safe: "ok", caution: "warn", harmful: "bad" }[status] || "muted");
const INVENTORY_PAGE = "../_Admin_Brand_Inventory/adminBrandInventory.html";

document.addEventListener("DOMContentLoaded", async () => {
    registerData();                   // before the page frame: a saved layout may hold panels that show this data
    mountShell("dashboard");
    setAssistPage("dashboard");
    registerHelpers();
    wireSignOut();
    setupNavigationGuard();
    await requireAdmin();

    loadCatalogueCounts();
    onListsRefreshed((name) => { if (name === "brands") loadCatalogueCounts(); });      // the kept brand list was behind the database
    watchScansToday();
    watchRecentActivity();
    loadSuggestions();
    loadReviewQueue();
    el("suggestionList").addEventListener("click", onSuggestionClick);

    // things that answer a click
    const press = (node, run) => { node.addEventListener("click", (event) => { if (!event.target.closest(".guide-badge")) run(event); }); node.addEventListener("keydown", (event) => { if (event.key === "Enter" && event.target === node) run(event); }); };
    press(el("kpiScans"), () => setActivityFilter(""));
    press(el("kpiAllergens"), () => setActivityFilter("flagged", true));
    el("resultBar").addEventListener("click", (event) => { const part = event.target.closest("[data-status]"); if (part) setActivityFilter(part.dataset.status); });
    el("resultLegend").addEventListener("click", (event) => { const part = event.target.closest("[data-status]"); if (part) setActivityFilter(part.dataset.status); });
    el("activityFilter").addEventListener("click", () => setActivityFilter(""));
    el("activityList").addEventListener("click", (event) => {
        const row = event.target.closest(".activity-item");
        if (!row || event.target.closest("a, button")) return;
        openScanId = openScanId === row.dataset.id ? "" : row.dataset.id;
        renderActivity();
    });
    el("topBrandsList").addEventListener("click", (event) => { const row = event.target.closest("[data-brand]"); if (row) window.location.href = INVENTORY_PAGE + "?brand=" + encodeURIComponent(row.dataset.brand); });

    // a dashboard left open overnight starts counting the new day
    window.setInterval(() => { if (startOfTodayUnix() !== todayStart) watchScansToday(); }, 60000);
});

function setMetric(id, value, title) {
    const box = el(id);
    if (!box) return;
    box.classList.remove("skeleton");
    box.textContent = value;
    box.title = title || "";
}

// ---------------------------------------------------------------------------------------------
// Brands, products, ingredients
// ---------------------------------------------------------------------------------------------
async function loadCatalogueCounts() {
    // brands: the documents themselves, because the "Most Scanned Brands" panel needs their scan counters
    try {
        const snapshot = await getDocs(collection(db, "brands"));
        // a brand in the Recycle bin is not counted and not ranked
        allBrands = snapshot.docs.filter((d) => d.data().binned !== true).map((d) => ({ id: d.id, name: d.data().name || "Unnamed Brand", category: d.data().category || "", status: String(d.data().status || "Active"), scans: toWholeNumber(d.data().scans) }));
        setMetric("totalBrandsCount", allBrands.length);
        allBrands.sort((a, b) => b.scans - a.scans || a.name.localeCompare(b.name));
        renderTopBrands(allBrands);
        datasetChanged("topBrands");
        refreshAssist("brands");
    } catch (error) {
        console.error("Error counting brands: ", error);
        setMetric("totalBrandsCount", "–", describeError(error));
        showEmpty("topBrands", "Could not load the brands.", true);
    }
    countInto("products", "totalProductsCount");
    countInto("ingredients", "totalIngredientsCount");
}

async function countInto(collectionName, elementId) {
    try {
        // everything, less what waits in the Recycle bin
        const [all, binned] = await Promise.all([
            getCountFromServer(collection(db, collectionName)),
            getCountFromServer(query(collection(db, collectionName), where("binned", "==", true)))
        ]);
        setMetric(elementId, Math.max(0, all.data().count - binned.data().count));
    } catch (error) {
        console.error(`Error counting ${collectionName}: `, error);
        setMetric(elementId, "–", describeError(error));
    }
}

function renderTopBrands(brands) {
    const ranked = brands.filter((b) => b.scans > 0).sort((a, b) => b.scans - a.scans).slice(0, TOP_BRANDS);
    // nothing to rank: the panel folds to one line until a brand is scanned (the admin can open it all the same)
    setPanelQuiet("brands", ranked.length === 0, ranked.length === 0 ? "No brand scanned yet" : `${ranked[0].name} leads with ${many(ranked[0].scans, "scan", "scans")}`);
    if (ranked.length === 0) { showEmpty("topBrands", "No scans recorded for any brand yet.", false); return; }
    el("topBrandsEmpty").style.display = "none";
    const list = el("topBrandsList");
    list.style.display = "";
    list.innerHTML = ranked.map((b, i) => `
        <li class="rank-item can-open" data-brand="${escapeHtml(b.id)}">
            <span class="rank-number">${i + 1}</span>
            <span class="rank-name">${escapeHtml(b.name)}</span>
            <span class="rank-value">${b.scans} ${b.scans === 1 ? "scan" : "scans"}</span>
        </li>`).join("");
}

/** Shows a panel's empty box (and hides its list). prefix = "activity" | "suggestion" | "topBrands". */
function showEmpty(prefix, message, isError) {
    const list = el(prefix === "topBrands" ? "topBrandsList" : prefix + "List");
    if (list) list.style.display = "none";
    const box = el(prefix + "Empty");
    const text = el(prefix + "EmptyMessage");
    if (box) { box.style.display = ""; box.classList.remove("loading"); }
    if (text) { text.textContent = message; text.classList.toggle("error-text", !!isError); }
}

// ---------------------------------------------------------------------------------------------
// Waiting for review: what the app added by itself, or what was imported
// ---------------------------------------------------------------------------------------------
async function loadReviewQueue() {
    // not reviewed yet, less what was moved to the Recycle bin
    const waiting = async (collectionName, ...conditions) => {
        const [all, binned] = await Promise.all([
            getCountFromServer(query(collection(db, collectionName), ...conditions, where("reviewed", "==", false))),
            getCountFromServer(query(collection(db, collectionName), ...conditions, where("reviewed", "==", false), where("binned", "==", true)))
        ]);
        return Math.max(0, all.data().count - binned.data().count);
    };

    try {
        const [brandCount, productCount] = await Promise.all([waiting("brands"), waiting("products")]);
        waitingInventory = brandCount + productCount;
        if (brandCount + productCount > 0) {
            const parts = [];
            if (productCount > 0) parts.push(many(productCount, "product", "products"));
            if (brandCount > 0) parts.push(many(brandCount, "brand", "brands"));
            el("reviewInventoryTitle").textContent = parts.join(" and ") + " waiting for review";
            el("reviewInventoryTile").style.display = "";
        }
    } catch (error) {
        console.warn("Could not count the inventory entries waiting for review: ", error);        // the tile simply stays hidden
    }
    try {
        const ingredientCount = await waiting("ingredients", where("source", "==", "ai"));
        waitingIngredients = ingredientCount;
        if (ingredientCount > 0) {
            el("reviewIngredientsTitle").textContent = many(ingredientCount, "AI-found ingredient", "AI-found ingredients") + " waiting for review";
            el("reviewIngredientsTile").style.display = "";
        }
    } catch (error) {
        console.warn("Could not count the ingredients waiting for review: ", error);
    }
    // products app users sent as a photo (pending_products): one count query, 1 read
    let waitingSnapshots = 0;
    try {
        const snapshots = await getCountFromServer(query(collection(db, "pending_products"), where("isApproved", "==", false)));
        waitingSnapshots = snapshots.data().count;
        if (waitingSnapshots > 0) {
            el("reviewSnapshotsTitle").textContent = many(waitingSnapshots, "product photo", "product photos") + " waiting for review";
            el("reviewSnapshotsTile").style.display = "";
        }
    } catch (error) {
        console.warn("Could not count the photos waiting for review: ", error);           // the tile simply stays hidden
    }
    // with nothing waiting, only the two shortcuts are left (the menu on the left has them too): the panel folds to one line
    const waitingNow = (waitingInventory || 0) + (waitingIngredients || 0) + waitingSnapshots;
    setPanelQuiet("actions", waitingNow === 0, waitingNow === 0 ? "Nothing is waiting for review" : `${many(waitingNow, "entry waits", "entries wait")} for review`);
}

// ---------------------------------------------------------------------------------------------
// Scans made in the app
// ---------------------------------------------------------------------------------------------
function watchScansToday() {
    if (stopToday) stopToday();
    todayStart = startOfTodayUnix();
    exactToday = null; lastCountAt = 0;
    const sinceMidnight = where("scannedAtUnix", ">=", todayStart);
    const today = query(collection(db, "scanActivity"), sinceMidnight, orderBy("scannedAtUnix", "desc"), limit(TODAY_LIMIT));
    stopToday = onSnapshot(today, (snapshot) => {
        const capped = snapshot.size >= TODAY_LIMIT;
        if (capped && Date.now() - lastCountAt >= COUNT_EVERY_MS) {
            lastCountAt = Date.now();
            getCountFromServer(query(collection(db, "scanActivity"), sinceMidnight)).then((counted) => {
                exactToday = counted.data().count;
                scansToday = exactToday;
                setMetric("totalScansCount", exactToday, "Scans made in the app since midnight (exact count)");
            }).catch((error) => console.warn("Could not count today's scans:", error));
        }
        if (!capped) exactToday = null;
        let flagged = 0;
        const results = { safe: 0, caution: 0, harmful: 0 };
        snapshot.forEach((d) => {
            if (d.data().allergenFlagged === true) flagged++;
            const status = d.data().status;
            results[status === "safe" || status === "harmful" ? status : "caution"]++;
        });
        todayResults = results;
        scansToday = capped && exactToday !== null ? exactToday : snapshot.size;
        flaggedToday = flagged;
        renderResults(results);
        datasetChanged("results");
        refreshAssist("numbers");
        setMetric("totalScansCount", capped ? (exactToday !== null ? exactToday : TODAY_LIMIT + "+") : snapshot.size, "Scans made in the app since midnight");
        setMetric("flaggedAllergensCount", flagged, "Scans today in which one of the user's own allergens was found" + (capped ? " (among the latest " + TODAY_LIMIT + ")" : ""));
    }, (error) => {
        console.error("Error watching today's scans: ", error);
        setMetric("totalScansCount", "–", describeError(error));
        setMetric("flaggedAllergensCount", "–", describeError(error));
    });
}

/** The bar and the three counts of the "results of today" panel. */
function renderResults(results) {
    const total = results.safe + results.caution + results.harmful;
    // no scan yet today: three zeros say nothing, so the panel folds to one line until the first scan comes in
    setPanelQuiet("results", total === 0, total === 0 ? "No scans yet today" : `${many(total, "scan", "scans")} today: ${results.harmful} harmful, ${results.caution} with care, ${results.safe} safe`);
    const names = { safe: "Safe", caution: "Use with care", harmful: "Harmful" };
    el("resultBar").innerHTML = total === 0 ? "" : Object.keys(names).filter((k) => results[k] > 0)
        .map((k) => `<span class="${k}" data-status="${k}" style="flex-grow:${results[k]}" data-tip-title="${results[k]} scans" data-tip="${names[k]}"></span>`).join("");
    el("resultLegend").innerHTML = Object.keys(names).map((k) =>
        `<li data-status="${k}" class="${activityFilter === k ? "on" : ""}"><span class="activity-dot ${k}"></span>${names[k]}<span class="num">${results[k]}${total > 0 ? ` <span class="muted small">${Math.round(results[k] * 100 / total)}%</span>` : ""}</span></li>`).join("");
}

/** Narrows the activity list to one result (or to scans with an allergen match). The same choice again shows everything. */
function setActivityFilter(value, keep) {
    activityFilter = activityFilter === value && !keep ? "" : value;
    activityPage.page = 1;
    renderActivity();
    renderResults(todayResults);
    refreshAssist("activity");
    const panel = el("activityList").closest(".panel");
    if (panel) panel.scrollIntoView({ behavior: "smooth", block: "nearest" });
}

function renderActivity() {
    const list = el("activityList");
    const shown = recentScans.filter((a) => !activityFilter || (activityFilter === "flagged" ? a.allergenFlagged === true : statusOf(a) === activityFilter));
    const label = activityFilter === "flagged" ? "Allergen matches" : STATUS_NAMES[activityFilter] || "";
    el("activityFilter").innerHTML = label ? `<button type="button" class="filter-chip" data-tip="Showing only these scans. Click to show all again.">${escapeHtml(label)}${icon("x")}</button>` : "";
    list.innerHTML = shown.length > 0 ? paginate(shown, activityPage).map(activityRow).join("")
        : `<li class="table-message">No scan like that among the latest ${recentScans.length}.</li>`;
    renderPager(el("activityPager"), activityPage, shown.length, renderActivity, "scans");
}

function watchRecentActivity() {
    const recent = query(collection(db, "scanActivity"), orderBy("scannedAtUnix", "desc"), limit(RECENT_ITEMS));
    onSnapshot(recent, (snapshot) => {
        recentScans = snapshot.docs.map((d) => Object.assign({ id: d.id }, d.data()));
        datasetChanged("activity");
        refreshAssist("activity");
        refreshAssist("numbers");
        const harmfulLately = recentScans.filter((a) => statusOf(a) === "harmful").length;
        setPanelQuiet("activity", snapshot.empty, snapshot.empty ? "No scans recorded yet" : `${many(recentScans.length, "latest scan", "latest scans")}, ${harmfulLately} harmful`);
        if (snapshot.empty) { showEmpty("activity", "No app user activity recorded yet.", false); return; }
        el("activityEmpty").style.display = "none";
        const list = el("activityList");
        list.style.display = "";
        renderActivity();
    }, (error) => {
        console.error("Error watching recent activity: ", error);
        showEmpty("activity", "Could not load the activity.", true);
        el("activityEmptyHint").textContent = describeError(error);
    });
}

function activityRow(a) {
    const status = ["safe", "caution", "harmful"].includes(a.status) ? a.status : "caution";
    const product = a.productName ? a.productName : "Product without a name";
    const brand = a.brandName ? ` <span class="muted">· ${escapeHtml(a.brandName)}</span>` : "";
    const flagged = asArray(a.flaggedIngredients);
    const harmful = toWholeNumber(a.harmfulCount), caution = toWholeNumber(a.cautionCount);

    // how the scan was judged for the person who made it: ONE badge (dot and word). An allergen match stands beside it,
    // because it is a different fact. Everything else (kind, counts, the flagged names) waits until the scan is clicked:
    // a line that repeated it under every scan made the list twice as long.
    const verdict = toneBadge(STATUS_NAMES[status], toneOf(status), status === "safe" ? "Nothing in it was flagged for this user." : status === "harmful" ? "Something in it is flagged for this user." : "Something in it is worth watching for this user.");

    // the details of the scan that was clicked
    let more = "";
    if (a.id === openScanId) {
        const lines = [`<b>Scanned</b> ${escapeHtml(formatWhen(a.scannedAtUnix))}${a.source === "manual" ? " · typed in by the user" : ""}`];
        if (a.cosmeticType) lines.push(`<b>Kind</b> ${escapeHtml(a.cosmeticType)}`);
        lines.push(`<b>Ingredients</b> ${toWholeNumber(a.ingredientCount)} read · ${harmful} harmful · ${caution} to watch`);
        if (flagged.length > 0) lines.push(`<b>Flagged for this user</b> ${escapeHtml(flagged.join(", "))}`);
        if (a.brandName) lines.push(`<b>Brand</b> ${escapeHtml(a.brandName)}`);
        more = `<div class="activity-more">${lines.join("<br>")}</div>`;
    }

    return `
        <li class="activity-item${a.id === openScanId ? " open" : ""}" data-id="${escapeHtml(a.id)}" tabindex="0">
            <div class="activity-main"><span class="activity-title">${escapeHtml(product)}</span>${brand}</div>
            <span class="activity-flags">${a.allergenFlagged === true ? `<span class="chip warn" data-tip="One of this user's own allergens is in the product.">allergen match</span>` : ""}${verdict}</span>
            <span class="activity-time" title="${escapeHtml(formatWhen(a.scannedAtUnix))}">${escapeHtml(timeAgo(a.scannedAtUnix))}</span>
            ${more}
        </li>`;
}

// ---------------------------------------------------------------------------------------------
// Suggestions typed in by app users
// ---------------------------------------------------------------------------------------------
async function loadSuggestions() {
    const sources = [
        { name: "allergenSuggestions", kind: "Allergen" },
        { name: "conditionSuggestions", kind: "Skin condition" }
    ];
    suggestions = [];
    let failed = null;
    for (const source of sources) {
        try {
            const snapshot = await getDocs(query(collection(db, source.name), where("status", "==", "pending")));
            snapshot.forEach((d) => suggestions.push({
                id: d.id, collection: source.name, kind: source.kind,
                text: d.data().text || "", createdAtUnix: toWholeNumber(d.data().createdAtUnix)
            }));
        } catch (error) {
            console.error(`Error loading ${source.name}: `, error);
            failed = error;
        }
    }
    if (failed && suggestions.length === 0) {
        showEmpty("suggestion", "Could not load the suggestions.", true);
        el("suggestionEmptyHint").textContent = describeError(failed);
        return;
    }
    renderSuggestions();
}

function renderSuggestions() {
    suggestionsLoaded = true;
    datasetChanged("suggestions");
    refreshAssist("suggestions");
    setPanelQuiet("suggestions", suggestions.length === 0, suggestions.length === 0 ? "Nothing waiting" : `${many(suggestions.length, "suggestion waits", "suggestions wait")} for review`);
    if (suggestions.length === 0) { showEmpty("suggestion", "No suggestions waiting for review.", false); return; }
    suggestions.sort((a, b) => b.createdAtUnix - a.createdAtUnix);
    el("suggestionEmpty").style.display = "none";
    const list = el("suggestionList");
    list.style.display = "";
    renderPager(el("suggestionPager"), suggestionPage, suggestions.length, renderSuggestions, "suggestions");
    list.innerHTML = paginate(suggestions, suggestionPage).map((s) => `
        <li class="suggestion-item">
            <div class="suggestion-main">
                <span class="suggestion-text">${escapeHtml(s.text || "(empty)")}</span>
                <span class="chip ${s.kind === "Allergen" ? "warn" : "pink"}">${escapeHtml(s.kind)}</span>
                <span class="small muted">${escapeHtml(timeAgo(s.createdAtUnix))}</span>
            </div>
            <div class="suggestion-actions btn-group">
                <button class="icon-btn small row-btn approve" data-action="reviewed" data-id="${escapeHtml(s.id)}" data-collection="${escapeHtml(s.collection)}" aria-label="Mark reviewed" data-tip-title="Mark reviewed" data-tip="You have dealt with it, for example added it to the Ingredient Curator." data-tip-preview="It leaves this list.">${icon("check")}</button>
                <button class="icon-btn small row-btn danger" data-action="dismissed" data-id="${escapeHtml(s.id)}" data-collection="${escapeHtml(s.collection)}" aria-label="Dismiss" data-tip-title="Dismiss" data-tip="Not useful. It leaves this list.">${icon("x")}</button>
            </div>
        </li>`).join("");
}

async function onSuggestionClick(event) {
    const button = event.target.closest("button[data-action]");
    if (!button) return;
    const id = button.getAttribute("data-id");
    const collectionName = button.getAttribute("data-collection");
    const status = button.getAttribute("data-action");
    button.disabled = true;
    try {
        await updateDoc(doc(db, collectionName, id), { status, reviewedAt: serverTimestamp() });
        suggestions = suggestions.filter((s) => !(s.id === id && s.collection === collectionName));
        renderSuggestions();
        notify(status === "reviewed" ? "Marked as reviewed." : "Suggestion dismissed.", "ok");
    } catch (error) {
        console.error("Error updating suggestion: ", error);
        notify("The suggestion was not updated. " + describeError(error), "error");
        button.disabled = false;
    }
}

// ---------------------------------------------------------------------------------------------
// Helpers: the line in each panel that reads its numbers and says what to do next (see adminAssist.js)
// ---------------------------------------------------------------------------------------------
function registerHelpers() {
    const percent = (part, whole) => (whole > 0 ? Math.round(part * 100 / whole) : 0);
    const hourName = (hour) => new Date(2000, 0, 1, hour).toLocaleTimeString(undefined, { hour: "numeric" });

    registerAssist("numbers", () => {
        if (scansToday === null) return [];
        // (a helper only speaks when it adds something: "no scans yet" beside a tile that says 0 was the same thing twice)
        const lines = [];
        if (scansToday > 0) {
            const hours = new Map();
            recentScans.forEach((a) => { if (toWholeNumber(a.scannedAtUnix) >= todayStart) { const hour = new Date(a.scannedAtUnix * 1000).getHours(); hours.set(hour, (hours.get(hour) || 0) + 1); } });
            const busiest = Array.from(hours).sort((a, b) => b[1] - a[1])[0];
            if (busiest) lines.push({ tone: "info", text: `Busiest hour today so far: ${hourName(busiest[0])}, with ${many(busiest[1], "scan", "scans")}.` });
            if (flaggedToday > 0) lines.push({ tone: "warn", text: `${flaggedToday} of today's ${many(scansToday, "scan", "scans")} matched one of the user's own allergens.`, label: "Show them", run: () => setActivityFilter("flagged", true) });
        }
        // yesterday, when the scans that were fetched reach back that far
        const yesterday = todayStart - 86400;
        const oldest = recentScans.length > 0 ? toWholeNumber(recentScans[recentScans.length - 1].scannedAtUnix) : 0;
        if (recentScans.length > 0 && (recentScans.length < RECENT_ITEMS || oldest < yesterday)) {
            const count = recentScans.filter((a) => a.scannedAtUnix >= yesterday && a.scannedAtUnix < todayStart).length;
            lines.push({ text: `Yesterday there ${count === 1 ? "was 1 scan" : "were " + count + " scans"} in all.` });
        }
        return lines;
    });

    registerAssist("activity", () => {
        const total = recentScans.length;
        if (total === 0) return [];
        const lines = [];
        const harmful = recentScans.filter((a) => statusOf(a) === "harmful").length;
        if (harmful > 0) lines.push({ tone: "bad", text: `${harmful} of the latest ${total} scans (${percent(harmful, total)}%) were harmful for the person who scanned.`, label: activityFilter === "harmful" ? "Show all" : "Show them", run: () => setActivityFilter("harmful") });
        const kinds = new Map();
        recentScans.forEach((a) => { if (a.cosmeticType) kinds.set(a.cosmeticType, (kinds.get(a.cosmeticType) || 0) + 1); });
        const top = Array.from(kinds).sort((a, b) => b[1] - a[1])[0];
        if (top) lines.push({ text: `Scanned most lately: ${top[0]} (${top[1]} of ${total}).` });
        const nameless = recentScans.filter((a) => !a.productName).length;
        if (nameless > 0) lines.push({ text: `${many(nameless, "scan", "scans")} had no product name: the name on the label could not be read.` });
        return lines;
    });

    // "Quick actions" and "Today's results" have no helper line: it only repeated what their tiles and their legend say
    // (the sum of what waits, the share of each result). Their folded line carries the short version instead.

    registerAssist("suggestions", () => {
        if (!suggestionsLoaded || suggestions.length === 0) return [];
        const allergens = suggestions.filter((s) => s.kind === "Allergen").length, conditions = suggestions.length - allergens;
        const parts = [];
        if (allergens > 0) parts.push(many(allergens, "allergen", "allergens"));
        if (conditions > 0) parts.push(many(conditions, "skin condition", "skin conditions"));
        const oldest = suggestions.reduce((least, s) => (s.createdAtUnix > 0 && s.createdAtUnix < least ? s.createdAtUnix : least), Infinity);
        return [{ tone: "info", text: `${parts.join(" and ")} suggested by app users.` + (Number.isFinite(oldest) ? ` Oldest: ${timeAgo(oldest)}.` : ""), label: "Curator", run: () => { window.location.href = "../_Admin_Ingredient_Curator/adminIngredientCurator.html"; }, tip: "Add what is useful to the Ingredient Curator, then mark the suggestion reviewed." }];
    });

    registerAssist("brands", () => {
        if (allBrands.length === 0) return [];
        const total = allBrands.reduce((sum, b) => sum + b.scans, 0);
        const lines = [];
        if (total > 0) lines.push({ text: `${allBrands[0].name} has ${percent(allBrands[0].scans, total)}% of all brand scans.` });
        const never = allBrands.filter((b) => b.scans === 0).length;
        if (never > 0) lines.push({ text: `${many(never, "brand was", "brands were")} never scanned yet.`, label: "Inventory", run: () => { window.location.href = INVENTORY_PAGE; } });
        return lines;
    });
}

// ---------------------------------------------------------------------------------------------
// The data this page offers to data panels (lists, rows, grids and graphs; see adminWidgets.js)
// ---------------------------------------------------------------------------------------------
function registerData() {
    const two = (n) => String(n).padStart(2, "0");
    const dayKey = (unix) => { const d = new Date(unix * 1000); return `${d.getFullYear()}-${two(d.getMonth() + 1)}-${two(d.getDate())}`; };
    const dayLabel = (key) => { const p = key.split("-").map(Number); return p.length === 3 && !p.some(isNaN) ? new Date(p[0], p[1] - 1, p[2]).toLocaleDateString(undefined, { day: "numeric", month: "short" }) : key; };
    /** Every day from the first to the last one that has a scan (at most a month), so a quiet day shows as zero. */
    const fillDays = (keys) => {
        const real = keys.filter((k) => /^\d{4}-\d{2}-\d{2}$/.test(k)).sort();
        if (real.length === 0) return [];
        const p = real[0].split("-").map(Number), day = new Date(p[0], p[1] - 1, p[2]), out = [];
        for (let i = 0; i < 31; i++) { const key = dayKey(day.getTime() / 1000); out.push(key); if (key >= real[real.length - 1]) break; day.setDate(day.getDate() + 1); }
        return out;
    };

    registerDataset("activity", {
        label: "Recent scans", noun: "scans", nounOne: "scan",
        items: () => recentScans,
        map: (a) => ({
            id: a.id, title: a.productName || "Product without a name", subtitle: [a.brandName, a.cosmeticType].filter((t) => t).join(" · "),
            meta: timeAgo(a.scannedAtUnix), tone: toneOf(statusOf(a)), value: toWholeNumber(a.ingredientCount), valueLabel: "ingredients"
        }),
        dimensions: [
            { id: "status", label: "Result", of: statusOf, keys: ["harmful", "caution", "safe"], labelOf: (k) => STATUS_NAMES[k] || k, tone: toneOf, pick: (k) => setActivityFilter(k, true) },
            { id: "type", label: "Cosmetic type", of: (a) => a.cosmeticType || null, none: "No type" },
            { id: "brand", label: "Brand", of: (a) => a.brandName || null, none: "No brand" },
            { id: "how", label: "How it was entered", of: (a) => (a.source === "manual" ? "Typed in" : "Scanned"), keys: ["Scanned", "Typed in"] },
            { id: "allergen", label: "Allergen match", of: (a) => (a.allergenFlagged === true ? "Allergen match" : "No match"), keys: ["Allergen match", "No match"], tone: (k) => (k === "Allergen match" ? "warn" : "muted"), pick: (k) => setActivityFilter(k === "Allergen match" ? "flagged" : "", true) },
            { id: "day", label: "Day", ordered: true, of: (a) => dayKey(a.scannedAtUnix), labelOf: dayLabel, fill: fillDays },
            { id: "hour", label: "Hour of the day", ordered: true, of: (a) => two(new Date(a.scannedAtUnix * 1000).getHours()), labelOf: (k) => (/^\d\d$/.test(k) ? Number(k) + "h" : k), fill: () => Array.from({ length: 24 }, (_, i) => two(i)) }
        ],
        remove: async (rows) => {
            try {
                for (let i = 0; i < rows.length; i += 400) {
                    const batch = writeBatch(db);
                    rows.slice(i, i + 400).forEach((a) => batch.delete(doc(db, "scanActivity", a.id)));
                    await batch.commit();
                }
                notify(`${rows.length} scan note${rows.length === 1 ? "" : "s"} deleted.`, "ok");
            } catch (error) { throw new Error(describeError(error)); }
        },
        removeNote: "These notes leave the dashboard's history. The users' own saved scans in the app are not touched."
    });

    registerDataset("suggestions", {
        label: "Suggestions", noun: "suggestions", nounOne: "suggestion",
        items: () => suggestions,
        map: (s) => ({ id: s.collection + "/" + s.id, title: s.text || "(empty)", subtitle: s.kind, meta: timeAgo(s.createdAtUnix), tone: s.kind === "Allergen" ? "warn" : "pink" }),
        dimensions: [{ id: "kind", label: "Kind", of: (s) => s.kind, keys: ["Allergen", "Skin condition"] }],
        removedWord: "dismissed", foreverLabel: "Dismiss for good",
        removeNote: "They leave this list for everyone who uses the admin site.",
        remove: async (rows) => {
            try {
                for (const s of rows) await updateDoc(doc(db, s.collection, s.id), { status: "dismissed", reviewedAt: serverTimestamp() });
                suggestions = suggestions.filter((s) => !rows.includes(s));
                renderSuggestions();
                notify(`${rows.length} suggestion${rows.length === 1 ? "" : "s"} dismissed.`, "ok");
            } catch (error) { throw new Error(describeError(error)); }
        }
    });

    registerDataset("topBrands", {
        label: "Brands", noun: "brands", nounOne: "brand",
        items: () => allBrands,
        map: (b) => ({ id: b.id, title: b.name, subtitle: b.category, tone: b.status.toLowerCase() === "active" ? "ok" : "muted", value: b.scans, valueLabel: b.scans === 1 ? "scan" : "scans" }),
        dimensions: [
            { id: "brand", label: "Brand", of: (b) => b.name },
            { id: "category", label: "Category", of: (b) => b.category || null, none: "No category" },
            { id: "status", label: "Status", of: (b) => b.status || "Active" }
        ],
        measures: [{ id: "scans", label: "Scans", noun: "scans", of: (b) => b.scans }],
        open: (b) => { window.location.href = INVENTORY_PAGE + "?brand=" + encodeURIComponent(b.id); }
    });

    registerDataset("results", {
        label: "Today's results", noun: "results", nounOne: "result", countRows: false,
        items: () => ["harmful", "caution", "safe"].map((k) => ({ id: k, scans: todayResults[k] })),
        map: (r) => ({ id: r.id, title: STATUS_NAMES[r.id], tone: toneOf(r.id), value: r.scans, valueLabel: r.scans === 1 ? "scan" : "scans" }),
        dimensions: [{ id: "result", label: "Result", of: (r) => r.id, keys: ["harmful", "caution", "safe"], labelOf: (k) => STATUS_NAMES[k] || k, tone: toneOf, pick: (k) => setActivityFilter(k, true) }],
        measures: [{ id: "scans", label: "Scans today", noun: "scans today", of: (r) => r.scans }],
        open: (r) => setActivityFilter(r.id, true)
    });
}
