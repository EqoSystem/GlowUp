// Header notification bell. The profile/avatar control is intentionally not rendered in the header;
// account actions are available from Quick settings so every page keeps the same clean right edge.
//
// BELL: a live count of new support reports the admin has not opened yet. It follows the same documents the dashboard's report
// panels read (supportReports with status "new"), but live (onSnapshot through adminStore) so a report sent from the app shows
// at once. A report is "unread" until an admin marks it: `read: true` (+ `readAt`) is written on that report. A report that
// has no `read` field counts as unread, so old reports need no migration and the app (whose create rule allows no extra field)
// needs no change. `read` is the bell's own flag: the dashboard panels and `status` are not touched by it.
// The listener starts when an account is signed in, and stops on sign-out and when the page is left (it starts again when the
// page comes back from the browser's back/forward cache).
//
// PROFILE: Edit Profile and My activity log are available under the gear's Account section.
//
// One dropdown at a time: opening one closes the other (and any open menu); a click outside, Escape or a resize closes it.

import { auth, db } from "../_Database/firebase-console.js";
import { onAuthStateChanged } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-auth.js";
import * as fb from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { onSnapshot, writeBatch } from "./adminStore.js";
import { timeAgo } from "./adminCommon.js";
import { openEditProfile, lastLoginText } from "./adminProfile.js";
import { openAuditLog } from "./adminAuditLog.js";
import { openReplyDrawer } from "./replyDrawer.js";
import { icon, closeMenu, toast } from "./adminShell.js";

export const KINDS = [
    { key: "bug", label: "Bug", icon: "bug", match: (category) => category === "Bug" },
    { key: "suggestion", label: "Suggestion", icon: "bulb", match: (category) => category === "Suggestion" },
    { key: "wrong", label: "Wrong Scan", icon: "camera", match: (category) => category === "Wrong scan result" },
    { key: "product", label: "New product", icon: "box", match: (category) => category === "New product" },      // app Send for Review (27 Nov 2026)
    { key: "other", label: "Others", icon: "inbox", match: () => true }          // anything else
];
export const kindOf = (category) => KINDS.find((kind) => kind.match(String(category || ""))) || KINDS[KINDS.length - 1];
const PREVIEW = 90;
const CHUNK = 200;
const preview = (text) => { const t = String(text || "").replace(/\s+/g, " ").trim(); return t.length > PREVIEW ? t.slice(0, PREVIEW - 1) + "…" : t; };
const senderOf = (r) => r.userName || (r.uid ? "Account " + r.uid.slice(0, 8) : "Unknown sender");

// ---- the live list ----
let reports = null;         // null = not loaded yet
let problem = null;
let stopReports = null;
let user = null;
let bellBtn = null, badgeEl = null, avatarBtn = null;

const subscribers = new Set();
const emit = () => subscribers.forEach((callback) => { try { callback(); } catch (error) { console.error(error); } });

function startReports() {
    stopWatching();
    reports = null; problem = null;
    emit();
    stopReports = onSnapshot(fb.query(fb.collection(db, "supportReports"), fb.where("status", "==", "new")), (snapshot) => {
        reports = snapshot.docs.map((d) => {
            const data = d.data();
            return { id: d.id, category: String(data.category || "Other"), message: String(data.message || ""), uid: String(data.uid || ""), userName: String(data.userName || "").trim(), read: data.read === true, when: data.createdAt || data.createdAtUnix || 0 };
        }).sort((a, b) => whenMs(b) - whenMs(a));
        problem = null;
        emit();
    }, (error) => {
        console.warn("Notifications could not be loaded:", error && error.code ? error.code : error);
        problem = error || { code: "unknown" };
        reports = reports || [];
        emit();
    });
}
const whenMs = (r) => { const w = r.when; if (!w) return 0; if (typeof w.toMillis === "function") return w.toMillis(); if (typeof w === "number") return w * 1000; return typeof w.seconds === "number" ? w.seconds * 1000 : 0; };

/** Stops the live listener (sign-out, leaving the page). Safe to call twice. */
export function stopWatching() {
    if (stopReports) { stopReports(); stopReports = null; }
}

async function markRead(ids) {
    if (ids.length === 0) return;
    const changed = (reports || []).filter((r) => ids.includes(r.id) && !r.read);
    changed.forEach((r) => { r.read = true; });                         // the badge drops at once
    emit();
    try {
        for (let i = 0; i < ids.length; i += CHUNK) {
            const batch = writeBatch(db);
            ids.slice(i, i + CHUNK).forEach((id) => batch.update(fb.doc(db, "supportReports", id), { read: true, readAt: fb.serverTimestamp() }));
            await batch.commit();
        }
    } catch (error) {
        console.error("Mark as read failed:", error && error.code ? error.code : error);
        changed.forEach((r) => { r.read = false; });
        emit();
        toast("Not marked as read. Check the connection and try again.", "error");
    }
}

