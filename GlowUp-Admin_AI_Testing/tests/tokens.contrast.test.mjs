// New21 colour guideline, section 9 (WCAG 2.2 AA): text and status colours must reach 4.5:1 on every surface
// they can sit on, in both themes. This reads the real tokens from _Shared/adminCommon.css, so a later change
// to a shade that breaks contrast fails here instead of on someone's screen.
import { readFileSync } from "node:fs";
import { ratio } from "../scripts/contrast-check.mjs";

let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };

const css = readFileSync(new URL("../_Shared/adminCommon.css", import.meta.url), "utf8");
const block = (selectorStart) => {
    const at = css.indexOf(selectorStart);
    if (at < 0) return null;
    const body = css.slice(css.indexOf("{", at) + 1, css.indexOf("\n}", at));
    const out = {};
    for (const m of body.matchAll(/--([a-z0-9-]+):\s*(#[0-9a-fA-F]{6})\b/g)) out[m[1]] = m[2].toLowerCase();
    return out;
};
const themes = { dark: block(':root, :root[data-theme="dark"]'), light: block(':root[data-theme="light"]') };

const SURFACES = ["bg", "surface", "surface-2", "surface-3"];
const TEXT = ["text", "text-2", "muted", "accent-text", "gold", "ok", "warn", "bad", "info"];
for (const [name, t] of Object.entries(themes)) {
    check(t && SURFACES.every((s) => t[s]), `${name}: every surface token is found`);
    if (!t) continue;
    for (const fg of TEXT) for (const bg of SURFACES) {
        if (!t[fg]) { check(false, `${name}: --${fg} is defined`); continue; }
        const r = ratio(t[fg], t[bg]);
        check(r >= 4.5, `${name}: --${fg} ${t[fg]} on --${bg} ${t[bg]} is ${r.toFixed(2)}:1 (needs 4.5)`);
    }
    check(ratio(t["accent-ink"], t.accent) >= 4.5, `${name}: button text on the pink fill reaches 4.5:1`);
    check(ratio(t.accent, t.surface) >= 3, `${name}: the pink focus ring / control edge reaches 3:1 on cards`);
    check(t["bad-ink"] && ratio(t["bad-ink"], t.bad) >= 4.5, `${name}: text on a red fill (--bad-ink) reaches 4.5:1`);
}
// Fixed white text on a pink or red fill fails in dark mode (3.3:1 and 2.8:1): such text must use the ink tokens.
for (const file of ["../_Shared/adminCommon.css", "../_Shared/adminSettingsMenu.css"]) {
    const src = readFileSync(new URL(file, import.meta.url), "utf8");
    const bad = src.split(/\r?\n/).filter((l) => /color:\s*#fff(fff)?\b/i.test(l) && /var\(--(accent|bad)\)/.test(l));
    check(bad.length === 0, `${file}: no white text on a pink/red fill (${bad.map((l) => l.trim().slice(0, 60)).join(" | ")})`);
}
// Section 7: dark mode is charcoal, not pure black.
check(themes.dark && themes.dark.bg !== "#000000" && themes.dark.surface !== "#000000", "dark: no pure black background or card");

console.log(`checks made: ${checks}`);
console.log(failed === 0 ? "ALL PASSED" : `${failed} FAILED`);
process.exit(failed === 0 ? 0 : 1);
