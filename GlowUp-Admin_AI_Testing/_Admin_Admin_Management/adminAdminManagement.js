// Admin Management: the same layout and the same ways of working as User Management, for the admin accounts (admins/{uid}).
// ONE live listener on the admins collection feeds the cards, the tabs, the table and the details window. Tabs, search, filters and paging are
// client-side on that snapshot. Admins are made by approving a request (login page "Sign Up" > Quick settings > Admin requests): nobody is added here.
// What an admin can do to ANOTHER admin: rename, archive (no portal access until restored), send to the Recycle Bin, restore, and delete from the bin.
// Never to themself, never to one marked `protected` in the console: firestore.rules refuse both, and this page does not offer them.
// Writes go through adminStore, which records each in auditLog.

import { auth, db } from "../_Database/firebase-console.js";
import { collection, doc, serverTimestamp } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { onSnapshot, writeBatch } from "../_Shared/adminStore.js";
import { requireAdmin, wireSignOut, setupNavigationGuard, escapeHtml, formatWhen, notify, describeError, wireSearch } from "../_Shared/adminCommon.js";
import { mountShell, placeHeaderParts, paginate, renderPager, icon, askConfirm, dialogFrame, openMenu } from "../_Shared/adminShell.js";
import { openFacets } from "../_Shared/adminFacets.js";
import { EXPORT_FILE_PREFIX, toCsv, downloadCsv, today } from "../_Shared/adminExport.js";
import { normalizeAdmin, summarize, filterAdmins, sortAdmins, statusPatch, restoreTarget, statusLabel, initialOf, editProblem, canChange as mayChange, CSV_COLUMNS, TABS } from "../_Shared/adminRules.js";

const el = (id) => document.getElementById(id);
const BATCH = 200;
const TAB_TITLES = { all: "All Admins", active: "Active Admins", archived: "Archived Admins", bin: "Recycle Bin", online: "Online Now" };
const TAB_EMPTY = {
    all: ["No admins yet.", "An admin appears here once a request for admin access is approved."],
    active: ["No active admins.", "Admins you archive or send to the Recycle Bin are not listed here."],
    archived: ["Nothing archived.", "Archived admins have no portal access. Restore them, or delete them."],
    bin: ["The Recycle Bin is empty.", "Admins you delete come here. Restore them, or delete them permanently."],
    online: ["No admins are online.", "Admins appear here while one of their pages is open (checked in within the last 5 minutes)."]
};

let admins = null;                        // null = the first answer has not come yet
let failure = null;
let stop = null;
let tab = "all";
let facets = { status: "" };
const picked = new Set();
const pager = { page: 1, size: 10 };
let viewing = null;
const myUid = () => (auth.currentUser ? auth.currentUser.uid : "");

document.addEventListener("DOMContentLoaded", async () => {
    mountShell("admins");
    placeHeaderParts();
    wireSignOut();
    setupNavigationGuard();
    try { const wanted = new URLSearchParams(location.search).get("tab"); if (TABS.some((t) => t.id === wanted)) tab = wanted; } catch (error) { }
    paintTabs();
    await requireAdmin();

    el("userKpis").addEventListener("click", (event) => { const k = event.target.closest(".kpi[data-tab]"); if (k) setTab(k.dataset.tab); });
    el("userKpis").addEventListener("keydown", (event) => { const k = event.target.closest(".kpi[data-tab]"); if (k && (event.key === "Enter" || event.key === " ")) { event.preventDefault(); setTab(k.dataset.tab); } });
    wireSearch(el("userSearchInput"), el("userSearchClear"), () => { pager.page = 1; render(); });
    el("userPageSize").addEventListener("change", (event) => { pager.size = Number(event.target.value) || 10; pager.page = 1; render(); });
    el("userFilterBtn").addEventListener("click", openFilter);
    el("userExportBtn").addEventListener("click", exportCsv);
    el("userPickAll").addEventListener("click", () => {
        const rows = pageRows().filter(canChange);
        const all = rows.length > 0 && rows.every((a) => picked.has(a.uid));
        rows.forEach((a) => (all ? picked.delete(a.uid) : picked.add(a.uid)));
        render();
    });
    el("userTableBody").addEventListener("click", onTableClick);
    el("userTableBody").addEventListener("keydown", (event) => { if (event.key === "Enter" && event.target.matches("tr[data-uid]")) openDetails(event.target.dataset.uid); });
    el("barRestore").addEventListener("click", () => restore(chosen()));
    el("barArchive").addEventListener("click", () => archive(chosen()));
    el("barBin").addEventListener("click", () => toBin(chosen()));
    el("barPurge").addEventListener("click", () => purge(chosen()));
    el("barClear").addEventListener("click", () => { picked.clear(); render(); });

    listen();
    window.addEventListener("pagehide", unlisten);
    window.addEventListener("pageshow", (event) => { if (event.persisted && !stop) listen(); });
    window.setInterval(() => { if (admins && !document.hidden) render(); }, 30000);
});