// ---- one dropdown at a time ----
let pop = null;             // { name, el, anchor, off }
export function closePop(giveFocusBack) {
    if (!pop) return;
    const { el, anchor, off } = pop;
    pop = null;
    off();
    el.remove();
    anchor.setAttribute("aria-expanded", "false");
    if (giveFocusBack) anchor.focus({ preventScroll: true });
}
function openPop(name, anchor, el, repaint) {
    if (pop && pop.name === name) { closePop(true); return false; }
    closePop(false);
    closeMenu();
    el.classList.add("menu", "hdr-pop");
    document.body.appendChild(el);
    const box = anchor.getBoundingClientRect(), size = { width: el.offsetWidth, height: el.offsetHeight };   // layout size: the closed panel is drawn a little smaller
    let left = box.right - size.width;
    if (left < 8) left = Math.min(box.left, window.innerWidth - size.width - 8);
    let top = box.bottom + 6;
    if (top + size.height > window.innerHeight - 8) top = Math.max(8, box.top - size.height - 6);
    el.style.left = Math.max(8, left) + "px";
    el.style.top = top + "px";
    requestAnimationFrame(() => el.classList.add("show"));
    anchor.setAttribute("aria-expanded", "true");
    const onDown = (event) => { if (!el.contains(event.target) && !anchor.contains(event.target)) closePop(false); };
    const onKey = (event) => { if (event.key === "Escape" && ![...document.querySelectorAll(".modal-overlay")].some((o) => getComputedStyle(o).display !== "none")) { event.stopPropagation(); closePop(true); } };
    const onResize = () => closePop(false);
    document.addEventListener("pointerdown", onDown, true);
    document.addEventListener("keydown", onKey, true);
    window.addEventListener("resize", onResize);
    // a live panel repaints when the list changes, and every minute so "2 h ago" stays true while it is open
    const tick = repaint ? window.setInterval(repaint, 60 * 1000) : 0;
    if (repaint) subscribers.add(repaint);
    pop = { name, el, anchor, off: () => { document.removeEventListener("pointerdown", onDown, true); document.removeEventListener("keydown", onKey, true); window.removeEventListener("resize", onResize); window.clearInterval(tick); if (repaint) subscribers.delete(repaint); } };
    return true;
}

const node = (tag, className, text) => { const el = document.createElement(tag); if (className) el.className = className; if (text !== undefined) el.textContent = text; return el; };

// ---- bell ----
const unread = () => (reports || []).filter((r) => !r.read);
function paintBadge() {
    if (!bellBtn) return;
    const n = unread().length;
    badgeEl.hidden = n === 0;
    badgeEl.textContent = n > 99 ? "99+" : String(n);
    bellBtn.setAttribute("aria-label", n > 0 ? `Notifications, ${n} new` : "Notifications");
}

const KIND_WORDS = { bug: "Bug report", suggestion: "Suggestion", wrong: "Wrong scan result", product: "New product", other: "Other report" };
const dayStart = () => { const d = new Date(); d.setHours(0, 0, 0, 0); return d.getTime(); };

function paintNotifications(panel) {
    const head = node("div", "np-head");
    const name = node("div", "np-name");
    name.append(node("strong", "np-title", "Notifications"));
    const left = unread().length;
    if (left > 0) name.append(node("span", "np-unread", String(left > 99 ? "99+" : left)));
    head.append(name);
    const all = node("button", "btn ghost np-all", "Mark all as read");
    all.type = "button";
    all.disabled = left === 0;
    all.addEventListener("click", () => markRead(unread().map((r) => r.id)));
    head.append(all);
    const body = node("div", "np-body");
    if (reports === null) {
        body.setAttribute("aria-busy", "true");
        for (let i = 0; i < 3; i++) { const row = node("div", "np-item np-sk"); row.setAttribute("aria-hidden", "true"); row.append(node("span", "sk"), node("span", "sk short")); body.append(row); }
    } else if (problem) {
        const fail = node("div", "np-state np-error");
        fail.setAttribute("role", "alert");
        fail.append(node("p", "", String(problem.code || "").includes("permission-denied") ? "The reports could not be read. The latest database rules may not be published yet." : "The reports could not be loaded. Check the connection."));
        const again = node("button", "btn", "Retry");
        again.type = "button";
        again.addEventListener("click", startReports);
        fail.append(again);
        body.append(fail);
    } else if (reports.length === 0) {
        const empty = node("div", "np-state");
        empty.append(node("p", "", "You are all caught up."), node("small", "", "New reports from the app appear here by themselves."));
        body.append(empty);
    } else {
        const cut = dayStart();
        [["Today", reports.filter((r) => whenMs(r) >= cut)], ["Earlier", reports.filter((r) => whenMs(r) < cut)]].forEach(([label, mine]) => {
            if (mine.length === 0) return;
            const group = node("section", "np-group");
            group.append(node("h4", "np-group-title", label));
            mine.forEach((r) => {
                const item = node("button", "np-item" + (r.read ? " is-read" : ""));
                item.type = "button";
                item.dataset.kind = kindOf(r.category).key;
                const dot = node("span", "np-dot"); dot.setAttribute("aria-hidden", "true");
                const words = node("span", "np-words");
                words.append(node("span", "np-kind", KIND_WORDS[kindOf(r.category).key]), node("span", "np-msg", preview(r.message)), node("span", "np-from", senderOf(r)));
                item.append(dot, words, node("span", "np-when", timeAgo(r.when)));
                item.setAttribute("aria-label", `${r.read ? "" : "Unread. "}${KIND_WORDS[kindOf(r.category).key]} from ${senderOf(r)}: ${preview(r.message)}. Open to reply.`);
                item.addEventListener("click", () => {
                    closePop(false);
                    if (!r.read) markRead([r.id]);
                    // a "New product" report opens in the inventory's Review queue (one place for every review)
                    if (r.category === "New product") { window.location.href = new URL("../_Admin_Brand_Inventory/adminBrandInventory.html#report=" + encodeURIComponent(r.id), import.meta.url).href; return; }
                    openReplyDrawer({ id: r.id, uid: r.uid, userName: r.userName, category: r.category, message: r.message });
                });
                group.append(item);
            });
            body.append(group);
        });
    }
    const foot = node("a", "np-foot", "View all reports");
    foot.href = "../_Admin_Dashboard/adminDashboard.html#reports";
    panel.replaceChildren(head, body, foot);
}

