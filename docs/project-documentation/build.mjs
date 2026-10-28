// Builds GlowUp_Project_Documentation.pdf from src/parts/*.html and diagrams/*.mmd with the installed Google Chrome.
//
//   npm install        (once: mermaid and pdfjs-dist, dev tools only, inside this folder)
//   node build.mjs     writes build/document.html and GlowUp_Project_Documentation.pdf
//   node check.mjs     renders every PDF page to build/pages/page-NN.png for a visual check
//
// Authoring shorthand inside the parts (replaced here):
//   {V:source}  [Verified]  seen in code/config/data, source = file path
//   {I:basis}   [Inferred]  strong conclusion from several files
//   {N:what}    [Not found] searched, not in the project (also listed in Appendix D)
//   <figure data-diagram="name"><figcaption>Caption</figcaption></figure>  inlines diagrams/name.mmd, numbered
//   <table data-caption="Caption">  numbered table caption
// Chrome is found at CHROME (environment) or the usual Windows install path.

import { readFileSync, writeFileSync, readdirSync, mkdirSync, existsSync, rmSync, mkdtempSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { spawn } from "node:child_process";
import { tmpdir } from "node:os";
import { getDocument } from "pdfjs-dist/legacy/build/pdf.mjs";

const here = dirname(fileURLToPath(import.meta.url));
const OUT_PDF = join(here, "GlowUp_Project_Documentation.pdf");
const BUILD = join(here, "build");
const CHROME = process.env.CHROME || "C:/Program Files/Google/Chrome/Application/chrome.exe";

const esc = (s) => String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");

// ---------------------------------------------------------------- assemble the HTML
function assemble(pageNumbers) {
    const parts = readdirSync(join(here, "src", "parts")).filter((f) => f.endsWith(".html")).sort();
    let body = parts.map((f) => readFileSync(join(here, "src", "parts", f), "utf8")).join("\n");

    const counts = { V: 0, I: 0, N: 0 };
    const notFound = [];
    const label = { V: "Verified", I: "Inferred", N: "Not found" };
    body = body.replace(/\{(V|I|N)(?::([^{}]*))?\}/g, (_, kind, src) => {
        counts[kind]++;
        if (kind === "N" && src) notFound.push(src.trim());
        const cite = src ? ` <span class="src">(${esc(src.trim())})</span>` : "";
        return `<span class="tag tag-${kind.toLowerCase()}">${label[kind]}</span>${cite}`;
    });

    let figure = 0;
    const figures = [];
    body = body.replace(/<figure data-diagram="([^"]+)"([^>]*)>\s*<figcaption>([\s\S]*?)<\/figcaption>\s*<\/figure>/g, (_, name, attrs, caption) => {
        figure++;
        const file = join(here, "diagrams", name + ".mmd");
        if (!existsSync(file)) throw new Error("Missing diagram " + file);
        figures.push({ n: figure, caption: caption.trim(), id: "fig-" + figure });
        return `<figure class="diagram" id="fig-${figure}"${attrs}><pre class="mermaid">${esc(readFileSync(file, "utf8"))}</pre>`
            + `<figcaption><b>Figure ${figure}.</b> ${caption.trim()} <span class="src">(source: diagrams/${name}.mmd)</span></figcaption></figure>`;
    });

    let table = 0;
    body = body.replace(/<table data-caption="([^"]+)"([^>]*)>/g, (_, caption, attrs) => {
        table++;
        return `<div class="tcap"><b>Table ${table}.</b> ${caption}</div><table${attrs}>`;
    });

    // headings: ids, page markers, contents
    const toc = [];
    let h = 0;
    body = body.replace(/<(h1|h2)([^>]*)>([\s\S]*?)<\/\1>/g, (_, tag, attrs, text) => {
        h++;
        const id = "h" + h;
        const plain = text.replace(/<span class="part">[\s\S]*?<\/span>/, "").replace(/<[^>]+>/g, "").trim();
        if (!/data-notoc/.test(attrs)) toc.push({ id, tag, plain });
        return `<${tag} id="${id}"${attrs}>${text}<span class="pm">@@${id}@@</span></${tag}>`;
    });

    const tocHtml = toc.map((t) => `<li class="toc-${t.tag}"><a href="#${t.id}"><span class="toc-text">${esc(t.plain)}</span>`
        + `<span class="toc-dots"></span><span class="toc-page">${pageNumbers?.[t.id] ?? "00"}</span></a></li>`).join("\n");
    const lofHtml = figures.map((f) => `<li class="toc-h2"><a href="#${f.id}"><span class="toc-text">Figure ${f.n}. ${f.caption.replace(/<[^>]+>/g, "")}</span>`
        + `<span class="toc-dots"></span><span class="toc-page">${pageNumbers?.["p-" + f.id] ?? "00"}</span></a></li>`).join("\n");
    body = body.replace("<!--TOC-->", `<ol class="toc">${tocHtml}</ol>`).replace("<!--LOF-->", `<ol class="toc lof">${lofHtml}</ol>`);
    // figure page markers
    body = body.replace(/<figcaption><b>Figure (\d+)\.<\/b>/g, (m, n) => `<figcaption><span class="pm">@@p-fig-${n}@@</span><b>Figure ${n}.</b>`);
    const uniqueNotFound = Array.from(new Set(notFound));
    body = body.replace("<!--NOTFOUND-->", `<ol class="checklist">${uniqueNotFound.map((n) => `<li>${esc(n)}</li>`).join("")}</ol>`);
    body = body.replace("<!--COUNTS-->",`<b>${counts.V}</b> Verified, <b>${counts.I}</b> Inferred, <b>${counts.N}</b> Not found`);

    const css = readFileSync(join(here, "src", "style.css"), "utf8");
    const mermaidUrl = pathToFileURL(join(here, "node_modules", "mermaid", "dist", "mermaid.min.js")).href;
    const html = `<!doctype html><html lang="en"><head><meta charset="utf-8"><title>GlowUp Project Documentation</title>
<style>${css}</style></head><body>${body}
<script src="${mermaidUrl}"></script>
<script>
mermaid.initialize({ startOnLoad: false, theme: "base", securityLevel: "loose",
  themeVariables: { fontFamily: "Segoe UI, Arial, sans-serif", fontSize: "15px", primaryColor: "#FDECF3", primaryBorderColor: "#C2185B",
    primaryTextColor: "#2B2B2B", lineColor: "#6B6B6B", secondaryColor: "#FFF6E0", tertiaryColor: "#F4F4F4", noteBkgColor: "#FFF6E0", noteBorderColor: "#B8860B" },
  flowchart: { htmlLabels: true, curve: "basis", useMaxWidth: true, wrappingWidth: 220 },
  sequence: { useMaxWidth: true, wrap: true, width: 130, messageFontSize: 12, actorFontSize: 12, noteFontSize: 12 },
  er: { useMaxWidth: true }, gantt: { useMaxWidth: true, barHeight: 18, fontSize: 11, leftPadding: 160 } });
mermaid.run({ querySelector: ".mermaid" }).then(() => { window.__ready = "ok"; }).catch((e) => { window.__ready = "error: " + e.message; });
</script></body></html>`;
    return { html, counts, notFound, figures: figure, tables: table };
}

