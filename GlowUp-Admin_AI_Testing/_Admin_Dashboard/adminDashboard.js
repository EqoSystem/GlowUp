








import { setBellNotes } from "../_Shared/headerAccount.js";
import { db } from "../_Database/firebase-console.js";
import {
    collection, query, where, orderBy, limit, doc, serverTimestamp
} from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";


import { getDocs, updateDoc, writeBatch, onListsRefreshed, getCountFromServer, onSnapshot } from "../_Shared/adminStore.js";
import { openReplyDrawer } from "../_Shared/replyDrawer.js";
import { archiveReport } from "../_Shared/adminArchive.js";
import {
    requireAdmin, wireSignOut, setupNavigationGuard, escapeHtml, asArray, toWholeNumber, timeAgo, formatWhen, startOfTodayUnix,
    notify, describeError, toneBadge
} from "../_Shared/adminCommon.js";
import { mountShell, paginate, renderPager, icon, askConfirm } from "../_Shared/adminShell.js";
import { bindButtons } from "../_Shared/shortcuts.js";
import { registerDataset, registerTileDataset, datasetChanged } from "../_Shared/adminWidgets.js";
import { setAssistPage, registerAssist, refreshAssist } from "../_Shared/adminAssist.js";
import { setPanelQuiet, setPanelSummary } from "../_Shared/adminFold.js";
import { openAuditLog } from "../_Shared/adminAuditLog.js";

const RECENT_ITEMS = 120;
const PAGE_SIZE = 8;
const REPORT_PAGE_SIZE = 10;
const activityPage = { page: 1, size: PAGE_SIZE };
const suggestionPage = { page: 1, size: REPORT_PAGE_SIZE };


const SUPPORT_KINDS = [
    { prefix: "support", dataset: "supportBug", label: "Bug reports", category: "Bug", title: "bug reports", none: "No new bug reports.", page: { page: 1, size: REPORT_PAGE_SIZE } },
    { prefix: "supportWrong", dataset: "supportWrong", label: "Wrong scan results", category: "Wrong scan result", title: "wrong scan results", none: "No new wrong scan results.", page: { page: 1, size: REPORT_PAGE_SIZE } },
    { prefix: "supportOther", dataset: "supportOther", label: "Other reports", category: "Other", title: "other reports", none: "No new reports.", page: { page: 1, size: REPORT_PAGE_SIZE } }
];
const kindOf = (category) => (SUPPORT_KINDS.find((k) => k.category === category) || SUPPORT_KINDS[2]);
let supportReports = [];
let recentScans = [];
const TOP_BRANDS = 5;
const TODAY_LIMIT = 200;
const COUNT_EVERY_MS = 60 * 1000;
let lastCountAt = 0;
let exactToday = null;

const el = (id) => document.getElementById(id);
let stopToday = null;
let stopSupportReports = null;
let todayStart = 0;
let suggestions = [];
let appSuggestions = [];
let typedSuggestions = [];
let allBrands = [];
let todayResults = { safe: 0, caution: 0, harmful: 0 };
let activityFilter = "";
let openScanId = "";

let scansToday = null, flaggedToday = 0;
let waitingInventory = null, waitingIngredients = null;
let suggestionsLoaded = false;
let stopCatalogueCards = [];

let todayAnswered = false, activityAnswered = false, brandsAnswered = false;
const many = (n, one, more) => `${n} ${n === 1 ? one : more}`;

const STATUS_NAMES = { safe: "Safe", caution: "Use with care", harmful: "Harmful" };
const statusOf = (a) => (a.status === "safe" || a.status === "harmful" ? a.status : "caution");
const toneOf = (status) => ({ safe: "ok", caution: "warn", harmful: "bad" }[status] || "muted");
const INVENTORY_PAGE = "../_Admin_Brand_Inventory/adminBrandInventory.html";

