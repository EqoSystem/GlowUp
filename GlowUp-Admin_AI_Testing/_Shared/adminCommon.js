// Shared by every admin page: sign-in guard, real sign-out, safe HTML, messages and small helpers.
import { auth, db } from "../_Database/firebase-console.js";
import { onAuthStateChanged, signOut } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-auth.js";
import { doc, getDoc } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { MAKEUP_TYPES, normalizeName, judgeProduct, notMakeupWords, describeSourceLine, SOURCE_LINK_GUIDE } from "./glowupRules.js";
import { clearStore } from "./adminStore.js";
import { askConfirm } from "./adminShell.js";

// "An admin was signed in on this browser": lets the next page draw its frame and fixed text at once (pageFrame.js).
// It says nothing about who, grants nothing and is no part of the check: the check below runs every time.
const KNOWN_KEY = "glowup.admin.known";
const remember = (known) => { try { if (known) localStorage.setItem(KNOWN_KEY, "1"); else localStorage.removeItem(KNOWN_KEY); } catch (error) { /* a private window */ } };

export const LOGIN_PAGE = "../_Index/index.html";
export const DASHBOARD_PAGE = "../_Admin_Dashboard/adminDashboard.html";

// false = any account that can sign in may use the portal (how the portal worked before).
// true  = only accounts that have a document in the "admins" collection (document id = the account's UID).
// It is on: the admin account was added to "admins" on 2026-10-09. To add another admin see FIRESTORE_SCHEMA.md, "Admin accounts".
export const REQUIRE_ADMIN_RECORD = true;

// The words the mobile app understands. Keep these lists the same as the app's.
export const ALLERGEN_FAMILIES = [
    { value: "fragrance", label: "Fragrance / Parfum" },
    { value: "paraben", label: "Parabens" },
    { value: "sulfate", label: "Sulfates (SLS / SLES)" },
    { value: "formaldehyde", label: "Formaldehyde releasers" },
    { value: "lanolin", label: "Lanolin" },
    { value: "essentialoil", label: "Essential oils" },
    { value: "nut", label: "Nut-derived oils" }
];
export const SKIN_TYPES = [
    { value: "Oily", label: "Oily" },
    { value: "Normal", label: "Normal" },
    { value: "Dry", label: "Dry" },
    { value: "Mix", label: "Mix (combination)" }
];
export const SKIN_CONDITIONS = [
    { value: "acne", label: "Acne" },
    { value: "eczema", label: "Eczema" },
    { value: "psoriasis", label: "Psoriasis" },
    { value: "rosacea", label: "Rosacea" },
    { value: "seborrheic", label: "Seborrheic dermatitis" },
    { value: "melasma", label: "Melasma / hyperpigmentation" },
    { value: "contact", label: "Contact dermatitis" }
];
// GlowUp is about MAKE-UP ONLY. Skin care, hair, body, nail and fragrance products are not listed.
// The rule itself lives in glowupRules.js (the same rule as the app's ProductScope.cs, tested against the same examples).
/** The kinds of make-up a product can be: the app's own words. */
export const COSMETIC_TYPES = MAKEUP_TYPES;
/** The one category a brand can have. */
export const BRAND_CATEGORY = "Makeup";

/**
 * What kind of product is it?
 *   "makeup"   - the app checks it, recommends it and offers AR Try-On
 *   "skincare" - not make-up: not listed
 *   "out"      - hair, body, nail, fragrance, make-up tools: not listed
 *   "unclear"  - the rule cannot tell, or the kind is care and colour at once (a tinted lip balm): it waits for an admin
 */
export const productScope = (cosmeticType, productName) => judgeProduct(cosmeticType, productName).kind;
/** True for a product that is clearly something else than make-up. */
export const notMakeup = (cosmeticType, productName) => { const kind = productScope(cosmeticType, productName); return kind === "skincare" || kind === "out"; };
/** In a few words why a product is not listed ("a hair product"), or why its kind is unclear. "" for make-up. */
export function scopeWords(cosmeticType, productName) {
    const judged = judgeProduct(cosmeticType, productName);
    if (judged.kind === "makeup") return "";
    if (judged.kind === "unclear") return judged.word ? `its kind is not clear (the word "${judged.word}")` : "its kind is not clear";
    return notMakeupWords(judged);
}

