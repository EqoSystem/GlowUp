// Change notices for the app. Pure functions, no page and no Firebase, so tests/changeNotice.test.mjs runs them in node.
// Every admin save that touches a product or an ingredient adds ONE short notice to the same batch (adminStore.js). The app's
// Notifications screen lists it live. The text is cut to the limits in firestore.rules (title 80, text 300): a notice that did not
// fit would be refused and would fail the whole save, so this module must never produce more.

export const TITLE_MAX = 80, BODY_MAX = 300;
const QUIET_FIELDS = new Set(["scans", "updatedAt", "lastScannedAt", "scanCount"]);   // counters and stamps are not something a user needs to hear about
const KINDS = { products: "product", ingredients: "ingredient" };
const WORD = { product: ["product", "products"], ingredient: ["ingredient", "ingredients"] };

// names can come from the app (a user's own scan creates a product), so markup and control characters are removed here;
// the app also shows notices as plain text
const plain = (text) => String(text || "").replace(/[\u0000-\u001f\u007f<>]/g, " ");
const cut = (text, max) => { const t = plain(text).replace(/\s+/g, " ").trim(); return t.length <= max ? t : t.slice(0, max - 1).trimEnd() + "…"; };
// 28 Nov 2026 (owner: the app showed "165 products updated" again and again with nothing to read): an automatic job (the
// recommendation flags, the rule-filled cautions, the review marks) changes many entries at once and is not news for a user. Only a
// change to what the app SHOWS makes a notice: these fields. A new entry ("set" with every field) always has some of them.
const SHOWN_FIELDS = new Set(["name", "brandName", "brandId", "cosmeticType", "ingredients", "shades", "shadeCodes", "shadeNames", "status", "barcode", "description",
    "imageUrl", "minAge", "binned", "aliases", "purpose", "riskLevel", "cautionNote", "allergenTags", "avoidForSkinTypes", "classificationId", "commonUses", "chemicalDescription"]);
const meaningful = (item) => item.action === "delete" || !Array.isArray(item.fields) || item.fields.some((f) => !QUIET_FIELDS.has(f) && SHOWN_FIELDS.has(f));

/**
 * The notice for one batch of audit items ({ collection, docId, action, fields, name }), or null when nothing the app shows changed.
 * Products win over ingredients when a save touches both.
 */
export function noticeFor(items) {
    const hits = (Array.isArray(items) ? items : []).filter((i) => i && KINDS[i.collection] && meaningful(i));
    if (hits.length === 0) return null;
    const kind = hits.some((i) => KINDS[i.collection] === "product") ? "product" : "ingredient";
    const mine = hits.filter((i) => KINDS[i.collection] === kind);
    const allGone = mine.every((i) => i.action === "delete");
    const verb = allGone ? "removed" : "updated";
    const [one, many] = WORD[kind];
    const names = [...new Set(mine.map((i) => cut(i.name, 60)).filter(Boolean))];
    const title = mine.length === 1 ? `${one[0].toUpperCase()}${one.slice(1)} ${verb}` : `${mine.length} ${many} ${verb}`;
    let body;
    if (names.length === 0) body = mine.length === 1 ? `A ${one} was ${verb} in the GlowUp catalogue.` : `${mine.length} ${many} were ${verb} in the GlowUp catalogue.`;
    else if (names.length <= 3) body = `${names.join(", ")} ${names.length === 1 && mine.length === 1 ? "was" : "were"} ${verb}${mine.length > names.length ? ` (and ${mine.length - names.length} more)` : ""}.`;
    else body = `${names.slice(0, 3).join(", ")} and ${mine.length - 3} more ${many} were ${verb}.`;
    return { kind, title: cut(title, TITLE_MAX), body: cut(body, BODY_MAX) };
}
