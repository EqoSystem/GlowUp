// One-off converter (18 Nov 2026): every length in px in the site's CSS becomes `calc(N * var(--u))`.
// --u is 1px on a desktop and grows with the phone's width (see :root in _Shared/adminCommon.css), so on a phone every
// component (text, buttons, panels, icons, paddings) scales together, not only the gaps. Idempotent.
// Left alone: 0, 1px and smaller (hairlines), @media / @container / @supports conditions, url(...), strings, comments.
// Run: node tools/scalePx.mjs
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const files = [
    "_Shared/adminCommon.css", "_Shared/adminSettingsMenu.css", "_Admin_Archive/adminArchive.css",
    "_Admin_Brand_Inventory/adminBrandInventory.css", "_Admin_Dashboard/adminDashboard.css",
    "_Admin_Ingredient_Curator/adminIngredientCurator.css", "_Admin_Recycle_Bin/adminRecycleBin.css", "_Index/indexStyle.css"
];

export function convert(css) {
    let out = "", i = 0;
    const n = css.length;
    while (i < n) {
        const c = css[i];
        if (c === "/" && css[i + 1] === "*") { const e = css.indexOf("*/", i + 2); const end = e < 0 ? n : e + 2; out += css.slice(i, end); i = end; continue; }
        if (c === '"' || c === "'") { let j = i + 1; while (j < n && css[j] !== c) { if (css[j] === "\\") j++; j++; } out += css.slice(i, j + 1); i = j + 1; continue; }
        if (c === "@") {
            const m = /^@(media|container|supports|import|layer|property|charset|namespace)\b/.exec(css.slice(i, i + 20));
            if (m && m[1] !== "layer" && m[1] !== "property") {
                // a condition: copy up to the "{" (or ";") untouched
                let j = i; while (j < n && css[j] !== "{" && css[j] !== ";") j++;
                out += css.slice(i, j); i = j; continue;
            }
        }
        if (css.startsWith("url(", i)) { const e = css.indexOf(")", i); out += css.slice(i, e + 1); i = e + 1; continue; }
        const m = /^(-?)(\d*\.?\d+)px(?![\w-])/.exec(css.slice(i, i + 24));
        if (m && !/[\w.-]/.test(i > 0 ? css[i - 1] : " ")) {
            const value = parseFloat(m[2]);
            if (value > 1) {
                const inside = /calc\(\s*-?[\d.]+\s*\*\s*var\(--u\)\s*\)$/.test(out) ; // already converted
                out += `calc(${m[1]}${m[2]} * var(--u))`;
                i += m[0].length; void inside; continue;
            }
        }
        out += c; i++;
    }
    return out;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
    for (const f of files) {
        const p = path.join(root, f);
        const before = fs.readFileSync(p, "utf8");
        const after = convert(before);
        if (after !== before) fs.writeFileSync(p, after);
        console.log(f, (before.match(/\d+(\.\d+)?px/g) || []).length, "px ->", (after.match(/var\(--u\)/g) || []).length, "scaled");
    }
}
