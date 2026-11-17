


import { chromium } from "playwright-core";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { makeSeed } from "./seed.mjs";

const HERE = path.dirname(fileURLToPath(import.meta.url));
export const SITE = process.env.SITE || path.resolve(HERE, "../..");
export const BASE = "http://glowup.test/";
export const PAGES = {
    login: "_Index/index.html", dash: "_Admin_Dashboard/adminDashboard.html", inv: "_Admin_Brand_Inventory/adminBrandInventory.html",
    cur: "_Admin_Ingredient_Curator/adminIngredientCurator.html", bin: "_Admin_Recycle_Bin/adminRecycleBin.html", arc: "_Admin_Archive/adminArchive.html"
};
const TYPES = { ".html": "text/html", ".js": "text/javascript", ".mjs": "text/javascript", ".css": "text/css", ".png": "image/png", ".jpg": "image/jpeg", ".webp": "image/webp", ".svg": "image/svg+xml", ".json": "application/json", ".mp3": "audio/mpeg", ".wav": "audio/wav", ".ico": "image/x-icon", ".woff2": "font/woff2" };
const auditSrc = fs.readFileSync(path.join(HERE, "audit.js"), "utf8");


export async function launch() {
    const executablePath = process.env.CHROME || "C:/Program Files/Google/Chrome/Application/chrome.exe";
    return chromium.launch({ executablePath, headless: true });
}


export async function openPage(browser, key, { theme = "light", width = 1440, height = 900, lat = { auth: 0, read: 0 }, go = true, touch = false, pageHeaders = null } = {}) {
    // pageHeaders: extra response headers for the HTML pages (the CSP test serves the real vercel.json policy).
    // touch: a phone context (pointer: coarse), so touch-only CSS such as the 44 px targets applies.
    const ctx = await browser.newContext({ viewport: { width, height }, colorScheme: theme || "dark", ...(touch ? { hasTouch: true, isMobile: true } : {}) });
    await ctx.addInitScript(({ seed, signedIn, theme, lat }) => {
        window.__SEED = seed; window.__LAT = lat; window.__SIGNED_IN = signedIn;
        if (theme) { try { localStorage.setItem("glowup.theme", theme); } catch (e) { } }
    }, { seed: makeSeed(), signedIn: key !== "login", theme, lat });
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
            return route.fulfill({ status: 200, contentType: TYPES[path.extname(file)] || "application/octet-stream", body: fs.readFileSync(file), headers: { "cache-control": "no-store", ...(pageHeaders && path.extname(file) === ".html" ? pageHeaders : {}) } });
        }
        return route.abort();
    });
    const page = await ctx.newPage();
    const errors = [];
    page.on("pageerror", (e) => errors.push(String(e.message || e).slice(0, 200)));
    page.on("console", (m) => { if (m.type() === "error") errors.push("console: " + m.text().slice(0, 200)); });

    const settle = async (ms = 300) => {
        await page.waitForTimeout(ms);
        await page.evaluate(() => { document.getAnimations().forEach((a) => { try { a.finish(); } catch (e) { } }); });
        await page.waitForTimeout(80);
    };
    if (!go) return { page, ctx, errors, settle, url: BASE + PAGES[key] };
    await page.goto(BASE + PAGES[key], { waitUntil: "load" });
    await page.waitForTimeout(900);
    await settle(400);
    return { page, ctx, errors, settle };
}






export async function check(page) {
    return page.evaluate(() => {
        const faults = [];
        const vw = document.documentElement.clientWidth;
        if (document.documentElement.scrollWidth > vw) faults.push(`document scrollWidth ${document.documentElement.scrollWidth} > ${vw}`);
        const adminPage = !!document.querySelector("main.main-content");
        const menuButton = document.querySelector(".topbar #menuBtn"), nav = document.getElementById("sidebarNav");
        if (adminPage && (!menuButton || menuButton.getAttribute("aria-controls") !== "sidebarNav" || !nav)) faults.push("header menu button is not connected to the navigation");
        if (document.querySelector("#sidebarToggle, #sbMenuBtn")) faults.push("legacy sidebar control is still present");
        const a = window.__audit();
        for (const [kind, list] of Object.entries(a)) list.forEach((item) => faults.push(kind + ": " + item));

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


export async function setQuickSettings(page, open) {
    await page.evaluate((open) => {
        const gear = document.querySelector(".quick-gear");
        if (gear && (gear.getAttribute("aria-expanded") === "true") !== open) gear.click();
    }, open);
}




export async function setSidebar(page, state) {
    await page.evaluate((state) => {
        const root = document.documentElement;
        const mobile = innerWidth <= 768 || matchMedia("(hover: none)").matches;
        const open = mobile ? root.classList.contains("sb-mobile-open") : root.classList.contains("sb-pinned");
        if (open === (state === "expanded")) return;
        const button = document.querySelector(".topbar #menuBtn");
        if (button) button.click();
    }, state);
}
