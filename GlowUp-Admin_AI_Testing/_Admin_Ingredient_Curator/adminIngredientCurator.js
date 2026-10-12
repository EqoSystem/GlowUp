// Ingredient Curator: the 'ingredients' collection the mobile app reads when it analyses a scan.
// An ingredient document:
//   name, techName, description                       - what app users read (always filled in here)
//   aliases[]                                          - other spellings found on labels
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
import { collection, doc, serverTimestamp } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
// reading and writing go through the site's own copy of its lists: a list is downloaded once and kept between pages,
// and after a change only the documents that were written are read again (see adminStore.js)
import { getDocs, addDoc, updateDoc, writeBatch, onListsRefreshed } from "../_Shared/adminStore.js";
import {
    requireAdmin, wireSignOut, setupNavigationGuard, setupModal, escapeHtml, sameKey, toList, asArray, toWholeNumber, toDate, timeAgo,
    notify, describeError, setBusy, setFormError, renderChecks, readChecks, writeChecks, originLabel, sourceLinks, wireSearch,
    ALLERGEN_FAMILIES, SKIN_TYPES, SKIN_CONDITIONS, dialogIsOpen, statusBadge, toneBadge
} from "../_Shared/adminCommon.js";
import { matchesSearch } from "../_Shared/glowupRules.js";
import { cleanupPlan, ingredientDuplicates, ingredientLookalikes, ingredientIssues, mergeIngredients } from "../_Shared/glowupCatalog.js";
import { openPlanDialog, openDuplicatesDialog } from "../_Shared/adminCleanup.js";
import { mountShell, paginate, renderPager, icon, BIN_ICON, openMenu, askConfirm } from "../_Shared/adminShell.js";
import { registerDataset, datasetChanged } from "../_Shared/adminWidgets.js";
import { createOrganizer, cleanFolder, friendly } from "../_Shared/adminOrganizer.js";
import { setAssistPage, registerAssist, refreshAssist } from "../_Shared/adminAssist.js";

const pageState = { page: 1, size: 10 };          // the list shows ten ingredients at a time
const BATCH_LIMIT = 400;                          // Firestore allows 500 writes in one batch
const VIEW_KEY = "glowup.curator.view.v1";
// true  = an ingredient the app's AI lookup found is approved by itself when it comes with a CREDIBLE SOURCE (see hasCredibleSource):
//         nobody has to press Approve. It is marked "approved automatically", so it can still be found and corrected
//         (filter "AI-found (all)"). An entry without such a source keeps waiting for an admin's review.
// false = every such entry waits for review, and until then the app never counts it as proof that an ingredient is safe.
const AUTO_APPROVE_AI = true;
/**
 * Credible = the name is listed in the US FDA substance registry (it has a UNII code), or the AI really searched the web
 * and gave the pages it used. Pages the AI only named from memory do not count.
 */
