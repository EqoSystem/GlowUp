// Maintenance Mode on the admin site (New22, 20 November 2026).
// ONE live listener per page on systemConfig/maintenanceMode feeds both surfaces of this page: the status pill in the header and
// the "System Status & Maintenance Mode" card in Quick settings. Every state shown (pill colour, switch, disabled inputs, the
// notice button) is read from the latest snapshot, never from what was last typed.
// Watchdog: there is no Cloud Function (free tier), so every open admin tab checks the times on each snapshot and every 15 s:
// a scheduled start that has come switches maintenance on, a passed end switches it off. A Cloud Function on a schedule would
// be the production-safe way; with no admin tab open the switch waits until the next admin opens the site.
// Writes go through adminStore.writeBatch, so each save is in the audit log.

import { auth, db } from "../_Database/firebase-console.js";
import { onAuthStateChanged } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-auth.js";
import * as fb from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { writeBatch } from "./adminStore.js";
import { askConfirm, toast } from "./adminShell.js";
import { maintenanceState, STATE_TEXT, watchdogPatch, planSave, toLocalInput, noticeText, msOf, TITLE_MAX, MESSAGE_MAX } from "./maintenanceRules.js";

const REF = () => fb.doc(db, "systemConfig", "maintenanceMode");
let data = null, loaded = false, failure = null, stop = null, started = false, tick = 0, saving = false;
let pill = null;
const subscribers = new Set();
const emit = () => subscribers.forEach((fn) => { try { fn(); } catch (e) { console.error(e); } });
export const onMaintenanceChanged = (fn) => { subscribers.add(fn); return () => subscribers.delete(fn); };
export const maintenanceSnapshot = () => ({ data, loaded, failure });

function listen() {
    unlisten();
    stop = fb.onSnapshot(REF(), (snap) => {
        data = snap.exists() ? snap.data() : null;
        loaded = true; failure = null;
        emit();
        runWatchdog();
    }, (error) => {
        console.warn("Maintenance state could not be read:", error && error.code ? error.code : error);
        failure = error; loaded = true; emit();
    });
    tick = window.setInterval(runWatchdog, 15000);
}
function unlisten() { if (stop) { stop(); stop = null; } window.clearInterval(tick); tick = 0; }

let watchdogBusy = false;
async function runWatchdog() {
    const patch = watchdogPatch(data);
    if (!patch || watchdogBusy || !auth.currentUser) return;
    watchdogBusy = true;
    try {
        const batch = writeBatch(db);
        batch.set(REF(), Object.assign(patch, { updatedBy: auth.currentUser.uid, updatedAt: fb.serverTimestamp() }), { merge: true });
        await batch.commit();
    } catch (error) { console.warn("Maintenance watchdog write failed:", error && error.code ? error.code : error); }
    finally { watchdogBusy = false; }
}

/** Starts the listener for this page (signed in) and stops it on sign-out and when the page is left. */
export function watchMaintenance() {
    if (started) return;
    started = true;
    onAuthStateChanged(auth, (user) => { if (user) listen(); else { unlisten(); data = null; loaded = false; emit(); } });
    window.addEventListener("pagehide", unlisten);
    window.addEventListener("pageshow", (event) => { if (event.persisted && auth.currentUser && !stop) listen(); });
}

// ---------- the header pill ----------
export function mountMaintenancePill(tools, openCard) {
    if (!tools || tools.querySelector(".mm-pill")) return;
    pill = document.createElement("button");
    pill.type = "button";
    pill.className = "mm-pill";
    pill.id = "maintenancePill";
    pill.innerHTML = '<span class="mm-dot" aria-hidden="true"></span><span class="mm-pill-text"></span>';
    const bell = tools.querySelector(".hdr-bell"), gear = tools.querySelector(".quick-gear");
    if (bell) bell.before(pill); else if (gear) gear.before(pill); else tools.append(pill);
    pill.addEventListener("click", openCard);
    const paint = () => {
        const state = loaded && !failure ? maintenanceState(data) : "online";
        pill.dataset.state = state;
        pill.hidden = !loaded || !!failure;
        pill.querySelector(".mm-pill-text").textContent = STATE_TEXT[state];
        pill.setAttribute("aria-label", STATE_TEXT[state] + ". Open System Status and Maintenance Mode.");
    };
    subscribers.add(paint);
    paint();
    window.setInterval(paint, 30000);                 // a scheduled start turns the pill by itself even without a write
}