/** A brand category other than Makeup ("Skincare", "Haircare" ...). GlowUp lists make-up brands only. */
export function categoryOutOfScope(category) {
    const key = normalizeName(category);
    return key !== "makeup" && key !== "make up";
}
export const STATUSES = ["Active", "Pending", "Inactive"];

// ---------------------------------------------------------------------------------------------
// Sign-in guard and sign-out
// ---------------------------------------------------------------------------------------------

/** True when this account has a document in "admins". A read that is refused counts as "not an admin". */
export async function isAdmin(user) {
    try {
        const snap = await getDoc(doc(db, "admins", user.uid));
        return snap.exists();
    } catch (error) {
        console.warn("Could not check the admins list:", error);
        return false;
    }
}

/**
 * Call first on every admin page. The page stays hidden until we know who is signed in.
 * Nobody signed in (or signed out in another tab) -> back to the login page.
 */
export function requireAdmin() {
    return new Promise((resolve) => {
        let resolved = false;
        let leaving = false;                 // once we are on the way out, later sign-in news must not change where we go
        onAuthStateChanged(auth, async (user) => {
            if (leaving) return;
            if (!user) { leaving = true; remember(false); clearStore(); window.location.replace(LOGIN_PAGE); return; }
            if (REQUIRE_ADMIN_RECORD && !(await isAdmin(user))) {
                if (leaving) return;
                leaving = true;
                remember(false); clearStore();
                try { await signOut(auth); } catch (error) { console.error("Sign out failed:", error); }
                window.location.replace(LOGIN_PAGE + "?denied=1");      // the login page explains why
                return;
            }
            remember(true);
            document.documentElement.classList.remove("auth-pending", "auth-stuck", "frame-first");
            document.querySelectorAll(".brand-loader, .auth-stuck-box").forEach((box) => box.remove());
            showSignedInAs(user);
            if (!resolved) { resolved = true; resolve(user); }
        });
    });
}

function showSignedInAs(user) {
    const footer = document.querySelector(".sidebar-footer");
    if (!footer) return;
    // the line is in the frame already (empty), so the Sign Out link below it does not jump when the name arrives
    let line = footer.querySelector(".signed-in-as");
    if (!line) { line = document.createElement("p"); line.className = "signed-in-as"; footer.insertBefore(line, footer.firstChild); }
    line.textContent = user.email || "Signed in";
    line.title = user.email || "";
}

/**
 * "Sign Out" asks first: "Sign out?" with Cancel and Sign out. Only "Sign out" signs out; Cancel, Escape, the X and a
 * click outside the box all leave the admin signed in. The keyboard starts on Cancel.
 */
export function wireSignOut() {
    document.querySelectorAll(".sign-out").forEach((link) => {
        if (link.dataset.wired === "1") return;
        link.dataset.wired = "1";
        link.addEventListener("click", async (event) => {
            event.preventDefault();
            const sure = await askConfirm({ title: "Sign out?", message: "You will need your e-mail and password to come back in.", confirmLabel: "Sign out", danger: true });
            if (!sure) return;
            remember(false);
            clearStore();                    // the lists this tab kept belong to the signed-in admin
            try { await signOut(auth); }
            catch (error) { console.error("Sign out failed:", error); }
            window.location.replace(LOGIN_PAGE);
        });
    });
}

/** True while a dialog is open (a form, a question): the list behind it is not redrawn under the admin's hands. */
export const dialogIsOpen = () => Array.from(document.querySelectorAll(".modal-overlay")).some((box) => getComputedStyle(box).display !== "none");

/** Stops a double click on a sidebar link from starting the same navigation twice. */
export function setupNavigationGuard() {
    const links = document.querySelectorAll("a[href]:not(.sign-out)");
    links.forEach((link) => {
        link.addEventListener("click", (event) => {
            if (link.classList.contains("is-navigating")) { event.preventDefault(); return; }
            link.classList.add("is-navigating");
        });
    });
    // coming back with the browser's Back button must not leave the links switched off
    window.addEventListener("pageshow", () => links.forEach((link) => link.classList.remove("is-navigating")));
}

