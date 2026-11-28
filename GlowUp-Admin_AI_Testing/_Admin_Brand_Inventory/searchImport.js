// Search and import + Review queue + Product review + User report (redesign plan, 27 November 2026; brand search, ticks kept
// across searches and one batch import since 28 November 2026, when the separate barcode list window was removed).
// One panel and one button run the whole chain (_Shared/importChain.js). Everything found is saved as Pending; the app does not use
// it until an admin approves it. Imports, app scans and "New product" reports from the app wait in ONE Review queue.
// Navigation rules (docs/NAVIGATION_RULES.md 3c): closing a review or a report returns to the queue with its page kept and shows the
// next item or "No pending items"; unsaved edits ask "Discard changes?"; the search is locked while it runs; Cancel saves nothing.
import { db, auth } from "../_Database/firebase-console.js";
import { collection, doc, query, where, serverTimestamp, addDoc as rawAddDoc } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { updateDoc, onSnapshot } from "../_Shared/adminStore.js";
import { escapeHtml, notify, describeError } from "../_Shared/adminCommon.js";
import { askConfirm, askText, icon, paginate, renderPager } from "../_Shared/adminShell.js";
import { openReplyDrawer } from "../_Shared/replyDrawer.js";
import { popWindow } from "../_Shared/planViews.js";
import { mountHints } from "../_Shared/buttonHints.js";
import { draftIngredient, ingredientRecord } from "../_Shared/draftIngredient.js";
import { classifications, loadClassifications } from "../_Shared/adminClassifications.js";
import { runImport, reviewRows, countResults, STEPS, RESULT_WORDS, FLAGGED, NO_RULE, NOT_IDENTIFIED } from "../_Shared/importChain.js";
import { matchRoute } from "../_Shared/capture.js";

