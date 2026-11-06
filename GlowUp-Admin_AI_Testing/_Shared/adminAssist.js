// Helpers: one slim line in a panel that says what needs attention there and offers the next step.
//
// A page tells what the helper of a panel can say:
//     registerAssist("table", () => [{ text, tone, label, run, tip }, ...])     // most important first
// and calls refreshAssist() whenever its data changed. The helper shows the first line; the small
// arrow steps to the next one. tone = "bad" | "warn" | "info" | "ok" | "" (a plain tip).
// "label" + "run" add one button that does what the line suggests.
// A provider that returns null says "my data is not here yet": the panel shows a loading shape where the line will be.
// A helper can be hidden per panel (its small x); the panel's "..." menu and Customise bring it back.
//
// The text is always written with textContent, never as HTML.
import { icon, toast, infoBadge, refreshInfoWindow } from "./adminShell.js";
import { setPanelSummary } from "./adminFold.js";

const OFF_KEY = "glowup.assist.off.v1";
const providers = new Map();          // panel id -> what the page registered
const shown = new Map();              // panel id -> which of its lines shows
const notesOf = new Map();            // panel id -> id of the list (in a notes panel) that carries its lines, most important first
const badgeOf = new Map();            // panel id -> id of the slot whose info badge carries its lines (instead of a strip in the panel)
let pageName = "";

const readOff = () => {
    try { const saved = JSON.parse(localStorage.getItem(OFF_KEY) || "[]"); return new Set(Array.isArray(saved) ? saved.map(String) : []); }
    catch (error) { return new Set(); }
};
const writeOff = (set) => { try { localStorage.setItem(OFF_KEY, JSON.stringify(Array.from(set).slice(0, 60))); } catch (error) { /* private window: hidden until the page is left */ } };
const nameOf = (panelId) => pageName + "/" + panelId;

/** Call once per page, before the helpers are registered: a hidden helper is remembered per page. */
export function setAssistPage(page) { pageName = String(page || ""); }

export function registerAssist(panelId, provider, options) {
    providers.set(panelId, provider);
    if (options && options.badge) badgeOf.set(panelId, String(options.badge)); else badgeOf.delete(panelId);
    if (options && options.notes) notesOf.set(panelId, String(options.notes)); else notesOf.delete(panelId);
    paint(panelId);
}

/** The lines of every helper that shares a slot, as ONE small info badge (its pop-over lists them, with their buttons). */
function paintBadge(slotId) {
    const slot = document.getElementById(slotId);
    if (!slot) return;
    const lines = [];
    badgeOf.forEach((id, panelId) => {
        if (id !== slotId || assistHidden(panelId)) return;
        try { const provider = providers.get(panelId); (provider ? provider() || [] : []).forEach((line) => { if (line && line.text) lines.push(line); }); }
        catch (error) { console.warn("A helper could not work out what to say: ", error); }
    });
    slot.replaceChildren();
    refreshInfoWindow(slotId, lines);
    if (lines.length === 0) return;
    const badge = infoBadge({ label: lines.length === 1 ? "A note about this page" : lines.length + " notes about this page", lines, window: true, title: "Notes about this page", key: slotId });
    badge.dataset.tone = ["bad", "warn", "info"].find((tone) => lines.some((line) => line.tone === tone)) || "";
    if (lines.length > 1) badge.dataset.count = String(lines.length);
    slot.appendChild(badge);
}

/**
 * The lines of every helper that shares a notes panel, as ONE list, most important first: Urgent (red), Check (amber),
 * Next (blue), Good (green), Tip (grey). Each row can carry the button of its line. The panel folds like any other panel and
 * says how many lines of each level it holds while it is folded. Nothing is replaced until a helper has something to say.
 */
const LEVELS = { bad: [0, "Urgent"], warn: [1, "Check"], info: [2, "Next"], ok: [3, "Good"], "": [4, "Tip"] };
function paintNotes(slotId) {
    const slot = document.getElementById(slotId);
    if (!slot) return;
    const lines = [];
    notesOf.forEach((id, panelId) => {
        if (id !== slotId || assistHidden(panelId)) return;
        try { const provider = providers.get(panelId); (provider ? provider() || [] : []).forEach((line) => { if (line && line.text) lines.push(line); }); }
        catch (error) { console.warn("A helper could not work out what to say: ", error); }
    });
    if (lines.length === 0) return;
    const level = (line) => (LEVELS[line.tone || ""] || LEVELS[""]);
    lines.sort((a, b) => level(a)[0] - level(b)[0]);
    slot.replaceChildren();
    lines.forEach((line) => {
        const row = document.createElement("div");
        row.className = "note-row"; row.dataset.tone = line.tone || "";
        const badge = document.createElement("span"); badge.className = "note-level"; badge.textContent = level(line)[1];
        const text = document.createElement("span"); text.className = "note-text"; text.textContent = line.text;
        row.append(badge, text);
        if (line.label && line.run) {
            const act = document.createElement("button"); act.type = "button"; act.className = "btn small note-act"; act.textContent = line.label;
            if (line.tip) act.dataset.tip = line.tip;
            act.addEventListener("click", () => line.run());
            row.appendChild(act);
        }
        slot.appendChild(row);
    });
    const count = (tone) => lines.filter((line) => (line.tone || "") === tone).length;
    const parts = [["bad", "urgent"], ["warn", "to check"], ["info", "next"]].map(([tone, word]) => [count(tone), word]).filter(([n]) => n > 0).map(([n, word]) => n + " " + word);
    const panel = slot.closest(".panel");
    if (panel) setPanelSummary(panel.dataset.panel, parts.length ? parts.join(" · ") : "All good");
}

