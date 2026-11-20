// Maintenance Mode rules (New22, 20 November 2026): pure functions, no page and no Firebase (tests/maintenanceRules.test.mjs).
// The document is systemConfig/maintenanceMode: { isActive, scheduledStart, scheduledEnd, bannerTitle, customMessage,
// notifyUsers, notifiedAt, updatedBy, updatedAt }. The app (Scripts/Common/MaintenanceWatcher.cs) reads the same fields.

export const TITLE_MAX = 80;
export const MESSAGE_MAX = 300;

export const msOf = (value) => {
    if (!value) return 0;
    if (typeof value.toMillis === "function") return value.toMillis();
    if (value instanceof Date) return value.getTime();
    if (typeof value === "number") return value;
    if (typeof value.seconds === "number") return value.seconds * 1000;
    return 0;
};

/** "online" | "scheduled" | "active", from the document (missing document = online). */
export function maintenanceState(data, now = Date.now()) {
    if (!data) return "online";
    if (data.isActive === true) return "active";
    return msOf(data.scheduledStart) > now ? "scheduled" : "online";
}

export const STATE_TEXT = { online: "System Online", scheduled: "Update Scheduled", active: "Maintenance Active" };

/**
 * What the watchdog must write now, or null. A scheduled start that has come switches maintenance on; an end time that has
 * passed switches it off (and clears the notice, so "Send Advance Notice" opens again).
 */
export function watchdogPatch(data, now = Date.now()) {
    if (!data) return null;
    const start = msOf(data.scheduledStart), end = msOf(data.scheduledEnd);
    if (data.isActive === true && end > 0 && end <= now) return { isActive: false, scheduledStart: null, scheduledEnd: null, notifyUsers: false, notifiedAt: null };
    if (data.isActive !== true && start > 0 && start <= now) {
        if (end > 0 && end <= now) return { scheduledStart: null, scheduledEnd: null, notifyUsers: false, notifiedAt: null };   // the whole window passed with no tab open
        return { isActive: true };
    }
    return null;
}

/** "2026-11-20T14:30" for a datetime-local input (local time), or "". */
export function toLocalInput(value) {
    const ms = msOf(value);
    if (!ms) return "";
    const d = new Date(ms), pad = (n) => String(n).padStart(2, "0");
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}
export const fromLocalInput = (text) => { if (!text) return 0; const ms = new Date(text).getTime(); return Number.isFinite(ms) ? ms : 0; };

/**
 * The form checked before a save. enable = the toggle. Returns { problem, field } or the plan
 * { isActive, startMs, endMs } (startMs 0 = start now).
 */
export function planSave({ enable, start, end, title, message }, now = Date.now()) {
    const startMs = fromLocalInput(start), endMs = fromLocalInput(end);
    if (String(title || "").length > TITLE_MAX) return { problem: `The banner title is longer than ${TITLE_MAX} characters.`, field: "title" };
    if (String(message || "").length > MESSAGE_MAX) return { problem: `The message is longer than ${MESSAGE_MAX} characters.`, field: "message" };
    if (!enable) return { isActive: false, startMs: 0, endMs: 0 };
    if (startMs && startMs <= now) return { problem: "The start time has already passed. Clear it to start now, or choose a later time.", field: "start" };
    if (startMs && !endMs) return { problem: "Choose when the app is expected back online.", field: "end" };
    if (endMs && endMs <= Math.max(now, startMs)) return { problem: "Expected back online must be after the start.", field: "end" };
    return { isActive: !startMs, startMs, endMs };
}

/** The notice the app shows before maintenance. */
export function noticeText(data) {
    const fmt = (ms) => new Date(ms).toLocaleString(undefined, { day: "numeric", month: "short", hour: "numeric", minute: "2-digit" });
    const start = msOf(data && data.scheduledStart), end = msOf(data && data.scheduledEnd);
    if (!start) return "";
    return `Heads up! GlowUp will be unavailable for updates starting at ${fmt(start)}${end ? ` until ${fmt(end)}` : ""}.`;
}