// ---------------------------------------------------------------------------------------------
// Text helpers
// ---------------------------------------------------------------------------------------------

/** Makes any text safe to place inside HTML (also inside an attribute). */
export function escapeHtml(value) {
    return String(value === null || value === undefined ? "" : value)
        .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;").replace(/'/g, "&#39;");
}

/** "a, b; c" or one item per line -> ["a", "b", "c"] (trimmed, no blanks, no repeats). */
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

/**
 * The same name written with different capitals, spacing, accents or punctuation compares as equal
 * ("L-Oreal  PARIS" and "l oreal paris"). The rule is the app's own: normalizeName in glowupRules.js.
 */
export function sameKey(value) {
    return normalizeName(value);
}

/**
 * A search box: the list follows a moment after the typing stops (not on every key), Enter searches at once,
 * Esc or the X button empties it. `clear` = the X button inside the box (may be null).
 */
export function wireSearch(input, clear, onChange) {
    let timer = 0;
    const show = () => { if (clear) clear.hidden = input.value.length === 0; };
    const now = () => { window.clearTimeout(timer); show(); onChange(); };
    input.addEventListener("input", () => { show(); window.clearTimeout(timer); timer = window.setTimeout(onChange, 160); });
    input.addEventListener("keydown", (event) => { if (event.key === "Escape" && input.value) { event.stopPropagation(); input.value = ""; now(); } else if (event.key === "Enter") now(); });
    if (clear) clear.addEventListener("click", () => { input.value = ""; now(); input.focus(); });
    show();
}

/** Always an array of text, whatever shape the field was saved in (array, or one comma-separated text). */
export function asArray(value) {
    if (Array.isArray(value)) return value.map((v) => String(v).trim()).filter((v) => v.length > 0);
    if (typeof value === "string") return toList(value);
    return [];
}

/** A whole number 0 or above; anything else becomes 0. */
export function toWholeNumber(value) {
    const n = Math.floor(Number(value));
    return Number.isFinite(n) && n > 0 ? n : 0;
}

/** Firestore Timestamp, Date, or Unix seconds -> Date (or null). */
export function toDate(value) {
    if (!value) return null;
    if (typeof value.toDate === "function") return value.toDate();
    if (value instanceof Date) return value;
    if (typeof value === "number") return new Date(value * 1000);
    if (typeof value.seconds === "number") return new Date(value.seconds * 1000);
    return null;
}

/** "9 Oct 2026, 3:40 PM" */
export function formatWhen(value) {
    const date = toDate(value);
    if (!date) return "";
    return date.toLocaleString(undefined, { day: "numeric", month: "short", year: "numeric", hour: "numeric", minute: "2-digit" });
}

/** "just now", "5 min ago", "3 h ago", otherwise the date. */
export function timeAgo(value) {
    const date = toDate(value);
    if (!date) return "";
    const seconds = Math.floor((Date.now() - date.getTime()) / 1000);
    if (seconds < 60) return "just now";
    if (seconds < 3600) return Math.floor(seconds / 60) + " min ago";
    if (seconds < 86400) return Math.floor(seconds / 3600) + " h ago";
    return formatWhen(date);
}

/** Unix seconds of today's midnight on this computer. */
export function startOfTodayUnix() {
    const now = new Date();
    return Math.floor(new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime() / 1000);
}

// ---------------------------------------------------------------------------------------------
// Messages
// ---------------------------------------------------------------------------------------------

/** A database error in plain words. */
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

/** A small message in the corner of the page. kind: "ok" | "error" | "info". */
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

/** Switches a button off while its action runs, so a double click cannot save twice. */
export function setBusy(button, busy, busyText) {
    if (!button) return;
    if (busy) {
        if (!button.dataset.label) button.dataset.label = button.innerHTML;      // the button's own markup (it may hold an icon)
        button.disabled = true;
        if (busyText) button.textContent = busyText;
    } else {
        button.disabled = false;
        if (button.dataset.label) { button.innerHTML = button.dataset.label; delete button.dataset.label; }
    }
}

