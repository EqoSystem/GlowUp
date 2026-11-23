
import { auth, db } from "../_Database/firebase-console.js";
import { onAuthStateChanged, signOut } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-auth.js";
import { doc, getDoc, updateDoc, serverTimestamp } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { MAKEUP_TYPES, normalizeName, judgeProduct, notMakeupWords, describeSourceLine, SOURCE_LINK_GUIDE } from "./glowupRules.js";
import { clearStore } from "./adminStore.js";
import { askConfirm } from "./adminShell.js";



const KNOWN_KEY = "glowup.admin.known";
const remember = (known) => { try { if (known) localStorage.setItem(KNOWN_KEY, "1"); else localStorage.removeItem(KNOWN_KEY); } catch (error) {                        } };

export const LOGIN_PAGE = "../_Index/index.html";
export const DASHBOARD_PAGE = "../_Admin_Dashboard/adminDashboard.html";




export const REQUIRE_ADMIN_RECORD = true;


// The official skin types (23 Nov 2026): Normal, Dry, Oily, Combination, Sensitive. "Mix" stays the stored word of Combination
// (ingredients and the app's saved profiles carry it); only the label changed.
export const SKIN_TYPES = [
    { value: "Normal", label: "Normal" },
    { value: "Dry", label: "Dry" },
    { value: "Oily", label: "Oily" },
    { value: "Mix", label: "Combination" },
    { value: "Sensitive", label: "Sensitive" }
];



export const COSMETIC_TYPES = MAKEUP_TYPES;

export const BRAND_CATEGORY = "Makeup";








export const productScope = (cosmeticType, productName) => judgeProduct(cosmeticType, productName).kind;

export const notMakeup = (cosmeticType, productName) => { const kind = productScope(cosmeticType, productName); return kind === "skincare" || kind === "out"; };

export function scopeWords(cosmeticType, productName) {
    const judged = judgeProduct(cosmeticType, productName);
    if (judged.kind === "makeup") return "";
    if (judged.kind === "unclear") return judged.word ? `its kind is not clear (the word "${judged.word}")` : "its kind is not clear";
    return notMakeupWords(judged);
}


export function categoryOutOfScope(category) {
    const key = normalizeName(category);
    return key !== "makeup" && key !== "make up";
}
export const STATUSES = ["Active", "Pending", "Inactive"];






export async function isAdmin(user) {
    try {
        const snap = await getDoc(doc(db, "admins", user.uid));
        // an admin that Admin Management archived or sent to the bin has no access until restored (a record with no status is active)
        return snap.exists() && String(snap.data().status || "active") === "active";
    } catch (error) {
        console.warn("Could not check the admins list:", error);
        return false;
    }
}





// Session expiry, sign-out and the login redirect are the app leaving by itself:
// the browser's "Leave site?" question must not appear for them (only a real window close asks).
function leaveToLogin(address) {
    window.__glowupLeaving = true;
    window.location.replace(address);
}

// "Online now" on Admin Management: this admin's own record gets lastActiveAt now and then while a page is open and visible
// (firestore.rules let an admin write that one field of their own record, and only the server's time).
let heartbeatTimer = 0;
function startHeartbeat(user) {
    const beat = () => { if (!document.hidden) updateDoc(doc(db, "admins", user.uid), { lastActiveAt: serverTimestamp() }).catch(() => { }); };
    beat();
    window.clearInterval(heartbeatTimer);
    heartbeatTimer = window.setInterval(beat, 2 * 60 * 1000);
    document.addEventListener("visibilitychange", () => { if (!document.hidden) beat(); });
}

export function requireAdmin() {
    return new Promise((resolve) => {
        let resolved = false;
        let leaving = false;
        onAuthStateChanged(auth, async (user) => {
            if (leaving) return;
            if (!user) { leaving = true; remember(false); clearStore(); leaveToLogin(LOGIN_PAGE); return; }
            if (REQUIRE_ADMIN_RECORD && !(await isAdmin(user))) {
                if (leaving) return;
                leaving = true;
                remember(false); clearStore();
                try { await signOut(auth); } catch (error) { console.error("Sign out failed:", error); }
                leaveToLogin(LOGIN_PAGE + "?denied=1");
                return;
            }
            remember(true);
            startHeartbeat(user);
            document.documentElement.classList.remove("auth-pending", "auth-stuck", "frame-first");
            document.querySelectorAll(".brand-loader, .auth-stuck-box").forEach((box) => box.remove());
            showSignedInAs(user);
            watchIdle();
            if (!resolved) { resolved = true; resolve(user); }
        });
    });
}



