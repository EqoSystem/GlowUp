// Sign out dialog: title only, Cancel (x icon) and Sign out (logout icon); fits at 360 px with no sideways scroll.
import { launch, openPage } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };
const shots = process.env.SHOTS || "";
const browser = await launch();
try {
    for (const [width, theme] of [[1440, "light"], [360, "light"], [1440, "dark"]]) {
        const { page, ctx } = await openPage(browser, "dash", { width, height: 800, theme });
        await page.waitForSelector(".sign-out", { state: "attached", timeout: 8000 });
        await page.evaluate(() => {
            const open = document.documentElement.classList;
            if (innerWidth < 768) open.add("sb-mobile-open");
            document.querySelector(".sign-out").click();
        });
        await page.waitForSelector(".modal-content.confirm-inline", { timeout: 4000 });
        const m = await page.evaluate(() => {
            const box = document.querySelector(".modal-content.confirm-inline");
            const b = box.getBoundingClientRect();
            const buttons = [...box.querySelectorAll(".modal-actions button")];
            return {
                badge: !!box.querySelector(".confirm-badge svg.ic"),
                line: box.querySelector(".modal-intro")?.textContent || "",
                labels: buttons.map((x) => x.textContent.trim()),
                icons: buttons.map((x) => !!x.querySelector("svg.ic")),
                heights: buttons.map((x) => x.getBoundingClientRect().height),
                inside: b.left >= 0 && b.right <= innerWidth,
                scroll: document.documentElement.scrollWidth > innerWidth,
                focused: document.activeElement?.textContent.trim()
            };
        });
        const where = `${width}px ${theme}`;
        ok(!m.badge, `${where}: no badge`);
        ok(m.line.length === 0, `${where}: no sub text`);
        ok(m.labels.join("|") === "Cancel|Sign out", `${where}: labels ${m.labels}`);
        ok(m.icons.every(Boolean), `${where}: both buttons have an icon`);
        ok(m.heights.every((h) => h >= 43), `${where}: 44px (page scale rounds to 43.5) buttons ${m.heights}`);
        ok(m.inside && !m.scroll, `${where}: fits the screen`);
        ok(m.focused === "Cancel", `${where}: Cancel has focus (danger)`);
        if (shots) await page.locator(".modal-content.confirm-inline").screenshot({ path: `${shots}/signout_${width}_${theme}.png` });
        await page.keyboard.press("Escape");
        ok(!(await page.$(".modal-content.confirm-inline")), `${where}: Escape closes`);
        await ctx.close();
    }
} finally { await browser.close(); }
console.log(`${checks - failed}/${checks} checks passed`);
process.exit(failed ? 1 : 0);