export const NEW_PRODUCT = "New product";
const el = (id) => document.getElementById(id);
const make = (tag, className, text) => { const n = document.createElement(tag); if (className) n.className = className; if (text != null) n.textContent = text; return n; };
const TONE = { [FLAGGED]: "warn", [NO_RULE]: "", [NOT_IDENTIFIED]: "bad" };
const lineUrl = (line) => { const t = String(line || ""); const u = t.slice(t.lastIndexOf("|") + 1).trim(); return /^https:\/\//i.test(u) ? u : ""; };
const lineTitle = (line) => { const t = String(line || ""); const at = t.lastIndexOf("|"); return (at > 0 ? t.slice(0, at) : t).trim(); };
const hostOf = (url) => { try { return new URL(url).hostname.replace(/^www\./, ""); } catch (error) { return url; } };

/**
 * deps (from the inventory page):
 *   lookup(text)              the page's product look-up (quickFill.lookupProduct)
 *   lookupBrand(text)         every product of one brand the sources know (quickFill.lookupBrand)
 *   inInventory(candidate)    the inventory product that is this candidate (same barcode, or same brand and name), or null
 *   brandProducts(text)       the inventory products of the brand typed (not in the bin)
 *   library()                 Curator entries { id, name, techName, aliases, allergenTags, avoidForSkinTypes, riskLevel, sources, identified }
 *   pending()                 products waiting for review (not in the bin)
 *   saveRecord(record)        saves one product as Pending (the file-import path); resolves with a summary text
 *   findSaved(product)        the saved product for a plan's product (after saveRecord), or null
 *   approve(product)          approves one product (asks first); resolves true when it was approved
 *   toBin(product)            moves one product to the Recycle bin
 *   openProduct(product)      opens the product form (to add a list by hand)
 *   reload()                  reads the lists again
 */
export function mountSearchImport(deps) {
    const queryBox = el("siQuery"), run = el("siRun"), progress = el("siProgress");
    const results = el("siResults"), body = el("siBody"), batchBar = el("siBatch");
    let busy = false, abort = null, fallback = null, fromReport = null, plan = null;
    const queueState = { page: 1, size: 10 };
    let reports = [];
    let shown = [];                                   // the rows of the last search: { key, candidate, owner }
    const batch = new Map();                          // ticked products, kept across searches and brands: key -> candidate

    // a file picked from the + menu (or a shortcut) shows its progress in this panel
    el("inventoryFileInput").addEventListener("change", () => { el("siFile").hidden = false; el("siPanel").scrollIntoView({ block: "nearest" }); });
    mountHints(el("siPanel"));
    mountHints(el("rqPanel"));
    queryBox.addEventListener("keydown", (event) => { if (event.key === "Enter") { event.preventDefault(); search(); } });
    run.addEventListener("click", search);

    const lock = (on) => { busy = on; run.disabled = on; queryBox.readOnly = on; el("siBatchImport").disabled = on || batch.size === 0; el("siBatchClear").disabled = on; el("siPickAll").disabled = on; };
    const keyOf = (c) => [compact(c.brand), compact(c.name), String(c.barcode || "").replace(/\D/g, "")].join("|");
    const compact = (t) => String(t || "").toLowerCase().replace(/[^a-z0-9]+/g, "");

    // ---------- 1. Search: brand, product name, or several barcodes ----------
    async function search() {
        if (busy) return;
        if (!progress.hidden && !abort) { progress.hidden = true; runRows = []; }
        const typed = queryBox.value.trim();
        if (!typed) { queryBox.focus(); notify("Type a brand, a product name or a barcode first.", "info"); return; }
        lock(true);
        results.hidden = false;
        el("siResultsTitle").textContent = `Searching for "${typed}"...`;
        body.innerHTML = `<tr><td colspan="6" class="table-message"><span class="sk sk-line" aria-hidden="true"></span></td></tr>`;
        try {
            const parts = typed.split(/[\s,;]+/).filter(Boolean);
            const codes = parts.length > 1 && parts.every((t) => /^\d{6,14}$/.test(t)) ? parts.slice(0, 30) : null;
            let found = [];
            if (codes) {
                for (const code of codes) { const r = await deps.lookup(code).catch(() => ({ candidates: [] })); if (r.candidates && r.candidates[0]) found.push(r.candidates[0]); }
            } else {
                const [brand, name] = await Promise.all([deps.lookupBrand(typed).catch(() => ({ candidates: [] })), deps.lookup(typed).catch(() => ({ candidates: [] }))]);
                found = (brand.candidates || []).concat(name.candidates || []);
            }
            const seen = new Set();
            shown = [];
            found.forEach((c) => { const key = keyOf(c); if (!c || !c.name || seen.has(key)) return; seen.add(key); shown.push({ key, candidate: c, owner: deps.inInventory(c) }); });
            // the brand's products that are in the inventory already, also when no source lists them (the admin can review them)
            if (!codes) deps.brandProducts(typed).forEach((p) => {
                const key = keyOf({ brand: p.brandName, name: p.name, barcode: p.barcode });
                if (shown.some((r) => r.owner && r.owner.id === p.id) || seen.has(key)) return;
                seen.add(key); shown.push({ key, candidate: { brand: p.brandName, name: p.name, cosmeticType: p.cosmeticType, ingredients: p.ingredients, originLabel: "GlowUp inventory" }, owner: p });
            });
            shown.sort((a, b) => Number(!!a.owner) - Number(!!b.owner));
            el("siResultsTitle").textContent = shown.length ? `${shown.length} found for "${typed}"` : `Nothing found for "${typed}". Check the spelling, or try the brand alone.`;
            paintResults();
        } catch (error) {
            el("siResultsTitle").textContent = "The search stopped. " + describeError(error);
            body.innerHTML = `<tr><td colspan="6" class="table-message"><button type="button" class="btn" data-si-retry>Retry</button></td></tr>`;
        } finally { lock(false); paintBatch(); }
    }
    body.addEventListener("click", (event) => {
        if (event.target.closest("[data-si-retry]")) { search(); return; }
        const open = event.target.closest("[data-si-open]");
        if (open) { const row = shown.find((r) => r.owner && r.owner.id === open.dataset.siOpen); if (row) openOwner(row.owner); }
    });
    body.addEventListener("change", (event) => {
        const box = event.target.closest("[data-si-pick]");
        if (!box) return;
        const row = shown.find((r) => r.key === box.dataset.siPick);
        if (!row) return;
        if (box.checked) batch.set(row.key, row.candidate); else batch.delete(row.key);
        paintBatch();
    });
    el("siPickAll").addEventListener("click", () => { shown.filter((r) => !r.owner).forEach((r) => batch.set(r.key, r.candidate)); paintResults(); paintBatch(); });
    el("siBatchClear").addEventListener("click", () => { batch.clear(); paintResults(); paintBatch(); });
    el("siBatchImport").addEventListener("click", importBatch);

    const openOwner = (p) => { if (p.needsReview && p.ingredients.length) openReview(p); else deps.openProduct(p); };

    function paintResults() {
        if (!shown.length) { body.innerHTML = `<tr><td colspan="6" class="table-message">No products to show.</td></tr>`; return; }
        body.innerHTML = shown.map((r) => {
            const c = r.candidate, n = Array.isArray(c.ingredients) ? c.ingredients.length : 0;
            const pick = r.owner ? "" : `<input type="checkbox" data-si-pick="${escapeHtml(r.key)}" aria-label="Tick ${escapeHtml(c.name)}"${batch.has(r.key) ? " checked" : ""}>`;
            const status = r.owner
                ? `<span class="chip ${r.owner.needsReview ? "warn" : "good"}">In inventory · ${r.owner.needsReview ? "Pending" : escapeHtml(r.owner.status || "Active")}</span> <button type="button" class="btn" data-si-open="${escapeHtml(r.owner.id)}">${r.owner.needsReview ? "Review" : "Open"}</button>`
                : `<span class="chip">New</span>`;
            return `<tr><td class="si-pick">${pick}</td><td><strong>${escapeHtml(c.name)}</strong><div class="small muted">${escapeHtml(c.brand || "")}${c.barcode ? " · " + escapeHtml(c.barcode) : ""}</div></td>
                <td>${escapeHtml(c.cosmeticType || "-")}</td><td>${n ? `${n} ingredients` : `<span class="chip bad">None found</span>`}</td>
                <td class="small">${escapeHtml(c.originLabel || c.origin || "-")}</td><td class="actions">${status}</td></tr>`;
        }).join("");
    }

    function paintBatch() {
        batchBar.hidden = batch.size === 0;
        const brands = new Set(Array.from(batch.values()).map((c) => compact(c.brand)).filter(Boolean));
        el("siBatchWords").textContent = batch.size ? `Batch: ${batch.size} product${batch.size === 1 ? "" : "s"} from ${brands.size || 1} brand${brands.size > 1 ? "s" : ""}, ready to import` : "";
        el("siBatchImport").disabled = busy || batch.size === 0;
        el("siBatchImport").textContent = batch.size ? `Import batch (${batch.size})` : "Import batch";
    }

    // ---------- 2. Import the batch (redesigned 28 Nov 2026) ----------
    // One table row per product shows where it is: Waiting, then each step, then Saved / Not saved / Cancelled. Before the chain
    // runs, the missing details of each product (list, kind, barcode, FDA number) are filled from the other sources by itself:
    // only a sure match of the SAME brand is used. Cancel stops at once: the rows not saved yet are marked Cancelled.
    const runBody = progress.querySelector(".si-run"), say = progress.querySelector(".si-say"), bar = progress.querySelector(".si-bar > span");
    const cancelBtn = progress.querySelector(".si-cancel"), openBtn = progress.querySelector(".si-open"), doneBtn = progress.querySelector(".si-done");
    let runRows = [];
    const stoppedError = () => { const e = new Error("Cancelled"); e.stopped = true; return e; };
    // resolves like the promise, but rejects AT ONCE when Cancel is pressed (the screen never waits for a slow source)
    const cut = (promise, signal) => new Promise((resolve, reject) => {
        if (signal.aborted) { reject(stoppedError()); return; }
        const onAbort = () => reject(stoppedError());
        signal.addEventListener("abort", onAbort, { once: true });
        Promise.resolve(promise).then((v) => { signal.removeEventListener("abort", onAbort); resolve(v); }, (e) => { signal.removeEventListener("abort", onAbort); reject(e); });
    });
    const ROW_WORDS = { wait: "Waiting", fill: "Filling missing details", saving: "Saving", saved: "Saved as Pending", failed: "Not saved", cancelled: "Cancelled, not saved" };
    const ROW_TONE = { saved: "good", failed: "bad", cancelled: "" };
    function paintRun() {
        runBody.innerHTML = runRows.map((r) => {
            const step = r.state === "step" ? `${r.at + 1} of ${STEPS.length}: ${STEPS[r.at]}` : ROW_WORDS[r.state];
            const tone = ROW_TONE[r.state];
            return `<tr><td><strong>${escapeHtml(r.c.name || "")}</strong><div class="small muted">${escapeHtml(r.c.brand || "")}</div></td>
                <td>${tone !== undefined ? `<span class="chip ${tone}">${escapeHtml(step)}</span>` : `<span class="si-now${r.state === "wait" ? " muted" : ""}">${escapeHtml(step)}</span>`}${r.why ? `<div class="small muted">${escapeHtml(r.why)}</div>` : ""}</td>
                <td class="small">${r.filled.length ? escapeHtml(r.filled.join(", ")) : `<span class="muted">${r.state === "fill" ? "looking..." : r.fillDone ? "nothing missing, or no source had it" : "-"}</span>`}</td>
                <td>${r.count == null ? "-" : r.count ? `${r.count}` : `<span class="chip bad">None found</span>`}</td></tr>`;
        }).join("");
        const finished = runRows.filter((r) => ["saved", "failed", "cancelled"].includes(r.state)).length;
        bar.style.width = runRows.length ? `${Math.round((finished / runRows.length) * 100)}%` : "0%";
    }
    function startRun(list) {
        runRows = list.map(([key, c]) => ({ key, c, state: "wait", at: 0, filled: [], count: null, why: "" }));
        progress.hidden = false;
        cancelBtn.hidden = false; cancelBtn.disabled = false; openBtn.hidden = true; doneBtn.hidden = true;
        paintRun();
        progress.scrollIntoView({ block: "nearest" });
    }
    function endRun(text) {
        say.textContent = text;
        cancelBtn.hidden = true;
        openBtn.hidden = !runRows.some((r) => r.state === "saved"); openBtn.disabled = openBtn.hidden;
        doneBtn.hidden = false;
        paintRun();
    }

    // the missing details of one product, from the other sources (same brand, a match that is not weak); never replaces a value
    const FILL_KEYS = [["ingredients", "ingredient list"], ["cosmeticType", "cosmetic type"], ["barcode", "barcode"], ["fdaNumber", "FDA number"]];
    const emptyOf = (c, k) => (Array.isArray(c[k]) ? !c[k].length : !c[k]);
    async function fillGaps(c, signal) {
        const out = { ...c, sources: (c.sources || []).slice() }, filled = [];
        if (!FILL_KEYS.some(([k]) => emptyOf(out, k))) return { candidate: out, filled };
        const brandKey = compact(c.brand), queries = [];
        if (c.barcode) queries.push(String(c.barcode));
        if (c.name) queries.push([c.brand, c.name].filter(Boolean).join(" "));
        const from = new Set();
        for (const text of queries) {
            const answer = await cut(deps.lookup(text).catch(() => ({ candidates: [] })), signal);
            for (const other of answer.candidates || []) {
                const exactCode = c.barcode && String(other.barcode || "").replace(/\D/g, "").replace(/^0+/, "") === String(c.barcode).replace(/\D/g, "").replace(/^0+/, "");
                if (brandKey && compact(other.brand) !== brandKey) continue;
                if (!exactCode && matchRoute(other.score) === "weak") continue;
                FILL_KEYS.forEach(([k, words]) => {
                    if (!emptyOf(out, k) || emptyOf(other, k)) return;
                    out[k] = Array.isArray(other[k]) ? other[k].slice() : other[k];
                    filled.push(words); from.add(other.originLabel || (other.origins || [])[0] || "another source");
                });
                (other.sources || []).forEach((line) => { if (!out.sources.includes(line) && out.sources.length < 10) out.sources.push(line); });
            }
            if (!FILL_KEYS.some(([k]) => emptyOf(out, k))) break;
        }
        if (filled.length) out.originLabel = [c.originLabel || c.origin, ...from].filter(Boolean).filter((v, at, all) => all.indexOf(v) === at).join(" + ");
        return { candidate: out, filled };
    }

    async function importBatch() {
        if (busy || !batch.size) return;
        const items = Array.from(batch.entries());
        if (!(await askConfirm({ title: `Import ${items.length} product${items.length === 1 ? "" : "s"}?`, message: "Missing details are filled from the other sources first. Each product is saved as Pending and waits in the Review queue until you approve it. Cancel stops at once.", items: items.map(([, c]) => [c.brand, c.name].filter(Boolean).join(", ")), confirmLabel: "Import batch" }))) return;
        lock(true);
        startRun(items);
        abort = new AbortController();
        const signal = abort.signal;
        let done = 0;
        try {
            for (const row of runRows) {
                if (signal.aborted) break;
                say.textContent = `Importing ${runRows.indexOf(row) + 1} of ${runRows.length}. ${done} saved so far.`;
                try {
                    row.state = "fill"; paintRun();
                    const { candidate, filled } = await fillGaps(row.c, signal);
                    row.filled = filled; row.fillDone = true; row.state = "step"; paintRun();
                    const made = await cut(runImport("", { candidate, lookupProduct: () => ({ candidates: [] }), library: deps.library(), draftIngredient, fallback: {} }, (at) => { row.at = at; paintRun(); }, signal), signal);
                    row.count = made.names.length;
                    if (!made.names.length) row.why = "No source had its ingredient list: add it in the Review queue.";
                    row.state = "saving"; paintRun();
                    await cut(savePlan(made, signal), signal);
                    row.state = "saved"; batch.delete(row.key); done++;
                } catch (error) {
                    if (error && error.stopped) break;
                    row.state = "failed"; row.why = describeError(error);
                }
                paintRun();
            }
        } finally {
            const stopped = signal.aborted;
            let maybe = false;
            runRows.forEach((r) => { if (r.state === "saving") maybe = true; if (!["saved", "failed"].includes(r.state)) { r.state = "cancelled"; r.why = r.why || ""; } });
            abort = null;
            const failed = runRows.filter((r) => r.state === "failed").length;
            endRun((stopped ? "Cancelled. " : "Done. ") + `${done} saved as Pending in the Review queue.` + (failed ? ` ${failed} not saved: tick them and try again.` : "")
                + (maybe ? " The product being saved when you pressed Cancel may still appear in the Review queue: bin it there if you do not want it." : ""));
            shown.forEach((r) => { if (!r.owner) r.owner = deps.inInventory(r.candidate); });
            lock(false); paintResults(); paintBatch();
        }
    }

    // ---------- a user's "New product" report: one product, the report's text fills what the sources lack ----------
    async function importReportProduct() {
        if (busy) return;
        const typed = queryBox.value.trim();
        lock(true); plan = null;
        startRun([["report", { name: typed || "Reported product", brand: fallback ? fallback.brand : "" }]]);
        const row = runRows[0];
        say.textContent = "Importing the reported product...";
        abort = new AbortController();
        const signal = abort.signal;
        try {
            row.state = "step"; paintRun();
            plan = await cut(runImport(typed, { lookupProduct: (text) => deps.lookup(text), library: deps.library(), draftIngredient, fallback: fallback || {} }, (at) => { row.at = at; paintRun(); }, signal), signal);
            row.c = { name: plan.product.name, brand: plan.product.brand };
            row.count = plan.names.length;
            row.state = "saving"; paintRun();
            await cut(savePlan(plan, signal), signal);
            row.state = "saved";
            if (!plan.names.length) row.why = "No ingredient list was found: add it from the pack or the brand's page.";
            endRun("Saved as Pending. It is in the Review queue.");
            if (fromReport) { await closeReport(fromReport, true); fromReport = null; }
            fallback = null;
        } catch (error) {
            row.state = error && error.stopped ? "cancelled" : "failed";
            row.why = error && error.stopped ? "" : error && error.notFound ? "No product was found. Check the spelling or the barcode, or try the brand and the name." : describeError(error);
            endRun(error && error.stopped ? "Cancelled. Nothing was saved." : "The import stopped.");
        } finally { abort = null; lock(false); }
    }
    cancelBtn.addEventListener("click", () => { if (!abort) return; cancelBtn.disabled = true; say.textContent = "Cancelling..."; abort.abort(); });
    openBtn.addEventListener("click", () => el("rqPanel").scrollIntoView({ behavior: "smooth", block: "start" }));
    doneBtn.addEventListener("click", () => { progress.hidden = true; runRows = []; });

    async function savePlan(p, signal = null) {
        const product = p.product;
        const record = {
            brandName: product.brand, productName: product.name, cosmeticType: product.cosmeticType, ingredients: p.names.slice(0, 300),
            barcode: product.barcode, sources: product.sources, imageUrl: "", description: product.note || "", shades: [],
            lookupFrom: "Search and import" + (product.foundBy ? `: ${product.foundBy}` : "")
        };
        if (p.listFrom && /user report/i.test(p.listFrom)) record.description = ("Ingredient list typed in a user report: not verified. " + record.description).trim();
        if (signal && signal.aborted) throw stoppedError();
        await deps.saveRecord(record);
        // the new ingredient names go to the Curator now, as Pending: rules filled the cautions, an admin checks them
        const uid = auth.currentUser ? auth.currentUser.uid : "";
        await loadClassifications().catch(() => null);
        for (const d of p.drafts) {
            if (signal && signal.aborted) break;              // the product is saved; its remaining new names wait for the Curator
            if (!d.name || d.name.length < 2) continue;
            // source "ai" means "nobody has checked it": the app then takes its warnings but never as proof of safety
            try { await rawAddDoc(collection(db, "ingredients"), ingredientRecord(d, { uid, via: "search-import", stamp: serverTimestamp(), classes: classifications() })); }
            catch (error) { console.warn("A new ingredient entry was not saved:", error && error.code ? error.code : error); }
        }
        await deps.reload();
    }

    // ---------- the Review queue: imports, app scans and user reports ----------
    function queueItems() {
        const lib = deps.library();
        const items = deps.pending().map((p) => {
            const rows = reviewRows(p.ingredients, lib);
            return { kind: "product", product: p, rows, counts: countResults(rows), at: p.addedAt || 0 };
        });
        reports.forEach((r) => items.push({ kind: "report", report: r, at: r.createdAtUnix || 0 }));
        return items.sort((a, b) => b.at - a.at);
    }

    // the queue repaints with every inventory render: rows that did not change are left alone, so buttons stay in place
    // (and a repaint that changes nothing cannot start the page's resize work again)
    let lastQueue = "";
    const put = (body, html) => { if (html === lastQueue && body.innerHTML) return; lastQueue = html; body.innerHTML = html; };

    function paintQueue() {
        const body = el("rqBody");
        if (!body) return;
        const items = queueItems();
        const count = el("rqCount");
        if (count) count.textContent = items.length ? String(items.length) : "";
        if (!items.length) { put(body, `<tr><td colspan="6" class="table-message">No pending items.</td></tr>`); renderPager(el("rqPager"), queueState, 0, paintQueue, "items"); return; }
        put(body, paginate(items, queueState).map((item, at) => {
            const key = `${item.kind}:${item.kind === "product" ? item.product.id : item.report.id}`;
            if (item.kind === "report") {
                const r = item.report;
                return `<tr data-rq="${escapeHtml(key)}"><td><strong>User report: new product</strong><div class="small muted">${escapeHtml([r.brand, r.productName].filter(Boolean).join(", ") || (r.barcode ? "Barcode " + r.barcode : ""))}</div></td>
                    <td>-</td><td>-</td><td>-</td><td><span class="chip warn">From a user</span></td><td class="actions"><button type="button" class="btn" data-rq-open="${escapeHtml(key)}" data-hint="Open report: read what the user sent, then import it, reply or close it.">Open report</button></td></tr>`;
            }
            const p = item.product, c = item.counts;
            const both = `<button type="button" class="btn" data-rq-open="${escapeHtml(key)}" data-hint="Review: check the ingredients in a window, then approve, save for later or bin.">Review</button><button type="button" class="btn" data-rq-add="${escapeHtml(key)}" data-hint="Add the list: open the product form and type or correct the ingredients from the pack.">Add the list</button>`;
            if (!p.ingredients.length) return `<tr data-rq="${escapeHtml(key)}"><td><strong>${escapeHtml(p.name)}</strong><div class="small muted">${escapeHtml(p.brandName)}</div></td>
                <td>0</td><td>-</td><td>-</td><td><span class="chip bad">No list found</span><div class="small muted rq-why">${escapeHtml(noListReason(p))}</div></td><td class="actions">${both}</td></tr>`;
            return `<tr data-rq="${escapeHtml(key)}"><td><strong>${escapeHtml(p.name)}</strong><div class="small muted">${escapeHtml(p.brandName)}</div></td>
                <td>${c.all}</td><td><span class="chip warn">${c[FLAGGED]}</span></td><td><span class="chip">${c[NO_RULE]}</span></td><td><span class="chip ${c[NOT_IDENTIFIED] ? "bad" : ""}">${c[NOT_IDENTIFIED]}</span></td>
                <td class="actions">${both}</td></tr>`;
        }).join(""));
        renderPager(el("rqPager"), queueState, items.length, paintQueue, "items");
    }

    // why a waiting product has no ingredient list, from where it came from (the admin then knows to add it by hand)
    function noListReason(p) {
        const from = String((p.fieldOrigins && p.fieldOrigins.name && p.fieldOrigins.name.from) || "");
        if (/search and import/i.test(from)) return "Searched the FDA Philippines list, Open Beauty Facts and UPCitemdb: none had its list.";
        if (p.source === "scan" || String(p.id || "").startsWith("scan_")) return "Added by an app scan: the list on the pack could not be read.";
        if (/barcode look-up/i.test(from)) return "Found by its barcode, but that source had no ingredient list.";
        if (p.source === "document-import") return "The imported file had no ingredient list for it.";
        return "No source with its ingredient list was saved.";
    }

    const itemByKey = (key) => queueItems().find((item) => `${item.kind}:${item.kind === "product" ? item.product.id : item.report.id}` === key) || null;
    el("rqBody").addEventListener("click", (event) => {
        const openBtn = event.target.closest("[data-rq-open]"), addBtn = event.target.closest("[data-rq-add]");
        if (addBtn) { const item = itemByKey(addBtn.dataset.rqAdd); if (item) deps.openProduct(item.product); return; }
        if (!openBtn) return;
        const item = itemByKey(openBtn.dataset.rqOpen);
        if (!item) return;
        if (item.kind === "report") openReport(item.report); else openReview(item.product);
    });

    function openNext(afterKey) {
        const items = queueItems().filter((item) => `${item.kind}:${item.kind === "product" ? item.product.id : item.report.id}` !== afterKey);
        const next = items.find((item) => item.kind === "report" || item.product.ingredients.length > 0) || items[0];
        if (!next) { notify("No pending items.", "ok"); return; }
        if (next.kind === "report") openReport(next.report);
        else if (!next.product.ingredients.length) deps.openProduct(next.product);
        else openReview(next.product);
    }

    // ---------- Product review ----------
    function openReview(product) {
        let names = product.ingredients.slice();
        let dirty = false, filter = "all", working = false, unfolded = false;
        const leave = async () => {
            if (working) return;
            if (dirty && !(await askConfirm({ title: "Discard changes?", message: "The corrected names are not saved.", confirmLabel: "Discard", cancelLabel: "Keep editing" }))) return;
            frame.close(); paintQueue();
        };
        const frame = popWindow("Product review", leave, { subtitle: [product.brandName, "Pending"].filter(Boolean).join(" · ") });
        frame.box.classList.add("review-window");
        frame.title.textContent = product.name;
        const sources = make("p", "rv-sources small");
        const links = (product.sources || []).map((s) => lineUrl(s) ? `<a href="${escapeHtml(lineUrl(s))}" target="_blank" rel="noopener noreferrer">${escapeHtml(lineTitle(s) && lineTitle(s) !== lineUrl(s) ? lineTitle(s) : hostOf(lineUrl(s)))} ↗</a>` : "").filter(Boolean);
        sources.innerHTML = "Sources: " + (links.length ? links.join(" &nbsp; ") : `<span class="muted">none saved</span>`);
        const chips = make("div", "rv-filter"); chips.setAttribute("role", "tablist");
        const tableWrap = make("div", "table-container");
        const foot = frame.foot;
        frame.body.append(sources, chips, tableWrap);

        const paint = () => {
            const rows = reviewRows(names, deps.library());
            const c = countResults(rows);
            chips.innerHTML = [["all", `All ${c.all}`], [FLAGGED, `Flagged ${c[FLAGGED]}`], [NO_RULE, `No rule matched ${c[NO_RULE]}`], [NOT_IDENTIFIED, `Not identified ${c[NOT_IDENTIFIED]}`]]
                .map(([id, words]) => `<button type="button" class="chip-btn${filter === id ? " on" : ""}" data-rv-filter="${id}" aria-selected="${filter === id}">${escapeHtml(words)}</button>`).join("");
            const picked = rows.filter((r) => filter === "all" || r.result === filter);
            // problems come first: in "All", the quiet rows after the first one are folded behind "N more rows"
            const quiet = picked.filter((r) => r.result === NO_RULE);
            const folded = filter === "all" && !unfolded && quiet.length > 2 ? quiet.slice(1) : [];
            const shown = picked.filter((r) => !folded.includes(r));
            const more = folded.length ? `<tr><td>${folded.length} more rows</td><td><span class="chip ${TONE[NO_RULE]}">${RESULT_WORDS[NO_RULE]}</span></td><td>-</td><td></td><td class="actions"><button type="button" class="btn" data-rv-more>Show</button></td></tr>` : "";
            tableWrap.innerHTML = `<table class="admin-table rv-table"><thead><tr><th>Ingredient</th><th>Result</th><th>Caution for</th><th>Why and source</th><th></th></tr></thead><tbody>${
                shown.map((r) => `<tr><td>${r.result === NOT_IDENTIFIED ? `"${escapeHtml(r.name)}"<div class="small muted">as read from the list</div>` : escapeHtml(r.entry ? r.entry.name : r.name)}</td>
                    <td><span class="chip ${TONE[r.result]}">${RESULT_WORDS[r.result]}</span></td>
                    <td>${r.cautionFor.length ? escapeHtml(r.cautionFor.join("; ")) : "-"}</td>
                    <td>${escapeHtml(r.why)}${r.source && r.source.url ? ` <a href="${escapeHtml(r.source.url)}" target="_blank" rel="noopener noreferrer">${escapeHtml(hostOf(r.source.url))} ↗</a>` : ""}</td>
                    <td class="actions">${r.result === NOT_IDENTIFIED ? `<button type="button" class="btn" data-rv-fix="${escapeHtml(r.name)}">Fix name</button>`
                        : `<a class="btn" href="../_Admin_Ingredient_Curator/adminIngredientCurator.html?q=${encodeURIComponent(r.entry ? r.entry.name : r.name)}" target="_blank" rel="noopener">Open</a>`}</td></tr>`).join("")
                + more || `<tr><td colspan="5" class="table-message">Nothing in this group.</td></tr>`}</tbody></table>`;
            const unknown = c[NOT_IDENTIFIED];
            foot.innerHTML = `<p class="small muted">${unknown ? `${unknown} ingredient${unknown === 1 ? " is" : "s are"} not identified. If approved now, ${unknown === 1 ? "it shows" : "they show"} as "Not checked" in the app.` : "Every ingredient was identified."}</p>
                <p class="btn-hint">Save for later keeps your name fixes. The product stays Pending in the Review queue.</p>
                <div class="pw-actions"><button type="button" class="btn danger" data-rv="bin" data-hint="Send to Recycle Bin: it leaves the queue. You can restore it.">${icon("trash")}Send to Recycle Bin</button><span class="spacer"></span><button type="button" class="btn" data-rv="later" data-hint="Save for later keeps your name fixes. The product stays Pending in the Review queue.">Save for later</button><button type="button" class="save-btn" data-rv="approve" data-hint="Approve all: the product goes live and the app starts using it.">Approve all</button></div>`;
            mountHints(foot);
        };
        chips.addEventListener("click", (event) => { const b = event.target.closest("[data-rv-filter]"); if (b) { filter = b.dataset.rvFilter; paint(); } });
        tableWrap.addEventListener("click", async (event) => {
            if (event.target.closest("[data-rv-more]")) { unfolded = true; paint(); return; }
            const fix = event.target.closest("[data-rv-fix]");
            if (!fix) return;
            const was = fix.dataset.rvFix;
            const typed = await askText({ title: "Fix the name", label: "Ingredient name as printed", value: was, maxLength: 120, confirmLabel: "Use this name" });
            if (!typed || !typed.trim() || typed.trim() === was) return;
            names = names.map((n) => (n === was ? typed.trim() : n));
            dirty = true; paint();
        });
        const saveNames = async () => { if (!dirty) return; await updateDoc(doc(db, "products", product.id), { ingredients: names.slice(0, 300), updatedAt: serverTimestamp() }); product.ingredients = names.slice(); dirty = false; };
        foot.addEventListener("click", async (event) => {
            const b = event.target.closest("[data-rv]");
            if (!b || working) return;
            working = true; b.disabled = true;
            try {
                if (b.dataset.rv === "later") { await saveNames(); frame.close(); paintQueue(); notify(`Saved for later. "${product.name}" is in the Review queue on this page, still Pending.`, "ok"); return; }
                if (b.dataset.rv === "bin") { if (!(await askConfirm({ title: "Send to the Recycle Bin?", message: `${product.name} leaves the Review queue. You can restore it from the Recycle Bin.`, confirmLabel: "Send to Recycle Bin", danger: true }))) return; dirty = false; await deps.toBin(product); frame.close(); paintQueue(); openNext(`product:${product.id}`); return; }
                if (b.dataset.rv === "approve") {
                    await saveNames();
                    const ok = await deps.approve(product);
                    if (!ok) return;
                    frame.close(); paintQueue(); openNext(`product:${product.id}`);
                }
            } catch (error) { notify("That did not work. " + describeError(error), "error"); }
            finally { working = false; if (document.body.contains(b)) b.disabled = false; }
        });
        paint();
    }

    // ---------- User report: a "New product" sent from the app ----------
    function openReport(r) {
        const leave = () => { frame.close(); paintQueue(); };
        const frame = popWindow("User report: new product", leave, { subtitle: "Sent by an app user. Not verified." });
        frame.box.classList.add("review-window");
        const body = make("div", "ur-body");
        body.innerHTML = `<div class="ur-grid">
                <div><div class="field-label">Barcode</div><div class="ur-value">${escapeHtml(r.barcode || "-")}</div></div>
                <div><div class="field-label">Brand and product name</div><div class="ur-value">${escapeHtml([r.brand, r.productName].filter(Boolean).join(", ") || "-")}</div></div>
            </div>
            <div class="field-label">Ingredient text</div><div class="ur-value ur-text">${escapeHtml(r.ingredientText || "-")}</div>
            <p class="small muted">Reported by a user. Not verified.${r.note ? " Note: " + escapeHtml(r.note) : ""}</p>`;
        const actions = make("div", "pw-actions");
        const closeBtn = make("button", "btn", "Close report"); closeBtn.type = "button";
        const reply = make("button", "btn", "Reply"); reply.type = "button"; reply.disabled = !r.uid;
        const importBtn = make("button", "save-btn", "Search and import this"); importBtn.type = "button";
        actions.append(closeBtn, make("span", "spacer"), reply, importBtn);
        frame.body.append(body);
        frame.foot.append(actions);
        closeBtn.addEventListener("click", async () => { closeBtn.disabled = true; await closeReport(r, false); frame.close(); paintQueue(); openNext(`report:${r.id}`); });
        reply.addEventListener("click", () => { frame.close(); openReplyDrawer({ id: r.id, uid: r.uid, category: NEW_PRODUCT, message: r.message, userName: r.userName }, { onSent: () => paintQueue() }); });
        importBtn.addEventListener("click", () => {
            frame.close();
            fallback = { brand: r.brand, name: r.productName, barcode: r.barcode, ingredientText: r.ingredientText };
            fromReport = r;
            queryBox.value = r.barcode || [r.brand, r.productName].filter(Boolean).join(" ");
            el("siPanel").scrollIntoView({ behavior: "smooth", block: "start" });
            importReportProduct();
        });
    }

    async function closeReport(r, imported) {
        try { await updateDoc(doc(db, "supportReports", r.id), { status: "handled", handledAt: serverTimestamp() }); reports = reports.filter((x) => x.id !== r.id); }
        catch (error) { notify("The report was not closed. " + describeError(error), "error"); }
        if (imported) notify("The report was imported and closed. Reply to the user from the bell if you like.", "ok");
    }

    // the "New product" reports, live
    onSnapshot(query(collection(db, "supportReports"), where("category", "==", NEW_PRODUCT), where("status", "==", "new")), (snapshot) => {
        reports = snapshot.docs.map((d) => {
            const x = d.data();
            const s = (v, n) => String(v || "").slice(0, n);
            return { id: d.id, uid: s(x.uid, 128), userName: s(x.userName, 100), message: s(x.message, 2000), barcode: s(x.barcode, 20), brand: s(x.brand, 60),
                productName: s(x.productName, 100), ingredientText: s(x.ingredientText, 4000), note: s(x.note, 500), createdAtUnix: Number(x.createdAtUnix) || 0 };
        });
        paintQueue();
        const wanted = (location.hash.match(/^#report=([\w-]+)$/) || [])[1];
        if (wanted) { const r = reports.find((x) => x.id === wanted); if (r) { history.replaceState(null, "", location.pathname + location.search); openReport(r); } }
    }, (error) => console.warn("New product reports could not be loaded:", error && error.code ? error.code : error));

    paintQueue();
    return { refresh: paintQueue };
}