function showSignedInAs() {
    document.querySelectorAll(".signed-in-as").forEach((line) => line.remove());
}





export function wireSignOut() {
    document.querySelectorAll(".sign-out").forEach((link) => {
        if (link.dataset.wired === "1") return;
        link.dataset.wired = "1";
        link.addEventListener("click", (event) => { event.preventDefault(); confirmSignOut(); });
    });
}



const IDLE_MINUTES = 30;
let idleWatched = false;
function watchIdle() {
    if (idleWatched) return;
    idleWatched = true;
    let last = Date.now();
    const awake = () => { last = Date.now(); };
    ["pointerdown", "keydown", "wheel", "touchstart", "scroll"].forEach((name) => window.addEventListener(name, awake, { passive: true, capture: true }));
    window.setInterval(async () => {
        if (Date.now() - last < IDLE_MINUTES * 60 * 1000) return;
        remember(false);
        clearStore();
        try { await signOut(auth); } catch (error) { console.error("Sign out failed:", error); }
        leaveToLogin(LOGIN_PAGE + "?idle=1");
    }, 30 * 1000);
}

export async function confirmSignOut() {
    const sure = await askConfirm({ title: "Sign out?", confirmLabel: "Sign out", danger: true, inline: true, confirmIcon: "logout", cancelIcon: "x" });
    if (!sure) return;
    remember(false);
    clearStore();
    try { await signOut(auth); }
    catch (error) { console.error("Sign out failed:", error); }
    leaveToLogin(LOGIN_PAGE);
}


export const dialogIsOpen = () => Array.from(document.querySelectorAll(".modal-overlay")).some((box) => getComputedStyle(box).display !== "none");


export function setupNavigationGuard() {
    const links = document.querySelectorAll("a[href]:not(.sign-out)");
    links.forEach((link) => {
        link.addEventListener("click", (event) => {
            if (link.classList.contains("is-navigating")) { event.preventDefault(); return; }
            link.classList.add("is-navigating");
        });
    });

    window.addEventListener("pageshow", () => links.forEach((link) => link.classList.remove("is-navigating")));
}






export function escapeHtml(value) {
    return String(value === null || value === undefined ? "" : value)
        .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;").replace(/'/g, "&#39;");
}


export function toList(text) {
    const seen = new Set();
    const result = [];
    String(text || "").split(/[,;\n]+/).forEach((part) => {
        const item = part.replace(/\s+/g, " ").trim();
        const key = item.toLowerCase();
        if (item && !seen.has(key)) { seen.add(key); result.push(item); }
    });
    return result;
}





export function sameKey(value) {
    return normalizeName(value);
}





export function wireSearch(input, clear, onChange) {
    let timer = 0;
    const show = () => { if (clear) clear.hidden = input.value.length === 0; };
    const now = () => { window.clearTimeout(timer); show(); onChange(); };
    input.addEventListener("input", () => { show(); window.clearTimeout(timer); timer = window.setTimeout(onChange, 160); });
    input.addEventListener("keydown", (event) => { if (event.key === "Escape" && input.value) { event.stopPropagation(); input.value = ""; now(); } else if (event.key === "Enter") now(); });
    if (clear) clear.addEventListener("click", () => { input.value = ""; now(); input.focus(); });
    show();
}


export function asArray(value) {
    if (Array.isArray(value)) return value.map((v) => String(v).trim()).filter((v) => v.length > 0);
    if (typeof value === "string") return toList(value);
    return [];
}


export function toWholeNumber(value) {
    const n = Math.floor(Number(value));
    return Number.isFinite(n) && n > 0 ? n : 0;
}


export function toDate(value) {
    if (!value) return null;
    if (typeof value.toDate === "function") return value.toDate();
    if (value instanceof Date) return value;
    if (typeof value === "number") return new Date(value * 1000);
    if (typeof value.seconds === "number") return new Date(value.seconds * 1000);
    return null;
}


export function formatWhen(value) {
    const date = toDate(value);
    if (!date) return "";
    return date.toLocaleString(undefined, { day: "numeric", month: "short", year: "numeric", hour: "numeric", minute: "2-digit" });
}