// ---------- the one listener ----------
function listen() {
    unlisten();
    failure = null;
    stop = onSnapshot(collection(db, "admins"), (snapshot) => {
        admins = snapshot.docs.map((d) => normalizeAdmin(d.id, d.data()));
        failure = null;
        const here = new Set(admins.map((a) => a.uid));
        [...picked].forEach((uid) => { if (!here.has(uid)) picked.delete(uid); });
        render();
        if (viewing) viewing.paint();
    }, (error) => {
        console.error("Error listening to admins: ", error);
        failure = error;
        render();
    });
}
function unlisten() { if (stop) { stop(); stop = null; } }

// ---------- what is shown ----------
const filtered = () => sortAdmins(filterAdmins(admins || [], { tab, query: el("userSearchInput").value, status: facets.status }));
const pageRows = () => paginate(filtered(), pager);
const byUid = (uid) => (admins || []).find((a) => a.uid === uid) || null;
const canChange = (a) => mayChange(a, myUid());
const chosen = () => (admins || []).filter((a) => picked.has(a.uid) && canChange(a));
const many = (n, one, more) => `${n} ${n === 1 ? one : more}`;

function setTab(next) {
    if (!TABS.some((t) => t.id === next) || next === tab) return;
    tab = next; picked.clear(); pager.page = 1;
    paintTabs();
    render();
}
function paintTabs() {
    document.querySelectorAll("#userKpis .kpi").forEach((k) => { const on = k.dataset.tab === tab; k.classList.toggle("on", on); k.setAttribute("aria-pressed", on ? "true" : "false"); });
    el("userTitle").textContent = TAB_TITLES[tab];
}

const avatar = (a, big) => `<span class="um-avatar${big ? " big" : ""}" aria-hidden="true">${escapeHtml(initialOf(a))}</span>`;
const statusBadgeOf = (a) => `<span class="status-badge user-${a.status}">${statusLabel(a.status)}</span>`;
const when = (t) => (t ? escapeHtml(formatWhen(t / 1000)) : '<span class="muted">Not recorded</span>');

function actionsOf(a) {
    const b = (act, ic, label, extra = "") => `<button type="button" class="icon-btn small${extra}" data-act="${act}" aria-label="${label}: ${escapeHtml(a.name)}">${icon(ic)}</button>`;
    if (a.uid === myUid()) return b("view", "eye", "View details") + '<span class="chip um-self">You</span>';
    if (a.protectedAdmin) return b("view", "eye", "View details") + '<span class="chip um-self">Protected</span>';
    if (a.status === "deleted") return b("view", "eye", "View details") + b("restore", "undo", "Restore") + b("purge", "trash", "Delete permanently", " danger");
    if (a.status === "archived") return b("view", "eye", "View details") + b("restore", "undo", "Restore") + b("bin", "trash", "Delete to Recycle Bin", " danger");
    return b("view", "eye", "View details") + b("edit", "edit", "Edit admin") + b("more", "more", "More actions");
}

