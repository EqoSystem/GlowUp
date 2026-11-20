// Character counters for form fields that have a maxlength (New22, 20 November 2026). Same look as the Ingredient Curator's
// counters (.hint-row .counter): "12 / 100", red when full. Only fields where the limit matters to the person typing get one
// (names and free text); web addresses, barcodes and codes keep their maxlength without a counter, to avoid clutter.

/** Adds a live "n / max" counter under each listed field (once). Returns a function that repaints them all (after a form is filled in). */
export function attachCounters(ids) {
    const boxes = ids.map((id) => document.getElementById(id)).filter((box) => box && box.maxLength > 0);
    boxes.forEach((box) => {
        if (box._counter) return;
        const field = box.closest(".field") || box.parentElement;
        let row = field.querySelector(":scope > .hint-row");
        if (!row) {
            row = document.createElement("div");
            row.className = "hint-row";
            const hint = field.querySelector(":scope > .field-hint");
            if (hint) { hint.replaceWith(row); row.appendChild(hint); }
            else box.insertAdjacentElement("afterend", row);
        }
        const counter = document.createElement("span");
        counter.className = "counter";
        counter.setAttribute("aria-live", "off");
        row.appendChild(counter);
        box._counter = counter;
        box.addEventListener("input", () => paint(box));
    });
    const paintAll = () => boxes.forEach(paint);
    paintAll();
    return paintAll;
}

function paint(box) {
    if (!box._counter) return;
    const max = box.maxLength;
    box._counter.textContent = `${box.value.length} / ${max}`;
    box._counter.classList.toggle("is-full", box.value.length >= max);
}
