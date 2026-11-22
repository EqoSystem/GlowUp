























import { db } from "../_Database/firebase-console.js";
import { collection, doc, serverTimestamp, deleteField } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { refreshRecommendabilityFlags } from "../_Shared/recommendabilityAdmin.js";
// reading and writing go through the site's own copy of its lists: a list is downloaded once and kept between pages,
// and after a change only the documents that were written are read again (see adminStore.js)
import { getDocs, addDoc, updateDoc, writeBatch, onListsRefreshed, onSnapshot } from "../_Shared/adminStore.js";
import {
    requireAdmin, wireSignOut, setupNavigationGuard, setupModal, escapeHtml, sameKey, toList, asArray, toWholeNumber, toDate, timeAgo,
    notify, describeError, setBusy, setFormError, renderChecks, readChecks, writeChecks, originLabel, sourceLinks, sourceGuideHtml, wireSearch,
    SKIN_TYPES, dialogIsOpen, statusBadge, toneBadge
} from "../_Shared/adminCommon.js";
import { matchesSearch, normalizeName, evidenceProblem } from "../_Shared/glowupRules.js";
import { auth } from "../_Database/firebase-console.js";
import { cleanupPlan, ingredientDuplicates, ingredientLookalikesAsync, lookalikeCount, ingredientIssues, mergeIngredients } from "../_Shared/glowupCatalog.js";
import { openPlanDialog, openDuplicatesDialog, showScan } from "../_Shared/adminCleanup.js";
import { attachCounters } from "../_Shared/charLimit.js";
import { mountShell, placeInHeader, paginate, renderPager, icon, BIN_ICON, openMenu, askConfirm, setBinCount } from "../_Shared/adminShell.js";
import { bindButtons } from "../_Shared/shortcuts.js";
import { readAdminDocument } from "../_Shared/adminDocumentReader.js";
import { startImport } from "../_Shared/importReview.js";
import { parseImportText, inventoryNotHere } from "../_Shared/importFormat.js";
import { loadLists, optionsOf, allOptions, openListManager, listsNow } from "../_Shared/adminLists.js";
import { BUILT_IN_HIGHLIGHTS } from "../_Shared/listRules.js";
import { registerDataset, registerTileDataset, datasetChanged } from "../_Shared/adminWidgets.js";
import { createOrganizer, cleanFolder, friendly } from "../_Shared/adminOrganizer.js";
import { setAssistPage, registerAssist, refreshAssist } from "../_Shared/adminAssist.js";
import { setPageSettings } from "../_Shared/adminSettingsMenu.js";
import { loadClassifications, classifications, classificationName, openClassificationManager, askNewClassification, openFieldMigration, addStartingClassifications, TEST_MODE } from "../_Shared/adminClassifications.js";
import { makeCombo } from "../_Shared/adminSelect.js";
import { wireWindow } from "../_Shared/adminWindow.js";
import { mountSourceLinks, countSaved } from "./sourceLinks.js";
import { SOURCE_LINK_FIELDS } from "../_Shared/fieldSources.js";
import { FIELD_GUIDE, SOURCES, linksFor, allSourcesFor, addReference, labelBareUrls, pubchemLookupUrl, firstCompoundId, pubchemPageUrl, insertPaste } from "../_Shared/fieldSources.js";
import { aliasesWithInci, firstAlias, needsClassification, MAX_PURPOSE, CLASSIFICATION_REQUIRED_ON_EXISTING, PURPOSE_REQUIRED_ON_EXISTING } from "../_Shared/classificationRules.js";

const pageState = { page: 1, size: 10 };
const BATCH_LIMIT = 400;
const VIEW_KEY = "glowup.curator.view.v1";




const AUTO_APPROVE_AI = true;






const NEVER_AUTO = ["document-import", "snapshot"];






const hasCredibleSource = (item) => !NEVER_AUTO.includes(item.via) && (item.riskLevel === "caution" || item.riskLevel === "harmful")
    && evidenceProblem(item.riskLevel, item.sources) === "";

let everyIngredient = [];
let ingredients = [];
let binIngredients = [];
let productUse = new Map();
let productUseKnown = false;
let editingIngredientId = null;
let modal = null;
let sourceUi = null;
let curatorLiveStops = [];
let curatorLiveReady = new Set();
let curatorLiveRefresh = false;

function paintSourceCount() {
    const chip = el("ingredientSourceCount");
    if (!chip || !sourceUi) return;
    const { n, total } = sourceUi.count();
    chip.textContent = `Sources: ${n} of ${total} fields`;
    chip.classList.toggle("ok", total > 0 && n === total);
}
let windowKit = { reset() {} };
let fromProducts = [];
let fdaBrands = [];
let fieldFound = {};
let definitionBasis = null;
let assisted = null;
let org = null;
let openId = "";
let quick = "";
const view = { sort: "name", layout: "rows" };

const el = (id) => document.getElementById(id);
const RISK_LEVELS = ["safe", "caution", "harmful"];
const RISK_NAMES = { harmful: "Harmful", caution: "Caution", safe: "Safe", auto: "App decides" };
const FDA_SUBSTANCE_PAGE = "https://precision.fda.gov/uniisearch/srs/unii/";
const titleOf = (item) => item.name || item.techName || "Unnamed Ingredient";
const byName = (a, b) => titleOf(a).localeCompare(titleOf(b), undefined, { sensitivity: "base" });
const many = (n, one, more) => `${n} ${n === 1 ? one : more}`;
const toneOfRisk = (risk) => ({ harmful: "bad", caution: "warn", safe: "ok" }[risk] || "muted");

const brokenText = (text) => /System\.Collections\./i.test(String(text || ""));


const needsReview = (item) => item.source === "ai" && !item.reviewed;

const reviewer = () => (auth.currentUser ? auth.currentUser.uid : "");


const FILTERS = [
    { value: "", label: "All ingredients", test: () => true },
    { value: "review", label: "AI-found, waiting for review", test: needsReview },
    { value: "ai", label: "AI-found (all)", test: (item) => item.source === "ai" },
    { value: "starter", label: "Starter data", test: (item) => item.source === "starter" || item.source === "fda" },
    { value: "admin", label: "Origin not recorded", test: (item) => item.source === "" || item.source === "admin" }
];

const FROM_PRODUCTS = "products";
const NO_CLASS = "__none";
const ADD_CLASS = "__add";
let classCombo = null;
const lacksClass = (item) => needsClassification(item, classifications());


const RISK_ORDER = { harmful: 0, caution: 1, "": 2, safe: 3 };
const SORTS = [
    { id: "name", label: "Name A to Z", short: "A to Z", cmp: byName },
    { id: "nameDesc", label: "Name Z to A", short: "Z to A", cmp: (a, b) => byName(b, a) },
    { id: "risk", label: "Most harmful first", short: "most harmful", cmp: (a, b) => RISK_ORDER[a.riskLevel] - RISK_ORDER[b.riskLevel] || byName(a, b) },
    { id: "review", label: "Waiting for review first", short: "waiting first", cmp: (a, b) => Number(needsReview(b)) - Number(needsReview(a)) || byName(a, b) },
    { id: "used", label: "Used in most products", short: "most used", cmp: (a, b) => usedInProducts(b) - usedInProducts(a) || byName(a, b) },
    { id: "new", label: "Newest first", short: "newest", cmp: (a, b) => b.addedAt - a.addedAt || byName(a, b) }
];
const sortNow = () => SORTS.find((s) => s.id === view.sort) || SORTS[0];


const QUICK = {
    broken: { label: "Broken \"Used in\" text", test: (item) => item.usesBroken },
    noNote: { label: "Flagged without a caution note", test: (item) => (item.riskLevel === "harmful" || item.riskLevel === "caution") && !item.cautionNote },
    unused: { label: "Not in any listed product", test: (item) => productUseKnown && usedInProducts(item) === 0 },
    noEvidence: { label: "Harmful without a regulator's page", test: (item) => item.riskLevel === "harmful" && evidenceProblem("harmful", item.sources) !== "" },
    noPurpose: { label: "No purpose yet", test: (item) => !item.purpose }
};

document.addEventListener("DOMContentLoaded", async () => {
    try { const saved = JSON.parse(localStorage.getItem(VIEW_KEY) || "{}"); if (SORTS.some((s) => s.id === saved.sort)) view.sort = saved.sort; } catch (error) {                   }
    registerData();
    mountShell("curator");

    bindButtons({ "curator.addIngredient": "#addIngredientBtn", "curator.import": "#chooseIngredientFileBtn", "curator.classifications": "#manageClassBtn",
        "curator.sort": "#sortMenuBtn" });
    placeInHeader(".search-container", "headerActions");
    wireSignOut();
    setupNavigationGuard();
    await requireAdmin();

    if (TEST_MODE) {
        let hidden = false;
        try { hidden = sessionStorage.getItem("glowup.testBannerHidden") === "1"; } catch (error) { /* storage blocked: show it */ }
        if (!hidden) {
            const banner = document.createElement("div");
            banner.className = "test-banner";
            banner.setAttribute("role", "note");
            const text = document.createElement("p");
            text.className = "test-banner-text";
            text.innerHTML = "<strong>Test copy.</strong> The classifications on this address are a separate test list. Saving an ingredient and the field change are switched off here, so nothing live is changed.";
            const close = document.createElement("button");
            close.type = "button";
            close.className = "test-banner-close";
            close.setAttribute("aria-label", "Hide the test copy notice");
            close.textContent = "Hide";
            close.addEventListener("click", () => { banner.remove(); try { sessionStorage.setItem("glowup.testBannerHidden", "1"); } catch (error) { /* ignore */ } });
            banner.append(text, close);
            document.querySelector("main.main-content > header.topbar").after(banner);
        }
    }
    await loadLists();
    await loadClassifications();

    if (classifications().length === 0) {
        try { await addStartingClassifications(); } catch (error) { console.warn("The starting classifications could not be added:", error); }
    }
    renderFit(el("skinTypesFit"), "skinType", SKIN_TYPES);
    renderPickers(null);
    el("addIngredientForm").addEventListener("click", (event) => {
        const button = event.target.closest("[data-list-edit]");
        if (!button) return;
        if (button.dataset.listEdit === "classifications") { manageClassifications(); return; }
        openListManager(button.dataset.listEdit, {
            ingredients: () => everyIngredient,
            onSaved: async () => { renderPickers(editingIngredientId ? everyIngredient.find((i) => i.id === editingIngredientId) || null : null); renderIngredients(); }
        });
    });

    modal = setupModal(el("addIngredientModal"), () => { editingIngredientId = null; });

    org = createOrganizer({
        kind: "ingredients", noun: "ingredients", nounOne: "ingredient", allLabel: "All",
        panel: el("ingredientsList").closest(".panel"), host: el("ingredientsList"), shelf: el("ingredientShelf"), shelfAdd: el("ingredientShelfAdd"), shelfEnd: el("ingredientShelfEnd"), bar: el("ingredientBar"),
        folderNames: () => everyIngredient.map((item) => item.folder).filter((name) => name),
        folderOf: (key) => { const item = everyIngredient.find((x) => x.id === key); return item ? item.folder : ""; },
        titleOf: (key) => { const item = everyIngredient.find((x) => x.id === key); return item ? titleOf(item) : ""; },
        count: (v) => (v.kind === "bin" ? binIngredients.length : ingredients.filter((item) => v.kind !== "folder" || sameKey(item.folder) === sameKey(v.name)).length),
        binKeys: () => binIngredients.map((item) => item.id),
        move: moveToFolder, bin: moveToBin, restore: restoreFromBin, purge: purgeFromBin,
        renameFolder: (from, to) => refolder(from, to), deleteFolder: (name) => refolder(name, ""),
        selectionActions: () => [
            { label: "Approve All", className: "bar-quiet", tip: "Approve only selected AI entries that are still waiting for review.", run: approveSelectedIngredients }
        ],
        onView: () => { fromFirstPage(); }
    });
    setAssistPage("curator");
    registerHelpers();

    wireSearch(el("ingredientSearchInput"), el("ingredientSearchClear"), fromFirstPage);

    setPageSettings(() => [
        { label: "Manage classifications", icon: "sliders", description: "Add, rename or remove the classifications ingredients can have.", run: manageClassifications },
        ...(fieldChangeWaits() ? [{ label: "Change the ingredient fields", icon: "layers", description: "One time: back up, add the classification list, move old INCI values into Also Known As.", run: runFieldChange }] : []),
        { label: "Clean up", icon: "sparkle", description: "Entries that are notes, joined names or label text, and entries that are there twice. You see every change first.", run: openCleanUp },
        { label: "Find duplicates", icon: "layers", description: "Entries that explain the same ingredient, or have nearly the same name. You choose which one to keep.", run: openDuplicates }
    ]);
    el("ingredientSourceFilter").addEventListener("change", fromFirstPage);
    el("ingredientRiskFilter").addEventListener("change", fromFirstPage);
    el("ingredientClassFilter").addEventListener("change", fromFirstPage);
    el("manageClassBtn").addEventListener("click", manageClassifications);
    wirePurposeAndClassification();
    el("curatorStats").addEventListener("click", (event) => {
        const tile = event.target.closest(".stat");
        if (!tile) return;
        narrow(tile.dataset.source || "", tile.dataset.risk || "");
    });
    el("sortMenuBtn").addEventListener("click", (event) => openMenu(event.currentTarget, [{ heading: "Order of the list" }].concat(
        SORTS.map((s) => ({ label: s.label, checked: s.id === view.sort, run: () => { view.sort = s.id; try { localStorage.setItem(VIEW_KEY, JSON.stringify(view)); } catch (error) {                      } fromFirstPage(); } })))));
    el("ingredientQuick").addEventListener("click", () => { quick = ""; fromFirstPage(); });
    el("addIngredientBtn").addEventListener("click", () => openIngredientForm(null));
    Array.from(el("listLayoutSwitch").querySelectorAll(".seg")).forEach((button) => button.addEventListener("click", () => {
        view.layout = button.dataset.layout === "cards" ? "cards" : "rows";
        try { localStorage.setItem(VIEW_KEY, JSON.stringify(view)); } catch (error) {                      }
        paintListLayout();
    }));
    paintListLayout();

    el("chooseIngredientFileBtn").addEventListener("click", () => el("ingredientFileInput").click());
    el("ingredientDropBox").addEventListener("click", () => el("ingredientFileInput").click());
    el("ingredientFileInput").addEventListener("change", (event) => {
        const file = event.target.files && event.target.files[0];
        event.target.value = "";
        if (file) importIngredientFile(file);
    });
    el("ingredientDropBox").addEventListener("dragover", (event) => { event.preventDefault(); event.currentTarget.classList.add("drag-over"); });
    el("ingredientDropBox").addEventListener("dragleave", (event) => event.currentTarget.classList.remove("drag-over"));
    el("ingredientDropBox").addEventListener("drop", (event) => {
        event.preventDefault();
        event.currentTarget.classList.remove("drag-over");
        const file = event.dataTransfer && event.dataTransfer.files && event.dataTransfer.files[0];
        if (file) importIngredientFile(file);
    });
    el("addIngredientForm").addEventListener("submit", saveIngredient);
    el("ingredientNameInput").addEventListener("input", showNameClash);
    el("ingredientNameInput").addEventListener("input", paintFieldSources);
    el("addIngredientForm").addEventListener("click", onFieldSourceClick);
    el("addIngredientForm").addEventListener("toggle", onSourcesToggle, true);
    el("ingredientSourcesInput").addEventListener("blur", labelReferenceLinks);
    windowKit = wireWindow(el("addIngredientModal"));
    sourceUi = mountSourceLinks({
        getName: () => el("ingredientNameInput").value,
        getCas: () => ((el("ingredientAliasesInput").value + " " + el("ingredientChemInput").value).match(/\b\d{2,7}-\d{2}-\d\b/) || [""])[0],
        getToken: async () => (auth.currentUser ? auth.currentUser.getIdToken() : ""),
        onChange: paintSourceCount
    });
    el("ingredientNameInput").addEventListener("input", () => sourceUi.refresh());
    wirePasteTidy();
    wireLimited();
    el("ingredientAliasesInput").addEventListener("input", showNameClash);
    el("ingredientsList").addEventListener("click", onListClick);
    el("ingredientsList").addEventListener("dblclick", (event) => {
        if (event.target.closest("button, a, .detail-box")) return;
        const card = event.target.closest(".ingredient-card");
        const item = card ? ingredients.find((i) => i.id === card.dataset.id) : null;
        if (item) openIngredientForm(item);
    });

    onListsRefreshed(() => { if (!dialogIsOpen()) loadIngredients(); });
    el("ingredientsList").addEventListener("keydown", (event) => { if (event.key === "Enter" && event.target.matches(".ingredient-card")) { event.preventDefault(); toggleDetails(event.target.dataset.id); } });


    const askedFor = new URLSearchParams(window.location.search);
    loadIngredients().then(() => {
        startCuratorLive();
        if (askedFor.get("add") === "1") openIngredientForm(null);
        else if (askedFor.get("cleanup") === "1") openCleanUp();
        else if (askedFor.get("duplicates") === "1") openDuplicates();
    });
    window.addEventListener("pagehide", stopCuratorLive);
    window.addEventListener("pageshow", (event) => { if (event.persisted) startCuratorLive(); });
});

