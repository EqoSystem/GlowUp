// Local web server for the admin website: `npm run dev` from the repository root.
// Plain Node, nothing to install. It serves the files the way Vercel does:
//   /  -> /_Landing/index.html (same redirect as vercel.json)
//   /api, /tests, /tools are not served (same as nginx.conf); the Vercel functions only run on Vercel.
// Change the port: in PowerShell  $env:PORT=4000; npm run dev   (Git Bash: PORT=4000 npm run dev)

import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const SITE = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const PORT = Number(process.env.PORT) || 3000;
const TYPES = {
    ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".mjs": "text/javascript; charset=utf-8",
    ".css": "text/css; charset=utf-8", ".json": "application/json; charset=utf-8", ".png": "image/png", ".jpg": "image/jpeg",
    ".jpeg": "image/jpeg", ".webp": "image/webp", ".svg": "image/svg+xml", ".ico": "image/x-icon", ".woff2": "font/woff2",
    ".mp3": "audio/mpeg", ".wav": "audio/wav", ".txt": "text/plain; charset=utf-8", ".wasm": "application/wasm"
};

const server = http.createServer((req, res) => {
    const url = new URL(req.url, "http://localhost");
    let rel = decodeURIComponent(url.pathname);

    if (rel === "/") { res.writeHead(302, { Location: "/_Landing/index.html" }); return res.end(); }
    if (/^\/(api|tests|tools|node_modules)(\/|$)/.test(rel) || rel.split("/").some(p => p.startsWith(".") && p.length > 1)) {
        res.writeHead(404); return res.end("Not found");
    }

    let file = path.join(SITE, rel);
    if (!file.startsWith(SITE)) { res.writeHead(403); return res.end("Forbidden"); }   // blocks ../ tricks
    if (fs.existsSync(file) && fs.statSync(file).isDirectory()) {
        if (!rel.endsWith("/")) { res.writeHead(302, { Location: rel + "/" + url.search }); return res.end(); }
        file = path.join(file, "index.html");
    }

    fs.readFile(file, (err, data) => {
        if (err) { res.writeHead(404, { "Content-Type": "text/plain" }); return res.end("Not found: " + rel); }
        res.writeHead(200, { "Content-Type": TYPES[path.extname(file).toLowerCase()] || "application/octet-stream", "Cache-Control": "no-cache" });
        res.end(data);
    });
});

server.on("error", e => {
    console.error(e.code === "EADDRINUSE" ? `Port ${PORT} is busy. In PowerShell try: $env:PORT=4000; npm run dev` : e.message);
    process.exit(1);
});
server.listen(PORT, () => {
    console.log(`GlowUp website running:`);
    console.log(`  Landing page  http://localhost:${PORT}/`);
    console.log(`  Admin login   http://localhost:${PORT}/_Index/`);
    console.log(`Press Ctrl+C to stop.`);
});
