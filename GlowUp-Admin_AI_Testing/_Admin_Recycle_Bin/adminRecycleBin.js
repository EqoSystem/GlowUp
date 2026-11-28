













import { db } from "../_Database/firebase-console.js";
import { auth } from "../_Database/firebase-console.js";
import { collection, doc, serverTimestamp, query, where } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { writeBatch, onSnapshot, getDocs } from "../_Shared/adminStore.js";
import { normalizeUser, statusPatch, restoreTarget, statusLabel } from "../_Shared/userRules.js";
import {
    requireAdmin, wireSignOut, setupNavigationGuard, escapeHtml, sameKey, asArray, toDate, timeAgo, formatWhen, notify, describeError,
    statusBadge, statusGuide, normalizeStatus, STATUSES, wireSearch, dialogIsOpen
} from "../_Shared/adminCommon.js";
import { matchesSearch } from "../_Shared/glowupRules.js";
import { moveToArchive, KEEP_DAYS } from "../_Shared/adminArchive.js";
import { mountShell, placeHeaderParts, paginate, renderPager, icon, BIN_ICON, askConfirm, hydrate, setBinCount } from "../_Shared/adminShell.js";
import { bindButtons } from "../_Shared/shortcuts.js";
import { registerDataset, datasetChanged } from "../_Shared/adminWidgets.js";

const el = (id) => document.getElementById(id);
const many = (n, one, more) => `${n} ${n === 1 ? one : more}`;
const BATCH_LIMIT = 400;
const KINDS = {
    ingredients: { title: "Removed ingredients", one: "ingredient", more: "ingredients" },
    brands: { title: "Removed brands", one: "brand", more: "brands" },
    products: { title: "Removed products", one: "product", more: "products" },
    users: { title: "Removed users", one: "user", more: "users" }
};

const all = { ingredients: [], brands: [], products: [], users: [] };
let kind = "ingredients";
let loaded = false;
const picked = new Set();
const pager = { page: 1, size: 10 };


registerDataset("bin", {
    label: "Removed entries", noun: "entries", nounOne: "entry",
    items: () => (loaded ? shownEntries() : []),
    map: (x) => ({ id: x.id, title: nameOf(x), subtitle: kind, meta: x.binnedAt ? timeAgo(new Date(x.binnedAt)) : "not noted", tone: "muted" }),
    dimensions: [{ id: "why", label: "Reason", of: (x) => x.binnedReason || null, none: "No reason noted" }]
});

document.addEventListener("DOMContentLoaded", async () => {
    mountShell("bin");
    bindButtons({ "bin.restore": "#restoreSelectedBtn", "bin.archive": "#archiveSelectedBtn", "bin.delete": "#deleteSelectedBtn" });

    placeHeaderParts();                                          // (no Refresh button: the lists are live)
    wireSignOut();
    setupNavigationGuard();
    el("binStatusGuide").innerHTML = statusGuide();
    try { const wanted = new URLSearchParams(location.search).get("show"); if (KINDS[wanted]) kind = wanted; } catch (error) {                          }
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

    load();
    window.addEventListener("pagehide", stopListening);
    window.addEventListener("pageshow", (event) => { if (event.persisted) load(); });
});




// Live lists: one listener per list, so a removal on another page or by another admin shows here at once.
// Only the entries IN the bin are watched (binned == true; 28 Nov 2026 quota fix): watching the whole ingredients, brands
// and products collections read every document in them on each visit. A restored entry leaves the query, which the
// listener reports as a change, so it still disappears from the list at once.
let stops = [];
const answered = new Set();
function stopListening() { stops.forEach((stop) => stop()); stops = []; answered.clear(); }
function failed(error) {
    console.error("Error loading the Recycle bin: ", error);
    el("binTableBody").innerHTML = `<tr><td colspan="6" class="table-message error">Could not load the Recycle bin. ${escapeHtml(describeError(error))} <button type="button" class="btn" id="binRetry">Try again</button></td></tr>`;
    el("binRetry").addEventListener("click", load);
}
function load() {
    stopListening();
    const read = (snapshot) => snapshot.docs.map((d) => {
        const data = d.data(), since = toDate(data.binnedAt);
        return Object.assign({}, data, { id: d.id, binned: data.binned === true, binnedAt: since ? since.getTime() : 0, raw: data });
    });
    const arrive = (name) => { answered.add(name); loaded = answered.size === 4; if (loaded && !dialogIsOpen()) render(); };
    ["ingredients", "brands", "products"].forEach((name) => {
        stops.push(onSnapshot(query(collection(db, name), where("binned", "==", true)), (snapshot) => { all[name] = read(snapshot); arrive(name); }, failed));
    });
    // users removed on User Management (status "deleted", inBin true): the same rows, restored to the status they had
    stops.push(onSnapshot(query(collection(db, "users"), where("inBin", "==", true)), (snapshot) => {
        all.users = snapshot.docs.map((d) => {
            const u = normalizeUser(d.id, d.data());
            return Object.assign({}, u, { id: d.id, binned: u.status === "deleted", binnedAt: u.changedAt, binnedReason: "Deleted on User Management.", raw: d.data(), user: u });
        });
        arrive("users");
    }, failed));
}