// The curator stat cards follow the ingredient and product collections in real time.
function stopCuratorLive() {
    curatorLiveStops.forEach((stop) => stop());
    curatorLiveStops = [];
    curatorLiveReady = new Set();
}

function startCuratorLive() {
    stopCuratorLive();
    ["ingredients", "products"].forEach((name) => curatorLiveStops.push(onSnapshot(collection(db, name), () => {
        curatorLiveReady.add(name);
        if (curatorLiveReady.size < 2 || curatorLiveRefresh || dialogIsOpen()) return;
        curatorLiveRefresh = true;
        loadIngredients().finally(() => { curatorLiveRefresh = false; });
    }, (error) => console.error(`Error watching ${name} for curator cards: `, error))));
}

function fromFirstPage() { pageState.page = 1; renderIngredients(); }


function paintListLayout() {
    el("ingredientsList").classList.toggle("as-rows", view.layout !== "cards");
    Array.from(el("listLayoutSwitch").querySelectorAll(".seg")).forEach((button) => {
        const on = button.dataset.layout === view.layout;
        button.classList.toggle("on", on);
        button.setAttribute("aria-pressed", String(on));
    });
}

function narrow(source, risk) {
    el("ingredientSearchInput").value = "";
    el("ingredientSourceFilter").value = source;
    el("ingredientRiskFilter").value = risk;
    el("ingredientClassFilter").value = "";
    quick = "";
    if (org.view.kind !== "all") org.show({ kind: "all" }); else fromFirstPage();
}




async function loadIngredients() {
    const container = el("ingredientsList");
    try {
        const snapshot = await getDocs(collection(db, "ingredients"));
        everyIngredient = snapshot.docs.map((d) => {
            const data = d.data();
            const risk = String(data.riskLevel || "").toLowerCase();
            const made = toDate(data.createdAt), since = toDate(data.binnedAt);
            const uses = typeof data.commonUses === "string" ? data.commonUses : Array.isArray(data.commonUses) ? data.commonUses.map((u) => String(u)).join(", ") : "";
            return {
                id: d.id,
                name: data.name || "",
                techName: data.techName || "",
                rawTech: Object.prototype.hasOwnProperty.call(data, "techName"),
                rawMarked: Object.prototype.hasOwnProperty.call(data, "classificationId"),
                description: data.description || "",
                aliases: aliasesWithInci(data.name, data.techName, asArray(data.aliases)),
                classificationId: String(data.classificationId || ""),
                purpose: String(data.purpose || ""),
                riskLevel: RISK_LEVELS.includes(risk) ? risk : "",
                cautionNote: data.cautionNote || "",
                frequentUseAllergy: String(data.frequentUseAllergy || ""),
                sourceLinks: Object.fromEntries(SOURCE_LINK_FIELDS.map((f) => [f.key, typeof data[f.data] === "string" ? data[f.data] : ""])),
                commonUses: brokenText(uses) ? "" : uses,
                usesBroken: brokenText(uses),
                minAge: toWholeNumber(data.minAge),
                keyActive: data.keyActive === true,
                umbrella: data.umbrella === true,
                highlights: asArray(data.highlights),
                chemicalDescription: String(data.chemicalDescription || ""),
                allergenTags: asArray(data.allergenTags),
                differentFrom: asArray(data.differentFrom),
                avoidForSkinTypes: asArray(data.avoidForSkinTypes),
                goodForSkinTypes: asArray(data.goodForSkinTypes),
                avoidForConditions: asArray(data.avoidForConditions),
                goodForConditions: asArray(data.goodForConditions),
                source: String(data.source || "").trim().toLowerCase(),
                reviewed: data.reviewed === true,
                generic: data.generic === true,
                sources: Array.isArray(data.sources) ? data.sources.map((s) => String(s)) : [],
                fdaUnii: String(data.fdaUnii || "").trim(),
                via: String(data.via || "").trim().toLowerCase(),
                webSearched: data.webSearched === true,
                autoApproved: data.autoApproved === true,
                folder: cleanFolder(data.folder),
                binned: data.binned === true,
                binnedAt: since ? since.getTime() : 0,
                binnedReason: String(data.binnedReason || ""),
                addedAt: made ? Math.floor(made.getTime() / 1000) : toWholeNumber(data.addedAtUnix)
            };
        });
    } catch (error) {
        console.error("Error fetching ingredients from Firestore: ", error);
        container.innerHTML = `<div class="empty-state"><p class="empty-message error-text">Could not load the ingredients.</p><span class="empty-hint">${escapeHtml(describeError(error))}</span></div>`;
        datasetChanged("ingredients");
        return;
    }


    if (AUTO_APPROVE_AI && !TEST_MODE) {
        const fresh = everyIngredient.filter((item) => needsReview(item) && !item.binned && hasCredibleSource(item));
        if (fresh.length > 0) {
            try {
                for (let i = 0; i < fresh.length; i += BATCH_LIMIT) {
                    const batch = writeBatch(db);
                    fresh.slice(i, i + BATCH_LIMIT).forEach((item) => batch.update(doc(db, "ingredients", item.id), { reviewed: true, autoApproved: true, reviewedAt: serverTimestamp() }));
                    await batch.commit();
                }
                fresh.forEach((item) => { item.reviewed = true; item.autoApproved = true; });
                notify(`${many(fresh.length, "ingredient with a credible source was", "ingredients with a credible source were")} approved automatically.`, "ok");
            } catch (error) {
                console.warn("Automatic approval did not work: ", error);
            }
        }
    }


    productUse = new Map();
    try {
        const productSnapshot = await getDocs(collection(db, "products"));
        const checkedBrands = new Set();
        const listedBy = new Map();
        productSnapshot.forEach((d) => {
            if (d.data().binned === true) return;
            if (d.data().source === "fdaph" || /FDA Philippines: notified as/i.test(String(d.data().description || ""))) checkedBrands.add(String(d.data().brandName || "").trim());
            const seen = new Set();
            asArray(d.data().ingredients).forEach((listed) => {
                const key = sameKey(listed);
                if (seen.has(key)) return;
                seen.add(key);
                productUse.set(key, (productUse.get(key) || 0) + 1);
                if (!listedBy.has(key)) listedBy.set(key, { key, name: listed, products: [] });
                listedBy.get(key).products.push([String(d.data().brandName || "").trim(), String(d.data().name || "").trim()].filter((x) => x).join(": "));
            });
        });

        const known = new Set();
        everyIngredient.forEach((item) => { if (!item.binned) keysOf(item).forEach((k) => known.add(k)); });
        fromProducts = Array.from(listedBy.values()).filter((x) => x.key.length >= 2 && !known.has(x.key)).sort((a, b) => b.products.length - a.products.length || a.name.localeCompare(b.name));
        fdaBrands = Array.from(checkedBrands).filter((name) => name).sort((a, b) => a.localeCompare(b));
        productUseKnown = true;
    } catch (error) {
        console.warn("Could not read the products: ", error);
        productUseKnown = false;
    }
    try {
        await refreshRecommendabilityFlags({ ingredients: everyIngredient });
    } catch (error) {
        console.error("Could not refresh recommendation eligibility after ingredient curation: ", error);
        notify("Recommendation eligibility could not be refreshed. " + describeError(error), "error");
    }
    afterChange();
}


function afterChange() {
    everyIngredient.sort(byName);
    ingredients = everyIngredient.filter((item) => !item.binned);
    binIngredients = everyIngredient.filter((item) => item.binned);
    setBinCount(binIngredients.length, "ingredients");
    if (org) org.sync(everyIngredient.map((item) => item.id));
    fillSourceFilter();
    fillClassFilter();
    renderIngredients();
    datasetChanged("ingredients");
}


function fillSourceFilter() {
    const select = el("ingredientSourceFilter");
    const wanted = select.dataset.ready ? select.value : (new URLSearchParams(window.location.search).get("show") || "");
    select.dataset.ready = "1";
    select.innerHTML = FILTERS.map((f) => `<option value="${f.value}">${escapeHtml(f.label)} (${ingredients.filter(f.test).length})</option>`).join("")
        + `<option value="${FROM_PRODUCTS}">From products, not explained yet (${fromProducts.length})</option>`;
    select.value = wanted === FROM_PRODUCTS || FILTERS.some((f) => f.value === wanted) ? wanted : "";
}


