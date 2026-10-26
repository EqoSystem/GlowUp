// Renders every page of GlowUp_AI_Data_Report.pdf to build/pages/page-NN.png (pdf.js inside headless Chrome),
// so the layout can be looked at: diagrams cut off, tables too wide, empty pages.
//   node check.mjs [scale]      default scale 1.4
import { readFileSync, writeFileSync, mkdirSync, existsSync, rmSync, mkdtempSync, readdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { spawn } from "node:child_process";
import { tmpdir } from "node:os";

const here = dirname(fileURLToPath(import.meta.url));
const CHROME = process.env.CHROME || "C:/Program Files/Google/Chrome/Application/chrome.exe";
const scale = Number(process.argv[2] || 1.4);
const pagesDir = join(here, "build", "pages");
rmSync(pagesDir, { recursive: true, force: true });
mkdirSync(pagesDir, { recursive: true });

const viewer = join(here, "build", "viewer.html");
const pdfjs = pathToFileURL(join(here, "node_modules", "pdfjs-dist", "build", "pdf.min.mjs")).href;
const worker = pathToFileURL(join(here, "node_modules", "pdfjs-dist", "build", "pdf.worker.min.mjs")).href;
const pdfData = readFileSync(join(here, "GlowUp_AI_Data_Report.pdf")).toString("base64");
writeFileSync(viewer, `<!doctype html><meta charset="utf-8"><body style="margin:0;background:#888">
<script type="module">
import * as pdfjsLib from "${pdfjs}";
pdfjsLib.GlobalWorkerOptions.workerSrc = "${worker}";
const bytes = Uint8Array.from(atob("${pdfData}"), c => c.charCodeAt(0));
const doc = await pdfjsLib.getDocument({ data: bytes }).promise;
window.__pages = [];
for (let p = 1; p <= doc.numPages; p++) {
  const page = await doc.getPage(p);
  const viewport = page.getViewport({ scale: ${scale} });
  const canvas = document.createElement("canvas");
  canvas.width = viewport.width; canvas.height = viewport.height;
  await page.render({ canvasContext: canvas.getContext("2d"), viewport }).promise;
  window.__pages.push(canvas.toDataURL("image/png"));
}
window.__ready = "ok";
</script>`);

const profile = mkdtempSync(join(tmpdir(), "glowup-check-chrome-"));
const chrome = spawn(CHROME, ["--headless=new", "--disable-gpu", "--no-first-run", "--allow-file-access-from-files",
    "--remote-debugging-port=0", "--user-data-dir=" + profile, "about:blank"], { stdio: "ignore" });
try {
    let port = null;
    for (let i = 0; i < 100 && !port; i++) {
        await new Promise((r) => setTimeout(r, 150));
        const f = join(profile, "DevToolsActivePort");
        try { if (existsSync(f)) port = readFileSync(f, "utf8").split("\n")[0].trim() || null; } catch { /* Chrome is still writing it */ }
    }
    const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: "PUT" })).json();
    const ws = new WebSocket(target.webSocketDebuggerUrl);
    await new Promise((ok, fail) => { ws.onopen = ok; ws.onerror = fail; });
    let next = 0; const waiting = new Map();
    ws.onmessage = (e) => { const m = JSON.parse(e.data); if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); } };
    const send = (method, params = {}) => new Promise((ok) => { const id = ++next; waiting.set(id, ok); ws.send(JSON.stringify({ id, method, params })); });
    const value = async (expression) => (await send("Runtime.evaluate", { expression, returnByValue: true, awaitPromise: true })).result?.result?.value;
    await send("Page.navigate", { url: pathToFileURL(viewer).href });
    let state = null;
    for (let i = 0; i < 600 && !state; i++) { await new Promise((r) => setTimeout(r, 250)); state = await value("window.__ready"); }
    const count = await value("window.__pages.length");
    for (let i = 0; i < count; i++) {
        const url = await value(`window.__pages[${i}]`);
        writeFileSync(join(pagesDir, `page-${String(i + 1).padStart(2, "0")}.png`), Buffer.from(url.split(",")[1], "base64"));
    }
    ws.close();
    console.log(`${count} pages written to ${pagesDir}`);
} finally {
    chrome.kill();
    await new Promise((r) => setTimeout(r, 500));
    try { rmSync(profile, { recursive: true, force: true }); } catch { }
    try { rmSync(viewer, { force: true }); } catch { }
}
