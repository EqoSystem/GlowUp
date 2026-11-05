// Recycle Bin: every ingredient, brand and product that was removed, in one place.
//
// Removing an entry on the other pages does not delete it: it gets binned == true (with binnedAt, and for brands and
// products statusBeforeBin, binnedWith and binnedReason). The app stops using it at once. This page lists those entries
// under three tabs and offers two things:
//   Restore             - the entry comes back with the status it had. A brand brings back the products that were
//                         removed WITH it; a product never comes back without its brand (the brand is restored too).
//   Delete permanently  - always asks first, with the names and what is lost. It cannot be undone, so a copy of what is
//                         deleted is saved to the admin's computer first (a JSON file in the Downloads folder).
//                         Before an ingredient is deleted the page says how many products still list it.
// Several entries can be selected and restored or deleted together ("Delete selected permanently" asks as well).
// Nothing here is new in the database: the page reads and writes the same fields the Inventory and the Curator use.
// Since 29 October 2026 a third action, Move to Archive, hands entries to the Archive (adminArchive.js): kept 30 days,
// then deleted by the server job. Restore and Delete permanently above work exactly as before.
import { db } from "../_Database/firebase-console.js";
import { collection, doc, serverTimestamp } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { getDocs, writeBatch, onListsRefreshed } from "../_Shared/adminStore.js";
import {
    requireAdmin, wireSignOut, setupNavigationGuard, escapeHtml, sameKey, asArray, toDate, timeAgo, formatWhen, notify, describeError,
    statusBadge, statusGuide, normalizeStatus, STATUSES, wireSearch, dialogIsOpen
} from "../_Shared/adminCommon.js";
import { matchesSearch } from "../_Shared/glowupRules.js";
import { moveToArchive, KEEP_DAYS } from "../_Shared/adminArchive.js";
import { mountShell, placeHeaderParts, paginate, renderPager, icon, BIN_ICON, askConfirm, hydrate, setBinCount, addRefreshDataButton } from "../_Shared/adminShell.js";

const el = (id) => document.getElementById(id);
const many = (n, one, more) => `${n} ${n === 1 ? one : more}`;
const BATCH_LIMIT = 400;               // Firestore takes at most 500 writes in one batch
const KINDS = {
    ingredients: { title: "Removed ingredients", one: "ingredient", more: "ingredients" },
    brands: { title: "Removed brands", one: "brand", more: "brands" },
    products: { title: "Removed products", one: "product", more: "products" }
};

const all = { ingredients: [], brands: [], products: [] };     // every entry of each list, removed or not
let kind = "ingredients";
let loaded = false;
const picked = new Set();              // ids picked on the tab that is shown
const pager = { page: 1, size: 10 };

document.addEventListener("DOMContentLoaded", async () => {
    mountShell("bin");
    // the one Refresh data button of the site goes in front of the Archive button, in the header (New8.pdf)
    addRefreshDataButton(placeHeaderParts());
    wireSignOut();
    setupNavigationGuard();
    el("binStatusGuide").innerHTML = statusGuide();
    try { const wanted = new URLSearchParams(location.search).get("show"); if (KINDS[wanted]) kind = wanted; } catch (error) { /* keep the first tab */ }
    paintTabs();
    await requireAdmin();

    el("binTabs").addEventListener("click", (event) => {
        const button = event.target.closest("[data-kind]");
        if (!button || button.dataset.kind === kind) return;
        kind = button.dataset.kind; picked.clear(); pager.page = 1;
        render();
    });
    wireSearch(el("binSearchInput"), el("binSearchClear"), () => { pager.page = 1; render(); });
    el("binPickAll").addEventListener("click", () => {
        const shown = shownEntries();
        const allPicked = shown.length > 0 && shown.every((x) => picked.has(x.id));
        shown.forEach((x) => { if (allPicked) picked.delete(x.id); else picked.add(x.id); });
        render();
    });
    el("binTableBody").addEventListener("click", onRowClick);
    el("restoreSelectedBtn").addEventListener("click", () => restore(binned().filter((x) => picked.has(x.id))));
    el("deleteSelectedBtn").addEventListener("click", () => purge(binned().filter((x) => picked.has(x.id))));
    el("archiveSelectedBtn").addEventListener("click", () => archive(binned().filter((x) => picked.has(x.id))));
    el("clearSelectedBtn").addEventListener("click", () => { picked.clear(); render(); });

    onListsRefreshed(() => { if (!dialogIsOpen()) load(); });
    load();
});