function fillClassFilter() {
    const select = el("ingredientClassFilter"), wanted = select.value;
    const options = [make("", "Any classification"), make(NO_CLASS, `Needs classification (${ingredients.filter(lacksClass).length})`)];
    classifications().forEach((c) => options.push(make(c.id, `${c.name} (${ingredients.filter((i) => i.classificationId === c.id).length})`)));
    select.replaceChildren(...options);
    select.value = options.some((o) => o.value === wanted) ? wanted : "";
    function make(value, text) { const option = document.createElement("option"); option.value = value; option.textContent = text; return option; }
}


function fillClassInput(selected) {
    const select = el("ingredientClassInput");
    const make = (value, text) => { const option = document.createElement("option"); option.value = value; option.textContent = text; return option; };

    const placeholder = make("", "Choose a classification"), line = make("", "-"), add = make(ADD_CLASS, "+ Add new classification");
    placeholder.disabled = true; line.disabled = true; line.setAttribute("data-sep", ""); add.setAttribute("data-add", "");
    select.replaceChildren(placeholder, ...classifications().map((c) => make(c.id, c.name)), line, add);
    select.value = classifications().some((c) => c.id === selected) ? selected : "";
    select.dataset.last = select.value;
    if (classCombo) classCombo.refresh();
}

function manageClassifications() {
    openClassificationManager({
        ingredients: () => everyIngredient,
        onChanged: async () => { fillClassInput(el("ingredientClassInput").value); fillClassFilter(); renderIngredients(); datasetChanged("ingredients"); }
    });
}

function wirePurposeAndClassification() {
    const purpose = el("ingredientPurposeInput"), select = el("ingredientClassInput");
    purpose.maxLength = MAX_PURPOSE;
    classCombo = makeCombo(select);
    purpose.addEventListener("input", paintPurposeCount);
    select.addEventListener("change", async () => {
        if (select.value !== ADD_CLASS) { select.dataset.last = select.value; return; }
        const before = select.dataset.last || "";
        select.value = before;
        const made = await askNewClassification();
        fillClassInput(made ? made.id : before);
        if (made) { fillClassFilter(); renderIngredients(); }
    });
}
let paintNameCount = () => { };           // the name field's counter (charLimit.js); no auto height on a one-line box
function paintPurposeCount() { paintLimited(); paintNameCount(); }





const LIMITED_IDS = ["ingredientPurposeInput", "ingredientCautionInput", "ingredientUsesInput", "ingredientAliasesInput", "ingredientDescInput", "ingredientChemInput", "ingredientSourcesInput"];
let limitedWidth = 0;
function wireLimited() {
    paintNameCount = attachCounters(["ingredientNameInput"]);
    LIMITED_IDS.forEach((id) => {
        const box = el(id);
        if (!box || box.dataset.limitedWired) return;
        box.dataset.limitedWired = "1";
        if (box.classList.contains("one-line")) {
            box.addEventListener("keydown", (e) => { if (e.key === "Enter") e.preventDefault(); });
            box.addEventListener("input", () => { if (/[\r\n]/.test(box.value)) box.value = box.value.replace(/\s*[\r\n]+\s*/g, " "); });
        }
        box.addEventListener("input", () => paintLimited(box));
    });
    if (typeof ResizeObserver === "function") {
        const body = el("ingBody");
        new ResizeObserver(() => { const w = body.clientWidth; if (w !== limitedWidth) { limitedWidth = w; fitLimited(); } }).observe(body);
    }
}
function paintLimited(only) {
    LIMITED_IDS.forEach((id) => {
        const box = el(id);
        if (only && only !== box) return;
        // maxlength remains active on the field; no counter is shown below it.
    });
    if (!only) fitLimited();
}

function fitLimited() {
    const canvas = fitLimited.canvas || (fitLimited.canvas = document.createElement("canvas")), ctx = canvas.getContext("2d");
    LIMITED_IDS.forEach((id) => {
        const box = el(id);
        if (!box.maxLength || box.maxLength < 0 || !box.offsetParent) return;
        const cs = getComputedStyle(box);
        ctx.font = `${cs.fontStyle} ${cs.fontWeight} ${cs.fontSize} ${cs.fontFamily}`;
        const sample = "abcdefghijklmnopqrstuvwxyz ABCDEFGHIJKLMNOPQRSTUVWXYZ .,-";
        const perChar = ctx.measureText(sample).width / sample.length;
        const inner = box.clientWidth - parseFloat(cs.paddingLeft) - parseFloat(cs.paddingRight);
        if (inner <= 0) return;
        const lines = Math.min(box.maxLength > 200 ? 4 : 99, Math.max(1, Math.ceil((box.maxLength * perChar * 1.08) / inner)));
        const lh = parseFloat(cs.lineHeight) || parseFloat(cs.fontSize) * 1.45;
        const extra = parseFloat(cs.paddingTop) + parseFloat(cs.paddingBottom) + parseFloat(cs.borderTopWidth) + parseFloat(cs.borderBottomWidth);
        box.style.minHeight = Math.ceil(lines * lh + extra) + "px";
    });
}


const fieldChangeWaits = () => everyIngredient.length > 0 && (classifications().length === 0 || everyIngredient.some((item) => item.rawTech || !item.rawMarked));
function runFieldChange() { return openFieldMigration(async () => { await loadIngredients(); }); }


function keysOf(item) {
    return [item.name, item.techName, ...item.aliases].map(sameKey).filter((k) => k.length > 0);
}


function nameClashes(name, aliases) {
    const mine = new Set([name, ...(aliases || [])].map(sameKey).filter((k) => k.length > 0));
    return everyIngredient.filter((other) => other.id !== editingIngredientId && !other.binned && !differentIds.has(other.id) && keysOf(other).some((k) => mine.has(k)));
}


function showNameClash() {
    const box = el("ingredientNameClash");
    if (!box) return;
    box.replaceChildren();
    const name = el("ingredientNameInput").value.replace(/\s+/g, " ").trim();
    if (!name) { box.hidden = true; return; }
    const found = nameClashes(name, toList(el("ingredientAliasesInput").value).filter((a) => sameKey(a) !== sameKey(name)));
    if (found.length === 0) { box.hidden = true; return; }
    box.hidden = false;
    found.slice(0, 3).forEach((other) => {
        const exact = sameKey(other.name) === sameKey(name);
        const row = document.createElement("div");
        row.className = "name-clash-row";
        const words = document.createElement("span");
        words.textContent = `Already in the library: "${titleOf(other)}"${exact ? "." : " (it uses this name as Also known as)."}`;
        const actions = document.createElement("span");
        actions.className = "name-clash-actions";
        const open = document.createElement("button");
        open.type = "button"; open.className = "btn"; open.textContent = "Open it";
        open.addEventListener("click", () => { modal.close(); openIngredientForm(other); });
        actions.append(open);
        if (!exact) {
            const different = document.createElement("button");
            different.type = "button"; different.className = "btn"; different.textContent = "It is a different ingredient";
            different.addEventListener("click", () => { differentIds.add(other.id); showNameClash(); });
            actions.append(different);
        }
        row.append(words, actions);
        box.append(row);
    });
}

function usedInProducts(item) {
    let total = 0;
    new Set(keysOf(item)).forEach((key) => { total += productUse.get(key) || 0; });
    return total;
}


function renderPickers(item) {
    const allergens = readChecks(el("allergenTagsChecks")), highlights = readChecks(el("highlightTagsChecks"));
    const conditions = readFit(el("conditionsFit"));
    renderChecks(el("allergenTagsChecks"), "allergenTags", optionsOf("allergens", (item ? item.allergenTags : []).concat(allergens)));
    renderChecks(el("highlightTagsChecks"), "highlights", optionsOf("highlights", (item ? item.highlights : []).concat(highlights)).filter((o) => !BUILT_IN_HIGHLIGHTS.includes(o.value)));
    renderFit(el("conditionsFit"), "condition", optionsOf("conditions", (item ? item.avoidForConditions.concat(item.goodForConditions) : []).concat(conditions.avoid, conditions.good)));
    writeChecks(el("allergenTagsChecks"), allergens);
    writeChecks(el("highlightTagsChecks"), highlights);
    writeFit(el("conditionsFit"), conditions.avoid, conditions.good);
}

const labelOf = (options, value) => {
    const match = options.find((o) => o.value.toLowerCase() === String(value).toLowerCase());
    return match ? match.label : value;
};


function proofLines(item) {
    const lines = [];
    const links = sourceLinks(item.sources);
    if (links) {
        const lead = item.source !== "ai" ? "Source: "
            : item.webSearched ? "Pages the AI's web search used: "
            : "Pages named by the AI (it did not search the web, so open them and check): ";
        lines.push(lead + links);
    }
    if (item.fdaUnii)
        lines.push(`Listed in the US FDA substance registry: <a class="source-link" href="${FDA_SUBSTANCE_PAGE}${encodeURIComponent(item.fdaUnii)}" target="_blank" rel="noopener noreferrer">UNII ${escapeHtml(item.fdaUnii)}</a>`);
    else if (item.source === "ai")
        lines.push("No match in the US FDA substance registry was recorded for this name.");
    return lines;
}


function paintStats() {
    const inBin = org.view.kind === "bin";
    const source = el("ingredientSourceFilter").value, risk = el("ingredientRiskFilter").value;
    const tiles = [
        { label: "Ingredients", value: ingredients.length, iconName: "flask", source: "", risk: "", tip: "Show every ingredient." },
        { label: "Waiting for review", value: ingredients.filter(needsReview).length, iconName: "inbox", source: "review", risk: "", tip: "Found by the app's AI lookup and not checked yet. The app uses their warnings but never counts them as proof of safety." },
        { label: "Harmful", value: ingredients.filter((i) => i.riskLevel === "harmful").length, iconName: "alert", source: "", risk: "harmful", tip: "Ingredients marked Harmful here." },
        { label: "Caution", value: ingredients.filter((i) => i.riskLevel === "caution").length, iconName: "shield", source: "", risk: "caution", tip: "Ingredients marked Caution here." },
        { label: "From products, not explained", value: fromProducts.length, iconName: "lipstick", source: FROM_PRODUCTS, risk: "", tip: "Ingredients the inventory's products list that have no entry here yet. The app uses its built-in knowledge for them." }
    ];

    const buttons = Array.from(el("curatorStats").querySelectorAll(".stat"));
    tiles.forEach((t, index) => {
        const button = buttons[index];
        if (!button) return;
        const value = button.querySelector(".stat-value");
        if (value) { value.textContent = String(t.value); value.classList.remove("skeleton"); }
        button.classList.toggle("on", !inBin && t.source === source && t.risk === risk && !!(source || risk));
    });
}

function renderIngredients() {
    const container = el("ingredientsList");
    const typed = el("ingredientSearchInput").value, term = sameKey(typed);
    const filter = FILTERS.find((f) => f.value === el("ingredientSourceFilter").value) || FILTERS[0];
    const risk = el("ingredientRiskFilter").value, classFilter = el("ingredientClassFilter").value;
    const now = org.view, inBin = now.kind === "bin", folder = now.kind === "folder" ? now.name : "";
    paintStats();
    el("sortNow").textContent = sortNow().short;
    el("ingredientQuick").innerHTML = quick && !inBin ? `<button type="button" class="filter-chip" data-tip="Showing only these ingredients. Click to show all again.">${escapeHtml(QUICK[quick].label)}${icon("x")}</button>` : "";

    if (!inBin && el("ingredientSourceFilter").value === FROM_PRODUCTS) { renderFromProducts(term, typed); return; }


    const matches = (item) => matchesSearch(typed, [item.name, item.techName, ...item.aliases, item.description]);
    const pool = inBin ? binIngredients : ingredients;
    const shown = (inBin
        ? pool.filter(matches).sort((a, b) => b.binnedAt - a.binnedAt || byName(a, b))
        : pool.filter((item) => filter.test(item)
            && (!risk || (risk === "auto" ? !item.riskLevel : item.riskLevel === risk))
            && (!folder || sameKey(item.folder) === sameKey(folder))
            && (!classFilter || (classFilter === NO_CLASS ? lacksClass(item) : item.classificationId === classFilter))
            && (!quick || QUICK[quick].test(item)) && matches(item)).sort(sortNow().cmp));

    el("ingredientCountLine").textContent = inBin ? (pool.length === 0 ? "" : `${many(shown.length, "entry", "entries")} · left out by the app`)
        : everyIngredient.length === 0 ? "" : shown.length === ingredients.length ? `${ingredients.length} in all` : `${shown.length} of ${ingredients.length}${folder ? ` · in "${folder}"` : ""}`;
    renderPager(el("ingredientPager"), pageState, shown.length, renderIngredients, "ingredients");
    renderPager(el("ingredientPagerTop"), pageState, shown.length, renderIngredients, "ingredients");

    if (shown.length === 0) {
        const message = everyIngredient.length === 0 ? "No ingredients yet."
            : inBin ? (term ? `Nothing in the Recycle bin matches "${escapeHtml(typed)}".` : "The Recycle bin is empty.")
            : term ? `No ingredient matches "${escapeHtml(typed)}"${filter.value || risk || folder || quick || classFilter ? " in this view" : ""}.`
            : quick ? "No ingredient like that. Click the chip above to show all again."
            : folder ? `"${escapeHtml(folder)}" is empty.`
            : filter.value === "review" ? "Nothing is waiting for review."
            : "No ingredients in this view.";
        const hint = everyIngredient.length === 0 ? "Add the first one with the Ingredient button."
            : inBin && !term ? "Drag a card onto the bin to remove it."
            : folder && !term ? "Drag cards onto its chip above."
            : "";
        container.innerHTML = `<div class="empty-state"><p class="empty-message">${message}</p>${hint ? `<span class="empty-hint">${hint}</span>` : ""}</div>`;
    } else container.innerHTML = paginate(shown, pageState).map((item) => cardOf(item, inBin)).join("");
    org.refresh();
    refreshAssist();
}


