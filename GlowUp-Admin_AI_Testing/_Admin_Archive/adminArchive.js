// Archive page: what left the Recycle bin, under three tabs. Kept 30 days from the day it was archived, then the
// server job (api/archive-purge.js) deletes it. The data side is in _Shared/adminArchive.js; the design in ARCHIVE_DESIGN.md.
//   Restore     - back to the Recycle bin, exactly as it was (never over an entry that uses the same id again).
//                 A brand brings back the products archived with it; a product whose brand is archived brings the brand back.
//   Delete now  - always asks, saves a copy to the admin's computer first, then deletes the archive document.
// "X days left" on each row; from 3 days left the row is marked. An entry whose time is up waits for the next daily run.
import {
    requireAdmin, wireSignOut, setupNavigationGuard, escapeHtml, asArray, timeAgo, formatWhen, notify, describeError, wireSearch, dialogIsOpen
} from "../_Shared/adminCommon.js";
import { matchesSearch } from "../_Shared/glowupRules.js";
import { mountShell, placeHeaderParts, paginate, renderPager, icon, BIN_ICON, askConfirm, hydrate } from "../_Shared/adminShell.js";
import { listArchive, restoreFromArchive, deleteFromArchive, daysLeft, expired, KEEP_DAYS, WARN_DAYS } from "../_Shared/adminArchive.js";

const el = (id) => document.getElementById(id);
const many = (n, one, more) => `${n} ${n === 1 ? one : more}`;
const KINDS = {
    ingredients: { title: "Archived ingredients", one: "ingredient", more: "ingredients" },
    brands: { title: "Archived brands", one: "brand", more: "brands" },
    products: { title: "Archived products", one: "product", more: "products" }
};
const toDate = (value) => (value && typeof value.toDate === "function" ? value.toDate() : null);

let entries = [];
let kind = "ingredients";
let loaded = false;
const picked = new Set();
const pager = { page: 1, size: 10 };

document.addEventListener("DOMContentLoaded", async () => {
    mountShell("archive");
    placeHeaderParts();                 // the same header as the Recycle Bin (New8.pdf)
    wireSignOut();
    setupNavigationGuard();
    try { const wanted = new URLSearchParams(location.search).get("show"); if (KINDS[wanted]) kind = wanted; } catch (error) { /* keep the first tab */ }
    paintTabs();
    await requireAdmin();

    el("archiveTabs").addEventListener("click", (event) => {
        const button = event.target.closest("[data-kind]");
        if (!button || button.dataset.kind === kind) return;
        kind = button.dataset.kind; picked.clear(); pager.page = 1;
        render();
    });
    wireSearch(el("archiveSearchInput"), el("archiveSearchClear"), () => { pager.page = 1; render(); });
    el("archivePickAll").addEventListener("click", () => {
        const shown = shownEntries();
        const allPicked = shown.length > 0 && shown.every((x) => picked.has(x.id));
        shown.forEach((x) => { if (allPicked) picked.delete(x.id); else picked.add(x.id); });
        render();
    });
    el("archiveTableBody").addEventListener("click", onRowClick);
    el("archiveRestoreSelectedBtn").addEventListener("click", () => restore(ofKind().filter((x) => picked.has(x.id))));
    el("archiveDeleteSelectedBtn").addEventListener("click", () => removeNow(ofKind().filter((x) => picked.has(x.id))));
    el("archiveClearSelectedBtn").addEventListener("click", () => { picked.clear(); render(); });
    // "X days left" moves on while the page stays open
    window.setInterval(() => { if (loaded && !dialogIsOpen()) render(); }, 60 * 60 * 1000);
    load();
});

async function load() {
    try {
        entries = await listArchive();
        loaded = true;
        render();
    } catch (error) {
        console.error("Error loading the Archive: ", error);
        el("archiveTableBody").innerHTML = `<tr><td colspan="6" class="table-message error">Could not load the Archive. ${escapeHtml(describeError(error))} <button type="button" class="btn" id="archiveRetry">Try again</button></td></tr>`;
        el("archiveRetry").addEventListener("click", load);
    }
}

const ofKind = (of = kind) => entries.filter((x) => x.sourceCollection === of);
const nameOf = (x) => String(x.name || (x.data && x.data.name) || "Unnamed");
const archivedAt = (x) => { const d = toDate(x.deleted_at); return d ? d.getTime() : 0; };

function shownEntries() {
    const typed = el("archiveSearchInput").value;
    const data = (x) => x.data || {};
    return ofKind()
        .filter((x) => matchesSearch(typed, [nameOf(x), data(x).techName, data(x).brandName, data(x).cosmeticType, x.deleted_by_email].concat(asArray(data(x).aliases))))
        .sort((a, b) => daysLeft(a) - daysLeft(b) || archivedAt(b) - archivedAt(a) || nameOf(a).localeCompare(nameOf(b)));      // the soonest to go first
}