// ---------------------------------------------------------------------------------------------
// Loading
// ---------------------------------------------------------------------------------------------
async function load() {
    try {
        const [ingredients, brands, products] = await Promise.all([
            getDocs(collection(db, "ingredients")), getDocs(collection(db, "brands")), getDocs(collection(db, "products"))
        ]);
        const read = (snapshot) => snapshot.docs.map((d) => {
            const data = d.data(), since = toDate(data.binnedAt);
            return Object.assign({}, data, { id: d.id, binned: data.binned === true, binnedAt: since ? since.getTime() : 0, raw: data });
        });
        all.ingredients = read(ingredients); all.brands = read(brands); all.products = read(products);
        loaded = true;
        render();
    } catch (error) {
        console.error("Error loading the Recycle bin: ", error);
        el("binTableBody").innerHTML = `<tr><td colspan="6" class="table-message error">Could not load the Recycle bin. ${escapeHtml(describeError(error))} <button type="button" class="btn" id="binRetry">Try again</button></td></tr>`;
        el("binRetry").addEventListener("click", load);
    }
}

// ---------------------------------------------------------------------------------------------
// What is shown
// ---------------------------------------------------------------------------------------------
const binned = (of = kind) => all[of].filter((x) => x.binned);
const brandOf = (product) => all.brands.find((b) => b.id === product.brandId) || null;
const nameOf = (x, of = kind) => String(x.name || (of === "products" ? x.productName : "") || `Unnamed ${KINDS[of].one}`);
/** The products that went to the bin together with a brand. */
const ridersOf = (brand) => all.products.filter((p) => p.binned && p.binnedWith === brand.id);

function shownEntries() {
    const typed = el("binSearchInput").value;
    return binned()
        .filter((x) => matchesSearch(typed, [nameOf(x), x.techName, x.brandName, x.cosmeticType, x.binnedReason].concat(asArray(x.aliases))))
        .sort((a, b) => (b.binnedAt || 0) - (a.binnedAt || 0) || nameOf(a).localeCompare(nameOf(b)));        // the latest removal first
}

/** Why an entry is in the bin, in words (what was written down when it was removed). */
function whyOf(x) {
    if (x.binnedReason) return String(x.binnedReason);
    if (kind === "products" && x.binnedWith) { const brand = all.brands.find((b) => b.id === x.binnedWith); return `Removed together with its brand${brand ? " " + nameOf(brand, "brands") : ""}.`; }
    return "";
}

function paintTabs() {
    document.querySelectorAll("#binTabs .seg").forEach((button) => {
        const on = button.dataset.kind === kind;
        button.classList.toggle("on", on);
        button.setAttribute("aria-selected", on ? "true" : "false");
    });
    el("binTitle").textContent = KINDS[kind].title;
}

function render() {
    paintTabs();
    if (!loaded) return;
    el("countIngredients").textContent = binned("ingredients").length || "";
    el("countBrands").textContent = binned("brands").length || "";
    el("countProducts").textContent = binned("products").length || "";
    setBinCount();                         // the count on the logo's settings menu follows what this page shows

    const inBin = binned(), shown = shownEntries();
    Array.from(picked).forEach((id) => { if (!inBin.some((x) => x.id === id)) picked.delete(id); });
    const typed = el("binSearchInput").value.trim();
    el("binCountLine").textContent = typed ? `${shown.length} of ${many(inBin.length, KINDS[kind].one, KINDS[kind].more)}` : many(inBin.length, KINDS[kind].one, KINDS[kind].more);

    const empty = shown.length === 0;
    el("binEmpty").hidden = !empty;
    document.querySelector(".bin-table").hidden = empty;
    if (empty) {
        el("binEmptyMessage").textContent = inBin.length === 0 ? `No ${KINDS[kind].more} in the Recycle bin.` : `No removed ${KINDS[kind].one} fits "${typed}".`;
        el("binEmptyHint").textContent = inBin.length === 0 ? "What you remove on the other pages comes here, and can be brought back." : "Try fewer letters, or another word of the name.";
    }

    const page = paginate(shown, pager);
    el("binTableBody").innerHTML = page.map(rowOf).join("");
    hydrate(el("binTableBody"));
    const again = () => render();
    renderPager(el("binPagerTop"), pager, shown.length, again, KINDS[kind].more);
    renderPager(el("binPager"), pager, shown.length, again, KINDS[kind].more);

    const allPicked = shown.length > 0 && shown.every((x) => picked.has(x.id));
    el("binPickAll").setAttribute("aria-checked", allPicked ? "true" : picked.size > 0 ? "mixed" : "false");
    el("binPickAll").classList.toggle("on", allPicked);
    el("binBar").hidden = picked.size === 0;
    el("binBarCount").textContent = `${picked.size} selected`;
}

