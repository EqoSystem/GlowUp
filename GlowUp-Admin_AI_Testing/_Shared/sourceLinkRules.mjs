







export const MAX_SOURCE_LINK = 600;


export const HOST_TIERS = {
    official: { label: "Official", hosts: ["ec.europa.eu", "echa.europa.eu", "eur-lex.europa.eu"] },
    industry: { label: "Industry/reference", hosts: ["cosmileeurope.eu", "cir-safety.org", "incipedia.personalcarecouncil.org", "cosmeticsinfo.org", "pubchem.ncbi.nlm.nih.gov"] },
    unofficial: { label: "Unofficial", hosts: ["cosingchecker.com"] }
};


export const SEARCH_ENGINE_HOSTS = ["google.", "bing.com", "duckduckgo.com", "yahoo.", "search.brave.com", "yandex.", "baidu.com", "ecosia.org", "startpage.com", "search.aol.com", "ask.com"];


const GENERAL_PAGES = [
    /^\/?$/,
    /^\/growth\/tools-databases\/cosing\/?$/i,
    /^\/ingredients\/?$/i,
    /^\/inci\/results\/?$/i,
    /^\/search\/?$/i, /^\/inci\/?$/i
];
const GENERAL_QUERY = /(^|&)(q|s|query|search)=/i;

const hostMatches = (host, list) => list.some((h) => host === h || host.endsWith("." + h));


export function tierOf(host) {
    const h = String(host || "").toLowerCase().replace(/^www\./, "");
    for (const [key, tier] of Object.entries(HOST_TIERS)) if (hostMatches(h, tier.hosts)) return { key, label: tier.label };
    return { key: "other", label: "Other" };
}

export const isSearchEngine = (host) => { const h = String(host || "").toLowerCase(); return SEARCH_ENGINE_HOSTS.some((e) => e.endsWith(".") ? h.startsWith(e) || h.includes("." + e) : h === e || h.endsWith("." + e)); };






export function checkSourceUrl(raw) {
    const value = String(raw == null ? "" : raw).trim();
    const result = { value, empty: value.length === 0, ok: true, error: "", warnings: [], tier: null, host: "" };
    if (result.empty) return result;
    const fail = (error) => Object.assign(result, { ok: false, error });
    if (value.length > MAX_SOURCE_LINK) return fail(`The address is longer than ${MAX_SOURCE_LINK} characters.`);
    if (/\s/.test(value)) return fail("An address cannot contain spaces.");
    const scheme = (value.match(/^([a-z][a-z0-9+.-]*):/i) || [])[1];
    if (!scheme) return fail("Paste the whole address, starting with https://");
    if (scheme.toLowerCase() !== "https") return fail(/^(javascript|data|file|vbscript|blob)$/i.test(scheme) ? "This kind of address is not allowed. Use an https:// web page." : "Only https:// addresses are allowed.");
    let url;
    try { url = new URL(value); } catch (error) { return fail("This is not a valid web address."); }
    if (url.username || url.password || value.includes("\\")) return fail("This address hides another site. Paste the page's own address.");
    const host = url.hostname.toLowerCase();
    result.host = host.replace(/^www\./, "");
    if (!host.includes(".")) return fail("This is not a public web address.");
    if (isSearchEngine(host)) return fail("A search engine's page is not a source. Open the result and paste that page's address.");
    result.tier = tierOf(host);
    if (result.tier.key === "other") result.warnings.push({ code: "untrusted", text: "Not one of the trusted sources. Check that the site is reliable." });
    const path = url.pathname || "/";
    if (GENERAL_PAGES.some((re) => re.test(path)) || (GENERAL_QUERY.test(url.search.replace(/^\?/, "")) && result.tier.key !== "other"))
        result.warnings.push({ code: "general", text: "This looks like a general page, not a page for this ingredient." });
    return result;
}


export function isPrivateAddress(ip) {
    const a = String(ip || "").toLowerCase().replace(/^\[|\]$/g, "");
    if (!a) return true;
    if (a.includes(":")) {
        if (a === "::" || a === "::1") return true;
        const mapped = a.match(/^::ffff:(\d+\.\d+\.\d+\.\d+)$/);
        if (mapped) return isPrivateAddress(mapped[1]);
        return /^(fc|fd)/.test(a) || /^fe[89ab]/.test(a) || /^ff/.test(a) || a.startsWith("2001:db8") || a.startsWith("64:ff9b");
    }
    const p = a.split(".").map(Number);
    if (p.length !== 4 || p.some((n) => !Number.isInteger(n) || n < 0 || n > 255)) return true;
    const [x, y] = p;
    return x === 0 || x === 10 || x === 127 || (x === 169 && y === 254) || (x === 172 && y >= 16 && y <= 31) || (x === 192 && y === 168)
        || (x === 100 && y >= 64 && y <= 127) || (x === 192 && y === 0 && p[2] === 0) || (x === 198 && (y === 18 || y === 19)) || x >= 224;
}


export const checkable = (host) => tierOf(host).key !== "other";


export function pageMentions(text, inci, cas) {
    const squash = (v) => String(v || "").toLowerCase().replace(/[^a-z0-9]+/g, "");
    const page = squash(text);
    const name = squash(inci);
    if (name.length >= 3 && page.includes(name)) return true;
    const c = String(cas || "").trim();
    return /^\d{2,7}-\d{2}-\d$/.test(c) && String(text || "").includes(c);
}


export function cosingCheckerPage(relative) {
    const path = String(relative || "");
    return /^\/ingredients\/[a-z0-9][a-z0-9-]{0,300}\/$/i.test(path) ? "https://cosingchecker.com" + path : "";
}


export function statementOf(r) {
    const parts = [];
    const fn = Array.isArray(r.functions) && r.functions.length ? r.functions.join(", ") : r.function;
    if (fn) parts.push("Function: " + String(fn).toLowerCase() + ".");
    if (r.cas_number) parts.push("CAS: " + r.cas_number + ".");
    if (r.ec_number) parts.push("EC: " + r.ec_number + ".");
    parts.push("EU restriction: " + (r.restriction ? String(r.restriction) : "none listed") + ".");
    return parts.join(" ");
}
