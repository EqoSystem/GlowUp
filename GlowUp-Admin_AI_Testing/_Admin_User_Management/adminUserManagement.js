// User Management. ONE live listener on the users collection feeds everything on the page: the four cards, the
// tab counts, the table, and the details window. Tabs, search, filters and paging are client-side on that snapshot (no extra reads).
// The details window is drawn from the same snapshot, so it follows live changes with no second listener.
// Users are made by the app (sign-up); an admin cannot add one here. Writes go through adminStore, which records each in auditLog.

import { auth, db } from "../_Database/firebase-console.js";
import { collection, doc, serverTimestamp } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { onSnapshot, writeBatch } from "../_Shared/adminStore.js";
import { requireAdmin, wireSignOut, setupNavigationGuard, escapeHtml, formatWhen, notify, describeError, wireSearch } from "../_Shared/adminCommon.js";
import { mountShell, placeHeaderParts, paginate, renderPager, icon, askConfirm, dialogFrame, openMenu } from "../_Shared/adminShell.js";
import { openFacets } from "../_Shared/adminFacets.js";
import { EXPORT_FILE_PREFIX, toCsv, downloadCsv, today } from "../_Shared/adminExport.js";
import { normalizeUser, summarize, filterUsers, sortUsers, statusPatch, restoreTarget, statusLabel, roleLabel, initialOf, editProblem, CSV_COLUMNS, TABS } from "../_Shared/userRules.js";

const el = (id) => document.getElementById(id);
const BATCH = 200;                       // a batch also carries one audit record of up to 450 items
const TAB_TITLES = { all: "All Users", active: "Active Users", archived: "Archived Users", bin: "Recycle Bin", online: "Online Now" };
const TAB_EMPTY = {
    all: ["No users yet.", "People who sign up in the app appear here by themselves."],
    active: ["No active users.", "Users you archive or send to the Recycle Bin are not listed here."],
    archived: ["Nothing archived.", "Archived users are kept here until you restore or delete them."],
    bin: ["The Recycle Bin is empty.", "Users you delete come here. Restore them, or delete them permanently."],
    online: ["No users are online.", "Users appear here while the app has checked in within the last 5 minutes."]
};

let users = null;                         // null = the first answer has not come yet
let failure = null;
let stop = null;
let tab = "all";
let facets = { role: "", status: "" };
const picked = new Set();
const pager = { page: 1, size: 10 };
let viewing = null;                       // { uid, paint, frame }
const myUid = () => (auth.currentUser ? auth.currentUser.uid : "");

document.addEventListener("DOMContentLoaded", async () => {
    mountShell("users");
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
        const all = rows.length > 0 && rows.every((u) => picked.has(u.uid));
        rows.forEach((u) => (all ? picked.delete(u.uid) : picked.add(u.uid)));
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
    // the live listener ends with the page; it starts again when the page comes back from the back/forward cache
    window.addEventListener("pagehide", unlisten);
    window.addEventListener("pageshow", (event) => { if (event.persisted && !stop) listen(); });
    window.setInterval(() => { if (users && !document.hidden) render(); }, 30000);
});

// ---------- the one listener ----------
function listen() {
    unlisten();
    failure = null;
    stop = onSnapshot(collection(db, "users"), (snapshot) => {
        users = snapshot.docs.map((d) => normalizeUser(d.id, d.data()));
        failure = null;
        const here = new Set(users.map((u) => u.uid));
        [...picked].forEach((uid) => { if (!here.has(uid)) picked.delete(uid); });
        render();
        if (viewing) viewing.paint();
    }, (error) => {
        console.error("Error listening to users: ", error);
        failure = error;
        render();
    });
}
function unlisten() { if (stop) { stop(); stop = null; } }

// ---------- what is shown ----------
const filtered = () => sortUsers(filterUsers(users || [], { tab, query: el("userSearchInput").value, role: facets.role, status: facets.status }));
const pageRows = () => paginate(filtered(), pager);
const byUid = (uid) => (users || []).find((u) => u.uid === uid) || null;
const canChange = (u) => u.uid !== myUid();
const chosen = () => (users || []).filter((u) => picked.has(u.uid) && canChange(u));
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

