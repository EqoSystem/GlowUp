// Data panels: the same data shown as a list, a row of cards, a grid of tiles or a graph.
//
// A page tells this module what data it has (registerDataset) and says when that data was loaded or
// changed (datasetChanged). adminLayouts.js then puts data panels on the page (mountWidget).
//
//   list   - one item under the other
//   row    - cards side by side that scroll sideways (drag them, or use the mouse wheel)
//   grid   - tiles. Where the data can be removed, the tiles work like the tiles of the mobile app:
//              click a tile            -> it is selected (glowing outline, pixel sweep); "Remove" takes the selected ones
//              press and hold a tile   -> all tiles shake and the left side of the panel turns into the remove area
//              drag a tile to the left -> it pops and is gone
//            Where the page has a Recycle bin (products, brands, ingredients) a removed tile goes there.
//            For other data the removed items wait in the bar at the bottom: "Restore" brings them back;
//            only "Delete forever" (which asks first) really deletes them from the database.
//   graph  - bars, a line, a donut or a table of the numbers, counted per group ("products by type").
//
// Everything a person typed (names, brands ...) is written with textContent, never as HTML.
import { icon, BIN_ICON, paginate, renderPager, toast, askConfirm, playSweep, calmMotion, hideTip } from "./adminShell.js";

export const VIEWS = [
    { id: "list", label: "List", icon: "list", hint: "One under the other." },
    { id: "row", label: "Row", icon: "rows", hint: "Cards side by side that scroll sideways." },
    { id: "grid", label: "Grid", icon: "grid", hint: "Tiles. Select or drag tiles to remove them quickly." },
    { id: "graph", label: "Graph", icon: "bars", hint: "Counted per group, to analyse the data." }
];
export const CHARTS = [
    { id: "bars", label: "Bars", icon: "bars" },
    { id: "line", label: "Line", icon: "line" },
    { id: "donut", label: "Donut", icon: "pie" },
    { id: "table", label: "Table", icon: "table" }
];

const LIST_SIZE = 8, GRID_SIZE = 12, ROW_LIMIT = 40;
// a row of the list that is still loading, in GlowUp's look (see "skeleton loading" in adminCommon.css)
const SKELETON_ROW = '<div class="sk-line-row"><i class="sk sk-dot"></i><span><i class="sk sk-text w60"></i><i class="sk sk-text w35 thin"></i></span><i class="sk sk-pill"></i></div>';
const HOLD_MS = 450;                       // press this long on a tile and the tiles start to shake
const BAR_LIMIT = 8;                       // a bar graph shows this many groups; the rest become "Other"
const DONUT_LIMIT = 6;                     // a donut is only readable with a few slices

const datasets = new Map();                // id -> what the page registered (+ ready)
const mounted = new Map();                 // panel element -> widget
const trash = new Map();                   // dataset id -> Map(item id -> row): removed, not yet deleted

// ---------------------------------------------------------------------------------------------
// Data
// ---------------------------------------------------------------------------------------------
/**
 * registerDataset("products", {
 *     label: "Products", noun: "products", nounOne: "product",
 *     items: () => rows,                                     // the rows as the page holds them
 *     map: (row) => ({ id, title, subtitle, meta, tone, value, valueLabel, swatches }),
 *     dimensions: [{ id, label, of: (row) => key | [keys], tone: (key) => tone, keys: [...], ordered, labelOf, fill, pick: (key) => {} }],
 *     measures: [{ id, label, noun, of: (row) => number }],  // besides "how many"
 *     open: (row) => {},                                      // a click on an item
 *     bin: (rows) => {},                                      // move to the page's Recycle bin (the page says what happened)
 *     remove: async (rows) => {},                             // or: really delete (the page reloads its data afterwards)
 *     removedWord: "removed", foreverLabel: "Delete forever", removeNote: "..."
 * })
 * tone = "ok" | "warn" | "bad" | "info" | "pink" | "muted"
 */
export function registerDataset(id, spec) {
    datasets.set(id, Object.assign({ id, ready: false, label: id, noun: "items", nounOne: "item" }, spec));
}

/** Call after the data of a set was loaded or changed: every panel that shows it is drawn again. */
export function datasetChanged(id) {
    const d = datasets.get(id);
    if (!d) return;
    d.ready = true;
    // something that was waiting in the remove bar and is gone from the data needs no bar any more
    const waiting = trash.get(id);
    if (waiting && waiting.size > 0) {
        const alive = new Set(d.items().map((row) => String(d.map(row).id)));
        Array.from(waiting.keys()).forEach((key) => { if (!alive.has(key)) waiting.delete(key); });
    }
    mounted.forEach((w) => { if (w.config.source === id) render(w); });
}

export const datasetList = () => Array.from(datasets.values()).map((d) => ({ id: d.id, label: d.label }));
export const datasetSpec = (id) => datasets.get(id) || null;
/** What can be counted: how many rows there are, plus the data's own numbers (countRows: false leaves the first one out). */
export const measuresOf = (d) => (d && d.countRows === false ? [] : [{ id: "count", label: "How many " + (d ? d.noun : "items"), noun: d ? d.noun : "items" }]).concat((d && d.measures) || []);

const trashOf = (id) => { if (!trash.has(id)) trash.set(id, new Map()); return trash.get(id); };
const toItem = (d, row) => { const item = d.map(row); item.id = String(item.id); item.row = row; return item; };
const visibleRows = (d) => { const gone = trashOf(d.id); return gone.size === 0 ? d.items() : d.items().filter((row) => !gone.has(String(d.map(row).id))); };
const number = (n) => (Math.round(n * 10) / 10).toLocaleString();

