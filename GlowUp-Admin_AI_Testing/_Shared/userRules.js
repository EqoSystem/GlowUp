// User Management rules (20 November 2026): pure functions, no page and no Firebase, so tests/userRules.test.mjs can run them in node.
// The app writes users/{uid} (firstName, fullName, username, email, skinType, skinTone, skinConditions, knownAllergens, createdAt ...).
// The admin site adds the fields it manages itself: status, statusBefore, role, inBin, statusChangedAt, statusChangedBy.
// A user with none of them is an active "user", so every account the app ever made shows up with no migration.

export const STATUSES = ["active", "archived", "banned", "deleted"];
export const ROLES = ["user", "admin"];
/** Fields only an admin may change (firestore.rules refuse a user who writes them). */
export const ADMIN_FIELDS = ["status", "statusBefore", "role", "inBin", "statusChangedAt", "statusChangedBy"];
export const WEEK_MS = 7 * 24 * 3600 * 1000;
export const TABS = [
    { id: "all", label: "All Users" },
    { id: "active", label: "Active" },
    { id: "archived", label: "Archived" },
    { id: "bin", label: "Recycle Bin" }
];

const ms = (value) => {
    if (!value) return 0;
    if (typeof value.toMillis === "function") return value.toMillis();
    if (value instanceof Date) return value.getTime();
    if (typeof value === "number") return value < 1e12 ? value * 1000 : value;       // seconds or milliseconds
    if (typeof value.seconds === "number") return value.seconds * 1000;
    return 0;
};
const text = (value) => (typeof value === "string" ? value.trim() : "");
const list = (value) => (Array.isArray(value) ? value.map((v) => String(v).trim()).filter(Boolean) : []);

/** The picture to show: the app's own photo (photoData: a small JPEG as data:image/jpeg;base64, New22) or an https link; "" when none. */
export function photoOf(d) {
    const data = text(d && d.photoData);
    if (/^data:image\/jpeg;base64,[A-Za-z0-9+/=]+$/.test(data) && data.length <= 200100) return data;
    const url = text(d && d.photoURL);
    return /^https:\/\//.test(url) ? url : "";
}

/** One users/{uid} document as the page uses it. */
export function normalizeUser(uid, data) {
    const d = data || {};
    const joined = [d.firstName, d.middleName, d.lastName].map(text).filter(Boolean).join(" ");
    const name = text(d.fullName) || joined || text(d.username) || text(d.email) || "Unnamed user";
    const raw = text(d.status).toLowerCase();
    const status = d.inBin === true || raw === "deleted" ? "deleted" : raw === "archived" || raw === "banned" ? raw : "active";
    const skin = {
        skinType: text(d.skinType), skinTone: text(d.skinTone), skinConditions: list(d.skinConditions), knownAllergens: list(d.knownAllergens),
        age: typeof d.age === "number" && d.age > 0 ? d.age : 0, birthday: text(d.birthday)
    };
    return {
        uid, name, username: text(d.username), email: text(d.email), role: text(d.role).toLowerCase() === "admin" ? "admin" : "user",
        status, statusBefore: text(d.statusBefore).toLowerCase(), photoURL: photoOf(d),
        createdAt: ms(d.createdAt), changedAt: ms(d.statusChangedAt), lastActive: ms(d.lastActiveAt || d.lastActive || d.lastSeenAt),
        skin, data: d
    };
}

/** The tab a user belongs to. A banned user shows in All Users (and through the Status filter) only. */
export const inTab = (user, tab) => tab === "all" || (tab === "bin" ? user.status === "deleted" : user.status === tab);

/** Counts for the four cards and the four tabs, with the "+N this week" of each. */
export function summarize(users, now = Date.now()) {
    const since = now - WEEK_MS;
    const out = {};
    TABS.forEach((tab) => {
        const mine = users.filter((u) => inTab(u, tab.id));
        out[tab.id] = { count: mine.length, week: mine.filter((u) => (tab.id === "all" ? u.createdAt : Math.max(u.createdAt, u.changedAt)) >= since).length };
    });
    return out;
}

/** Search (name, username, e-mail, UID) + the Role / Status filter + the tab. Client side only. */
export function filterUsers(users, { tab = "all", query = "", role = "", status = "" } = {}) {
    const q = String(query || "").trim().toLowerCase();
    return users.filter((u) => inTab(u, tab)
        && (!role || u.role === role)
        && (!status || u.status === status)
        && (!q || [u.name, u.username, u.email, u.uid].some((v) => String(v || "").toLowerCase().includes(q))));
}

export const sortUsers = (users) => users.slice().sort((a, b) => (b.createdAt - a.createdAt) || a.name.localeCompare(b.name));

export const statusLabel = (status) => ({ active: "Active", archived: "Archived", banned: "Banned", deleted: "In Recycle Bin" })[status] || "Active";
export const roleLabel = (role) => (role === "admin" ? "Admin" : "User");
export const initialOf = (user) => (String(user.name || user.email || "?").trim().charAt(0) || "?").toUpperCase();

/** The fields written for a status change. `now` is the server timestamp the caller passes in. */
export function statusPatch(user, to, now, adminUid) {
    const patch = { status: to, inBin: to === "deleted", statusChangedAt: now, statusChangedBy: adminUid };
    if (to === "deleted") patch.statusBefore = user.status === "deleted" ? (user.statusBefore || "active") : user.status;
    return patch;
}
/** Where Restore sends a user back to: what the status was before, or active. */
export const restoreTarget = (user) => (user.status === "deleted" && (user.statusBefore === "archived" || user.statusBefore === "banned") ? user.statusBefore : "active");

export const CSV_COLUMNS = [
    { title: "UID", value: (u) => u.uid },
    { title: "Name", value: (u) => u.name },
    { title: "Email", value: (u) => u.email },
    { title: "Role", value: (u) => roleLabel(u.role) },
    { title: "Status", value: (u) => statusLabel(u.status) },
    { title: "Joined At", value: (u) => (u.createdAt ? new Date(u.createdAt).toISOString() : "") },
    { title: "Last Active", value: (u) => (u.lastActive ? new Date(u.lastActive).toISOString() : "") }
];

/** What the edit form may change, cleaned; null + a message when it is not valid. */
export function editProblem({ name, username, role, status }) {
    const n = String(name || "").replace(/\s+/g, " ").trim();
    if (n.length < 2) return { field: "name", text: "Enter the full name (at least 2 letters)." };
    if (n.length > 100) return { field: "name", text: "The name is too long (100 at most)." };
    const u = String(username || "").trim();
    if (u.length > 50) return { field: "username", text: "The username is too long (50 at most)." };
    if (!ROLES.includes(role)) return { field: "role", text: "Choose a role." };
    if (!["active", "archived", "banned"].includes(status)) return { field: "status", text: "Choose a status." };
    return null;
}