// ---- the Reports panel: one container, a button per kind of report in its header ----
const REPORT_VIEWS = ["bug", "wrong", "other", "suggestions"];
let reportView = "bug";
function showReportView(view) {
    if (!REPORT_VIEWS.includes(view)) return;
    reportView = view;
    REPORT_VIEWS.forEach((key) => {
        const on = key === view;
        const tab = el("rt_" + key), body = el("rv_" + key);
        if (tab) { tab.classList.toggle("on", on); tab.setAttribute("aria-selected", on ? "true" : "false"); tab.tabIndex = on ? 0 : -1; }
        if (body) body.hidden = !on;
    });
}
function paintReportTabs() {
    const counts = {
        bug: supportReports.filter((r) => kindOf(r.category) === SUPPORT_KINDS[0]).length,
        wrong: supportReports.filter((r) => kindOf(r.category) === SUPPORT_KINDS[1]).length,
        other: supportReports.filter((r) => kindOf(r.category) === SUPPORT_KINDS[2]).length,
        suggestions: suggestions.length
    };
    REPORT_VIEWS.forEach((key) => { const c = el("rtCount_" + key); if (c) c.textContent = counts[key] > 0 ? String(counts[key]) : ""; });
    const total = counts.bug + counts.wrong + counts.other + counts.suggestions;
    setPanelQuiet("reports", total === 0, total === 0 ? "Nothing waiting" : many(total, "item waits", "items wait"));
}
function wireReportTabs() {
    const bar = el("reportTabs");
    if (!bar) return;
    bar.addEventListener("click", (event) => { const b = event.target.closest("[data-view]"); if (b) showReportView(b.dataset.view); });
    bar.addEventListener("keydown", (event) => {                                   // arrow keys move between the tabs
        if (event.key !== "ArrowRight" && event.key !== "ArrowLeft") return;
        const i = REPORT_VIEWS.indexOf(reportView), next = REPORT_VIEWS[(i + (event.key === "ArrowRight" ? 1 : REPORT_VIEWS.length - 1)) % REPORT_VIEWS.length];
        showReportView(next); el("rt_" + next).focus(); event.preventDefault();
    });
    const wanted = (location.hash.match(/^#reports-(bug|wrong|other|suggestions)$/) || [])[1];
    showReportView(wanted || "bug");
}

document.addEventListener("DOMContentLoaded", async () => {
    registerData();
    mountShell("dashboard");
    wireReportTabs();
    bindButtons({ "dashboard.activityLog": "#auditLogTile", "dashboard.addProduct": 'a.tile[href*="add=product"]' });
    const auditTile = document.getElementById("auditLogTile");
    if (auditTile) auditTile.addEventListener("click", openAuditLog);
    setAssistPage("dashboard");
    registerHelpers();
    wireSignOut();
    setupNavigationGuard();
    await requireAdmin();

    watchCatalogueCards();
    onListsRefreshed(() => { /* live card listeners below are the source of truth */ });
    watchScansToday();
    watchRecentActivity();
    loadSuggestions();
    watchSupportReports();
    loadReviewQueue();
    el("suggestionList").addEventListener("click", onSuggestionClick);
    el("suggestionClearAll").addEventListener("click", clearAllSuggestions);
    SUPPORT_KINDS.forEach((kind) => {
        el(kind.prefix + "ClearAll").addEventListener("click", () => clearAllSupportReports(kind));
        el(kind.prefix + "List").addEventListener("click", onSupportClick);
    });


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


    window.setInterval(() => { if (startOfTodayUnix() !== todayStart) watchScansToday(); }, 60000);
    window.addEventListener("pagehide", stopCatalogueCardListeners);
    window.addEventListener("pageshow", (event) => { if (event.persisted) watchCatalogueCards(); });
});

function setMetric(id, value, title) {
    const box = el(id);
    if (!box) return;
    box.classList.remove("skeleton");
    box.textContent = value;
}




async function loadCatalogueCounts() {

    try {
        const snapshot = await getDocs(collection(db, "brands"));

        allBrands = snapshot.docs.filter((d) => d.data().binned !== true).map((d) => ({ id: d.id, name: d.data().name || "Unnamed Brand", category: d.data().category || "", status: String(d.data().status || "Active"), scans: toWholeNumber(d.data().scans) }));
        setMetric("totalBrandsCount", allBrands.length);
        allBrands.sort((a, b) => b.scans - a.scans || a.name.localeCompare(b.name));
        brandsAnswered = true;
        renderTopBrands(allBrands);
        datasetChanged("topBrands");
        refreshAssist("brands");
    } catch (error) {
        console.error("Error counting brands: ", error);
        brandsAnswered = true;
        refreshAssist("brands");
        setMetric("totalBrandsCount", "–", describeError(error));
        showEmpty("topBrands", "Could not load the brands.", true);
    }
    countInto("products", "totalProductsCount");
    countInto("ingredients", "totalIngredientsCount");
}

// The dashboard cards must move when another admin or the app changes the catalogue.
// The listener snapshots also keep the top-brand card in step with the three totals.
function stopCatalogueCardListeners() {
    stopCatalogueCards.forEach((stop) => stop());
    stopCatalogueCards = [];
}

function watchCatalogueCards() {
    stopCatalogueCardListeners();
    const watch = (name, done) => stopCatalogueCards.push(onSnapshot(collection(db, name), done, (error) => {
        console.error(`Error watching ${name} for dashboard cards: `, error);
        const target = name === "brands" ? "totalBrandsCount" : name === "products" ? "totalProductsCount" : "totalIngredientsCount";
        setMetric(target, "–", describeError(error));
    }));
    watch("brands", (snapshot) => {
        allBrands = snapshot.docs.filter((d) => d.data().binned !== true).map((d) => ({ id: d.id, name: d.data().name || "Unnamed Brand", category: d.data().category || "", status: String(d.data().status || "Active"), scans: toWholeNumber(d.data().scans) }));
        setMetric("totalBrandsCount", allBrands.length);
        allBrands.sort((a, b) => b.scans - a.scans || a.name.localeCompare(b.name));
        brandsAnswered = true;
        renderTopBrands(allBrands);
        datasetChanged("topBrands");
        refreshAssist("brands");
    });
    watch("products", (snapshot) => setMetric("totalProductsCount", snapshot.docs.filter((d) => d.data().binned !== true).length));
    watch("ingredients", (snapshot) => setMetric("totalIngredientsCount", snapshot.docs.filter((d) => d.data().binned !== true).length));
}

async function countInto(collectionName, elementId) {
    try {

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

    setPanelQuiet("brands", ranked.length === 0, ranked.length === 0 ? "No brand scanned yet" : `${ranked[0].name} leads with ${many(ranked[0].scans, "scan", "scans")}`);
    if (ranked.length === 0) { showEmpty("topBrands", "No scans recorded for any brand yet.", false); return; }
    el("topBrandsEmpty").style.display = "none";
    const list = el("topBrandsList");
    list.style.display = "";
    list.innerHTML = ranked.map((b, i) => `
        <li class="rank-item can-open" data-brand="${escapeHtml(b.id)}">
            <span class="rank-number">${i + 1}</span>
            <span class="rank-name" title="${escapeHtml(b.name)}">${escapeHtml(b.name)}</span>
            <span class="rank-value">${b.scans} ${b.scans === 1 ? "scan" : "scans"}</span>
        </li>`).join("");
}


function showEmpty(prefix, message, isError) {
    const list = el(prefix === "topBrands" ? "topBrandsList" : prefix + "List");
    if (list) list.style.display = "none";
    const box = el(prefix + "Empty");
    const text = el(prefix + "EmptyMessage");
    if (box) { box.style.display = ""; box.classList.remove("loading"); }
    if (text) { text.textContent = message; text.classList.toggle("error-text", !!isError); }
}




async function loadReviewQueue() {

    const waiting = async (collectionName, ...conditions) => {
        const [all, binned] = await Promise.all([
            getCountFromServer(query(collection(db, collectionName), ...conditions, where("reviewed", "==", false))),
            getCountFromServer(query(collection(db, collectionName), ...conditions, where("reviewed", "==", false), where("binned", "==", true)))
        ]);
        return Math.max(0, all.data().count - binned.data().count);
    };


    const shapeGone = () => { const shape = el("reviewTilesLoading"); if (shape) shape.remove(); };
    try {
        const [brandCount, productCount] = await Promise.all([waiting("brands"), waiting("products")]);
        waitingInventory = brandCount + productCount;
        if (brandCount + productCount > 0) {
            const parts = [];
            if (productCount > 0) parts.push(many(productCount, "product", "products"));
            if (brandCount > 0) parts.push(many(brandCount, "brand", "brands"));
            el("reviewInventoryTitle").textContent = parts.join(" and ") + " waiting for review";
            el("reviewInventoryTitle").dataset.base = el("reviewInventoryTitle").textContent;
            paintNewProductReports(newProductCount);
            shapeGone();
            el("reviewInventoryTile").style.display = "";
        }
    } catch (error) {
        console.warn("Could not count the inventory entries waiting for review: ", error);
    }
    try {
        const ingredientCount = await waiting("ingredients", where("source", "==", "ai"));
        waitingIngredients = ingredientCount;
        if (ingredientCount > 0) {
            el("reviewIngredientsTitle").textContent = many(ingredientCount, "AI-found ingredient", "AI-found ingredients") + " waiting for review";
            shapeGone();
            el("reviewIngredientsTile").style.display = "";
        }
    } catch (error) {
        console.warn("Could not count the ingredients waiting for review: ", error);
    }
    shapeGone();

    const waitingNow = (waitingInventory || 0) + (waitingIngredients || 0);
    setPanelQuiet("actions", waitingNow === 0, waitingNow === 0 ? "Nothing is waiting for review" : `${many(waitingNow, "entry waits", "entries wait")} for review`);
}




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
        todayAnswered = true;
        scansToday = capped && exactToday !== null ? exactToday : snapshot.size;
        flaggedToday = flagged;
        renderResults(results);
        datasetChanged("results");
        refreshAssist("numbers");
        setMetric("totalScansCount", capped ? (exactToday !== null ? exactToday : TODAY_LIMIT + "+") : snapshot.size, "Scans made in the app since midnight");
        setMetric("flaggedAllergensCount", flagged, "Scans today in which one of the user's own allergens was found" + (capped ? " (among the latest " + TODAY_LIMIT + ")" : ""));
    }, (error) => {
        console.error("Error watching today's scans: ", error);
        todayAnswered = true;
        refreshAssist("numbers");
        setMetric("totalScansCount", "–", describeError(error));
        setMetric("flaggedAllergensCount", "–", describeError(error));
    });
}


