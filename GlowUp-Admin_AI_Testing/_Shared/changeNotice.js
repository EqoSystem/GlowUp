// Change notices for the app (New23, 22 November 2026). Pure functions, no page and no Firebase, so tests/changeNotice.test.mjs runs them in node.
// Every admin save that touches a product or an ingredient adds ONE short notice to the same batch (adminStore.js). The app's
// Notifications screen lists it live. The text is cut to the limits in firestore.rules (title 80, text 300): a notice that did not
// fit would be refused and would fail the whole save, so this module must never produce more.

export const TITLE_MAX = 80, BODY_MAX = 300;
const QUIET_FIELDS = new Set(["scans", "updatedAt", "lastScannedAt", "scanCount"]);   // counters and stamps are not something a user needs to hear about
const KINDS = { products: "product", ingredients: "ingredient" };
const WORD = { product: ["product", "products"], ingredient: ["ingredient", "ingredients"] };

const cut = (text, max) => { const t = String(text || "").replace(/\s+/g, " ").trim(); return t.length <= max ? t : t.slice(0, max - 1).trimEnd() + "…"; };
const meaningful = (item) => item.action === "delete" || !Array.isArray(item.fields) || item.fields.length === 0 || item.fields.some((f) => !QUIET_FIELDS.has(f));

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