/** Fills in what is missing in a panel's settings and drops what its data does not have. */
export function normalizeConfig(config) {
    // the data a panel was made for stays its data, also while the page has not registered it yet
    const asked = (config && config.source) || "";
    const d = datasets.get(asked) || (asked ? null : Array.from(datasets.values())[0] || null);
    const out = { source: asked || (d ? d.id : ""), view: VIEWS.some((v) => v.id === (config && config.view)) ? config.view : "list" };
    if (out.view === "graph") {
        const dims = (d && d.dimensions) || [];
        out.by = dims.some((x) => x.id === config.by) ? config.by : (dims[0] ? dims[0].id : "");
        const measures = measuresOf(d);
        out.measure = measures.some((m) => m.id === config.measure) ? config.measure : (measures[0] ? measures[0].id : "count");
        out.graph = CHARTS.some((c) => c.id === config.graph) ? config.graph : "bars";
        const dim = dims.find((x) => x.id === out.by);
        if (out.graph === "line" && dim && !dim.ordered) { const timed = dims.find((x) => x.ordered); if (timed) out.by = timed.id; else out.graph = "bars"; }   // a line needs an order (time)
    }
    return out;
}

/** "Products by type", "Recent scans" ... */
export function defaultTitle(config) {
    const d = datasets.get(config.source);
    if (!d) return "Data panel";
    if (config.view !== "graph") return d.label;
    const dim = (d.dimensions || []).find((x) => x.id === config.by);
    return dim ? `${d.label} by ${dim.label.toLowerCase()}` : d.label;
}

// ---------------------------------------------------------------------------------------------
// Small builders (text always goes in as text)
// ---------------------------------------------------------------------------------------------
function h(tag, attrs, ...kids) {
    const el = document.createElement(tag);
    setAttrs(el, attrs);
    addKids(el, kids);
    return el;
}
function s(tag, attrs, ...kids) {
    const el = document.createElementNS("http://www.w3.org/2000/svg", tag);
    setAttrs(el, attrs);
    addKids(el, kids);
    return el;
}
function setAttrs(el, attrs) {
    if (!attrs) return;
    Object.keys(attrs).forEach((key) => {
        const value = attrs[key];
        if (value === null || value === undefined || value === false) return;
        if (key === "text") el.textContent = value;
        else if (key === "markup") el.innerHTML = value;                 // only for this file's own icons
        else if (key === "class") el.setAttribute("class", value);
        else if (key.startsWith("on")) el.addEventListener(key.slice(2), value);
        else el.setAttribute(key, value === true ? "" : value);
    });
}
function addKids(el, kids) {
    kids.flat().forEach((kid) => {
        if (kid === null || kid === undefined || kid === false) return;
        el.appendChild(typeof kid === "string" || typeof kid === "number" ? document.createTextNode(String(kid)) : kid);
    });
}
const TONE_VAR = { ok: "var(--ok)", warn: "var(--warn)", bad: "var(--bad)", info: "var(--info)", pink: "var(--accent)", muted: "var(--viz-other)" };
const emptyBox = (message, hint) => h("div", { class: "empty-state" }, h("p", { class: "empty-message", text: message }), hint ? h("span", { class: "empty-hint", text: hint }) : null);

// ---------------------------------------------------------------------------------------------
// Putting a data panel on the page
// ---------------------------------------------------------------------------------------------
/** Shows `config` ({ source, view, graph, by, measure }) inside a panel. Call again to change it. */
export function mountWidget(panel, config) {
    let w = mounted.get(panel);
    if (!w) {
        const inner = panel.querySelector(":scope > .panel-inner") || panel;
        const body = h("div", { class: "widget-body" });
        inner.appendChild(body);
        const head = inner.querySelector(":scope > .panel-head");
        const pager = h("div", { class: "pager widget-pager" });
        if (head) head.insertBefore(pager, head.querySelector(".panel-menu-btn"));
        w = { panel, body, pager, wanted: null, config: null, page: { page: 1, size: LIST_SIZE }, selected: new Set(), shaking: false, width: 0 };
        // a graph is drawn for the width it has: draw it again when the panel is resized
        w.watcher = new ResizeObserver(() => {
            const width = Math.round(body.clientWidth);
            if (!w.config || Math.abs(width - w.width) < 12 || w.config.view !== "graph") { w.width = width; return; }
            w.width = width;
            render(w, true);                       // only its size changed: draw it again without the entrance animation
        });
        w.watcher.observe(body);
        mounted.set(panel, w);
    }
    const before = w.config;
    w.wanted = Object.assign({}, config);          // as it was asked for: the data may only be registered later
    w.config = normalizeConfig(w.wanted);
    if (!before || before.source !== w.config.source || before.view !== w.config.view) { w.page.page = 1; w.selected.clear(); w.shaking = false; }
    render(w);
    return w;
}

export function unmountWidget(panel) {
    const w = mounted.get(panel);
    if (!w) return;
    w.watcher.disconnect();
    w.body.remove();
    w.pager.remove();
    mounted.delete(panel);
}