const binned = (of = kind) => all[of].filter((x) => x.binned);
const brandOf = (product) => all.brands.find((b) => b.id === product.brandId) || null;
const nameOf = (x, of = kind) => String(x.name || (of === "products" ? x.productName : "") || `Unnamed ${KINDS[of].one}`);

const ridersOf = (brand) => all.products.filter((p) => p.binned && p.binnedWith === brand.id);

function shownEntries() {
    const typed = el("binSearchInput").value;
    return binned()
        .filter((x) => matchesSearch(typed, [nameOf(x), x.techName, x.brandName, x.cosmeticType, x.binnedReason, x.email, x.username].concat(asArray(x.aliases))))
        .sort((a, b) => (b.binnedAt || 0) - (a.binnedAt || 0) || nameOf(a).localeCompare(nameOf(b)));
}


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
    datasetChanged("bin");
    el("countIngredients").textContent = binned("ingredients").length || "";
    el("countBrands").textContent = binned("brands").length || "";
    el("countProducts").textContent = binned("products").length || "";
    el("countUsers").textContent = binned("users").length || "";
    const userTab = kind === "users";
    el("archiveSelectedBtn").hidden = userTab;                     // users are not archived: restore or delete
    setBinCount();

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
    if (kind === "users") sub = [x.username ? "@" + x.username : "", x.email].filter(Boolean).join(" · ");
    const before = kind === "ingredients" ? `<span class="muted">–</span>` : kind === "users" ? `<span class="status-badge user-${escapeHtml(restoreTarget(x.user))}">${escapeHtml(statusLabel(restoreTarget(x.user)))}</span>` : statusBadge(STATUSES.includes(normalizeStatus(x.statusBeforeBin)) ? normalizeStatus(x.statusBeforeBin) : "Active");
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
                ${kind === "users" ? "" : `<button type="button" class="row-btn" data-act="archive" data-tip="Moves it to the Archive: kept ${KEEP_DAYS} days, then deleted. It can be restored until then.">${icon("archive")}Archive</button>`}
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
    render();
}





async function runBatches(ops) {
    for (let i = 0; i < ops.length; i += BATCH_LIMIT) {
        const batch = writeBatch(db);
        ops.slice(i, i + BATCH_LIMIT).forEach((op) => { if (op.data) batch.update(op.ref, op.data); else batch.delete(op.ref); });
        await batch.commit();
    }
}






async function restoreUsers(entries) {
    const me = auth.currentUser ? auth.currentUser.uid : "";
    try {
        const batch = writeBatch(db);
        entries.forEach((x) => batch.update(doc(db, "users", x.id), statusPatch(x.user, restoreTarget(x.user), serverTimestamp(), me)));
        await batch.commit();
        notify(`${entries.length === 1 ? `"${nameOf(entries[0])}"` : many(entries.length, "user", "users")} restored: back on User Management with the status they had.`, "ok");
    } catch (error) {
        console.error("Error restoring users: ", error);
        notify("Not everything was restored. " + describeError(error), "error");
    }
    picked.clear();
}

async function restore(entries) {
    if (entries.length === 0) return;
    if (kind === "users") return restoreUsers(entries);
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
    render();
}