function paintTabs() {
    document.querySelectorAll("#archiveTabs .seg").forEach((button) => {
        const on = button.dataset.kind === kind;
        button.classList.toggle("on", on);
        button.setAttribute("aria-selected", on ? "true" : "false");
    });
    el("archiveTitle").textContent = KINDS[kind].title;
}

function render() {
    paintTabs();
    if (!loaded) return;
    el("countIngredients").textContent = ofKind("ingredients").length || "";
    el("countBrands").textContent = ofKind("brands").length || "";
    el("countProducts").textContent = ofKind("products").length || "";

    const here = ofKind(), shown = shownEntries();
    Array.from(picked).forEach((id) => { if (!here.some((x) => x.id === id)) picked.delete(id); });
    const typed = el("archiveSearchInput").value.trim();
    el("archiveCountLine").textContent = typed ? `${shown.length} of ${many(here.length, KINDS[kind].one, KINDS[kind].more)}` : many(here.length, KINDS[kind].one, KINDS[kind].more);

    const empty = shown.length === 0;
    el("archiveEmpty").hidden = !empty;
    document.querySelector(".archive-table").hidden = empty;
    if (empty) {
        el("archiveEmptyMessage").textContent = here.length === 0 ? `No ${KINDS[kind].more} in the Archive.` : `No archived ${KINDS[kind].one} fits "${typed}".`;
        el("archiveEmptyHint").textContent = here.length === 0 ? `Entries moved here from the Recycle bin are kept ${KEEP_DAYS} days.` : "Try fewer letters, or another word of the name.";
    }

    const page = paginate(shown, pager);
    el("archiveTableBody").innerHTML = page.map(rowOf).join("");
    hydrate(el("archiveTableBody"));
    const again = () => render();
    renderPager(el("archivePagerTop"), pager, shown.length, again, KINDS[kind].more);
    renderPager(el("archivePager"), pager, shown.length, again, KINDS[kind].more);

    const allPicked = shown.length > 0 && shown.every((x) => picked.has(x.id));
    el("archivePickAll").setAttribute("aria-checked", allPicked ? "true" : picked.size > 0 ? "mixed" : "false");
    el("archivePickAll").classList.toggle("on", allPicked);
    el("archiveBar").hidden = picked.size === 0;
    el("archiveBarCount").textContent = `${picked.size} selected`;
}

function leftWords(x) {
    if (expired(x)) return { text: "Time is up: deleted at the next daily run", tone: "bad" };
    const n = daysLeft(x);
    return { text: `${many(n, "day", "days")} left`, tone: n <= WARN_DAYS ? "warn" : "" };
}

function rowOf(x) {
    const isPicked = picked.has(x.id), data = x.data || {};
    let sub = "";
    if (kind === "ingredients" && data.techName) sub = data.techName;
    if (kind === "products") sub = [data.brandName, data.cosmeticType].filter((part) => part).join(" · ");
    if (kind === "brands") { const riders = entries.filter((p) => p.sourceCollection === "products" && p.archivedWith === x.sourceId).length; sub = riders > 0 ? `${many(riders, "product was", "products were")} archived with it` : ""; }
    const when = toDate(x.deleted_at), left = leftWords(x);
    return `
        <tr class="${isPicked ? "is-picked" : ""}${left.tone ? " left-" + left.tone : ""}" data-id="${escapeHtml(x.id)}">
            <td class="pick-col"><button type="button" class="pick${isPicked ? " on" : ""}" role="checkbox" aria-checked="${isPicked}" data-act="pick" aria-label="Select ${escapeHtml(nameOf(x))}"></button></td>
            <td><span class="bin-name">${escapeHtml(nameOf(x))}</span>${sub ? `<span class="row-sub">${escapeHtml(sub)}</span>` : ""}</td>
            <td class="bin-when" title="${escapeHtml(when ? formatWhen(when) : "")}">${when ? escapeHtml(timeAgo(when)) : `<span class="muted">just now</span>`}</td>
            <td class="bin-why">${x.deleted_by_email ? escapeHtml(x.deleted_by_email) : `<span class="muted">–</span>`}</td>
            <td><span class="days-left${left.tone ? " " + left.tone : ""}">${left.tone ? icon("clock") : ""}${escapeHtml(left.text)}</span></td>
            <td><div class="row-actions">
                <button type="button" class="row-btn main" data-act="restore" data-tip="Puts it back in the Recycle bin, as it was.">${icon("undo")}Restore</button>
                <button type="button" class="row-btn danger" data-act="delete" data-tip="Deletes it now instead of when its time is up. It asks first, and saves a copy to your computer.">${BIN_ICON}Delete now</button>
            </div></td>
        </tr>`;
}