const avatar = (u, big) => `<span class="um-avatar${big ? " big" : ""}" aria-hidden="true">${u.photoURL ? `<img src="${escapeHtml(u.photoURL)}" alt="" referrerpolicy="no-referrer" loading="lazy">` : escapeHtml(initialOf(u))}</span>`;
const roleBadge = (u) => `<span class="status-badge role-${u.role}">${roleLabel(u.role)}</span>`;
const statusBadgeOf = (u) => `<span class="status-badge user-${u.status}">${statusLabel(u.status)}</span>`;
const when = (ms) => (ms ? escapeHtml(formatWhen(ms / 1000)) : '<span class="muted">Not recorded</span>');

function actionsOf(u) {
    const b = (act, ic, label, extra = "") => `<button type="button" class="icon-btn small${extra}" data-act="${act}" aria-label="${label}: ${escapeHtml(u.name)}">${icon(ic)}</button>`;
    if (!canChange(u)) return b("view", "eye", "View details") + '<span class="chip um-self">You</span>';
    if (u.status === "deleted") return b("view", "eye", "View details") + b("restore", "undo", "Restore") + b("purge", "trash", "Delete permanently", " danger");
    if (u.status === "archived") return b("view", "eye", "View details") + b("restore", "undo", "Restore") + b("bin", "trash", "Delete to Recycle Bin", " danger");
    return b("view", "eye", "View details") + b("edit", "edit", "Edit user") + (u.status === "banned" ? b("unban", "check", "Unban") : "") + b("more", "more", "More actions");
}

function render() {
    const body = el("userTableBody");
    if (failure) {
        body.innerHTML = `<tr><td colspan="8" class="table-message error">Could not load the users. ${escapeHtml(describeError(failure))} <button type="button" class="btn" id="userRetry">Try again</button></td></tr>`;
        el("userRetry").addEventListener("click", () => { users = null; render(); listen(); });
        el("userEmpty").hidden = true; el("userPager").innerHTML = "";
        return;
    }
    if (users === null) return;                                  // the page's own skeleton rows stay until the first answer

    const sum = summarize(users);
    [["All", "all"], ["Online", "online"], ["Active", "active"], ["Archived", "archived"], ["Bin", "bin"]].forEach(([name, id]) => {
        const value = el("kpi" + name + "Value");
        value.classList.remove("skeleton");
        value.textContent = String(sum[id].count);
        el("kpi" + name + "Week").textContent = sum[id].week > 0 ? `+${sum[id].week} this week` : "";
    });

    const all = filtered();
    const rows = paginate(all, pager);
    const typed = el("userSearchInput").value.trim() !== "" || facets.role || facets.status;
    el("userCountLine").textContent = many(all.length, "user", "users") + (typed ? " match" : "");
    const filterOn = Boolean(facets.role) + Boolean(facets.status);
    el("userFilterBtn").classList.toggle("has-filter", filterOn > 0);
    el("userFilterBtn").querySelector(".um-btn-text").textContent = filterOn ? `Filter (${filterOn})` : "Filter";

    body.innerHTML = rows.map((u) => `
        <tr data-uid="${escapeHtml(u.uid)}" tabindex="0" class="${picked.has(u.uid) ? "is-picked" : ""}">
            <td class="pick-col">${canChange(u) ? `<button type="button" class="pick" role="checkbox" data-pick="${escapeHtml(u.uid)}" aria-checked="${picked.has(u.uid)}" aria-label="Select ${escapeHtml(u.name)}"></button>` : ""}</td>
            <td><div class="um-user">${avatar(u)}<div class="um-names"><span class="um-name">${u.username ? "@" + escapeHtml(u.username) : escapeHtml(u.name)}</span><span class="um-sub">${u.username ? escapeHtml(u.name) : "No username yet"}</span></div></div></td>
            <td class="um-email">${escapeHtml(u.email) || '<span class="muted">No email</span>'}</td>
            <td>${roleBadge(u)}</td>
            <td>${statusBadgeOf(u)}</td>
            <td class="um-when">${when(u.createdAt)}</td>
            <td class="um-when">${when(u.lastActive)}</td>
            <td><div class="um-actions">${actionsOf(u)}</div></td>
        </tr>`).join("");

    const empty = rows.length === 0;
    el("userEmpty").hidden = !empty;
    el("userEmpty").closest(".panel").querySelector(".table-container").hidden = empty;
    if (empty) {
        el("userEmptyMessage").textContent = typed ? "No users match." : TAB_EMPTY[tab][0];
        el("userEmptyHint").textContent = typed ? "Clear the search or the filter to see more." : TAB_EMPTY[tab][1];
    }
    renderPager(el("userPager"), pager, all.length, render, "users");

    const mine = rows.filter(canChange);
    const allPicked = mine.length > 0 && mine.every((u) => picked.has(u.uid));
    const some = mine.some((u) => picked.has(u.uid));
    el("userPickAll").setAttribute("aria-checked", allPicked ? "true" : some ? "mixed" : "false");
    const sel = chosen();
    el("userBar").hidden = sel.length === 0;
    el("userBarCount").textContent = `${sel.length} selected`;
    const every = (...states) => sel.length > 0 && sel.every((u) => states.includes(u.status));
    el("barRestore").hidden = !every("archived", "deleted");
    el("barArchive").hidden = !every("active", "banned");
    el("barBin").hidden = !every("active", "banned", "archived");
    el("barPurge").hidden = !every("deleted");
}

