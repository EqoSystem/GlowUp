// Search and import + Review queue + Product review + User report (redesign plan, 27 November 2026).
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
import { openPhoneLink } from "../_Shared/phoneLink.js";
import { pageView } from "../_Shared/planViews.js";
import { lookupIngredient, lookupHazards } from "../_Shared/quickFill.js";
import { suggestFit } from "../_Shared/ingredientFit.js";
import { planFit } from "../_Shared/fitReasons.js";
import { pubchemPage } from "../_Shared/smartAdd.js";
import { runImport, reviewRows, countResults, STEPS, RESULT_WORDS, FLAGGED, NO_RULE, NOT_IDENTIFIED } from "../_Shared/importChain.js";

export const NEW_PRODUCT = "New product";
const el = (id) => document.getElementById(id);
const make = (tag, className, text) => { const n = document.createElement(tag); if (className) n.className = className; if (text != null) n.textContent = text; return n; };
const TONE = { [FLAGGED]: "warn", [NO_RULE]: "", [NOT_IDENTIFIED]: "bad" };
const lineUrl = (line) => { const t = String(line || ""); const u = t.slice(t.lastIndexOf("|") + 1).trim(); return /^https:\/\//i.test(u) ? u : ""; };
const lineTitle = (line) => { const t = String(line || ""); const at = t.lastIndexOf("|"); return (at > 0 ? t.slice(0, at) : t).trim(); };
const hostOf = (url) => { try { return new URL(url).hostname.replace(/^www\./, ""); } catch (error) { return url; } };

let refs;
async function loadRefs() {
    if (refs !== undefined) return refs;
    try { const r = await fetch(new URL("../_Shared/data/ingredientRefs.json", import.meta.url).href, { credentials: "omit" }); refs = r.ok ? await r.json() : null; }
    catch (error) { refs = null; }
    return refs;
}

/** A new Curator entry for a name no entry knows: PubChem names + hazard codes and GlowUp's name rules. Never sets a risk level. */
async function draftIngredient(name) {
    const table = await loadRefs();
    const found = await lookupIngredient(name).catch(() => null);
    const key = (t) => String(t || "").toLowerCase().replace(/[^a-z0-9]+/g, "");
    const aliases = (found ? found.aliases : []).filter((a) => key(a) !== key(name)).slice(0, 6);
    const page = found && found.cid ? pubchemPage(found.cid) : "";
    const fit = suggestFit([name].concat(aliases), table);
    const hazards = found && found.cid ? await lookupHazards(found.cid).catch(() => null) : null;
    const plan = planFit({ rules: fit, hazards });
    const sources = Array.from(new Set([page ? "PubChem | " + page : ""].concat(plan.sources).filter(Boolean))).slice(0, 10);
    return {
        name: String(name).replace(/\s+/g, " ").trim().slice(0, 80), aliases, identified: !!page || fit.rules.length > 0,
        allergenTags: plan.allergenTags, avoidForSkinTypes: plan.avoidForSkinTypes, sources,
        cautionNote: "", fitWhy: fit.why.join(" ").slice(0, 400), chemical: found ? String(found.chemical || "").slice(0, 600) : "", page
    };
}

/**
 * deps (from the inventory page):
 *   lookup(text)              the page's product look-up (quickFill.lookupProduct)
 *   library()                 Curator entries { id, name, techName, aliases, allergenTags, avoidForSkinTypes, riskLevel, sources, identified }
 *   pending()                 products waiting for review (not in the bin)
 *   saveRecord(record)        saves one product as Pending (the file-import path); resolves with a summary text
 *   findSaved(product)        the saved product for a plan's product (after saveRecord), or null
 *   approve(product)          approves one product (asks first); resolves true when it was approved
 *   toBin(product)            moves one product to the Recycle bin
 *   openProduct(product)      opens the product form (to add a list by hand)
 *   openBatch()               the existing Many barcodes drawer (file import sits inside the panel)
 *   reload()                  reads the lists again
 */
export function mountSearchImport(deps) {
    const queryBox = el("siQuery"), run = el("siRun"), phone = el("siPhone"), progress = el("siProgress");
    const tabs = Array.from(document.querySelectorAll("[data-si-tab]"));
    let busy = false, abort = null, fallback = null, fromReport = null, plan = null;
    const queueState = { page: 1, size: 10 };
    let reports = [];

    // ---------- the three ways to import ----------
    const fileBox = el("siFile"), searchRow = queryBox.parentElement;
    const showMode = (mode) => {
        tabs.forEach((t) => t.setAttribute("aria-selected", String(t.dataset.siTab === mode)));
        fileBox.hidden = mode !== "file"; searchRow.hidden = mode === "file";
        if (mode === "one") queryBox.focus();
    };
    // a file picked from anywhere (the tab, the + menu, a shortcut) shows its progress under "From a file"
    el("inventoryFileInput").addEventListener("change", () => showMode("file"));
    tabs.forEach((tab) => tab.addEventListener("click", () => {
        if (busy) return;
        if (tab.dataset.siTab === "many") { deps.openBatch(); return; }
        showMode(tab.dataset.siTab);
    }));
    phone.addEventListener("click", () => openPhoneLink({ title: "Scan with your phone", what: "barcode", onValue: (value) => { queryBox.value = String(value || "").trim(); queryBox.focus(); } }));
    queryBox.addEventListener("keydown", (event) => { if (event.key === "Enter") { event.preventDefault(); start(); } });
    run.addEventListener("click", start);

    const lock = (on) => { busy = on; run.disabled = on; queryBox.readOnly = on; phone.disabled = on; tabs.forEach((t) => { t.disabled = on; }); };

    function paintSteps(at, done) {
        const bar = progress.querySelector(".si-steps");
        bar.innerHTML = STEPS.map((words, i) => {
            const state = done || i < at ? "done" : i === at ? "now" : "";
            return `<li class="${state}"><span class="si-step-mark">${state === "done" ? icon("check") : i + 1}</span>${escapeHtml(words)}</li>`;
        }).join("");
    }

    async function start() {
        if (busy) return;
        const typed = queryBox.value.trim();
        if (!typed && !(fallback && (fallback.name || fallback.barcode))) { queryBox.focus(); notify("Type a brand, a product name or a barcode first.", "info"); return; }
        lock(true); plan = null;
        progress.hidden = false;
        const card = progress.querySelector(".si-found"), cancel = progress.querySelector(".si-cancel"), open = progress.querySelector(".si-open"), say = progress.querySelector(".si-say");
        card.innerHTML = ""; say.textContent = ""; open.disabled = true; cancel.disabled = false; cancel.hidden = false;
        paintSteps(0, false);
        abort = new AbortController();
        try {
            plan = await runImport(typed, {
                lookupProduct: (text) => deps.lookup(text),
                library: deps.library(),
                draftIngredient,
                fallback: fallback || {}
            }, (at) => paintSteps(at, false), abort.signal);
            cancel.disabled = true;
            paintFound(card, plan);
            say.textContent = "Saving as Pending...";
            await savePlan(plan);
            paintSteps(STEPS.length - 1, true);
            say.textContent = plan.names.length ? "Saved as Pending. Open the review to check it." : "Saved as Pending without an ingredient list: add it from the pack or the brand's page.";
            open.disabled = false;
            cancel.hidden = true;
            if (fromReport) { await closeReport(fromReport, true); fromReport = null; }
            fallback = null;
        } catch (error) {
            say.textContent = error && error.stopped ? "Cancelled. Nothing was saved." : error && error.notFound ? "No product was found for that search. Check the spelling or the barcode, or try the brand and the name." : "The import stopped. " + describeError(error);
            cancel.hidden = true;
        } finally { abort = null; lock(false); }
    }
    progress.querySelector(".si-cancel").addEventListener("click", () => { if (abort) abort.abort(); });
    progress.querySelector(".si-open").addEventListener("click", () => {
        const saved = plan ? deps.findSaved(plan.product) : null;
        if (saved) openReview(saved); else notify("The saved product is not in the list yet. It appears in the Review queue in a moment.", "info");
    });

    function paintFound(card, p) {
        const from = [p.product.foundBy ? `Brand and name: ${p.product.foundBy}.` : "", p.listFrom ? `Ingredient list: ${p.listFrom}.` : "Ingredient list: none found."].filter(Boolean).join(" ");
        card.innerHTML = `<div class="si-found-main"><strong>${escapeHtml([p.product.brand, p.product.name].filter(Boolean).join(", "))}</strong><span class="small muted">${escapeHtml(from)}</span></div>
            <div class="si-found-counts small"><span>${p.names.length} ingredients</span><span>${p.known} already in the Curator</span><span>${p.fresh} new</span></div>`;
    }

    async function savePlan(p) {
        const product = p.product;
        const record = {
            brandName: product.brand, productName: product.name, cosmeticType: product.cosmeticType, ingredients: p.names.slice(0, 300),
            barcode: product.barcode, sources: product.sources, imageUrl: "", description: product.note || "", shades: [],
            lookupFrom: "Search and import" + (product.foundBy ? `: ${product.foundBy}` : "")
        };
        if (p.listFrom && /user report/i.test(p.listFrom)) record.description = ("Ingredient list typed in a user report: not verified. " + record.description).trim();
        await deps.saveRecord(record);
        // the new ingredient names go to the Curator now, as Pending: rules filled the cautions, an admin checks them
        const uid = auth.currentUser ? auth.currentUser.uid : "";
        for (const d of p.drafts) {
            if (!d.name || d.name.length < 2) continue;
            const data = {
                name: d.name, techName: "", aliases: d.aliases || [], classificationId: "", purpose: "", description: "", chemicalDescription: d.chemical || "",
                riskLevel: "", cautionNote: "", commonUses: "", minAge: 0, allergenTags: d.allergenTags || [], avoidForSkinTypes: d.avoidForSkinTypes || [],
                avoidForConditions: [], goodForSkinTypes: [], goodForConditions: [], sources: d.sources || [], folder: "", source: "import", via: "search-import",
                reviewed: false, identified: d.identified !== false, fitAuto: (d.allergenTags || []).length + (d.avoidForSkinTypes || []).length > 0, fitAutoWhy: d.fitWhy || "",
                detailsAutoChecked: false, addedBy: uid, createdAt: serverTimestamp(), updatedAt: serverTimestamp()
            };
            if (d.page) data.aliasesSourceUrl = d.page;
            try { await rawAddDoc(collection(db, "ingredients"), data); } catch (error) { console.warn("A new ingredient entry was not saved:", error && error.code ? error.code : error); }
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
        const count = el("rqCount"), next = el("rqNext");
        if (count) count.textContent = items.length ? String(items.length) : "";
        if (next) next.disabled = items.length === 0;
        if (!items.length) { put(body, `<tr><td colspan="6" class="table-message">No pending items.</td></tr>`); renderPager(el("rqPager"), queueState, 0, paintQueue, "items"); return; }
        put(body, paginate(items, queueState).map((item, at) => {
            const key = `${item.kind}:${item.kind === "product" ? item.product.id : item.report.id}`;
            if (item.kind === "report") {
                const r = item.report;
                return `<tr data-rq="${escapeHtml(key)}"><td><strong>User report: new product</strong><div class="small muted">${escapeHtml([r.brand, r.productName].filter(Boolean).join(", ") || (r.barcode ? "Barcode " + r.barcode : ""))}</div></td>
                    <td>-</td><td>-</td><td>-</td><td><span class="chip warn">From a user</span></td><td class="actions"><button type="button" class="btn" data-rq-open="${escapeHtml(key)}">Open report</button></td></tr>`;
            }
            const p = item.product, c = item.counts;
            if (!p.ingredients.length) return `<tr data-rq="${escapeHtml(key)}"><td><strong>${escapeHtml(p.name)}</strong><div class="small muted">${escapeHtml(p.brandName)}</div></td>
                <td>0</td><td>-</td><td>-</td><td><span class="chip bad">No list found</span></td><td class="actions"><button type="button" class="btn" data-rq-add="${escapeHtml(key)}">Add the list</button></td></tr>`;
            return `<tr data-rq="${escapeHtml(key)}"><td><strong>${escapeHtml(p.name)}</strong><div class="small muted">${escapeHtml(p.brandName)}</div></td>
                <td>${c.all}</td><td><span class="chip warn">${c[FLAGGED]}</span></td><td><span class="chip">${c[NO_RULE]}</span></td><td><span class="chip ${c[NOT_IDENTIFIED] ? "bad" : ""}">${c[NOT_IDENTIFIED]}</span></td>
                <td class="actions"><button type="button" class="btn" data-rq-open="${escapeHtml(key)}">Review</button></td></tr>`;
        }).join(""));
        renderPager(el("rqPager"), queueState, items.length, paintQueue, "items");
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
    el("rqNext").addEventListener("click", () => openNext(null));

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
        const frame = pageView("Product review", leave);
        frame.box.classList.add("review-window");
        const head = frame.box.querySelector("h3");
        const top = make("div", "rv-head");
        const back = make("button", "btn"); back.type = "button"; back.innerHTML = `${icon("left")}Queue`;
        back.addEventListener("click", leave);
        head.textContent = product.name;
        top.append(back, head, make("span", "small muted", product.brandName), make("span", "chip warn", "Pending"));
        const sources = make("p", "rv-sources small");
        const links = (product.sources || []).map((s) => lineUrl(s) ? `<a href="${escapeHtml(lineUrl(s))}" target="_blank" rel="noopener noreferrer">${escapeHtml(lineTitle(s) && lineTitle(s) !== lineUrl(s) ? lineTitle(s) : hostOf(lineUrl(s)))} ↗</a>` : "").filter(Boolean);
        sources.innerHTML = "Sources: " + (links.length ? links.join(" &nbsp; ") : `<span class="muted">none saved</span>`);
        const chips = make("div", "rv-filter"); chips.setAttribute("role", "tablist");
        const tableWrap = make("div", "table-container");
        const foot = make("div", "rv-foot");
        const headCard = make("div", "panel"), bodyCard = make("div", "panel"), footCard = make("div", "panel");
        headCard.append(top, sources); bodyCard.append(chips, tableWrap); footCard.append(foot);
        frame.box.replaceChildren(headCard, bodyCard, footCard);

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
                <div class="modal-actions"><button type="button" class="btn" data-rv="bin">${icon("trash")}Send to Recycle Bin</button><button type="button" class="btn" data-rv="later">Save for later</button><button type="button" class="save-btn" data-rv="approve">Approve all</button></div>`;
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
                if (b.dataset.rv === "later") { await saveNames(); frame.close(); paintQueue(); notify("Saved for later. It stays in the Review queue.", "ok"); return; }
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
        const frame = pageView("User report: new product", leave);
        frame.box.classList.add("review-window");
        const body = make("div", "ur-body");
        body.innerHTML = `<div class="ur-grid">
                <div><div class="field-label">Barcode</div><div class="ur-value">${escapeHtml(r.barcode || "-")}</div></div>
                <div><div class="field-label">Brand and product name</div><div class="ur-value">${escapeHtml([r.brand, r.productName].filter(Boolean).join(", ") || "-")}</div></div>
            </div>
            <div class="field-label">Ingredient text</div><div class="ur-value ur-text">${escapeHtml(r.ingredientText || "-")}</div>
            <p class="small muted">Reported by a user. Not verified.${r.note ? " Note: " + escapeHtml(r.note) : ""}</p>`;
        const actions = make("div", "modal-actions");
        const closeBtn = make("button", "btn", "Close report"); closeBtn.type = "button";
        const reply = make("button", "btn", "Reply"); reply.type = "button"; reply.disabled = !r.uid;
        const importBtn = make("button", "save-btn", "Search and import this"); importBtn.type = "button";
        actions.append(closeBtn, reply, make("span", "spacer"), importBtn);
        const headCard = make("div", "panel"), bodyCard = make("div", "panel"), footCard = make("div", "panel");
        const top = make("div", "rv-head");
        const back = make("button", "btn"); back.type = "button"; back.innerHTML = `${icon("left")}Queue`;
        back.addEventListener("click", leave);
        top.append(back, frame.box.querySelector("h3"), make("span", "chip warn", "New"));
        headCard.append(top); bodyCard.append(body); footCard.append(actions);
        frame.box.replaceChildren(headCard, bodyCard, footCard);
        closeBtn.addEventListener("click", async () => { closeBtn.disabled = true; await closeReport(r, false); frame.close(); paintQueue(); openNext(`report:${r.id}`); });
        reply.addEventListener("click", () => { frame.close(); openReplyDrawer({ id: r.id, uid: r.uid, category: NEW_PRODUCT, message: r.message, userName: r.userName }, { onSent: () => paintQueue() }); });
        importBtn.addEventListener("click", () => {
            frame.close();
            fallback = { brand: r.brand, name: r.productName, barcode: r.barcode, ingredientText: r.ingredientText };
            fromReport = r;
            queryBox.value = r.barcode || [r.brand, r.productName].filter(Boolean).join(" ");
            el("siPanel").scrollIntoView({ behavior: "smooth", block: "start" });
            start();
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