function onRowClick(event) {
    const button = event.target.closest("[data-act]"), row = event.target.closest("tr[data-id]");
    if (!button || !row) return;
    const entry = ofKind().find((x) => x.id === row.dataset.id);
    if (!entry) return;
    if (button.dataset.act === "pick") { if (picked.has(entry.id)) picked.delete(entry.id); else picked.add(entry.id); render(); }
    else if (button.dataset.act === "restore") restore([entry]);
    else if (button.dataset.act === "delete") removeNow([entry]);
}

/** RESTORE: back to the Recycle bin. A brand brings its products archived with it; a product brings its archived brand. */
async function restore(chosen) {
    if (chosen.length === 0) return;
    const going = new Map();
    const take = (x) => { if (x) going.set(x.id, x); };
    chosen.forEach((x) => {
        take(x);
        if (x.sourceCollection === "brands") entries.filter((p) => p.sourceCollection === "products" && p.archivedWith === x.sourceId).forEach(take);
        if (x.sourceCollection === "products" && x.data && x.data.brandId) take(entries.find((b) => b.sourceCollection === "brands" && b.sourceId === x.data.brandId));
    });
    const extra = going.size - chosen.length;
    const names = chosen.length === 1 ? `"${nameOf(chosen[0])}"` : many(chosen.length, KINDS[kind].one, KINDS[kind].more);
    try {
        const done = await restoreFromArchive(Array.from(going.values()));
        const skipped = done.skipped.length > 0 ? ` Not restored, because an entry with the same id exists again: ${done.skipped.join(", ")}.` : "";
        if (done.restored > 0) notify(`${names}${extra > 0 ? ` (and ${many(extra, "related entry", "related entries")})` : ""} back in the Recycle bin.${skipped}`, done.skipped.length > 0 ? "info" : "ok");
        else notify(`Nothing was restored.${skipped}`, "error");
    } catch (error) {
        console.error("Error restoring from the Archive: ", error);
        notify("Not everything was restored. " + describeError(error), "error");
    }
    picked.clear();
    await load();
}

/** A copy of what is about to be deleted, saved to the admin's computer (a JSON file), as the Recycle bin does. */
function saveCopy(chosen) {
    try {
        const stamp = new Date().toISOString().replace(/[:T]/g, "-").slice(0, 19);
        const documents = chosen.map((x) => ({ collection: x.sourceCollection, id: x.sourceId, archivedAs: x.id, data: x.data }));
        const blob = new Blob([JSON.stringify({ deletedAt: new Date().toISOString(), from: "GlowUp Archive", documents }, null, 2)], { type: "application/json" });
        const link = document.createElement("a");
        link.href = URL.createObjectURL(blob);
        link.download = `glowup-archive-deleted-${stamp}.json`;
        document.body.appendChild(link); link.click(); link.remove();
        setTimeout(() => URL.revokeObjectURL(link.href), 4000);
        return true;
    } catch (error) { console.warn("The copy could not be saved: ", error); return false; }
}

/** DELETE NOW: asks first, with the names. A brand takes the products archived with it. */
async function removeNow(chosen) {
    if (chosen.length === 0) return;
    const going = new Map(), names = [];
    chosen.forEach((x) => {
        going.set(x.id, x);
        const riders = x.sourceCollection === "brands" ? entries.filter((p) => p.sourceCollection === "products" && p.archivedWith === x.sourceId) : [];
        riders.forEach((p) => going.set(p.id, p));
        names.push(nameOf(x) + (riders.length > 0 ? ` (with ${many(riders.length, "product", "products")})` : ""));
    });
    const sure = await askConfirm({
        title: `Delete now: ${many(chosen.length, KINDS[kind].one, KINDS[kind].more)}?`,
        message: "They are deleted for good now, instead of when their time is up. This cannot be undone. A copy is saved to your computer first.",
        items: names, confirmLabel: "Delete now", danger: true
    });
    if (!sure) return;
    const list = Array.from(going.values());
    if (!saveCopy(list) && !(await askConfirm({ title: "The copy could not be saved", message: "Delete anyway, without a copy?", confirmLabel: "Delete without a copy", danger: true }))) return;
    try {
        const n = await deleteFromArchive(list);
        notify(`${many(n, "entry", "entries")} deleted for good. A copy is in your Downloads folder.`, "ok");
    } catch (error) {
        console.error("Error deleting from the Archive: ", error);
        notify("Not everything was deleted. " + describeError(error), "error");
    }
    picked.clear();
    await load();
}
