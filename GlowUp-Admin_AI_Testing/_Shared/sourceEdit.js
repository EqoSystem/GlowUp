// What the Edit Product form and "apply to other products" do to a product's sources[]. PURE logic: no page, no network,
// so tests/sourceEdit.test.mjs can check it.
//
// A source stays one line of text, "page title | https://address" (FIRESTORE_SCHEMA.md). Nothing new is stored; the roles are
// worked out from the list each time:
//   official   the line titled "Official website" (always first)
//   primary    the first other line with a web address that can be edited
//   secondary  every line after it (retailers and the like)
//   locked     an FDA Philippines page, or a line with no web address ("Imported document: x.pdf"). Kept as it is, never edited.

export const OFFICIAL_TITLE = "Official website";
export const ROLES = { official: "Official website", primary: "Primary source", secondary: "Secondary source" };
export const MAX_SOURCES = 10;                    // what the product form keeps
export const MAX_SOURCES_RULE = 40;               // what firestore.rules accepts on a product
export const BATCH_SIZE = 400;                    // Firestore takes at most 500 writes in one batch

// Bulk apply is ON ONLY for these brands until the first brand has been tried and reported. [] = every brand.
export const BULK_PILOT_BRANDS = ["Maybelline New York"];
const brandKey = (name) => String(name || "").trim().toLowerCase();
export function bulkAllowed(brandName, pilot = BULK_PILOT_BRANDS) {
    return pilot.length === 0 || pilot.some((name) => brandKey(name) === brandKey(brandName));
}

