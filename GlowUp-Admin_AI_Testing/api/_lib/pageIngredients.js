// INGREDIENT LISTS FROM FETCHED PAGES ONLY (7 November 2026).
//
// The scan-snapshot function asks the AI WHICH product the photo shows (brand, name, kind, shade). It never takes an
// ingredient list from the AI. The list comes from here: product pages are fetched by this server and the printed
// "Ingredients:" block is cut out of the page's own text by plain rules. No list on a page = no list.
//
//   Pages tried: the pages the AI's web search opened, then a plain web search for "<brand> <product> ingredients".
//   Order: the brand's own website first ("official"), then known shops ("retailer"), then other sites ("other").
//   Safety: https only, public host names only (no IP addresses, no local names), 6 seconds and 1.5 MB per page, HTML only,
//   at most MAX_PAGES pages. A page's text is data, never an instruction: it is only cut and split here, never sent to an AI.
"use strict";

const https = require("https");
const dns = require("dns");
const net = require("net");
const MAX_HOPS = 3;

const MAX_PAGES = 6;
const PAGE_SECONDS = 6;
const SEARCH_SECONDS = 7;
const MAX_PAGE_BYTES = 1500000;

/** The page's text with one line per block (paragraph, list item, cell, heading, line break). */
function pageText(html) {
    let s = String(html || "");
    s = s.replace(/<(script|style|noscript|svg|template)\b[\s\S]*?<\/\1>/gi, " ");
    s = s.replace(/<br\s*\/?>/gi, "\n").replace(/<\/(p|li|div|h[1-6]|tr|td|th|dd|dt|section|article|ul|ol|table|span)>/gi, "\n");
    s = s.replace(/<[^>]*>/g, " ");
    s = s.replace(/&nbsp;/gi, " ").replace(/&amp;/gi, "&").replace(/&quot;/gi, '"').replace(/&#39;|&#x27;|&apos;/gi, "'")
        .replace(/&lt;/gi, "<").replace(/&gt;/gi, ">")
        .replace(/&#(\d+);/g, (_, n) => { try { return String.fromCodePoint(Number(n)); } catch (e) { return " "; } })
        .replace(/&#x([0-9a-f]+);/gi, (_, n) => { try { return String.fromCodePoint(parseInt(n, 16)); } catch (e) { return " "; } });
    return s.split(/\n/).map((line) => line.replace(/[ \t\r\f\v]+/g, " ").trim()).filter((line) => line.length > 0).join("\n");
}

// words that show a block really is an ingredient list (at least one item must hold one)
const ANCHORS = /\b(aqua|water|eau|glycerin|dimethicone|cyclopentasiloxane|mica|titanium dioxide|ci ?\d{5}|iron oxides?|silica|talc|tocopher|phenoxyethanol|paraffin|cera|wax|oil|butylene glycol|nylon|kaolin|isododecane|trimethylsiloxysilicate|polyethylene|caprylyl|ethylhexyl|stearate|alcohol)\b/i;
const ITEM = /^[\p{L}\p{N} ().,/+\-'&:%[\]*]+$/u;
const LABEL = /\b(full ingredients?|ingredients?(?: list)?|inci|ingrédients|ingredientes|composition)\b\s*[:\-–]?\s*/i;

/** One ingredient name, tidied: no "may contain" lead-in, no trailing full stop, no stars or footnote marks. */
function tidyItem(raw) {
    let s = String(raw || "").replace(/^[\s*•·\-]+/, "").replace(/[\s.*•·]+$/, "").trim();
    s = s.replace(/^\[?\s*(\+\/-|±)?\s*(may contain|peut contenir)\s*[:\-]?\s*/i, "").replace(/^\(?\+\/-\)?\s*/, "").replace(/[\]]$/, "").trim();
    return s;
}

/** Splits a block of text into names, or returns null when it does not look like an INCI list. */
function listFrom(block) {
    const parts = String(block || "").split(/\s*(?:,|;|•|·|\s\|\s)\s*/).map(tidyItem).filter((item) => item.length > 0);
    const items = [];
    for (const item of parts) {
        if (items.length >= 120) break;
        if (!items.some((known) => known.toLowerCase() === item.toLowerCase())) items.push(item);
    }
    if (items.length < 5) return null;
    const good = items.filter((item) => item.length >= 2 && item.length <= 80 && ITEM.test(item) && item.split(/\s+/).length <= 7);
    if (good.length / items.length < 0.8) return null;
    if (!good.some((item) => ANCHORS.test(item))) return null;
    const words = good.reduce((sum, item) => sum + item.split(/\s+/).length, 0) / good.length;
    if (words > 4.5) return null;                                       // prose, not a list
    return good;
}

/** The first block on the page that follows an "Ingredients" label and reads as an INCI list; null when there is none. */
function extractIngredientList(html) {
    const lines = pageText(html).split("\n");
    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        const label = LABEL.exec(line);
        if (!label) continue;
        // the list can follow the label on the same line, or start on the next line(s)
        const after = line.slice(label.index + label[0].length).trim();
        const candidates = [];
        if (after.length > 0) candidates.push(after);
        let joined = "";
        for (let j = i + 1; j < Math.min(lines.length, i + 4); j++) {
            joined = joined ? joined + ", " + lines[j] : lines[j];
            candidates.push(lines[j], joined);
        }
        for (const block of candidates) {
            if (block.length > 4000) continue;
            const list = listFrom(block);
            if (list) return list;
        }
    }
    return null;
}

/** "www.shop.maybelline.com" gives "maybelline.com"-like checks: true when the brand's name is the site's own name. */
function isOfficial(host, brand) {
    const h = String(host || "").toLowerCase().replace(/^www\./, "");
    const labels = h.split(".");
    if (labels.length < 2) return false;
    const site = labels.length >= 3 && /^(co|com|org|net)$/.test(labels[labels.length - 2]) ? labels[labels.length - 3] : labels[labels.length - 2];
    const slug = String(brand || "").toLowerCase().normalize("NFD").replace(/[̀-ͯ]/g, "").replace(/[^a-z0-9]/g, "");
    const first = String(brand || "").toLowerCase().normalize("NFD").replace(/[̀-ͯ]/g, "").split(/[^a-z0-9]+/).filter((w) => w.length >= 4)[0] || "";
    if (!site || site.length < 3 || slug.length < 3) return false;
    return site === slug || (first.length >= 4 && site.includes(first)) || (site.length >= 5 && slug.includes(site));
}

/** True for an https address on a public host name (no IP address, no single-word or local name). */
function fetchable(url) {
    try {
        const u = new URL(url);
        if (u.protocol !== "https:" || u.username || u.password || u.port) return false;
        const host = u.hostname.toLowerCase();
        if (!host.includes(".") || /^[\d.]+$/.test(host) || host.includes(":") || /(^|\.)(localhost|local|internal|lan|home|corp)$/.test(host)) return false;
        return true;
    } catch (error) { return false; }
}

/** True for an address on the public internet. Loopback, private (RFC 1918), CGNAT, link-local, ULA, multicast, reserved and
 *  IPv4-mapped forms of these are refused. */
function publicAddress(address, family) {
    let a = String(address || "").toLowerCase();
    if (family === 6 || a.includes(":")) {
        const mapped = /^::ffff:(\d+\.\d+\.\d+\.\d+)$/.exec(a);
        if (mapped) return publicAddress(mapped[1], 4);
        if (a === "::" || a === "::1") return false;
        if (/^(fc|fd)/.test(a) || /^fe[89ab]/.test(a) || /^ff/.test(a) || a.startsWith("64:ff9b:") || a.startsWith("2001:db8") || a.startsWith("100:")) return false;
        return true;
    }
    const n = a.split(".").map(Number);
    if (n.length !== 4 || n.some((x) => !Number.isInteger(x) || x < 0 || x > 255)) return false;
    const [b0, b1] = n;
    if (b0 === 0 || b0 === 10 || b0 === 127 || b0 >= 224) return false;
    if (b0 === 100 && b1 >= 64 && b1 <= 127) return false;              // CGNAT
    if (b0 === 169 && b1 === 254) return false;                         // link-local (cloud metadata)
    if (b0 === 172 && b1 >= 16 && b1 <= 31) return false;
    if (b0 === 192 && (b1 === 168 || (b1 === 0 && (n[2] === 0 || n[2] === 2)))) return false;
    if (b0 === 198 && (b1 === 18 || b1 === 19 || (b1 === 51 && n[2] === 100))) return false;
    if (b0 === 203 && b1 === 0 && n[2] === 113) return false;
    return true;
}

/** DNS look-up used AT CONNECT TIME: every answer must be public, so the address checked is the address connected to
 *  (no window for DNS rebinding between a check and the connection). */
function vettedLookup(hostname, options, callback) {
    dns.lookup(hostname, { all: true, verbatim: true }, (error, addresses) => {
        if (error) return callback(error);
        if (!addresses.length || !addresses.every((x) => publicAddress(x.address, x.family))) return callback(new Error("refused: not a public address"));
        const first = addresses[0];
        if (options && options.all) return callback(null, addresses);
        callback(null, first.address, first.family);
    });
}

/** One GET with the vetted look-up; redirects are NOT followed here. Resolves { status, location, type, body }. */
function getOnce(url, msLeft) {
    return new Promise((resolve) => {
        let done = false, deadline = null, request = null;
        const finish = (value) => { if (!done) { done = true; clearTimeout(deadline); resolve(value); } };
        // a TOTAL limit (the socket's own timeout only notices silence, not a page that trickles in slowly)
        deadline = setTimeout(() => { if (request) request.destroy(); finish({ status: 0 }); }, Math.max(1, msLeft));
        request = https.get(url, {
            lookup: vettedLookup, timeout: PAGE_SECONDS * 1000,
            headers: { "User-Agent": "GlowUpCatalogue/1.0 (ingredient list check)", Accept: "text/html" }
        }, (response) => {
            const status = response.statusCode || 0;
            const type = String(response.headers["content-type"] || "");
            if (status >= 300 && status < 400) { response.resume(); return finish({ status, location: String(response.headers.location || "") }); }
            if (status !== 200 || !/text\/html|application\/xhtml/i.test(type) || Number(response.headers["content-length"] || 0) > MAX_PAGE_BYTES) {
                response.resume(); return finish({ status: 0 });
            }
            const chunks = []; let size = 0;
            response.on("data", (chunk) => {
                size += chunk.length;
                if (size > MAX_PAGE_BYTES) { request.destroy(); return finish({ status: 200, type, body: Buffer.concat(chunks).toString("utf8") }); }
                chunks.push(chunk);
            });
            response.on("end", () => finish({ status: 200, type, body: Buffer.concat(chunks).toString("utf8") }));
            response.on("error", () => finish({ status: 0 }));
        });
        request.on("timeout", () => { request.destroy(); finish({ status: 0 }); });
        request.on("error", () => finish({ status: 0 }));
    });
}

/** The page's HTML, or null. Redirects are followed by hand (at most MAX_HOPS), every hop checked again. */
async function safeGet(url) {
    let at = url;
    const stop = Date.now() + PAGE_SECONDS * 1000;                    // one deadline for the page and all its redirects
    for (let hop = 0; hop <= MAX_HOPS; hop++) {
        if (!fetchable(at) || Date.now() >= stop) return null;
        const answer = await getOnce(at, stop - Date.now());
        if (answer.status === 200) return answer.body || null;
        if (!answer.location) return null;
        try { at = new URL(answer.location, at).href; } catch (error) { return null; }
    }
    return null;
}

let pageGetter = safeGet;
/** Tests only: the pretend web. Production always uses safeGet. */
function setPageGetterForTests(getter) { pageGetter = getter || safeGet; }

async function fetchPage(url, fetcher) {
    if (!fetchable(url)) return null;
    try {
        if (fetcher) {
            // only the tests pass a fetcher (a pretend web); it is never given a live address in production
            const answer = await fetcher(url, { redirect: "manual" });
            if (!answer || !answer.ok || (answer.url && !fetchable(answer.url))) return null;
            if (!/text\/html|application\/xhtml/i.test(String(answer.headers.get("content-type") || ""))) return null;
            const html = await answer.text();
            return html.length > MAX_PAGE_BYTES ? html.slice(0, MAX_PAGE_BYTES) : html;
        }
        return await pageGetter(url);
    } catch (error) { return null; }
}

/** Plain web search (DuckDuckGo's HTML page): result addresses only. */
async function searchPages(query, fetcher, resultUrl) {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), SEARCH_SECONDS * 1000);
    try {
        const answer = await fetcher("https://html.duckduckgo.com/html/?q=" + encodeURIComponent(query), { headers: { "User-Agent": "GlowUpCatalogue/1.0 (ingredient list check)" }, signal: controller.signal });
        if (!answer.ok) return [];
        const html = await answer.text();
        const urls = [];
        for (const match of html.matchAll(/<a\b(?=[^>]*class="[^"]*\bresult__a\b")[^>]*\bhref="([^"]+)"/gi)) {
            const url = resultUrl(match[1]);
            if (url && !urls.includes(url)) urls.push(url);
            if (urls.length >= 8) break;
        }
        return urls;
    } catch (error) { return []; }
    finally { clearTimeout(timer); }
}

/**
 * Looks for the product's ingredient list on real pages. `pages` = addresses the AI's search opened. Returns
 * { ingredients, url, site, kind, tried } (ingredients empty when no page had a list) and never throws.
 * `kindOf(url)` = "retailer" for a known shop (from assist-ingredient.js), anything else otherwise.
 */
async function findPageIngredients({ brand, productName, pages, fetcher, searchFetcher, resultUrl, kindOf, notes }) {
    const result = { ingredients: [], url: "", site: "", kind: "", tried: 0 };
    if (!brand || !productName) return result;
    // the search goes to one fixed public address (DuckDuckGo); product pages go through the vetted fetch (fetchPage)
    const doSearch = searchFetcher || fetcher || fetch;
    let urls = (pages || []).filter(fetchable);
    if (resultUrl) urls = urls.concat(await searchPages(`${brand} ${productName} ingredients`, doSearch, resultUrl));
    const seen = new Set();
    const ranked = urls.filter((url) => !seen.has(url) && seen.add(url)).map((url) => {
        const site = new URL(url).hostname.toLowerCase().replace(/^www\./, "");
        const kind = isOfficial(site, brand) ? "official" : (kindOf && kindOf(url) === "retailer") ? "retailer" : "other";
        return { url, site, kind, rank: kind === "official" ? 0 : kind === "retailer" ? 1 : 2 };
    }).sort((a, b) => a.rank - b.rank).slice(0, MAX_PAGES);
    // fetched side by side; the best-ranked page that has a list wins
    const found = await Promise.all(ranked.map(async (page) => ({ page, list: extractIngredientList(await fetchPage(page.url, fetcher)) })));
    result.tried = ranked.length;
    const best = found.find((f) => f.list && f.list.length > 0);
    if (best) Object.assign(result, { ingredients: best.list.slice(0, 80).map((item) => item.slice(0, 100)), url: best.page.url, site: best.page.site, kind: best.page.kind });
    if (notes) notes.push(best ? `ingredient list from ${best.page.kind} page ${best.page.site}` : `no ingredient list on ${ranked.length} page(s)`);
    return result;
}

module.exports = { pageText, listFrom, extractIngredientList, isOfficial, fetchable, publicAddress, safeGet, findPageIngredients, setPageGetterForTests, MAX_PAGES };