function onTableClick(event) {
    const pick = event.target.closest("[data-pick]");
    if (pick) { const uid = pick.dataset.pick; if (picked.has(uid)) picked.delete(uid); else picked.add(uid); render(); return; }
    const row = event.target.closest("tr[data-uid]");
    if (!row) return;
    const u = byUid(row.dataset.uid);
    if (!u) return;
    const button = event.target.closest("[data-act]");
    if (!button) { openDetails(u.uid); return; }
    const act = button.dataset.act;
    if (act === "view") openDetails(u.uid);
    else if (act === "edit") openEdit(u);
    else if (act === "restore") restore([u]);
    else if (act === "bin") toBin([u]);
    else if (act === "purge") purge([u]);
    else if (act === "unban") unban([u]);
    else if (act === "more") openMenu(button, [
        { label: "View Details", icon: "eye", run: () => openDetails(u.uid) },
        { label: "Edit User", icon: "edit", run: () => openEdit(u) },
        { separator: true },
        ...(u.status === "active" || u.status === "banned" ? [{ label: "Move to Archive", icon: "archive", run: () => archive([u]) }] : []),
        { label: "Delete to Recycle Bin", icon: "trash", danger: true, run: () => toBin([u]) }
    ]);
}

// ---------- filter and export ----------
function openFilter(event) {
    openFacets(event.currentTarget, {
        title: "Filters",
        fields: [
            { id: "role", label: "Role", kind: "select", anyLabel: "Any role", options: [{ value: "user", label: "User" }, { value: "admin", label: "Admin" }], value: facets.role },
            { id: "status", label: "Status", kind: "select", anyLabel: "Any status", options: [{ value: "active", label: "Active" }, { value: "archived", label: "Archived" }, { value: "banned", label: "Banned" }], value: facets.status }
        ],
        onApply: (values) => { facets = { role: values.role || "", status: values.status || "" }; pager.page = 1; render(); }
    });
}
function exportCsv() {
    if (users === null) return;
    const rows = filtered();
    if (rows.length === 0) { notify("Nothing to export: no user is shown.", "info"); return; }
    downloadCsv(`${EXPORT_FILE_PREFIX}-users-${today()}.csv`, toCsv(CSV_COLUMNS, rows, rows.length));
    notify(`Exported ${many(rows.length, "user", "users")}.`, "ok");
}

// ---------- changes (always after a confirmed question) ----------
async function commit(list, make, done) {
    const me = myUid();
    try {
        for (let i = 0; i < list.length; i += BATCH) {
            const batch = writeBatch(db);
            list.slice(i, i + BATCH).forEach((u) => make(batch, u, me));
            await batch.commit();
        }
        picked.clear();
        notify(done, "ok");
    } catch (error) {
        console.error("User change failed: ", error);
        notify("Not saved. " + describeError(error), "error");
    }
}
const names = (list) => list.map((u) => u.name);
const setStatus = (list, to) => commit(list, (batch, u, me) => batch.update(doc(db, "users", u.uid), statusPatch(u, to, serverTimestamp(), me)));