function render(w, calm) {
    w.config = normalizeConfig(w.wanted);
    const d = datasets.get(w.config.source);
    w.body.className = "widget-body view-" + w.config.view + (w.shaking ? " shaking" : "") + (calm ? " calm" : "");
    w.pager.innerHTML = "";
    const count = w.panel.querySelector(".widget-count");
    if (!d) { w.body.replaceChildren(emptyBox("This data is not on this page.", "Open the panel's menu and choose something else to show.")); return; }
    if (!d.ready) { w.body.replaceChildren(h("div", { class: "sk-list", markup: SKELETON_ROW.repeat(4) })); return; }

    const rows = visibleRows(d);
    if (count) count.textContent = `${rows.length} ${rows.length === 1 ? d.nounOne : d.noun}`;
    if (rows.length === 0 && trashOf(d.id).size === 0) { w.body.replaceChildren(emptyBox(`No ${d.noun} yet.`)); return; }

    if (w.config.view === "graph") renderGraph(w, d, rows);
    else if (w.config.view === "grid") renderGrid(w, d, rows);
    else if (w.config.view === "row") renderRow(w, d, rows);
    else renderList(w, d, rows);
    paintActionBar(w, d);
}

const repaintAll = (id) => mounted.forEach((w) => { if (w.config.source === id) render(w); });

