// THE ADMIN ACTIVITY LOG: who changed what on this website, newest first. The records are written by adminStore.js in the same batch as
// every change (collection "auditLog"); the rules let nobody change or delete them. This window only reads the latest ones.
import { db } from "../_Database/firebase-console.js";
import { collection, query, orderBy, limit } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { getDocs, AUDIT_COLLECTION } from "./adminStore.js";
import { dialogFrame } from "./adminShell.js";

const SHOW = 60;
const ACTION_WORDS = { set: "saved", update: "changed", delete: "deleted for good" };
const COLLECTION_WORDS = { brands: "brand", products: "product", ingredients: "ingredient", ingredientClassifications: "classification", archive: "archive entry", lists: "list", pending_products: "photo review" };

function make(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
}

function whenOf(record) {
    const seconds = record.at && typeof record.at.toDate === "function" ? record.at.toDate().getTime() / 1000 : Number(record.atUnix) || 0;
    return seconds ? new Date(seconds * 1000).toLocaleString() : "";
}

function lineOf(item) {
    const kind = COLLECTION_WORDS[item.collection] || item.collection || "entry";
    const binned = Array.isArray(item.fields) && item.fields.includes("binned") ? " (Recycle bin)" : "";
    return `${kind} ${item.name ? `"${item.name}"` : item.docId}: ${ACTION_WORDS[item.action] || item.action}${binned}`
        + (Array.isArray(item.fields) && item.fields.length > 0 && item.action !== "delete" ? ` (${item.fields.slice(0, 6).join(", ")}${item.fields.length > 6 ? ", ..." : ""})` : "");
}

/** Opens the window with the latest records. */
export async function openAuditLog() {
    const frame = dialogFrame("Admin activity log", () => frame.close());
    frame.box.classList.add("wide");
    const intro = make("p", "modal-intro", "Every change made on this website is recorded here with the admin who made it. Records cannot be edited or deleted. Changes made by the phone app (new scans, pending entries) are not admin changes and are not listed.");
    const list = make("div", "audit-list");
    list.append(make("p", "muted small", "Loading..."));
    const foot = make("div", "modal-actions");
    const close = make("button", "cancel-btn", "Close");
    close.type = "button";
    close.addEventListener("click", () => frame.close());
    foot.append(close);
    frame.box.append(intro, list, foot);
    try {
        const snapshot = await getDocs(query(collection(db, AUDIT_COLLECTION), orderBy("atUnix", "desc"), limit(SHOW)));
        list.replaceChildren();
        if (snapshot.empty) { list.append(make("p", "muted small", "No changes recorded yet. Recording started on 6 November 2026.")); return; }
        snapshot.forEach((d) => {
            const record = d.data();
            const row = make("details", "audit-row");
            const summary = make("summary", "");
            summary.append(make("b", "", whenOf(record)), document.createTextNode(` · ${record.email || record.uid || "unknown admin"} · ${record.count || 0} ${record.count === 1 ? "change" : "changes"}${record.page ? " · " + record.page.replace(/\.html$/, "") : ""}`));
            row.append(summary);
            const items = make("ul", "confirm-list");
            (Array.isArray(record.items) ? record.items : []).slice(0, 40).forEach((item) => items.append(make("li", "", lineOf(item))));
            if ((record.items || []).length > 40) items.append(make("li", "muted", `and ${(record.items || []).length - 40} more`));
            row.append(items);
            list.append(row);
        });
    } catch (error) {
        console.error("The activity log could not be read:", error);
        list.replaceChildren(make("p", "table-message error", "The activity log could not be read. Check the connection and try again."));
    }
}