async function archive(list) {
    if (!list.length) return;
    const yes = await askConfirm({ title: "Archive User", message: `${many(list.length, "user is", "users are")} kept but marked Archived. They can be restored at any time.`, items: names(list), confirmLabel: "Move to Archive", badgeIcon: "archive" });
    if (yes) await setStatus(list, "archived").then(() => notify(`${many(list.length, "user", "users")} archived.`, "ok"));
}
async function toBin(list) {
    if (!list.length) return;
    const yes = await askConfirm({ title: "Move to Recycle Bin", message: `${many(list.length, "user is", "users are")} moved to the Recycle Bin. They can be restored from there.`, items: names(list), confirmLabel: "Delete to Recycle Bin", danger: true, badgeIcon: "trash" });
    if (yes) await setStatus(list, "deleted").then(() => notify(`${many(list.length, "user", "users")} moved to the Recycle Bin.`, "ok"));
}
async function restore(list) {
    if (!list.length) return;
    const yes = await askConfirm({ title: "Restore User", message: `${many(list.length, "user returns", "users return")} to the list they came from.`, items: names(list), confirmLabel: "Restore", badgeIcon: "undo" });
    if (!yes) return;
    await commit(list, (batch, u, me) => batch.update(doc(db, "users", u.uid), statusPatch(u, restoreTarget(u), serverTimestamp(), me)), `${many(list.length, "user", "users")} restored.`);
}
async function unban(list) {
    if (!list.length) return;
    const yes = await askConfirm({ title: "Unban User", message: `${many(list.length, "user may", "users may")} use the app again and change their own profile.`, items: names(list), confirmLabel: "Unban", badgeIcon: "check" });
    if (yes) await setStatus(list, "active").then(() => notify(`${many(list.length, "user", "users")} unbanned.`, "ok"));
}
async function purge(list) {
    if (!list.length) return;
    const yes = await askConfirm({
        title: "Delete Permanently",
        message: `${many(list.length, "profile is", "profiles are")} deleted for good and cannot be restored. The person's sign-in account and scans are not removed by this page.`,
        items: names(list), confirmLabel: "Delete permanently", danger: true, badgeIcon: "trash"
    });
    if (!yes) return;
    await commit(list, (batch, u) => batch.delete(doc(db, "users", u.uid)), `${many(list.length, "user", "users")} deleted permanently.`);
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
    const frame = dialogFrame("User details", close);
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

    const tag = (text) => `<span class="chip">${escapeHtml(text)}</span>`;
    const paint = () => {
        const u = byUid(uid);
        if (!u) { content.innerHTML = '<p class="um-none">This user no longer exists.</p>'; return; }
        const s = u.skin;
        const hasSkin = s.skinType || s.skinTone || s.knownAllergens.length || s.age;      // chronic skin conditions are no longer asked (27 Nov 2026)
        const fact = (k, v) => `<dt>${k}</dt><dd>${v}</dd>`;
        content.innerHTML = `
            <section class="um-section"><h4>Account info</h4>
                <div class="um-person">${avatar(u, true)}<div class="um-names"><span class="um-name">${escapeHtml(u.name)}</span><span class="um-sub">${u.username ? "@" + escapeHtml(u.username) : "No username"}</span></div></div>
                <dl class="um-facts">
                    ${fact("Email", escapeHtml(u.email) || "No email")}
                    ${fact("UID", escapeHtml(u.uid))}
                    ${fact("Role", roleBadge(u))}
                    ${fact("Status", statusBadgeOf(u))}
                    ${fact("Joined at", when(u.createdAt))}
                    ${fact("Last active", when(u.lastActive))}
                </dl></section>
            <section class="um-section"><h4>Skin profile</h4>
                ${hasSkin ? `<dl class="um-facts">
                    ${fact("Skin type", escapeHtml(s.skinType) || '<span class="muted">Not set</span>')}
                    ${fact("Skin tone", escapeHtml(s.skinTone) || '<span class="muted">Not set</span>')}
                    ${s.age ? fact("Age", String(s.age)) : ""}
                    ${fact("Allergens", s.knownAllergens.length ? `<span class="um-tags">${s.knownAllergens.map(tag).join("")}</span>` : '<span class="muted">None</span>')}
                </dl>` : '<p class="um-none">No skin profile found.</p>'}</section>`;
    };
    viewing = { uid, paint, frame };
    paint();
    closeBtn.focus();
}

