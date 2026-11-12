import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const sharedCss = path.join(root, "_Shared", "adminCommon.css");
const tokens = ["rail-w", "nav-w", "icon-box", "icon-center-x", "label-start-x", "gutter", "header-h"];
const tokenDeclaration = new RegExp(`--(?:${tokens.join("|")})\\s*:`, "i");
const failures = [];
const htmlFiles = [];

function walk(directory) {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
        if (entry.isDirectory()) {
            if (![".git", ".vercel", "node_modules", "tests"].includes(entry.name)) walk(path.join(directory, entry.name));
        } else if (entry.isFile() && entry.name.toLowerCase().endsWith(".html")) {
            htmlFiles.push(path.join(directory, entry.name));
        }
    }
}

function stylesheetLinks(html) {
    return Array.from(html.matchAll(/<link\b[^>]*>/gi), ([tag]) => {
        const rel = tag.match(/\brel\s*=\s*["']([^"']*)["']/i);
        const href = tag.match(/\bhref\s*=\s*["']([^"']*)["']/i);
        return rel && /\bstylesheet\b/i.test(rel[1]) && href ? href[1] : null;
    }).filter(Boolean);
}

function cssOverride(file, text, htmlPath) {
    if (path.resolve(file) === sharedCss) return;
    const match = tokenDeclaration.exec(text);
    tokenDeclaration.lastIndex = 0;
    if (match) failures.push(`${path.relative(root, htmlPath)}: layout token override in ${path.relative(root, file)} (${match[0].trim()})`);
}

function inspectCss(file, htmlPath, seen) {
    const absolute = path.resolve(file);
    if (!absolute.startsWith(root + path.sep) || seen.has(absolute) || !fs.existsSync(absolute)) return;
    seen.add(absolute);
    const text = fs.readFileSync(absolute, "utf8");
    cssOverride(absolute, text, htmlPath);
    for (const match of text.matchAll(/@import\s+(?:url\()?["']([^"']+)["']/gi)) {
        if (/^(?:[a-z]+:)?\/\//i.test(match[1])) continue;
        inspectCss(path.resolve(path.dirname(absolute), match[1]), htmlPath, seen);
    }
}

walk(root);
if (htmlFiles.length === 0) failures.push("No admin HTML files were found.");

for (const file of htmlFiles) {
    const html = fs.readFileSync(file, "utf8");
    const relative = path.relative(root, file);
    const links = stylesheetLinks(html);
    const commonLink = links.find((href) => {
        if (/^(?:[a-z]+:)?\/\//i.test(href)) return false;
        return path.resolve(path.dirname(file), href) === sharedCss;
    });
    if (!commonLink) failures.push(`${relative}: missing _Shared/adminCommon.css`);

    const seen = new Set();
    for (const href of links) {
        if (/^(?:[a-z]+:)?\/\//i.test(href)) continue;
        inspectCss(path.resolve(path.dirname(file), href), file, seen);
    }
    for (const style of html.matchAll(/<style\b[^>]*>([\s\S]*?)<\/style\s*>/gi)) cssOverride(file, style[1], file);
    for (const inline of html.matchAll(/\bstyle\s*=\s*(["'])(.*?)\1/gi)) cssOverride(file, inline[2], file);
    if (commonLink) console.log(`OK  ${relative}`);
}

const sharedText = fs.readFileSync(sharedCss, "utf8");
for (const token of tokens) {
    const declarations = Array.from(sharedText.matchAll(new RegExp(`--${token}\\s*:`, "g")));
    if (declarations.length !== 1) failures.push(`_Shared/adminCommon.css: expected one declaration of --${token}, found ${declarations.length}`);
}

if (failures.length) {
    console.error(failures.map((failure) => `FAIL ${failure}`).join("\n"));
    process.exitCode = 1;
} else {
    console.log(`PASS ${htmlFiles.length} admin HTML files include the shared stylesheet; no per-page layout token overrides found.`);
}