function rowOf(x) {
    const isPicked = picked.has(x.id);
    let sub = "";
    if (kind === "ingredients" && x.techName) sub = x.techName;
    if (kind === "products") sub = [x.brandName || (brandOf(x) ? nameOf(brandOf(x), "brands") : ""), x.cosmeticType].filter((part) => part).join(" · ");
    if (kind === "brands") { const riders = ridersOf(x).length; sub = riders > 0 ? `${many(riders, "product was", "products were")} removed with it` : ""; }
    const before = kind === "ingredients" ? `<span class="muted">–</span>` : statusBadge(STATUSES.includes(normalizeStatus(x.statusBeforeBin)) ? normalizeStatus(x.statusBeforeBin) : "Active");
    const why = whyOf(x);
    return `
        <tr class="${isPicked ? "is-picked" : ""}" data-id="${escapeHtml(x.id)}">
            <td class="pick-col"><button type="button" class="pick${isPicked ? " on" : ""}" role="checkbox" aria-checked="${isPicked}" data-act="pick" aria-label="Select ${escapeHtml(nameOf(x))}"></button></td>
            <td><span class="bin-name">${escapeHtml(nameOf(x))}</span>${sub ? `<span class="row-sub">${escapeHtml(sub)}</span>` : ""}</td>
            <td class="bin-when" title="${escapeHtml(x.binnedAt ? formatWhen(new Date(x.binnedAt)) : "")}">${x.binnedAt ? escapeHtml(timeAgo(new Date(x.binnedAt))) : `<span class="muted">not noted</span>`}</td>
            <td class="bin-why">${why ? escapeHtml(why) : `<span class="muted">–</span>`}</td>
            <td>${before}</td>
            <td><div class="row-actions">
                <button type="button" class="row-btn main" data-act="restore" data-tip="Brings it back with the status it had.">${icon("undo")}Restore</button>
                <button type="button" class="row-btn" data-act="archive" data-tip="Moves it to the Archive: kept ${KEEP_DAYS} days, then deleted. It can be restored until then.">${icon("archive")}Archive</button>
                <button type="button" class="row-btn danger" data-act="delete" data-tip="Takes it out of the database for good. It asks first, and saves a copy to your computer.">${BIN_ICON}Delete permanently</button>
            </div></td>
        </tr>`;
}

function onRowClick(event) {
    const button = event.target.closest("[data-act]"), row = event.target.closest("tr[data-id]");
    if (!button || !row) return;
    const entry = binned().find((x) => x.id === row.dataset.id);
    if (!entry) return;
    if (button.dataset.act === "pick") { if (picked.has(entry.id)) picked.delete(entry.id); else picked.add(entry.id); render(); }
    else if (button.dataset.act === "restore") restore([entry]);
    else if (button.dataset.act === "delete") purge([entry]);
    else if (button.dataset.act === "archive") archive([entry]);
}

/**
 * MOVE TO ARCHIVE. Asks first. A brand takes the products that are in the bin under it (as Delete permanently does);
 * each entry is checked again just before the move (adminArchive.js), and the move and the removal from here are one batch.
 */