/** Draws the helper of one panel again (or of every panel when no id is given). */
export function refreshAssist(panelId) {
    if (panelId) paint(panelId);
    else providers.forEach((provider, id) => paint(id));
}

export const hasAssist = (panelId) => providers.has(panelId);
export const assistHidden = (panelId) => readOff().has(nameOf(panelId));
export const anyAssistHidden = () => Array.from(providers.keys()).some(assistHidden);

export function setAssistHidden(panelId, hidden) {
    const off = readOff();
    if (hidden) off.add(nameOf(panelId)); else off.delete(nameOf(panelId));
    writeOff(off);
    paint(panelId);
}

/** Every hidden helper of this page comes back. */
export function showAllAssists() {
    const off = readOff();
    providers.forEach((provider, id) => off.delete(nameOf(id)));
    writeOff(off);
    refreshAssist();
}

function build(panelId) {
    const strip = document.createElement("div");
    strip.className = "assist";
    strip.setAttribute("role", "status");
    strip.innerHTML = `<span class="assist-main"><span class="assist-icon">${icon("sparkle")}</span><span class="assist-text"></span></span>
        <span class="assist-controls"><button type="button" class="row-btn assist-act"></button>
        <span class="assist-pager"><button type="button" class="assist-next" aria-label="Next hint"><span class="assist-step"></span>${icon("right")}</button>
        <button type="button" class="assist-x" aria-label="Hide this helper">${icon("x")}</button></span></span>`;
    strip.querySelector(".assist-act").addEventListener("click", () => { const line = strip.line; if (line && line.run) line.run(); });
    strip.querySelector(".assist-next").addEventListener("click", () => { shown.set(panelId, (shown.get(panelId) || 0) + 1); paint(panelId, true); });
    strip.querySelector(".assist-x").addEventListener("click", () => {
        setAssistHidden(panelId, true);
        toast("Helper hidden. The panel's ... menu brings it back.", "info", { label: "Undo", run: () => setAssistHidden(panelId, false) });
    });
    return strip;
}

function paint(panelId, stepped) {
    if (notesOf.has(panelId)) {
        paintNotes(notesOf.get(panelId));
        const own = document.querySelector(`.board .panel[data-panel="${CSS.escape(panelId)}"] > .panel-inner > .assist`);
        if (own) own.remove();
        return;
    }
    if (badgeOf.has(panelId)) {
        paintBadge(badgeOf.get(panelId));
        const own = document.querySelector(`.board .panel[data-panel="${CSS.escape(panelId)}"] > .panel-inner > .assist`);
        if (own) own.remove();
        return;
    }
    const panel = document.querySelector(`.board .panel[data-panel="${CSS.escape(panelId)}"]`);
    if (!panel) return;
    const inner = panel.querySelector(":scope > .panel-inner") || panel;
    let strip = inner.querySelector(":scope > .assist");
    const head = inner.querySelector(":scope > .panel-head");
    const place = (node) => { if (head) head.insertAdjacentElement("afterend", node); else inner.appendChild(node); };
    let lines = [], raw = [];
    try { const provider = providers.get(panelId); raw = provider ? provider() : []; lines = (raw || []).filter((line) => line && line.text); }
    catch (error) { console.warn("A helper could not work out what to say: ", error); }
    // A provider answers null while its panel's data is still on the way (7 November 2026): the line will come, so a loading
    // shape of the same size holds its place and the panel does not jump when the words arrive. Any other answer ends it.
    let waiting = inner.querySelector(":scope > .assist-sk");
    if (raw === null && !assistHidden(panelId)) {
        if (!strip && !waiting) { waiting = document.createElement("div"); waiting.className = "sk assist-sk"; waiting.setAttribute("aria-hidden", "true"); place(waiting); }
        return;
    }
    if (waiting) waiting.remove();
    if (lines.length === 0 || assistHidden(panelId)) { if (strip) strip.remove(); return; }

    if (!strip) {
        strip = build(panelId);
        place(strip);
    }
    const index = (shown.get(panelId) || 0) % lines.length;
    shown.set(panelId, index);
    const line = lines[index];
    strip.line = line;
    strip.dataset.tone = line.tone || "";

    const text = strip.querySelector(".assist-text");
    if (text.textContent !== line.text) {
        text.textContent = line.text;
        if (stepped) { text.classList.remove("swap"); void text.offsetWidth; text.classList.add("swap"); }
    }
    const act = strip.querySelector(".assist-act");
    act.hidden = !(line.label && line.run);
    if (line.label && act.textContent !== line.label) act.textContent = line.label;
    if (line.tip) act.dataset.tip = line.tip; else delete act.dataset.tip;
    const next = strip.querySelector(".assist-next");
    next.hidden = lines.length < 2;
    strip.querySelector(".assist-step").textContent = `${index + 1}/${lines.length}`;
}