// listed products come from the shared cached list (a few reads), read only when an ingredient is deleted for good
function productsUsing(ingredient, products) {
    const keys = new Set([ingredient.name, ingredient.techName].concat(asArray(ingredient.aliases)).map(sameKey).filter((key) => key));
    return products.filter((p) => !p.binned && asArray(p.ingredients).some((name) => keys.has(sameKey(name)))).length;
}


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





async function purgeUsers(entries) {
    const sure = await askConfirm({
        title: `Delete permanently: ${many(entries.length, "user", "users")}?`,
        message: "Their GlowUp profile (skin profile, settings, photo) is taken out of the database and their sign-in account is removed from Firebase Authentication, so they can no longer sign in. This cannot be undone. A copy is saved to your computer first.",
        items: entries.map((x) => nameOf(x) + (x.email ? " (" + x.email + ")" : "")), confirmLabel: "Delete permanently", danger: true
    });
    if (!sure) return;
    if (!saveCopy(entries.map((x) => ({ collection: "users", id: x.id, data: x.raw }))) && !(await askConfirm({ title: "The copy could not be saved", message: "Delete anyway, without a copy?", confirmLabel: "Delete without a copy", danger: true }))) return;
    try {
        // the sign-in (Firebase Authentication) accounts go first, through the server; the profile is deleted only for accounts that are gone
        let results = null;
        try {
            const token = await auth.currentUser.getIdToken();
            const answer = await fetch("/api/delete-auth-user", { method: "POST", headers: { "Content-Type": "application/json", Authorization: "Bearer " + token },
                body: JSON.stringify({ uids: entries.map((x) => x.id) }), signal: AbortSignal.timeout(30000) });
            if (answer.ok) results = (await answer.json()).results || {};
        } catch (e) { console.warn("Sign-in accounts could not be removed: ", e); }
        let removable = entries;
        if (results) removable = entries.filter((x) => results[x.id] === "removed");
        else if (!(await askConfirm({ title: "Sign-in accounts could not be removed", message: "The server that removes sign-in accounts did not answer (it may not be set up yet). Delete only the profiles from the database? The sign-in accounts would stay in Firebase Authentication.", confirmLabel: "Delete profiles only", danger: true }))) { picked.clear(); return; }
        if (removable.length > 0) await runBatches(removable.map((x) => ({ ref: doc(db, "users", x.id) })));
        const skipped = entries.length - removable.length;
        notify(skipped > 0 ? `${many(removable.length, "user", "users")} deleted for good. ${many(skipped, "was", "were")} kept because the sign-in account could not be removed.`
            : `${many(entries.length, "user", "users")} deleted for good, with ${entries.length === 1 ? "the" : "their"} sign-in ${entries.length === 1 ? "account" : "accounts"}. A copy is in your Downloads folder.`, skipped > 0 ? "error" : "ok");
    } catch (error) {
        console.error("Error deleting users: ", error);
        notify("Not everything was deleted. " + describeError(error), "error");
    }
    picked.clear();
}

async function purge(entries) {
    if (entries.length === 0) return;
    if (kind === "users") return purgeUsers(entries);
    const refs = new Map(), copies = [], names = [];
    const take = (x, listName) => {
        const key = listName + "/" + x.id;
        if (refs.has(key)) return false;
        refs.set(key, doc(db, listName, x.id));
        copies.push({ collection: listName, id: x.id, data: x.raw });
        return true;
    };
    let lost = "", stillUsed = 0;
    let listed = [];
    if (kind === "ingredients") {
        try { listed = (await getDocs(collection(db, "products"))).docs.map((d) => d.data()); }
        catch (error) { console.warn("Could not check which products still list these ingredients: ", error); }
    }
    entries.forEach((x) => {
        if (kind === "brands") {
            const riders = all.products.filter((p) => p.binned && p.brandId === x.id);
            riders.forEach((p) => take(p, "products"));
            names.push(nameOf(x) + (riders.length > 0 ? ` (with ${many(riders.length, "product", "products")})` : ""));
        } else if (kind === "products") names.push((x.brandName ? x.brandName + ": " : "") + nameOf(x));
        else { const used = productsUsing(x, listed); if (used > 0) stillUsed++; names.push(nameOf(x) + (used > 0 ? ` (still listed by ${many(used, "product", "products")})` : "")); }
    });
    entries.forEach((x) => take(x, kind));
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
    render();
}
