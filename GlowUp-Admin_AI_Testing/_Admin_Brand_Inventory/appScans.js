// "Scanned by the app": products the staff capture screen of the phone app just sent (source "staff-capture").
// Its own live listener, so a new scan shows here within a second, with no refresh and without waiting for the inventory table.
import { db } from "../_Database/firebase-console.js";
import { collection, query, where } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { onSnapshot } from "../_Shared/adminStore.js";
import { escapeHtml, timeAgo } from "../_Shared/adminCommon.js";

const SHOW = 12;

export function startAppScans({ host, count, onOpen }) {
    if (!host) return () => { };
    let rows = [];
    const paint = () => {
        const waiting = rows.filter((r) => !r.binned && !r.reviewed).sort((a, b) => b.at - a.at);
        if (count) count.textContent = waiting.length ? String(waiting.length) : "";
        if (waiting.length === 0) {
            host.innerHTML = '<p class="field-hint app-scan-empty">Nothing waiting. A product scanned in the app appears here at once.</p>';
            return;
        }
        host.innerHTML = waiting.slice(0, SHOW).map((r) =>
            `<button type="button" class="app-scan-row" data-open="${escapeHtml(r.id)}">`
            + `<span class="app-scan-name">${escapeHtml(r.name || "Unnamed product")}</span>`
            + `<span class="app-scan-meta">${escapeHtml([r.brand, r.barcode ? "Barcode " + r.barcode : "No barcode", r.at ? timeAgo(r.at * 1000) : ""].filter(Boolean).join(" · "))}</span>`
            + "</button>").join("")
            + (waiting.length > SHOW ? `<p class="field-hint">${waiting.length - SHOW} more are in the table under Waiting for review.</p>` : "");
    };
    host.addEventListener("click", (event) => {
        const row = event.target.closest("[data-open]");
        if (row && onOpen) onOpen(row.dataset.open);
    });
    host.innerHTML = '<p class="field-hint app-scan-empty">Listening for scans from the app...</p>';
    return onSnapshot(query(collection(db, "products"), where("source", "==", "staff-capture")), (snapshot) => {
        rows = snapshot.docs.map((d) => {
            const f = d.data();
            return { id: d.id, name: String(f.name || ""), brand: String(f.brandName || ""), barcode: String(f.barcode || ""),
                at: Number(f.addedAtUnix) || 0, reviewed: f.reviewed === true, binned: f.binned === true };
        });
        paint();
    }, (error) => {
        console.error("Error watching the app's scans: ", error);
        host.innerHTML = '<p class="table-message error">Scans from the app cannot be shown right now.</p>';
    });
}
