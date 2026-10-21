// A small filter panel ("Apply filters") that opens under a button: several choices at once, one press to apply.
// It only collects the choices; the page decides what they filter (nothing here reads the database).
//
//   openFacets(anchor, {
//       title: "Filters",
//       fields: [
//           { id: "type",   label: "Kind",  kind: "select", options: [{ value, label }], value: "" },
//           { id: "facts",  label: "Only",  kind: "checks", options: [{ value, label }], value: ["has-shades"] }
//       ],
//       onApply(values)    // { type: "Lipstick", facts: ["has-shades"] }
//   })
//
// Esc and a click outside close it; the choices are put on the page with textContent only.
let open = null;

const make = (tag, className, text) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
};

export function closeFacets(giveFocusBack) {
    if (!open) return;
    const { panel, anchor, off } = open;
    open = null;
    off();
    panel.remove();
    if (anchor) { anchor.setAttribute("aria-expanded", "false"); if (giveFocusBack) anchor.focus(); }
}

export function openFacets(anchor, spec) {
    if (open && open.anchor === anchor) { closeFacets(false); return; }
    closeFacets(false);
    const panel = make("div", "facet-panel");
    panel.setAttribute("role", "dialog");
    panel.setAttribute("aria-label", spec.title || "Filters");
    panel.appendChild(make("h4", "facet-title", spec.title || "Filters"));

    const read = {};
    spec.fields.forEach((field) => {
        const group = make("div", "facet-field");
        const label = make("label", "facet-label", field.label);
        group.appendChild(label);
        if (field.kind === "select") {
            const select = make("select");
            select.id = "facet_" + field.id;
            label.htmlFor = select.id;
            const any = make("option", "", "Any"); any.value = ""; select.appendChild(any);
            field.options.forEach((o) => { const option = make("option", "", o.label); option.value = o.value; select.appendChild(option); });
            select.value = field.value || "";
            group.appendChild(select);
            read[field.id] = () => select.value;
        } else {
            const checks = make("div", "facet-checks");
            const boxes = field.options.map((o) => {
                const item = make("label", "check");
                const box = make("input"); box.type = "checkbox"; box.value = o.value; box.checked = (field.value || []).includes(o.value);
                item.append(box, make("span", "", " " + o.label));
                checks.appendChild(item);
                return box;
            });
            group.appendChild(checks);
            read[field.id] = () => boxes.filter((b) => b.checked).map((b) => b.value);
        }
        panel.appendChild(group);
    });

    const actions = make("div", "facet-actions");
    const clear = make("button", "cancel-btn", "Clear"); clear.type = "button";
    const apply = make("button", "save-btn", "Apply filters"); apply.type = "button";
    actions.append(clear, apply);
    panel.appendChild(actions);

    const collect = () => Object.fromEntries(Object.keys(read).map((id) => [id, read[id]()]));
    apply.addEventListener("click", () => { spec.onApply(collect()); closeFacets(true); });
    clear.addEventListener("click", () => { spec.onApply(Object.fromEntries(spec.fields.map((f) => [f.id, f.kind === "checks" ? [] : ""]))); closeFacets(true); });

    document.body.appendChild(panel);
    const box = anchor.getBoundingClientRect(), size = panel.getBoundingClientRect();
    panel.style.left = Math.max(8, Math.min(box.left, window.innerWidth - size.width - 8)) + "px";
    panel.style.top = Math.min(box.bottom + 6, Math.max(8, window.innerHeight - size.height - 8)) + "px";
    anchor.setAttribute("aria-expanded", "true");
    anchor.setAttribute("aria-haspopup", "dialog");

    const onKey = (event) => { if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); closeFacets(true); } };
    const onDown = (event) => { if (!panel.contains(event.target) && !anchor.contains(event.target)) closeFacets(false); };
    document.addEventListener("keydown", onKey, true);
    document.addEventListener("pointerdown", onDown, true);
    open = { panel, anchor, off: () => { document.removeEventListener("keydown", onKey, true); document.removeEventListener("pointerdown", onDown, true); } };
    const first = panel.querySelector("select, input");
    if (first) first.focus();
}