async function archive(entries) {
    if (entries.length === 0) return;
    const moving = [], names = [], seen = new Set();
    const add = (x, listName, withBrand) => {
        const key = listName + "/" + x.id;
        if (seen.has(key)) return;
        seen.add(key);
        moving.push({ collection: listName, id: x.id, name: nameOf(x, listName), with: withBrand || "" });
    };
    entries.forEach((x) => {
        if (kind === "brands") {
            const riders = all.products.filter((p) => p.binned && p.brandId === x.id);
            riders.forEach((p) => add(p, "products", x.id));
            names.push(nameOf(x) + (riders.length > 0 ? ` (with ${many(riders.length, "product", "products")})` : ""));
        } else names.push((kind === "products" && x.brandName ? x.brandName + ": " : "") + nameOf(x));
    });
    entries.forEach((x) => add(x, kind));
    const sure = await askConfirm({
        title: `Move to the Archive: ${many(entries.length, KINDS[kind].one, KINDS[kind].more)}?`,
        message: `They leave the Recycle bin and are kept in the Archive for ${KEEP_DAYS} days, then deleted for good. Until then the Archive can bring them back.`,
        items: names, confirmLabel: "Move to Archive"
    });
    if (!sure) return;
    try {
        const done = await moveToArchive(moving);
        const skipped = done.skipped.length > 0 ? ` ${many(done.skipped.length, "entry was", "entries were")} no longer in the Recycle bin and stayed where they are.` : "";
        notify(`${many(done.moved, "entry", "entries")} moved to the Archive (kept ${KEEP_DAYS} days).${skipped}`, done.skipped.length > 0 ? "info" : "ok");
    } catch (error) {
        console.error("Error moving to the Archive: ", error);
        notify("Nothing was moved to the Archive. " + describeError(error), "error");
    }
    picked.clear();
    await load();
}

// ---------------------------------------------------------------------------------------------
// Writing
// ---------------------------------------------------------------------------------------------
/** Writes in batches; `ops` = [{ ref, data }] to change a document, [{ ref }] to delete it. Throws on the first batch that fails. */
async function runBatches(ops) {
    for (let i = 0; i < ops.length; i += BATCH_LIMIT) {
        const batch = writeBatch(db);
        ops.slice(i, i + BATCH_LIMIT).forEach((op) => { if (op.data) batch.update(op.ref, op.data); else batch.delete(op.ref); });
        await batch.commit();
    }
}

/**
 * RESTORE. Each entry gets back the status it had before it was removed. A brand brings back the products that were
 * removed with it (binnedWith); a product whose brand is still in the bin brings its brand back too, because the app
 * never shows a product without its brand.
 */
async function restore(entries) {
    if (entries.length === 0) return;
    const ops = [], seen = new Set();
    let riders = 0, brandsToo = 0;
    const statusOf = (x) => (STATUSES.includes(normalizeStatus(x.statusBeforeBin)) ? normalizeStatus(x.statusBeforeBin) : "Active");
    const back = (x, listName) => {
        if (!x.binned || seen.has(listName + "/" + x.id)) return false;
        seen.add(listName + "/" + x.id);
        const data = listName === "ingredients" ? { binned: false, binnedReason: "", updatedAt: serverTimestamp() }
            : { binned: false, status: statusOf(x), statusBeforeBin: "", binnedWith: "", binnedReason: "", updatedAt: serverTimestamp() };
        ops.push({ ref: doc(db, listName, x.id), data });
        return true;
    };
    entries.forEach((x) => {
        if (kind === "brands") ridersOf(x).forEach((p) => { if (back(p, "products")) riders++; });
        if (kind === "products") { const brand = brandOf(x); if (brand && brand.binned && back(brand, "brands")) brandsToo++; }
        back(x, kind);
    });
    if (ops.length === 0) return;
    const names = entries.length === 1 ? `"${nameOf(entries[0])}"` : many(entries.length, KINDS[kind].one, KINDS[kind].more);
    try {
        await runBatches(ops);
        const extra = riders > 0 ? ` with ${many(riders, "product", "products")}` : brandsToo > 0 ? ` (and ${many(brandsToo, "brand", "brands")}, because a product is never listed without its brand)` : "";
        const where = kind === "ingredients" ? "the Ingredient Curator" : "the Brand & Product Inventory";
        notify(`${names}${extra} restored: back in ${where}${kind === "ingredients" ? "" : " with the status it had"}.`, "ok");
    } catch (error) {
        console.error("Error restoring: ", error);
        notify("Not everything was restored. " + describeError(error), "error");
    }
    picked.clear();
    await load();
}

