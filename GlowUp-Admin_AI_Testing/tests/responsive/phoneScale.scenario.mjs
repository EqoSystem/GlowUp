// Phone scale (18 Nov 2026, the owner's Redmi Note 14 4G, about 393 CSS px wide): on a phone every component scales with the screen
// width (--u in adminCommon.css), not only the gaps. Checks, on every page:
//  - desktop (1440): --u is 1px and text is 14px (nothing changed);
//  - phone 393: --u is about 1.16px, body text and the Add / search controls are larger by the same ratio;
//  - phone 320: --u stays 1px (no shrinking below the design size); phone 600: --u is capped at 1.3px;
//  - no page scrolls sideways at 320 / 393 / 480.
// Run: node tests/responsive/phoneScale.scenario.mjs
import { launch, openPage, PAGES } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (cond, what) => { checks++; if (!cond) { failed++; console.log("FAILED: " + what); } };
const near = (a, b, tol) => Math.abs(a - b) <= tol;

const probe = () => {
    // --u is an unregistered property, so read it through a box that is 1000 units wide
    const gauge = document.createElement("div"); gauge.style.cssText = "position:absolute;visibility:hidden;width:calc(1000 * var(--u));height:1px";
    document.body.appendChild(gauge); const u = gauge.getBoundingClientRect().width / 1000; gauge.remove();
    const body = parseFloat(getComputedStyle(document.body).fontSize);
    const btn = [...document.querySelectorAll(".btn, button.btn")].find((b) => b.offsetParent);
    const btnH = btn ? btn.getBoundingClientRect().height : 0;
    return { u, body, btnH, overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth };
};

const browser = await launch();
try {
    for (const key of Object.keys(PAGES).filter((k) => k !== "login")) {
        const base = {};
        for (const [width, touch] of [[1440, false], [393, true], [320, true], [480, true], [600, true]]) {
            const { page, ctx, settle } = await openPage(browser, key, { theme: "dark", width, height: 800, touch });
            await settle(300);
            const r = await page.evaluate(probe);
            if (width === 1440) { base.body = r.body; base.btnH = r.btnH; ok(near(r.u, 1, 0.001), `${key} desktop: --u is 1px (${r.u})`); ok(near(r.body, 14, 0.6), `${key} desktop: text ${r.body}px`); }
            if (width === 393) {
                const expect = 393 / 340;
                ok(near(r.u, expect, 0.01), `${key} 393px: --u is ${expect.toFixed(3)}px (${r.u})`);
                ok(r.body <= 0 || near(r.body, 13 * expect, 0.8), `${key} 393px: text ${r.body}px, about ${(13 * expect).toFixed(1)}px`);
                if (base.btnH && r.btnH) ok(r.btnH >= base.btnH * 1.05 || r.btnH >= 44, `${key} 393px: a button is ${r.btnH.toFixed(1)}px high (desktop ${base.btnH.toFixed(1)}px)`);
            }
            if (width === 320) ok(near(r.u, 1, 0.001), `${key} 320px: --u stays 1px (${r.u})`);
            if (width === 600) ok(near(r.u, 1.3, 0.001), `${key} 600px: --u is capped at 1.3px (${r.u})`);
            if (width !== 1440) ok(r.overflow <= 1, `${key} ${width}px: the page does not scroll sideways (${r.overflow}px too wide)`);
            await ctx.close();
        }
    }
} finally { await browser.close(); }
console.log(failed ? `${failed} FAILED of ${checks}` : `ALL PASSED  (checks made: ${checks})`);
process.exit(failed ? 1 : 0);
