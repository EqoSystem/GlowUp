// Shared by every admin page: sign-in guard, real sign-out, safe HTML, messages and small helpers.
import { auth, db } from "../_Database/firebase-console.js";
import { onAuthStateChanged, signOut } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-auth.js";
import { doc, getDoc } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";

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
// GlowUp is about make-up and products for the face. Hair, body, nail and fragrance products are not listed.
export const COSMETIC_TYPES = [
    "Lipstick", "Lip Gloss", "Lip Balm", "Lip Tint", "Foundation", "Concealer", "Primer", "Powder", "Blush", "Bronzer", "Highlighter",
    "Eyeshadow", "Eyeliner", "Mascara", "Brow", "Moisturizer", "Serum", "Cleanser", "Toner", "Sunscreen", "Mask", "Skincare"
];

// ---------------------------------------------------------------------------------------------
// What kind of product is it? (the same rule as the app's ProductScope.cs)
//   "makeup"   - colour cosmetics for the face: the app checks them and offers AR Try-On
//   "skincare" - face care: the app only warns about harmful ingredients, no Try-On, no treatment advice
//   "out"      - hair, body, nails, fragrance and the like: not what GlowUp is for
// ---------------------------------------------------------------------------------------------
const FACE_WORDS = /\b(face|facial|lip|lips|eye|eyes|brow|brows|lash|lashes|cheek|cheeks|complexion)\b/i;
const OUT_WORDS = /\b(shampoo|conditioner|hair|scalp|body|hand|hands|foot|feet|nail|nails|deodorant|antiperspirant|perfume|fragrance|cologne|eau de|soap|shower|bath|shaving|shave|toothpaste|oral|intimate|baby|tanning|massage)\b/i;
const MAKEUP_WORDS = /\b(lipstick|lip gloss|lip tint|lip balm|lip liner|gloss|tint|foundation|concealer|primer|powder|blush|blusher|bronzer|highlighter|contour|eyeshadow|eye shadow|eyeliner|liner|mascara|brow|cushion|bb cream|cc cream|makeup|make-up|setting spray)\b/i;

export function productScope(cosmeticType, productName) {
    const type = String(cosmeticType || ""), text = type + " " + String(productName || "");
    if (MAKEUP_WORDS.test(type)) return "makeup";
    if (OUT_WORDS.test(text) && !FACE_WORDS.test(text)) return "out";
    if (MAKEUP_WORDS.test(text)) return "makeup";
    return "skincare";
}

/** A brand category that is outside make-up and face care ("Haircare", "Body Care", "Fragrance" ...). */
export function categoryOutOfScope(category) {
    const text = String(category || "");
    return /\b(hair|haircare|body|fragrance|perfume|nail|nails|oral|baby)\b/i.test(text) && !/\b(makeup|make-up|skin|skincare|face|cosmetic)/i.test(text);
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
            if (!user) { leaving = true; window.location.replace(LOGIN_PAGE); return; }
            if (REQUIRE_ADMIN_RECORD && !(await isAdmin(user))) {
                if (leaving) return;
                leaving = true;
                try { await signOut(auth); } catch (error) { console.error("Sign out failed:", error); }
                window.location.replace(LOGIN_PAGE + "?denied=1");      // the login page explains why
                return;
            }
            document.documentElement.classList.remove("auth-pending");
            showSignedInAs(user);
            if (!resolved) { resolved = true; resolve(user); }
        });
    });
}

function showSignedInAs(user) {
    const footer = document.querySelector(".sidebar-footer");
    if (!footer || footer.querySelector(".signed-in-as")) return;
    const line = document.createElement("p");
    line.className = "signed-in-as";
    line.textContent = user.email || "Signed in";
    line.title = user.email || "";
    footer.insertBefore(line, footer.firstChild);
}

/** Makes every "Sign Out" link really sign out before it returns to the login page. */
export function wireSignOut() {
    document.querySelectorAll(".sign-out").forEach((link) => {
        link.addEventListener("click", async (event) => {
            event.preventDefault();
            try { await signOut(auth); }
            catch (error) { console.error("Sign out failed:", error); }
            window.location.replace(LOGIN_PAGE);
        });
    });
}

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

/** The same ingredient / brand written with different capitals or spacing compares as equal. */
export function sameKey(value) {
    return String(value || "").replace(/\s+/g, " ").trim().toLowerCase();
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
    window.setTimeout(() => toast.remove(), kind === "error" ? 8000 : 3500);
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

/** <span class="status-pill active">Active</span> */
export function statusPill(status) {
    const text = status || "Active";
    return `<span class="status-pill ${escapeHtml(text.toLowerCase())}">${escapeHtml(text)}</span>`;
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
        case "scan": return "Added by an app scan";
        case "openbeautyfacts": return "Imported from Open Beauty Facts";
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
    if (!title) { try { title = new URL(url).hostname.replace(/^www\./, ""); } catch (error) { title = url; } }
    return { title, url };
}

/** Links to the pages an entry's information came from. Anything that is not a web address shows as plain text. */
export function sourceLinks(sources) {
    return (Array.isArray(sources) ? sources : []).map(parseSource).filter((s) => s.title || s.url).map((s) => s.url
        ? `<a class="source-link" href="${escapeHtml(s.url)}" target="_blank" rel="noopener noreferrer">${escapeHtml(s.title)}</a>`
        : `<span>${escapeHtml(s.title)}</span>`).join(" · ");
}
