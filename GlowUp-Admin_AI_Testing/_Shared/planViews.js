// Redesign plan, pages 29 to 35: a full-page view that takes the board's place ("← Queue"), and the "Needs your attention: N"
// label that stands in for the large notes panel. Navigation rules: browser Back and Escape leave a view (2 and 1), a view that
// refuses to leave (unsaved edits) stays.
import { dialogFrame } from "./adminShell.js";

const open = [];

function showBoard() {
    const main = document.querySelector("main.main-content"), board = main && main.querySelector(":scope > .board");
    if (board) board.hidden = open.length > 0;
}

window.addEventListener("popstate", () => {
    const view = open[open.length - 1];
    if (!view) return;
    history.pushState({ planView: true }, "");          // stays on this page; the view itself decides whether it closes
    view.dismiss();
});

/** The same shape as dialogFrame ({ box, close }), drawn in the page instead of over it. */
export function pageView(title, onDismiss) {
    const main = document.querySelector("main.main-content");
    const box = document.createElement("div");
    box.className = "plan-view";
    box.setAttribute("role", "region");
    box.setAttribute("aria-label", title);
    const heading = document.createElement("h3");
    heading.textContent = title;
    box.appendChild(heading);
    const onKey = (event) => {
        // a question on top answers Escape first (the page keeps its closed pop-ups in the document: only a shown one counts)
        if (event.key !== "Escape" || Array.from(document.querySelectorAll(".modal-overlay")).some((o) => o.getClientRects().length > 0)) return;
        event.stopPropagation(); onDismiss();
    };
    const view = { dismiss: onDismiss };
    open.slice().forEach((other) => other.close());
    open.push(view);
    view.close = () => {
        const at = open.indexOf(view);
        if (at < 0) return;
        open.splice(at, 1);
        document.removeEventListener("keydown", onKey, true);
        box.remove();
        showBoard();
        // the history entry goes only when no other view took this one's place
        setTimeout(() => { if (!open.length && history.state && history.state.planView) history.back(); }, 0);
    };
    document.addEventListener("keydown", onKey, true);
    if (!(history.state && history.state.planView)) history.pushState({ planView: true }, "");
    main.appendChild(box);
    showBoard();
    window.scrollTo(0, 0);
    return { box, close: view.close };
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