function openEdit(u) {
    if (viewing) return;
    const opener = document.activeElement;
    let sending = false, release = () => { };
    const dirty = () => input.name.value !== u.name || input.username.value !== u.username || input.role.value !== u.role || input.status.value !== (u.status === "deleted" ? "active" : u.status);
    const leave = async () => {
        if (sending) return;
        if (dirty() && !(await askConfirm({ title: "Discard changes?", message: "What you changed will be lost.", cancelLabel: "Keep editing", confirmLabel: "Discard", danger: true }))) return;
        release(); frame.close();
    };
    const frame = dialogFrame("Edit user", leave);
    frame.box.classList.add("um-dialog");
    const form = document.createElement("form");
    form.className = "um-form";
    form.noValidate = true;
    const field = (label, control, id) => { const wrap = document.createElement("div"); wrap.className = "field"; const l = document.createElement("label"); l.htmlFor = id; l.textContent = label; control.id = id; wrap.append(l, control); return wrap; };
    const text = (value, max) => { const i = document.createElement("input"); i.type = "text"; i.value = value; i.maxLength = max; i.autocomplete = "off"; return i; };
    const select = (options, value) => { const s = document.createElement("select"); options.forEach(([v, l]) => { const o = document.createElement("option"); o.value = v; o.textContent = l; s.appendChild(o); }); s.value = value; return s; };
    const input = {
        name: text(u.name, 100), username: text(u.username, 50),
        email: Object.assign(text(u.email, 254), { readOnly: true }),
        role: select([["user", "User"], ["admin", "Admin"]], u.role),
        status: select([["active", "Active"], ["archived", "Archived"], ["banned", "Banned"]], u.status === "deleted" ? "active" : u.status)
    };
    input.email.readOnly = true;
    const hint = (t) => { const p = document.createElement("small"); p.className = "field-hint"; p.textContent = t; return p; };
    const emailField = field("Email", input.email, "umEmail"); emailField.append(hint("The email cannot be changed here."));
    const roleField = field("Role", input.role, "umRole"); roleField.append(hint("A label only. Admin access is given through Admin requests."));
    const problem = document.createElement("p");
    problem.className = "form-error"; problem.setAttribute("role", "alert");
    const bar = document.createElement("div");
    bar.className = "modal-actions";
    const cancel = document.createElement("button"); cancel.type = "button"; cancel.className = "cancel-btn"; cancel.textContent = "Cancel";
    const save = document.createElement("button"); save.type = "submit"; save.className = "save-btn"; save.textContent = "Save changes";
    bar.append(cancel, save);
    form.append(field("Full Name", input.name, "umName"), field("Username", input.username, "umUsername"), emailField, roleField, field("Status", input.status, "umStatus"), problem, bar);
    frame.box.appendChild(form);
    release = trapFocus(frame, opener);
    const show = (t, which) => { problem.textContent = t || ""; problem.style.display = t ? "block" : "none"; Object.values(input).forEach((i) => i.removeAttribute("aria-invalid")); if (which && input[which]) { input[which].setAttribute("aria-invalid", "true"); input[which].focus(); } };
    form.addEventListener("input", () => show(""));
    cancel.addEventListener("click", leave);
    form.addEventListener("submit", async (event) => {
        event.preventDefault();
        if (sending) return;
        const values = { name: input.name.value, username: input.username.value, role: input.role.value, status: input.status.value };
        const bad = editProblem(values);
        if (bad) { show(bad.text, bad.field); return; }
        if (!dirty()) { release(); frame.close(); return; }
        // banning from the edit form asks first, like Unban, Archive and Recycle Bin
        if (values.status === "banned" && u.status !== "banned" && !(await askConfirm({ title: "Ban User", message: "This user can no longer change their profile in the app. You can unban them at any time.", items: [u.name], confirmLabel: "Ban", danger: true }))) return;
        sending = true;
        save.disabled = true; cancel.disabled = true;
        save.innerHTML = '<span class="btn-spin" aria-hidden="true"></span>Saving...';
        try {
            const patch = { fullName: values.name.replace(/\s+/g, " ").trim(), username: values.username.trim(), role: values.role };
            if (values.status !== u.status) Object.assign(patch, statusPatch(u, values.status, serverTimestamp(), myUid()));
            const batch = writeBatch(db);
            batch.update(doc(db, "users", u.uid), patch);
            await batch.commit();
            release(); frame.close();
            notify("Changes saved.", "ok");
        } catch (error) {
            console.error("Edit user failed: ", error);
            show("Not saved. " + describeError(error));
            sending = false; save.disabled = false; cancel.disabled = false; save.textContent = "Save changes";
        }
    });
    input.name.focus();
}
