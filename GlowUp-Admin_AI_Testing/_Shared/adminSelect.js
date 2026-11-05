// A drop-down that looks like the site (dark panel, pink hover and selected row) and works from an ordinary <select>.
// The <select> stays in the page, hidden, as the one source of truth: its options are what is offered and its value is what is read, so
// any code that fills it or reads it keeps working. After changing its options by code, call refresh().
//
// Option conventions (on the <option>):
//   value ""        the placeholder: shown on the button while nothing is chosen, greyed in the list, never selectable
//   disabled        a line that is shown but cannot be chosen (the thin separator above "+ Add ..." is an option with data-sep)
//   data-add        an action row ("+ Add new classification"): stays at the bottom whatever is typed in the filter
// More than FILTER_FROM choices: a box to type in filters the list. The open list scrolls when it is long.
//
// Keys: Enter, Space or the arrows open it; the arrows move; Enter chooses; Esc closes it (and only it); Tab leaves.
import { matchesSearch } from "./glowupRules.js";

const FILTER_FROM = 10;

export function makeCombo(select) {
    const box = document.createElement("div");
    box.className = "combo";
    const button = document.createElement("button");
    button.type = "button";
    button.className = "combo-btn";
    button.id = select.id + "Combo";
    button.setAttribute("aria-haspopup", "listbox");
    button.setAttribute("aria-expanded", "false");
    const label = document.createElement("span");
    label.className = "combo-label";
    const caret = document.createElement("span");
    caret.className = "combo-caret";
    caret.setAttribute("aria-hidden", "true");
    button.append(label, caret);
    const pop = document.createElement("div");
    pop.className = "combo-pop";
    pop.hidden = true;
    const filter = document.createElement("input");
    filter.type = "text";
    filter.className = "combo-filter";
    filter.placeholder = "Type to filter";
    filter.autocomplete = "off";
    filter.setAttribute("aria-label", "Filter the list");
    const list = document.createElement("ul");
    list.className = "combo-list";
    list.setAttribute("role", "listbox");
    list.id = select.id + "List";
    button.setAttribute("aria-controls", list.id);
    pop.append(filter, list);
    box.append(button, pop);
    select.classList.add("combo-native");
    select.tabIndex = -1;
    select.setAttribute("aria-hidden", "true");
    select.after(box);
    const nativeLabel = document.querySelector(`label[for="${select.id}"]`);
    if (nativeLabel) nativeLabel.htmlFor = button.id;
    select.focus = () => button.focus();            // code that sends the person to this field lands on the visible button

    let at = -1;                                    // the highlighted row among the rows that can be chosen
    const rows = () => Array.from(list.querySelectorAll("[role=option]:not([aria-disabled=true])"));
    const isOpen = () => !pop.hidden;

    function paintButton() {
        const chosen = select.selectedOptions[0];
        const empty = !chosen || chosen.value === "";
        label.textContent = chosen ? chosen.textContent : "";
        button.classList.toggle("is-placeholder", empty);
    }

    function paintList() {
        const typed = filter.value.trim();
        list.replaceChildren();
        Array.from(select.options).forEach((option) => {
            const add = option.hasAttribute("data-add"), placeholder = option.value === "" , sep = option.hasAttribute("data-sep");
            if (sep) { const line = document.createElement("li"); line.className = "combo-sep"; line.setAttribute("role", "separator"); list.appendChild(line); return; }
            if (typed && !add && !placeholder && !matchesSearch(typed, option.textContent)) return;
            if (typed && placeholder) return;
            const row = document.createElement("li");
            row.className = "combo-item" + (add ? " is-add" : "") + (placeholder ? " is-placeholder" : "");
            row.setAttribute("role", "option");
            row.textContent = option.textContent;
            row.dataset.value = option.value;
            const off = option.disabled || placeholder;
            row.setAttribute("aria-disabled", String(off));
            row.setAttribute("aria-selected", String(!placeholder && option.value === select.value));
            if (!off) {
                row.addEventListener("mousedown", (event) => event.preventDefault());       // the filter box keeps the focus
                row.addEventListener("click", () => choose(option.value));
                row.addEventListener("mousemove", () => { at = rows().indexOf(row); paintActive(false); });
            }
            list.appendChild(row);
        });
        if (!list.querySelector("[role=option]:not([aria-disabled=true]):not(.is-add)")) {
            const none = document.createElement("li");
            none.className = "combo-none";
            none.textContent = "Nothing matches";
            list.insertBefore(none, list.firstChild);
        }
    }

    function paintActive(scroll) {
        rows().forEach((row, i) => { row.classList.toggle("active", i === at); if (i === at && scroll) row.scrollIntoView({ block: "nearest" }); });
        if (at >= 0 && rows()[at]) filter.setAttribute("aria-activedescendant", rows()[at].id || ""); else filter.removeAttribute("aria-activedescendant");
    }

    function place() {
        const r = button.getBoundingClientRect();
        const room = window.innerHeight - r.bottom - 12, above = r.top - 12;
        const down = room >= 180 || room >= above;
        pop.style.left = r.left + "px";
        pop.style.width = r.width + "px";
        pop.style.maxHeight = Math.max(140, Math.min(320, down ? room : above)) + "px";
        if (down) { pop.style.top = r.bottom + 4 + "px"; pop.style.bottom = ""; }
        else { pop.style.top = ""; pop.style.bottom = window.innerHeight - r.top + 4 + "px"; }
    }

    function open() {
        if (isOpen()) return;
        document.body.appendChild(pop);               // out of the window's scroll area (and of its blurred layer, which would hold a fixed box inside it)
        const choices = Array.from(select.options).filter((o) => o.value !== "" && !o.disabled && !o.hasAttribute("data-add")).length;
        filter.hidden = choices <= FILTER_FROM;
        filter.value = "";
        paintList();
        pop.hidden = false;
        button.setAttribute("aria-expanded", "true");
        place();
        at = Math.max(0, rows().findIndex((row) => row.getAttribute("aria-selected") === "true"));
        paintActive(true);
        (filter.hidden ? pop : filter).focus({ preventScroll: true });
    }

    function close(back) {
        if (!isOpen()) return;
        pop.hidden = true;
        box.appendChild(pop);
        button.setAttribute("aria-expanded", "false");
        if (back) button.focus({ preventScroll: true });
    }

    function choose(value) {
        close(true);
        if (select.value === value && value !== "") { return; }
        select.value = value;
        paintButton();
        select.dispatchEvent(new Event("change", { bubbles: true }));
        paintButton();
    }

    pop.tabIndex = -1;
    button.addEventListener("click", () => { if (isOpen()) close(true); else open(); });
    button.addEventListener("keydown", (event) => {
        if (["ArrowDown", "ArrowUp", "Enter", " "].includes(event.key)) { event.preventDefault(); open(); }
    });
    filter.addEventListener("input", () => { paintList(); at = rows().length > 0 ? 0 : -1; paintActive(true); });
    pop.addEventListener("keydown", (event) => {
        const all = rows();
        if (event.key === "ArrowDown") { event.preventDefault(); at = all.length ? (at + 1) % all.length : -1; paintActive(true); }
        else if (event.key === "ArrowUp") { event.preventDefault(); at = all.length ? (at <= 0 ? all.length - 1 : at - 1) : -1; paintActive(true); }
        else if (event.key === "Enter") { event.preventDefault(); if (all[at]) choose(all[at].dataset.value); }
        else if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); close(true); }
        else if (event.key === "Tab") close(false);
    });
    document.addEventListener("pointerdown", (event) => { if (isOpen() && !box.contains(event.target) && !pop.contains(event.target)) close(false); }, true);
    window.addEventListener("resize", () => { if (isOpen()) place(); });
    document.addEventListener("scroll", () => { if (isOpen()) place(); }, true);

    paintButton();
    return { refresh() { paintButton(); if (isOpen()) { paintList(); place(); } }, close };
}