// ---------------------------------------------------------------------------------------------
// List, row
// ---------------------------------------------------------------------------------------------
function valueNode(item) {
    if (item.swatches && item.swatches.length > 0)
        return h("span", { class: "w-swatches" }, item.swatches.slice(0, 4).map((colour) => h("span", { class: "swatch", style: /^#[0-9a-f]{6}$/i.test(colour) ? "background:" + colour : null })));
    if (item.value === undefined || item.value === null) return null;
    return h("span", { class: "w-value" }, number(item.value), item.valueLabel ? h("small", { text: " " + item.valueLabel }) : null);
}

function opener(d, item) {
    if (!d.open) return {};
    return {
        tabindex: 0, role: "button",
        onclick: () => d.open(item.row),
        onkeydown: (event) => { if (event.key === "Enter") d.open(item.row); }
    };
}

function renderList(w, d, rows) {
    w.page.size = LIST_SIZE;
    const items = paginate(rows, w.page).map((row) => toItem(d, row));
    const list = h("ul", { class: "w-list" }, items.map((item) => h("li", Object.assign({ class: "w-item" + (d.open ? " can-open" : "") }, opener(d, item)),
        item.meta ? null : h("span", { class: "tone-dot tone-" + (item.tone || "muted") }),
        h("div", { class: "w-main" }, h("div", { class: "w-title", text: item.title }), item.subtitle ? h("div", { class: "w-sub", text: item.subtitle }) : null),
        item.meta ? h("span", { class: "status-badge tone-" + (item.tone || "muted"), text: item.meta }) : null,
        valueNode(item))));
    w.body.replaceChildren(list);
    renderPager(w.pager, w.page, rows.length, () => render(w), d.noun);
}

function renderRow(w, d, rows) {
    const items = rows.slice(0, ROW_LIMIT).map((row) => toItem(d, row));
    const strip = h("div", { class: "w-strip", tabindex: 0, "aria-label": d.label }, items.map((item) => h("div", Object.assign({ class: "w-card" + (d.open ? " can-open" : "") }, opener(d, item)),
        h("div", { class: "w-card-top" }, item.meta ? h("span", { class: "status-badge tone-" + (item.tone || "muted"), text: item.meta }) : h("span", { class: "tone-dot tone-" + (item.tone || "muted") })),
        h("div", { class: "w-title", text: item.title }),
        item.subtitle ? h("div", { class: "w-sub", text: item.subtitle }) : null,
        h("div", { class: "w-foot" }, valueNode(item)))));
    if (rows.length > ROW_LIMIT) strip.appendChild(h("div", { class: "w-card more", text: `and ${rows.length - ROW_LIMIT} more` }));
    w.body.replaceChildren(strip);
    wireStrip(strip);
}

/** A sideways list: the wheel moves it sideways, and it can be dragged with the mouse like on a touch screen. */
function wireStrip(strip) {
    strip.addEventListener("wheel", (event) => {
        if (Math.abs(event.deltaY) <= Math.abs(event.deltaX)) return;
        const room = strip.scrollWidth - strip.clientWidth;
        if (room <= 0 || (event.deltaY < 0 && strip.scrollLeft <= 0) || (event.deltaY > 0 && strip.scrollLeft >= room - 1)) return;       // at an end: the page scrolls
        event.preventDefault();
        strip.scrollLeft += event.deltaY;
    }, { passive: false });
    let from = null;
    const move = (event) => {
        if (!from) return;
        const dx = event.clientX - from.x;
        if (!from.moved && Math.abs(dx) < 5) return;
        from.moved = true;
        strip.classList.add("dragging");
        strip.scrollLeft = from.left - dx;
    };
    const up = () => {
        window.removeEventListener("pointermove", move);
        window.removeEventListener("pointerup", up);
        if (from && from.moved) { strip.classList.remove("dragging"); strip.dataset.justDragged = "1"; window.setTimeout(() => delete strip.dataset.justDragged, 0); }
        from = null;
    };
    strip.addEventListener("pointerdown", (event) => {
        if (event.pointerType !== "mouse" || event.button !== 0) return;       // a finger scrolls it by itself
        from = { x: event.clientX, left: strip.scrollLeft, moved: false };
        window.addEventListener("pointermove", move);
        window.addEventListener("pointerup", up);
    });
    // a drag must not count as a click on the card it started on
    strip.addEventListener("click", (event) => { if (strip.dataset.justDragged) { event.stopPropagation(); event.preventDefault(); } }, true);
}

// ---------------------------------------------------------------------------------------------
// Grid: tiles that are selected, shaken, dragged and popped like the tiles of the mobile app
// ---------------------------------------------------------------------------------------------
function renderGrid(w, d, rows) {
    w.page.size = GRID_SIZE;
    const items = paginate(rows, w.page).map((row) => toItem(d, row));
    const can = !!(d.remove || d.bin);
    const grid = h("div", { class: "w-grid", role: can ? "listbox" : null, "aria-multiselectable": can ? "true" : null }, items.map((item) => h("div", {
        class: "w-tile" + (w.selected.has(item.id) ? " is-selected" : ""), "data-id": item.id, tabindex: 0,
        role: can ? "option" : "button", "aria-selected": can ? String(w.selected.has(item.id)) : null
    },
        h("div", { class: "w-card-top" }, item.meta ? h("span", { class: "status-badge tone-" + (item.tone || "muted"), text: item.meta }) : h("span", { class: "tone-dot tone-" + (item.tone || "muted") })),
        h("div", { class: "w-title", text: item.title }),
        item.subtitle ? h("div", { class: "w-sub", text: item.subtitle }) : null,
        h("div", { class: "w-foot" }, valueNode(item)))));
    const kids = [grid];
    if (can) {
        kids.unshift(h("div", { class: "remove-zone", "aria-hidden": "true" }, h("span", { class: "zone-disc", markup: d.bin ? BIN_ICON : icon("trash") }), h("span", { class: "zone-text", text: d.bin ? "Drag here: Recycle bin" : "Drag here to remove" })));
        kids.push(h("div", { class: "chart-note", text: "Click: select  ·  Double-click: open  ·  Press and hold: drag left to " + (d.bin ? "send it to the Recycle bin" : "remove") }));
    }
    w.body.replaceChildren(...kids);
    renderPager(w.pager, w.page, rows.length, () => render(w), d.noun);
    wireGrid(w, d, grid);
}

function wireGrid(w, d, grid) {
    const can = !!(d.remove || d.bin);
    const rowOf = (id) => d.items().find((row) => String(d.map(row).id) === id);
    const zone = w.body.querySelector(".remove-zone");
    let press = null, swallowClick = false;

    const setSelected = (tile, on) => {
        const id = tile.dataset.id;
        if (on) w.selected.add(id); else w.selected.delete(id);
        tile.classList.toggle("is-selected", on);
        tile.setAttribute("aria-selected", String(on));
        if (on) playSweep(tile);
        paintActionBar(w, d);
    };

    grid.addEventListener("click", (event) => {
        const tile = event.target.closest(".w-tile");
        if (swallowClick) { swallowClick = false; return; }
        if (!tile) { if (w.shaking) setShaking(w, d, false); return; }
        if (w.shaking) return;                                   // while the tiles shake a tap does nothing, as on the phone
        if (can) setSelected(tile, !w.selected.has(tile.dataset.id));
        else if (d.open) d.open(rowOf(tile.dataset.id));
    });
    grid.addEventListener("dblclick", (event) => {
        const tile = event.target.closest(".w-tile");
        if (tile && d.open && !w.shaking) { if (can) setSelected(tile, false); d.open(rowOf(tile.dataset.id)); }
    });
    grid.addEventListener("keydown", (event) => {
        const tile = event.target.closest(".w-tile");
        if (!tile) return;
        if (event.key === "Enter" && d.open) d.open(rowOf(tile.dataset.id));
        else if (event.key === " " && can) { event.preventDefault(); setSelected(tile, !w.selected.has(tile.dataset.id)); }
        else if ((event.key === "Delete" || event.key === "Backspace") && can && w.selected.size > 0) { event.preventDefault(); sendToTrash(w, d, Array.from(w.selected)); }
        else if (event.key === "Escape" && w.shaking) setShaking(w, d, false);
    });
    if (!can) return;

    const threshold = () => Math.min(170, w.body.clientWidth * 0.34);       // drag a tile this far to the left and it is removed
    const move = (event) => {
        if (!press) return;
        const dx = event.clientX - press.x, dy = event.clientY - press.y;
        if (!press.dragging) {
            if (Math.hypot(dx, dy) < 6) return;
            if (!w.shaking) { window.clearTimeout(press.timer); return; }        // moved before the hold was over: not a drag
            press.dragging = true;
            press.tile.classList.add("dragging");
            document.body.classList.add("is-dragging");
            hideTip();
        }
        press.tile.style.translate = `${dx}px ${dy}px`;
        const k = Math.max(0, Math.min(1, -dx / threshold()));
        press.k = k;
        if (zone) { zone.style.opacity = String(0.55 + 0.45 * k); zone.classList.toggle("armed", k >= 1); }
    };
    const up = () => {
        window.removeEventListener("pointermove", move);
        window.removeEventListener("pointerup", up);
        window.removeEventListener("pointercancel", up);
        if (!press) return;
        window.clearTimeout(press.timer);
        const done = press;
        press = null;
        document.body.classList.remove("is-dragging");
        if (zone) { zone.style.opacity = ""; zone.classList.remove("armed"); }
        if (done.held || done.dragging) swallowClick = true;
        if (!done.dragging) return;
        if (done.k >= 1) { sendToTrash(w, d, [done.id], done.tile); return; }
        // not far enough: the tile springs back
        done.tile.style.transition = "translate .32s cubic-bezier(.3, 1.4, .5, 1)";
        done.tile.style.translate = "0px 0px";
        window.setTimeout(() => { done.tile.classList.remove("dragging"); done.tile.style.transition = ""; done.tile.style.translate = ""; }, 330);
    };
    grid.addEventListener("pointerdown", (event) => {
        const tile = event.target.closest(".w-tile");
        if (!tile || event.button > 0) return;
        swallowClick = false;
        press = { tile, id: tile.dataset.id, x: event.clientX, y: event.clientY, dragging: false, held: false, k: 0 };
        press.timer = window.setTimeout(() => { if (press && !w.shaking) { press.held = true; setShaking(w, d, true); } }, HOLD_MS);
        window.addEventListener("pointermove", move);
        window.addEventListener("pointerup", up);
        window.addEventListener("pointercancel", up);
    });
}

/** The tiles start (or stop) shaking and the remove area on the left shows (or hides). */
function setShaking(w, d, on) {
    if (w.shaking === on) return;
    w.shaking = on;
    w.body.classList.toggle("shaking", on);
    if (on) {
        w.selected.clear();
        w.body.querySelectorAll(".w-tile.is-selected").forEach((tile) => tile.classList.remove("is-selected"));
        const stop = (event) => {
            if (event.type === "keydown" && event.key !== "Escape") return;
            if (event.type === "pointerdown" && w.body.contains(event.target)) return;
            document.removeEventListener("keydown", stop, true);
            document.removeEventListener("pointerdown", stop, true);
            setShaking(w, d, false);
        };
        w.stopShaking = () => { document.removeEventListener("keydown", stop, true); document.removeEventListener("pointerdown", stop, true); };
        document.addEventListener("keydown", stop, true);
        document.addEventListener("pointerdown", stop, true);
    } else if (w.stopShaking) { w.stopShaking(); w.stopShaking = null; }
    paintActionBar(w, d);
}

/** The tiles pop (swell, then burst) and wait in the remove bar. Nothing is deleted from the database yet. */
function sendToTrash(w, d, ids, draggedTile) {
    const tiles = ids.map((id) => w.body.querySelector(`.w-tile[data-id="${CSS.escape(id)}"]`)).filter((tile) => tile);
    const finish = () => {
        const rows = ids.map((id) => d.items().find((r) => String(d.map(r).id) === id)).filter((row) => row);
        ids.forEach((id) => w.selected.delete(id));
        if (d.bin) { d.bin(rows); return; }                       // the page moves them to its Recycle bin and draws everything again
        const gone = trashOf(d.id);
        rows.forEach((row) => gone.set(String(d.map(row).id), row));
        glideTiles(w, () => repaintAll(d.id));
    };
    if (calmMotion() || tiles.length === 0) { finish(); return; }
    tiles.forEach((tile, index) => {
        if (tile === draggedTile) tile.style.transition = "";
        tile.style.animationDelay = (index * 50) + "ms";
        tile.classList.add("popping");
    });
    window.setTimeout(finish, 250 + tiles.length * 50);
}

/** Draws the panel again and lets the tiles that stay glide to their new places. */
function glideTiles(w, redraw) {
    const before = new Map(Array.from(w.body.querySelectorAll(".w-tile")).map((tile) => [tile.dataset.id, tile.getBoundingClientRect()]));
    redraw();
    if (calmMotion()) return;
    w.body.querySelectorAll(".w-tile").forEach((tile) => {
        const was = before.get(tile.dataset.id), now = tile.getBoundingClientRect();
        if (!was) { tile.classList.add("arriving"); return; }
        const dx = was.left - now.left, dy = was.top - now.top;
        if (Math.abs(dx) > 1 || Math.abs(dy) > 1) tile.animate([{ transform: `translate(${dx}px, ${dy}px)` }, { transform: "translate(0, 0)" }], { duration: 300, easing: "cubic-bezier(.2, .8, .2, 1)" });
    });
}

/** The bar at the bottom of a data panel: what is selected, what was removed, and "Done" while the tiles shake. */
function paintActionBar(w, d) {
    const old = w.body.querySelector(":scope > .w-bar");
    if (old) old.remove();
    if (!d.remove && !d.bin) return;
    const selected = w.config.view === "grid" ? w.selected.size : 0, removed = d.bin ? 0 : trashOf(d.id).size;
    if (!selected && !removed && !w.shaking) return;
    // a graph leaves the bar to a list, row or grid of the same data when one is on the page: one bar is enough
    if (w.config.view === "graph" && Array.from(mounted.values()).some((other) => other !== w && other.config.source === d.id && other.config.view !== "graph" && other.panel.isConnected)) return;
    const word = d.removedWord || "removed", forever = d.foreverLabel || "Delete forever";
    const button = (label, cls, run, tip) => h("button", { type: "button", class: "row-btn " + cls, onclick: run, "data-tip": tip || null }, label);
    const bar = h("div", { class: "w-bar" });
    if (w.shaking) bar.append(h("span", { class: "w-bar-text", text: d.bin ? "Drag a tile to the left: it goes to the Recycle bin." : "Drag a tile to the left to remove it." }));
    if (selected) bar.append(h("span", { class: "w-bar-text" }, h("b", { text: String(selected) }), " selected"),
        button(d.bin ? "Recycle bin" : "Remove", "bar-main", () => sendToTrash(w, d, Array.from(w.selected)), d.bin ? "The selected tiles go to the Recycle bin. They can be restored there." : "The selected tiles pop and wait here. Nothing is deleted until you press " + forever + "."),
        button("Clear", "bar-quiet", () => { w.selected.clear(); render(w); }));
    if (removed) bar.append(h("span", { class: "w-bar-text" }, h("b", { text: String(removed) }), " " + word),
        button("Restore", "bar-main", () => { trashOf(d.id).clear(); glideTiles(w, () => repaintAll(d.id)); }, "Bring them back. They were never deleted."),
        button(forever, "bar-quiet danger", () => purge(w, d), "Really delete them from the database. It asks first."));
    if (w.shaking) bar.append(h("span", { class: "spacer" }), button("Done", "bar-main", () => setShaking(w, d, false), "Stop the shaking."));
    w.body.appendChild(bar);
}

async function purge(w, d) {
    const rows = Array.from(trashOf(d.id).values());
    if (rows.length === 0) return;
    const forever = d.foreverLabel || "Delete forever";
    const sure = await askConfirm({
        title: `${forever}: ${rows.length} ${rows.length === 1 ? d.nounOne : d.noun}?`,
        message: d.removeNote || "They are deleted from the database. This cannot be undone.",
        items: rows.map((row) => d.map(row).title || "(no name)"), confirmLabel: forever, danger: true
    });
    if (!sure) return;
    const bar = w.body.querySelector(":scope > .w-bar");
    if (bar) bar.classList.add("busy");
    try {
        await d.remove(rows);
        trashOf(d.id).clear();
    } catch (error) {
        console.error("Removing failed: ", error);
        toast("Not everything was removed. " + (error && error.message ? error.message : ""), "error");
    }
    repaintAll(d.id);
}

// ---------------------------------------------------------------------------------------------
// Graphs
// ---------------------------------------------------------------------------------------------
/** Counts (or adds up) the rows per group. Returns [{ key, label, value, tone }] in the order to draw them. */
function aggregate(d, rows, dim, measure) {
    const sums = new Map();
    rows.forEach((row) => {
        let keys = dim.of(row);
        if (!Array.isArray(keys)) keys = [keys];
        if (keys.length === 0) keys = [null];
        const amount = measure.of ? Number(measure.of(row)) || 0 : 1;
        keys.forEach((k) => { const key = k === null || k === undefined || k === "" ? (dim.none || "Not set") : String(k); sums.set(key, (sums.get(key) || 0) + amount); });
    });
    if (dim.fill) dim.fill(Array.from(sums.keys())).forEach((key) => { if (!sums.has(key)) sums.set(key, 0); });
    if (dim.keys) dim.keys.forEach((key) => { if (!sums.has(key)) sums.set(key, 0); });
    const out = Array.from(sums, ([key, value]) => ({ key, value, label: dim.labelOf ? dim.labelOf(key) : key, tone: dim.tone ? dim.tone(key) : null }));
    if (dim.keys) out.sort((a, b) => { const ia = dim.keys.indexOf(a.key), ib = dim.keys.indexOf(b.key); return (ia < 0 ? 99 : ia) - (ib < 0 ? 99 : ib); });
    else if (dim.ordered) out.sort((a, b) => (a.key < b.key ? -1 : a.key > b.key ? 1 : 0));
    else out.sort((a, b) => b.value - a.value || a.label.localeCompare(b.label));
    return out;
}

/**
 * A bar graph stays readable with a handful of bars: the biggest groups are drawn, and the rest is told in words
 * under the graph (one long "Other" bar would dwarf the bars that matter).
 */
function topGroups(groups, limit, noun) {
    if (groups.length <= limit) return { shown: groups, rest: "" };
    const rest = groups.slice(limit);
    return { shown: groups.slice(0, limit), rest: `and ${rest.length} more: ${number(rest.reduce((sum, g) => sum + g.value, 0))} ${noun} together. The Table shows every one.` };
}

function renderGraph(w, d, rows) {
    const dim = (d.dimensions || []).find((x) => x.id === w.config.by);
    if (!dim) { w.body.replaceChildren(emptyBox("This data has nothing to group by.", "Choose List, Row or Grid in the panel's menu.")); return; }
    const measure = measuresOf(d).find((m) => m.id === w.config.measure) || measuresOf(d)[0] || { id: "count", noun: d.noun };
    const groups = aggregate(d, rows, dim, measure);
    const total = groups.reduce((sum, g) => sum + g.value, 0);
    const noun = measure.noun || d.noun;
    const pick = dim.pick ? (group) => { if (!group.other) dim.pick(group.key); } : null;
    const caption = h("div", { class: "chart-cap" }, h("b", { text: number(total) }), ` ${noun} by ${dim.label.toLowerCase()}`, pick ? h("span", { class: "muted", text: " · click a group to filter the page" }) : null);
    let chart, note = null;

    let kind = w.config.graph;
    if (kind === "line" && !dim.ordered) kind = "bars";
    if (kind === "donut") {
        const real = groups.filter((g) => g.value > 0);
        if (real.length > DONUT_LIMIT || real.length < 2) { kind = "bars"; note = real.length < 2 ? null : `More than ${DONUT_LIMIT} groups do not fit a donut: shown as bars.`; }
    }
    if (total === 0) chart = emptyBox("Nothing to count yet.");
    else if (kind === "table") chart = tableOf(groups, total, dim, noun);
    else if (kind === "donut") chart = donutOf(groups.filter((g) => g.value > 0), total, dim, noun, pick);
    else if (kind === "line") chart = plotOf(w, groups, noun, pick, true);
    else if (dim.ordered) chart = plotOf(w, groups, noun, pick, false);
    else {
        const top = topGroups(groups, BAR_LIMIT, noun);
        chart = barsOf(top.shown, total, noun, pick);
        if (top.rest) note = top.rest;
    }
    w.body.replaceChildren(caption, chart);
    if (note) w.body.appendChild(h("div", { class: "chart-note", text: note }));
    const reading = total > 0 ? insightOf(groups, total, dim, noun) : "";
    if (reading) w.body.appendChild(h("div", { class: "chart-insight" }, h("span", { markup: icon("sparkle") }), h("span", { text: reading })));
}

/** One sentence that reads a graph: the biggest group, or for days and hours the peak and the latest. */
function insightOf(groups, total, dim, noun) {
    const real = groups.filter((g) => g.value > 0);
    if (real.length === 0) return "";
    const top = real.reduce((best, g) => (g.value > best.value ? g : best), real[0]);
    if (dim.ordered) {
        const last = groups[groups.length - 1];
        return `Peak: ${number(top.value)} ${noun} at ${top.label}.` + (last !== top ? ` Latest (${last.label}): ${number(last.value)}.` : "");
    }
    if (real.length === 1) return `All ${number(total)} ${noun} are "${top.label}".`;
    const low = real.reduce((least, g) => (g.value < least.value ? g : least), real[0]);
    return `Biggest: ${top.label} with ${number(top.value)} (${share(top.value, total)}).` + (real.length > 2 && low.value < top.value ? ` Smallest: ${low.label} with ${number(low.value)}.` : "");
}

const share = (value, total) => (total > 0 ? Math.round(value * 100 / total) + "%" : "0%");
/** What a mark tells on hover and on keyboard focus: the number first, then what it is. */
const tipOf = (group, total, noun) => ({ "data-tip-title": `${number(group.value)} ${noun}`, "data-tip": `${group.label} · ${share(group.value, total)} of the total` });
const pickable = (pick, group) => (pick && !group.other
    ? { tabindex: 0, role: "button", onclick: () => pick(group), onkeydown: (event) => { if (event.key === "Enter") pick(group); } }
    : { tabindex: 0 });

/** Horizontal bars: one group per line, the value at the tip of its bar. One colour, because the groups are one series. */
function barsOf(groups, total, noun, pick) {
    const max = Math.max(1, ...groups.map((g) => g.value));
    return h("div", { class: "chart bars" }, groups.map((group, index) => h("div", Object.assign({ class: "bar-row" + (pick && !group.other ? " can-pick" : "") }, tipOf(group, total, noun), pickable(pick, group)),
        h("span", { class: "bar-label", text: group.label }),
        h("span", { class: "bar-line" },
            h("span", { class: "bar-fill", style: `width: calc((100% - 52px) * ${group.value / max}); background: ${group.tone ? TONE_VAR[group.tone] : "var(--viz-1)"}; animation-delay: ${index * 40}ms` }),
            h("span", { class: "bar-value", text: number(group.value) })))));
}

function tableOf(groups, total, dim, noun) {
    return h("table", { class: "admin-table w-table" },
        h("thead", null, h("tr", null, h("th", { text: dim.label }), h("th", { class: "text-right", text: noun.charAt(0).toUpperCase() + noun.slice(1) }), h("th", { class: "text-right", text: "Share" }))),
        h("tbody", null, groups.map((group) => h("tr", null, h("td", { text: group.label }), h("td", { class: "text-right num", text: number(group.value) }), h("td", { class: "text-right num", text: share(group.value, total) })))));
}

/** A step that reads cleanly on an axis: 1, 2, 5, 10, 20, 50 ... */
function niceStep(raw) {
    const power = Math.pow(10, Math.floor(Math.log10(Math.max(raw, 1e-9))));
    const unit = raw / power;
    return Math.max(1, (unit <= 1 ? 1 : unit <= 2 ? 2 : unit <= 5 ? 5 : 10) * power);
}

/** Columns (or a line) over an ordered scale, such as days: thin marks, hairline grid, one direct label, a hover band per position. */
function plotOf(w, groups, noun, pick, asLine) {
    const width = Math.max(260, Math.round(w.body.clientWidth || 480)), height = 200;
    const m = { left: 34, right: 16, top: 16, bottom: 26 };
    const plotW = width - m.left - m.right, plotH = height - m.top - m.bottom;
    const max = Math.max(1, ...groups.map((g) => g.value));
    const step = niceStep(max / 3), top = Math.ceil(max / step) * step;
    const band = plotW / groups.length;
    const x = (i) => m.left + band * (i + 0.5), y = (v) => m.top + plotH * (1 - v / top);
    const total = groups.reduce((sum, g) => sum + g.value, 0);
    const svg = s("svg", { class: "chart plot", viewBox: `0 0 ${width} ${height}`, width, height, role: "img", "aria-label": `${noun} per group` });

    for (let tick = 0; tick <= top + 1e-9; tick += step) {                                   // grid and its numbers: quiet
        svg.appendChild(s("line", { class: tick === 0 ? "axis-base" : "axis-grid", x1: m.left, x2: width - m.right, y1: y(tick), y2: y(tick) }));
        svg.appendChild(s("text", { class: "axis-text", x: m.left - 8, y: y(tick) + 4, "text-anchor": "end" }, number(tick)));
    }
    const every = Math.max(1, Math.ceil(groups.length / Math.max(2, Math.floor(plotW / 58))));
    groups.forEach((group, i) => { if (i % every === 0 || i === groups.length - 1 && (groups.length - 1) % every >= every / 2) svg.appendChild(s("text", { class: "axis-text", x: x(i), y: height - 8, "text-anchor": "middle" }, group.label)); });

    const peak = groups.reduce((best, g, i) => (g.value > groups[best].value ? i : best), 0);
    const labelAt = asLine ? groups.length - 1 : peak;                                      // one direct label: the end of a line, the tallest column
    if (asLine) {
        const points = groups.map((g, i) => `${x(i)},${y(g.value)}`);
        svg.appendChild(s("path", { class: "line-area", d: `M${x(0)},${y(0)} L${points.join(" L")} L${x(groups.length - 1)},${y(0)} Z` }));
        svg.appendChild(s("path", { class: "line-path", d: "M" + points.join(" L") }));
        svg.appendChild(s("circle", { class: "line-dot", cx: x(groups.length - 1), cy: y(groups[groups.length - 1].value), r: 4.5 }));
    } else {
        const barW = Math.min(24, band * 0.62);
        groups.forEach((g, i) => {
            if (g.value <= 0) return;
            const h0 = y(0) - y(g.value), r = Math.min(4, h0, barW / 2), left = x(i) - barW / 2;
            svg.appendChild(s("path", { class: "column", "data-i": i, d: `M${left},${y(0)} V${y(g.value) + r} Q${left},${y(g.value)} ${left + r},${y(g.value)} H${left + barW - r} Q${left + barW},${y(g.value)} ${left + barW},${y(g.value) + r} V${y(0)} Z` }));
        });
    }
    svg.appendChild(s("text", { class: "mark-label", x: x(labelAt), y: y(groups[labelAt].value) - 9, "text-anchor": labelAt === groups.length - 1 && asLine ? "end" : "middle" }, number(groups[labelAt].value)));

    // hover and keyboard: one band per position, as tall as the plot, so nobody has to hit a thin mark
    const cross = s("line", { class: "crosshair", y1: m.top, y2: y(0) });
    const dot = s("circle", { class: "line-dot hover", r: 4.5 });
    svg.append(cross, dot);
    const show = (i) => {
        cross.setAttribute("x1", x(i)); cross.setAttribute("x2", x(i)); cross.classList.add("on");
        if (asLine) { dot.setAttribute("cx", x(i)); dot.setAttribute("cy", y(groups[i].value)); dot.classList.add("on"); }
        svg.querySelectorAll(".column").forEach((c) => c.classList.toggle("lit", Number(c.getAttribute("data-i")) === i));
    };
    const hide = () => { cross.classList.remove("on"); dot.classList.remove("on"); svg.querySelectorAll(".column.lit").forEach((c) => c.classList.remove("lit")); };
    groups.forEach((group, i) => {
        const hit = s("rect", Object.assign({ class: "hit" + (pick ? " can-pick" : ""), x: m.left + band * i, y: m.top, width: band, height: plotH }, tipOf(group, total, noun), pickable(pick, group)));
        hit.addEventListener("pointerenter", () => show(i));
        hit.addEventListener("focus", () => show(i));
        hit.addEventListener("pointerleave", hide);
        hit.addEventListener("blur", hide);
        svg.appendChild(hit);
    });
    return svg;
}

/** A donut for a few parts of a whole, with a legend that carries the names and numbers (never colour alone). */
function donutOf(groups, total, dim, noun, pick) {
    const R = 52, STROKE = 17, C = 2 * Math.PI * R, GAP = 2.5;
    // a colour belongs to a group, not to its rank: the groups take the slots in a fixed order
    const fixed = dim.keys ? dim.keys.slice() : groups.map((g) => g.key).sort();
    const colourOf = (group) => (group.tone ? TONE_VAR[group.tone] : `var(--viz-${(Math.max(0, fixed.indexOf(group.key)) % DONUT_LIMIT) + 1})`);
    const svg = s("svg", { class: "chart donut", viewBox: "0 0 140 140", width: 150, height: 150, role: "img", "aria-label": `${noun} by ${dim.label.toLowerCase()}` });
    const legend = h("ul", { class: "legend" });
    let offset = 0;
    groups.forEach((group, index) => {
        const length = C * group.value / total;
        const arc = s("circle", Object.assign({
            class: "slice" + (pick ? " can-pick" : ""), cx: 70, cy: 70, r: R, "stroke-width": STROKE,
            style: `stroke: ${colourOf(group)}`, "stroke-dasharray": `${Math.max(0.5, length - GAP)} ${C}`, "stroke-dashoffset": -offset, transform: "rotate(-90 70 70)"
        }, tipOf(group, total, noun), pickable(pick, group)));
        const row = h("li", Object.assign({ class: "legend-row" + (pick ? " can-pick" : "") }, tipOf(group, total, noun), pickable(pick, group)),
            h("span", { class: "legend-key", style: `background: ${colourOf(group)}` }),
            h("span", { class: "legend-label", text: group.label }),
            h("span", { class: "legend-value", text: number(group.value) }),
            h("span", { class: "legend-share", text: share(group.value, total) }));
        const lit = (on) => { svg.classList.toggle("has-lit", on); arc.classList.toggle("lit", on); row.classList.toggle("lit", on); };
        [arc, row].forEach((el) => { el.addEventListener("pointerenter", () => lit(true)); el.addEventListener("pointerleave", () => lit(false)); el.addEventListener("focus", () => lit(true)); el.addEventListener("blur", () => lit(false)); });
        arc.style.animationDelay = (index * 60) + "ms";
        svg.appendChild(arc);
        legend.appendChild(row);
        offset += length;
    });
    svg.appendChild(s("text", { class: "donut-total", x: 70, y: 70, "text-anchor": "middle" }, number(total)));
    svg.appendChild(s("text", { class: "axis-text", x: 70, y: 86, "text-anchor": "middle" }, noun));
    return h("div", { class: "donut-wrap" }, svg, legend);
}