function renderFromProducts(term, typed) {
    const container = el("ingredientsList");
    const shown = fromProducts.filter((x) => matchesSearch(typed, [x.name]));
    el("ingredientCountLine").textContent = `${shown.length} listed by products, no entry yet`;
    renderPager(el("ingredientPager"), pageState, shown.length, renderIngredients, "ingredients");
    renderPager(el("ingredientPagerTop"), pageState, shown.length, renderIngredients, "ingredients");
    if (shown.length === 0) {
        container.innerHTML = `<div class="empty-state"><p class="empty-message">${term ? `Nothing matches "${escapeHtml(typed)}".` : productUseKnown ? "Every ingredient the products list has an entry here." : "The products could not be read."}</p></div>`;
    } else container.innerHTML = paginate(shown, pageState).map((x) => {
        const open = openId === "new:" + x.key;
        const detail = !open ? "" : `
            <div class="detail-box">
                <dl class="detail-grid">
                    <dt>Listed by</dt><dd>${x.products.slice(0, 12).map((name) => `<span class="chip">${escapeHtml(name)}</span>`).join("")}${x.products.length > 12 ? `<span class="muted small">and ${x.products.length - 12} more</span>` : ""}</dd>
                    <dt>In the app</dt><dd>No entry here yet: the app uses its own built-in knowledge for it, or looks it up when it is scanned.</dd>
                </dl>
                <div class="detail-actions"><button type="button" class="row-btn main" data-act="write" data-name="${escapeHtml(x.name)}" data-tip="Opens the form with the name filled in. You write what it is and how the app should judge it.">${icon("plus")}Write its entry</button></div>
            </div>`;
        return `
            <article class="ingredient-card from-product${open ? " open" : ""}" data-id="new:${escapeHtml(x.key)}" tabindex="0" aria-expanded="${open}">
                <header class="card-head"><h4 class="ingredient-name">${escapeHtml(x.name)}</h4></header>
                <div class="card-sub"><p class="ingredient-tech">in ${many(x.products.length, "product", "products")}</p>${toneBadge("No entry yet", "muted", "Listed by products in the inventory. Nothing is written about it here yet.")}</div>
                ${detail}
            </article>`;
    }).join("");
    org.refresh();
    refreshAssist();
}

const actionButton = (classes, act, id, iconHtml, title, tip, preview) =>
    `<button type="button" class="icon-btn small ${classes}" data-act="${act}" data-id="${escapeHtml(id)}" aria-label="${escapeHtml(title)}" data-tip-title="${escapeHtml(title)}" data-tip="${escapeHtml(tip)}"${preview ? ` data-tip-preview="${escapeHtml(preview)}"` : ""}>${iconHtml}</button>`;


function cardOf(item, inBin) {
    const open = item.id === openId;
    const review = needsReview(item);
    const riskText = item.riskLevel ? `How the app judges it: ${RISK_NAMES[item.riskLevel]}.` : "No risk level is set here: the app decides by its own built-in knowledge.";
    const buttons = [];
    if (inBin) {
        buttons.push(actionButton("row-btn approve", "restore", item.id, icon("undo"), "Restore", "Back to the list. The app uses it again."));
        buttons.push(actionButton("row-btn danger", "purge", item.id, icon("trash"), "Delete forever", "Delete it from the database.", "It asks first. This cannot be undone."));
    } else {
        if (review) buttons.push(actionButton("row-btn approve", "approve", item.id, icon("check"), "Approve", "You have checked this entry: the app may now trust it like one typed here.", "To correct it first, open it and press Edit: saving also approves it."));
        buttons.push(actionButton("row-btn danger", "bin", item.id, BIN_ICON, "Recycle bin", "Remove this ingredient.", "It goes to the Recycle bin and can be restored."));
    }
    return `
        <article class="ingredient-card${review ? " needs-review" : ""}${open ? " open" : ""}" data-id="${escapeHtml(item.id)}" data-key="${escapeHtml(item.id)}" data-drag tabindex="0" aria-expanded="${open}">
            <header class="card-head">
                <button type="button" class="pick" role="checkbox" aria-checked="false" aria-label="Select ${escapeHtml(titleOf(item))}"></button>
                <h4 class="ingredient-name">${escapeHtml(titleOf(item))}</h4>
                <span class="spacer"></span>
                <div class="btn-group row-actions">${buttons.join("")}</div>
            </header>
            <div class="card-sub"><p class="ingredient-tech">${escapeHtml(firstAlias(item))}</p><span class="class-cell">${classCell(item)}</span>${review ? statusBadge("Pending") : ""}${toneBadge(RISK_NAMES[item.riskLevel || "auto"], toneOfRisk(item.riskLevel), riskText)}</div>
            ${open ? detailOf(item, inBin) : ""}
        </article>`;
}


const classCell = (item) => lacksClass(item) ? `<span class="chip warn">Needs classification</span>` : `<span class="chip">${escapeHtml(classificationName(item.classificationId))}</span>`;