export function timeAgo(value) {
    const date = toDate(value);
    if (!date) return "";
    const seconds = Math.floor((Date.now() - date.getTime()) / 1000);
    if (seconds < 60) return "just now";
    if (seconds < 3600) return Math.floor(seconds / 60) + " min ago";
    if (seconds < 86400) return Math.floor(seconds / 3600) + " h ago";
    if (seconds < 7 * 86400) return Math.floor(seconds / 86400) + " d ago";
    const sameYear = date.getFullYear() === new Date().getFullYear();
    return date.toLocaleDateString("en-US", sameYear ? { month: "short", day: "numeric" } : { month: "short", day: "numeric", year: "numeric" });
}


export function startOfTodayUnix() {
    const now = new Date();
    return Math.floor(new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime() / 1000);
}






export function describeError(error) {
    const code = error && error.code ? String(error.code) : "";
    if (code.includes("permission-denied"))
        return "The database refused this (permission denied). Check that the Firestore rules are published and that your account is in the admins list.";
    if (code.includes("unavailable") || code.includes("network"))
        return "The database could not be reached. Check your internet connection and try again.";
    if (code.includes("unauthenticated")) return "You are signed out. Sign in again.";
    if (code.includes("not-found")) return "That item no longer exists. Refresh the page.";
    return (error && error.message) ? error.message : "Something went wrong.";
}


export function notify(message, kind = "info") {
    let host = document.getElementById("toastHost");
    if (!host) {
        host = document.createElement("div");
        host.id = "toastHost";
        host.className = "toast-host";
        host.setAttribute("aria-live", "polite");
        document.body.appendChild(host);
    }
    const toast = document.createElement("div");
    toast.className = "toast " + kind;
    toast.textContent = message;
    host.appendChild(toast);
    window.setTimeout(() => toast.remove(), kind === "error" || kind === "warn" ? 8000 : 3500);
}


export function setBusy(button, busy, busyText) {
    if (!button) return;
    if (busy) {
        if (!button.dataset.label) button.dataset.label = button.innerHTML;
        button.disabled = true;
        if (busyText) button.textContent = busyText;
    } else {
        button.disabled = false;
        if (button.dataset.label) { button.innerHTML = button.dataset.label; delete button.dataset.label; }
    }
}


export function setFormError(form, message) {
    const line = form ? form.querySelector(".form-error") : null;
    if (!line) return;
    line.textContent = message || "";
    line.style.display = message ? "block" : "none";
}






export function setupModal(modal, onClose) {
    const close = () => {
        modal.style.display = "none";
        if (onClose) onClose();
    };
    const open = () => {
        modal.style.display = "flex";
        const first = modal.querySelector("input:not([type=hidden]):not([type=checkbox]), select, textarea");
        if (first) first.focus();
    };
    modal.addEventListener("mousedown", (event) => { if (event.target === modal) close(); });
    document.addEventListener("keydown", (event) => { if (event.key === "Escape" && modal.style.display !== "none") close(); });
    modal.querySelectorAll("[data-close]").forEach((button) => button.addEventListener("click", close));
    return { open, close };
}


export function renderChecks(container, name, options) {
    container.innerHTML = options.map((o) =>
        `<label class="check"><input type="checkbox" name="${escapeHtml(name)}" value="${escapeHtml(o.value)}"> <span>${escapeHtml(o.label)}</span></label>`
    ).join("");
}


export function readChecks(container) {
    return Array.from(container.querySelectorAll("input[type=checkbox]:checked")).map((box) => box.value);
}


export function writeChecks(container, values) {
    const wanted = new Set(asArray(values).map((v) => v.toLowerCase()));
    container.querySelectorAll("input[type=checkbox]").forEach((box) => { box.checked = wanted.has(box.value.toLowerCase()); });
}


export function normalizeStatus(value) {
    const text = String(value === null || value === undefined ? "" : value).trim();
    if (!text) return "Active";
    const known = STATUSES.find((s) => s.toLowerCase() === text.toLowerCase());
    return known || text;
}


export const STATUS_MEANINGS = {
    "Active": "The app uses it.",
    "Pending": "It waits for an admin to check it. The app does not use it yet.",
    "Inactive": "An admin switched it off. The app does not use it.",
    "Recycle bin": "It was removed. It can be restored, or deleted for good, on the Recycle Bin page."
};






