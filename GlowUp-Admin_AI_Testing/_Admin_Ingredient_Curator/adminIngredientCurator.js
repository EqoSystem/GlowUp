// Ingredient Curator: the 'ingredients' collection the mobile app reads when it analyses a scan.
// An ingredient document:
//   name (shown as "INCI name"), description            - what app users read (always filled in here)
//   classificationId, purpose                          - the classification's document id (see adminClassifications.js) and one line on what it does in a product;
//                                                        required in the form; empty = "Needs classification", and the app hides the badge and the line
//   aliases[]                                          - other spellings found on labels; the first one is shown as the common name
//   techName                                           - the old INCI field. Only entries from before the field change, or from the app's AI look-up, still have it:
//                                                        it is shown as an alias, and saving the entry removes it
//   riskLevel ("" | "safe" | "caution" | "harmful"), cautionNote, commonUses, minAge, keyActive, umbrella
//   allergenTags[], avoidForSkinTypes[], goodForSkinTypes[], avoidForConditions[], goodForConditions[]
//   source, reviewed, sources[], fdaUnii, webSearched  - only on entries that were NOT typed here:
//       source "ai"      = found by the app's AI lookup during a scan (document id starts with "ai_")
//       source "starter" = copied from the app's built-in list
//       source "fda"     = from the US FDA's list of prohibited and restricted cosmetic ingredients
//       reviewed false   = no admin has checked it yet. For an "ai" entry the app then uses its warnings
//                          but never treats it as proof that the ingredient is safe.
// Every field after "description" is optional: left empty, the app falls back to its built-in knowledge.
//
// Arranging and removing (see ../_Shared/adminOrganizer.js):
//   folder       - the name of the folder the entry was put in ("" = none). Only this website uses it.
//   binned true  - in the Recycle bin: the app leaves the entry out (app builds from 2026-10-10 on) until it
//                  is restored. Only "Delete forever" removes the document.
//
// A card shows the ingredient's name and its scientific name only. A click on it shows everything else.
import { db } from "../_Database/firebase-console.js";
import { collection, doc, serverTimestamp, deleteField } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
// reading and writing go through the site's own copy of its lists: a list is downloaded once and kept between pages,
// and after a change only the documents that were written are read again (see adminStore.js)
import { getDocs, addDoc, updateDoc, writeBatch, onListsRefreshed } from "../_Shared/adminStore.js";
import {
    requireAdmin, wireSignOut, setupNavigationGuard, setupModal, escapeHtml, sameKey, toList, asArray, toWholeNumber, toDate, timeAgo,
    notify, describeError, setBusy, setFormError, renderChecks, readChecks, writeChecks, originLabel, sourceLinks, assistSourceLinks, sourceGuideHtml, wireSearch,
    SKIN_TYPES, dialogIsOpen, statusBadge, toneBadge
} from "../_Shared/adminCommon.js";
import { matchesSearch, normalizeName, evidenceProblem } from "../_Shared/glowupRules.js";
import { auth } from "../_Database/firebase-console.js";
import { cleanupPlan, ingredientDuplicates, ingredientLookalikesAsync, lookalikeCount, ingredientIssues, mergeIngredients } from "../_Shared/glowupCatalog.js";
import { openPlanDialog, openDuplicatesDialog, showScan } from "../_Shared/adminCleanup.js";
import { mountShell, placeInHeader, paginate, renderPager, icon, BIN_ICON, openMenu, askConfirm, setBinCount } from "../_Shared/adminShell.js";
import { bindButtons } from "../_Shared/shortcuts.js";
import { readAdminDocument, splitForAi } from "../_Shared/adminDocumentReader.js";
import { startImport } from "../_Shared/importReview.js";
import { parseImportText, inventoryNotHere } from "../_Shared/importFormat.js";
import { loadLists, optionsOf, allOptions, openListManager, listsNow } from "../_Shared/adminLists.js";
import { BUILT_IN_HIGHLIGHTS } from "../_Shared/listRules.js";
import { registerDataset, datasetChanged } from "../_Shared/adminWidgets.js";
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

const pageState = { page: 1, size: 10 };          // the list shows ten ingredients at a time
const BATCH_LIMIT = 400;                          // Firestore allows 500 writes in one batch
const VIEW_KEY = "glowup.curator.view.v1";
// true  = an ingredient the app's AI lookup found is approved by itself when it comes with a CREDIBLE SOURCE (see hasCredibleSource):
//         nobody has to press Approve. It is marked "approved automatically", so it can still be found and corrected
//         (filter "AI-found (all)"). An entry without such a source keeps waiting for an admin's review.
// false = every such entry waits for review, and until then the app never counts it as proof that an ingredient is safe.
const AUTO_APPROVE_AI = true;
/**
 * Credible = the AI really searched the web and one of the pages it used backs up the rating it gave: a regulator for
 * "harmful", a regulator or a medical or scientific body for "caution" (the evidence tiers of glowupRules.js). Any other
 * web page, an ingredient database, and pages the AI only named from memory, do not count.
 */
// an entry that came from a document import or a user's photo (item.via) is NEVER approved by itself: a person has to look at it
const NEVER_AUTO = ["document-import", "snapshot"];
// ... and it needs a SOURCE LINK that backs up its rating by the evidence tiers of docs/DATA_SOURCE_POLICY.md (evidenceProblem in glowupRules.js):
// "harmful" needs a regulator's page, "caution" a regulator's or a medical / scientific body's page. An ingredient database alone is not enough
// (it explains what an ingredient is, it does not rate it). A claim written into the entry itself (a UNII code, "searched the web") is not enough:
// only the link counts, and sourceKind judges the real host of the address.
// Only a WARNING (caution or harmful) is approved by itself: a false warning does little harm, but "safe" or an empty level on a
// fabricated entry could hide a harmful ingredient, so those wait for a person.
const hasCredibleSource = (item) => !NEVER_AUTO.includes(item.via) && (item.riskLevel === "caution" || item.riskLevel === "harmful")
    && evidenceProblem(item.riskLevel, item.sources) === "";

let everyIngredient = [];             // every document, also the ones in the Recycle bin
let ingredients = [];                 // listed: [{ id, name, techName, description, ... }]
let binIngredients = [];              // in the Recycle bin
let productUse = new Map();           // ingredient key -> number of catalogue products that list it
let productUseKnown = false;
let editingIngredientId = null;
let modal = null;
let sourceUi = null;                     // the source link beside each box and the Source Finder (sourceLinks.js)
/** The header's "Sources: n of N fields" (N = boxes with a value, n = those with a source link). */
function paintSourceCount() {
    const chip = el("ingredientSourceCount");
    if (!chip || !sourceUi) return;
    const { n, total } = sourceUi.count();
    chip.textContent = `Sources: ${n} of ${total} fields`;
    chip.classList.toggle("ok", total > 0 && n === total);
}
let windowKit = { reset() {} };          // the section bar, focus trap and invalid marks of the window (set when the page starts)
let fromProducts = [];                // ingredient names the inventory's products list that the Curator has no entry for: [{ key, name, products[] }]
let fdaBrands = [];                   // the brands whose products were checked on the FDA Philippines list (for the helper line)
let fieldFound = {};                 // pages Assist found for each box of the form: { purpose: [page], description: [page], ... }
let definitionBasis = null;          // what the User-Friendly Definition rests on: { source, title, url, quote, how, replaced } (Assist, 9 November 2026)
let assisted = null;                  // description draft evidence: { sources, model } (null = the description was written by hand)
let ingredientAiDrafts = [];
let org = null;                       // folders, the Recycle bin, picking and dragging
let openId = "";                      // the card whose details are open
let quick = "";                       // a filter a helper switched on: "" | a key of QUICK
const view = { sort: "name", layout: "rows" };        // the order and the look of the list (rows or cards); remembered in this browser