function detailOf(item, inBin) {
    const review = needsReview(item);
    const origin = originLabel(item.source);
    const facts = [];

    if (item.purpose) facts.push(["Purpose", escapeHtml(item.purpose)]);
    facts.push(["Definition", escapeHtml(item.description || "No description provided.")]);
    if (item.chemicalDescription) facts.push(["What the chemical is", escapeHtml(item.chemicalDescription)]);
    if (item.aliases.length > 0) facts.push(["Also known as", escapeHtml(item.aliases.join(", "))]);
    if (item.commonUses) facts.push(["Used in", escapeHtml(item.commonUses)]);
    else if (item.usesBroken) facts.push(["Used in", `<span class="chip warn" data-tip="An older build of the app wrote text here that is not a description. It is not shown to anyone. Edit and save to replace it.">text from the app could not be read</span>`]);
    if (item.cautionNote) facts.push(["Caution", "It " + escapeHtml(item.cautionNote)]);
    {
        const counted = countSaved(item);
        const links = SOURCE_LINK_FIELDS.filter((f) => f.enabled && item.sourceLinks && /^https:\/\//i.test(item.sourceLinks[f.key] || ""))
            .map((f) => `<a class="source-link" href="${escapeHtml(item.sourceLinks[f.key])}" target="_blank" rel="noopener noreferrer">${escapeHtml(f.label)}</a>`);
        if (counted.total > 0) facts.push(["Field sources", `<span class="chip">Sources: ${counted.n} of ${counted.total} fields</span>${links.length ? " " + links.join(" · ") : ""}`]);
    }

    const chips = [];
    if (item.keyActive) chips.push(`<span class="chip pink">Key active</span>`);
    if (item.umbrella) chips.push(`<span class="chip warn">Umbrella term</span>`);
    if (item.minAge > 0) chips.push(`<span class="chip warn">Age ${item.minAge}+</span>`);
    item.highlights.forEach((t) => chips.push(`<span class="chip pink">Highlight: ${escapeHtml(labelOf(allOptions("highlights"), t))}</span>`));
    item.allergenTags.forEach((t) => chips.push(`<span class="chip warn">Allergen: ${escapeHtml(labelOf(allOptions("allergens"), t))}</span>`));
    item.avoidForSkinTypes.forEach((t) => chips.push(`<span class="chip warn">Avoid: ${escapeHtml(labelOf(SKIN_TYPES, t))} skin</span>`));
    item.avoidForConditions.forEach((t) => chips.push(`<span class="chip warn">Avoid: ${escapeHtml(labelOf(allOptions("conditions"), t))}</span>`));
    item.goodForSkinTypes.forEach((t) => chips.push(`<span class="chip good">Good: ${escapeHtml(labelOf(SKIN_TYPES, t))} skin</span>`));
    item.goodForConditions.forEach((t) => chips.push(`<span class="chip good">Good: ${escapeHtml(labelOf(allOptions("conditions"), t))}</span>`));
    if (chips.length > 0) facts.push(["For the app", chips.join("")]);

    if (productUseKnown) { const used = usedInProducts(item); facts.push(["In products", used === 0 ? "Not in any listed product yet" : many(used, "listed product", "listed products")]); }
    if (item.folder) facts.push(["Folder", escapeHtml(item.folder)]);
    if (origin) facts.push(["Added", escapeHtml(origin) + (review ? ` <span class="chip review" data-tip="Until you approve it, the app uses this entry's warnings but never counts it as proof that the ingredient is safe.">needs review</span>` : item.autoApproved ? ` <span class="chip" data-tip="Approved by itself because it came with a credible source: the US FDA substance registry, or a page from a regulator, a medical or scientific body, or an ingredient database that the AI's web search used. Press Edit to correct it.">approved automatically</span>` : item.reviewed ? " · reviewed" : "")]);
    const proof = proofLines(item);
    if (proof.length > 0) facts.push(["Sources", `<span class="source-line">${proof.join("<br>")}</span>`]);
    if (inBin && item.binnedAt) facts.push(["Removed", escapeHtml(timeAgo(new Date(item.binnedAt))) + (item.binnedReason ? ` · ${escapeHtml(item.binnedReason)}` : "")]);

    const button = (cls, act, iconHtml, label, tip) => `<button type="button" class="row-btn ${cls}" data-act="${act}" data-id="${escapeHtml(item.id)}" data-tip="${escapeHtml(tip)}">${iconHtml}${label}</button>`;
    const buttons = inBin
        ? [button("main", "restore", icon("undo"), "Restore", "Back to the list. The app uses it again."), `<span class="spacer"></span>`, button("danger", "purge", icon("trash"), "Delete forever", "Delete it from the database. It asks first.")]
        : [button("main", "edit", icon("edit"), "Edit", "Change the wording, the risk level or who should avoid it.")]
            .concat(review ? [button("approve", "approve", icon("check"), "Approve", "You have checked it: the app may trust it.")] : [])
            .concat([button("", "folder", icon("folder"), "Folder", "Put it in a folder, or take it out."), `<span class="spacer"></span>`, button("danger", "bin", BIN_ICON, "Recycle bin", "Remove it. It can be restored from the Recycle bin.")]);

    return `
        <div class="detail-box">
            <dl class="detail-grid">${facts.map(([label, value]) => `<dt>${label}</dt><dd>${value}</dd>`).join("")}</dl>
            <div class="detail-actions">${buttons.join("")}</div>
        </div>`;
}

function toggleDetails(id) {
    openId = openId === id ? "" : id;
    renderIngredients();
    if (openId) { const card = el("ingredientsList").querySelector(`.ingredient-card[data-id="${CSS.escape(openId)}"]`); if (card) { card.focus({ preventScroll: true }); card.scrollIntoView({ block: "nearest", behavior: "smooth" }); } }
}





function renderFit(container, group, options) {
    container.innerHTML = options.map((o, i) => {
        const name = `fit_${group}_${i}`;
        const choice = (value, label, cls, tip) => `<label class="fit-choice ${cls}"><input type="radio" aria-label="${tip}" name="${name}" value="${value}"${value === "" ? " checked" : ""}><span>${label}</span></label>`;
        return `<fieldset class="fit-row" data-value="${escapeHtml(o.value)}"><legend class="sr-only">${escapeHtml(o.label)}</legend><span class="fit-name" aria-hidden="true">${escapeHtml(o.label)}</span><span class="fit-choices">`
            + choice("avoid", "Avoid", "avoid", "The app warns users with this") + choice("", "Neutral", "none", "No effect either way") + choice("good", "Good", "good", "The app counts it as a plus for users with this")
            + `</span></fieldset>`;
    }).join("");
}


function readFit(container) {
    const result = { avoid: [], good: [] };
    container.querySelectorAll(".fit-row").forEach((row) => {
        const picked = row.querySelector("input:checked");
        if (picked && result[picked.value]) result[picked.value].push(row.dataset.value);
    });
    return result;
}


function writeFit(container, avoid, good) {
    const lower = (list) => new Set((Array.isArray(list) ? list : []).map((v) => String(v).toLowerCase()));
    const no = lower(avoid), yes = lower(good);
    container.querySelectorAll(".fit-row").forEach((row) => {
        const key = row.dataset.value.toLowerCase();
        const want = no.has(key) ? "avoid" : yes.has(key) ? "good" : "";
        row.querySelectorAll("input").forEach((input) => { input.checked = input.value === want; });
    });
}











const sourcesOpen = new Map();
try { JSON.parse(sessionStorage.getItem("glowup.curator.sourcesOpen") || "[]").forEach((field) => sourcesOpen.set(field, true)); } catch (error) {                                      }
function rememberSources() {
    try { sessionStorage.setItem("glowup.curator.sourcesOpen", JSON.stringify(Array.from(sourcesOpen).filter(([, open]) => open).map(([field]) => field))); } catch (error) {                              }
}

function sourceRow(link, detail) {
    const hint = link.kind === "search" ? "" : link.copyName ? ` <span class="site-hint">site search</span>` : ` <span class="site-hint">list</span>`;
    const act = link.canRef
        ? `<button type="button" class="ref-add" data-add-ref="${escapeHtml(link.id)}" aria-label="Add ${escapeHtml(link.label)} page of this ingredient to Reference links">+ ref</button>`
        : `<button type="button" class="ref-add" disabled aria-disabled="true" aria-label="Add ${escapeHtml(link.label)} to Reference links (not available: no page of this ingredient)">+ ref</button>`;
    return `<li class="lookup-row"><span class="lookup-name"><a class="source-link" href="${escapeHtml(link.url)}" target="_blank" rel="noopener noreferrer" data-source="${escapeHtml(link.id)}">${escapeHtml(link.label)}</a>${hint}</span>`
        + `<span class="lookup-text">${escapeHtml(detail)}</span><span class="lookup-act">${act}</span></li>`;
}

function paintFieldSources() {
    const name = el("ingredientNameInput").value.trim();
    document.querySelectorAll("[data-field-sources]").forEach((box) => {
        const field = box.dataset.fieldSources;
        const guide = FIELD_GUIDE[field];
        if (!guide) { box.hidden = true; box.innerHTML = ""; return; }
        const helper = guide.helper ? `<p class="field-hint lookup-helper">${escapeHtml(guide.helper)}</p>` : "";
        if (guide.sources.length === 0 && field !== "sources") { box.innerHTML = helper; box.hidden = !helper; return; }
        const links = field === "sources" ? allSourcesFor(name) : linksFor(field, name);
        if (links.length === 0) { box.hidden = true; box.innerHTML = ""; return; }
        const found = (fieldFound[field] || []).filter((page) => page && /^https:\/\//i.test(page.url || "")).map((page) =>
            `<a class="source-link" href="${escapeHtml(page.url)}" target="_blank" rel="noopener noreferrer">${escapeHtml(page.title || page.site || page.url)}</a> `
            + `<span class="chip source-kind${page.tone ? " " + page.tone : ""}">${escapeHtml(page.label || "Source")}</span>`);

        const basis = field === "description" && definitionBasis && /^https:\/\//i.test(definitionBasis.url || "")
            ? `<p class="definition-basis"><b>Based on:</b> <a class="source-link" href="${escapeHtml(definitionBasis.url)}" target="_blank" rel="noopener noreferrer">${escapeHtml(definitionBasis.title)}</a>`
                + (definitionBasis.quote ? ` <q>${escapeHtml(definitionBasis.quote)}</q>` : "")
                + (definitionBasis.replaced && definitionBasis.replaced.length ? `<br><span class="warn-text">The AI's text named a use this source does not give (${escapeHtml(definitionBasis.replaced.join(", "))}), so the definition was written from the source instead.</span>` : "")
                + `</p>` : "";
        const where = found.length && !basis ? `<p class="definition-basis"><b>Where this box's text came from:</b> ${found.join(" · ")}</p>` : "";
        const rows = links.map((link) => sourceRow(link, field === "sources" ? "Used for: " + link.usedFor.join(", ") : link.lookFor)).join("");
        const extra = (guide.title ? `<p class="field-hint"><b>${escapeHtml(guide.title)}</b></p>` : "") + helper
            + `<p class="field-hint lookup-note">+ ref is only for a page of this ingredient. For any other source, find the ingredient on its site and paste the page address in Reference links.</p>`;
        box.innerHTML = basis + where + `<details class="sources-box" data-sources-field="${escapeHtml(field)}"${sourcesOpen.get(field) ? " open" : ""}><summary>Sources (${links.length})</summary><ul class="lookup-list">${rows}</ul>${extra}</details>`;
        box.hidden = false;
    });
}



const pubchemAnswers = new Map();
async function pubchemPage(name) {
    const key = name.trim().toLowerCase();
    if (pubchemAnswers.has(key)) return pubchemAnswers.get(key);
    let cid = 0;
    try {
        const answer = await fetch(pubchemLookupUrl(name), { signal: AbortSignal.timeout(5000) });
        cid = answer.ok ? firstCompoundId(await answer.text()) : 0;
        if (answer.ok || answer.status === 404) pubchemAnswers.set(key, cid);
    } catch (error) { cid = 0; }
    return cid;
}

function copyInciName(name) {
    if (name && navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(name).catch(() => {});
}




const SOURCE_CLICK_GAP_MS = 2000;
const lastSourceClick = new Map();
function tooSoon(key, node) {
    const now = Date.now();
    if (now - (lastSourceClick.get(key) || 0) < SOURCE_CLICK_GAP_MS) return true;
    lastSourceClick.set(key, now);
    if (node) {
        node.classList.add("is-wait");
        node.setAttribute("aria-busy", "true");
        window.setTimeout(() => { node.classList.remove("is-wait"); node.removeAttribute("aria-busy"); }, SOURCE_CLICK_GAP_MS);
    }
    return false;
}

async function onFieldSourceClick(event) {
    const name = el("ingredientNameInput").value.trim();
    const link = event.target.closest("a.source-link[data-source]");
    if (link) {
        const source = SOURCES[link.dataset.source];
        if (!source) return;
        if (tooSoon("open:" + link.dataset.source, link)) { event.preventDefault(); return; }
        if (source.kind === "page") {
            event.preventDefault();
            const tab = window.open("about:blank", "_blank");
            if (tab) tab.opener = null;
            const cid = await pubchemPage(name);
            if (cid) { if (tab) tab.location.href = pubchemPageUrl(cid); else notify("PubChem opened in a new tab was blocked. Allow pop-ups for this site.", "error"); return; }
            copyInciName(name);
            if (tab) tab.location.href = source.landing;
            notify("PubChem has no page for this exact name. Its home page opened; the INCI name is copied. Paste it in the site's search box.", "ok");
        } else if (source.copyName) {
            copyInciName(name);
            notify("INCI name copied. Paste it in the site's search box.", "ok");
        }
        return;
    }
    const add = event.target.closest("button[data-add-ref]");
    if (!add || add.disabled) return;
    const source = SOURCES[add.dataset.addRef];
    if (!source || source.kind !== "page") return;
    if (tooSoon("ref:" + add.dataset.addRef, null)) return;
    add.disabled = true;
    try {
        const cid = await pubchemPage(name);
        if (!cid) { notify("PubChem has no page for this exact name, so nothing was added. Find it on the site and paste its address.", "error"); return; }
        const box = el("ingredientSourcesInput");
        const done = addReference(box.value, source.label, pubchemPageUrl(cid), box.maxLength > 0 ? box.maxLength : 900);
        if (done.added) { box.value = done.text; box.dispatchEvent(new Event("input", { bubbles: true })); notify(`${source.label} page added to Reference links.`, "ok"); }
        else notify(done.reason === "exists" ? `${source.label} is already in Reference links.` : done.reason === "full" ? "Reference links is full. Remove a line first." : "That page cannot be added.", done.reason === "exists" ? "ok" : "error");
    } finally { add.disabled = false; }
}


function onSourcesToggle(event) {
    const box = event.target.closest && event.target.closest("details.sources-box");
    if (!box || event.target !== box) return;
    sourcesOpen.set(box.dataset.sourcesField, box.open);
    rememberSources();
}


function labelReferenceLinks() {
    const box = el("ingredientSourcesInput");
    const labelled = labelBareUrls(box.value);
    if (labelled !== box.value) box.value = labelled.slice(0, box.maxLength > 0 ? box.maxLength : 900);
}


function wirePasteTidy() {
    const kinds = { ingredientNameInput: "name", ingredientPurposeInput: "purpose", ingredientDescInput: "description", ingredientChemInput: "chemical", ingredientAliasesInput: "aliases" };
    Object.entries(kinds).forEach(([id, kind]) => {
        const box = el(id);
        box.addEventListener("paste", (event) => {
            const raw = event.clipboardData ? event.clipboardData.getData("text") : "";
            if (!raw) return;
            event.preventDefault();
            const done = insertPaste(kind, box.value, box.selectionStart, box.selectionEnd, raw, box.maxLength > 0 ? box.maxLength : 0);
            box.value = done.value;
            box.setSelectionRange(done.caret, done.caret);
            box._pasting = true;
            box.dispatchEvent(new Event("input", { bubbles: true }));
            box._pasting = false;
            showPasteNote(box, done.note);
        });
        box.addEventListener("input", () => { if (!box._pasting) showPasteNote(box, ""); });
    });
}
function showPasteNote(box, text) {
    const field = box.closest(".field");
    let note = field.querySelector(".paste-note");
    if (!text) { if (note) note.remove(); return; }
    if (!note) { note = document.createElement("span"); note.className = "field-hint warn paste-note"; note.setAttribute("role", "status"); field.insertBefore(note, field.querySelector(".field-sources")); }
    note.textContent = text;
}


const IMPORT_MAX_ROWS = 60;





function importIngredientFile(file) {
    startImport({
        dropbox: el("ingredientDropBox"), choose: el("chooseIngredientFileBtn"), status: el("ingredientImportStatus"), file,
        read: (f, say) => readAdminDocument(f, say),
        extract: extractIngredients,
        review: INGREDIENT_REVIEW,
        save: (draft, say) => saveIngredientDraft(draft, say)
    });
}

const INGREDIENT_REVIEW = {
    title: "Check before saving",
    lists: [{
        key: "ingredients", label: "Ingredients", nounOne: "ingredient", noun: "ingredients",
        titleOf: (r) => r.name,
        noteOf: (r) => {
            const mine = [r.name, r.techName, ...(Array.isArray(r.aliases) ? r.aliases : [])].map(sameKey).filter((k) => k.length > 0);
            const other = everyIngredient.find((item) => keysOf(item).some((k) => mine.includes(k)));
            return other ? (other.binned ? "In the Recycle bin: will be skipped" : "Already listed: will be skipped") : "";
        },
        fields: [
            { key: "name", label: "Name" }, { key: "techName", label: "INCI name" },
            { key: "riskLevel", label: "Risk level", kind: "select", options: [["", "Let the app decide"], ["safe", "Safe"], ["caution", "Caution"], ["harmful", "Harmful"]] },
            { key: "description", label: "User-friendly definition", kind: "area", wide: true },
            { key: "aliases", label: "Also known as (one per line)", kind: "lines" },
            { key: "cautionNote", label: "Caution note (It ...)" }, { key: "commonUses", label: "Commonly used in" },
            { key: "chemicalDescription", label: "What the chemical is", kind: "area", wide: true }
        ]
    }]
};


async function extractIngredients(documentText, say, signal) {

    const keyed = parseImportText(documentText);
    if (keyed.kind === "inventory") return { refused: "This file lists only brands or products. " + inventoryNotHere(keyed.draft.products.length + keyed.draft.brands.length) };
    if (keyed.kind === "mixed") return { refused: `This file mixes ${many(keyed.counts.ingredients, "ingredient record", "ingredient records")} with brands or products, so nothing was imported. ` + inventoryNotHere(keyed.counts.inventory) };
    if (keyed.kind === "ingredients") { say("Reading the file's own fields. No AI is used."); return { draft: keyed.draft, notes: keyed.notes.slice(0, 6), model: "" }; }
    return { refused: "This file is not in the GlowUp import format, so nothing was imported. Use the import template." };
}


async function saveIngredientDraft(draft, say) {
    const records = Array.isArray(draft.ingredients) ? draft.ingredients : [];
    const result = { saved: 0, skippedDuplicates: 0, skippedInvalid: 0, unmatched: [] };
    const groups = Math.max(1, Math.ceil(records.length / IMPORT_MAX_ROWS));
    for (let g = 0; g < groups; g++) {
        if (groups > 1) say(`Saving group ${g + 1} of ${groups}...`);
        const part = await saveImportedIngredients(records.slice(g * IMPORT_MAX_ROWS, (g + 1) * IMPORT_MAX_ROWS));
        result.saved += part.saved; result.skippedDuplicates += part.skippedDuplicates; result.skippedInvalid += part.skippedInvalid;
        part.unmatched.forEach((name) => { if (!result.unmatched.includes(name)) result.unmatched.push(name); });

        if (part.saved && g < groups - 1) await loadIngredients();
    }
    if (result.saved === 0) {
        const nothing = `Nothing new was saved: ${result.skippedDuplicates} already in the list or the Recycle bin, ${result.skippedInvalid} incomplete.`;
        notify(nothing, "warn");
        return nothing;
    }
    const skipped = result.skippedDuplicates + result.skippedInvalid;
    const summary = `Saved ${many(result.saved, "ingredient", "ingredients")} as Pending for review` + (skipped ? `; skipped ${skipped} already listed or incomplete.` : ".")
        + (result.unmatched.length ? ` Not in your lists, so left out: ${result.unmatched.slice(0, 6).join(", ")}${result.unmatched.length > 6 ? " ..." : ""} (add them with Manage lists, then edit the ingredient).` : "");
    notify(summary, "ok");
    await loadIngredients();
    return summary;
}


async function saveImportedIngredients(records) {
    const batch = writeBatch(db);
    const planned = new Set();
    const unmatched = new Set();
    let saved = 0, skippedDuplicates = 0, skippedInvalid = 0;
    records.slice(0, IMPORT_MAX_ROWS).forEach((record) => {
        const name = String(record.name || "").replace(/\s+/g, " ").trim().slice(0, 80);
        if (name.length < 2) { skippedInvalid++; return; }

        const aliases = aliasesWithInci(name, String(record.techName || "").slice(0, 120), (Array.isArray(record.aliases) ? record.aliases : []).map((a) => String(a).replace(/\s+/g, " ").trim().slice(0, 80)).filter(Boolean).slice(0, 6));
        const mine = [name, ...aliases].map(sameKey).filter((k) => k.length > 0);
        if (mine.some((k) => planned.has(k)) || everyIngredient.some((other) => keysOf(other).some((k) => mine.includes(k)))) { skippedDuplicates++; return; }
        mine.forEach((k) => planned.add(k));
        const risk = ["safe", "caution", "harmful"].includes(record.riskLevel) ? record.riskLevel : "";
        const highlightIds = listIds("highlights", record.highlights, unmatched);
        const avoidConditions = listIds("conditions", record.avoidConditions, unmatched);
        const skinTypes = (list) => (Array.isArray(list) ? list : []).filter((type) => SKIN_TYPES.some((option) => option.value === type));
        const avoidTypes = skinTypes(record.avoidSkinTypes);
        const sources = (Array.isArray(record.sources) ? record.sources : []).map((url) => String(url)).filter((url) => /^https:\/\//i.test(url))
            .map((url) => { try { return new URL(url).hostname.replace(/^www\./, "") + " | " + url; } catch (error) { return ""; } }).filter(Boolean).slice(0, 10);
        batch.set(doc(collection(db, "ingredients")), {
            name, aliases, classificationId: "", purpose: "",
            description: String(record.description || "").replace(/\s+/g, " ").trim().slice(0, 300),
            chemicalDescription: String(record.chemicalDescription || "").replace(/\s+/g, " ").trim().slice(0, 400),
            riskLevel: risk,
            cautionNote: String(record.cautionNote || "").replace(/\s+/g, " ").trim().slice(0, 120).replace(/^it\s+/i, "").replace(/\.$/, ""),
            commonUses: String(record.commonUses || "").replace(/\s+/g, " ").trim().slice(0, 120),
            minAge: Number.isInteger(record.minAge) && record.minAge > 0 && record.minAge < 100 ? record.minAge : 0,
            keyActive: highlightIds.includes("keyActive"), umbrella: highlightIds.includes("umbrella"),
            highlights: highlightIds.filter((id) => !BUILT_IN_HIGHLIGHTS.includes(id)),
            allergenTags: listIds("allergens", record.allergens, unmatched),
            avoidForSkinTypes: avoidTypes, goodForSkinTypes: skinTypes(record.goodSkinTypes).filter((type) => !avoidTypes.includes(type)),
            avoidForConditions: avoidConditions, goodForConditions: listIds("conditions", record.goodConditions, unmatched).filter((id) => !avoidConditions.includes(id)),
            sources,
            folder: "",

            source: "ai", reviewed: false, via: "document-import",
            createdAt: serverTimestamp(), updatedAt: serverTimestamp()
        });
        saved++;
    });
    if (saved === 0) return { saved, skippedDuplicates, skippedInvalid, unmatched: Array.from(unmatched) };
    await batch.commit();
    return { saved, skippedDuplicates, skippedInvalid, unmatched: Array.from(unmatched) };
}






function listIds(kind, names, unmatched) {
    const items = (listsNow()[kind] || []).filter((item) => !item.retired);
    const ids = [];
    (Array.isArray(names) ? names : []).forEach((raw) => {
        const key = normalizeName(raw);
        if (!key) return;
        const item = items.find((it) => normalizeName(it.label) === key || normalizeName(it.id) === key
            || (it.keywords || []).some((word) => { const w = normalizeName(word); return w && (" " + key + " ").includes(" " + w + " "); }));
        if (item) { if (!ids.includes(item.id)) ids.push(item.id); } else unmatched.add(String(raw).trim());
    });
    return ids;
}

async function approveSelectedIngredients(keys) {
    const waiting = keys.map((id) => everyIngredient.find((item) => item.id === id && !item.binned && needsReview(item))).filter((item) => item);

    const selected = waiting.filter((item) => !(item.riskLevel === "harmful" && evidenceProblem("harmful", item.sources)));
    if (selected.length < waiting.length) notify(`${many(waiting.length - selected.length, "harmful entry was", "harmful entries were")} left out: no regulator's page backs the rating. Use Edit on each.`, "warn");
    if (!selected.length) {
        notify("No selected ingredients are AI entries waiting for review.", "info");
        return;
    }
    if (!(await askConfirm({
        title: `Approve ${many(selected.length, "selected ingredient", "selected ingredients")}?`,
        message: "This marks only existing AI-found entries as reviewed. It does not change descriptions or risk ratings.",
        items: selected.map(titleOf),
        confirmLabel: "Approve All"
    }))) return;
    try {
        await saveOrReload(selected.map((item) => ({
            ref: refOf(item),
            data: { reviewed: true, reviewedBy: reviewer(), reviewedAt: serverTimestamp(), updatedAt: serverTimestamp() }
        })));
        notify(`${many(selected.length, "ingredient was", "ingredients were")} approved.`, "ok");
    } catch (error) {
        notify("The selected ingredients were not approved. " + describeError(error), "error");
    }
}




async function saveOrReload(ops) {
    try {
        for (let i = 0; i < ops.length; i += BATCH_LIMIT) {
            const batch = writeBatch(db);
            ops.slice(i, i + BATCH_LIMIT).forEach((op) => { if (op.data) batch.update(op.ref, op.data); else batch.delete(op.ref); });
            await batch.commit();
        }
    } catch (error) {
        console.error("Saving failed: ", error);
        await loadIngredients();
        throw new Error(describeError(error));
    }
}
const pick = (keys) => Array.from(new Set(keys)).map((key) => everyIngredient.find((item) => item.id === key)).filter((item) => item);
const named = (items) => (items.length === 1 ? `"${titleOf(items[0])}"` : many(items.length, "ingredient", "ingredients"));
const refOf = (item) => doc(db, "ingredients", item.id);

async function moveToFolder(keys, folder) {
    const list = pick(keys).filter((item) => !item.binned && sameKey(item.folder) !== sameKey(folder));
    if (list.length === 0) throw friendly(folder ? `Already in "${folder}".` : "Not in a folder.");
    const before = list.map((item) => ({ item, folder: item.folder }));
    const write = (changes) => saveOrReload(changes.map((c) => ({ ref: refOf(c.item), data: { folder: c.folder, updatedAt: serverTimestamp() } })));
    list.forEach((item) => { item.folder = folder; });
    afterChange();
    await write(list.map((item) => ({ item, folder })));
    return { text: named(list), undo: async () => { before.forEach((c) => { c.item.folder = c.folder; }); afterChange(); await write(before); } };
}

async function refolder(from, to) {
    const list = everyIngredient.filter((item) => sameKey(item.folder) === sameKey(from));
    list.forEach((item) => { item.folder = to; });
    afterChange();
    await saveOrReload(list.map((item) => ({ ref: refOf(item), data: { folder: to, updatedAt: serverTimestamp() } })));
}

async function moveToBin(keys) {
    const list = pick(keys).filter((item) => !item.binned);
    if (list.length === 0) throw friendly("Nothing to remove.");
    list.forEach((item) => { item.binned = true; item.binnedAt = Date.now(); });
    if (list.some((item) => item.id === openId)) openId = "";
    afterChange();
    await saveOrReload(list.map((item) => ({ ref: refOf(item), data: { binned: true, binnedAt: serverTimestamp(), updatedAt: serverTimestamp() } })));
    return { text: named(list), undo: () => restoreFromBin(keys) };
}

async function restoreFromBin(keys, folder) {
    const list = pick(keys).filter((item) => item.binned);
    if (list.length === 0) throw friendly("Nothing to restore.");
    list.forEach((item) => { item.binned = false; item.binnedAt = 0; item.binnedReason = ""; if (folder) item.folder = folder; });
    afterChange();
    await saveOrReload(list.map((item) => ({ ref: refOf(item), data: Object.assign({ binned: false, binnedReason: "", updatedAt: serverTimestamp() }, folder ? { folder } : null) })));
    return { text: named(list) };
}


async function purgeFromBin(keys) {
    const list = pick(keys).filter((item) => item.binned);
    if (list.length === 0) return false;
    const used = productUseKnown ? list.filter((item) => usedInProducts(item) > 0).length : 0;
    if (!(await askConfirm({
        title: `Delete forever: ${many(list.length, "ingredient", "ingredients")}?`,
        message: (used > 0 ? `${many(used, "of them is", "of them are")} listed by products in the inventory. ` : "") + "The app falls back to its built-in knowledge for them (or treats them as unknown). This cannot be undone.",
        items: list.map(titleOf), confirmLabel: "Delete forever", danger: true
    }))) return false;
    try {
        for (let i = 0; i < list.length; i += BATCH_LIMIT) {
            const batch = writeBatch(db);
            list.slice(i, i + BATCH_LIMIT).forEach((item) => batch.delete(refOf(item)));
            await batch.commit();
        }
        notify(`${many(list.length, "ingredient", "ingredients")} deleted for good.`, "ok");
    } catch (error) {
        console.error("Error deleting ingredients: ", error);
        notify("Not everything was deleted. " + describeError(error), "error");
    }
    await loadIngredients();
    return true;
}








async function applyChanges(changes) {
    const ops = [];
    changes.forEach((change) => {
        const item = everyIngredient.find((x) => x.id === change.docId);
        if (!item || change.collection !== "ingredients") return;
        if (change.action === "bin") { if (!item.binned) ops.push({ ref: refOf(item), data: { binned: true, binnedAt: serverTimestamp(), binnedReason: String(change.reason || "").slice(0, 200), updatedAt: serverTimestamp() } }); }
        else if (change.action === "update" && change.fields) ops.push({ ref: refOf(item), data: Object.assign({}, change.fields, { updatedAt: serverTimestamp() }) });
    });
    if (ops.length === 0) return 0;
    await saveOrReload(ops);
    await loadIngredients();
    return ops.length;
}




let scanning = false;

let differentIds = new Set();
async function openCleanUp() {
    if (scanning) { notify("A scan is running already.", "info"); return; }
    scanning = true;
    const scan = showScan("Clean up the ingredients", `Scanning ${lookalikeCount(everyIngredient)} ingredients: {done} / {total} checked`);
    scan.update(0, lookalikeCount(everyIngredient));
    let groups = null;
    try { groups = await ingredientLookalikesAsync(everyIngredient, 0.93, { onProgress: scan.update, cancelled: scan.cancelled }); }
    catch (error) { console.error("Clean up: the scan failed:", error); scan.close(); scanning = false; notify("The scan did not finish. Please try again.", "error"); return; }
    scan.close();
    scanning = false;
    if (groups === null) return;
    showCleanUp(groups);
}
function showCleanUp(lookalikes) {
    openPlanDialog({
        title: "Clean up the ingredients",
        intro: "Entries that are not ingredients (notes, two names joined into one, label text), and entries that explain an ingredient that has an entry already. Open the step to see every entry, take the tick off what should stay, and apply it. Nothing is deleted: what is removed goes to the Recycle bin.",
        makePlan: () => cleanupPlan([], [], everyIngredient, { only: ["joinedNames", "friendlyNames", "ingredients", "markdown", "definitions", "safetyClaims"], classifications: classifications() }),
        apply: (changes) => applyChanges(changes),
        notes: () => [{
            title: "Ingredient names that are nearly the same", text: "Chemical names can differ by one letter and still be different ingredients (methylparaben, ethylparaben), so these are only listed.",
            items: lookalikes.map((group) => group.items.map(titleOf).join("  /  ")),
            action: { label: "Open Find duplicates", run: openDuplicates }
        }]
    });
}

function openDuplicates() {
    if (scanning) { notify("A scan is running already.", "info"); return; }
    const origin = (item) => originLabel(item.source) || "Origin not recorded";
    openDuplicatesDialog({
        title: "Find duplicate ingredients",
        intro: "Entries that explain the same ingredient, or have nearly the same name. Most often the app's AI lookup made a new entry although one was there already. Choose which entry to keep; merging gives it the other names, so every product list is still explained. Chemical names can differ by one letter and be different ingredients: look before you merge.",
        threshold: 0.93,
        sections: [{
            id: "ingredients", label: "Ingredients",

            find: async (threshold, progress) => {
                scanning = true;
                try {
                    const possible = await ingredientLookalikesAsync(everyIngredient, threshold, progress);
                    return possible === null ? null : { sure: ingredientDuplicates(everyIngredient), possible };
                } finally { scanning = false; }
            },
            describe: (item) => ({
                title: titleOf(item),
                badges: (item.riskLevel ? [{ text: RISK_NAMES[item.riskLevel], tone: item.riskLevel === "safe" ? "good" : item.riskLevel === "caution" ? "warn" : "bad" }] : [])
                    .concat(item.source === "ai" ? [{ text: needsReview(item) ? "found by the AI, not reviewed" : "found by the AI", tone: "review" }] : []),
                lines: [item.techName && item.techName !== item.name ? `On labels: ${item.techName}` : "", item.aliases.length > 0 ? `Also: ${item.aliases.slice(0, 6).join(", ")}` : "",
                    [origin(item), productUseKnown ? `in ${many(usedInProducts(item), "listed product", "listed products")}` : "", item.addedAt ? "added " + timeAgo(new Date(item.addedAt * 1000)) : ""].filter((t) => t).join(" · ")]
            }),
            explainMerge: (keep, others) => {
                const merged = mergeIngredients(keep, others), lines = [`"${titleOf(keep)}" is kept, with its own wording and risk level.`];
                if (merged.keepChanges.aliases) lines.push(`It also answers to: ${merged.keepChanges.aliases.join(", ")}.`);
                else lines.push("It knows all the names of the others already.");
                others.forEach((item) => lines.push(`"${titleOf(item)}" goes to the Recycle bin. What it said is not copied over.`));
                return lines;
            },
            merge: (keep, others) => {
                const merged = mergeIngredients(keep, others);
                return applyChanges((Object.keys(merged.keepChanges).length > 0 ? [{ collection: "ingredients", docId: keep.id, action: "update", fields: merged.keepChanges }] : [])
                    .concat(merged.bin.map((gone) => ({ collection: "ingredients", docId: gone.item.id, action: "bin", reason: gone.reason }))));
            },
            remove: (others, keep) => applyChanges(others.map((item) => ({ collection: "ingredients", docId: item.id, action: "bin", reason: `Removed as a copy of "${titleOf(keep)}".` }))),

            whyNotRemove: (item) => (productUseKnown && usedInProducts(item) > 0 ? `${many(usedInProducts(item), "listed product uses", "listed products use")} "${titleOf(item)}".` : "")
        }]
    });
}




function openIngredientForm(item) {
    const form = el("addIngredientForm");
    form.reset();
    setFormError(form, "");
    form.querySelectorAll(".paste-note").forEach((note) => note.remove());
    editingIngredientId = item ? item.id : null;
    differentIds = new Set(item ? item.differentFrom : []);
    assisted = null;
    fieldFound = {};
    definitionBasis = null;
    el("modalTitle").textContent = item ? "Edit Ingredient" : "Add Ingredient";

    const note = el("ingredientReviewNote");
    if (item && needsReview(item)) {
        const proof = proofLines(item);
        note.innerHTML = "The app's AI lookup wrote this entry and nobody has checked it yet. Compare every box with the sources, correct what is wrong, then save: <b>saving marks it as reviewed</b>, and the app then trusts it like an entry typed here."
            + (proof.length > 0 ? "<br>" + proof.join("<br>") : "");
        note.style.display = "";
    } else {
        note.textContent = "";
        note.style.display = "none";
    }

    el("ingredientNameInput").value = item ? item.name : "";
    paintFieldSources();
    showNameClash();
    fillClassInput(item ? item.classificationId : "");
    el("ingredientPurposeInput").value = item ? item.purpose : "";
    paintPurposeCount();
    el("ingredientDescInput").value = item ? item.description : "";
    el("ingredientAliasesInput").value = item ? item.aliases.join(", ") : "";
    showNameClash();
    el("ingredientChemInput").value = item ? item.chemicalDescription : "";
    el("ingredientSourcesInput").value = item ? item.sources.join("\n") : "";
    el("ingredientRiskInput").value = item ? item.riskLevel : "";
    el("ingredientMinAgeInput").value = item && item.minAge > 0 ? item.minAge : "";
    el("ingredientCautionInput").value = item ? item.cautionNote : "";
    el("ingredientUsesInput").value = item ? item.commonUses : "";
    renderPickers(item);
    el("ingredientKeyActiveInput").checked = item ? item.keyActive : false;
    el("ingredientUmbrellaInput").checked = item ? item.umbrella : false;
    writeChecks(el("allergenTagsChecks"), item ? item.allergenTags : []);
    writeChecks(el("highlightTagsChecks"), item ? item.highlights : []);
    writeFit(el("skinTypesFit"), item ? item.avoidForSkinTypes : [], item ? item.goodForSkinTypes : []);
    writeFit(el("conditionsFit"), item ? item.avoidForConditions : [], item ? item.goodForConditions : []);
    modal.open();
    paintLimited();
    if (sourceUi) sourceUi.fill(item);
    windowKit.reset();
}

async function saveIngredient(event) {
    event.preventDefault();
    const form = el("addIngredientForm");
    const saveButton = el("saveIngredientBtn");
    if (saveButton.disabled) return;
    if (TEST_MODE) { setFormError(form, "This is the test copy of the site: saving is switched off here, so no live ingredient gets a test classification."); return; }

    const clean = (id) => el(id).value.replace(/\s+/g, " ").trim();
    const name = clean("ingredientNameInput");
    const classificationId = el("ingredientClassInput").value;
    const purpose = clean("ingredientPurposeInput");
    const description = el("ingredientDescInput").value.trim();
    const aliases = toList(el("ingredientAliasesInput").value).filter((a) => sameKey(a) !== sameKey(name));
    const minAgeText = el("ingredientMinAgeInput").value.trim();
    const minAge = toWholeNumber(minAgeText);

    const types = readFit(el("skinTypesFit")), conditions = readFit(el("conditionsFit"));
    const avoidTypes = types.avoid, goodTypes = types.good, avoidConditions = conditions.avoid, goodConditions = conditions.good;

    if (!name) { setFormError(form, "Type the INCI name."); el("ingredientNameInput").focus(); return; }


    if (/\s\/\s/.test(name)) {
        const parts = name.split(/\s+\/\s+/).map((p) => p.trim()).filter(Boolean);
        el("ingredientNameInput").value = parts[0];
        const others = toList(el("ingredientAliasesInput").value);
        parts.slice(1).forEach((p) => { if (!others.some((o) => sameKey(o) === sameKey(p))) others.push(p); });
        el("ingredientAliasesInput").value = others.join(", ");
        setFormError(form, `The name box holds only the INCI name. "${name}" was split: "${parts[0]}" is the INCI name, and ${parts.slice(1).map((p) => `"${p}"`).join(", ")} moved to Also known as. Check it, then save again.`);
        el("ingredientNameInput").focus();
        return;
    }


    const existing = editingIngredientId ? everyIngredient.find((i) => i.id === editingIngredientId) : null;
    if ((!classificationId || classificationId === ADD_CLASS) && (!existing || CLASSIFICATION_REQUIRED_ON_EXISTING || !lacksClass(existing))) { setFormError(form, "Choose the ingredient classification."); el("ingredientClassInput").focus(); return; }
    if (!purpose && (!existing || PURPOSE_REQUIRED_ON_EXISTING || existing.purpose)) { setFormError(form, "Write one line on what this ingredient does in a product."); el("ingredientPurposeInput").focus(); return; }
    if (!description) { setFormError(form, "Write a short user-friendly definition."); el("ingredientDescInput").focus(); return; }

    const sourceLines = el("ingredientSourcesInput").value.split(/\r?\n/).map((line) => line.replace(/\s+/g, " ").trim()).filter(Boolean);
    const badLine = sourceLines.find((line) => { try { return new URL((line.split("|").pop() || "").trim()).protocol !== "https:"; } catch (error) { return true; } });
    if (badLine) { setFormError(form, "Each reference link must contain a web address that starts with https://  (" + badLine.slice(0, 50) + ")"); el("ingredientSourcesInput").focus(); return; }
    const badSource = sourceUi ? sourceUi.firstError() : null;
    if (badSource) { setFormError(form, badSource.message); badSource.input.focus(); return; }
    if (minAgeText !== "" && (!/^\d+$/.test(minAgeText) || minAge > 99)) { setFormError(form, "Minimum age must be a whole number from 0 to 99."); el("ingredientMinAgeInput").focus(); return; }


    const myKeys = new Set([name, ...aliases].map(sameKey));


    const clash = everyIngredient.find((other) => other.id !== editingIngredientId && !differentIds.has(other.id)
        && (other.binned ? keysOf(other).some((k) => myKeys.has(k)) : sameKey(other.name) === sameKey(name)));
    if (clash) { setFormError(form, clash.binned ? `"${titleOf(clash)}" uses one of these names and is in the Recycle bin. Restore it from there instead.` : `"${titleOf(clash)}" already uses one of these names. Edit that entry instead.`); return; }
    const unsure = nameClashes(name, aliases);
    if (unsure.length > 0) { showNameClash(); setFormError(form, `"${titleOf(unsure[0])}" already uses one of these names. Open it, or tell the system this is a different ingredient (the button under the name box).`); el("ingredientNameInput").focus(); return; }
    setFormError(form, "");

    const data = {
        name, description, aliases, classificationId: classificationId === ADD_CLASS ? "" : classificationId, purpose,
        riskLevel: el("ingredientRiskInput").value,
        cautionNote: clean("ingredientCautionInput").replace(/^it\s+/i, "").replace(/\.$/, ""),
        commonUses: clean("ingredientUsesInput"),
        minAge,
        keyActive: el("ingredientKeyActiveInput").checked,
        highlights: readChecks(el("highlightTagsChecks")),
        chemicalDescription: el("ingredientChemInput").value.replace(/\s+/g, " ").trim().slice(0, 600),
        sources: sourceLines.slice(0, 10),
        umbrella: el("ingredientUmbrellaInput").checked,
        differentFrom: Array.from(differentIds).slice(0, 50),
        allergenTags: readChecks(el("allergenTagsChecks")),
        avoidForSkinTypes: avoidTypes,
        goodForSkinTypes: goodTypes,
        avoidForConditions: avoidConditions,
        goodForConditions: goodConditions,
        updatedAt: serverTimestamp()
    };

    const before = ingredients.find((i) => i.id === editingIngredientId);
    if (sourceUi) {
        const links = sourceUi.values();
        for (const f of SOURCE_LINK_FIELDS) {
            if (!(f.data in links)) continue;
            const had = before && before.sourceLinks && before.sourceLinks[f.key];
            if (links[f.data] || had) data[f.data] = links[f.data];
        }
    }
    const nowReviewed = !!before && before.source !== "" && before.source !== "admin" && !before.reviewed;
    if (nowReviewed) { data.reviewed = true; data.reviewedBy = reviewer(); data.reviewedAt = serverTimestamp(); }

    if (assisted && !editingIngredientId)
        Object.assign(data, { source: "aiassist", reviewed: true, reviewedBy: reviewer(), reviewedAt: serverTimestamp(), webSearched: assisted.sources.length > 0, model: assisted.model,
            sources: Array.from(new Set(assisted.sources.map((p) => `${p.title || p.site || "web page"} | ${p.url}`).concat(sourceLines))).slice(0, 10) });



    const unbacked = data.riskLevel === "harmful" && !(before && before.riskLevel === "harmful") ? evidenceProblem("harmful", data.sources) : "";
    if (unbacked) { setFormError(form, unbacked); el("ingredientSourcesInput").focus(); return; }

    setBusy(saveButton, true, "Saving...");
    try {
        if (editingIngredientId) {
            await updateDoc(doc(db, "ingredients", editingIngredientId), { ...data, techName: deleteField() });
            notify(nowReviewed ? "Ingredient saved and marked as reviewed." : "Ingredient saved.", "ok");
        } else {
            const folder = org.view.kind === "folder" ? org.view.name : "";
            await addDoc(collection(db, "ingredients"), { ...data, folder, createdAt: serverTimestamp() });
            notify(folder ? `Ingredient added to "${folder}".` : "Ingredient added.", "ok");
        }
        modal.close();
        await loadIngredients();
    } catch (error) {
        console.error("Error saving ingredient: ", error);
        setFormError(form, "The ingredient was not saved. " + describeError(error));
    } finally {
        setBusy(saveButton, false);
    }
}




function onListClick(event) {
    const button = event.target.closest("button");
    if (!button) {

        if (event.target.closest("a, .detail-box")) return;
        const card = event.target.closest(".ingredient-card");
        if (card) toggleDetails(card.dataset.id);
        return;
    }
    const act = button.dataset.act, id = button.dataset.id || "";
    if (act === "write") {
        openIngredientForm(null);
        el("ingredientNameInput").value = button.dataset.name || "";
        el("ingredientDescInput").focus();
        return;
    }
    const item = everyIngredient.find((i) => i.id === id);
    if (!item) return;
    if (act === "edit") openIngredientForm(item);
    else if (act === "approve") approveIngredient(item, button);
    else if (act === "folder") org.chooseFolder(button, [item.id]);
    else if (act === "bin") org.toBin([item.id]);
    else if (act === "restore") org.restore([item.id]);
    else if (act === "purge") org.purge([item.id]);
}


async function approveIngredient(item, button) {
    if (button.disabled) return;
    const unbacked = item.riskLevel === "harmful" ? evidenceProblem("harmful", item.sources) : "";
    if (unbacked) { notify(`"${titleOf(item)}" was not approved. ${unbacked} Use Edit to add the page or to change the rating.`, "error"); return; }
    button.disabled = true;
    try {
        await updateDoc(doc(db, "ingredients", item.id), { reviewed: true, reviewedBy: reviewer(), reviewedAt: serverTimestamp() });
        notify(`"${titleOf(item)}" approved. The app now trusts this entry.`, "ok");
        await loadIngredients();
    } catch (error) {
        console.error("Error approving ingredient: ", error);
        notify("The ingredient was not approved. " + describeError(error), "error");
        button.disabled = false;
    }
}


async function clearBrokenUses() {
    const list = everyIngredient.filter((item) => item.usesBroken);
    if (list.length === 0) return;
    if (!(await askConfirm({
        title: `Clear the unreadable "Used in" text on ${many(list.length, "ingredient", "ingredients")}?`,
        message: "An older build of the app wrote text there that is not a description. Only that one box is emptied; everything else stays.",
        items: list.map(titleOf), confirmLabel: "Clear it"
    }))) return;
    try {
        await saveOrReload(list.map((item) => ({ ref: refOf(item), data: { commonUses: "", updatedAt: serverTimestamp() } })));
        notify(`Cleared on ${many(list.length, "ingredient", "ingredients")}.`, "ok");
        quick = "";
        await loadIngredients();
    } catch (error) { notify("Not cleared. " + error.message, "error"); }
}




function registerHelpers() {
    const ready = () => everyIngredient.length > 0;
    const showLabel = (on) => (on ? "Show all" : "Show");
    const reviewing = () => el("ingredientSourceFilter").value === "review";
    const setQuick = (name) => { quick = quick === name ? "" : name; el("ingredientSearchInput").value = ""; el("ingredientSourceFilter").value = ""; el("ingredientRiskFilter").value = ""; if (org.view.kind !== "all") org.show({ kind: "all" }); else fromFirstPage(); };

    registerAssist("counts", () => {
        if (!ready()) return [];
        const review = ingredients.filter(needsReview).length;
        if (review > 0) return [{ tone: "info", text: `Next: ${many(review, "ingredient the app's AI found waits", "ingredients the app's AI found wait")} for your review. Until then the app uses the warnings but never counts ${review === 1 ? "it" : "them"} as proof of safety.`, label: reviewing() ? "Show all" : "Show them", run: () => narrow(reviewing() ? "" : "review", "") }];
        return [{ tone: "ok", text: "Every ingredient has been checked by an admin." }];
    }, { notes: "notesList" });

    registerAssist("tools", () => {
        if (!ready()) return [];
        const parts = [];
        const typed = el("ingredientSearchInput").value.trim(), source = FILTERS.find((f) => f.value === el("ingredientSourceFilter").value), risk = el("ingredientRiskFilter").value, now = org.view;
        if (typed) parts.push(`search "${typed}"`);
        if (source && source.value) parts.push(source.label.toLowerCase());
        const classChoice = el("ingredientClassFilter");
        if (classChoice.value) parts.push(classChoice.value === NO_CLASS ? "needs classification" : classificationName(classChoice.value));
        if (risk) parts.push(RISK_NAMES[risk].toLowerCase());
        if (now.kind === "folder") parts.push(`folder "${now.name}"`);
        if (now.kind === "bin") parts.push("the Recycle bin");
        if (quick) parts.push(QUICK[quick].label.toLowerCase());
        if (parts.length > 0) return [{ tone: "info", text: `Showing only: ${parts.join(" · ")}.`, label: "Show everything", run: () => narrow("", "") }];
        return [
            { text: `Sorted by ${sortNow().short}. Search also finds other spellings and words in the definition.` },
            { text: "Drag a card onto a folder or the bin. Ctrl+click picks several cards; Shift+click picks a range." }
        ];
    }, { notes: "notesList" });

    registerAssist("list", () => {
        if (!ready()) return [];
        if (org.view.kind === "bin") return [binIngredients.length > 0
            ? { tone: "info", text: `${many(binIngredients.length, "ingredient", "ingredients")} in the Recycle bin. The app leaves them out. Restore brings one back; Delete forever is final.` }
            : { tone: "ok", text: "The Recycle bin is empty." }];
        const lines = [];
        const count = (name) => ingredients.filter(QUICK[name].test).length;
        if (fieldChangeWaits()) lines.push({ tone: "warn", text: "The ingredient fields have not been changed yet: the classification list and the move of old INCI values into Also Known As are waiting.", label: "Look at it", run: runFieldChange, tip: "You see what it will do and confirm first. A backup is saved on this computer." });
        const unclassified = ingredients.filter(lacksClass).length, noPurpose = count("noPurpose");
        if (unclassified > 0) lines.push({ tone: "warn", text: `${many(unclassified, "ingredient needs", "ingredients need")} a classification. Until then the app shows no badge for ${unclassified === 1 ? "it" : "them"}.`, label: el("ingredientClassFilter").value === NO_CLASS ? "Show all" : "Show", run: () => { const select = el("ingredientClassFilter"); select.value = select.value === NO_CLASS ? "" : NO_CLASS; if (org.view.kind !== "all") org.show({ kind: "all" }); else fromFirstPage(); } });
        if (noPurpose > 0) lines.push({ tone: "warn", text: `${many(noPurpose, "ingredient has", "ingredients have")} no purpose yet. Until then the app shows no Purpose line for ${noPurpose === 1 ? "it" : "them"}.`, label: showLabel(quick === "noPurpose"), run: () => setQuick("noPurpose") });
        const broken = everyIngredient.filter((item) => item.usesBroken).length, review = ingredients.filter(needsReview).length, noNote = count("noNote"), unused = count("unused");
        if (broken > 0) lines.push({ tone: "warn", text: `${many(broken, "entry has", "entries have")} unreadable text in "Used in" (an older build of the app wrote it). It is hidden here.`, label: "Clear it", run: clearBrokenUses, tip: "You see the list and confirm first. Only that box is emptied." });
        const notReal = ingredientIssues(everyIngredient).length, twice = ingredientDuplicates(everyIngredient).length;
        if (notReal + twice > 0) lines.push({ tone: "warn", text: [notReal > 0 ? `${many(notReal, "entry is", "entries are")} not an ingredient (a note, joined names or label text)` : "", twice > 0 ? `${many(twice, "ingredient has", "ingredients have")} more than one entry` : ""].filter((t) => t).join("; ") + ".", label: "Clean up", run: openCleanUp, tip: "You see every entry with the reason and confirm first. Nothing is deleted." });
        if (review > 0) lines.push({ tone: "info", text: `${many(review, "AI-found entry waits", "AI-found entries wait")} for review.`, label: showLabel(reviewing()), run: () => narrow(reviewing() ? "" : "review", "") });
        if (noNote > 0) lines.push({ tone: "warn", text: `${many(noNote, "flagged ingredient has", "flagged ingredients have")} no caution note, so app users see the warning without a reason.`, label: showLabel(quick === "noNote"), run: () => setQuick("noNote") });
        const noEvidence = count("noEvidence");
        if (noEvidence > 0) lines.push({ tone: "warn", text: `${many(noEvidence, "ingredient is", "ingredients are")} rated Harmful without a regulator's page. Add the page (EU, ASEAN, FDA Philippines or US FDA) or lower the rating to Caution.`, label: showLabel(quick === "noEvidence"), run: () => setQuick("noEvidence") });
        if (unused > 0) lines.push({ text: `${many(unused, "ingredient is", "ingredients are")} not in any listed product yet.`, label: showLabel(quick === "unused"), run: () => setQuick("unused") });
        if (binIngredients.length > 0) lines.push({ text: `${many(binIngredients.length, "ingredient is", "ingredients are")} in the Recycle bin.`, label: "Open the bin", run: () => org.show({ kind: "bin" }) });
        if (fromProducts.length > 0) lines.push({ tone: "info", text: `${many(fromProducts.length, "ingredient that products in the inventory list has", "ingredients that products in the inventory list have")} no entry here yet. Most used: ${fromProducts.slice(0, 4).map((x) => x.name).join(", ")}.`, label: "Show them", run: () => narrow(FROM_PRODUCTS, "") });
        if (fdaBrands.length > 0) lines.push({ tone: "ok", text: `Brands checked on the FDA Philippines list (${fdaBrands.length}): ${fdaBrands.slice(0, 10).join(", ")}${fdaBrands.length > 10 ? ` and ${fdaBrands.length - 10} more` : ""}. The FDA list names products, not ingredients, so their ingredients arrive here when the products are scanned.`, label: "Open the inventory", run: () => { window.location.href = "../_Admin_Brand_Inventory/adminBrandInventory.html"; } });
        lines.push({ text: "A card shows the INCI name, the first alias and the classification. Click it for everything else; double-click to edit." });
        return lines;
    }, { notes: "notesList" });
}




function registerData() {
    registerTileDataset("curatorCounts", {
        label: "Counts", noun: "counts", nounOne: "count", valueNoun: "Number", selector: "#curatorStats .stat", watch: "#curatorStats",
        map: (tile) => ({ title: ((tile.querySelector(".stat-title") || {}).textContent || "").trim(), value: parseFloat(((tile.querySelector(".stat-value") || {}).textContent || "0").replace(/[^\d.]/g, "")) || 0, tone: "info" }),
        open: (tile) => tile.click()
    });
    registerDataset("ingredients", {
        label: "Ingredients", noun: "ingredients", nounOne: "ingredient",
        items: () => ingredients,
        map: (i) => ({
            id: i.id, title: titleOf(i), subtitle: firstAlias(i), meta: needsReview(i) ? "needs review" : RISK_NAMES[i.riskLevel || "auto"],
            tone: needsReview(i) ? "info" : toneOfRisk(i.riskLevel), value: productUseKnown ? usedInProducts(i) : undefined, valueLabel: "products"
        }),
        dimensions: [
            { id: "risk", label: "Risk level", of: (i) => i.riskLevel || "auto", keys: ["harmful", "caution", "safe", "auto"], labelOf: (k) => RISK_NAMES[k] || k, tone: toneOfRisk, pick: (k) => narrow("", k) },
            { id: "origin", label: "Where it came from", of: (i) => originLabel(i.source) || "Origin not recorded" },
            { id: "review", label: "Checked or not", of: (i) => (needsReview(i) ? "Waiting for review" : "Checked"), keys: ["Waiting for review", "Checked"], tone: (k) => (k === "Checked" ? "ok" : "info"), pick: (k) => narrow(k === "Checked" ? "" : "review", "") },
            { id: "allergen", label: "Allergen family", of: (i) => i.allergenTags.map((t) => labelOf(allOptions("allergens"), t)), none: "No allergen family" },
            { id: "avoid", label: "Chronic skin condition to avoid it", of: (i) => i.avoidForConditions.map((t) => labelOf(allOptions("conditions"), t)), none: "None" },
            { id: "skin", label: "Skin type to avoid it", of: (i) => i.avoidForSkinTypes.map((t) => labelOf(SKIN_TYPES, t)), none: "None" },
            { id: "used", label: "Used in a listed product", of: (i) => (usedInProducts(i) > 0 ? "In a listed product" : "Not in any product"), keys: ["In a listed product", "Not in any product"] },
            { id: "folder", label: "Folder", of: (i) => i.folder || null, none: "No folder", pick: (k) => org.show(k === "No folder" ? { kind: "all" } : { kind: "folder", name: k }) }
        ],
        open: (i) => openIngredientForm(i),

        bin: (rows) => org.toBin(rows.map((i) => i.id))
    });
}
