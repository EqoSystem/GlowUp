// Browser check (12 November 2026): the "..." menu of every panel that shows records or numbers offers "Show as".
// The panels that are forms or instructions (notes, tools, import, AI drafts) have nothing to show as a list or a graph and are listed, not counted.
//   Run: node tests/responsive/showAs.scenario.mjs
import { launch, openPage } from "./harness.mjs";
let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };
const NOT_DATA = new Set(["notes", "tools", "import", "ai-drafts"]);
const browser = await launch();
const lacking = [];
try {
    for (const key of ["dash", "inv", "cur", "arc", "bin"]) {
        const { page, errors, settle } = await openPage(browser, key, { width: 1440, height: 900 });
        await settle(1200);
        const ids = await page.evaluate(() => Array.from(document.querySelectorAll(".board > .panel[data-panel]")).filter((p) => p.querySelector(".panel-menu-btn")).map((p) => p.dataset.panel));
        for (const id of ids) {
            const has = await page.evaluate((id) => {
                const panel = document.querySelector(`.board > .panel[data-panel="${id}"]`);
                const btn = panel.querySelector(".panel-menu-btn");
                btn.click();
                const menu = document.querySelector(".menu");
                const found = !!menu && Array.from(menu.querySelectorAll(".menu-heading")).some((h) => /show as/i.test(h.textContent));
                document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
                document.querySelectorAll(".menu").forEach((m) => m.remove());
                return found;
            }, id);
            if (NOT_DATA.has(id)) { if (!has) lacking.push(`${key}/${id}`); continue; }
            ok(has, `${key}: the "${id}" panel's menu has "Show as"`);
        }
        ok(errors.length === 0, `${key}: no page errors ${errors.join("; ")}`);
        await page.context().close();
    }
} finally { await browser.close(); }
if (lacking.length) console.log("Panels without Show as (forms or instructions, nothing to show as a list): " + lacking.join(", "));
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
