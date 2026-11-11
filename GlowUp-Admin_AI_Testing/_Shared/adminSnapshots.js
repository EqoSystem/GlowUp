// PENDING SNAPSHOT REVIEWS: products app users sent as ONE photo, read by the AI, waiting for an admin.
//
// The app stages them in "pending_products" (the photo in "pending_product_images", which only admins can read). Users already
// see the AI's reading, marked PENDING APPROVAL. This module is the floating review window of the Brand & Product Inventory:
//   - ONE snapshot at a time, "Reviewing snapshot 2 of 5" and a progress bar, the page behind dimmed and blurred;
//   - the photo on the left, what the AI read on the right, each field with a green tick (looks right) or an amber flag
//     (check it), and every ingredient as a pill coloured by what the library says about it;
//   - Approve & Publish, Edit & Approve (unlocks the boxes first), Reject; after an action the card slides out, the counter
//     goes down and the next one comes in.
// Quotas: nothing is read until the window opens. Then ONE query (at most MAX_LOADED entries) and the photo of the card on
// screen (one read, only when that card is shown). Brands, products and ingredients come from the lists the page already holds.
// Everything a user sent is put on the page with textContent or .value, never as HTML.
//
// Things to change quickly:
export const MAX_LOADED = 50;                 // waiting entries read when the window opens (one read each)
export const MAX_NEW_INGREDIENTS = 80;        // ingredients one approval may add to the library
export const SURE_ABOVE = 0.75;               // the AI's own confidence at or above this counts as "looks right"
export const SIMILAR_PRODUCT = 0.88;          // a product this alike (0 to 1) is shown as "looks like", the same measure as Find duplicates
export const SIMILAR_INGREDIENT = 0.93;       // the Ingredient Curator's own limit for names that are nearly the same
/** The statuses an admin can give a brand or product approved from a snapshot (a first-time entry: the admin decides). */
export const STATUSES = ["Active", "Pending", "Inactive"];

/** The three web sources of a snapshot as the catalogue keeps sources ("label | address"), each once. */
export function snapshotSources(entry) {
    const out = [];
    const add = (label, url) => { if (url && !out.some((s) => s.endsWith(" | " + url))) out.push(`${label} | ${url}`); };
    add(entry.primarySourceKind === "official" ? "Official manufacturer site" : "Unverified site", entry.primarySourceUrl);
    add("E-commerce retailer", entry.secondarySourceUrl);
    add(entry.thirdSourceKind === "official" ? "Official manufacturer site" : entry.thirdSourceKind === "retailer" ? "E-commerce retailer" : "Other site", entry.thirdSourceUrl);
    return out;
}

export const APPROVED_DESCRIPTION ="Added from an app user's photo (read by the AI), checked by an admin.";
const EXIT_MS = 320;                          // how long a card takes to slide out

import { db } from "../_Database/firebase-console.js";
import { collection, doc, query, where, limit, getDoc, serverTimestamp } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { getDocs, getCountFromServer, writeBatch, noteReads } from "./adminStore.js";
import { sameKey, toList, asArray, describeError, COSMETIC_TYPES, productScope, scopeWords } from "./adminCommon.js";
import { compactName } from "./glowupRules.js";
import { similarity } from "./glowupCatalog.js";
import { icon, askConfirm, toast } from "./adminShell.js";

const make = (tag, className, text) => { const node = document.createElement(tag); if (className) node.className = className; if (text !== undefined) node.textContent = text; return node; };
const cleanHex = (value) => { const t = String(value || "").trim().toUpperCase(); return /^#[0-9A-F]{6}$/.test(t) ? t : ""; };
const slug = (text) => String(text || "").toLowerCase().normalize("NFD").replace(/[̀-ͯ]/g, "").replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 30);
const calm = () => window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;

// ---------------------------------------------------------------------------------------------
// Data
// ---------------------------------------------------------------------------------------------
// The badge's number is remembered for a while on this computer, so opening Brand & Product Inventory and the Ingredient
// Curator one after the other costs ONE count read, not one per page (and none at all within COUNT_FRESH_MS).
const COUNT_KEY = "glowup.snapshots.count.v1";
const COUNT_FRESH_MS = 10 * 60 * 1000;

/** How many snapshots wait (one count query = 1 read; a number remembered in the last 10 minutes costs none). */
export async function countPending(force) {
    if (!force) {
        try {
            const kept = JSON.parse(localStorage.getItem(COUNT_KEY) || "null");
            if (kept && Number.isInteger(kept.n) && kept.n >= 0 && Date.now() - kept.at < COUNT_FRESH_MS) return kept.n;
        } catch (error) { /* private window: read it */ }
    }
    const answer = await getCountFromServer(query(collection(db, "pending_products"), where("isApproved", "==", false)));
    rememberPendingCount(answer.data().count);
    return answer.data().count;
}

/** Keeps the badge's number (after a count, a load, or an approve / reject in the window). */
export function rememberPendingCount(n) {
    try { localStorage.setItem(COUNT_KEY, JSON.stringify({ n: Math.max(0, Math.floor(Number(n) || 0)), at: Date.now() })); } catch (error) { /* not kept */ }
}
const keptPendingCount = () => { try { const kept = JSON.parse(localStorage.getItem(COUNT_KEY) || "null"); return kept && Number.isInteger(kept.n) ? kept.n : null; } catch (error) { return null; } };