function render() {
    const body = el("userTableBody");
    if (failure) {
        body.innerHTML = `<tr><td colspan="7" class="table-message error">Could not load the admins. ${escapeHtml(describeError(failure))} <button type="button" class="btn" id="userRetry">Try again</button></td></tr>`;
        el("userRetry").addEventListener("click", () => { admins = null; render(); listen(); });
        el("userEmpty").hidden = true; el("userPager").innerHTML = "";
        return;
    }
    if (admins === null) return;

    const sum = summarize(admins);
    [["All", "all"], ["Online", "online"], ["Active", "active"], ["Archived", "archived"], ["Bin", "bin"]].forEach(([name, id]) => {
        const value = el("kpi" + name + "Value");
        value.classList.remove("skeleton");
        value.textContent = String(sum[id].count);
        el("kpi" + name + "Week").textContent = sum[id].week > 0 ? `+${sum[id].week} this week` : "";
    });

    const all = filtered();
    const rows = paginate(all, pager);
    const typed = el("userSearchInput").value.trim() !== "" || facets.status;
    el("userCountLine").textContent = many(all.length, "admin", "admins") + (typed ? " match" : "");
    const filterOn = Boolean(facets.status) ? 1 : 0;
    el("userFilterBtn").classList.toggle("has-filter", filterOn > 0);
    el("userFilterBtn").querySelector(".um-btn-text").textContent = filterOn ? `Filter (${filterOn})` : "Filter";

    body.innerHTML = rows.map((a) => `
        <tr data-uid="${escapeHtml(a.uid)}" tabindex="0" class="${picked.has(a.uid) ? "is-picked" : ""}">
            <td class="pick-col">${canChange(a) ? `<button type="button" class="pick" role="checkbox" data-pick="${escapeHtml(a.uid)}" aria-checked="${picked.has(a.uid)}" aria-label="Select ${escapeHtml(a.name)}"></button>` : ""}</td>
            <td><div class="um-user">${avatar(a)}<div class="um-names"><span class="um-name">${escapeHtml(a.name)}</span><span class="um-sub">${a.approvedBy ? "Approved by " + escapeHtml(nameOf(a.approvedBy)) : "Added in the console"}</span></div></div></td>
            <td class="um-email">${escapeHtml(a.email) || '<span class="muted">No email</span>'}</td>
            <td>${statusBadgeOf(a)}</td>
            <td class="um-when">${when(a.createdAt)}</td>
            <td class="um-when">${when(a.lastActive)}</td>
            <td><div class="um-actions">${actionsOf(a)}</div></td>
        </tr>`).join("");

    const empty = rows.length === 0;
    el("userEmpty").hidden = !empty;
    el("userEmpty").closest(".panel").querySelector(".table-container").hidden = empty;
    if (empty) {
        el("userEmptyMessage").textContent = typed ? "No admins match." : TAB_EMPTY[tab][0];
        el("userEmptyHint").textContent = typed ? "Clear the search or the filter to see more." : TAB_EMPTY[tab][1];
    }
    renderPager(el("userPager"), pager, all.length, render, "admins");

    const mine = rows.filter(canChange);
    const allPicked = mine.length > 0 && mine.every((a) => picked.has(a.uid));
    const some = mine.some((a) => picked.has(a.uid));
    el("userPickAll").setAttribute("aria-checked", allPicked ? "true" : some ? "mixed" : "false");
    const sel = chosen();
    el("userBar").hidden = sel.length === 0;
    el("userBarCount").textContent = `${sel.length} selected`;
    const every = (...states) => sel.length > 0 && sel.every((a) => states.includes(a.status));
    el("barRestore").hidden = !every("archived", "deleted");
    el("barArchive").hidden = !every("active");
    el("barBin").hidden = !every("active", "archived");
    el("barPurge").hidden = !every("deleted");
}

/** The name of the admin who approved, when that admin is in the list. */
function nameOf(uid) { const a = byUid(uid); return a ? a.name : "another admin"; }

