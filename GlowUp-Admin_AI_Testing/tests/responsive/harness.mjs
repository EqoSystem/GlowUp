// The browser rig behind tests/responsive.spec.js: a headless Chrome that serves this folder as http://glowup.test/, answers
// Firebase with the pretend modules in ./mocks (signed in as a test admin, data from seed.mjs) and blocks every other
// request. Nothing reaches the live Firebase project and no real credentials are used.
import { chromium } from "playwright-core";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { makeSeed } from "./seed.mjs";

const HERE = path.dirname(fileURLToPath(import.meta.url));
export const SITE = process.env.SITE || path.resolve(HERE, "../..");   // SITE=... measures another copy (an older commit)
export const BASE = "http://glowup.test/";
export const PAGES = {
    login: "_Index/index.html", dash: "_Admin_Dashboard/adminDashboard.html", inv: "_Admin_Brand_Inventory/adminBrandInventory.html",
    cur: "_Admin_Ingredient_Curator/adminIngredientCurator.html", bin: "_Admin_Recycle_Bin/adminRecycleBin.html", arc: "_Admin_Archive/adminArchive.html"
};
const TYPES = { ".html": "text/html", ".js": "text/javascript", ".mjs": "text/javascript", ".css": "text/css", ".png": "image/png", ".jpg": "image/jpeg", ".webp": "image/webp", ".svg": "image/svg+xml", ".json": "application/json", ".mp3": "audio/mpeg", ".wav": "audio/wav", ".ico": "image/x-icon", ".woff2": "font/woff2" };
const auditSrc = fs.readFileSync(path.join(HERE, "audit.js"), "utf8");

/** Chrome of this computer (playwright-core brings no browser). Set CHROME to use another one. */
export async function launch() {
    const executablePath = process.env.CHROME || "C:/Program Files/Google/Chrome/Application/chrome.exe";
    return chromium.launch({ executablePath, headless: true });
}

/** A fresh tab on one page of the site, in one theme, at one size. Returns { page, ctx, errors, settle }. */
export async function openPage(browser, key, { theme = "light", width = 1440, height = 900 } = {}) {
    const ctx = await browser.newContext({ viewport: { width, height }, colorScheme: theme });
    await ctx.addInitScript(({ seed, signedIn, theme }) => {
        window.__SEED = seed; window.__LAT = { auth: 0, read: 0 }; window.__SIGNED_IN = signedIn;
        try { localStorage.setItem("glowup.theme", theme); } catch (e) { }
    }, { seed: makeSeed(), signedIn: key !== "login", theme });
    await ctx.addInitScript(auditSrc);
    await ctx.route("**/*", async (route) => {
        const url = new URL(route.request().url());
        if (url.host === "www.gstatic.com" && url.pathname.includes("/firebasejs/")) {
            const file = path.join(HERE, "mocks", path.basename(url.pathname));
            return route.fulfill({ status: 200, contentType: "text/javascript", body: fs.readFileSync(file, "utf8"), headers: { "access-control-allow-origin": "*" } });
        }
        if (url.host === "glowup.test") {
            if (url.pathname.startsWith("/api/")) return route.fulfill({ status: 404, body: "{}" });
            const file = path.join(SITE, decodeURIComponent(url.pathname));
            if (!fs.existsSync(file) || fs.statSync(file).isDirectory()) return route.fulfill({ status: 404, body: "not found" });
            return route.fulfill({ status: 200, contentType: TYPES[path.extname(file)] || "application/octet-stream", body: fs.readFileSync(file), headers: { "cache-control": "no-store" } });
        }
        return route.abort();
    });
    const page = await ctx.newPage();
    const errors = [];
    page.on("pageerror", (e) => errors.push(String(e.message || e).slice(0, 200)));
    page.on("console", (m) => { if (m.type() === "error") errors.push("console: " + m.text().slice(0, 200)); });
    /** Waits until entrance animations and data loading are over (animations are finished at once, not waited for). */
    const settle = async (ms = 300) => {
        await page.waitForTimeout(ms);
        await page.evaluate(() => { document.getAnimations().forEach((a) => { try { a.finish(); } catch (e) { } }); });
        await page.waitForTimeout(80);
    };
    await page.goto(BASE + PAGES[key], { waitUntil: "load" });
    await page.waitForTimeout(900);
    await settle(400);
    return { page, ctx, errors, settle };
}

/**
 * Everything the brief asks about one picture: no sideways scroll, nothing past the right edge, no text or badge on top of
 * another in the same card, nothing cut off, and at 1024 px and wider Quick settings beside the content, not over it.
 * Returns a list of faults (empty when the page is right).
 */
export async function check(page) {
    return page.evaluate(() => {
        const faults = [];
        const vw = document.documentElement.clientWidth;
        if (document.documentElement.scrollWidth > vw) faults.push(`document scrollWidth ${document.documentElement.scrollWidth} > ${vw}`);
        const a = window.__audit();
        for (const [kind, list] of Object.entries(a)) list.forEach((item) => faults.push(kind + ": " + item));
        // every element of the page content: its right edge inside the window (or inside a box that scrolls on purpose)
        const scrolls = (el) => { for (let n = el.parentElement; n && n !== document.body; n = n.parentElement) { const o = getComputedStyle(n).overflowX; if (o === "auto" || o === "scroll" || o === "hidden" || o === "clip") return true; } return false; };
        document.querySelectorAll(".main-content *, .login-main *, .topbar *").forEach((el) => {
            const r = el.getBoundingClientRect();
            if (r.width < 1 || r.height < 1 || r.right <= vw + 1) return;
            const s = getComputedStyle(el);
            if (s.visibility === "hidden" || s.display === "none" || el.closest("[aria-hidden=true], .tip-pop, .toast, .settings-window, .modal-overlay, dialog")) return;
            if (scrolls(el)) return;
            faults.push(`past the right edge: ${el.tagName.toLowerCase()}.${String(el.className).split(" ")[0]} right ${Math.round(r.right)} > ${vw}`);
        });
        const win = document.getElementById("settingsWindow"), board = document.querySelector(".board");
        if (win && board && document.documentElement.classList.contains("qs-open") && innerWidth >= 1024) {
            const w = win.getBoundingClientRect(), b = board.getBoundingClientRect();
            if (w.left < b.right - 2) faults.push(`Quick settings covers the content: panel left ${Math.round(w.left)} < board right ${Math.round(b.right)}`);
        }
        return Array.from(new Set(faults)).slice(0, 12);
    });
}

/** Opens or closes Quick settings with its own gear button. */
export async function setQuickSettings(page, open) {
    await page.evaluate((open) => {
        const gear = document.querySelector(".quick-gear");
        if (gear && (gear.getAttribute("aria-expanded") === "true") !== open) gear.click();
    }, open);
}

/**
 * The menu at the left: "expanded" or "collapsed". From 1024 px the edge tab folds it to icons (kept); under 1024 px it is
 * icons only and the tab (or the menu button on a phone) opens the whole menu over the page.
 */
export async function setSidebar(page, state) {
    await page.evaluate((state) => {
        const root = document.documentElement, wide = innerWidth >= 1024;
        const open = wide ? !root.classList.contains("sb-collapsed") : root.classList.contains("sb-peek");
        if (open === (state === "expanded")) return;
        const button = innerWidth < 768 ? document.querySelector(".topbar > .menu-btn") : document.getElementById("sidebarToggle");
        if (button) button.click();
    }, state);
}