/** The waiting snapshots, newest first (one read each, at most MAX_LOADED). */
export async function loadPending() {
    const snapshot = await getDocs(query(collection(db, "pending_products"), where("isApproved", "==", false), limit(MAX_LOADED)));
    return snapshot.docs.map((d) => {
        const data = d.data();
        return {
            id: d.id, name: String(data.name || ""), brandName: String(data.brandName || ""), cosmeticType: String(data.cosmeticType || ""),
            shadeHex: cleanHex(data.shadeHex), ingredients: asArray(data.ingredients).map(String), hasImage: data.hasImage === true,
            model: String(data.model || ""), confidence: Number(data.confidence) || 0, createdAtUnix: Number(data.createdAtUnix) || 0, createdByUid: String(data.createdByUid || ""),
            variant: String(data.variant || "").slice(0, 60), size: String(data.size || "").slice(0, 40), sourceUrls: httpsLinks(data.sourceUrls),
            // what the phone's own reader found, and where the list came from (7 November 2026; older entries have none of these)
            ocrText: String(data.ocrText || "").slice(0, 1500), ocrBrand: String(data.ocrBrand || "").slice(0, 60),
            ingredientsFrom: ["page", "printed", "none"].includes(data.ingredientsFrom) ? data.ingredientsFrom : "",
            ingredientSourceUrl: httpsLinks([data.ingredientSourceUrl])[0] || "",
            ingredientSourceKind: ["official", "retailer", "other"].includes(data.ingredientSourceKind) ? data.ingredientSourceKind : "",
            // the web search result's facts (11 November 2026; older entries have none). Sent by the app: claims to check, never proof.
            description: String(data.description || "").replace(/\s+/g, " ").trim().slice(0, 300),
            shadeCode: String(data.shadeCode || "").replace(/\s+/g, " ").trim().slice(0, 30),
            minimumAge: Number.isInteger(data.minimumAge) && data.minimumAge > 0 && data.minimumAge <= 18 ? data.minimumAge : 0,
            brandWebsite: httpsLinks([data.brandWebsite])[0] || "", brandLogoUrl: httpsLinks([data.brandLogoUrl])[0] || "", brandImageUrl: httpsLinks([data.brandImageUrl])[0] || "",
            productPhotoUrl: httpsLinks([data.productPhotoUrl])[0] || "", productPageUrl: httpsLinks([data.productPageUrl])[0] || "",
            primarySourceUrl: httpsLinks([data.primarySourceUrl])[0] || "", primarySourceKind: data.primarySourceKind === "official" ? "official" : "unverified",
            secondarySourceUrl: httpsLinks([data.secondarySourceUrl])[0] || "",
            thirdSourceUrl: httpsLinks([data.thirdSourceUrl])[0] || "", thirdSourceKind: ["official", "retailer"].includes(data.thirdSourceKind) ? data.thirdSourceKind : "other"
        };
    }).sort((a, b) => b.createdAtUnix - a.createdAtUnix);
}

/** Links to show: https only, under 300 characters, at most 5, each once. Anything else is dropped. */
export function httpsLinks(value) {
    const out = [];
    asArray(value).forEach((item) => {
        const text = String(item || "");
        try { const url = new URL(text); if (url.protocol === "https:" && text.length <= 300 && !out.includes(url.href)) out.push(url.href); } catch (error) { /* not a link */ }
    });
    return out.slice(0, 5);
}

/** The photo of a snapshot as a data address ("" when there is none). One read. */
export async function loadPhoto(id, senderUid) {
    const snapshot = await getDoc(doc(db, "pending_product_images", id));
    noteReads("pending_product_images", 1);
    // a photo is shown only when the same account sent it and the entry (older entries have no sender: shown as before)
    const sameSender = snapshot.exists() && (!senderUid || String(snapshot.data().createdByUid || "") === senderUid);
    const data = sameSender ? String(snapshot.data().jpegBase64 || "") : "";
    return data && /^[A-Za-z0-9+/=]+$/.test(data) ? "data:image/jpeg;base64," + data : "";
}

/**
 * What the page holds, for the checks: { brands: [{ id, name, aliases, binned }], products: [{ id, brandId, name, aliases,
 * ingredients, binned }], library: [{ id, name, techName, aliases, riskLevel }] }.
 */
function lookups(context) {
    const ids = new Map(), risks = new Map();
    (context.library || []).forEach((entry) => [entry.name, entry.techName, ...(entry.aliases || [])].forEach((n) => {
        const k = sameKey(n);
        if (!k || ids.has(k)) return;
        ids.set(k, entry.id); risks.set(k, entry.riskLevel || "");
    }));
    const findBrand = (text) => { const key = compactName(text); return key ? (context.brands || []).find((b) => [b.name, ...(b.aliases || [])].some((n) => compactName(n) === key)) || null : null; };
    const findProduct = (brandId, text) => { const key = compactName(text); return key ? (context.products || []).find((p) => !p.binned && p.brandId === brandId && [p.name, ...(p.aliases || [])].some((n) => compactName(n) === key)) || null : null; };
    return { ids, risks, findBrand, findProduct };
}

/** What is wrong with the values before approving ("" = nothing). */
export function checkValues(v, context) {
    const { findBrand } = lookups(context);
    if (v.brand.length < 2) return "Type the brand.";
    if (!v.name) return "Type the product's name.";
    if (!v.kind) return "Choose the kind of make-up.";
    const scope = productScope(v.kind, v.name);
    if (scope === "skincare" || scope === "out") return `GlowUp lists make-up only. This reads like ${scopeWords(v.kind, v.name)}. Reject it instead.`;
    if (scope === "unclear") return `The app cannot tell that this is make-up (${scopeWords(v.kind, v.name)}). Correct the kind or the name.`;
    if (v.ingredients.length === 0) return "The ingredient list is empty. Reject it, or type the list from the photo.";
    const brand = findBrand(v.brand);
    if (brand && brand.binned) return `The brand "${brand.name}" is in the Recycle bin. Restore it first.`;
    return "";
}

