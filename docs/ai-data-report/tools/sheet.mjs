// Puts the page pictures of build/pages (made by check.mjs) on contact sheets, 12 pages each: build/sheet-N.png.
// A quick way to look at the whole PDF for cut-off diagrams, overflowing tables and empty pages.
//   node tools/sheet.mjs
import { readdirSync, writeFileSync, readFileSync, mkdtempSync, existsSync, rmSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { spawn } from "node:child_process";
import { tmpdir } from "node:os";

const here = join(dirname(fileURLToPath(import.meta.url)), "..");
const CHROME = process.env.CHROME || "C:/Program Files/Google/Chrome/Application/chrome.exe";
const pages = readdirSync(join(here, "build", "pages")).filter((f) => f.endsWith(".png")).sort();
const perSheet = 12;

const profile = mkdtempSync(join(tmpdir(), "glowup-sheet-chrome-"));
const chrome = spawn(CHROME, ["--headless=new", "--disable-gpu", "--no-first-run", "--allow-file-access-from-files",
    "--remote-debugging-port=0", "--user-data-dir=" + profile, "about:blank"], { stdio: "ignore" });
try {
    let port = null;
    for (let i = 0; i < 100 && !port; i++) {
        await new Promise((r) => setTimeout(r, 150));
        const f = join(profile, "DevToolsActivePort");
        try { if (existsSync(f)) port = readFileSync(f, "utf8").split("\n")[0].trim() || null; } catch { /* still writing */ }
    }
    const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: "PUT" })).json();
    const ws = new WebSocket(target.webSocketDebuggerUrl);
    await new Promise((ok, fail) => { ws.onopen = ok; ws.onerror = fail; });
    let next = 0; const waiting = new Map();
    ws.onmessage = (e) => { const m = JSON.parse(e.data); if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); } };
    const send = (method, params = {}) => new Promise((ok) => { const id = ++next; waiting.set(id, ok); ws.send(JSON.stringify({ id, method, params })); });
    await send("Emulation.setDeviceMetricsOverride", { width: 1800, height: 1700, deviceScaleFactor: 1, mobile: false });
    for (let s = 0; s * perSheet < pages.length; s++) {
        const some = pages.slice(s * perSheet, (s + 1) * perSheet);
        const html = `<!doctype html><body style="margin:0;background:#666;display:grid;grid-template-columns:repeat(6,1fr);gap:6px;padding:6px">`
            + some.map((p) => `<figure style="margin:0;background:#fff"><img src="${pathToFileURL(join(here, "build", "pages", p)).href}" style="width:100%;display:block"><figcaption style="font:12px sans-serif;text-align:center">${p}</figcaption></figure>`).join("")
            + `</body>`;
        const file = join(here, "build", `sheet-${s + 1}.html`);
        writeFileSync(file, html);
        await send("Page.navigate", { url: pathToFileURL(file).href });
        await new Promise((r) => setTimeout(r, 1500));
        const shot = await send("Page.captureScreenshot", { format: "png" });
        writeFileSync(join(here, "build", `sheet-${s + 1}.png`), Buffer.from(shot.result.data, "base64"));
        console.log("build/sheet-" + (s + 1) + ".png");
    }
    ws.close();
} finally {
    chrome.kill();
    await new Promise((r) => setTimeout(r, 400));
    try { rmSync(profile, { recursive: true, force: true }); } catch { /* Chrome may still hold a file */ }
}