function renderResults(results) {
    const total = results.safe + results.caution + results.harmful;

    setPanelQuiet("results", total === 0, total === 0 ? "No scans yet today" : `${many(total, "scan", "scans")} today: ${results.harmful} harmful, ${results.caution} with care, ${results.safe} safe`);
    const names = { safe: "Safe", caution: "Use with care", harmful: "Harmful" };
    el("resultBar").innerHTML = total === 0 ? "" : Object.keys(names).filter((k) => results[k] > 0)
        .map((k) => `<span class="${k}" data-status="${k}" style="flex-grow:${results[k]}" data-tip-title="${results[k]} scans" data-tip="${names[k]}"></span>`).join("");
    el("resultLegend").innerHTML = Object.keys(names).map((k) =>
        `<li data-status="${k}" class="${activityFilter === k ? "on" : ""}"><span class="legend-name ${k}">${names[k]}</span><span class="num">${results[k]}${total > 0 ? ` <span class="muted small">${Math.round(results[k] * 100 / total)}%</span>` : ""}</span></li>`).join("");
}


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
        activityAnswered = true;
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
        activityAnswered = true;
        refreshAssist("activity");
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




    const verdict = toneBadge(STATUS_NAMES[status], toneOf(status), status === "safe" ? "Nothing in it was flagged for this user." : status === "harmful" ? "Something in it is flagged for this user." : "Something in it is worth watching for this user.");


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