/** Shows (or clears, with "") the red line inside a form. */
export function setFormError(form, message) {
    const line = form ? form.querySelector(".form-error") : null;
    if (!line) return;
    line.textContent = message || "";
    line.style.display = message ? "block" : "none";
}

// ---------------------------------------------------------------------------------------------
// Pop-up windows and form pieces
// ---------------------------------------------------------------------------------------------

/** Open / close a pop-up. It also closes with Esc, the Cancel button and a click on the dark area. */
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

/** Fills a box with one tick-box per option. options = [{ value, label }]. */
export function renderChecks(container, name, options) {
    container.innerHTML = options.map((o) =>
        `<label class="check"><input type="checkbox" name="${escapeHtml(name)}" value="${escapeHtml(o.value)}"> <span>${escapeHtml(o.label)}</span></label>`
    ).join("");
}

/** The ticked values of a group made with renderChecks. */
export function readChecks(container) {
    return Array.from(container.querySelectorAll("input[type=checkbox]:checked")).map((box) => box.value);
}

/** Ticks the boxes whose value is in `values` (capital letters do not matter). */
export function writeChecks(container, values) {
    const wanted = new Set(asArray(values).map((v) => v.toLowerCase()));
    container.querySelectorAll("input[type=checkbox]").forEach((box) => { box.checked = wanted.has(box.value.toLowerCase()); });
}

/** "active" or " ACTIVE " -> "Active", so a status typed by hand in the database still counts. Empty -> "Active". */
export function normalizeStatus(value) {
    const text = String(value === null || value === undefined ? "" : value).trim();
    if (!text) return "Active";
    const known = STATUSES.find((s) => s.toLowerCase() === text.toLowerCase());
    return known || text;
}

/** What each status means for the app: said by the "i" next to a status (statusGuide) and by each badge's own tooltip. */
export const STATUS_MEANINGS = {
    "Active": "The app uses it.",
    "Pending": "It waits for an admin to check it. The app does not use it yet.",
    "Inactive": "An admin switched it off. The app does not use it.",
    "Recycle bin": "It was removed. It can be restored, or deleted for good, on the Recycle Bin page."
};

/**
 * THE ONE STATUS BADGE of the whole site: a dot and the word, together, in one colour. A status is shown with this
 * and nothing else (no separate dot, no second label), so it looks the same on every page and is never said by colour
 * alone. status = "Active" | "Pending" | "Inactive" | "Recycle bin"; anything else is shown as it is, in grey.
 */
export function statusBadge(status) {
    const text = String(status || "Active");
    const key = text.toLowerCase() === "recycle bin" ? "bin" : text.toLowerCase().replace(/[^a-z]/g, "");
    return `<span class="status-badge ${escapeHtml(key)}" title="${escapeHtml(STATUS_MEANINGS[text] || "")}">${escapeHtml(text)}</span>`;
}
/** The older name of the same badge. */
export const statusPill = statusBadge;

/**
 * The same badge for a judgement that is not a status (how a scan was judged, how risky an ingredient is):
 * tone = "ok" | "warn" | "bad" | "info" | "muted". `title` = what a tooltip says about it (optional).
 */
export function toneBadge(text, tone, title) {
    return `<span class="status-badge tone-${escapeHtml(tone || "muted")}"${title ? ` title="${escapeHtml(title)}"` : ""}>${escapeHtml(text)}</span>`;
}

/** The "i" that tells an admin what each status means. */
export function statusGuide() {
    const text = Object.keys(STATUS_MEANINGS).map((name) => `${name}: ${STATUS_MEANINGS[name]}`).join("\n");
    return `<button type="button" class="guide-badge" aria-label="What each status means" data-tip-title="What each status means" data-tip="${escapeHtml(text)}">i</button>`;
}

// ---------------------------------------------------------------------------------------------
// Entries that were not typed here: found by the app's AI lookup, added by an app scan, or imported
// ---------------------------------------------------------------------------------------------

