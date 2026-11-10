// Tests every source link of the Ingredient form against the real sites (needs the internet).
//   Run from the website folder:   node scripts/verify-source-links.mjs
// For each source: the LANDING address must answer 200 on an allowed host. A SEARCH address (the site's own search, plain GET)
// is tested with 3 sample ingredients: final status 200, final host allowed, and the page text contains the ingredient name.
// A PAGE builder (PubChem) resolves each sample to a compound number, then the compound page must answer 200 and the compound's
// own synonym list must contain the name. Exit code 1 when anything that is shipped fails. Prints one table.
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";

const here = path.dirname(fileURLToPath(import.meta.url));
const f = await import("data:text/javascript;base64," + Buffer.from(readFileSync(path.join(here, "..", "_Shared", "fieldSources.js"))).toString("base64"));

const SAMPLES = ["Glycerin", "Niacinamide", "1-Methylhydantoin-2-Imide"];
const HEADERS = { "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/124 Safari/537.36", "Accept-Language": "en", Accept: "text/html,text/plain,*/*" };

async function get(url) {
    const response = await fetch(url, { redirect: "follow", headers: HEADERS, signal: AbortSignal.timeout(30000) });
    return { status: response.status, finalUrl: response.url, text: await response.text() };
}

const rows = [];
let failed = 0;
const note = (source, type, url, ok, detail) => { rows.push([source, type, url, ok ? "PASS" : "FAIL " + detail]); if (!ok) failed++; };

for (const [id, source] of Object.entries(f.SOURCES)) {
    // landing: always tested
    try {
        const r = await get(source.landing);
        note(source.label, "landing", source.landing, r.status === 200 && f.hostAllowed(r.finalUrl), `status ${r.status}, host ${new URL(r.finalUrl).hostname}`);
    } catch (error) { note(source.label, "landing", source.landing, false, String(error.message || error)); }

    if (source.kind === "search") {
        for (const name of SAMPLES) {
            const url = source.search(name);
            try {
                const r = await get(url);
                const found = r.text.toLowerCase().includes(name.toLowerCase());
                note(source.label, "search", url, r.status === 200 && f.hostAllowed(r.finalUrl) && found, `status ${r.status}, name in page: ${found}`);
            } catch (error) { note(source.label, "search", url, false, String(error.message || error)); }
        }
    }
    if (source.kind === "page") {
        for (const name of SAMPLES) {
            const lookup = f.pubchemLookupUrl(name);
            try {
                const answer = await get(lookup);
                const cid = f.firstCompoundId(answer.text);
                if (!cid) { note(source.label, "page", lookup, false, `no compound number (status ${answer.status})`); continue; }
                const page = f.pubchemPageUrl(cid);
                const shown = await get(page);
                const synonyms = await get(`https://pubchem.ncbi.nlm.nih.gov/rest/pug/compound/cid/${cid}/synonyms/TXT`);
                const named = synonyms.text.toLowerCase().includes(name.toLowerCase());
                note(source.label, "page", `${page} (from ${name})`, shown.status === 200 && f.hostAllowed(shown.finalUrl) && named, `page ${shown.status}, name in the compound's synonyms: ${named}`);
            } catch (error) { note(source.label, "page", lookup, false, String(error.message || error)); }
        }
    }
}

const widths = [0, 1, 2, 3].map((i) => Math.min(70, Math.max(...rows.map((r) => String(r[i]).length), 6)));
console.log(["source", "type shipped", "tested URL", "result"].map((h, i) => h.padEnd(widths[i])).join(" | "));
rows.forEach((r) => console.log(r.map((c, i) => String(c).slice(0, widths[i]).padEnd(widths[i])).join(" | ")));
console.log(failed === 0 ? "\nALL SHIPPED LINKS VERIFIED" : `\n${failed} CHECK(S) FAILED: remove or fix that link type in _Shared/fieldSources.js`);
process.exit(failed === 0 ? 0 : 1);
