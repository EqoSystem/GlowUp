// Phone layout (17 Nov 2026, the owner's phone screenshots). Three faults this guards:
// 1. A layout made on a computer (half- and third-width panels) stayed two columns of ~150 px on a phone: the compact-board
//    rule "html:not(.qs-open) .board > .panel" (span 6) beat the phone rule (full width) on specificity.
// 2. The header squeezed the search under the name and scrolled Add and the settings button out of sight sideways.
// 3. "Sign out?" sat at the left above the two buttons.
// Every page, dark theme, a touch phone at 320 / 360 / 412 px, with a desktop-style layout forced on the panels.
// Run: node tests/responsive/phoneLayout.scenario.mjs
import { launch, openPage, check, PAGES } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (cond, what) => { checks++; if (!cond) { failed++; console.log("FAILED: " + what); } };

const browser = await launch();
try {
    for (const width of [320, 360, 412]) {
        for (const key of Object.keys(PAGES).filter((k) => k !== "login")) {
            const { page, ctx, settle } = await openPage(browser, key, { theme: "dark", width, height: 780, touch: true });
            await page.evaluate(() => { let i = 0; document.querySelectorAll(".board > .panel").forEach((p) => p.style.setProperty("--w", [6, 6, 4, 8][i++ % 4])); });
            await settle(400);
            const r = await page.evaluate(() => {
                const board = document.querySelector(".board"), controls = document.querySelector(".topbar-controls"), topbar = document.querySelector(".main-content > .topbar");
                const inner = board ? board.clientWidth - parseFloat(getComputedStyle(board).paddingLeft) - parseFloat(getComputedStyle(board).paddingRight) : 0;
                const narrow = [...document.querySelectorAll(".board > .panel")].filter((p) => p.offsetParent && p.getBoundingClientRect().width < inner - 2).length;
                const hidden = controls ? [...controls.querySelectorAll("button, input, a")].filter((e) => e.offsetParent).filter((e) => { const b = e.getBoundingClientRect(), c = controls.getBoundingClientRect(); return b.right > c.right + 1 || b.left < c.left - 1 || b.bottom > topbar.getBoundingClientRect().bottom + 1; }).map((e) => e.id || e.className).slice(0, 3) : [];
                const bg = topbar ? getComputedStyle(topbar).backgroundColor : "";
                return { narrow, overflow: controls ? controls.scrollWidth - controls.clientWidth : 0, hidden, solid: !/rgba\([^)]*,\s*0?\.\d+\)/.test(bg) };
            });
            ok(r.narrow === 0, `${key} ${width}px: every panel takes the whole width (${r.narrow} narrower)`);
            ok(r.overflow <= 1 && r.hidden.length === 0, `${key} ${width}px: every header control is in view (overflow ${r.overflow}, outside: ${r.hidden.join(", ")})`);
            ok(r.solid, `${key} ${width}px: the header background is solid, so the page does not show through it`);
            const faults = await check(page);
            ok(faults.length === 0, `${key} ${width}px: no overlap or spill (${faults.join(" | ")})`);
            if (key === "cur" && width === 360) {
                await page.evaluate(() => document.querySelector(".sign-out").click());
                await settle(400);
                const d = await page.evaluate(() => { const box = document.querySelector(".confirm-inline"), h = box && box.querySelector("h3"); if (!h) return null; const a = box.getBoundingClientRect(), t = h.getBoundingClientRect(); return { off: Math.abs((a.left + a.width / 2) - (t.left + t.width / 2)), align: getComputedStyle(h).textAlign }; });
                ok(d && d.off <= 2 && d.align === "center", `Sign out?: the title is centred over the buttons (${JSON.stringify(d)})`);
            }
            await ctx.close();
        }
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
