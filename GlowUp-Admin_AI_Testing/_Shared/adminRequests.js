// Admin requests: the people who pressed "Sign Up" on the login page and wait for admin access.
// Shown in Quick settings > Admin requests, LIVE: the list and the menu's count follow adminRequests in real time, and a new
// request shows a notice on whatever admin page is open.
//
// Approve = one batch: admins/{uid} is created and the request is marked approved (firestore.rules allow this only for a
// pending request whose e-mail is confirmed, and never for yourself). Decline = the request is deleted; the person's
// Firebase account stays (removing another person's account needs the Admin SDK, which this free-tier site does not use).
// Both go through adminStore's writeBatch, so the audit log records them.

import { auth, db } from "../_Database/firebase-console.js";
import { onAuthStateChanged } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-auth.js";
import * as fb from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { onSnapshot, writeBatch } from "./adminStore.js";
import { askConfirm, toast, icon } from "./adminShell.js";

let list = null;            // null = not loaded yet
let problem = null;         // the last listener error, or null
let stop = null;
let started = false;
let firstSnapshot = true;
const subscribers = new Set();
const busyIds = new Set();

const emit = () => subscribers.forEach((callback) => { try { callback(); } catch (error) { console.error(error); } });
export const onRequestsChanged = (callback) => { subscribers.add(callback); return () => subscribers.delete(callback); };
/** { list: [...] | null, problem } */
export const requestsState = () => ({ list, problem });
export const pendingCount = () => (list ? list.length : 0);

const millis = (value) => (value && typeof value.toMillis === "function" ? value.toMillis() : 0);

function listen() {
    if (stop) { stop(); stop = null; }
    problem = null;
    list = null;
    firstSnapshot = true;
    emit();
    stop = onSnapshot(fb.query(fb.collection(db, "adminRequests"), fb.where("status", "==", "pending")), (snapshot) => {
        const before = new Set((list || []).map((r) => r.id));
        list = snapshot.docs.map((d) => Object.assign({ id: d.id }, d.data())).sort((a, b) => millis(a.createdAt) - millis(b.createdAt));
        problem = null;
        if (!firstSnapshot) {
            const fresh = list.filter((r) => !before.has(r.id));
            if (fresh.length === 1) toast(`New admin request from ${fresh[0].name || fresh[0].email}. Open Quick settings > Admin requests.`, "info");
            else if (fresh.length > 1) toast(`${fresh.length} new admin requests. Open Quick settings > Admin requests.`, "info");
        }
        firstSnapshot = false;
        emit();
    }, (error) => {
        console.warn("Admin requests could not be loaded:", error && error.code ? error.code : error);
        problem = error || { code: "unknown" };
        list = list || [];
        emit();
    });
}

/** Starts the live list once an account is signed in (every admin page calls this through Quick settings). */
export function watchRequests() {
    if (started) return;
    started = true;
    onAuthStateChanged(auth, (user) => {
        if (user) listen();
        else { if (stop) { stop(); stop = null; } list = null; emit(); }
    });
}
export const retryRequests = () => listen();

async function approve(request) {
    const me = auth.currentUser;
    if (!me || busyIds.has(request.id)) return;
    const yes = await askConfirm({
        title: "Give admin access?",
        message: `${request.name} (${request.email}) will be able to sign in to this portal and change all GlowUp data. Approve only people you know.`,
        cancelLabel: "Cancel", confirmLabel: "Approve", badgeIcon: "shield"
    });
    if (!yes) return;
    busyIds.add(request.id); emit();
    try {
        const batch = writeBatch(db);
        batch.set(fb.doc(db, "admins", request.id), { email: request.email, name: String(request.name || "").slice(0, 80), approvedBy: me.uid, approvedAt: fb.serverTimestamp() });
        batch.update(fb.doc(db, "adminRequests", request.id), { status: "approved", decidedBy: me.uid, decidedAt: fb.serverTimestamp() });
        await batch.commit();
        toast(`${request.name} is now an admin.`, "ok");
    } catch (error) {
        console.error("Approve failed:", error && error.code ? error.code : error);
        toast(String(error && error.code || "").includes("permission-denied")
            ? "Not approved: the request must be pending and its email confirmed."
            : "Not approved. Check the connection and try again.", "error");
    } finally { busyIds.delete(request.id); emit(); }
}