function toggleBell() {
    const panel = node("div", "np-pop");
    panel.setAttribute("role", "dialog");
    panel.setAttribute("aria-label", "Notifications");
    paintNotifications(panel);
    openPop("bell", bellBtn, panel, () => paintNotifications(panel));
}

// ---- profile ----
const nameOf = (u) => String((u && (u.displayName || u.email)) || "").trim();
function paintAvatar() {
    if (!avatarBtn) return;
    const letter = nameOf(user).charAt(0).toUpperCase();
    avatarBtn.replaceChildren();
    if (letter) avatarBtn.append(node("span", "hdr-letter", letter)); else avatarBtn.innerHTML = icon("user");
    avatarBtn.setAttribute("aria-label", user ? `Account: ${nameOf(user)}` : "Account");
}

function toggleProfile() {
    const panel = node("div", "ap-pop");
    panel.setAttribute("role", "dialog");
    panel.setAttribute("aria-label", "Account");
    const who = node("div", "ap-who");
    const circle = node("span", "ap-circle", nameOf(user).charAt(0).toUpperCase() || "?");
    circle.setAttribute("aria-hidden", "true");
    const words = node("div", "ap-words");
    words.append(node("strong", "ap-name", (user && user.displayName) || "Admin"), node("span", "ap-email", (user && user.email) || ""));
    who.append(circle, words);
    const row = (label, iconName, run, danger) => {
        const button = node("button", "menu-item" + (danger ? " danger" : ""));
        button.type = "button";
        button.innerHTML = icon(iconName);
        button.append(node("span", "menu-text", label));
        button.addEventListener("click", () => { closePop(false); run(); });
        return button;
    };
    const items = [who, node("div", "menu-sep"),
        row("Edit Profile", "edit", openEditProfile),
        row("My activity log", "list", () => openAuditLog({ uid: user ? user.uid : "" }))];
    const last = lastLoginText(user);
    if (last) { items.push(node("div", "menu-sep")); const line = node("p", "ap-last", "Last login: " + last); items.push(line); }
    panel.append(...items);
    if (openPop("profile", avatarBtn, panel)) panel.querySelector(".menu-item")?.focus({ preventScroll: true });
}

// ---- mounting ----
/** Adds the notification bell left of the gear (once). `tools` is the header's .topbar-tools. */
export function mountHeaderAccount(tools) {
    if (!tools || tools.querySelector(".hdr-bell")) return;
    bellBtn = node("button", "icon-btn hdr-bell");
    bellBtn.type = "button";
    bellBtn.id = "notificationsBtn";
    bellBtn.setAttribute("aria-haspopup", "dialog");
    bellBtn.setAttribute("aria-expanded", "false");
    bellBtn.innerHTML = icon("bell");
    badgeEl = node("span", "hdr-badge");
    badgeEl.hidden = true;
    badgeEl.setAttribute("aria-hidden", "true");
    bellBtn.append(badgeEl);
    const gearBtn = tools.querySelector(".quick-gear");
    if (gearBtn) gearBtn.before(bellBtn); else tools.append(bellBtn);
    paintBadge();
    bellBtn.addEventListener("click", toggleBell);
    subscribers.add(paintBadge);

    onAuthStateChanged(auth, (account) => {
        user = account || null;
        if (user) startReports();
        else { stopWatching(); closePop(false); reports = null; problem = null; emit(); }
    });
    // leaving the page ends the listener; coming back from the back/forward cache starts it again
    window.addEventListener("pagehide", stopWatching);
    window.addEventListener("pageshow", (event) => { if (event.persisted && user && !stopReports) startReports(); });
}