const hasCredibleSource = (item) => !!item.fdaUnii || (item.webSearched && item.sources.some((s) => /https?:\/\//i.test(s)));

let everyIngredient = [];             // every document, also the ones in the Recycle bin
let ingredients = [];                 // listed: [{ id, name, techName, description, ... }]
let binIngredients = [];              // in the Recycle bin
let productUse = new Map();           // ingredient key -> number of catalogue products that list it
let productUseKnown = false;
let editingIngredientId = null;
let modal = null;
let fromProducts = [];                // ingredient names the inventory's products list that the Curator has no entry for: [{ key, name, products[] }]
let fdaBrands = [];                   // the brands whose products were checked on the FDA Philippines list (for the helper line)
let org = null;                       // folders, the Recycle bin, picking and dragging
let openId = "";                      // the card whose details are open
let quick = "";                       // a filter a helper switched on: "" | a key of QUICK
const view = { sort: "name" };        // the order; remembered in this browser

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

// the "show" box next to the search box
const FILTERS = [
    { value: "", label: "All ingredients", test: () => true },
    { value: "review", label: "AI-found, waiting for review", test: needsReview },
    { value: "ai", label: "AI-found (all)", test: (item) => item.source === "ai" },
    { value: "starter", label: "Starter data", test: (item) => item.source === "starter" || item.source === "fda" },
    { value: "admin", label: "Typed on this website", test: (item) => item.source === "" || item.source === "admin" }
];
/** The choice that shows what the inventory's products list and the Curator does not explain yet. */
const FROM_PRODUCTS = "products";

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
    unused: { label: "Not in any listed product", test: (item) => productUseKnown && usedInProducts(item) === 0 }
};

document.addEventListener("DOMContentLoaded", async () => {
    try { const saved = JSON.parse(localStorage.getItem(VIEW_KEY) || "{}"); if (SORTS.some((s) => s.id === saved.sort)) view.sort = saved.sort; } catch (error) { /* first visit */ }
    registerData();                   // before the page frame: a saved layout may hold panels that show this data
    mountShell("curator");
    wireSignOut();
    setupNavigationGuard();
    await requireAdmin();

    renderChecks(el("allergenTagsChecks"), "allergenTags", ALLERGEN_FAMILIES);
    renderFit(el("skinTypesFit"), "skinType", SKIN_TYPES);
    renderFit(el("conditionsFit"), "condition", SKIN_CONDITIONS);

    modal = setupModal(el("addIngredientModal"), () => { editingIngredientId = null; });

    org = createOrganizer({
        kind: "ingredients", noun: "ingredients", nounOne: "ingredient", allLabel: "All",
        panel: el("ingredientsList").closest(".panel"), host: el("ingredientsList"), shelf: el("ingredientShelf"), bar: el("ingredientBar"),
        folderNames: () => everyIngredient.map((item) => item.folder).filter((name) => name),
        folderOf: (key) => { const item = everyIngredient.find((x) => x.id === key); return item ? item.folder : ""; },
        titleOf: (key) => { const item = everyIngredient.find((x) => x.id === key); return item ? titleOf(item) : ""; },
        count: (v) => (v.kind === "bin" ? binIngredients.length : ingredients.filter((item) => v.kind !== "folder" || sameKey(item.folder) === sameKey(v.name)).length),
        binKeys: () => binIngredients.map((item) => item.id),
        move: moveToFolder, bin: moveToBin, restore: restoreFromBin, purge: purgeFromBin,
        renameFolder: (from, to) => refolder(from, to), deleteFolder: (name) => refolder(name, ""),
        onView: () => { fromFirstPage(); }
    });
    setAssistPage("curator");
    registerHelpers();

    wireSearch(el("ingredientSearchInput"), el("ingredientSearchClear"), fromFirstPage);
    el("moreMenuBtn").addEventListener("click", (event) => openMenu(event.currentTarget, [
        { label: "Clean up", icon: "sparkle", description: "Entries that are notes, joined names or label text, and entries that are there twice. You see every change first.", run: openCleanUp },
        { label: "Find duplicates", icon: "layers", description: "Entries that explain the same ingredient, or have nearly the same name. You choose which one to keep.", run: openDuplicates }
    ]));
    el("ingredientSourceFilter").addEventListener("change", fromFirstPage);
    el("ingredientRiskFilter").addEventListener("change", fromFirstPage);
    el("curatorStats").addEventListener("click", (event) => {
        const tile = event.target.closest(".stat");
        if (!tile) return;
        narrow(tile.dataset.source || "", tile.dataset.risk || "");
    });
    el("sortMenuBtn").addEventListener("click", (event) => openMenu(event.currentTarget, [{ heading: "Order of the list" }].concat(
        SORTS.map((s) => ({ label: s.label, checked: s.id === view.sort, run: () => { view.sort = s.id; try { localStorage.setItem(VIEW_KEY, JSON.stringify(view)); } catch (error) { /* private window */ } fromFirstPage(); } })))));
    el("ingredientQuick").addEventListener("click", () => { quick = ""; fromFirstPage(); });
    el("addIngredientBtn").addEventListener("click", () => openIngredientForm(null));
    el("addIngredientForm").addEventListener("submit", saveIngredient);
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

    loadIngredients();
});

function fromFirstPage() { pageState.page = 1; renderIngredients(); }
/** Narrows the list to one origin and one risk level (a count tile, a graph or a helper does this). */
function narrow(source, risk) {
    el("ingredientSearchInput").value = "";
    el("ingredientSourceFilter").value = source;
    el("ingredientRiskFilter").value = risk;
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
                description: data.description || "",
                aliases: asArray(data.aliases),
                riskLevel: RISK_LEVELS.includes(risk) ? risk : "",
                cautionNote: data.cautionNote || "",
                commonUses: brokenText(uses) ? "" : uses,
                usesBroken: brokenText(uses),
                minAge: toWholeNumber(data.minAge),
                keyActive: data.keyActive === true,
                umbrella: data.umbrella === true,
                allergenTags: asArray(data.allergenTags),
                avoidForSkinTypes: asArray(data.avoidForSkinTypes),
                goodForSkinTypes: asArray(data.goodForSkinTypes),
                avoidForConditions: asArray(data.avoidForConditions),
                goodForConditions: asArray(data.goodForConditions),
                source: String(data.source || "").trim().toLowerCase(),
                reviewed: data.reviewed === true,
                sources: Array.isArray(data.sources) ? data.sources.map((s) => String(s)) : [],
                fdaUnii: String(data.fdaUnii || "").trim(),
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
    if (AUTO_APPROVE_AI) {
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
    if (org) org.sync(everyIngredient.map((item) => item.id));
    fillSourceFilter();
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

/** Every way this ingredient can be written on a label. */
function keysOf(item) {
    return [item.name, item.techName, ...item.aliases].map(sameKey).filter((k) => k.length > 0);
}

function usedInProducts(item) {
    let total = 0;
    new Set(keysOf(item)).forEach((key) => { total += productUse.get(key) || 0; });
    return total;
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
    el("curatorStats").innerHTML = tiles.map((t) => `
        <button type="button" class="stat${!inBin && t.source === source && t.risk === risk && (source || risk) ? " on" : ""}" data-source="${t.source}" data-risk="${t.risk}" data-tip-title="${escapeHtml(t.label)}" data-tip="${escapeHtml(t.tip)}" data-tip-preview="Click: the list shows only these.">
            <span class="stat-icon">${icon(t.iconName)}</span><span><span class="stat-value">${t.value}</span><span class="stat-title">${escapeHtml(t.label)}</span></span>
        </button>`).join("");
}

function renderIngredients() {
    const container = el("ingredientsList");
    const typed = el("ingredientSearchInput").value, term = sameKey(typed);
    const filter = FILTERS.find((f) => f.value === el("ingredientSourceFilter").value) || FILTERS[0];
    const risk = el("ingredientRiskFilter").value;
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
            && (!quick || QUICK[quick].test(item)) && matches(item)).sort(sortNow().cmp));

    el("ingredientCountLine").textContent = inBin ? (pool.length === 0 ? "" : `${many(shown.length, "entry", "entries")} · left out by the app`)
        : everyIngredient.length === 0 ? "" : shown.length === ingredients.length ? `${ingredients.length} in all` : `${shown.length} of ${ingredients.length}${folder ? ` · in "${folder}"` : ""}`;
    renderPager(el("ingredientPager"), pageState, shown.length, renderIngredients, "ingredients");
    renderPager(el("ingredientPagerTop"), pageState, shown.length, renderIngredients, "ingredients");

    if (shown.length === 0) {
        const message = everyIngredient.length === 0 ? "No ingredients yet."
            : inBin ? (term ? `Nothing in the Recycle bin matches "${escapeHtml(typed)}".` : "The Recycle bin is empty.")
            : term ? `No ingredient matches "${escapeHtml(typed)}"${filter.value || risk || folder || quick ? " in this view" : ""}.`
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
            <div class="card-sub"><p class="ingredient-tech">${escapeHtml(item.techName || "No scientific name")}</p>${review ? statusBadge("Pending") : ""}${toneBadge(RISK_NAMES[item.riskLevel || "auto"], toneOfRisk(item.riskLevel), riskText)}</div>
            ${open ? detailOf(item, inBin) : ""}
        </article>`;
}

/** Everything else about an ingredient: shown inside its card when the card is clicked. */
function detailOf(item, inBin) {
    const review = needsReview(item);
    const origin = originLabel(item.source);
    const facts = [];
    // the risk is on the card's own badge (with what it means in its tooltip): it is not said a second time here
    facts.push(["Definition", escapeHtml(item.description || "No description provided.")]);
    if (item.aliases.length > 0) facts.push(["Also known as", escapeHtml(item.aliases.join(", "))]);
    if (item.commonUses) facts.push(["Used in", escapeHtml(item.commonUses)]);
    else if (item.usesBroken) facts.push(["Used in", `<span class="chip warn" data-tip="An older build of the app wrote text here that is not a description. It is not shown to anyone. Edit and save to replace it.">text from the app could not be read</span>`]);
    if (item.cautionNote) facts.push(["Caution", "It " + escapeHtml(item.cautionNote)]);

    const chips = [];
    if (item.keyActive) chips.push(`<span class="chip pink">Key active</span>`);
    if (item.umbrella) chips.push(`<span class="chip warn">Umbrella term</span>`);
    if (item.minAge > 0) chips.push(`<span class="chip warn">Age ${item.minAge}+</span>`);
    item.allergenTags.forEach((t) => chips.push(`<span class="chip warn">Allergen: ${escapeHtml(labelOf(ALLERGEN_FAMILIES, t))}</span>`));
    item.avoidForSkinTypes.forEach((t) => chips.push(`<span class="chip warn">Avoid: ${escapeHtml(labelOf(SKIN_TYPES, t))} skin</span>`));
    item.avoidForConditions.forEach((t) => chips.push(`<span class="chip warn">Avoid: ${escapeHtml(labelOf(SKIN_CONDITIONS, t))}</span>`));
    item.goodForSkinTypes.forEach((t) => chips.push(`<span class="chip good">Good: ${escapeHtml(labelOf(SKIN_TYPES, t))} skin</span>`));
    item.goodForConditions.forEach((t) => chips.push(`<span class="chip good">Good: ${escapeHtml(labelOf(SKIN_CONDITIONS, t))}</span>`));
    if (chips.length > 0) facts.push(["For the app", chips.join("")]);

    if (productUseKnown) { const used = usedInProducts(item); facts.push(["In products", used === 0 ? "Not in any listed product yet" : many(used, "listed product", "listed products")]); }
    if (item.folder) facts.push(["Folder", escapeHtml(item.folder)]);
    if (origin) facts.push(["Added", escapeHtml(origin) + (review ? ` <span class="chip review" data-tip="Until you approve it, the app uses this entry's warnings but never counts it as proof that the ingredient is safe.">needs review</span>` : item.autoApproved ? ` <span class="chip" data-tip="Approved by itself because it came with a credible source (the US FDA substance registry, or pages from a web search). Press Edit to correct it.">approved automatically</span>` : item.reviewed ? " · reviewed" : "")]);
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
        const choice = (value, label, cls, tip) => `<label class="fit-choice ${cls}" title="${tip}"><input type="radio" name="${name}" value="${value}"${value === "" ? " checked" : ""}><span>${label}</span></label>`;
        return `<div class="fit-row" data-value="${escapeHtml(o.value)}"><span class="fit-name">${escapeHtml(o.label)}</span><span class="fit-choices">`
            + choice("avoid", "Avoid", "avoid", "The app warns users with this") + choice("", "&ndash;", "none", "No effect either way") + choice("good", "Good", "good", "The app counts it as a plus for users with this")
            + `</span></div>`;
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

function openCleanUp() {
    openPlanDialog({
        title: "Clean up the ingredients",
        intro: "Entries that are not ingredients (notes, two names joined into one, label text), and entries that explain an ingredient that has an entry already. Open the step to see every entry, take the tick off what should stay, and apply it. Nothing is deleted: what is removed goes to the Recycle bin.",
        makePlan: () => cleanupPlan([], [], everyIngredient, { only: ["ingredients"] }),
        apply: (changes) => applyChanges(changes),
        notes: () => [{
            title: "Ingredient names that are nearly the same", text: "Chemical names can differ by one letter and still be different ingredients (methylparaben, ethylparaben), so these are only listed.",
            items: ingredientLookalikes(everyIngredient, 0.93).map((group) => group.items.map(titleOf).join("  /  ")),
            action: { label: "Open Find duplicates", run: openDuplicates }
        }]
    });
}

function openDuplicates() {
    const origin = (item) => originLabel(item.source) || "Typed on this website";
    openDuplicatesDialog({
        title: "Find duplicate ingredients",
        intro: "Entries that explain the same ingredient, or have nearly the same name. Most often the app's AI lookup made a new entry although one was there already. Choose which entry to keep; merging gives it the other names, so every product list is still explained. Chemical names can differ by one letter and be different ingredients: look before you merge.",
        threshold: 0.93,
        sections: [{
            id: "ingredients", label: "Ingredients",
            find: (threshold) => ({ sure: ingredientDuplicates(everyIngredient), possible: ingredientLookalikes(everyIngredient, threshold) }),
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
    editingIngredientId = item ? item.id : null;
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
    el("ingredientTechInput").value = item ? item.techName : "";
    el("ingredientDescInput").value = item ? item.description : "";
    el("ingredientAliasesInput").value = item ? item.aliases.join(", ") : "";
    el("ingredientRiskInput").value = item ? item.riskLevel : "";
    el("ingredientMinAgeInput").value = item && item.minAge > 0 ? item.minAge : "";
    el("ingredientCautionInput").value = item ? item.cautionNote : "";
    el("ingredientUsesInput").value = item ? item.commonUses : "";
    el("ingredientKeyActiveInput").checked = item ? item.keyActive : false;
    el("ingredientUmbrellaInput").checked = item ? item.umbrella : false;
    writeChecks(el("allergenTagsChecks"), item ? item.allergenTags : []);
    writeFit(el("skinTypesFit"), item ? item.avoidForSkinTypes : [], item ? item.goodForSkinTypes : []);
    writeFit(el("conditionsFit"), item ? item.avoidForConditions : [], item ? item.goodForConditions : []);
    modal.open();
}

async function saveIngredient(event) {
    event.preventDefault();
    const form = el("addIngredientForm");
    const saveButton = el("saveIngredientBtn");
    if (saveButton.disabled) return;

    const clean = (id) => el(id).value.replace(/\s+/g, " ").trim();
    const name = clean("ingredientNameInput");
    const techName = clean("ingredientTechInput");
    const description = el("ingredientDescInput").value.trim();
    const aliases = toList(el("ingredientAliasesInput").value).filter((a) => sameKey(a) !== sameKey(name) && sameKey(a) !== sameKey(techName));
    const minAgeText = el("ingredientMinAgeInput").value.trim();
    const minAge = toWholeNumber(minAgeText);
    // one choice per skin type and per condition, so a name can never be both "avoid" and "good"
    const types = readFit(el("skinTypesFit")), conditions = readFit(el("conditionsFit"));
    const avoidTypes = types.avoid, goodTypes = types.good, avoidConditions = conditions.avoid, goodConditions = conditions.good;

    if (!name) { setFormError(form, "Type the ingredient name."); el("ingredientNameInput").focus(); return; }
    if (!techName) { setFormError(form, "Type the technical / scientific name (the name printed on labels)."); el("ingredientTechInput").focus(); return; }
    if (!description) { setFormError(form, "Write a short user-friendly definition."); el("ingredientDescInput").focus(); return; }
    if (minAgeText !== "" && (!/^\d+$/.test(minAgeText) || minAge > 99)) { setFormError(form, "Minimum age must be a whole number from 0 to 99."); el("ingredientMinAgeInput").focus(); return; }

    // the same ingredient must not be in the list twice (any of its names matching any name of another entry)
    const myKeys = new Set([name, techName, ...aliases].map(sameKey));
    const clash = everyIngredient.find((other) => other.id !== editingIngredientId && keysOf(other).some((k) => myKeys.has(k)));
    if (clash) { setFormError(form, clash.binned ? `"${titleOf(clash)}" uses one of these names and is in the Recycle bin. Restore it from there instead.` : `"${titleOf(clash)}" already uses one of these names. Edit that entry instead.`); return; }
    setFormError(form, "");

    const data = {
        name, techName, description, aliases,
        riskLevel: el("ingredientRiskInput").value,
        cautionNote: clean("ingredientCautionInput").replace(/^it\s+/i, "").replace(/\.$/, ""),
        commonUses: clean("ingredientUsesInput"),
        minAge,
        keyActive: el("ingredientKeyActiveInput").checked,
        umbrella: el("ingredientUmbrellaInput").checked,
        allergenTags: readChecks(el("allergenTagsChecks")),
        avoidForSkinTypes: avoidTypes,
        goodForSkinTypes: goodTypes,
        avoidForConditions: avoidConditions,
        goodForConditions: goodConditions,
        updatedAt: serverTimestamp()
    };
    // an admin who opens an entry that was not typed here and saves it has checked it
    const before = ingredients.find((i) => i.id === editingIngredientId);
    const nowReviewed = !!before && before.source !== "" && before.source !== "admin" && !before.reviewed;
    if (nowReviewed) { data.reviewed = true; data.reviewedAt = serverTimestamp(); }

    setBusy(saveButton, true, "Saving...");
    try {
        if (editingIngredientId) {
            await updateDoc(doc(db, "ingredients", editingIngredientId), data);           // its folder is left as it is
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
    if (act === "write") {
        openIngredientForm(null);
        el("ingredientNameInput").value = button.dataset.name || "";
        el("ingredientTechInput").value = button.dataset.name || "";
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

/** Marks an AI-found entry as checked, unchanged. To correct it first, use Edit: saving there marks it reviewed too. */
async function approveIngredient(item, button) {
    if (button.disabled) return;
    button.disabled = true;
    try {
        await updateDoc(doc(db, "ingredients", item.id), { reviewed: true, reviewedAt: serverTimestamp() });
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
    });

    registerAssist("tools", () => {
        if (!ready()) return [];
        const parts = [];
        const typed = el("ingredientSearchInput").value.trim(), source = FILTERS.find((f) => f.value === el("ingredientSourceFilter").value), risk = el("ingredientRiskFilter").value, now = org.view;
        if (typed) parts.push(`search "${typed}"`);
        if (source && source.value) parts.push(source.label.toLowerCase());
        if (risk) parts.push(RISK_NAMES[risk].toLowerCase());
        if (now.kind === "folder") parts.push(`folder "${now.name}"`);
        if (now.kind === "bin") parts.push("the Recycle bin");
        if (quick) parts.push(QUICK[quick].label.toLowerCase());
        if (parts.length > 0) return [{ tone: "info", text: `Showing only: ${parts.join(" · ")}.`, label: "Show everything", run: () => narrow("", "") }];
        return [
            { text: `Sorted by ${sortNow().short}. Search also finds other spellings and words in the definition.` },
            { text: "Drag a card onto a folder or the bin. Ctrl+click picks several cards; Shift+click picks a range." }
        ];
    });

    registerAssist("list", () => {
        if (!ready()) return [];
        if (org.view.kind === "bin") return [binIngredients.length > 0
            ? { tone: "info", text: `${many(binIngredients.length, "ingredient", "ingredients")} in the Recycle bin. The app leaves them out. Restore brings one back; Delete forever is final.` }
            : { tone: "ok", text: "The Recycle bin is empty." }];
        const lines = [];
        const count = (name) => ingredients.filter(QUICK[name].test).length;
        const broken = everyIngredient.filter((item) => item.usesBroken).length, review = ingredients.filter(needsReview).length, noNote = count("noNote"), unused = count("unused");
        if (broken > 0) lines.push({ tone: "warn", text: `${many(broken, "entry has", "entries have")} unreadable text in "Used in" (an older build of the app wrote it). It is hidden here.`, label: "Clear it", run: clearBrokenUses, tip: "You see the list and confirm first. Only that box is emptied." });
        const notReal = ingredientIssues(everyIngredient).length, twice = ingredientDuplicates(everyIngredient).length;
        if (notReal + twice > 0) lines.push({ tone: "warn", text: [notReal > 0 ? `${many(notReal, "entry is", "entries are")} not an ingredient (a note, joined names or label text)` : "", twice > 0 ? `${many(twice, "ingredient has", "ingredients have")} more than one entry` : ""].filter((t) => t).join("; ") + ".", label: "Clean up", run: openCleanUp, tip: "You see every entry with the reason and confirm first. Nothing is deleted." });
        if (review > 0) lines.push({ tone: "info", text: `${many(review, "AI-found entry waits", "AI-found entries wait")} for review.`, label: showLabel(reviewing()), run: () => narrow(reviewing() ? "" : "review", "") });
        if (noNote > 0) lines.push({ tone: "warn", text: `${many(noNote, "flagged ingredient has", "flagged ingredients have")} no caution note, so app users see the warning without a reason.`, label: showLabel(quick === "noNote"), run: () => setQuick("noNote") });
        if (unused > 0) lines.push({ text: `${many(unused, "ingredient is", "ingredients are")} not in any listed product yet.`, label: showLabel(quick === "unused"), run: () => setQuick("unused") });
        if (binIngredients.length > 0) lines.push({ text: `${many(binIngredients.length, "ingredient is", "ingredients are")} in the Recycle bin.`, label: "Open the bin", run: () => org.show({ kind: "bin" }) });
        if (fromProducts.length > 0) lines.push({ tone: "info", text: `${many(fromProducts.length, "ingredient that products in the inventory list has", "ingredients that products in the inventory list have")} no entry here yet. Most used: ${fromProducts.slice(0, 4).map((x) => x.name).join(", ")}.`, label: "Show them", run: () => narrow(FROM_PRODUCTS, "") });
        if (fdaBrands.length > 0) lines.push({ tone: "ok", text: `Brands checked on the FDA Philippines list (${fdaBrands.length}): ${fdaBrands.slice(0, 10).join(", ")}${fdaBrands.length > 10 ? ` and ${fdaBrands.length - 10} more` : ""}. The FDA list names products, not ingredients, so their ingredients arrive here when the products are scanned.`, label: "Open the inventory", run: () => { window.location.href = "../_Admin_Brand_Inventory/adminBrandInventory.html"; } });
        lines.push({ text: "A card shows the name and the scientific name. Click it for everything else; double-click to edit." });
        return lines;
    });
}

// ---------------------------------------------------------------------------------------------
// The data this page offers to data panels (lists, rows, grids and graphs; see adminWidgets.js)
// ---------------------------------------------------------------------------------------------
function registerData() {
    registerDataset("ingredients", {
        label: "Ingredients", noun: "ingredients", nounOne: "ingredient",
        items: () => ingredients,
        map: (i) => ({
            id: i.id, title: titleOf(i), subtitle: i.techName, meta: needsReview(i) ? "needs review" : RISK_NAMES[i.riskLevel || "auto"],
            tone: needsReview(i) ? "info" : toneOfRisk(i.riskLevel), value: productUseKnown ? usedInProducts(i) : undefined, valueLabel: "products"
        }),
        dimensions: [
            { id: "risk", label: "Risk level", of: (i) => i.riskLevel || "auto", keys: ["harmful", "caution", "safe", "auto"], labelOf: (k) => RISK_NAMES[k] || k, tone: toneOfRisk, pick: (k) => narrow("", k) },
            { id: "origin", label: "Where it came from", of: (i) => originLabel(i.source) || "Typed on this website" },
            { id: "review", label: "Checked or not", of: (i) => (needsReview(i) ? "Waiting for review" : "Checked"), keys: ["Waiting for review", "Checked"], tone: (k) => (k === "Checked" ? "ok" : "info"), pick: (k) => narrow(k === "Checked" ? "" : "review", "") },
            { id: "allergen", label: "Allergen family", of: (i) => i.allergenTags.map((t) => labelOf(ALLERGEN_FAMILIES, t)), none: "No allergen family" },
            { id: "avoid", label: "Skin condition to avoid it", of: (i) => i.avoidForConditions.map((t) => labelOf(SKIN_CONDITIONS, t)), none: "None" },
            { id: "skin", label: "Skin type to avoid it", of: (i) => i.avoidForSkinTypes.map((t) => labelOf(SKIN_TYPES, t)), none: "None" },
            { id: "used", label: "Used in a listed product", of: (i) => (usedInProducts(i) > 0 ? "In a listed product" : "Not in any product"), keys: ["In a listed product", "Not in any product"] },
            { id: "folder", label: "Folder", of: (i) => i.folder || null, none: "No folder", pick: (k) => org.show(k === "No folder" ? { kind: "all" } : { kind: "folder", name: k }) }
        ],
        open: (i) => openIngredientForm(i),
        // a tile that is removed goes to the Recycle bin (it can be restored there)
        bin: (rows) => org.toBin(rows.map((i) => i.id))
    });
}