async function decline(request) {
    if (busyIds.has(request.id)) return;
    const yes = await askConfirm({
        title: "Decline this request?",
        message: `${request.name} (${request.email}) gets no admin access and the request is deleted. They can ask again later.`,
        cancelLabel: "Cancel", confirmLabel: "Decline", danger: true, badgeIcon: "ban"
    });
    if (!yes) return;
    busyIds.add(request.id); emit();
    try {
        await writeBatch(db).delete(fb.doc(db, "adminRequests", request.id)).commit();
        toast(`Request from ${request.name} declined.`, "info");
    } catch (error) {
        console.error("Decline failed:", error && error.code ? error.code : error);
        toast("Not declined. Check the connection and try again.", "error");
    } finally { busyIds.delete(request.id); emit(); }
}

const when = (value) => {
    const ms = millis(value);
    if (!ms) return "Just now";
    return new Date(ms).toLocaleString(undefined, { day: "numeric", month: "short", year: "numeric", hour: "numeric", minute: "2-digit" });
};

function node(tag, className, text) {
    const el = document.createElement(tag);
    if (className) el.className = className;
    if (text !== undefined) el.textContent = text;
    return el;
}

/** Draws the Quick settings view: loading rows, an error with Retry, an empty state, or the requests. */
export function paintRequestsView(host) {
    if (!host) return;
    const intro = node("div", "req-top");
    intro.append(node("p", "req-intro", "People who pressed Sign Up on the login page. Approve only people you know: an admin can change all GlowUp data."));
    // the three steps, read top to bottom, so a new admin knows what each button does
    const steps = node("ol", "req-steps");
    [["They sign up", "and confirm their e-mail from the link we send."], ["You approve", "and they can log in to this website at once."], ["Or you decline", "and the request is removed. They are not told."]]
        .forEach(([b, rest]) => { const li = node("li"); li.append(node("strong", "", b), document.createTextNode(" " + rest)); steps.append(li); });
    intro.append(steps);
    if (list === null) {
        const rows = Array.from({ length: 3 }, () => { const r = node("div", "req-row req-skeleton"); r.setAttribute("aria-hidden", "true"); r.append(node("span", "sk sk-dot"), node("span", "sk sk-line")); return r; });
        const box = node("div", "req-list"); box.setAttribute("aria-busy", "true"); box.append(...rows);
        host.replaceChildren(intro, box);
        return;
    }
    if (problem) {
        const fail = node("div", "req-state req-error");
        fail.setAttribute("role", "alert");
        fail.append(node("p", "", String(problem.code || "").includes("permission-denied") ? "The requests could not be read. The latest database rules may not be published yet." : "The requests could not be loaded. Check the connection."));
        const again = node("button", "btn", "Retry");
        again.type = "button";
        again.addEventListener("click", retryRequests);
        fail.append(again);
        host.replaceChildren(intro, fail);
        return;
    }
    if (list.length === 0) {
        const empty = node("div", "req-state req-empty");
        empty.innerHTML = icon("inbox");
        empty.append(node("p", "", "No requests waiting."), node("small", "", "New ones appear here by themselves."));
        host.replaceChildren(intro, empty);
        return;
    }
    const ready = list.filter((r) => r.emailVerified).length;
    const summary = node("p", "req-summary");
    summary.setAttribute("aria-live", "polite");
    summary.append(node("strong", "", list.length + (list.length === 1 ? " request" : " requests")), document.createTextNode(` waiting · ${ready} ready to approve`));
    const box = node("ul", "req-list");
    list.slice().sort((a, b) => Number(b.emailVerified) - Number(a.emailVerified)).forEach((request) => {
        const row = node("li", "req-row");
        const avatar = node("span", "req-avatar", String(request.name || request.email || "?").trim().charAt(0).toUpperCase());
        avatar.setAttribute("aria-hidden", "true");
        const words = node("div", "req-words");
        words.append(node("strong", "req-name", request.name || "(no name)"), node("span", "req-email", request.email || ""));
        const meta = node("div", "req-meta");
        meta.append(node("span", "req-when", "Asked " + when(request.createdAt)));
        const chip = node("span", "chip " + (request.emailVerified ? "tone-ok" : "tone-warn"), request.emailVerified ? "Email confirmed" : "Email not confirmed yet");
        meta.append(chip);
        words.append(meta);
        const actions = node("div", "req-actions");
        const busy = busyIds.has(request.id);
        const yes = node("button", "btn primary", busy ? "Working..." : "Approve");
        yes.type = "button";
        yes.disabled = busy || !request.emailVerified;
        yes.addEventListener("click", () => approve(request));
        const no = node("button", "btn danger", "Decline");
        no.type = "button";
        no.disabled = busy;
        no.addEventListener("click", () => decline(request));
        actions.append(no, yes);
        row.append(avatar, words, actions);
        if (!request.emailVerified) row.append(node("p", "req-why", "Approve opens once they confirm their email (they open the link we sent, or log in once after it)."));
        box.append(row);
    });
    host.replaceChildren(intro, summary, box);
}