const el = (id) => document.getElementById(id);
const RISK_LEVELS = ["safe", "caution", "harmful"];
const RISK_NAMES = { harmful: "Harmful", caution: "Caution", safe: "Safe", auto: "App decides" };
const FDA_SUBSTANCE_PAGE = "https://precision.fda.gov/uniisearch/srs/unii/";
const titleOf = (item) => item.name || item.techName || "Unnamed Ingredient";
const byName = (a, b) => titleOf(a).localeCompare(titleOf(b), undefined, { sensitivity: "base" });
const many = (n, one, more) => `${n} ${n === 1 ? one : more}`;
const toneOfRisk = (risk) => ({ harmful: "bad", caution: "warn", safe: "ok" }[risk] || "muted");
/** Text an old build of the app wrote when the AI answered with a list instead of a sentence. It is never shown. */
const brokenText = (text) => /System\.Collections\./i.test(String(text || ""));

/** Found by the app's AI lookup and not checked by an admin yet. */
const needsReview = (item) => item.source === "ai" && !item.reviewed;
/** The admin account that approves an entry is recorded on it (reviewedBy), so it is known who verified it. */
const reviewer = () => (auth.currentUser ? auth.currentUser.uid : "");

// the "show" box next to the search box
const FILTERS = [
    { value: "", label: "All ingredients", test: () => true },
    { value: "review", label: "AI-found, waiting for review", test: needsReview },
    { value: "ai", label: "AI-found (all)", test: (item) => item.source === "ai" },
    { value: "starter", label: "Starter data", test: (item) => item.source === "starter" || item.source === "fda" },
    { value: "admin", label: "Origin not recorded", test: (item) => item.source === "" || item.source === "admin" }
];
/** The choice that shows what the inventory's products list and the Curator does not explain yet. */
const FROM_PRODUCTS = "products";
const NO_CLASS = "__none";            // the classification filter's choice "Needs classification"
const ADD_CLASS = "__add";            // the form's choice "+ Add new classification"
let classCombo = null;                // the form's classification drop-down (adminSelect.js)
const lacksClass = (item) => needsClassification(item, classifications());

// the orders the Sort button offers
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

// the filters a helper can switch on
const QUICK = {
    broken: { label: "Broken \"Used in\" text", test: (item) => item.usesBroken },
    noNote: { label: "Flagged without a caution note", test: (item) => (item.riskLevel === "harmful" || item.riskLevel === "caution") && !item.cautionNote },
    unused: { label: "Not in any listed product", test: (item) => productUseKnown && usedInProducts(item) === 0 },
    noEvidence: { label: "Harmful without a regulator's page", test: (item) => item.riskLevel === "harmful" && evidenceProblem("harmful", item.sources) !== "" },
    noPurpose: { label: "No purpose yet", test: (item) => !item.purpose }
};

document.addEventListener("DOMContentLoaded", async () => {
    try { const saved = JSON.parse(localStorage.getItem(VIEW_KEY) || "{}"); if (SORTS.some((s) => s.id === saved.sort)) view.sort = saved.sort; } catch (error) { /* first visit */ }
    registerData();                   // before the page frame: a saved layout may hold panels that show this data
    mountShell("curator");
    // keyboard shortcuts (shortcuts.js): they press the same buttons, only when the buttons could be pressed
    bindButtons({ "curator.addIngredient": "#addIngredientBtn", "curator.import": "#chooseIngredientFileBtn", "curator.classifications": "#manageClassBtn",
        "curator.approveDrafts": "#ingredientApproveAllBtn", "curator.sort": "#sortMenuBtn" });
    placeInHeader(".search-container", "headerActions");      // search beside the title, + Ingredient and More beside the layout button
    wireSignOut();
    setupNavigationGuard();
    await requireAdmin();

    if (TEST_MODE) {                  // a preview or localhost: real ingredients, but a TEST copy of the classification list
        const banner = document.createElement("div");
        banner.className = "test-banner";
        banner.setAttribute("role", "note");
        banner.textContent = "TEST COPY: the classifications on this address are a separate test list. Saving an ingredient and the field change are switched off here, so nothing live is changed by them.";
        document.querySelector("main.main-content > header.topbar").after(banner);
    }
    await loadLists();                // allergen families, highlights and skin conditions: one document, built-in lists when it is missing
    await loadClassifications();      // their own collection: about 25 reads when the page opens
    // a database that has no classification at all gets the starting list once (an admin is signed in here); it is never added again by itself
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
            ingredients: () => everyIngredient,                 // also the ones in the Recycle bin: restoring one must not point at a deleted item
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
            { label: "Assist with AI", className: "bar-main", tip: "Draft sourced descriptions for selected ingredients.", icon: icon("sparkle"), run: assistSelectedIngredients },
            { label: "Approve All", className: "bar-quiet", tip: "Approve only selected AI entries that are still waiting for review.", run: approveSelectedIngredients }
        ],
        onView: () => { fromFirstPage(); }
    });
    setAssistPage("curator");
    registerHelpers();
    el("ingredientAiDraftList").addEventListener("change", updateIngredientDraftApproval);
    el("ingredientApproveAllBtn").addEventListener("click", approveIngredientDrafts);

    wireSearch(el("ingredientSearchInput"), el("ingredientSearchClear"), fromFirstPage);
    // what was behind "..." here is listed in the settings window under "This page" (5 November 2026)
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
        SORTS.map((s) => ({ label: s.label, checked: s.id === view.sort, run: () => { view.sort = s.id; try { localStorage.setItem(VIEW_KEY, JSON.stringify(view)); } catch (error) { /* private window */ } fromFirstPage(); } })))));
    el("ingredientQuick").addEventListener("click", () => { quick = ""; fromFirstPage(); });
    el("addIngredientBtn").addEventListener("click", () => openIngredientForm(null));
    Array.from(el("listLayoutSwitch").querySelectorAll(".seg")).forEach((button) => button.addEventListener("click", () => {
        view.layout = button.dataset.layout === "cards" ? "cards" : "rows";
        try { localStorage.setItem(VIEW_KEY, JSON.stringify(view)); } catch (error) { /* private window */ }
        paintListLayout();
    }));
    paintListLayout();
    // import of ingredient data from a file
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
    el("addIngredientForm").addEventListener("toggle", onSourcesToggle, true);       // "toggle" does not bubble: caught on the way down
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
    el("ingredientAliasesInput").addEventListener("input", showNameClash);
    el("ingredientsList").addEventListener("click", onListClick);
    el("ingredientsList").addEventListener("dblclick", (event) => {
        if (event.target.closest("button, a, .detail-box")) return;
        const card = event.target.closest(".ingredient-card");
        const item = card ? ingredients.find((i) => i.id === card.dataset.id) : null;
        if (item) openIngredientForm(item);
    });
    // the list on screen came from the site's own copy; when the database turns out to hold something newer, show that
    onListsRefreshed(() => { if (!dialogIsOpen()) loadIngredients(); });
    el("ingredientsList").addEventListener("keydown", (event) => { if (event.key === "Enter" && event.target.matches(".ingredient-card")) { event.preventDefault(); toggleDetails(event.target.dataset.id); } });

    // the Dashboard's shortcuts: ?add=1 opens the empty ingredient form, ?cleanup=1 the Clean up steps, once the ingredients are here
    const askedFor = new URLSearchParams(window.location.search);
    loadIngredients().then(() => {
        if (askedFor.get("add") === "1") openIngredientForm(null);
        else if (askedFor.get("cleanup") === "1") openCleanUp();
        else if (askedFor.get("duplicates") === "1") openDuplicates();
    });
});