function onTableClick(event) {
    const pick = event.target.closest("[data-pick]");
    if (pick) { const uid = pick.dataset.pick; if (picked.has(uid)) picked.delete(uid); else picked.add(uid); render(); return; }
    const row = event.target.closest("tr[data-uid]");
    if (!row) return;
    const a = byUid(row.dataset.uid);
    if (!a) return;
    const button = event.target.closest("[data-act]");
    if (!button) { openDetails(a.uid); return; }
    const act = button.dataset.act;
    if (act === "view") openDetails(a.uid);
    else if (act === "edit") openEdit(a);
    else if (act === "restore") restore([a]);
    else if (act === "bin") toBin([a]);
    else if (act === "purge") purge([a]);
    else if (act === "more") openMenu(button, [
        { label: "View Details", icon: "eye", run: () => openDetails(a.uid) },
        { label: "Edit Admin", icon: "edit", run: () => openEdit(a) },
        { separator: true },
        { label: "Move to Archive", icon: "archive", run: () => archive([a]) },
        { label: "Delete to Recycle Bin", icon: "trash", danger: true, run: () => toBin([a]) }
    ]);
}

// ---------- filter and export ----------
function openFilter(event) {
    openFacets(event.currentTarget, {
        title: "Filters",
        fields: [
            { id: "status", label: "Status", kind: "select", anyLabel: "Any status", options: [{ value: "active", label: "Active" }, { value: "archived", label: "Archived" }], value: facets.status }
        ],
        onApply: (values) => { facets = { status: values.status || "" }; pager.page = 1; render(); }
    });
}
function exportCsv() {
    if (admins === null) return;
    const rows = filtered();
    if (rows.length === 0) { notify("Nothing to export: no admin is shown.", "info"); return; }
    downloadCsv(`${EXPORT_FILE_PREFIX}-admins-${today()}.csv`, toCsv(CSV_COLUMNS, rows, rows.length));
    notify(`Exported ${many(rows.length, "admin", "admins")}.`, "ok");
}

// ---------- changes (always after a confirmed question) ----------
async function commit(list, make, done) {
    const me = myUid();
    try {
        for (let i = 0; i < list.length; i += BATCH) {
            const batch = writeBatch(db);
            list.slice(i, i + BATCH).forEach((a) => make(batch, a, me));
            await batch.commit();
        }
        picked.clear();
        notify(done, "ok");
    } catch (error) {
        console.error("Admin change failed: ", error);
        notify("Not saved. " + describeError(error), "error");
    }
}
const names = (list) => list.map((a) => a.name);
const setStatus = (list, to, done) => commit(list, (batch, a, me) => batch.update(doc(db, "admins", a.uid), statusPatch(a, to, serverTimestamp(), me)), done);

async function archive(list) {
    if (!list.length) return;
    const yes = await askConfirm({ title: "Archive Admin", message: `${many(list.length, "admin is", "admins are")} kept but marked Archived: no access to this portal until restored.`, items: names(list), confirmLabel: "Move to Archive", badgeIcon: "archive" });
    if (yes) await setStatus(list, "archived", `${many(list.length, "admin", "admins")} archived.`);
}
async function toBin(list) {
    if (!list.length) return;
    const yes = await askConfirm({ title: "Move to Recycle Bin", message: `${many(list.length, "admin is", "admins are")} moved to the Recycle Bin and lose access to this portal. They can be restored from there.`, items: names(list), confirmLabel: "Delete to Recycle Bin", danger: true, badgeIcon: "trash" });
    if (yes) await setStatus(list, "deleted", `${many(list.length, "admin", "admins")} moved to the Recycle Bin.`);
}
async function restore(list) {
    if (!list.length) return;
    const yes = await askConfirm({ title: "Restore Admin", message: `${many(list.length, "admin returns", "admins return")} to the list they came from, with the access that list gives.`, items: names(list), confirmLabel: "Restore", badgeIcon: "undo" });
    if (!yes) return;
    await commit(list, (batch, a, me) => batch.update(doc(db, "admins", a.uid), statusPatch(a, restoreTarget(a), serverTimestamp(), me)), `${many(list.length, "admin", "admins")} restored.`);
}
async function purge(list) {
    if (!list.length) return;
    const yes = await askConfirm({
        title: "Delete Permanently",
        message: `${many(list.length, "admin record is", "admin records are")} deleted for good. The person's sign-in account is not removed: they can ask for admin access again on the login page.`,
        items: names(list), confirmLabel: "Delete permanently", danger: true, badgeIcon: "trash"
    });
    if (!yes) return;
    await commit(list, (batch, a) => batch.delete(doc(db, "admins", a.uid)), `${many(list.length, "admin", "admins")} deleted permanently.`);
}