// ---------- the Quick settings card ----------
const node = (tag, className, text) => { const el = document.createElement(tag); if (className) el.className = className; if (text !== undefined) el.textContent = text; return el; };
const fmt = (ms) => new Date(ms).toLocaleString(undefined, { day: "numeric", month: "short", year: "numeric", hour: "numeric", minute: "2-digit" });

/** Builds the card into `host` once, then repaints it from every snapshot. */
export function paintMaintenanceView(host) {
    if (!host) return;
    if (!host._mm) host._mm = buildCard(host);
    host._mm.paint();
}

function counted(box, max) {
    box.maxLength = max;
    const counter = node("span", "counter");
    const paint = () => { counter.textContent = `${box.value.length} / ${max}`; counter.classList.toggle("is-full", box.value.length >= max); };
    box.addEventListener("input", paint);
    return { counter, paint };
}

function buildCard(host) {
    host.replaceChildren();
    const card = node("section", "mm-card");
    card.id = "maintenance-card";
    card.setAttribute("aria-labelledby", "mmTitle");
    const title = node("h4", "mm-title", "System Status & Maintenance Mode");
    title.id = "mmTitle";
    const status = node("p", "mm-status");
    status.setAttribute("role", "status");
    const intro = node("p", "mm-intro", "While maintenance is on, the app shows a maintenance screen and nobody can log in. Admins can still use this portal.");

    const toggleRow = node("div", "mm-row");
    const toggleLabel = node("span", "mm-row-label", "Enable Maintenance Mode");
    toggleLabel.id = "mmToggleLabel";
    const toggle = node("button", "switch");
    toggle.type = "button"; toggle.id = "mmToggle"; toggle.setAttribute("role", "switch"); toggle.setAttribute("aria-labelledby", "mmToggleLabel");
    toggle.innerHTML = "<i></i>";
    toggleRow.append(toggleLabel, toggle);

    const field = (label, control, id, hint) => {
        const wrap = node("div", "field");
        const l = node("label", "", label); l.htmlFor = id; control.id = id;
        wrap.append(l, control);
        if (hint) wrap.append(hint);
        return wrap;
    };
    const start = node("input"); start.type = "datetime-local";
    const end = node("input"); end.type = "datetime-local";
    const schedule = node("div", "mm-grid");
    schedule.append(
        field("Start time (optional)", start, "mmStart", node("small", "field-hint", "Empty: maintenance starts as soon as you save.")),
        field("Expected back online", end, "mmEnd", node("small", "field-hint", "Needed when you schedule a start. Maintenance ends by itself at this time.")));

    const titleBox = node("input"); titleBox.type = "text"; titleBox.placeholder = "Scheduled App Update"; titleBox.autocomplete = "off";
    const messageBox = node("textarea"); messageBox.rows = 3; messageBox.placeholder = "We're updating our ingredient database. Thank you for waiting.";
    const tc = counted(titleBox, TITLE_MAX), mc = counted(messageBox, MESSAGE_MAX);
    const titleRow = node("div", "hint-row"); titleRow.append(tc.counter);
    const messageRow = node("div", "hint-row"); messageRow.append(mc.counter);
    const titleField = field("Banner title", titleBox, "mmBannerTitle", titleRow);
    const messageField = field("Message for app users", messageBox, "mmMessage", messageRow);

    const notice = node("button", "btn primary mm-notice");
    notice.type = "button"; notice.id = "mmNotice";
    const noticeHint = node("small", "field-hint mm-notice-hint");

    const callout = node("div", "mm-callout");
    callout.setAttribute("role", "note");
    callout.textContent = "Administrators can still log in and use the portal while Maintenance Mode is active.";

    const problem = node("p", "form-error"); problem.setAttribute("role", "alert");
    const save = node("button", "save-btn mm-save", "Save changes");
    save.type = "button"; save.id = "mmSave";

    card.append(title, status, intro, toggleRow, schedule, titleField, messageField, notice, noticeHint, callout, problem, save);

    // ---- announcements (app update / product change) for the app's Notifications screen ----
    const ann = node("section", "mm-card mm-announce");
    ann.setAttribute("aria-labelledby", "annTitle");
    const annTitle = node("h4", "mm-title", "Announcement to app users"); annTitle.id = "annTitle";
    const annIntro = node("p", "mm-intro", "Appears in every user's Notifications in the app.");
    const kind = node("select"); [["update", "App update"], ["product", "Product change"]].forEach(([v, l]) => { const o = node("option", "", l); o.value = v; kind.append(o); });
    const annTitleBox = node("input"); annTitleBox.type = "text"; annTitleBox.autocomplete = "off"; annTitleBox.placeholder = "New shades added";
    const annBody = node("textarea"); annBody.rows = 3; annBody.placeholder = "What changed, in one or two sentences.";
    const ac = counted(annTitleBox, TITLE_MAX), bc = counted(annBody, MESSAGE_MAX);
    const r1 = node("div", "hint-row"); r1.append(ac.counter);
    const r2 = node("div", "hint-row"); r2.append(bc.counter);
    const annProblem = node("p", "form-error"); annProblem.setAttribute("role", "alert");
    const post = node("button", "btn primary", "Post announcement"); post.type = "button"; post.id = "annPost";
    ann.append(annTitle, annIntro, field("Kind", kind, "annKind"), field("Title", annTitleBox, "annTitleBox", r1), field("Message", annBody, "annBody", r2), annProblem, post);

    host.append(card, ann);

    let dirty = false;
    [start, end, titleBox, messageBox].forEach((box) => box.addEventListener("input", () => { dirty = true; problem.style.display = "none"; }));
    const fill = () => {
        start.value = toLocalInput(data && data.scheduledStart);
        end.value = toLocalInput(data && data.scheduledEnd);
        titleBox.value = (data && data.bannerTitle) || "";
        messageBox.value = (data && data.customMessage) || "";
        tc.paint(); mc.paint(); ac.paint(); bc.paint();
        dirty = false;
    };
    const showProblem = (text, box) => { problem.textContent = text; problem.style.display = text ? "block" : "none"; if (box) box.focus(); };

    const busy = (on, button, label) => {
        saving = on;
        [toggle, start, end, titleBox, messageBox, notice, save].forEach((c) => { c.disabled = on || c._lockedByState; });
        if (button) button.innerHTML = on ? `<span class="btn-spin" aria-hidden="true"></span>${label}` : button._label;
    };
    save._label = "Save changes";

    async function write(enable) {
        const plan = planSave({ enable, start: start.value, end: end.value, title: titleBox.value, message: messageBox.value });
        if (plan.problem) { showProblem(plan.problem, { start, end, title: titleBox, message: messageBox }[plan.field]); return false; }
        if (plan.isActive && !(data && data.isActive) && !(await askConfirm({
            title: "Start maintenance now?", message: "Everyone using the app is shown the maintenance screen and cannot log in until it ends.",
            confirmLabel: "Start maintenance", danger: true, badgeIcon: "alert"
        }))) return false;
        if (!enable && data && (data.isActive || maintenanceState(data) === "scheduled") && !(await askConfirm({
            title: "End maintenance?", message: "The app opens for everyone again and the scheduled time is cleared.", confirmLabel: "End maintenance", badgeIcon: "check"
        }))) return false;
        busy(true, save, "Saving...");
        try {
            const keepNotice = enable && data && data.notifyUsers === true;
            const batch = writeBatch(db);
            batch.set(REF(), {
                isActive: plan.isActive,
                scheduledStart: plan.startMs ? fb.Timestamp.fromMillis(plan.startMs) : null,
                scheduledEnd: plan.endMs ? fb.Timestamp.fromMillis(plan.endMs) : null,
                bannerTitle: titleBox.value.trim(), customMessage: messageBox.value.trim(),
                notifyUsers: keepNotice, notifiedAt: keepNotice ? (data.notifiedAt || null) : null,
                updatedBy: auth.currentUser.uid, updatedAt: fb.serverTimestamp()
            });
            await batch.commit();
            dirty = false;
            toast(plan.isActive ? "Maintenance Mode is on." : plan.startMs ? "Maintenance scheduled." : "Maintenance settings saved.", "ok");
            return true;
        } catch (error) {
            console.error("Maintenance save failed:", error && error.code ? error.code : error);
            toast(String(error && error.code || "").includes("permission-denied") ? "Failed to save: the database refused it. The latest rules may not be published yet." : "Failed to save. Please try again.", "error");
            return false;
        } finally { busy(false, save); paint(); }
    }

    toggle.addEventListener("click", async () => {
        if (saving) return;
        const want = toggle.getAttribute("aria-checked") !== "true";
        toggle.setAttribute("aria-checked", String(want));          // the snapshot sets the real state again after the write
        const done = await write(want);
        if (!done) paint();
    });
    save.addEventListener("click", () => { if (!saving) write(toggle.getAttribute("aria-checked") === "true"); });

    notice.addEventListener("click", async () => {
        if (saving || !data) return;
        const start = msOf(data.scheduledStart);
        if (!start) { showProblem("Save a scheduled start first: the notice tells users when the app will be away."); return; }
        busy(true, notice, "Sending...");
        try {
            const batch = writeBatch(db);
            batch.set(REF(), { notifyUsers: true, notifiedAt: fb.serverTimestamp(), updatedBy: auth.currentUser.uid, updatedAt: fb.serverTimestamp() }, { merge: true });
            batch.set(fb.doc(fb.collection(db, "announcements")), {
                kind: "maintenance", title: (data.bannerTitle || "Scheduled App Update").slice(0, TITLE_MAX), body: noticeText(data).slice(0, MESSAGE_MAX),
                startsAt: data.scheduledStart || null, endsAt: data.scheduledEnd || null, createdAt: fb.serverTimestamp(), createdBy: auth.currentUser.uid
            });
            await batch.commit();
            toast("Advance notice sent to app users.", "ok");
        } catch (error) {
            console.error("Notice failed:", error && error.code ? error.code : error);
            toast("Failed to send the notice. Please try again.", "error");
        } finally { busy(false, notice); paint(); }
    });

    post.addEventListener("click", async () => {
        const t = annTitleBox.value.trim(), b = annBody.value.trim();
        if (!t) { annProblem.textContent = "Write a title."; annProblem.style.display = "block"; annTitleBox.focus(); return; }
        if (!(await askConfirm({ title: "Post to every app user?", message: `"${t}" appears in every user's Notifications.`, confirmLabel: "Post", badgeIcon: "bell" }))) return;
        post.disabled = true; post.innerHTML = '<span class="btn-spin" aria-hidden="true"></span>Posting...';
        try {
            const batch = writeBatch(db);
            batch.set(fb.doc(fb.collection(db, "announcements")), { kind: kind.value, title: t, body: b, startsAt: null, endsAt: null, createdAt: fb.serverTimestamp(), createdBy: auth.currentUser.uid });
            await batch.commit();
            annTitleBox.value = ""; annBody.value = ""; ac.paint(); bc.paint(); annProblem.style.display = "none";
            toast("Announcement posted.", "ok");
        } catch (error) {
            console.error("Announcement failed:", error && error.code ? error.code : error);
            toast("Failed to post. Please try again.", "error");
        } finally { post.disabled = false; post.textContent = "Post announcement"; }
    });
    [annTitleBox, annBody].forEach((b) => b.addEventListener("input", () => { annProblem.style.display = "none"; }));

    function paint() {
        if (!loaded) { status.textContent = "Loading the current state..."; card.setAttribute("aria-busy", "true"); return; }
        card.removeAttribute("aria-busy");
        if (failure) { status.textContent = "The current state could not be read. The latest rules may not be published yet."; status.dataset.state = "error"; return; }
        const state = maintenanceState(data);
        status.dataset.state = state;
        const s = msOf(data && data.scheduledStart), e = msOf(data && data.scheduledEnd);
        status.textContent = state === "active" ? `Maintenance Active${e ? ` until ${fmt(e)}` : " (no end time)"}.`
            : state === "scheduled" ? `Update Scheduled for ${fmt(s)}${e ? ` to ${fmt(e)}` : ""}.` : "System Online.";
        if (!saving) toggle.setAttribute("aria-checked", String(state !== "online"));
        const live = state === "active";
        start._lockedByState = live; end._lockedByState = live;           // no rescheduling in the middle of maintenance
        if (!saving) { start.disabled = live; end.disabled = live; }
        const sent = !!(data && data.notifyUsers);
        notice._lockedByState = sent || state !== "scheduled";
        notice._label = sent ? "Notice Sent ✓" : "Send Advance Notice";
        if (!saving) { notice.disabled = notice._lockedByState; notice.textContent = notice._label; }
        noticeHint.textContent = sent ? `Sent ${data.notifiedAt ? fmt(msOf(data.notifiedAt)) : "just now"}. It can be sent again after this maintenance ends.`
            : state === "scheduled" ? "Tells app users when the app will be away. It also appears in their Notifications." : "Available once a start time is scheduled.";
        if (!dirty && !saving) fill();
    }
    subscribers.add(paint);
    return { paint };
}
