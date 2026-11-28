// Redesign plan, pages 29 to 35 (pop-up form since 28 Nov 2026): popWindow, the centred window used by Product review, User report
// and the "add a list" windows, and the "Needs your attention: N" label that stands in for the large notes panel.
// Navigation rules: X, Escape, backdrop and browser Back all ask the window to close; a window that refuses (unsaved edits,
// a save running) stays.
import { dialogFrame, icon } from "./adminShell.js";

const open = [];

window.addEventListener("popstate", () => {
    const view = open[open.length - 1];
    if (!view) return;
    history.pushState({ popWindow: true }, "");          // stays on this page; the window itself decides whether it closes
    view.dismiss();
});

const make = (tag, className, text) => { const n = document.createElement(tag); if (className) n.className = className; if (text) n.textContent = text; return n; };

/** { box, head, title, body, foot, close }. The head has the title, an optional line under it, and the X. */
export function popWindow(title, onDismiss, { subtitle = "" } = {}) {
    const overlay = make("div", "modal-overlay");
    overlay.style.display = "flex";
    const box = make("div", "modal-content pop-window");
    box.setAttribute("role", "dialog");
    box.setAttribute("aria-modal", "true");
    box.setAttribute("aria-label", title);
    const head = make("header", "pw-head"), titles = make("div", "pw-titles"), heading = make("h3", "", title);
    titles.append(heading);
    if (subtitle) titles.append(make("p", "pw-sub", subtitle));
    const x = make("button", "icon-btn pw-x");
    x.type = "button";
    x.setAttribute("aria-label", "Close");
    x.innerHTML = icon("x");
    head.append(titles, x);
    const body = make("div", "pw-body"), foot = make("footer", "pw-foot");
    box.append(head, body, foot);
    overlay.append(box);

    // a question asked on top of this window answers Escape first
    const onTop = () => {
        const shown = Array.from(document.querySelectorAll(".modal-overlay")).filter((o) => o.getClientRects().length > 0);
        return shown[shown.length - 1] === overlay;
    };
    const onKey = (event) => {
        if (event.key !== "Escape" || !onTop()) return;
        event.stopPropagation();
        onDismiss();
    };
    const view = { dismiss: onDismiss };
    open.slice().forEach((other) => other.close());
    open.push(view);
    view.close = () => {
        const at = open.indexOf(view);
        if (at < 0) return;
        open.splice(at, 1);
        document.removeEventListener("keydown", onKey, true);
        overlay.remove();
        // the history entry goes only when no other window took this one's place
        setTimeout(() => { if (!open.length && history.state && history.state.popWindow) history.back(); }, 0);
    };
    x.addEventListener("click", () => onDismiss());
    overlay.addEventListener("mousedown", (event) => { if (event.target === overlay) onDismiss(); });
    document.addEventListener("keydown", onKey, true);
    if (!(history.state && history.state.popWindow)) history.pushState({ popWindow: true }, "");
    document.body.append(overlay);
    x.focus({ preventScroll: true });
    return { box, head, title: heading, body, foot, close: view.close };
}

/** `list` is the notes list the page already fills; it is shown in a window opened from the label. */
export function attentionLabel(before, list) {
    if (!before || !list) return;
    const home = list.parentElement;
    const button = document.createElement("button");
    button.type = "button";
    button.className = "attention-label";
    button.setAttribute("aria-haspopup", "dialog");
    const paint = () => {
        const waiting = Array.from(list.querySelectorAll(".note-row")).filter((row) => ["bad", "warn", "info"].includes(row.dataset.tone)).length;
        const words = `Needs your attention: ${waiting}`;
        if (button.textContent !== words) button.textContent = words;
        button.classList.toggle("quiet", waiting === 0);
    };
    new MutationObserver(paint).observe(list, { childList: true, subtree: true });
    paint();
    button.addEventListener("click", () => {
        const done = () => { home.appendChild(list); frame.close(); list.removeEventListener("click", act); };
        const act = (event) => { if (event.target.closest(".note-act")) setTimeout(done, 0); };
        const frame = dialogFrame("Needs your attention", done);
        frame.box.classList.add("attention-window");
        const foot = document.createElement("div");
        foot.className = "modal-actions";
        const close = document.createElement("button");
        close.type = "button"; close.className = "btn"; close.textContent = "Close";
        close.addEventListener("click", done);
        foot.appendChild(close);
        list.addEventListener("click", act);
        frame.box.append(list, foot);
        close.focus();
    });
    before.insertAdjacentElement("beforebegin", button);
}
