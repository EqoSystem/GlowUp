// Source checks of Quick settings (the gear in the header, the plain logo, the count only inside the open panel).
// No browser and no network: this test reads the shared files as text and cannot run the DOM. The browser checklist covers the rest.
//   Run: node tests/quickSettings.test.mjs
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const read = (file) => readFileSync(path.join(root, file), "utf8");
let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };

const menu = read("_Shared/adminSettingsMenu.js");
const shell = read("_Shared/adminShell.js");
const frame = read("_Shared/pageFrame.js");
const css = read("_Shared/adminSettingsMenu.css") + read("_Shared/adminCommon.css");

// ---- every page mounts the shared shell, which adds the gear
const pages = {
    Dashboard: "_Admin_Dashboard/adminDashboard.js",
    "Brand & Product Inventory": "_Admin_Brand_Inventory/adminBrandInventory.js",
    "Ingredient Curator": "_Admin_Ingredient_Curator/adminIngredientCurator.js",
    "Recycle Bin": "_Admin_Recycle_Bin/adminRecycleBin.js",
    Archive: "_Admin_Archive/adminArchive.js"
};
for (const [name, file] of Object.entries(pages)) check(/mountShell\("/.test(read(file)), `${name} mounts the shared shell (so it gets the gear)`);
check(/mountSettingsMenu\(tools\)/.test(shell), "mountShell adds the gear to the header's tools");
check(/aria-label", "Quick settings"/.test(menu), 'the gear has aria-label "Quick settings"');
check(/aria-haspopup", "dialog"/.test(menu), "the gear has aria-haspopup=dialog");
check(/aria-expanded/.test(menu) && /setAttribute\("aria-expanded", "true"\)/.test(menu) && /setAttribute\("aria-expanded", "false"\)/.test(menu), "aria-expanded is kept in sync");
check(/data-tip", "Quick settings"/.test(menu), 'the tooltip text is "Quick settings"');
check(/icon\("gear"\)/.test(menu) && /\n\s+gear: '/.test(shell), "the gear is an inline SVG icon");
check(/class="icon-btn quick-gear"|className = "icon-btn quick-gear"/.test(menu), "the gear uses the header's icon-btn style (36 px)");
check(/aria-label", "Quick settings"/.test(menu) && /<h3 id="swTitle">Quick settings<\/h3>/.test(menu), 'the panel is titled "Quick settings"');

// ---- the logo is a plain badge
check(!/brandMenuBtn|brand-badge|brandBinBadge|brand-menu/.test(frame + menu + shell + css), "no logo button, no logo badge left anywhere");
check(!/<button[^>]*brand/.test(frame), "the logo is not a button");
check(/alt=""/.test(frame), "the logo image keeps its alt text");

// ---- the count only inside the open panel
check(!/badge/i.test(menu.split("export function mountSettingsMenu")[1]), "the gear gets no badge");
check((menu.match(/binCount\(\)/g) || []).length === 2, "binCount() is called in one place only (renderMenu), and defined once");
check(/function renderMenu[\s\S]*binCount\(\)/.test(menu), "the count is drawn by renderMenu, which draws the open panel");
check(!/aria-label[^\n]*\$\{|data-count|data-bin|title[^\n]*binCount/.test(menu), "no aria-label, data attribute or title carries the count");
check(!/binCount|BIN_LISTS|keptBinnedCount/.test(frame), "the page frame knows nothing of the count");
check(/function openWindow\(\)[\s\S]*if \(!win\) build\(\)/.test(menu), "the panel is built on the first open");
check(!/win = document\.createElement/.test(menu.split("export function mountSettingsMenu")[1]), "mountSettingsMenu builds no panel");

// ---- no new Firestore reads
check(!/getDocs|onSnapshot|getCountFromServer|getDoc\b|firebase-firestore|collection\(|query\(/.test(menu), "adminSettingsMenu.js has no Firestore call or import at all");

// ---- Esc, outside click, narrow sheet
check(/event\.key !== "Escape"/.test(menu) && /pointerdown/.test(menu), "Esc and a click outside close the panel");
check(/max-width: 599px/.test(css), "under 600 px the panel is a full-width sheet");
check(/grid-template-columns: var\(--sidebar-w\) minmax\(0, 1fr\) var\(--qs-col, 0px\)/.test(css) && /html\.qs-open \{ --qs-col: var\(--settings-w\)/.test(css), "beside the page the panel is a grid column: the page gets narrower, nothing is covered");
check(/@media \(max-width: 900px\)[\s\S]*position: fixed[\s\S]*translateX\(100%\)/.test(css), "on a window of 900 px or less the panel slides over the page");
check(/\.sw-body \{[^}]*overflow: auto/.test(css), "the panel scrolls by itself");
check(/classList\.add\("qs-open"\)/.test(menu) && /classList\.remove\("qs-open"\)/.test(menu), "opening and closing set and clear qs-open (the page width follows)");

console.log(`\nquickSettings: ${checks} checks made`);
console.log(failed === 0 ? "ALL PASSED" : `${failed} FAILED`);
process.exit(failed === 0 ? 0 : 1);
