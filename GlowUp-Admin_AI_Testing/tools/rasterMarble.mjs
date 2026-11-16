// Turns the login marble (_Index/marble-light.svg, marble-dark.svg, drawn by tools/makeMarble.mjs) into JPEG pictures.
// Why: the SVG is drawn with noise filters (feTurbulence, feDisplacementMap). A browser works those out again every time the
// window changes size, so dragging the edge of the login page stuttered. A JPEG is decoded once and only scaled.
// Run after makeMarble.mjs:  node tools/rasterMarble.mjs   (uses playwright-core from tests/responsive: npm install there once)
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "../tests/responsive/node_modules/playwright-core/index.mjs";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const DIR = path.resolve(HERE, "../_Index");
const W = 2400, H = 1500;                        // covers a 2560 x 1440 screen at its "cover" scale without visible softness
const browser = await chromium.launch({ executablePath: process.env.CHROME || "C:/Program Files/Google/Chrome/Application/chrome.exe", headless: true });
try {
    const page = await browser.newPage({ viewport: { width: W, height: H } });
    for (const theme of ["light", "dark"]) {
        const svg = fs.readFileSync(path.join(DIR, `marble-${theme}.svg`), "utf8").replace(/width="\d+" height="\d+"/, `width="${W}" height="${H}"`);
        await page.setContent(`<!doctype html><html><body style="margin:0">${svg}</body></html>`);
        await page.waitForTimeout(300);
        const out = path.join(DIR, `marble-${theme}.jpg`);
        await page.screenshot({ path: out, type: "jpeg", quality: 84, clip: { x: 0, y: 0, width: W, height: H } });
        console.log(path.basename(out), Math.round(fs.statSync(out).size / 1024) + " KB");
    }
} finally {
    await browser.close();
}