function fromFirstPage() { pageState.page = 1; renderIngredients(); }

/** Rows (one ingredient per line) or cards: the list's look. */
function paintListLayout() {
    el("ingredientsList").classList.toggle("as-rows", view.layout !== "cards");
    Array.from(el("listLayoutSwitch").querySelectorAll(".seg")).forEach((button) => {
        const on = button.dataset.layout === view.layout;
        button.classList.toggle("on", on);
        button.setAttribute("aria-pressed", String(on));
    });
}
/** Narrows the list to one origin and one risk level (a count tile, a graph or a helper does this). */
function narrow(source, risk) {
    el("ingredientSearchInput").value = "";
    el("ingredientSourceFilter").value = source;
    el("ingredientRiskFilter").value = risk;
    el("ingredientClassFilter").value = "";
    quick = "";
    if (org.view.kind !== "all") org.show({ kind: "all" }); else fromFirstPage();
}

// ---------------------------------------------------------------------------------------------
// Loading and showing
// ---------------------------------------------------------------------------------------------
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

    // what the app's AI lookup added since the last visit is approved by itself
    if (AUTO_APPROVE_AI && !TEST_MODE) {                 // (a test address writes no live ingredient)
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
                console.warn("Automatic approval did not work: ", error);       // they simply stay "waiting for review"
            }
        }
    }

    // how many catalogue products list each ingredient ("Used in 3 products"). Optional: the list still shows without it.
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
        // what the products list and the Curator does not explain: most used first
        const known = new Set();
        everyIngredient.forEach((item) => { if (!item.binned) keysOf(item).forEach((k) => known.add(k)); });
        fromProducts = Array.from(listedBy.values()).filter((x) => x.key.length >= 2 && !known.has(x.key)).sort((a, b) => b.products.length - a.products.length || a.name.localeCompare(b.name));
        fdaBrands = Array.from(checkedBrands).filter((name) => name).sort((a, b) => a.localeCompare(b));
        productUseKnown = true;
    } catch (error) {
        console.warn("Could not read the products: ", error);
        productUseKnown = false;
    }
    afterChange();
}

/** Splits what was loaded into what is listed and what is in the Recycle bin, and draws everything again. */
function afterChange() {
    everyIngredient.sort(byName);
    ingredients = everyIngredient.filter((item) => !item.binned);
    binIngredients = everyIngredient.filter((item) => item.binned);
    setBinCount(binIngredients.length, "ingredients");
    if (org) org.sync(everyIngredient.map((item) => item.id));
    fillSourceFilter();
    fillClassFilter();
    renderIngredients();
    datasetChanged("ingredients");        // the data panels (lists, grids, graphs) draw the new data
}

/** The "show" box, with how many entries each choice has. The first time, an address ending in ?show=review opens on that choice. */
function fillSourceFilter() {
    const select = el("ingredientSourceFilter");
    const wanted = select.dataset.ready ? select.value : (new URLSearchParams(window.location.search).get("show") || "");
    select.dataset.ready = "1";
    select.innerHTML = FILTERS.map((f) => `<option value="${f.value}">${escapeHtml(f.label)} (${ingredients.filter(f.test).length})</option>`).join("")
        + `<option value="${FROM_PRODUCTS}">From products, not explained yet (${fromProducts.length})</option>`;
    select.value = wanted === FROM_PRODUCTS || FILTERS.some((f) => f.value === wanted) ? wanted : "";
}

/** The classification filter, with how many ingredients each choice has. */
function fillClassFilter() {
    const select = el("ingredientClassFilter"), wanted = select.value;
    const options = [make("", "Any classification"), make(NO_CLASS, `Needs classification (${ingredients.filter(lacksClass).length})`)];
    classifications().forEach((c) => options.push(make(c.id, `${c.name} (${ingredients.filter((i) => i.classificationId === c.id).length})`)));
    select.replaceChildren(...options);
    select.value = options.some((o) => o.value === wanted) ? wanted : "";
    function make(value, text) { const option = document.createElement("option"); option.value = value; option.textContent = text; return option; }
}

/** The form's classification box: the list, then "+ Add new classification". */
function fillClassInput(selected) {
    const select = el("ingredientClassInput");
    const make = (value, text) => { const option = document.createElement("option"); option.value = value; option.textContent = text; return option; };
    // the one list from the database: A to Z, "Other" last; the placeholder cannot be chosen; "+ Add" sits under a thin line
    const placeholder = make("", "Choose a classification"), line = make("", "-"), add = make(ADD_CLASS, "+ Add new classification");
    placeholder.disabled = true; line.disabled = true; line.setAttribute("data-sep", ""); add.setAttribute("data-add", "");
    select.replaceChildren(placeholder, ...classifications().map((c) => make(c.id, c.name)), line, add);
    select.value = classifications().some((c) => c.id === selected) ? selected : "";
    select.dataset.last = select.value;
    if (classCombo) classCombo.refresh();
}