/** Where an entry came from, in a few words. "" for an entry typed on this website. */
export function originLabel(source) {
    const key = String(source || "").trim().toLowerCase();
    switch (key) {
        case "": case "admin": return "";
        case "ai": return "Found by the app's AI lookup";
        case "aiassist": return "Drafted with Assist with AI, checked by an admin";
        case "scan": return "Added by an app scan";
        case "openbeautyfacts": return "Imported from Open Beauty Facts";
        case "document-import": return "Imported from a document";
        case "starter": return "Starter data";
        case "fdaph": return "From the FDA Philippines list of notified cosmetics";
        case "fda": return "From the US FDA list";
        default: return "Imported (" + key + ")";
    }
}

/** "Page title | https://address" or just an address -> { title, url }. url is "" unless it is a real web address. */
export function parseSource(text) {
    const raw = String(text || "").trim();
    const cut = raw.lastIndexOf(" | ");
    let title = cut >= 0 ? raw.slice(0, cut).trim() : "";
    const url = cut >= 0 ? raw.slice(cut + 3).trim() : raw;
    if (!/^https?:\/\/[^\s"'<>]+$/i.test(url)) return { title: title || raw, url: "" };
    // a user name in front of the site ("https://fda.gov@evil.com") or a backslash can open another site than the one the
    // address seems to name: such an address is shown as plain text, never as a link
    let parsed = null;
    try { parsed = new URL(url); } catch (error) { parsed = null; }
    if (!parsed || parsed.username || parsed.password || url.includes(String.fromCharCode(92))) return { title: title || raw, url: "" };
    if (!title) { try { title = new URL(url).hostname.replace(/^www\./, ""); } catch (error) { title = url; } }
    return { title, url };
}

/**
 * The small label after a source link: what KIND of page it is (Regulator / government, Retailer listing ...), the
 * same words the app shows. Hovering it gives one line of advice. `confirmedWebsite` = the brand's confirmed website
 * ("" for an ingredient, or a brand without one): only its pages are "Manufacturer's own site".
 */
export function sourceKindChip(source, confirmedWebsite) {
    const kind = describeSourceLine(source, confirmedWebsite);
    return `<span class="chip source-kind${kind.tone ? " " + kind.tone : ""}" title="${escapeHtml(kind.advice)}">${escapeHtml(kind.label)}</span>`;
}

/** The line for people who are careful about links, under a list of source links. */
export const sourceGuideHtml = () => `<span class="field-hint source-guide">${escapeHtml(SOURCE_LINK_GUIDE)}</span>`;

/**
 * The pages Assist with AI returned ({ title, site, url, label, advice, tone }), one per line with their kind label,
 * then the guide line. `emptyHtml` is shown when there is no page. The server's label is used; an older answer without
 * one is labelled here by the same rule.
 */
export function assistSourceLinks(pages, emptyHtml, confirmedWebsite = "") {
    const shown = (Array.isArray(pages) ? pages : []).filter((page) => page && /^https:\/\//i.test(page.url || "") && parseSource(page.url).url);
    if (shown.length === 0) return emptyHtml;
    return shown.map((page) => {
        const kind = page.label ? page : describeSourceLine(`${page.site || page.title || ""} | ${page.url}`, confirmedWebsite);
        return `<a class="source-link" href="${escapeHtml(page.url)}" target="_blank" rel="noopener noreferrer">${escapeHtml(page.title || page.site || page.url)}</a> `
            + `<span class="chip source-kind${kind.tone ? " " + kind.tone : ""}" title="${escapeHtml(kind.advice || "")}">${escapeHtml(kind.label)}</span>`;
    }).join("<br>") + sourceGuideHtml();
}

/**
 * Links to the pages an entry's information came from, each with its kind label. Anything that is not a web address
 * shows as plain text. `confirmedWebsite` = the brand's confirmed website, so its own pages are recognised ("" for an ingredient).
 */
export function sourceLinks(sources, confirmedWebsite = "") {
    return (Array.isArray(sources) ? sources : []).map((line) => ({ line, ...parseSource(line) })).filter((s) => s.title || s.url).map((s) => s.url
        ? `<a class="source-link" href="${escapeHtml(s.url)}" target="_blank" rel="noopener noreferrer">${escapeHtml(s.title)}</a> ${sourceKindChip(s.line, confirmedWebsite)}`
        : `<span>${escapeHtml(s.title)}</span>`).join(" · ");
}