export function statusBadge(status) {
    const text = String(status || "Active");
    const key = text.toLowerCase() === "recycle bin" ? "bin" : text.toLowerCase().replace(/[^a-z]/g, "");
    return `<span class="status-badge ${escapeHtml(key)}" title="${escapeHtml(STATUS_MEANINGS[text] || "")}">${escapeHtml(text)}</span>`;
}





export function toneBadge(text, tone, title) {
    return `<span class="status-badge tone-${escapeHtml(tone || "muted")}"${title ? ` title="${escapeHtml(title)}"` : ""}>${escapeHtml(text)}</span>`;
}


export function statusGuide() {
    const text = Object.keys(STATUS_MEANINGS).map((name) => `${name}: ${STATUS_MEANINGS[name]}`).join("\n");
    return `<button type="button" class="guide-badge" aria-label="What each status means" data-tip-title="What each status means" data-tip="${escapeHtml(text)}">i</button>`;
}






export function originLabel(source) {
    const key = String(source || "").trim().toLowerCase();
    switch (key) {
        case "": case "admin": return "";
        case "ai": return "Found by the app's AI lookup";
        case "aiassist": return "Drafted with Assist with AI, checked by an admin";
        case "scan": return "Added by an app scan";
        case "staff-capture": return "Submitted from the mobile app (staff capture)";
        case "openbeautyfacts": return "Imported from Open Beauty Facts";
        case "document-import": return "Imported from a document";
        case "starter": return "Starter data";
        case "fdaph": return "From the FDA Philippines list of notified cosmetics";
        case "fda": return "From the US FDA list";
        default: return "Imported (" + key + ")";
    }
}


export function parseSource(text) {
    const raw = String(text || "").trim();
    const cut = raw.lastIndexOf(" | ");
    let title = cut >= 0 ? raw.slice(0, cut).trim() : "";
    const url = cut >= 0 ? raw.slice(cut + 3).trim() : raw;
    if (!/^https?:\/\/[^\s"'<>]+$/i.test(url)) return { title: title || raw, url: "" };


    let parsed = null;
    try { parsed = new URL(url); } catch (error) { parsed = null; }
    if (!parsed || parsed.username || parsed.password || url.includes(String.fromCharCode(92))) return { title: title || raw, url: "" };
    if (!title) { try { title = new URL(url).hostname.replace(/^www\./, ""); } catch (error) { title = url; } }
    return { title, url };
}






export function sourceKindChip(source, confirmedWebsite) {
    const kind = describeSourceLine(source, confirmedWebsite);
    return `<span class="chip source-kind${kind.tone ? " " + kind.tone : ""}" title="${escapeHtml(kind.advice)}">${escapeHtml(kind.label)}</span>`;
}


export const sourceGuideHtml = () => `<span class="field-hint source-guide">${escapeHtml(SOURCE_LINK_GUIDE)}</span>`;






export function assistSourceLinks(pages, emptyHtml, confirmedWebsite = "") {
    const shown = (Array.isArray(pages) ? pages : []).filter((page) => page && /^https:\/\//i.test(page.url || "") && parseSource(page.url).url);
    if (shown.length === 0) return emptyHtml;
    return shown.map((page) => {
        const kind = page.label ? page : describeSourceLine(`${page.site || page.title || ""} | ${page.url}`, confirmedWebsite);
        return `<a class="source-link" href="${escapeHtml(page.url)}" target="_blank" rel="noopener noreferrer">${escapeHtml(page.title || page.site || page.url)}</a> `
            + `<span class="chip source-kind${kind.tone ? " " + kind.tone : ""}" title="${escapeHtml(kind.advice || "")}">${escapeHtml(kind.label)}</span>`;
    }).join("<br>") + sourceGuideHtml();
}





export function sourceLinks(sources, confirmedWebsite = "") {
    return (Array.isArray(sources) ? sources : []).map((line) => ({ line, ...parseSource(line) })).filter((s) => s.title || s.url).map((s) => s.url
        ? `<a class="source-link" href="${escapeHtml(s.url)}" target="_blank" rel="noopener noreferrer">${escapeHtml(s.title)}</a> ${sourceKindChip(s.line, confirmedWebsite)}`
        : `<span>${escapeHtml(s.title)}</span>`).join(" · ");
}