async function loadSuggestions() {
    const sources = [
        { name: "allergenSuggestions", kind: "Allergen" }
    ];
    typedSuggestions = [];
    let failed = null;
    for (const source of sources) {
        try {
            const snapshot = await getDocs(query(collection(db, source.name), where("status", "==", "pending")));
            snapshot.forEach((d) => typedSuggestions.push({
                id: d.id, collection: source.name, kind: source.kind,
                text: d.data().text || "", createdAtUnix: toWholeNumber(d.data().createdAtUnix), lastScan: null
            }));
        } catch (error) {
            console.error(`Error loading ${source.name}: `, error);
            failed = error;
        }
    }
    suggestions = typedSuggestions.concat(appSuggestions);
    if (failed && suggestions.length === 0) {
        suggestionsLoaded = true;
        refreshAssist("suggestions");
        showEmpty("suggestion", "Could not load the suggestions.", true);
        el("suggestionEmptyHint").textContent = describeError(failed);
        return;
    }
    renderSuggestions();
}

function renderSuggestions() {
    suggestionsLoaded = true;
    el("suggestionClearAll").hidden = suggestions.length === 0;
    datasetChanged("suggestions"); datasetChanged("reports");
    refreshAssist("suggestions");
    paintReportTabs();
    if (suggestions.length === 0) { showEmpty("suggestion", "No suggestions waiting for review.", false); return; }
    suggestions.sort((a, b) => b.createdAtUnix - a.createdAtUnix);
    el("suggestionEmpty").style.display = "none";
    const list = el("suggestionList");
    list.style.display = "";
    renderPager(el("suggestionPager"), suggestionPage, suggestions.length, renderSuggestions, "suggestions");
    list.innerHTML = paginate(suggestions, suggestionPage).map((s) => `
        <li class="suggestion-item report-item">
            <div class="suggestion-main">
                <div class="report-head">
                    <span class="chip ${s.kind === "Allergen" ? "warn" : s.kind === "App suggestion" ? "good" : "pink"}">${escapeHtml(s.kind)}</span>
                    <span class="report-when small muted">${escapeHtml(formatWhen(s.createdAtUnix))} · ${escapeHtml(timeAgo(s.createdAtUnix))}</span>
                </div>
                <p class="report-text suggestion-text">${escapeHtml(s.text || "(empty)")}</p>
                ${s.from ? `<p class="report-from small muted">${escapeHtml(s.from)}</p>` : ""}
                ${lastScanDetails(s.lastScan)}
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

        if (collectionName === "supportReports") await updateDoc(doc(db, collectionName, id), { status: status === "reviewed" ? "handled" : status, handledAt: serverTimestamp() });
        else await updateDoc(doc(db, collectionName, id), { status, reviewedAt: serverTimestamp() });
        suggestions = suggestions.filter((s) => !(s.id === id && s.collection === collectionName));
        if (collectionName === "supportReports") appSuggestions = appSuggestions.filter((s) => s.id !== id);
        else typedSuggestions = typedSuggestions.filter((s) => !(s.id === id && s.collection === collectionName));
        renderSuggestions();
        notify(status === "reviewed" ? "Marked as reviewed." : "Suggestion dismissed.", "ok");
    } catch (error) {
        console.error("Error updating suggestion: ", error);
        notify("The suggestion was not updated. " + describeError(error), "error");
        button.disabled = false;
    }
}




// "New product" reports from the app (Send for Review): counted on the review tile, opened in the inventory's Review queue
let newProductCount = 0;
function paintNewProductReports(count) {
    const tile = el("reviewInventoryTile"), title = el("reviewInventoryTitle");
    if (!tile || !title) return;
    newProductCount = count;
    const base = title.dataset.base || "";
    if (count > 0) {
        title.textContent = (base ? base.replace(/ waiting for review$/, "") + " and " : "") + many(count, "user report", "user reports") + " waiting for review";
        const shape = el("reviewTilesLoading"); if (shape) shape.remove();
        tile.style.display = "";
    } else if (base) title.textContent = base;
}

function watchSupportReports() {
    if (stopSupportReports) stopSupportReports();
    stopSupportReports = onSnapshot(query(collection(db, "supportReports"), where("status", "==", "new")), (snapshot) => {
        supportReports = [];
        appSuggestions = [];
        let newProductReports = 0;
        snapshot.forEach((d) => {
            const data = d.data();
            const lastScan = data.lastScan && typeof data.lastScan === "object" ? data.lastScan : null;
            if (String(data.category || "") === "New product") { newProductReports++; return; }       // they wait in the inventory's Review queue
            if (String(data.category || "") === "Suggestion") {

                appSuggestions.push({ id: d.id, collection: "supportReports", kind: "App suggestion", text: String(data.message || ""), createdAtUnix: toWholeNumber(data.createdAtUnix), lastScan, from: senderText({ userName: String(data.userName || "").trim(), uid: String(data.uid || "") }) });
                return;
            }
            supportReports.push({
                id: d.id, category: String(data.category || "Other"), message: String(data.message || ""), uid: String(data.uid || ""), userName: String(data.userName || "").trim(),
                appVersion: String(data.appVersion || ""), device: String(data.device || ""), createdAtUnix: toWholeNumber(data.createdAtUnix),
                lastScan
            });
        });
        renderSupportReports();
        paintNewProductReports(newProductReports);
        suggestions = typedSuggestions.concat(appSuggestions);
        if (suggestionsLoaded) renderSuggestions();
    }, (error) => {
        console.error("Error loading supportReports: ", error);
        SUPPORT_KINDS.forEach((kind) => {
            showEmpty(kind.prefix, "Could not load the reports.", true);
            el(kind.prefix + "EmptyHint").textContent = describeError(error);
        });
    });
}

function renderSupportReports() {
    SUPPORT_KINDS.forEach((kind) => renderSupportKind(kind));
}

function renderSupportKind(kind) {
    const mine = supportReports.filter((r) => kindOf(r.category) === kind);
    datasetChanged(kind.dataset); datasetChanged("reports");
    el(kind.prefix + "ClearAll").hidden = mine.length === 0;
    paintReportTabs();
    if (mine.length === 0) { showEmpty(kind.prefix, kind.none, false); return; }
    mine.sort((a, b) => b.createdAtUnix - a.createdAtUnix);
    el(kind.prefix + "Empty").style.display = "none";
    const list = el(kind.prefix + "List");
    list.style.display = "";
    renderPager(el(kind.prefix + "Pager"), kind.page, mine.length, () => renderSupportKind(kind), "reports");
    const tone = (c) => (c === "Bug" ? "bad" : c === "Wrong scan result" ? "warn" : "pink");
    list.innerHTML = paginate(mine, kind.page).map((r) => {
        const scan = lastScanDetails(r.lastScan);
        return `<li class="suggestion-item support-item report-item">
            <div class="suggestion-main">
                <div class="report-head">
                    <span class="chip ${tone(r.category)}">${escapeHtml(r.category)}</span>
                    <span class="report-when small muted">${escapeHtml(formatWhen(r.createdAtUnix))} · ${escapeHtml(timeAgo(r.createdAtUnix))}</span>
                </div>
                <p class="support-message report-text">${escapeHtml(r.message)}</p>
                <p class="report-from small muted">${senderHtml(r)}</p>
                ${scan}
            </div>
            <div class="suggestion-actions report-actions">
                <button class="btn small-btn reply-btn" data-action="reply" data-id="${escapeHtml(r.id)}"${r.uid ? "" : " disabled"}>${icon("mail")}Reply</button>
                <button class="btn small-btn report-delete" data-action="delete" data-id="${escapeHtml(r.id)}">${icon("trash")}Delete</button>
                <span class="btn-group">
                    <button class="icon-btn small row-btn approve" data-action="handled" data-id="${escapeHtml(r.id)}" aria-label="Mark handled">${icon("check")}</button>
                </span>
            </div>
        </li>`;
    }).join("");
}





function senderText(r) {
    return r.userName || "GlowUp User";
}
function senderHtml(r) {
    return `<b title="Name given by the sender">${escapeHtml(r.userName || "GlowUp User")}</b>`;
}


function lastScanDetails(scan) {
    if (!scan) return "";
    return `<details class="support-scan"><summary>Last scan: ${escapeHtml([scan.product, scan.brand].filter(Boolean).join(" · ") || "(no name)")}</summary>
            <p class="small">Result: ${escapeHtml(scan.result || "-")}. ${escapeHtml(scan.summary || "")}</p>
            <p class="small muted">Ingredients read: ${escapeHtml(scan.ingredients || "-")}</p></details>`;
}





async function dismissAll(items, fields) {
    for (let i = 0; i < items.length; i += 400) {
        const batch = writeBatch(db);
        items.slice(i, i + 400).forEach((item) => batch.update(doc(db, item.collection, item.id), fields(item)));
        await batch.commit();
    }
}

async function clearAllSuggestions() {
    if (suggestions.length === 0) return;
    const ok = await askConfirm({
        title: "Clear all suggestions?", danger: true, confirmLabel: "Clear all",
        message: `All ${suggestions.length} suggestions leave this list (they are marked dismissed, not deleted).`
    });
    if (!ok) return;
    const button = el("suggestionClearAll");
    button.disabled = true;
    try {
        await dismissAll(suggestions, (item) => item.collection === "supportReports"
            ? { status: "dismissed", handledAt: serverTimestamp() }
            : { status: "dismissed", reviewedAt: serverTimestamp() });
        const count = suggestions.length;
        suggestions = []; typedSuggestions = []; appSuggestions = [];
        renderSuggestions();
        notify(`${count} suggestions cleared.`, "ok");
    } catch (error) {
        console.error("Error clearing suggestions: ", error);
        notify("The suggestions were not cleared. " + describeError(error), "error");
    } finally { button.disabled = false; }
}

async function clearAllSupportReports(kind) {
    const mine = supportReports.filter((r) => kindOf(r.category) === kind);
    if (mine.length === 0) return;
    const ok = await askConfirm({
        title: `Clear all ${kind.title}?`, danger: true, confirmLabel: "Clear all",
        message: `All ${mine.length} ${kind.title} leave this list (they are marked dismissed, not deleted).`
    });
    if (!ok) return;
    const button = el(kind.prefix + "ClearAll");
    button.disabled = true;
    try {
        await dismissAll(mine.map((r) => ({ id: r.id, collection: "supportReports" })), () => ({ status: "dismissed", handledAt: serverTimestamp() }));
        supportReports = supportReports.filter((r) => kindOf(r.category) !== kind);
        renderSupportReports();
        notify(`${mine.length} ${kind.title} cleared.`, "ok");
    } catch (error) {
        console.error("Error clearing support reports: ", error);
        notify("The reports were not cleared. " + describeError(error), "error");
    } finally { button.disabled = false; }
}

async function onSupportClick(event) {
    const button = event.target.closest("button[data-action]");
    if (!button) return;
    const id = button.getAttribute("data-id"), status = button.getAttribute("data-action");
    const report = supportReports.find((r) => r.id === id);
    // Reply opens the reply window; Delete moves the report to the Archive for 30 days (asks first; it can be restored there)
    if (status === "reply") {
        if (report) openReplyDrawer(report, { onSent: () => { supportReports = supportReports.filter((r) => r.id !== id); renderSupportReports(); } });
        return;
    }
    if (status === "delete") {
        const sure = await askConfirm({ title: "Delete this report?", message: "The report moves to the Archive. It stays there for 30 days and can be restored from the Archive page.", items: report ? [report.message.slice(0, 120)] : [], confirmLabel: "Delete", danger: true });
        if (!sure) return;
        button.disabled = true;
        try {
            await archiveReport({ id });
            supportReports = supportReports.filter((r) => r.id !== id);
            renderSupportReports();
            notify("Report moved to the Archive.", "ok");
        } catch (error) {
            console.error("Error deleting support report: ", error);
            notify("The report was not deleted. " + describeError(error), "error");
            button.disabled = false;
        }
        return;
    }
    button.disabled = true;
    try {
        await updateDoc(doc(db, "supportReports", id), { status, handledAt: serverTimestamp() });
        supportReports = supportReports.filter((r) => r.id !== id);
        renderSupportReports();
        notify(status === "handled" ? "Marked as handled." : "Report dismissed.", "ok");
    } catch (error) {
        console.error("Error updating support report: ", error);
        notify("The report was not updated. " + describeError(error), "error");
        button.disabled = false;
    }
}




function registerHelpers() {
    const percent = (part, whole) => (whole > 0 ? Math.round(part * 100 / whole) : 0);
    const hourName = (hour) => new Date(2000, 0, 1, hour).toLocaleTimeString(undefined, { hour: "numeric" });

    setBellNotes(() => {
        if (!todayAnswered) return null;
        if (scansToday === null) return [];

        const lines = [];
        if (scansToday > 0) {
            const hours = new Map();
            recentScans.forEach((a) => { if (toWholeNumber(a.scannedAtUnix) >= todayStart) { const hour = new Date(a.scannedAtUnix * 1000).getHours(); hours.set(hour, (hours.get(hour) || 0) + 1); } });
            const busiest = Array.from(hours).sort((a, b) => b[1] - a[1])[0];
            if (busiest) lines.push({ tone: "info", text: `Busiest hour today so far: ${hourName(busiest[0])}, with ${many(busiest[1], "scan", "scans")}.` });
            if (flaggedToday > 0) lines.push({ tone: "warn", text: `${flaggedToday} of today's ${many(scansToday, "scan", "scans")} matched one of the user's own allergens.`, label: "Show them", run: () => setActivityFilter("flagged", true) });
        }

        const yesterday = todayStart - 86400;
        const oldest = recentScans.length > 0 ? toWholeNumber(recentScans[recentScans.length - 1].scannedAtUnix) : 0;
        if (recentScans.length > 0 && (recentScans.length < RECENT_ITEMS || oldest < yesterday)) {
            const count = recentScans.filter((a) => a.scannedAtUnix >= yesterday && a.scannedAtUnix < todayStart).length;
            lines.push({ text: `Yesterday there ${count === 1 ? "was 1 scan" : "were " + count + " scans"} in all.` });
        }
        return lines;
    });

    registerAssist("activity", () => {
        if (!activityAnswered) return null;
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




    registerAssist("suggestions", () => {
        if (!suggestionsLoaded) return null;
        if (suggestions.length === 0) return [];
        const allergens = suggestions.filter((s) => s.kind === "Allergen").length, conditions = suggestions.length - allergens;
        const parts = [];
        if (allergens > 0) parts.push(many(allergens, "allergen", "allergens"));
        if (conditions > 0) parts.push(many(conditions, "chronic skin condition", "chronic skin conditions"));
        const oldest = suggestions.reduce((least, s) => (s.createdAtUnix > 0 && s.createdAtUnix < least ? s.createdAtUnix : least), Infinity);
        return [{ tone: "info", text: `${parts.join(" and ")} suggested by app users.` + (Number.isFinite(oldest) ? ` Oldest: ${timeAgo(oldest)}.` : ""), label: "Curator", run: () => { window.location.href = "../_Admin_Ingredient_Curator/adminIngredientCurator.html"; }, tip: "Add what is useful to the Ingredient Curator, then mark the suggestion reviewed." }];
    });



}




function registerData() {
    const two = (n) => String(n).padStart(2, "0");
    const dayKey = (unix) => { const d = new Date(unix * 1000); return `${d.getFullYear()}-${two(d.getMonth() + 1)}-${two(d.getDate())}`; };
    const dayLabel = (key) => { const p = key.split("-").map(Number); return p.length === 3 && !p.some(isNaN) ? new Date(p[0], p[1] - 1, p[2]).toLocaleDateString(undefined, { day: "numeric", month: "short" }) : key; };

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
        dimensions: [{ id: "kind", label: "Kind", of: (s) => s.kind, keys: ["Allergen"] }],
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


    SUPPORT_KINDS.forEach((kind) => registerDataset(kind.dataset, {
        label: kind.label, noun: kind.title, nounOne: kind.title.replace(/s$/, ""),
        items: () => supportReports.filter((r) => kindOf(r.category) === kind),
        map: (r) => ({ id: r.id, title: r.message || "(empty)", subtitle: senderText(r), meta: timeAgo(r.createdAtUnix), tone: r.category === "Bug" ? "bad" : r.category === "Wrong scan result" ? "warn" : "pink" }),
        dimensions: [
            { id: "who", label: "Sender (by account)", of: (r) => r.uid ? "account " + r.uid.slice(0, 8) : "unknown" },
            { id: "version", label: "App version", of: (r) => r.appVersion || null, none: "Unknown" }
        ],
        removedWord: "dismissed", foreverLabel: "Dismiss for good", removeNote: "They leave this list for everyone who uses the admin site.",
        remove: async (rows) => {
            try {
                await dismissAll(rows.map((r) => ({ id: r.id, collection: "supportReports" })), () => ({ status: "dismissed", handledAt: serverTimestamp() }));
                supportReports = supportReports.filter((r) => !rows.includes(r));
                renderSupportReports();
                notify(`${rows.length} ${rows.length === 1 ? kind.title.replace(/s$/, "") : kind.title} dismissed.`, "ok");
            } catch (error) { throw new Error(describeError(error)); }
        }
    }));


    // the merged Reports panel, all four tabs in one list (Show as row, grid or graph by kind)
    const reportKind = (r) => r.collection ? "Suggestion" : (kindOf(r.category) || { label: "Other reports" }).label;
    registerDataset("reports", {
        label: "Reports", noun: "reports", nounOne: "report",
        items: () => supportReports.concat(suggestions),
        map: (r) => r.collection
            ? { id: r.collection + "/" + r.id, title: r.text || "(empty)", subtitle: "Suggestion: " + r.kind, meta: timeAgo(r.createdAtUnix), tone: "pink" }
            : { id: r.id, title: r.message || "(empty)", subtitle: senderText(r), meta: timeAgo(r.createdAtUnix), tone: r.category === "Bug" ? "bad" : r.category === "Wrong scan result" ? "warn" : "pink" },
        dimensions: [{ id: "kind", label: "Kind", of: reportKind, keys: ["Bug reports", "Wrong scan results", "Other reports", "Suggestion"] }],
        removedWord: "dismissed", foreverLabel: "Dismiss for good", removeNote: "They leave this list for everyone who uses the admin site.",
        remove: async (rows) => {
            try {
                const tips = rows.filter((r) => r.collection), reports = rows.filter((r) => !r.collection);
                if (reports.length) await dismissAll(reports.map((r) => ({ id: r.id, collection: "supportReports" })), () => ({ status: "dismissed", handledAt: serverTimestamp() }));
                for (const s of tips) await updateDoc(doc(db, s.collection, s.id), { status: "dismissed", reviewedAt: serverTimestamp() });
                supportReports = supportReports.filter((r) => !rows.includes(r));
                suggestions = suggestions.filter((s) => !rows.includes(s));
                renderSupportReports(); renderSuggestions();
                notify(`${rows.length} report${rows.length === 1 ? "" : "s"} dismissed.`, "ok");
            } catch (error) { throw new Error(describeError(error)); }
        }
    });

    registerTileDataset("numbers", {
        label: "Numbers", noun: "numbers", nounOne: "number", valueNoun: "Number", selector: ".kpi-row .kpi", watch: ".kpi-row",
        map: (tile) => ({ title: (tile.querySelector(".kpi-title") || {}).textContent || "", value: parseFloat(((tile.querySelector(".kpi-value") || {}).textContent || "0").replace(/[^\d.]/g, "")) || 0, valueLabel: "", tone: "info" }),
        open: (tile) => tile.click()
    });
    registerTileDataset("quickActions", {
        label: "Quick actions", noun: "shortcuts", nounOne: "shortcut", selector: ".tile-stack a.tile, .tile-stack button.tile", watch: ".tile-stack",
        map: (tile) => ({ title: tile.getAttribute("data-tip-title") || tile.textContent.trim(), subtitle: tile.classList.contains("review") ? "waiting for review" : "", value: 1, tone: tile.classList.contains("review") ? "info" : "muted" }),
        open: (tile) => tile.click()
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