/** Adds every ingredient the library lacks as "waiting for review" (source ai) to the batch; answers the library id of each name. */
function addMissingIngredients(batch, names, libraryIds, now) {
    const ids = [];
    let added = 0;
    names.forEach((n) => {
        const key = sameKey(n);
        if (libraryIds.has(key)) { ids.push(libraryIds.get(key)); return; }
        if (added >= MAX_NEW_INGREDIENTS) return;
        const ref = doc(collection(db, "ingredients"));
        batch.set(ref, {
            name: n, description: "", aliases: [], classificationId: "", purpose: "", riskLevel: "", cautionNote: "", commonUses: "", minAge: 0, keyActive: false, umbrella: false,
            highlights: [], allergenTags: [], avoidForSkinTypes: [], goodForSkinTypes: [], avoidForConditions: [], goodForConditions: [], folder: "",
            source: "ai", reviewed: false, via: "snapshot", createdAt: now, updatedAt: now
        });
        libraryIds.set(key, ref.id);
        ids.push(ref.id);
        added++;
    });
    return ids;
}

/**
 * Approve: ONE batch. The brand (found, or added as Active), the product (added as Active; an existing entry without a list gets
 * this list), every ingredient the library does not have (added as waiting for review, so the app uses it as a warning at most),
 * and the snapshot marked approved (its photo deleted). Returns the product's id.
 */
export async function approveSnapshot(entry, v, context) {
    const { ids: libraryIds, findBrand, findProduct } = lookups(context);
    const batch = writeBatch(db);
    const now = serverTimestamp();
    const brand = findBrand(v.brand);
    let brandId, brandName;
    if (brand) { brandId = brand.id; brandName = brand.name; }
    else {
        const ref = doc(collection(db, "brands"));
        brandId = ref.id; brandName = v.brand;
        const brandData = { name: v.brand, category: "Makeup", market: "Global", status: STATUSES.includes(v.brandStatus) ? v.brandStatus : "Active", scans: 0, source: "snapshot", reviewed: true, reviewedAt: now, createdAt: now, updatedAt: now };
        // the web search's brand links: the logo and picture as addresses only; the website only as a SUGGESTION (an admin confirms it on the Brand form)
        if (v.brandLogoUrl) brandData.logoUrl = v.brandLogoUrl;
        if (v.brandImageUrl) brandData.imageUrl = v.brandImageUrl;
        if (v.brandWebsite) brandData.websiteSuggested = v.brandWebsite;
        batch.set(ref, brandData);
    }
    const ids = addMissingIngredients(batch, v.ingredients, libraryIds, now);
    const existing = brand ? findProduct(brandId, v.name) : null;
    let productId;
    if (existing) {
        productId = existing.id;
        if ((existing.ingredients || []).length === 0) batch.update(doc(db, "products", existing.id), { ingredients: v.ingredients, ingredientIds: Array.from(new Set(ids)), ingredientCount: v.ingredients.length, updatedAt: now });
    } else {
        const ref = doc(collection(db, "products"));
        productId = ref.id;
        const shades = v.shade ? [v.shade] : [];
        batch.set(ref, {
            name: v.name, brandId, brandName, cosmeticType: v.kind, description: v.description || APPROVED_DESCRIPTION, ingredients: v.ingredients, shades,
            ...(shades.length && v.shadeCode ? { shadeCodes: [v.shadeCode.toUpperCase().slice(0, 16)] } : {}),
            minAge: v.minimumAge || 0, fdaNumber: v.fdaNumber || "",
            status: STATUSES.includes(v.productStatus) ? v.productStatus : "Active", scans: 0, folder: "", source: "snapshot", reviewed: true, reviewedAt: now,
            imageUrl: v.productPhotoUrl || "", sources: v.sources || [],
            ingredientIds: Array.from(new Set(ids)), ingredientCount: v.ingredients.length, ingredientNotes: [],
            tags: [slug(v.kind), slug(brandName)].concat(shades.length ? ["has-shades"] : []).filter(Boolean),
            createdAt: now, updatedAt: now
        });
    }
    batch.update(doc(db, "pending_products", entry.id), {
        isApproved: true, status: "approved", approvedProductId: productId, reviewedAt: now,
        name: v.name, brandName, cosmeticType: v.kind, shadeHex: v.shade, ingredients: v.ingredients
    });
    if (entry.hasImage) batch.delete(doc(db, "pending_product_images", entry.id));
    await batch.commit();
    return productId;
}

/**
 * The product was saved with the Add Product form (prefilled from this snapshot): close the snapshot in ONE batch. The ingredients the
 * library lacks are added as waiting for review, the product's links to the library are put right, the snapshot is marked approved
 * and its photo deleted.
 */
export async function finishSnapshot(entry, productId, v, context) {
    const { ids: libraryIds } = lookups(context);
    const batch = writeBatch(db);
    const now = serverTimestamp();
    const ids = addMissingIngredients(batch, v.ingredients, libraryIds, now);
    batch.update(doc(db, "products", productId), { ingredientIds: Array.from(new Set(ids)), updatedAt: now });
    batch.update(doc(db, "pending_products", entry.id), {
        isApproved: true, status: "approved", approvedProductId: productId, reviewedAt: now,
        name: v.name, brandName: v.brand, cosmeticType: v.kind, shadeHex: v.shade, ingredients: v.ingredients
    });
    if (entry.hasImage) batch.delete(doc(db, "pending_product_images", entry.id));
    await batch.commit();
}