// ---------- dialogs ----------
const FOCUSABLE = 'a[href], button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])';
/** Keeps Tab inside the dialog and gives focus back to what opened it. */
function trapFocus(frame, opener) {
    const onKey = (event) => {
        if (event.key !== "Tab") return;
        const items = [...frame.box.querySelectorAll(FOCUSABLE)].filter((n) => n.offsetParent !== null);
        if (items.length === 0) return;
        const first = items[0], last = items[items.length - 1];
        if (!frame.box.contains(document.activeElement)) { event.preventDefault(); first.focus(); }
        else if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };
    document.addEventListener("keydown", onKey, true);
    return () => { document.removeEventListener("keydown", onKey, true); if (opener && document.contains(opener)) opener.focus({ preventScroll: true }); };
}

function openDetails(uid) {
    if (viewing) return;
    const opener = document.activeElement;
    let release = () => { };
    const close = () => { if (!viewing) return; viewing = null; release(); frame.close(); };
    const frame = dialogFrame("Admin details", close);
    frame.box.classList.add("um-dialog");
    frame.box.setAttribute("aria-labelledby", "umDetailsTitle");
    frame.box.querySelector("h3").id = "umDetailsTitle";
    const content = document.createElement("div");
    const actions = document.createElement("div");
    actions.className = "modal-actions";
    const closeBtn = document.createElement("button");
    closeBtn.type = "button"; closeBtn.className = "btn primary"; closeBtn.textContent = "Close";
    closeBtn.addEventListener("click", close);
    actions.appendChild(closeBtn);
    frame.box.append(content, actions);
    release = trapFocus(frame, opener);

    const paint = () => {
        const a = byUid(uid);
        if (!a) { content.innerHTML = '<p class="um-none">This admin no longer exists.</p>'; return; }
        const fact = (k, v) => `<dt>${k}</dt><dd>${v}</dd>`;
        content.innerHTML = `
            <section class="um-section"><h4>Account info</h4>
                <div class="um-person">${avatar(a, true)}<div class="um-names"><span class="um-name">${escapeHtml(a.name)}</span><span class="um-sub">${a.uid === myUid() ? "This is you" : a.protectedAdmin ? "Protected: changed only in the Firebase console" : "Admin"}</span></div></div>
                <dl class="um-facts">
                    ${fact("Email", escapeHtml(a.email) || "No email")}
                    ${fact("UID", escapeHtml(a.uid))}
                    ${fact("Status", statusBadgeOf(a))}
                    ${fact("Approved by", a.approvedBy ? escapeHtml(nameOf(a.approvedBy)) : '<span class="muted">Added in the console</span>')}
                    ${fact("Approved at", when(a.createdAt))}
                    ${fact("Last active", when(a.lastActive))}
                    ${a.changedAt ? fact("Status changed", when(a.changedAt)) : ""}
                </dl></section>`;
    };
    viewing = { uid, paint, frame };
    paint();
    closeBtn.focus();
}

