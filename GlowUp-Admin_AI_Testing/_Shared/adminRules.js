// Admin Management rules: pure functions, no page and no Firebase, so tests/adminRules.test.mjs can run them in node.
// admins/{uid} is made when an admin approves a request on the login page's "Sign Up" (email, name, approvedBy, approvedAt).
// This page adds what it manages: status (active | archived | deleted = in the Recycle Bin), statusBefore, inBin, statusChangedAt, statusChangedBy;
// the admin's own pages add lastActiveAt. An admin with no status is active. `protected` (set by hand in the console) cannot be changed here.
// firestore.rules enforce all of this; the page only mirrors it so it never offers what the rules would refuse.

import { ONLINE_MS, WEEK_MS, initialOf } from "./userRules.js";

export { initialOf };
export const STATUSES = ["active", "archived", "deleted"];
export const TABS = [
    { id: "all", label: "All Admins" },
    { id: "active", label: "Active" },
    { id: "archived", label: "Archived" },
    { id: "bin", label: "Recycle Bin" },
    { id: "online", label: "Online Now" }
];

const ms = (value) => {
    if (!value) return 0;
    if (typeof value.toMillis === "function") return value.toMillis();
    if (value instanceof Date) return value.getTime();
    if (typeof value === "number") return value < 1e12 ? value * 1000 : value;
    if (typeof value.seconds === "number") return value.seconds * 1000;
    return 0;
};
const text = (value) => (typeof value === "string" ? value.trim() : "");

/** One admins/{uid} document as the page uses it. */
export function normalizeAdmin(uid, data) {
    const d = data || {};
    const raw = text(d.status).toLowerCase();
    const status = d.inBin === true || raw === "deleted" ? "deleted" : raw === "archived" ? "archived" : "active";
    const email = text(d.email);
    return {
        uid, name: text(d.name) || email || "Unnamed admin", email, status, statusBefore: text(d.statusBefore).toLowerCase(),
        protectedAdmin: d.protected === true, approvedBy: text(d.approvedBy),
        createdAt: ms(d.approvedAt), changedAt: ms(d.statusChangedAt), lastActive: ms(d.lastActiveAt), data: d
    };
}

export const isOnline = (admin, now = Date.now()) => admin.status === "active" && admin.lastActive > 0 && now - admin.lastActive <= ONLINE_MS;
export const inTab = (admin, tab, now = Date.now()) => tab === "all" || (tab === "online" ? isOnline(admin, now) : tab === "bin" ? admin.status === "deleted" : admin.status === tab);

/** Counts for the cards and the tabs, with the "+N this week" of each. */
export function summarize(admins, now = Date.now()) {
    const since = now - WEEK_MS;
    const out = {};
    TABS.forEach((tab) => {
        const mine = admins.filter((a) => inTab(a, tab.id, now));
        out[tab.id] = { count: mine.length, week: tab.id === "online" ? 0 : mine.filter((a) => (tab.id === "all" ? a.createdAt : Math.max(a.createdAt, a.changedAt)) >= since).length };
    });
    return out;
}

/** Search (name, e-mail, UID) + the Status filter + the tab. Client side only. */
export function filterAdmins(admins, { tab = "all", query = "", status = "" } = {}) {
    const q = String(query || "").trim().toLowerCase();
    return admins.filter((a) => inTab(a, tab)
        && (!status || a.status === status)
        && (!q || [a.name, a.email, a.uid].some((v) => String(v || "").toLowerCase().includes(q))));
}

export const sortAdmins = (admins) => admins.slice().sort((a, b) => (b.createdAt - a.createdAt) || a.name.localeCompare(b.name));

export const statusLabel = (status) => ({ active: "Active", archived: "Archived", deleted: "In Recycle Bin" })[status] || "Active";

/** Who the page may change: never yourself, never an admin marked protected in the console (the rules refuse both). */
export const canChange = (admin, myUid) => admin.uid !== myUid && !admin.protectedAdmin;

/** The fields written for a status change. `now` is the server timestamp the caller passes in. */
export function statusPatch(admin, to, now, adminUid) {
    const patch = { status: to, inBin: to === "deleted", statusChangedAt: now, statusChangedBy: adminUid };
    if (to === "deleted") patch.statusBefore = admin.status === "deleted" ? (admin.statusBefore || "active") : admin.status;
    return patch;
}
/** Where Restore sends an admin back to: what the status was before, or active. */
export const restoreTarget = (admin) => (admin.status === "deleted" && admin.statusBefore === "archived" ? "archived" : "active");

export const CSV_COLUMNS = [
    { title: "UID", value: (a) => a.uid },
    { title: "Name", value: (a) => a.name },
    { title: "Email", value: (a) => a.email },
    { title: "Status", value: (a) => statusLabel(a.status) },
    { title: "Approved At", value: (a) => (a.createdAt ? new Date(a.createdAt).toISOString() : "") },
    { title: "Last Active", value: (a) => (a.lastActive ? new Date(a.lastActive).toISOString() : "") }
];

/** What the edit form may change, cleaned; null when valid. Only the name and the status (never the e-mail). */
export function editProblem({ name, status }) {
    const n = String(name || "").replace(/\s+/g, " ").trim();
    if (n.length < 2) return { field: "name", text: "Enter the full name (at least 2 letters)." };
    if (n.length > 80) return { field: "name", text: "The name is too long (80 at most)." };
    if (!["active", "archived"].includes(status)) return { field: "status", text: "Choose a status." };
    return null;
}