/** Products of the same brand (any brand when the brand is new) whose names are alike but not the same: [{ name, score }], best first, at most 3. */
export function similarProducts(v, context) {
    const { findBrand, findProduct } = lookups(context);
    const brand = findBrand(v.brand);
    if (!v.name || (brand && findProduct(brand.id, v.name))) return [];
    const found = [];
    (context.products || []).forEach((p) => {
        if (p.binned || (brand && p.brandId !== brand.id)) return;
        const score = Math.max(...[p.name, ...(p.aliases || [])].map((n) => similarity(v.name, n)));
        if (score >= SIMILAR_PRODUCT) found.push({ name: p.name, score });
    });
    return found.sort((a, b) => b.score - a.score).slice(0, 3);
}

/** For each ingredient the library lacks: the library entry it nearly equals (Map of sameKey(name) -> { name, score }). */
export function similarIngredients(names, context) {
    const { ids } = lookups(context);
    const out = new Map();
    const library = (context.library || []).filter((e) => e && e.name);
    names.forEach((n) => {
        const key = sameKey(n);
        if (!key || ids.has(key) || out.has(key)) return;
        let best = null;
        library.forEach((e) => {
            const score = Math.max(...[e.name, e.techName, ...(e.aliases || [])].filter(Boolean).map((x) => similarity(n, x)));
            if (score >= SIMILAR_INGREDIENT && (!best || score > best.score)) best = { name: e.name, score };
        });
        if (best) out.set(key, best);
    });
    return out;
}

/** Reject: the snapshot and its photo are deleted. */
export async function rejectSnapshot(entry) {
    const batch = writeBatch(db);
    batch.delete(doc(db, "pending_products", entry.id));
    batch.delete(doc(db, "pending_product_images", entry.id));
    await batch.commit();
}

// ---------------------------------------------------------------------------------------------
// The floating review window
// ---------------------------------------------------------------------------------------------
/**
 * Opens the window. options = {
 *   context(),          - the page's lists (see lookups)
 *   onLoaded(n),        - the window read n waiting snapshots (the page's badge can be put right with it)
 *   onProcessed(),      - one snapshot was approved or rejected (the page's badge goes down by one)
 *   onClose(approved)   - the window closed; approved = how many were approved (the page reloads its list then)
 * }
 */