function openEdit(a) {
    if (viewing) return;
    const opener = document.activeElement;
    let sending = false, release = () => { };
    const dirty = () => input.name.value !== a.name || input.status.value !== (a.status === "deleted" ? "active" : a.status);
    const leave = async () => {
        if (sending) return;
        if (dirty() && !(await askConfirm({ title: "Discard changes?", message: "What you changed will be lost.", cancelLabel: "Keep editing", confirmLabel: "Discard", danger: true }))) return;
        release(); frame.close();
    };
    const frame = dialogFrame("Edit admin", leave);
    frame.box.classList.add("um-dialog");
    const form = document.createElement("form");
    form.className = "um-form";
    form.noValidate = true;
    const field = (label, control, id) => { const wrap = document.createElement("div"); wrap.className = "field"; const l = document.createElement("label"); l.htmlFor = id; l.textContent = label; control.id = id; wrap.append(l, control); return wrap; };
    const text = (value, max) => { const i = document.createElement("input"); i.type = "text"; i.value = value; i.maxLength = max; i.autocomplete = "off"; return i; };
    const select = (options, value) => { const s = document.createElement("select"); options.forEach(([v, l]) => { const o = document.createElement("option"); o.value = v; o.textContent = l; s.appendChild(o); }); s.value = value; return s; };
    const input = {
        name: text(a.name, 80),
        email: Object.assign(text(a.email, 254), { readOnly: true }),
        status: select([["active", "Active"], ["archived", "Archived (no portal access)"]], a.status === "deleted" ? "active" : a.status)
    };
    const hint = (t) => { const p = document.createElement("small"); p.className = "field-hint"; p.textContent = t; return p; };
    const emailField = field("Email", input.email, "umEmail"); emailField.append(hint("The email cannot be changed here."));
    const problem = document.createElement("p");
    problem.className = "form-error"; problem.setAttribute("role", "alert");
    const bar = document.createElement("div");
    bar.className = "modal-actions";
    const cancel = document.createElement("button"); cancel.type = "button"; cancel.className = "cancel-btn"; cancel.textContent = "Cancel";
    const save = document.createElement("button"); save.type = "submit"; save.className = "save-btn"; save.textContent = "Save changes";
    bar.append(cancel, save);
    form.append(field("Full Name", input.name, "umName"), emailField, field("Status", input.status, "umStatus"), problem, bar);
    frame.box.appendChild(form);
    release = trapFocus(frame, opener);
    const show = (t, which) => { problem.textContent = t || ""; problem.style.display = t ? "block" : "none"; Object.values(input).forEach((i) => i.removeAttribute("aria-invalid")); if (which && input[which]) { input[which].setAttribute("aria-invalid", "true"); input[which].focus(); } };
    form.addEventListener("input", () => show(""));
    cancel.addEventListener("click", leave);
    form.addEventListener("submit", async (event) => {
        event.preventDefault();
        if (sending) return;
        const values = { name: input.name.value, status: input.status.value };
        const bad = editProblem(values);
        if (bad) { show(bad.text, bad.field); return; }
        if (!dirty()) { release(); frame.close(); return; }
        // archiving from the edit form asks first, like Archive and the Recycle Bin
        if (values.status === "archived" && a.status !== "archived" && !(await askConfirm({ title: "Archive Admin", message: "This admin has no access to this portal until restored.", items: [a.name], confirmLabel: "Archive", danger: true }))) return;
        sending = true;
        save.disabled = true; cancel.disabled = true;
        save.innerHTML = '<span class="btn-spin" aria-hidden="true"></span>Saving...';
        try {
            const patch = { name: values.name.replace(/\s+/g, " ").trim() };
            if (values.status !== a.status) Object.assign(patch, statusPatch(a, values.status, serverTimestamp(), myUid()));
            const batch = writeBatch(db);
            batch.update(doc(db, "admins", a.uid), patch);
            await batch.commit();
            release(); frame.close();
            notify("Changes saved.", "ok");
        } catch (error) {
            console.error("Edit admin failed: ", error);
            show("Not saved. " + describeError(error));
            sending = false; save.disabled = false; cancel.disabled = false; save.textContent = "Save changes";
        }
    });
    input.name.focus();
}
