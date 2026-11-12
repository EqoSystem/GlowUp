








import { icon } from "./adminShell.js";
import { isFolded, cleanChoice } from "./layoutRules.js";

const KEY = "glowup.fold.v1";
let pageName = "";
const quiet = new Map();
const summaries = new Map();

const nameOf = (panelId) => pageName + "/" + panelId;
function readChoices() {
    try { const saved = JSON.parse(localStorage.getItem(KEY) || "{}"); return saved && typeof saved === "object" && !Array.isArray(saved) ? saved : {}; }
    catch (error) { return {}; }
}
function writeChoices(choices) {
    try { localStorage.setItem(KEY, JSON.stringify(choices)); } catch (error) {                                                               }
}
const choiceOf = (panelId) => cleanChoice(readChoices()[nameOf(panelId)]);


export function setFoldPage(page) { pageName = String(page || ""); }


export const FOLD_PARTS = `<span class="panel-summary"></span>`;
export const foldButton = () => `<button type="button" class="icon-btn small panel-fold-btn" aria-label="Fold this panel" aria-expanded="true">${icon("down")}</button>`;

const panelOf = (panelId) => document.querySelector(`.board .panel[data-panel="${CSS.escape(panelId)}"]`);


export const foldedNow = (panelId) => isFolded(choiceOf(panelId), quiet.get(panelId) === true);


export function paintFold(panelId) {
    if (!panelId) { document.querySelectorAll(".board .panel[data-panel]").forEach((panel) => paintFold(panel.dataset.panel)); return; }
    const panel = panelOf(panelId);
    if (!panel) return;
    const button = panel.querySelector(":scope > .panel-inner > .panel-head .panel-fold-btn");

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


export function toggleFold(panelId) {
    const choices = readChoices();
    choices[nameOf(panelId)] = foldedNow(panelId) ? "open" : "closed";
    writeChoices(choices);
    paintFold(panelId);
}





export function setPanelQuiet(panelId, isQuiet, summary) {
    quiet.set(panelId, !!isQuiet);
    if (summary !== undefined) summaries.set(panelId, String(summary || ""));
    paintFold(panelId);
}


export function setPanelSummary(panelId, summary) {
    summaries.set(panelId, String(summary || ""));
    paintFold(panelId);
}


export function anyFoldChoice() {
    const choices = readChoices();
    return Object.keys(choices).some((key) => key.startsWith(pageName + "/") && cleanChoice(choices[key]));
}


export function forgetFoldChoices() {
    const choices = readChoices();
    Object.keys(choices).forEach((key) => { if (key.startsWith(pageName + "/")) delete choices[key]; });
    writeChoices(choices);
    paintFold();
}