function manageClassifications() {
    openClassificationManager({
        ingredients: () => everyIngredient,               // also the ones in the Recycle bin: restoring one must not point at a deleted classification
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
function paintPurposeCount() { el("ingredientPurposeCount").textContent = `${el("ingredientPurposeInput").value.length} / ${MAX_PURPOSE}`; }

/** True while the one-time field change has something left to do. */
const fieldChangeWaits = () => everyIngredient.length > 0 && (classifications().length === 0 || everyIngredient.some((item) => item.rawTech || !item.rawMarked));
function runFieldChange() { return openFieldMigration(async () => { await loadIngredients(); }); }

/** Every way this ingredient can be written on a label. */
function keysOf(item) {
    return [item.name, item.techName, ...item.aliases].map(sameKey).filter((k) => k.length > 0);
}

/** Listed entries (not this one, not already marked different) that use one of these names. */
function nameClashes(name, aliases) {
    const mine = new Set([name, ...(aliases || [])].map(sameKey).filter((k) => k.length > 0));
    return everyIngredient.filter((other) => other.id !== editingIngredientId && !other.binned && !differentIds.has(other.id) && keysOf(other).some((k) => mine.has(k)));
}

/** The note under the INCI name box: the name is used already. A different ingredient is told to the system with one button. */
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

/** Draws the allergen, highlight and condition pickers from the lists; what is ticked stays ticked, and a retired item an ingredient already has is still offered. */
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

/** Where an entry's information came from: the pages, and the US FDA substance registry's code when it has one. */
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

/** The four counts above the list; a click on one filters the list. */
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
    // the tiles and their labels are fixed HTML (they show before any data): only the numbers and the chosen one change here
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
    // Search: any letter case, with or without accents and punctuation, parts of words, several words in any order;
    // it looks at the name, the label name, the other spellings and the definition.
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

/** The ingredients the inventory's products list that have no entry yet: a card each, with the products that list it. */
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
                <div class="detail-actions"><button type="button" class="row-btn main" data-act="write" data-name="${escapeHtml(x.name)}" data-tip="Opens the form with the name filled in. You write what it is and how the app should judge it.">${icon("plus")}Write its entry</button>`
                    + `<button type="button" class="row-btn" data-act="assist" data-name="${escapeHtml(x.name)}" data-tip-title="Assist with AI" data-tip="The AI is given the COSMILE Europe record (when it has one) and is asked to use regulators, medical bodies and ingredient databases. It drafts the description, purpose, uses and other names; risk, allergen and skin boxes stay yours. Every page it used is listed with its kind. Nothing is saved until you have read it and pressed Save.">${icon("sparkle")}Assist with AI</button></div>
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

/** One card: the name and the scientific name. Clicked, it also shows everything else. */
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

/** The classification column: one neutral chip, or "Needs classification". */
const classCell = (item) => lacksClass(item) ? `<span class="chip warn">Needs classification</span>` : `<span class="chip">${escapeHtml(classificationName(item.classificationId))}</span>`;

/** Everything else about an ingredient: shown inside its card when the card is clicked. */
function detailOf(item, inBin) {
    const review = needsReview(item);
    const origin = originLabel(item.source);
    const facts = [];
    // the risk is on the card's own badge (with what it means in its tooltip): it is not said a second time here
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

// ---------------------------------------------------------------------------------------------
// "Avoid / no effect / Good" rows of the form: one row per skin type or condition
// ---------------------------------------------------------------------------------------------
/** Draws one row per option with three choices. `group` keeps the radio buttons of one row together. */
function renderFit(container, group, options) {
    container.innerHTML = options.map((o, i) => {
        const name = `fit_${group}_${i}`;
        const choice = (value, label, cls, tip) => `<label class="fit-choice ${cls}"><input type="radio" aria-label="${tip}" name="${name}" value="${value}"${value === "" ? " checked" : ""}><span>${label}</span></label>`;
        return `<fieldset class="fit-row" data-value="${escapeHtml(o.value)}"><legend class="sr-only">${escapeHtml(o.label)}</legend><span class="fit-name" aria-hidden="true">${escapeHtml(o.label)}</span><span class="fit-choices">`
            + choice("avoid", "Avoid", "avoid", "The app warns users with this") + choice("", "Neutral", "none", "No effect either way") + choice("good", "Good", "good", "The app counts it as a plus for users with this")
            + `</span></fieldset>`;
    }).join("");
}

/** What the rows say: { avoid: [...], good: [...] } (the values as they are stored). */
function readFit(container) {
    const result = { avoid: [], good: [] };
    container.querySelectorAll(".fit-row").forEach((row) => {
        const picked = row.querySelector("input:checked");
        if (picked && result[picked.value]) result[picked.value].push(row.dataset.value);
    });
    return result;
}

/** Sets the rows from the two stored lists (capital letters do not matter; a name in both lists counts as "avoid"). */
function writeFit(container, avoid, good) {
    const lower = (list) => new Set((Array.isArray(list) ? list : []).map((v) => String(v).toLowerCase()));
    const no = lower(avoid), yes = lower(good);
    container.querySelectorAll(".fit-row").forEach((row) => {
        const key = row.dataset.value.toLowerCase();
        const want = no.has(key) ? "avoid" : yes.has(key) ? "good" : "";
        row.querySelectorAll("input").forEach((input) => { input.checked = input.value === want; });
    });
}

// ---------------------------------------------------------------------------------------------
// Assist with AI: a DRAFT of the description only, with source pages (the server function api/assist-ingredient.js)
// ---------------------------------------------------------------------------------------------
const ASSIST_PER_DAY = 40;                    // drafts one browser may ask for in a day, so a slip cannot use up the AI's quota
const ASSIST_KEY = "glowup.curator.assist";

/** How many drafts were asked for today in this browser. */
function assistsToday(add) {
    const today = new Date().toISOString().slice(0, 10);
    let kept = {};
    try { kept = JSON.parse(localStorage.getItem(ASSIST_KEY) || "{}"); } catch (error) { kept = {}; }
    const count = (kept.day === today ? Number(kept.count) || 0 : 0) + (add ? 1 : 0);
    if (add) localStorage.setItem(ASSIST_KEY, JSON.stringify({ day: today, count }));
    return count;
}

/** The sources as links, each with its kind label (Regulator / government, Ingredient database ...), then the guide line. */
function assistSources(pages) {
    return assistSourceLinks(pages, "<b>The AI named no web page.</b> Treat every box as unchecked.");
}

/**
 * Under each box of the form that has a home in a dictionary or database: ONE "Sources (N)" row, closed until opened. Inside, one
 * list: the source (a link to its own site), what to look for there, and "+ ref" (only for the page of THIS ingredient). The page
 * Assist took a box's text from, and the source a definition rests on, stay above the row. Nothing is fetched here (see
 * _Shared/fieldSources.js for what each link is).
 */
const sourcesOpen = new Map();        // box name -> is its list open? (kept until the page is closed, so a repaint or the next ingredient does not fold it)
try { JSON.parse(sessionStorage.getItem("glowup.curator.sourcesOpen") || "[]").forEach((field) => sourcesOpen.set(field, true)); } catch (error) { /* browser storage may be blocked */ }
function rememberSources() {
    try { sessionStorage.setItem("glowup.curator.sourcesOpen", JSON.stringify(Array.from(sourcesOpen).filter(([, open]) => open).map(([field]) => field))); } catch (error) { /* keep it in memory only */ }
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
        // the definition says which source it rests on, with that source's own words, so the admin can check it in one look
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

// PubChem's page of THIS ingredient: its documented PUG REST service answers a name with a compound number. One request per click,
// 5 seconds at most, remembered for the name. (PubChem allows requests from a web page; nothing else is asked for.)
const pubchemAnswers = new Map();
async function pubchemPage(name) {
    const key = name.trim().toLowerCase();
    if (pubchemAnswers.has(key)) return pubchemAnswers.get(key);
    let cid = 0;
    try {
        const answer = await fetch(pubchemLookupUrl(name), { signal: AbortSignal.timeout(5000) });
        cid = answer.ok ? firstCompoundId(await answer.text()) : 0;
        if (answer.ok || answer.status === 404) pubchemAnswers.set(key, cid);      // a slow or failed answer is not remembered
    } catch (error) { cid = 0; }
    return cid;
}

function copyInciName(name) {
    if (name && navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(name).catch(() => {});
}

/** Clicks inside the Sources rows: a landing page copies the INCI name; PubChem opens this ingredient's page; "+ ref" adds that page to Reference links. */
// Click limit: a source opens at most once every 2 seconds (the same site from any row), and "+ ref" once per lookup, so an admin
// who taps again and again while a page is slow to open gets one tab and one message, not ten.
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
            const tab = window.open("about:blank", "_blank");              // opened now, while the click still counts, and sent on when PubChem has answered
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

/** Remembers which Sources rows are open (until the page is closed). */
function onSourcesToggle(event) {
    const box = event.target.closest && event.target.closest("details.sources-box");
    if (!box || event.target !== box) return;
    sourcesOpen.set(box.dataset.sourcesField, box.open);
    rememberSources();
}

/** A bare address in Reference links gets its label (from the site it is on) when the box is left. */
function labelReferenceLinks() {
    const box = el("ingredientSourcesInput");
    const labelled = labelBareUrls(box.value);
    if (labelled !== box.value) box.value = labelled.slice(0, box.maxLength > 0 ? box.maxLength : 900);
}

/** Pasting into the text boxes tidies the text (spaces, line breaks, list separators) and may show a non-blocking notice. Nothing is fetched. */
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

/** Asks the server for a description draft of `name`; other ingredient fields stay under the admin's control. */
async function assistWithAi(name) {
    const note = el("ingredientReviewNote"), form = el("addIngredientForm"), saveButton = el("saveIngredientBtn");
    const say = (html) => { note.innerHTML = html; note.style.display = ""; };
    if (!name) return;
    if (assistsToday(false) >= ASSIST_PER_DAY) { say(`Assist with AI was used ${ASSIST_PER_DAY} times today in this browser. Write this entry by hand, or go on tomorrow.`); return; }
    say(`${icon("sparkle")} <b>Asking the AI about "${escapeHtml(name)}"...</b> It is asked to use regulators, medical bodies and ingredient databases; this can take up to a minute.`);
    setBusy(saveButton, true, "Waiting for the AI...");
    try {
        const user = auth.currentUser;
        if (!user) throw new Error("You are signed out. Sign in again.");
        const stop = new AbortController();
        const timer = setTimeout(() => stop.abort(), 75000);
        let answer;
        try {
            answer = await fetch("/api/assist-ingredient", {
                method: "POST", signal: stop.signal,
                headers: { "Content-Type": "application/json", Authorization: "Bearer " + (await user.getIdToken()) },
                body: JSON.stringify({ mode: "description", name, aliases: toList(el("ingredientAliasesInput").value).slice(0, 3) })
            });
        } finally { clearTimeout(timer); }
        assistsToday(true);
        const body = await answer.json().catch(() => ({}));
        if (!answer.ok) throw new Error(body.error || "The AI could not be reached (error " + answer.status + ").");
        if (!dialogIsOpen() || el("ingredientNameInput").value.trim() !== name.trim()) return;      // the form was closed or used for something else meanwhile
        if (!body.found) { say(`<b>No description draft.</b> ${escapeHtml(body.message || "The AI found no source for this name.")}<br>${assistSources(body.sources)}`); return; }

        const d = body.draft;
        el("ingredientDescInput").value = d.description;
        // the other boxes are filled only when they are EMPTY: what the admin already wrote is never replaced
        const filled = ["Definition"];
        const fillEmpty = (id, value, label) => { const box = el(id); if (value && !box.value.trim()) { box.value = value; filled.push(label); box.dispatchEvent(new Event("input", { bubbles: true })); } };
        fillEmpty("ingredientPurposeInput", String(d.purpose || "").slice(0, MAX_PURPOSE), "Purpose");
        fillEmpty("ingredientUsesInput", d.commonUses, "Commonly used in");
        fillEmpty("ingredientChemInput", String(d.chemicalDescription || "").slice(0, 600), "What the chemical is");
        const names = toList(el("ingredientAliasesInput").value);
        const newNames = (d.aliases || []).filter((a) => sameKey(a) !== sameKey(name) && !names.some((n) => sameKey(n) === sameKey(a)));
        if (newNames.length) { el("ingredientAliasesInput").value = names.concat(newNames).join(", "); filled.push("Also known as"); el("ingredientAliasesInput").dispatchEvent(new Event("input", { bubbles: true })); }
        const links = el("ingredientSourcesInput");
        const newLinks = (d.links || []).filter((l) => !links.value.includes(l.url)).map((l) => `${l.title} | ${l.url}`);
        if (newLinks.length) { links.value = links.value.trim().split(/\r?\n/).concat(newLinks).filter(Boolean).join("\n"); filled.push("Reference links"); }
        assisted = { sources: body.sources || [], model: body.model || "" };
        fieldFound = body.fieldSources || {};
        definitionBasis = body.basis && typeof body.basis === "object" ? body.basis : null;
        paintFieldSources();
        const cosmile = body.cosmile && body.cosmile.found
            ? `<br><b>COSMILE Europe record used:</b> <a class="source-link" href="${escapeHtml(body.cosmile.url)}" target="_blank" rel="noopener noreferrer">${escapeHtml(body.cosmile.inci)}</a>`
            : `<br><span class="field-hint">COSMILE Europe has no record with exactly this name, so it was not used.</span>`;
        const backupNote = body.notes && body.notes.length > 0 ? `<br><span class="field-hint">Before it answered: ${escapeHtml(body.notes.join("; "))}.</span>` : "";
        say(`<b>This is a draft. Nothing is saved yet.</b> Filled: ${escapeHtml(filled.join(", "))}. Risk level, allergens and skin boxes are left to you. Read it against the sources, correct what is wrong, then press Save.`
            + cosmile
            + (body.searched === false ? `<br><b>This draft was written WITHOUT a web search (by ${escapeHtml(body.model || "a backup AI")}), from the AI's memory. It names no sources, so every box is unchecked: check it with the links below.</b>` : "")
            + `<br><b>Sources:</b><br>${assistSources(body.sources)}`
            + `<br><b>Check it yourself:</b> ` + (body.lookups || []).map((l) => `<a class="source-link" href="${escapeHtml(l.url)}" target="_blank" rel="noopener noreferrer">${escapeHtml(l.title)}</a>`).join(" · ")
            + backupNote);
    } catch (error) {
        const why = error && error.name === "AbortError" ? "The AI took too long." : (error && error.message) || "Something went wrong.";
        say(`<b>Assist with AI did not work.</b> ${escapeHtml(why)} You can still write the entry by hand.`);
        setFormError(form, "");
    } finally {
        setBusy(saveButton, false);
    }
}

// ---------------------------------------------------------------------------------------------
// Import ingredients from a file: ingredient data ONLY, saved as Pending (source "ai", not reviewed) for an admin to check
// ---------------------------------------------------------------------------------------------
// No size limit (9 November 2026): any PDF, Excel, Word, CSV or text file, of any length (adminDocumentReader.js says which types it reads).
const IMPORT_MAX_ROWS = 60;                   // ingredients saved per database batch (an import itself has no limit)

/**
 * The import drop box (importReview.js): read, then check every ingredient in a window, then save. Nothing reaches the
 * database before the admin has checked it and pressed Save.
 */
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

/** What a file's text holds, for the check window: { draft: { ingredients }, notes, model, refused }. Nothing is saved here. */
async function extractIngredients(documentText, say, signal) {
    // a file in the GlowUp import format is read as it is, with no AI; any other document is read by the AI
    const keyed = parseImportText(documentText);
    if (keyed.kind === "inventory") return { refused: "This file lists only brands or products. " + inventoryNotHere(keyed.draft.products.length + keyed.draft.brands.length) };
    if (keyed.kind === "mixed") return { refused: `This file mixes ${many(keyed.counts.ingredients, "ingredient record", "ingredient records")} with brands or products, so nothing was imported. ` + inventoryNotHere(keyed.counts.inventory) };
    if (keyed.kind === "ingredients") { say("Reading the file's own fields. No AI is used."); return { draft: keyed.draft, notes: keyed.notes.slice(0, 6), model: "" }; }
    if (assistsToday(false) >= ASSIST_PER_DAY) throw new Error(`The AI was used ${ASSIST_PER_DAY} times today in this browser. Try again tomorrow.`);
    // no size limit: the AI reads the file in parts, one after the other (the whole file counts once in the day's limit)
    const parts = splitForAi(documentText);
    const found = [], failedParts = [];
    let model = "", inventoryRecords = 0;
    for (let i = 0; i < parts.length; i++) {
        if (signal && signal.aborted) break;
        say(parts.length > 1 ? `Asking the AI about part ${i + 1} of ${parts.length}. Only the extracted text is sent, not the file.` : "Asking the AI to pick out the ingredients. Only the extracted text is sent, not the file.");
        try {
            const answer = await askCuratorApi({ mode: "ingredientImport", documentText: parts[i] }, signal);
            found.push(...((answer.draft && answer.draft.ingredients) || []));
            inventoryRecords += Number(answer.inventoryRecords) || 0;
            model = model || answer.model || "";
        } catch (error) {
            if (i === 0 || (signal && signal.aborted)) throw error;
            failedParts.push(`part ${i + 1} (${error && error.name === "AbortError" ? "the AI took too long" : (error && error.message) || "it failed"})`);
        }
    }
    assistsToday(true);
    const notes = (failedParts.length ? [`Not read: ${failedParts.join("; ")}. Import the file again later for those parts; what is saved now is then skipped as a duplicate.`] : [])
        .concat(inventoryRecords > 0 ? [inventoryNotHere(inventoryRecords)] : []);
    return { draft: { ingredients: found }, notes, model: model || "the AI" };
}

/** Writes the checked ingredients (NEW entries only, Pending) in groups; answers what was saved. */
async function saveIngredientDraft(draft, say) {
    const records = Array.isArray(draft.ingredients) ? draft.ingredients : [];
    const result = { saved: 0, skippedDuplicates: 0, skippedInvalid: 0, unmatched: [] };
    const groups = Math.max(1, Math.ceil(records.length / IMPORT_MAX_ROWS));
    for (let g = 0; g < groups; g++) {
        if (groups > 1) say(`Saving group ${g + 1} of ${groups}...`);
        const part = await saveImportedIngredients(records.slice(g * IMPORT_MAX_ROWS, (g + 1) * IMPORT_MAX_ROWS));
        result.saved += part.saved; result.skippedDuplicates += part.skippedDuplicates; result.skippedInvalid += part.skippedInvalid;
        part.unmatched.forEach((name) => { if (!result.unmatched.includes(name)) result.unmatched.push(name); });
        // the next group must see what this one added (duplicates inside one file)
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

/** Writes new ingredient entries only (never changes one that exists). Only ingredient fields are copied from a record. */
async function saveImportedIngredients(records) {
    const batch = writeBatch(db);
    const planned = new Set();
    const unmatched = new Set();          // allergen / condition / highlight names the page's lists do not have (left out)
    let saved = 0, skippedDuplicates = 0, skippedInvalid = 0;
    records.slice(0, IMPORT_MAX_ROWS).forEach((record) => {
        const name = String(record.name || "").replace(/\s+/g, " ").trim().slice(0, 80);
        if (name.length < 2) { skippedInvalid++; return; }
        // the file's label name becomes an alias when it differs from the name: the same rule as the field change
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
            // "ai" + not reviewed is the existing Pending state: the app uses such an entry as a warning at most, never as proof of safety
            source: "ai", reviewed: false, via: "document-import",
            createdAt: serverTimestamp(), updatedAt: serverTimestamp()
        });
        saved++;
    });
    if (saved === 0) return { saved, skippedDuplicates, skippedInvalid, unmatched: Array.from(unmatched) };
    await batch.commit();
    return { saved, skippedDuplicates, skippedInvalid, unmatched: Array.from(unmatched) };
}

/**
 * The ids of a list (allergens, conditions, highlights) that the names in a document stand for: a name matches an item's
 * label, id or one of its keywords. A name that matches nothing is noted in `unmatched` and left out (add it to the
 * list first with "Manage lists", then edit the ingredient).
 */
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

async function askCuratorAi(name) {
    return askCuratorApi({ mode: "description", name });
}

async function askCuratorApi(payload, signal = null) {
    const user = auth.currentUser;
    if (!user) throw new Error("You are signed out. Sign in again.");
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 75000);
    const onCancel = () => controller.abort();            // Cancel on the drop box stops the question at once
    if (signal) signal.addEventListener("abort", onCancel);
    let response;
    try {
        response = await fetch("/api/assist-ingredient", {
            method: "POST",
            signal: controller.signal,
            headers: { "Content-Type": "application/json", Authorization: "Bearer " + (await user.getIdToken()) },
            body: JSON.stringify(payload)
        });
    } finally {
        clearTimeout(timeout);
        if (signal) signal.removeEventListener("abort", onCancel);
    }
    const result = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(result.error || `The AI could not be reached (error ${response.status}).`);
    return result;
}

function paintIngredientDrafts() {
    const panel = el("ingredientAiDraftPanel"), list = el("ingredientAiDraftList");
    panel.hidden = ingredientAiDrafts.length === 0;
    el("ingredientAiDraftCount").textContent = many(ingredientAiDrafts.length, "draft", "drafts");
    list.innerHTML = ingredientAiDrafts.map((draft, index) => {
        const sources = assistSourceLinks(draft.sources, "");
        return `<article class="suggestion-card" data-draft-index="${index}">
            <strong>${escapeHtml(titleOf(draft.item))}</strong>
            <label class="field">Description draft <textarea data-draft-description rows="3" maxlength="600">${escapeHtml(draft.description)}</textarea></label>
            <p class="field-hint">AI: ${escapeHtml(draft.model || "unknown model")}${draft.searched === false ? " (no web search; verify carefully)" : ""}</p>
            <p class="field-hint">Sources: ${sources || "No source links were returned; verify this description yourself."}</p>
            <label class="field-hint"><input type="checkbox" data-draft-reviewed> I checked this description against its sources.</label>
        </article>`;
    }).join("");
    updateIngredientDraftApproval();
}

function updateIngredientDraftApproval() {
    const button = el("ingredientApproveAllBtn");
    if (button) button.disabled = !el("ingredientAiDraftList").querySelector("[data-draft-reviewed]:checked");
}

async function assistSelectedIngredients(keys) {
    const selected = keys.map((id) => everyIngredient.find((item) => item.id === id && !item.binned)).filter((item) => item && (item.name || item.techName));
    if (!selected.length) {
        notify("Select at least one listed ingredient first.", "info");
        return;
    }
    let completed = 0;
    for (const item of selected) {
        if (ingredientAiDrafts.some((draft) => draft.item.id === item.id)) continue;
        try {
            const answer = await askCuratorAi(item.techName || item.name);
            if (!answer.found || !answer.draft || !answer.draft.description) throw new Error(answer.message || "No sourced description was found.");
            ingredientAiDrafts.push({ item, description: answer.draft.description, sources: answer.sources || [], model: answer.model, searched: answer.searched });
            completed++;
        } catch (error) {
            const reason = error && error.name === "AbortError" ? "The AI took too long." : describeError(error);
            notify(`AI could not assist "${titleOf(item)}". ${reason}`, "error");
        }
    }
    paintIngredientDrafts();
    if (completed) notify(`${many(completed, "draft is", "drafts are")} ready. Check the sources and select the drafts you reviewed.`, "ok");
}

async function approveIngredientDrafts() {
    const checked = Array.from(el("ingredientAiDraftList").querySelectorAll("[data-draft-reviewed]:checked"))
        .map((checkbox) => Number(checkbox.closest("[data-draft-index]").dataset.draftIndex))
        .filter((index) => Number.isInteger(index) && ingredientAiDrafts[index]);
    if (!checked.length) return;
    const drafts = checked.map((index) => ({
        index,
        entry: ingredientAiDrafts[index],
        description: el("ingredientAiDraftList").querySelector(`[data-draft-index="${index}"] [data-draft-description]`).value.trim()
    })).filter((draft) => draft.description);
    if (!drafts.length) {
        notify("Enter a description in at least one checked draft before approving.", "error");
        return;
    }
    if (!(await askConfirm({
        title: `Approve ${many(drafts.length, "reviewed description", "reviewed descriptions")}?`,
        message: "Only checked descriptions are saved. Ingredient names and all risk or safety fields remain unchanged.",
        items: drafts.map((draft) => titleOf(draft.entry.item)),
        confirmLabel: "Approve All"
    }))) return;
    const ops = drafts.map(({ entry, description }) => {
        const sources = entry.sources.map((source) => `${source.title || source.site || "web page"} | ${source.url}`).filter((source) => !source.endsWith(" | "));
        return {
            ref: doc(db, "ingredients", entry.item.id),
            data: {
                description,
                source: entry.item.source || "aiassist",
                sources: Array.from(new Set(entry.item.sources.concat(sources))),
                webSearched: sources.length > 0,
                aiAssistModel: entry.model || "",
                reviewed: true,
                reviewedBy: reviewer(),
                reviewedAt: serverTimestamp(),
                updatedAt: serverTimestamp()
            }
        };
    });
    try {
        await saveOrReload(ops);
        ingredientAiDrafts = ingredientAiDrafts.filter((entry) => !drafts.some((draft) => draft.entry === entry));
        paintIngredientDrafts();
        notify(`${many(drafts.length, "reviewed description was", "reviewed descriptions were")} saved.`, "ok");
    } catch (error) {
        notify("The reviewed descriptions were not saved. " + describeError(error), "error");
    }
}

async function approveSelectedIngredients(keys) {
    const waiting = keys.map((id) => everyIngredient.find((item) => item.id === id && !item.binned && needsReview(item))).filter((item) => item);
    // a "harmful" rating without a regulator's page is not approved in bulk (docs/DATA_SOURCE_POLICY.md); it is edited one by one
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

// ---------------------------------------------------------------------------------------------
// Folders and the Recycle bin (what adminOrganizer.js asks this page to save)
// ---------------------------------------------------------------------------------------------
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
    list.forEach((item) => { item.folder = folder; });           // shown at once; the database follows
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

/** "Delete forever": asks with the list of names, then deletes the documents. Resolves true when it was done. */
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

// ---------------------------------------------------------------------------------------------
// Clean up, and Find duplicates (the lists come from ../_Shared/glowupCatalog.js, the dialogs from adminCleanup.js)
// ---------------------------------------------------------------------------------------------
/**
 * Writes changes as glowupCatalog.js describes them: { collection: "ingredients", docId, action: "bin" | "update", fields,
 * reason }. "bin" moves the entry to the Recycle bin and notes why. Nothing is deleted. The page then loads its data again.
 */
async function applyChanges(changes) {
    const ops = [];
    changes.forEach((change) => {
        const item = everyIngredient.find((x) => x.id === change.docId);
        if (!item || change.collection !== "ingredients") return;
        if (change.action === "bin") { if (!item.binned) ops.push({ ref: refOf(item), data: { binned: true, binnedAt: serverTimestamp(), binnedReason: String(change.reason || "").slice(0, 200), updatedAt: serverTimestamp() } }); }
        else if (change.action === "update" && change.fields) ops.push({ ref: refOf(item), data: Object.assign({}, change.fields, { updatedAt: serverTimestamp() }) });
    });
    if (ops.length === 0) return 0;
    await saveOrReload(ops);                 // after a failure the page shows what is really there, and the failure is passed on
    await loadIngredients();
    return ops.length;
}

// "Clean up" and "Find duplicates" compare every ingredient with every other one. That runs in THIS page (nothing is sent to a server), so it is
// cut into slices (ingredientLookalikesAsync): a window with a bar shows at once, the page stays alive, and a second click while a scan runs is
// refused. When the scan ends, or fails, or is cancelled, the guard is lifted again.
let scanning = false;
// ids of entries the admin said are DIFFERENT ingredients from the one in the form (kept on the saved entry; the duplicate tools then leave the pair alone)
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
    if (groups === null) return;                                  // cancelled
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
            // the slow part (names that are nearly the same) is awaited in slices; the dialog shows its own bar and drops an older search
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
            // an entry that product lists still use must not simply disappear: its names have to move to the one that is kept
            whyNotRemove: (item) => (productUseKnown && usedInProducts(item) > 0 ? `${many(usedInProducts(item), "listed product uses", "listed products use")} "${titleOf(item)}".` : "")
        }]
    });
}

// ---------------------------------------------------------------------------------------------
// Add / edit
// ---------------------------------------------------------------------------------------------
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
    // one choice per skin type and per condition, so a name can never be both "avoid" and "good"
    const types = readFit(el("skinTypesFit")), conditions = readFit(el("conditionsFit"));
    const avoidTypes = types.avoid, goodTypes = types.good, avoidConditions = conditions.avoid, goodConditions = conditions.good;

    if (!name) { setFormError(form, "Type the INCI name."); el("ingredientNameInput").focus(); return; }
    // "Aqua / Water" in the one name box: the box holds ONLY the INCI name. Split it, show the result and ask for one more Save, so nothing is
    // saved that the admin has not seen.
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
    // a NEW ingredient needs a classification and a purpose. Whether an EXISTING one that has none yet may be saved without is decided for each
    // box on its own in classificationRules.js (CLASSIFICATION_REQUIRED_ON_EXISTING, PURPOSE_REQUIRED_ON_EXISTING); one that has them cannot lose them.
    const existing = editingIngredientId ? everyIngredient.find((i) => i.id === editingIngredientId) : null;
    if ((!classificationId || classificationId === ADD_CLASS) && (!existing || CLASSIFICATION_REQUIRED_ON_EXISTING || !lacksClass(existing))) { setFormError(form, "Choose the ingredient classification."); el("ingredientClassInput").focus(); return; }
    if (!purpose && (!existing || PURPOSE_REQUIRED_ON_EXISTING || existing.purpose)) { setFormError(form, "Write one line on what this ingredient does in a product."); el("ingredientPurposeInput").focus(); return; }
    if (!description) { setFormError(form, "Write a short user-friendly definition."); el("ingredientDescInput").focus(); return; }
    // reference links: one per line, "Title | https://..." or just the address; only https is kept
    const sourceLines = el("ingredientSourcesInput").value.split(/\r?\n/).map((line) => line.replace(/\s+/g, " ").trim()).filter(Boolean);
    const badLine = sourceLines.find((line) => { try { return new URL((line.split("|").pop() || "").trim()).protocol !== "https:"; } catch (error) { return true; } });
    if (badLine) { setFormError(form, "Each reference link must contain a web address that starts with https://  (" + badLine.slice(0, 50) + ")"); el("ingredientSourcesInput").focus(); return; }
    const badSource = sourceUi ? sourceUi.firstError() : null;
    if (badSource) { setFormError(form, badSource.message); badSource.input.focus(); return; }
    if (minAgeText !== "" && (!/^\d+$/.test(minAgeText) || minAge > 99)) { setFormError(form, "Minimum age must be a whole number from 0 to 99."); el("ingredientMinAgeInput").focus(); return; }

    // the same ingredient must not be in the list twice (any of its names matching any name of another entry)
    const myKeys = new Set([name, ...aliases].map(sameKey));
    // hard stop: the same INCI name, or an entry in the Recycle bin that uses a name. A name shared only through Also known as can be a different
    // ingredient: the admin may say so (nameClashes / the button under the name box).
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
    // an admin who opens an entry that was not typed here and saves it has checked it
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
    // a draft the AI wrote and the admin has now read and saved: the pages it came from are kept with the entry
    if (assisted && !editingIngredientId)
        Object.assign(data, { source: "aiassist", reviewed: true, reviewedBy: reviewer(), reviewedAt: serverTimestamp(), webSearched: assisted.sources.length > 0, model: assisted.model,
            sources: Array.from(new Set(assisted.sources.map((p) => `${p.title || p.site || "web page"} | ${p.url}`).concat(sourceLines))).slice(0, 10) });

    // "Harmful" must be backed by a regulator's page (docs/DATA_SOURCE_POLICY.md). An entry that was already harmful can still be saved
    // (its other fields); it is listed under "Harmful without a regulator's page" until a page is added.
    const unbacked = data.riskLevel === "harmful" && !(before && before.riskLevel === "harmful") ? evidenceProblem("harmful", data.sources) : "";
    if (unbacked) { setFormError(form, unbacked); el("ingredientSourcesInput").focus(); return; }

    setBusy(saveButton, true, "Saving...");
    try {
        if (editingIngredientId) {
            await updateDoc(doc(db, "ingredients", editingIngredientId), { ...data, techName: deleteField() });           // its folder is left as it is; the old INCI field is now one of its aliases
            notify(nowReviewed ? "Ingredient saved and marked as reviewed." : "Ingredient saved.", "ok");
        } else {
            const folder = org.view.kind === "folder" ? org.view.name : "";               // added while a folder is open: it goes into that folder
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

// ---------------------------------------------------------------------------------------------
// Clicks on the cards
// ---------------------------------------------------------------------------------------------
function onListClick(event) {
    const button = event.target.closest("button");
    if (!button) {
        // a click on the card itself shows (or hides) the rest of it
        if (event.target.closest("a, .detail-box")) return;
        const card = event.target.closest(".ingredient-card");
        if (card) toggleDetails(card.dataset.id);
        return;
    }
    const act = button.dataset.act, id = button.dataset.id || "";
    if (act === "write" || act === "assist") {
        openIngredientForm(null);
        el("ingredientNameInput").value = button.dataset.name || "";
        el("ingredientDescInput").focus();
        if (act === "assist") assistWithAi(button.dataset.name || "");
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

/** Marks an AI-found entry as checked, unchanged. To correct it first, use Edit: saving there marks it reviewed too. */
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

/** Takes the unreadable "Used in" text (written by an older build of the app) off every entry that has it. */
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

// ---------------------------------------------------------------------------------------------
// Helpers: the line in each panel that says what needs attention (see adminAssist.js)
// ---------------------------------------------------------------------------------------------
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

// ---------------------------------------------------------------------------------------------
// The data this page offers to data panels (lists, rows, grids and graphs; see adminWidgets.js)
// ---------------------------------------------------------------------------------------------
function registerData() {
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
            { id: "avoid", label: "Skin condition to avoid it", of: (i) => i.avoidForConditions.map((t) => labelOf(allOptions("conditions"), t)), none: "None" },
            { id: "skin", label: "Skin type to avoid it", of: (i) => i.avoidForSkinTypes.map((t) => labelOf(SKIN_TYPES, t)), none: "None" },
            { id: "used", label: "Used in a listed product", of: (i) => (usedInProducts(i) > 0 ? "In a listed product" : "Not in any product"), keys: ["In a listed product", "Not in any product"] },
            { id: "folder", label: "Folder", of: (i) => i.folder || null, none: "No folder", pick: (k) => org.show(k === "No folder" ? { kind: "all" } : { kind: "folder", name: k }) }
        ],
        open: (i) => openIngredientForm(i),
        // a tile that is removed goes to the Recycle bin (it can be restored there)
        bin: (rows) => org.toBin(rows.map((i) => i.id))
    });
}