function cut(line) {
    const raw = String(line || "").trim();
    const at = raw.lastIndexOf(" | ");
    return at >= 0 ? { title: raw.slice(0, at).trim(), address: raw.slice(at + 3).trim() } : { title: "", address: raw };
}
export function sourceUrl(line) {
    const { address } = cut(line);
    return /^https?:\/\/[^\s"'<>]+$/i.test(address) ? address : "";
}
export const sourceTitle = (line) => cut(line).title;
export const isOfficial = (line) => sourceUrl(line) !== "" && sourceTitle(line).toLowerCase() === OFFICIAL_TITLE.toLowerCase();
export const isLocked = (line) => sourceUrl(line) === "" || /fda\.gov\.ph/i.test(String(line));

const urlKey = (url) => String(url || "").trim().replace(/\/+$/, "").toLowerCase();
export const sameUrl = (a, b) => urlKey(a) !== "" && urlKey(a) === urlKey(b);

export function hostOf(url) {
    try { return new URL(url).hostname.replace(/^www\./, ""); } catch (error) { return ""; }
}

/** What an admin typed: { url } when it can be saved, { error } when it cannot. An empty box gives { url: "" }. */
export function checkUrl(text) {
    const value = String(text == null ? "" : text).trim();
    if (!value) return { url: "" };
    if (!/^https:\/\/[^\s"'<>]+$/i.test(value)) return { error: "A source must be a web address that starts with https://" };
    if (value.length > 300) return { error: "That web address is too long (at most 300 characters)." };
    let parsed = null;
    try { parsed = new URL(value); } catch (error) { parsed = null; }
    if (!parsed || !parsed.hostname.includes(".") || parsed.username || parsed.password || value.includes("\\")) return { error: "Use the plain web address (no user name or backslash in it)." };
    return { url: value };
}

/** Where the official and the primary line are: an index, or -1. */
export function rolesOf(list) {
    const lines = Array.isArray(list) ? list : [];
    return {
        official: lines.findIndex(isOfficial),
        primary: lines.findIndex((line) => !isOfficial(line) && !isLocked(line))
    };
}
export const officialOf = (list) => { const at = rolesOf(list).official; return at >= 0 ? sourceUrl(list[at]) : ""; };

/** Puts the official website first. The other lines stay, except one that holds the same address. An empty url removes the line. */
export function withOfficial(list, url) {
    const rest = (list || []).filter((line) => !isOfficial(line) && !sameUrl(sourceUrl(line), url));
    return url ? [`${OFFICIAL_TITLE} | ${url}`, ...rest] : rest;
}

/** Replaces the primary line, or adds one right after the official line. Another line with the same address goes. */
export function withPrimary(list, url) {
    const rest = (list || []).filter((line, at) => at === rolesOf(list).primary || !sameUrl(sourceUrl(line), url));
    const { primary, official } = rolesOf(rest);
    const line = `${hostOf(url)} | ${url}`;
    if (primary >= 0) {
        if (sameUrl(sourceUrl(rest[primary]), url)) return rest;
        return rest.map((old, at) => (at === primary ? line : old));
    }
    const where = official >= 0 ? official + 1 : 0;
    return [...rest.slice(0, where), line, ...rest.slice(where)];
}

/** Adds a line at the end, unless the product has that address already. */
export function withSecondary(list, url) {
    const lines = (list || []).slice();
    return lines.some((line) => sameUrl(sourceUrl(line), url)) ? lines : [...lines, `${hostOf(url)} | ${url}`];
}

export const applyRole = (list, role, url) => (role === "official" ? withOfficial(list, url) : role === "primary" ? withPrimary(list, url) : withSecondary(list, url));

/** One row of the form was edited. An empty url removes the row. A new site gets its own name as the title; the same site keeps its title. */
export function setRow(list, index, url) {
    const lines = (list || []).slice();
    if (!url) { lines.splice(index, 1); return lines; }
    const old = lines[index];
    if (sameUrl(sourceUrl(old), url)) return lines;
    const kept = old && hostOf(sourceUrl(old)) === hostOf(url) ? sourceTitle(old) : "";
    lines[index] = `${kept || hostOf(url)} | ${url}`;
    return lines;
}

/**
 * What applying `url` as `role` would do to the chosen products: `change` will be written, `same` has it already, `full` cannot
 * take one more line. Products that were not chosen are not looked at. Each entry of change: { id, name, before, after }.
 */
export function planApply(products, chosenIds, role, url) {
    const chosen = chosenIds instanceof Set ? chosenIds : new Set(chosenIds);
    const plan = { change: [], same: [], full: [] };
    (products || []).forEach((p) => {
        if (!chosen.has(p.id)) return;
        const before = Array.isArray(p.sources) ? p.sources : [];
        const after = applyRole(before, role, url);
        if (JSON.stringify(before) === JSON.stringify(after)) plan.same.push(p);
        else if (after.length > MAX_SOURCES_RULE) plan.full.push(p);
        else plan.change.push({ id: p.id, name: p.name, before, after });
    });
    return plan;
}

/** The list in pieces of at most `size`, for Firestore's batches. */
export function chunks(list, size = BATCH_SIZE) {
    const out = [];
    for (let at = 0; at < list.length; at += size) out.push(list.slice(at, at + size));
    return out;
}

/** The products in A to Z order and the slice for one page: the whole list is sorted first, then cut. */
export function brandPageOf(products, page, size) {
    const sorted = (products || []).slice().sort((a, b) => String(a.name || "").localeCompare(String(b.name || ""), undefined, { sensitivity: "base" }));
    const pages = Math.max(1, Math.ceil(sorted.length / size));
    const now = Math.min(Math.max(1, page || 1), pages);
    return { rows: sorted.slice((now - 1) * size, now * size), page: now, pages, total: sorted.length, showPager: sorted.length > size, showSizes: sorted.length > 10 };
}

/** Products grouped by cosmetic type: groups A to Z (no type last), products A to Z inside each. Nothing is paged. */
export function groupByType(products) {
    const groups = new Map();
    (products || []).forEach((p) => {
        const label = String(p.cosmeticType || "").trim();
        const key = label.toLowerCase();
        if (!groups.has(key)) groups.set(key, { key, label: label || "No type set", rows: [] });
        groups.get(key).rows.push(p);
    });
    const byName = (a, b) => String(a.name || "").localeCompare(String(b.name || ""), undefined, { sensitivity: "base" });
    return Array.from(groups.values())
        .sort((a, b) => (a.key === "") - (b.key === "") || a.label.localeCompare(b.label, undefined, { sensitivity: "base" }))
        .map((group) => ({ ...group, rows: group.rows.sort(byName) }));
}
