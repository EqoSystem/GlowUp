// Header notification bell and admin profile (20 November 2026). Mounted by mountShell next to the Quick settings gear:
// bell, gear, profile (left to right), on every admin page.
//
// BELL: a live count of new support reports the admin has not opened yet. It follows the same documents the dashboard's report
// panels read (supportReports with status "new"), but live (onSnapshot through adminStore) so a report sent from the app shows
// at once. A report is "unread" until an admin marks it: `read: true` (+ `readAt`) is written on that report. A report that
// has no `read` field counts as unread, so old reports need no migration and the app (whose create rule allows no extra field)
// needs no change. `read` is the bell's own flag: the dashboard panels and `status` are not touched by it.
// The listener starts when an account is signed in, and stops on sign-out and when the page is left (it starts again when the
// page comes back from the browser's back/forward cache).
//
// PROFILE: a pink circle with the first letter of the admin's name or e-mail; its menu shows name and e-mail, opens Quick
// settings, and signs out through the same "Sign out?" question as the sidebar button.
//
// One dropdown at a time: opening one closes the other (and any open menu); a click outside, Escape or a resize closes it.

import { auth, db } from "../_Database/firebase-console.js";
import { onAuthStateChanged } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-auth.js";
import * as fb from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { onSnapshot, writeBatch } from "./adminStore.js";
import { timeAgo, confirmSignOut } from "./adminCommon.js";
import { icon, closeMenu, toast } from "./adminShell.js";

export const KINDS = [
    { key: "bug", label: "Bug", icon: "bug", match: (category) => category === "Bug" },
    { key: "suggestion", label: "Suggestion", icon: "bulb", match: (category) => category === "Suggestion" },
    { key: "wrong", label: "Wrong Scan", icon: "camera", match: (category) => category === "Wrong scan result" },
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
        }).filter((r) => !r.read).sort((a, b) => whenMs(b) - whenMs(a));
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
    const gone = (reports || []).filter((r) => ids.includes(r.id));
    reports = (reports || []).filter((r) => !ids.includes(r.id));        // the badge drops at once
    emit();
    try {
        for (let i = 0; i < ids.length; i += CHUNK) {
            const batch = writeBatch(db);
            ids.slice(i, i + CHUNK).forEach((id) => batch.update(fb.doc(db, "supportReports", id), { read: true, readAt: fb.serverTimestamp() }));
            await batch.commit();
        }
    } catch (error) {
        console.error("Mark as read failed:", error && error.code ? error.code : error);
        const have = new Set((reports || []).map((r) => r.id));
        reports = (reports || []).concat(gone.filter((r) => !have.has(r.id))).sort((a, b) => whenMs(b) - whenMs(a));
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
function paintBadge() {
    if (!bellBtn) return;
    const n = reports ? reports.length : 0;
    badgeEl.hidden = n === 0;
    badgeEl.textContent = n > 99 ? "99+" : String(n);
    bellBtn.setAttribute("aria-label", n > 0 ? `Notifications, ${n} new` : "Notifications");
}

function paintNotifications(panel) {
    const head = node("div", "np-head");
    head.append(node("strong", "np-title", "Notifications"));
    const all = node("button", "btn ghost np-all", "Mark all as read");
    all.type = "button";
    all.disabled = !reports || reports.length === 0;
    all.addEventListener("click", () => markRead((reports || []).map((r) => r.id)));
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
        empty.innerHTML = icon("bell");
        empty.append(node("p", "", "You are all caught up."), node("small", "", "New reports from the app appear here by themselves."));
        body.append(empty);
    } else {
        KINDS.forEach((kind) => {
            const mine = reports.filter((r) => kindOf(r.category) === kind);
            if (mine.length === 0) return;
            const group = node("section", "np-group");
            const title = node("h4", "np-group-title");
            title.innerHTML = icon(kind.icon);
            title.append(node("span", "", kind.label), node("span", "np-count", String(mine.length)));
            group.append(title);
            mine.forEach((r) => {
                const item = node("button", "np-item");
                item.type = "button";
                item.append(node("span", "np-msg", preview(r.message)));
                const meta = node("span", "np-meta");
                meta.append(node("span", "np-from", senderOf(r)), node("span", "np-when", timeAgo(r.when)));
                item.append(meta);
                item.setAttribute("aria-label", `Mark as read: ${preview(r.message)}`);
                item.addEventListener("click", () => markRead([r.id]));
                group.append(item);
            });
            body.append(group);
        });
    }
    panel.replaceChildren(head, body);
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
    const gear = () => document.getElementById("quickSettingsBtn");
    const items = [who, node("div", "menu-sep")];
    if (gear()) items.push(row("Settings", "gear", () => { const g = gear(); if (g && g.getAttribute("aria-expanded") !== "true") g.click(); }));
    items.push(row("Logout", "logout", confirmSignOut, true));
    panel.append(...items);
    if (openPop("profile", avatarBtn, panel)) panel.querySelector(".menu-item")?.focus({ preventScroll: true });
}

// ---- mounting ----
/** Adds the bell left of the gear and the profile right of it (once). `tools` is the header's .topbar-tools. */
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
    avatarBtn = node("button", "icon-btn hdr-avatar");
    avatarBtn.type = "button";
    avatarBtn.id = "accountBtn";
    avatarBtn.setAttribute("aria-haspopup", "dialog");
    avatarBtn.setAttribute("aria-expanded", "false");
    const gearBtn = tools.querySelector(".quick-gear");
    if (gearBtn) { gearBtn.before(bellBtn); gearBtn.after(avatarBtn); } else tools.append(bellBtn, avatarBtn);
    paintBadge(); paintAvatar();
    bellBtn.addEventListener("click", toggleBell);
    avatarBtn.addEventListener("click", toggleProfile);
    subscribers.add(paintBadge);

    onAuthStateChanged(auth, (account) => {
        user = account || null;
        paintAvatar();
        if (user) startReports();
        else { stopWatching(); closePop(false); reports = null; problem = null; emit(); }
    });
    // leaving the page ends the listener; coming back from the back/forward cache starts it again
    window.addEventListener("pagehide", stopWatching);
    window.addEventListener("pageshow", (event) => { if (event.persisted && user && !stopReports) startReports(); });
}
