// Folding panels: a panel can be folded to its title line, so a page does not show everything at once.
//
//   - every panel with a title has a small arrow at its top right: it folds the panel, or opens it again;
//   - a page can say that a panel has nothing to show right now:  setPanelQuiet("suggestions", true, "Nothing waiting")
//     such a panel folds BY ITSELF and shows that short line beside its title; it opens by itself when data arrives;
//   - what the admin chose with the arrow always wins, and is remembered in this browser, per page and panel;
//   - a page can give a panel a short line for when it is folded:  setPanelSummary("activity", "64 scans, 13 harmful")
//
// The text is always written with textContent, never as HTML.
import { icon } from "./adminShell.js";
import { isFolded, cleanChoice } from "./layoutRules.js";

const KEY = "glowup.fold.v1";
let pageName = "";
const quiet = new Map();              // panel id -> the page said it has nothing to show
const summaries = new Map();          // panel id -> the short line beside the title while the panel is folded

const nameOf = (panelId) => pageName + "/" + panelId;
function readChoices() {
    try { const saved = JSON.parse(localStorage.getItem(KEY) || "{}"); return saved && typeof saved === "object" && !Array.isArray(saved) ? saved : {}; }
    catch (error) { return {}; }
}
function writeChoices(choices) {
    try { localStorage.setItem(KEY, JSON.stringify(choices)); } catch (error) { /* private window: the choice holds until the page is left */ }
}
const choiceOf = (panelId) => cleanChoice(readChoices()[nameOf(panelId)]);

/** Call once per page, before the panels are painted: a choice is remembered per page. */
export function setFoldPage(page) { pageName = String(page || ""); }

/** The arrow and the summary line that every panel with a title gets (adminLayouts.js puts them into the title bar). */
export const FOLD_PARTS = `<span class="panel-summary"></span>`;
export const foldButton = () => `<button type="button" class="icon-btn small panel-fold-btn" aria-label="Fold this panel" aria-expanded="true">${icon("down")}</button>`;

const panelOf = (panelId) => document.querySelector(`.board .panel[data-panel="${CSS.escape(panelId)}"]`);

/** True when the panel shows only its title line. */
export const foldedNow = (panelId) => isFolded(choiceOf(panelId), quiet.get(panelId) === true);

/** Puts the folded / open look on one panel (or on every panel when no id is given). */
export function paintFold(panelId) {
    if (!panelId) { document.querySelectorAll(".board .panel[data-panel]").forEach((panel) => paintFold(panel.dataset.panel)); return; }
    const panel = panelOf(panelId);
    if (!panel) return;
    const button = panel.querySelector(":scope > .panel-inner > .panel-head > .panel-fold-btn");
    // a panel without a title bar, and a panel inside a combined panel, never folds
    const can = !!button && !panel.classList.contains("member");
    const folded = can && foldedNow(panelId);
    panel.classList.toggle("folded", folded);
    panel.classList.toggle("quiet", can && quiet.get(panelId) === true);
    if (!button) return;
    button.setAttribute("aria-expanded", String(!folded));
    button.setAttribute("aria-label", folded ? "Open this panel" : "Fold this panel");
    button.dataset.tip = folded ? "Open this panel." : "Fold this panel to its title. It stays folded until you open it again.";
    const line = panel.querySelector(":scope > .panel-inner > .panel-head > .panel-summary");
    if (line) line.textContent = summaries.get(panelId) || "";
}

/** The arrow was pressed: the panel does the opposite of what it shows now, and that is remembered. */
export function toggleFold(panelId) {
    const choices = readChoices();
    choices[nameOf(panelId)] = foldedNow(panelId) ? "open" : "closed";
    writeChoices(choices);
    paintFold(panelId);
}

/**
 * A page says whether a panel has nothing to show right now. `summary` = the short line shown beside the title while
 * the panel is folded (for example "Nothing waiting"); leave it out to keep the line it has.
 */
export function setPanelQuiet(panelId, isQuiet, summary) {
    quiet.set(panelId, !!isQuiet);
    if (summary !== undefined) summaries.set(panelId, String(summary || ""));
    paintFold(panelId);
}

/** The short line beside the title while the panel is folded. */
export function setPanelSummary(panelId, summary) {
    summaries.set(panelId, String(summary || ""));
    paintFold(panelId);
}

/** True when the admin folded or opened a panel of this page by hand. */
export function anyFoldChoice() {
    const choices = readChoices();
    return Object.keys(choices).some((key) => key.startsWith(pageName + "/") && cleanChoice(choices[key]));
}

/** Forgets what the admin chose on this page: every panel goes back to "open when it has something to show". */
export function forgetFoldChoices() {
    const choices = readChoices();
    Object.keys(choices).forEach((key) => { if (key.startsWith(pageName + "/")) delete choices[key]; });
    writeChoices(choices);
    paintFold();
}