export function openSnapshotReview(options) {
    let queue = [], at = 0, approved = 0, total = 0, busy = false, editing = false;
    const photos = new Map();                         // id -> data address, so going back does not read again
    const opener = document.activeElement;

    const overlay = make("div", "snapx-overlay");
    const win = make("div", "snapx-window");
    win.setAttribute("role", "dialog"); win.setAttribute("aria-modal", "true"); win.setAttribute("aria-labelledby", "snapxTitle");
    const top = make("div", "snapx-top");
    const title = make("h3", "snapx-title", "Pending Snapshot Reviews"); title.id = "snapxTitle";
    const progress = make("span", "snapx-progress", "Loading...");
    progress.setAttribute("aria-live", "polite");
    const close = make("button", "icon-btn small snapx-close"); close.type = "button"; close.setAttribute("aria-label", "Close the review window"); close.innerHTML = icon("x");
    const bar = make("div", "snapx-bar"); const fill = make("span", "snapx-fill"); bar.appendChild(fill);
    top.append(title, progress, close);
    const stage = make("div", "snapx-stage");
    win.append(top, bar, stage);
    overlay.appendChild(win);
    document.body.appendChild(overlay);
    document.body.classList.add("snapx-open");
    requestAnimationFrame(() => overlay.classList.add("show"));

    const finish = () => {
        overlay.classList.remove("show");
        document.removeEventListener("keydown", onKey, true);
        setTimeout(() => { overlay.remove(); document.body.classList.remove("snapx-open"); }, calm() ? 0 : 200);
        if (opener && opener.focus) opener.focus();
        if (options.onClose) options.onClose(approved);
    };
    close.addEventListener("click", finish);
    overlay.addEventListener("mousedown", (event) => { if (event.target === overlay && !busy) finish(); });
    const onKey = (event) => {
        if (document.querySelector(".modal-overlay[style*='flex']") && !win.contains(event.target)) return;     // a question on top answers its own keys
        const typing = /^(INPUT|TEXTAREA|SELECT)$/.test((event.target && event.target.tagName) || "");
        if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); if (!busy) finish(); }
        else if (!typing && !busy && event.key === "ArrowRight" && at < queue.length - 1) { at++; paint("next"); }
        else if (!typing && !busy && event.key === "ArrowLeft" && at > 0) { at--; paint("back"); }
        else if (event.key === "Tab") {                 // keep the keyboard inside the window
            const items = Array.from(win.querySelectorAll("button:not([disabled]), input:not([readonly]), select:not([disabled]), textarea, [tabindex='0']")).filter((n) => n.offsetParent !== null);
            if (items.length === 0) return;
            const first = items[0], last = items[items.length - 1];
            if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
            else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
        }
    };
    document.addEventListener("keydown", onKey, true);

    function paintProgress() {
        const left = queue.length;
        const done = Math.max(0, total - left);
        // counts UP as the queue is worked through: 1 of 5, then 2 of 5 ... (a skipped card keeps its place)
        progress.textContent = left === 0 ? (approved > 0 ? `All done: ${approved} approved` : "Nothing is waiting")
            : `Reviewing snapshot ${done + at + 1} of ${total}`;
        fill.style.transform = `scaleX(${total > 0 ? Math.min(1, (done + (left > 0 ? at : 0)) / total) : 1})`;
    }

    function paintEmpty() {
        stage.replaceChildren();
        const box = make("div", "snapx-empty");
        const mark = make("div", "snapx-empty-mark"); mark.innerHTML = icon("check");
        box.append(mark, make("p", "snapx-empty-title", approved > 0 ? "All caught up" : "Nothing is waiting"),
            make("p", "snapx-empty-text", approved > 0 ? `${approved} product${approved === 1 ? " is" : "s are"} in the inventory now. Users who scan ${approved === 1 ? "it" : "them"} see no pending badge any more.` : "Photos that app users send of products the catalogue does not have arrive here."));
        const done = make("button", "save-btn", "Close"); done.type = "button"; done.addEventListener("click", finish);
        box.appendChild(done);
        stage.appendChild(box);
        paintProgress();
        done.focus();
    }

    /** Draws the card at `at`. how = "next" | "back" | "" (which way it comes in). */
    function paint(how) {
        if (queue.length === 0) { paintEmpty(); return; }
        at = Math.min(at, queue.length - 1);
        editing = false;
        const entry = queue[at];
        const card = buildCard(entry);
        stage.replaceChildren(card);
        if (how && !calm()) { card.classList.add(how === "back" ? "enter-back" : "enter"); requestAnimationFrame(() => requestAnimationFrame(() => card.classList.remove("enter", "enter-back"))); }
        paintProgress();
        const primary = card.querySelector(".snapx-approve");
        if (primary) primary.focus();
    }

    /** A field row: label, a green tick or an amber flag (with why), and the box itself. */
    function field(labelText, control, ok, why) {
        const row = make("div", "snapx-field");
        const head = make("div", "snapx-field-head");
        const label = make("label", "", labelText);
        label.htmlFor = control.id || ((control.querySelector && control.querySelector("input, select, textarea")) || {}).id || "";
        const cue = make("span", "snapx-cue " + (ok ? "ok" : "check"));
        const cueIcon = make("span", "snapx-cue-icon"); cueIcon.innerHTML = icon(ok ? "check" : "flag");
        cue.append(cueIcon, make("span", "", ok ? "Looks right" : why));
        head.append(label, cue);
        row.append(head, control);
        return row;
    }

    function buildCard(entry) {
        const ctx = options.context();
        const { risks, findBrand, findProduct } = lookups(ctx);
        const closeIngredients = similarIngredients(entry.ingredients, ctx);
        const card = make("article", "snapx-card");

        // ---- left: the photo the user took (read only now, when this card is on screen)
        const left = make("div", "snapx-left");
        const frame = make("div", "snapx-photo", entry.hasImage ? "Loading the photo..." : "No photo was kept for this snapshot.");
        left.append(frame, make("p", "snapx-caption", `Sent ${entry.createdAtUnix ? new Date(entry.createdAtUnix * 1000).toLocaleString() : "at an unknown time"} by user ${entry.createdByUid || "unknown"} · identified by ${entry.model || "the AI"}`));
        // the text the phone read on the photo (cleaned on the phone: only lines that read as real words)
        if (entry.ocrText || entry.ocrBrand) {
            const read = make("details", "snapx-ocr");
            read.append(make("summary", "", "Text the phone read on the photo" + (entry.ocrBrand ? " · brand: " + entry.ocrBrand : "")));
            read.append(make("pre", "snapx-ocr-text", entry.ocrText || "(only the brand was readable)"));
            left.append(read);
        }
        if (entry.hasImage) {
            const show = (src) => {
                if (!src) { frame.textContent = "The photo could not be shown."; return; }
                const img = make("img"); img.alt = "The pack, as the app user photographed it"; img.src = src;
                frame.replaceChildren(img);
            };
            if (photos.has(entry.id)) show(photos.get(entry.id));
            else loadPhoto(entry.id, entry.createdByUid).then((src) => { photos.set(entry.id, src); if (frame.isConnected) show(src); })
                .catch((error) => { if (frame.isConnected) frame.textContent = "The photo could not be read. " + describeError(error); });
        }

        // ---- right: what the AI read, each with a cue
        const right = make("div", "snapx-right");
        const sure = entry.confidence >= SURE_ABOVE;
        const summary = make("div", "snapx-summary " + (sure ? "ok" : "check"));
        const summaryIcon = make("span", "snapx-cue-icon"); summaryIcon.innerHTML = icon(sure ? "check" : "flag");
        summary.append(summaryIcon, make("span", "", sure ? `The AI was sure (${Math.round(entry.confidence * 100)}%). Still compare it with the photo.`
            : `The AI was only ${Math.round(entry.confidence * 100)}% sure. Check every amber field against the photo.`));

        const input = (id, value, max) => { const box = make("input"); box.type = "text"; box.id = id; box.value = value; box.maxLength = max; box.autocomplete = "off"; box.readOnly = true; return box; };
        const brandBox = input("snapxBrand", entry.brandName, 60);
        const nameBox = input("snapxName", entry.name, 100);
        const kindBox = make("select"); kindBox.id = "snapxKind"; kindBox.disabled = true;
        const kinds = COSMETIC_TYPES.slice();
        if (entry.cosmeticType && !kinds.some((t) => sameKey(t) === sameKey(entry.cosmeticType))) kinds.unshift(entry.cosmeticType);
        [""].concat(kinds).forEach((t) => { const o = make("option", "", t || "Choose a kind"); o.value = t; kindBox.appendChild(o); });
        kindBox.value = kinds.find((t) => sameKey(t) === sameKey(entry.cosmeticType)) || "";
        const shadeBox = input("snapxShade", entry.shadeHex, 7);
        shadeBox.placeholder = "#RRGGBB";
        const shadeWrap = make("div", "snapx-shade");
        const swatch = make("span", "snapx-swatch"); swatch.style.background = entry.shadeHex || "transparent";
        shadeWrap.append(swatch, shadeBox);
        shadeBox.id = "snapxShade";
        shadeBox.addEventListener("input", () => { swatch.style.background = cleanHex(shadeBox.value) || "transparent"; });

        const brandHit = findBrand(entry.brandName);
        const kindKnown = COSMETIC_TYPES.some((t) => sameKey(t) === sameKey(entry.cosmeticType)) && productScope(entry.cosmeticType, entry.name) === "makeup";
        const existing = brandHit ? findProduct(brandHit.id, entry.name) : null;
        const rows = make("div", "snapx-fields");
        const pair1 = make("div", "snapx-pair"), pair2 = make("div", "snapx-pair");
        pair1.append(
            field("Brand", brandBox, !!brandHit && !brandHit.binned, brandHit && brandHit.binned ? "In the Recycle bin" : "New brand: check the spelling"),
            field("Product name", nameBox, sure && entry.name.length > 1 && !existing, existing ? "Already in the inventory" : "Check the name"));
        pair2.append(
            field("Kind", kindBox, kindKnown, "Choose the right kind"),
            field("Shade", shadeWrap, !!entry.shadeHex, "No shade read"));
        rows.append(pair1, pair2);

        // ingredients: pills coloured by the library (green safe, amber caution, red harmful, dashed = new to the library)
        const listBox = make("textarea"); listBox.id = "snapxList"; listBox.rows = 4; listBox.maxLength = 6000; listBox.value = entry.ingredients.join(", "); listBox.readOnly = true;
        listBox.hidden = true;
        const pills = make("div", "snapx-pills");
        const paintPills = () => {
            pills.replaceChildren();
            toList(listBox.value).forEach((n) => {
                const k = sameKey(n), known = risks.has(k), risk = known ? risks.get(k) : "";
                const pill = make("span", "snapx-pill " + (known ? (risk || "auto") : "new"), n);
                const near = !known && closeIngredients.get(k);
                pill.title = known ? (risk ? `In the library: ${risk}` : "In the library: the app decides")
                    : near ? `Nearly equal to "${near.name}" in the library. Added as waiting for review unless you correct the spelling.` : "Not in the library yet: added as waiting for review";
                pills.appendChild(pill);
            });
        };
        paintPills();
        listBox.addEventListener("input", paintPills);
        const names = toList(listBox.value);
        const newOnes = names.filter((n) => !risks.has(sameKey(n))).length;
        const harmful = names.filter((n) => risks.get(sameKey(n)) === "harmful").length;
        const listField = make("div", "snapx-field");
        const listHead = make("div", "snapx-field-head");
        const listLabel = make("label", "", `Ingredients (${names.length})`); listLabel.htmlFor = "snapxList";
        const listCue = make("span", "snapx-cue " + (newOnes === 0 && names.length > 0 ? "ok" : "check"));
        const listCueIcon = make("span", "snapx-cue-icon"); listCueIcon.innerHTML = icon(newOnes === 0 && names.length > 0 ? "check" : "flag");
        listCue.append(listCueIcon, make("span", "", names.length === 0 ? "No list read" : newOnes === 0 ? "All in the library" : `${newOnes} new to the library`));
        listHead.append(listLabel, listCue);
        const legend = make("p", "snapx-legend", harmful > 0 ? `${harmful} marked harmful in the library (red). ` : "");
        legend.append(make("span", "", "Green safe · amber caution · red harmful · dashed new."));
        listField.append(listHead, pills, listBox, legend);

        // what else the AI read, kept apart from the name; the pages it used (https only; text is set as text, never as markup)
        const extra = make("div", "snapx-extra");
        if (entry.variant || entry.size) extra.append(make("p", "snapx-details", [entry.variant && "Shade or variant: " + entry.variant, entry.size && "Size: " + entry.size].filter(Boolean).join("   ")));
        // where the INGREDIENT LIST came from: a page this server opened, or the pack itself (never written by the AI)
        const kindWord = { official: "official brand site", retailer: "retailer", other: "other site" };
        // these fields are SENT BY THE APP (any signed-in phone can write them): they are claims to check, never proof
        if (entry.ingredientsFrom === "printed") extra.append(make("p", "snapx-sources", "Ingredient list: the app says it was read from the pack. Compare it with the photo."));
        else if (entry.ingredientSourceUrl) {
            const line = make("p", "snapx-sources", "Ingredient list page sent by the app (it says: " + (kindWord[entry.ingredientSourceKind] || "web page") + "; not checked). Open it and compare before approving: ");
            const link = make("a", "", new URL(entry.ingredientSourceUrl).hostname.replace(/^www\./, ""));
            link.href = entry.ingredientSourceUrl; link.target = "_blank"; link.rel = "noopener noreferrer nofollow";
            line.append(link);
            extra.append(line);
        } else if (entry.ingredientsFrom === "none") extra.append(make("p", "snapx-sources", "No ingredient list was found on any page: add it from the pack or the brand's site."));
        if (entry.sourceUrls.length > 0) {
            const links = make("p", "snapx-sources", "Pages found for this product: ");
            entry.sourceUrls.forEach((url, index) => {
                const link = make("a", "", new URL(url).hostname.replace(/^www\./, ""));
                link.href = url; link.target = "_blank"; link.rel = "noopener noreferrer nofollow";
                links.append(index > 0 ? ", " : "", link);
            });
            extra.append(links);
        } else extra.append(make("p", "snapx-sources", "No web sources were found: this reading comes from the photo alone."));
        const alike = similarProducts({ brand: entry.brandName, name: entry.name }, ctx);
        if (alike.length > 0) extra.append(make("p", "snapx-similar", "Looks like a product already listed: " + alike.map((a) => `"${a.name}" (${Math.round(a.score * 100)}%)`).join(", ") + ". Compare before you approve."));
        if (closeIngredients.size > 0) extra.append(make("p", "snapx-similar", `${closeIngredients.size} ingredient${closeIngredients.size === 1 ? "" : "s"} nearly equal one in the library (hover the dashed pills). Add them as new only if they are different.`));
        const badge = make("p", "snapx-badge", entry.ingredientsFrom ? "Pending review · unverified · everything here was sent by the app: check it against the photo and the page" : "Pending approval · unverified · AI-generated");
        const error = make("p", "snapx-error"); error.setAttribute("role", "alert");

        // ---- what the app's web search found (11 November 2026): editable with Edit & Approve, every link to be opened and checked
        const web = make("details", "snapx-web");
        const hasWeb = !!(entry.description || entry.brandWebsite || entry.brandLogoUrl || entry.productPhotoUrl || entry.primarySourceUrl || entry.secondarySourceUrl || entry.minimumAge || entry.shadeCode);
        web.open = hasWeb;
        web.append(make("summary", "", hasWeb ? "From the app's web search (check each before approving)" : "From the app's web search: nothing was found"));
        const descBox = make("textarea"); descBox.id = "snapxDesc"; descBox.rows = 3; descBox.maxLength = 300; descBox.value = entry.description; descBox.readOnly = true;
        descBox.placeholder = "No description was found";
        const ageBox = make("input"); ageBox.type = "number"; ageBox.id = "snapxAge"; ageBox.min = "0"; ageBox.max = "18"; ageBox.step = "1"; ageBox.value = String(entry.minimumAge || 0); ageBox.readOnly = true;
        const codeBox = input("snapxShadeCode", entry.shadeCode, 16); codeBox.placeholder = "e.g. 119";
        const fdaBox = input("snapxFda", "", 40); fdaBox.placeholder = "Optional: find it on the FDA Philippines portal";
        const fdaLink = make("a", "snapx-link", "Open the FDA Philippines verification portal");
        fdaLink.href = "https://verification.fda.gov.ph/"; fdaLink.target = "_blank"; fdaLink.rel = "noopener noreferrer nofollow";
        const fdaWrap = make("div", "snapx-stack"); fdaWrap.append(fdaBox, fdaLink);
        const pick = (id, chosen) => { const box = make("select"); box.id = id; STATUSES.forEach((s) => { const o = make("option", "", s); o.value = s; box.appendChild(o); }); box.value = chosen; return box; };
        const brandStatus = pick("snapxBrandStatus", "Active"), productStatus = pick("snapxProductStatus", "Active");
        const newBrand = !brandHit;
        const webPair1 = make("div", "snapx-pair"), webPair2 = make("div", "snapx-pair"), webPair3 = make("div", "snapx-pair");
        webPair1.append(field("Minimum age of use", ageBox, entry.minimumAge > 0, "Not stated by the maker"), field("Shade code on the pack", codeBox, !!entry.shadeCode, "No code read"));
        webPair2.append(field("Brand status", brandStatus, true, ""), field("Product status", productStatus, true, ""));
        fdaBox.readOnly = false;                                        // optional, typed by the admin at any time
        const cueText = (pair, i, words) => { pair.children[i].querySelector(".snapx-cue span:last-child").textContent = words; };
        cueText(webPair2, 0, newBrand ? "New brand: you decide" : "Brand already listed: unchanged");
        cueText(webPair2, 1, "First entry: you decide");
        if (!newBrand) brandStatus.disabled = true;
        webPair3.append(field("FDA registration number", fdaWrap, false, "Optional: the admin looks it up"));
        const linkLine = (label, url, note) => {
            const line = make("p", "snapx-sources", label + ": ");
            if (!url) { line.append(make("span", "snapx-none", "not found")); return line; }
            const a = make("a", "", new URL(url).hostname.replace(/^www\./, "") + new URL(url).pathname.slice(0, 40));
            a.href = url; a.target = "_blank"; a.rel = "noopener noreferrer nofollow";
            line.append(a);
            if (note) line.append(make("span", "snapx-none", " (" + note + ")"));
            return line;
        };
        web.append(
            field("Description", descBox, !!entry.description, "No description found"),
            webPair1, webPair2, webPair3,
            linkLine("Official website", entry.brandWebsite, entry.brandWebsite ? "saved as a suggestion; confirm it on the Brand form" : ""),
            linkLine("Brand logo", entry.brandLogoUrl), linkLine("Brand picture", entry.brandImageUrl),
            linkLine("Product photo", entry.productPhotoUrl), linkLine("Official product page", entry.productPageUrl),
            linkLine("Primary source (" + (entry.primarySourceKind === "official" ? "official manufacturer site" : "unverified site") + ")", entry.primarySourceUrl),
            linkLine("Secondary source (e-commerce retailer)", entry.secondarySourceUrl),
            linkLine("Third source", entry.thirdSourceUrl));
        right.append(summary, badge, rows, extra, listField, web, error);

        // ---- actions
        const actions = make("div", "snapx-actions");
        const nav = make("div", "snapx-nav");
        const back = make("button", "icon-btn small", ""); back.type = "button"; back.innerHTML = icon("left"); back.setAttribute("aria-label", "Previous snapshot"); back.disabled = at === 0;
        const next = make("button", "icon-btn small", ""); next.type = "button"; next.innerHTML = icon("right"); next.setAttribute("aria-label", "Next snapshot (skip for now)"); next.disabled = at >= queue.length - 1;
        back.addEventListener("click", () => { if (at > 0) { at--; paint("back"); } });
        next.addEventListener("click", () => { if (at < queue.length - 1) { at++; paint("next"); } });
        nav.append(back, next);
        const reject = make("button", "btn danger", "Reject"); reject.type = "button";
        const edit = make("button", "btn", "Edit & Approve"); edit.type = "button";
        const approve = make("button", "save-btn snapx-approve", "Approve & Publish"); approve.type = "button";
        const toForm = make("button", "btn", "Open in Add Product form"); toForm.type = "button";
        if (typeof options.onOpenForm !== "function") toForm.hidden = true;
        actions.append(nav, make("span", "spacer"), reject, edit, toForm, approve);

        const values = () => {
            const age = Math.round(Number(ageBox.value));
            return {
                brand: brandBox.value.replace(/\s+/g, " ").trim(), name: nameBox.value.replace(/\s+/g, " ").trim(), kind: kindBox.value, shade: cleanHex(shadeBox.value), ingredients: toList(listBox.value),
                description: descBox.value.replace(/\s+/g, " ").trim().slice(0, 300), minimumAge: Number.isFinite(age) && age > 0 && age <= 18 ? age : 0,
                shadeCode: codeBox.value.replace(/[^\p{L}\p{N} .\/-]/gu, "").trim().slice(0, 16), fdaNumber: fdaBox.value.replace(/\s+/g, " ").trim().slice(0, 40),
                brandStatus: brandStatus.value, productStatus: productStatus.value,
                brandWebsite: entry.brandWebsite, brandLogoUrl: entry.brandLogoUrl, brandImageUrl: entry.brandImageUrl, productPhotoUrl: entry.productPhotoUrl,
                sources: snapshotSources(entry)
            };
        };
        const lock = (on) => { busy = on; [reject, edit, toForm, approve, back, next].forEach((b) => { b.disabled = on || (b === back && at === 0) || (b === next && at >= queue.length - 1); }); };

        edit.addEventListener("click", async () => {
            if (!editing) {
                editing = true;
                [brandBox, nameBox, shadeBox, listBox, descBox, ageBox, codeBox].forEach((b) => { b.readOnly = false; });
                web.open = true;
                kindBox.disabled = false;
                listBox.hidden = false;
                card.classList.add("editing");
                edit.textContent = "Save & Approve";
                brandBox.focus();
                return;
            }
            await doApprove();
        });
        approve.addEventListener("click", doApprove);
        toForm.addEventListener("click", () => { const v = values(); finish(); options.onOpenForm(entry, v); });
        reject.addEventListener("click", async () => {
            if (!(await askConfirm({ title: "Reject this snapshot?", message: "The entry and its photo are deleted. Users who scanned it keep their own result.", confirmLabel: "Reject", danger: true }))) return;
            lock(true);
            try { await rejectSnapshot(entry); leave(card, "rejected"); }
            catch (failure) { error.textContent = "It was not rejected. " + describeError(failure); lock(false); }
        });

        async function doApprove() {
            const v = values();
            const problem = checkValues(v, options.context());
            if (problem) {
                error.textContent = problem;
                if (!editing) edit.click();            // a problem opens the boxes, so it can be put right at once
                return;
            }
            error.textContent = "";
            lock(true);
            approve.textContent = "Publishing...";
            try {
                await approveSnapshot(entry, v, options.context());
                approved++;
                leave(card, "approved");
            } catch (failure) {
                console.error("Approving the snapshot failed:", failure);
                error.textContent = "It was not approved. " + describeError(failure);
                approve.textContent = "Approve & Publish";
                lock(false);
            }
        }

        card.append(left, right, actions);
        return card;
    }

    /** The card slides out (green for approved, grey for rejected), the counter goes down, the next card comes in. */
    function leave(card, how) {
        const entry = queue[at];
        card.classList.add(how === "approved" ? "leaving-ok" : "leaving-out");
        if (how === "approved") toast(`"${entry.name}" is published.`, "ok");
        setTimeout(() => {
            queue.splice(at, 1);
            busy = false;
            { const kept = keptPendingCount(); if (kept !== null) rememberPendingCount(kept - 1); }
            if (options.onProcessed) options.onProcessed();
            paint("next");
        }, calm() ? 0 : EXIT_MS);
    }

    // ---- nothing is read until now: one query for the waiting entries
    stage.appendChild(make("div", "snapx-loading", "Loading the waiting snapshots..."));
    loadPending().then((list) => {
        queue = list; total = list.length; at = 0;
        if (queue.length < MAX_LOADED) rememberPendingCount(queue.length);      // all of them were read: the exact number
        if (options.onLoaded) options.onLoaded(queue.length);
        paint("");
    }).catch((error) => {
        stage.replaceChildren(make("p", "snapx-error", "The waiting snapshots could not be read. " + describeError(error)));
        progress.textContent = "";
    });
}