// ---------------------------------------------------------------- Chrome through the DevTools protocol
async function withChrome(work) {
    const profile = mkdtempSync(join(tmpdir(), "glowup-doc-chrome-"));
    const chrome = spawn(CHROME, ["--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check",
        "--allow-file-access-from-files", "--remote-debugging-port=0", "--user-data-dir=" + profile, "about:blank"], { stdio: "ignore" });
    try {
        let port = null;
        for (let i = 0; i < 100 && !port; i++) {
            await new Promise((r) => setTimeout(r, 150));
            const file = join(profile, "DevToolsActivePort");
            try { if (existsSync(file)) port = readFileSync(file, "utf8").split("\n")[0].trim() || null; } catch { /* Chrome is still writing it */ }
        }
        if (!port) throw new Error("Chrome did not start: " + CHROME);
        const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: "PUT" })).json();
        const ws = new WebSocket(target.webSocketDebuggerUrl);
        await new Promise((ok, fail) => { ws.onopen = ok; ws.onerror = fail; });
        let next = 0;
        const waiting = new Map();
        ws.onmessage = (event) => {
            const msg = JSON.parse(event.data);
            if (msg.id && waiting.has(msg.id)) { const w = waiting.get(msg.id); waiting.delete(msg.id); msg.error ? w.fail(new Error(JSON.stringify(msg.error))) : w.ok(msg.result); }
        };
        const send = (method, params = {}) => new Promise((ok, fail) => { const id = ++next; waiting.set(id, { ok, fail }); ws.send(JSON.stringify({ id, method, params })); });
        const evaluate = async (expression) => (await send("Runtime.evaluate", { expression, returnByValue: true, awaitPromise: true })).result.value;
        await send("Page.enable");
        await send("Runtime.enable");
        const result = await work({ send, evaluate });
        ws.close();
        return result;
    } finally {
        chrome.kill();
        await new Promise((r) => setTimeout(r, 500));
        try { rmSync(profile, { recursive: true, force: true }); } catch { /* Chrome may still hold a file for a moment */ }
    }
}

