// The behaviour every big form window shares (Ingredient, Brand, Product): a section bar that marks the section being read and jumps to
// one, a focus that stays inside the open window, and the box that failed marked invalid for screen readers.
// The markup is: .modal-overlay > .ing-modal > (.ing-head, .ing-nav, form.ing-form > (.ing-body > sections, .ing-foot)).
// The look is in adminCommon.css ("the Add / Edit Ingredient window" block); nothing here changes a form's data or checks.

/**
 * Wires one window. Returns { reset }: call it when the window is opened (scrolls the body to the top and marks the first section).
 * `overlay` = the .modal-overlay element.
 */
export function wireWindow(overlay) {
    const form = overlay.querySelector("form");
    const body = overlay.querySelector(".ing-body");
    const buttons = Array.from(overlay.querySelectorAll(".ing-nav-btn"));
    const sections = buttons.map((button) => overlay.querySelector("#" + button.dataset.jump));

    // ---- the section bar
    const mark = () => {
        if (sections.length === 0) return;
        const line = body.getBoundingClientRect().top + 24;
        let current = sections[0];
        sections.forEach((section) => { if (section.getBoundingClientRect().top <= line) current = section; });
        if (body.scrollTop + body.clientHeight >= body.scrollHeight - 2) current = sections[sections.length - 1];
        buttons.forEach((button, i) => button.setAttribute("aria-current", String(sections[i] === current)));
    };
    body.addEventListener("scroll", mark, { passive: true });
    buttons.forEach((button, i) => button.addEventListener("click", () => {
        const calm = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
        sections[i].scrollIntoView({ block: "start", behavior: calm ? "auto" : "smooth" });
    }));

    // ---- Tab and Shift+Tab go round inside the open window instead of leaving it
    overlay.addEventListener("keydown", (event) => {
        if (event.key !== "Tab" || overlay.style.display === "none") return;
        const focusable = Array.from(overlay.querySelectorAll("a[href], button, input, select, textarea, summary, [tabindex]:not([tabindex='-1'])"))
            .filter((node) => !node.disabled && node.getAttribute("tabindex") !== "-1" && node.offsetParent !== null && getComputedStyle(node).visibility !== "hidden" && node.getBoundingClientRect().width > 1);
        if (focusable.length === 0) return;
        const first = focusable[0], last = focusable[focusable.length - 1];
        if (event.shiftKey && (document.activeElement === first || !overlay.contains(document.activeElement))) { event.preventDefault(); last.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    });

    // ---- when a message appears, the box that was focused for it (the save code focuses the box it complains about) is marked invalid until it is edited
    const line = form ? form.querySelector(".form-error") : null;
    if (line) {
        const lineId = (form.id || "window") + "ErrorLine";
        line.id = lineId;
        const clear = () => form.querySelectorAll("[aria-invalid]").forEach((node) => node.removeAttribute("aria-invalid"));
        new MutationObserver(() => {
            if (!line.textContent.trim()) { clear(); return; }
            window.setTimeout(() => {
                const active = document.activeElement;
                if (active && form.contains(active) && /^(INPUT|SELECT|TEXTAREA)$/.test(active.tagName)) {
                    clear();
                    active.setAttribute("aria-invalid", "true");
                    active.setAttribute("aria-describedby", ((active.getAttribute("aria-describedby") || "").split(" ").filter((id) => id && id !== lineId).concat(lineId)).join(" "));
                }
            }, 0);
        }).observe(line, { childList: true, characterData: true, subtree: true });
        form.addEventListener("input", (event) => { if (event.target.removeAttribute) event.target.removeAttribute("aria-invalid"); });
    }

    return { reset() { body.scrollTop = 0; mark(); } };
}