/** How many listed products still name this ingredient in their ingredient list. */
function productsUsing(ingredient) {
    const keys = new Set([ingredient.name, ingredient.techName].concat(asArray(ingredient.aliases)).map(sameKey).filter((key) => key));
    return all.products.filter((p) => !p.binned && asArray(p.ingredients).some((name) => keys.has(sameKey(name)))).length;
}

/** A copy of what is about to be deleted, saved to the admin's computer (a JSON file), so a mistake can still be put right by hand. */
function saveCopy(documents) {
    try {
        const stamp = new Date().toISOString().replace(/[:T]/g, "-").slice(0, 19);
        const blob = new Blob([JSON.stringify({ deletedAt: new Date().toISOString(), from: "GlowUp Recycle Bin", documents }, null, 2)], { type: "application/json" });
        const link = document.createElement("a");
        link.href = URL.createObjectURL(blob);
        link.download = `glowup-deleted-${stamp}.json`;
        document.body.appendChild(link); link.click(); link.remove();
        setTimeout(() => URL.revokeObjectURL(link.href), 4000);
        return true;
    } catch (error) { console.warn("The copy could not be saved: ", error); return false; }
}

/**
 * DELETE PERMANENTLY. Always asks, with the names and what is lost. A brand is deleted together with the products that
 * are in the bin under it. For ingredients the question says how many listed products still name them.
 */
async function purge(entries) {
    if (entries.length === 0) return;
    const refs = new Map(), copies = [], names = [];
    const take = (x, listName) => {
        const key = listName + "/" + x.id;
        if (refs.has(key)) return false;
        refs.set(key, doc(db, listName, x.id));
        copies.push({ collection: listName, id: x.id, data: x.raw });
        return true;
    };
    let lost = "", stillUsed = 0;
    entries.forEach((x) => {
        if (kind === "brands") {
            const riders = all.products.filter((p) => p.binned && p.brandId === x.id);
            riders.forEach((p) => take(p, "products"));
            names.push(nameOf(x) + (riders.length > 0 ? ` (with ${many(riders.length, "product", "products")})` : ""));
        } else if (kind === "products") names.push((x.brandName ? x.brandName + ": " : "") + nameOf(x));
        else { const used = productsUsing(x); if (used > 0) stillUsed++; names.push(nameOf(x) + (used > 0 ? ` (still listed by ${many(used, "product", "products")})` : "")); }
    });
    entries.forEach((x) => take(x, kind));                 // the entries themselves go last (a brand after its products)
    if (kind === "ingredients") lost = (stillUsed > 0 ? `${many(stillUsed, "of them is", "of them are")} still in the ingredient list of products. ` : "")
        + "What you wrote about them is lost; the app falls back to its own built-in knowledge, or treats them as unknown.";
    else if (kind === "brands") lost = entries.length === 1 ? "The brand and the removed products under it are taken out of the database, with their scan counts."
        : "The brands and the removed products under them are taken out of the database, with their scan counts.";
    else lost = entries.length === 1 ? "The product is taken out of the database, with its ingredient list and scan count."
        : "The products are taken out of the database, with their ingredient lists and scan counts.";

    const sure = await askConfirm({
        title: `Delete permanently: ${many(entries.length, KINDS[kind].one, KINDS[kind].more)}?`,
        message: `${lost} This cannot be undone. A copy is saved to your computer first.`,
        items: names, confirmLabel: "Delete permanently", danger: true
    });
    if (!sure) return;
    if (!saveCopy(copies) && !(await askConfirm({ title: "The copy could not be saved", message: "Delete anyway, without a copy?", confirmLabel: "Delete without a copy", danger: true }))) return;
    try {
        await runBatches(Array.from(refs.values()).map((ref) => ({ ref })));
        notify(`${many(refs.size, "entry", "entries")} deleted for good. A copy is in your Downloads folder.`, "ok");
    } catch (error) {
        console.error("Error deleting: ", error);
        notify("Not everything was deleted. " + describeError(error), "error");
    }
    picked.clear();
    await load();
}