export async function openAndWait(cdp, url, readyExpr = "window.__ready", seconds = 90) {
    await cdp.send("Page.navigate", { url });
    for (let i = 0; i < seconds * 4; i++) {
        await new Promise((r) => setTimeout(r, 250));
        const state = await cdp.evaluate(readyExpr);
        if (state) return state;
    }
    throw new Error("Page did not finish: " + url);
}

async function printPdf(htmlFile) {
    return withChrome(async (cdp) => {
        const state = await openAndWait(cdp, pathToFileURL(htmlFile).href);
        if (state !== "ok") throw new Error("Mermaid: " + state);
        const problems = await cdp.evaluate(`Array.from(document.querySelectorAll("figure.diagram")).filter(f => !f.querySelector("svg") || /Syntax error/i.test(f.textContent)).map(f => f.id)`);
        if (problems.length) throw new Error("Diagrams that did not render: " + problems.join(", "));
        const pdf = await cdp.send("Page.printToPDF", {
            printBackground: true, preferCSSPageSize: true, displayHeaderFooter: true, generateDocumentOutline: true, generateTaggedPDF: true,
            headerTemplate: `<span></span>`,
            footerTemplate: `<div style="font-size:7.5px;width:100%;padding:0 15mm;color:#888;font-family:Segoe UI,Arial;display:flex;justify-content:space-between"><span>GlowUp · Project Documentation · 24 October 2026</span><span>Page <span class="pageNumber"></span> of <span class="totalPages"></span></span></div>`
        });
        return Buffer.from(pdf.data, "base64");
    });
}

async function markerPages(pdfBytes) {
    const doc = await getDocument({ data: new Uint8Array(pdfBytes), verbosity: 0 }).promise;
    const pages = {};
    for (let p = 1; p <= doc.numPages; p++) {
        const text = (await (await doc.getPage(p)).getTextContent()).items.map((i) => i.str).join(" ");
        for (const m of text.matchAll(/@@\s*([a-z0-9-]+?)\s*@@/g)) if (!(m[1] in pages)) pages[m[1]] = p;
    }
    return { pages, total: doc.numPages };
}

// ---------------------------------------------------------------- main
mkdirSync(BUILD, { recursive: true });
const htmlFile = join(BUILD, "document.html");

let built = assemble(null);
writeFileSync(htmlFile, built.html);
let pdf = await printPdf(htmlFile);
let { pages, total } = await markerPages(pdf);
// second pass: the contents get their page numbers (the contents keep their length, so no page moves)
built = assemble(pages);
writeFileSync(htmlFile, built.html);
pdf = await printPdf(htmlFile);
const check = await markerPages(pdf);
const moved = Object.keys(pages).filter((k) => pages[k] !== check.pages[k]);
const finalPages = moved.length ? check.pages : pages;
// last pass: the same document without the hidden page markers (they would show up when text is copied or searched)
built = assemble(finalPages);
writeFileSync(htmlFile, built.html.replace(/<span class="pm">@@[a-z0-9-]+@@<\/span>/g, ""));
pdf = await printPdf(htmlFile);
writeFileSync(OUT_PDF, pdf);
writeFileSync(join(BUILD, "not-found.txt"), built.notFound.join("\n") + "\n");
console.log(`PDF: ${OUT_PDF}`);
console.log(`Pages: ${check.total}, figures: ${built.figures}, tables: ${built.tables}`);
console.log(`Tags: ${built.counts.V} Verified, ${built.counts.I} Inferred, ${built.counts.N} Not found`);
