// New21 guideline checks on the rendered pages (both themes):
//  - colour guideline 9: text reaches 4.5:1 (3:1 for large text) against the colour actually behind it;
//  - responsive guideline 9: on a touch screen every control is at least 44 x 44 CSS px (its own box or its hit area).
// Run: node tests/responsive/guidelines.scenario.mjs          (exit 1 on any fault)
import { launch, openPage, PAGES } from "./harness.mjs";
import { probe } from "./guidelinesProbe.mjs";

const browser = await launch();
let faults = 0;
try {
    for (const theme of ["light", "dark"]) for (const key of Object.keys(PAGES)) {
        for (const [w, h, touch] of [[1440, 900, false], [390, 844, true]]) {
            const { page, ctx } = await openPage(browser, key, { theme, width: w, height: h, touch });
            const out = await page.evaluate(probe);
            const contrast = out.low, targets = touch ? out.small : [];
            // bigger touch controls must not make anything overlap or spill
            const a = touch ? await page.evaluate(() => window.__audit()) : {};
            const layout = touch ? [...a.hscroll, ...a.panelOverlap, ...a.textOverlap, ...a.spill, ...a.offscreen] : [];
            if (contrast.length || targets.length || layout.length) {
                faults += contrast.length + targets.length + layout.length;
                console.log(`--- ${key} ${theme} ${w}px${touch ? " touch" : ""}`);
                contrast.forEach((x) => console.log("  contrast  " + x));
                targets.forEach((x) => console.log("  target    " + x));
                layout.forEach((x) => console.log("  layout    " + x));
            }
            await ctx.close();
        }
    }
} finally { await browser.close(); }
console.log(faults === 0 ? "ALL PASSED" : `${faults} faults`);
process.exit(faults === 0 ? 0 : 1);
