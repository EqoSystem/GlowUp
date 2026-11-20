// Reply to an app user's report (New22, 20 November 2026). A panel slides in from the right with the report, a reply box
// (500 characters, counter) and Send. Send writes, in ONE batch, a notification to users/{uid}/notifications (the app shows
// it in its Notifications screen, live) and marks the report handled with the reply kept on it. The batch goes through
// adminStore, so the audit log records it. Close (X), Cancel, Escape and a click beside the panel leave it; with text typed
// they ask "Discard reply?" first. Nothing is sent without the Send click.

import { auth, db } from "../_Database/firebase-console.js";
import * as fb from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { writeBatch } from "./adminStore.js";
import { icon, askConfirm, toast } from "./adminShell.js";

export const REPLY_MAX = 500;
let open = null;

const node = (tag, className, text) => { const el = document.createElement(tag); if (className) el.className = className; if (text !== undefined) el.textContent = text; return el; };

/** report: { id, uid, userName, category, message }. onSent(report) runs after a successful send. */
export function openReplyDrawer(report, { onSent } = {}) {
    if (open) return;
    const opener = document.activeElement;
    const backdrop = node("div", "reply-backdrop");
    const panel = node("aside", "reply-drawer");
    panel.setAttribute("role", "dialog");
    panel.setAttribute("aria-modal", "true");
    panel.setAttribute("aria-labelledby", "replyTitle");

    const head = node("div", "reply-head");
    const title = node("h3", "", "Reply to " + (report.userName || "this user"));
    title.id = "replyTitle";
    const close = node("button", "icon-btn small");
    close.type = "button"; close.setAttribute("aria-label", "Close the reply"); close.innerHTML = icon("x");
    head.append(title, close);

    const quote = node("div", "reply-quote");
    quote.append(node("span", "chip", report.category || "Report"), node("p", "", report.message || ""));

    const form = node("form", "reply-form");
    form.noValidate = true;
    const field = node("div", "field");
    const label = node("label", "", "Your reply");
    const box = node("textarea");
    box.id = "replyText"; box.rows = 6; box.maxLength = REPLY_MAX; box.placeholder = "Write what you found or what was fixed. The user sees this in the app's Notifications.";
    label.htmlFor = box.id;
    const row = node("div", "hint-row");
    row.append(node("p", "field-hint", "Sent as “The GlowUp team”."));
    const counter = node("span", "counter", `0 / ${REPLY_MAX}`);
    row.append(counter);
    field.append(label, box, row);
    const problem = node("p", "form-error"); problem.setAttribute("role", "alert");
    const actions = node("div", "modal-actions");
    const cancel = node("button", "cancel-btn", "Cancel"); cancel.type = "button";
    const send = node("button", "save-btn", "Send reply"); send.type = "submit"; send.disabled = true;
    actions.append(cancel, send);
    form.append(field, problem, actions);
    panel.append(head, quote, form);
    document.body.append(backdrop, panel);
    requestAnimationFrame(() => { backdrop.classList.add("show"); panel.classList.add("show"); });

    let sending = false;
    const paint = () => {
        counter.textContent = `${box.value.length} / ${REPLY_MAX}`;
        counter.classList.toggle("is-full", box.value.length >= REPLY_MAX);
        send.disabled = sending || box.value.trim().length < 2;
        problem.style.display = "none";
    };
    box.addEventListener("input", paint);

    const finish = () => {
        document.removeEventListener("keydown", onKey, true);
        panel.remove(); backdrop.remove();
        open = null;
        if (opener && document.contains(opener)) opener.focus({ preventScroll: true });
    };
    const leave = async () => {
        if (sending) return;
        if (box.value.trim() && !(await askConfirm({ title: "Discard reply?", message: "What you typed will be lost.", cancelLabel: "Keep writing", confirmLabel: "Discard", danger: true }))) { box.focus(); return; }
        finish();
    };
    const FOCUSABLE = 'button:not(:disabled), textarea, [href], input, select';
    const onKey = (event) => {
        if ([...document.querySelectorAll(".modal-overlay")].some((o) => getComputedStyle(o).display !== "none")) return;   // a question on top has the keys
        if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); leave(); return; }
        if (event.key !== "Tab") return;
        const items = [...panel.querySelectorAll(FOCUSABLE)];
        const first = items[0], last = items[items.length - 1];
        if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };
    document.addEventListener("keydown", onKey, true);
    close.addEventListener("click", leave);
    cancel.addEventListener("click", leave);
    backdrop.addEventListener("click", leave);

    form.addEventListener("submit", async (event) => {
        event.preventDefault();
        const text = box.value.replace(/\s+\n/g, "\n").trim();
        if (sending || text.length < 2) return;
        if (!report.uid) { problem.textContent = "This report has no sender account, so a reply cannot reach anyone."; problem.style.display = "block"; return; }
        sending = true; paint();
        box.readOnly = true; cancel.disabled = true; close.disabled = true;
        send.innerHTML = '<span class="btn-spin" aria-hidden="true"></span>Sending...';
        try {
            const batch = writeBatch(db);
            batch.set(fb.doc(fb.collection(db, "users", report.uid, "notifications")), {
                kind: "reply", title: "Reply to your " + String(report.category || "report").toLowerCase() + " report", body: text,
                reportId: report.id, read: false, createdAt: fb.serverTimestamp(), createdBy: auth.currentUser ? auth.currentUser.uid : ""
            });
            batch.update(fb.doc(db, "supportReports", report.id), { status: "handled", handledAt: fb.serverTimestamp(), reply: text, repliedAt: fb.serverTimestamp() });
            await batch.commit();
            finish();
            toast("Reply sent. The user sees it in the app's Notifications.", "ok");
            if (onSent) onSent(report);
        } catch (error) {
            console.error("Reply failed:", error && error.code ? error.code : error);
            sending = false; box.readOnly = false; cancel.disabled = false; close.disabled = false;
            send.textContent = "Send reply"; paint();
            problem.textContent = String(error && error.code || "").includes("permission-denied")
                ? "Not sent: the database refused it. The latest rules may not be published yet."
                : "Not sent. Check the connection and press Send reply again.";
            problem.style.display = "block";
        }
    });
    open = panel;
    box.focus();
}
