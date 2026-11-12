













import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { launch, openPage, check, setQuickSettings, setSidebar, PAGES } from "./responsive/harness.mjs";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const SHOT_DIR = path.join(HERE, "screenshots");
const SHOTS = process.env.SHOTS !== "0";
const parts = process.argv.slice(2).filter((a) => ["matrix", "live", "perf"].includes(a));
const run = (part) => parts.length === 0 || parts.includes(part);
const SIZES = [[1920, 1080], [1440, 900], [1280, 800], [1024, 768], [960, 1040], [768, 1024], [600, 900], [480, 850], [375, 812], [320, 640], [960, 520]];
const results = { pass: 0, fail: 0, failures: [] };
const record = (name, faults) => {
    if (faults.length === 0) { results.pass++; return; }
    results.fail++;
    results.failures.push(name + "\n      " + faults.join("\n      "));
};

const browser = await launch();
try {
    if (run("matrix")) {
        if (SHOTS) fs.mkdirSync(SHOT_DIR, { recursive: true });
        for (const theme of ["light", "dark"]) for (const key of Object.keys(PAGES)) {
            const { page, ctx, errors, settle } = await openPage(browser, key, { theme, width: SIZES[0][0], height: SIZES[0][1] });
            for (const [w, h] of SIZES) {
                await page.setViewportSize({ width: w, height: h });
                await settle(250);
                const states = key === "login" ? [["closed", "expanded"]] : [["closed", "expanded"], ["open", "expanded"], ["closed", "collapsed"], ["open", "collapsed"]];
                for (const [qs, sb] of states) {
                    if (key !== "login") {
                        await setQuickSettings(page, false); await settle(120);
                        await setSidebar(page, sb); await settle(120);
                        await setQuickSettings(page, qs === "open"); await settle(200);
                    }
                    const name = `${key} ${theme} ${w}x${h} qs-${qs} menu-${sb}`;
                    record(name, await check(page));
                    if (SHOTS) await page.screenshot({ path: path.join(SHOT_DIR, `${key}_${theme}_${w}x${h}_qs-${qs}_menu-${sb}.png`) });
                }
                if (key !== "login") { await setQuickSettings(page, false); await setSidebar(page, "expanded"); await settle(120); }
            }
            if (errors.length) record(`${key} ${theme} page errors`, Array.from(new Set(errors)));
            await ctx.close();
        }
        console.log(`matrix: ${results.pass} passed, ${results.fail} failed`);
    }

    if (run("live")) {
        const before = { pass: results.pass, fail: results.fail };
        for (const key of Object.keys(PAGES)) {
            const { page, ctx } = await openPage(browser, key, { theme: "dark", width: 1440, height: 900 });
            for (let w = 1440; w >= 320; w -= 10) {
                await page.setViewportSize({ width: w, height: 900 });
                await page.evaluate(() => new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r))));
                record(`live ${key} ${w}px`, await check(page));
            }
            await ctx.close();
        }
        console.log(`live resize: ${results.pass - before.pass} passed, ${results.fail - before.fail} failed`);
    }

    if (run("perf")) {



        const exercise = async (page) => {
            const phase = (name) => page.evaluate((name) => window.__perf.phases.push({ name, t: performance.now() }), name);
            await phase("the window drag");
            const t0 = Date.now();
            for (let w = 1440; w >= 400; w -= 10) {
                await page.setViewportSize({ width: w, height: 900 });
                await page.evaluate(() => new Promise((r) => requestAnimationFrame(r)));
            }
            for (let w = 400; w <= 1440; w += 10) {
                await page.setViewportSize({ width: w, height: 900 });
                await page.evaluate(() => new Promise((r) => requestAnimationFrame(r)));
            }
            const dragMs = Date.now() - t0;
            for (let i = 0; i < 2; i++) {
                await phase("Quick settings opening"); await setQuickSettings(page, true); await page.waitForTimeout(350);
                await phase("Quick settings closing"); await setQuickSettings(page, false); await page.waitForTimeout(350);
                await phase("menu folding"); await page.evaluate(() => document.getElementById("menuBtn").click()); await page.waitForTimeout(350);
                await phase("menu unfolding"); await page.evaluate(() => document.getElementById("menuBtn").click()); await page.waitForTimeout(350);
            }
            return dragMs;
        };
        const watch = (page) => page.evaluate(() => {
            window.__perf = { frames: [], long: [], phases: [] };
            new PerformanceObserver((list) => list.getEntries().forEach((e) => window.__perf.long.push({ d: Math.round(e.duration), t: e.startTime }))).observe({ type: "longtask", buffered: false });
            const tick = (t) => { window.__perf.frames.push(t); window.__perfRaf = requestAnimationFrame(tick); };
            window.__perfRaf = requestAnimationFrame(tick);
        });


        let tab = await openPage(browser, "dash", { theme: "light", width: 1440, height: 900 });
        await watch(tab.page);
        const dragMs = await exercise(tab.page);
        const perf = await tab.page.evaluate(() => { cancelAnimationFrame(window.__perfRaf); return window.__perf; });
        await tab.ctx.close();
        const span = perf.frames[perf.frames.length - 1] - perf.frames[0];
        const fps = Math.round(((perf.frames.length - 1) / span) * 1000 * 10) / 10;
        let worstGap = 0, janky = 0;
        for (let i = 1; i < perf.frames.length; i++) { const gap = perf.frames[i] - perf.frames[i - 1]; worstGap = Math.max(worstGap, gap); if (gap > 33.4) janky++; }
        const phaseOf = (t) => (perf.phases.filter((p) => p.t <= t).pop() || { name: "page start" }).name;
        const longTasks = perf.long.filter((l) => l.d > 50).map((l) => l.d + " ms during " + phaseOf(l.t));


        tab = await openPage(browser, "dash", { theme: "light", width: 1440, height: 900 });
        await watch(tab.page);
        const tracePath = path.join(HERE, "screenshots", "resize-trace.json");
        fs.mkdirSync(path.dirname(tracePath), { recursive: true });
        await browser.startTracing(tab.page, { path: tracePath, categories: ["devtools.timeline", "disabled-by-default-devtools.timeline", "disabled-by-default-devtools.timeline.stack", "blink.user_timing"] });
        await exercise(tab.page);
        await browser.stopTracing();
        await tab.ctx.close();


        const trace = JSON.parse(fs.readFileSync(tracePath, "utf8"));
        const events = trace.traceEvents || trace;
        const handlers = events.filter((e) => e.ph === "X" && ((e.name === "EventDispatch" && e.args && e.args.data && e.args.data.type === "resize") || e.name === "ResizeObserverCallback"));
        const forced = events.filter((e) => e.name === "Layout" && e.args && e.args.beginData && e.args.beginData.stackTrace && e.args.beginData.stackTrace.length);
        const forcedInResize = forced.filter((l) => handlers.some((h) => h.tid === l.tid && l.ts >= h.ts && l.ts <= h.ts + h.dur));

        console.log(`perf: drag ${dragMs} ms, ${perf.frames.length} frames, average ${fps} fps, worst frame gap ${Math.round(worstGap)} ms, frames over 33 ms: ${janky} (headless Chrome is not held to 60 Hz, so the average can pass 60)`);
        console.log(`perf: long tasks over 50 ms: ${longTasks.length}${longTasks.length ? " (" + longTasks.join(", ") + ")" : ""}`);
        console.log(`perf (trace): layouts forced by script: ${forced.length}, of them in resize handlers: ${forcedInResize.length}`);
        const faults = [];
        if (fps < 55) faults.push(`average ${fps} fps (target 55)`);
        if (longTasks.length) faults.push(`${longTasks.length} long tasks over 50 ms: ${longTasks.join(", ")}`);
        if (forcedInResize.length) faults.push(`${forcedInResize.length} forced layouts in resize handlers: ` + forcedInResize.slice(0, 3).map((l) => (l.args.beginData.stackTrace[0] || {}).url + ":" + (l.args.beginData.stackTrace[0] || {}).lineNumber).join(", "));
        record("perf dashboard", faults);
    }
} finally {
    await browser.close();
}
console.log(`\nTOTAL: ${results.pass} passed, ${results.fail} failed`);
results.failures.slice(0, 40).forEach((f) => console.log("  FAIL " + f));
if (results.failures.length) { fs.mkdirSync(SHOT_DIR, { recursive: true }); fs.writeFileSync(path.join(SHOT_DIR, "failures.txt"), results.failures.join("\n")); console.log("  all failures: tests/screenshots/failures.txt"); }
process.exit(results.fail ? 1 : 0);
