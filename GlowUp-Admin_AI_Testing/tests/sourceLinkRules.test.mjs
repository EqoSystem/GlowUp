// Checks _Shared/sourceLinkRules.mjs (the source-link rules shared by the page and the server) and the server's check / CosIng helpers
// in api/source-link.js with pretend network answers (nothing is sent anywhere).   Run from the website folder: node tests/sourceLinkRules.test.mjs
import { fileURLToPath, pathToFileURL } from "node:url";
import { createRequire } from "node:module";
import path from "node:path";

const here = path.dirname(fileURLToPath(import.meta.url));
const r = await import(pathToFileURL(path.join(here, "..", "_Shared", "sourceLinkRules.mjs")).href);
const require = createRequire(import.meta.url);
const api = require(path.join(here, "..", "api", "source-link.js")).helpers;

let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };
const equal = (got, expected, what) => check(JSON.stringify(got) === JSON.stringify(expected), `${what} (got ${JSON.stringify(got)})`);

// ---- the validator
check(r.checkSourceUrl("").ok && r.checkSourceUrl("  ").empty, "an empty box is fine (optional)");
for (const bad of ["javascript:alert(1)", "data:text/html,x", "file:///etc/passwd", "http://www.cir-safety.org/x", "ftp://x.org/a", "www.cir-safety.org/x", "https://cir-safety.org@evil.com/", "https://x.org/a b", "https://localhost/x"])
    check(!r.checkSourceUrl(bad).ok, `blocked: ${bad}`);
for (const engine of ["https://www.google.com/search?q=glycerin", "https://google.co.uk/search?q=x", "https://www.bing.com/search?q=x", "https://duckduckgo.com/?q=x", "https://search.yahoo.com/search?p=x", "https://search.brave.com/search?q=x"])
    check(!r.checkSourceUrl(engine).ok && /search engine/.test(r.checkSourceUrl(engine).error), `search engine blocked: ${engine}`);
check(!r.checkSourceUrl("https://x.org/" + "a".repeat(700)).ok, "too long is blocked");
const cir = r.checkSourceUrl("  https://www.cir-safety.org/ingredient/glycerin  ");
check(cir.ok && cir.value === "https://www.cir-safety.org/ingredient/glycerin" && cir.warnings.length === 0 && cir.tier.label === "Industry/reference", "a CIR ingredient page: trimmed, ok, no warning, Industry/reference");
const home = r.checkSourceUrl("https://cosmileeurope.eu/");
check(home.ok && home.warnings.some((w) => w.code === "general"), "a home page: allowed with the general-page warning");
check(r.checkSourceUrl("https://cosmileeurope.eu/inci/results/?q=Glycerin").warnings.some((w) => w.code === "general"), "a search results page: general-page warning");
check(r.checkSourceUrl("https://ec.europa.eu/growth/tools-databases/cosing/").warnings.some((w) => w.code === "general"), "CosIng's start page: general-page warning");
const other = r.checkSourceUrl("https://example-blog.com/glycerin-guide");
check(other.ok && other.tier.key === "other" && other.warnings.some((w) => w.code === "untrusted"), "another site: allowed with the untrusted warning");
// ---- the host map
equal(r.tierOf("echa.europa.eu").label, "Official", "ECHA is Official");
equal(r.tierOf("chem.echa.europa.eu").label, "Official", "a sub-site of ECHA is Official");
equal(r.tierOf("ec.europa.eu").label, "Official", "CosIng (ec.europa.eu) is Official");
equal(r.tierOf("eur-lex.europa.eu").label, "Official", "EUR-Lex is Official");
equal(r.tierOf("www.cir-safety.org").label, "Industry/reference", "CIR is Industry/reference");
equal(r.tierOf("pubchem.ncbi.nlm.nih.gov").label, "Industry/reference", "PubChem is Industry/reference");
equal(r.tierOf("cosingchecker.com").label, "Unofficial", "CosIng Checker is Unofficial");
equal(r.tierOf("cir-safety.org.evil.com").label, "Other", "a look-alike is Other");
// ---- SSRF guard
for (const ip of ["127.0.0.1", "10.1.2.3", "172.16.0.1", "172.31.255.255", "192.168.1.1", "169.254.169.254", "100.64.0.1", "0.0.0.0", "224.0.0.1", "::1", "fd00::1", "fe80::1", "::ffff:127.0.0.1", ""])
    check(r.isPrivateAddress(ip), `private refused: ${ip || "(empty)"}`);
for (const ip of ["8.8.8.8", "151.101.1.69", "172.32.0.1", "2a00:1450:4001::200e"]) check(!r.isPrivateAddress(ip), `public allowed: ${ip}`);
// ---- statements and pages
equal(r.cosingCheckerPage("/ingredients/34040-glycerin/"), "https://cosingchecker.com/ingredients/34040-glycerin/", "a CosIng Checker page");
equal(r.cosingCheckerPage("https://evil.com/x"), "", "an absolute address from the API is not trusted");
equal(r.cosingCheckerPage("/ingredients/../../x/"), "", "no path tricks");
equal(r.statementOf({ functions: ["HUMECTANT", "SOLVENT"], cas_number: "56-81-5", ec_number: "200-289-5", restriction: null }), "Function: humectant, solvent. CAS: 56-81-5. EC: 200-289-5. EU restriction: none listed.", "statement");
check(r.pageMentions("<h1>GLYCERIN</h1>", "Glycerin", "") && r.pageMentions("CAS 60-27-5", "x", "60-27-5") && !r.pageMentions("nothing here", "Glycerin", ""), "page mentions by name or CAS");

// ---- the server's link check (pretend network)
const page = (status, body, headers = {}) => ({ ok: status >= 200 && status < 300, status, headers: { get: (k) => headers[k.toLowerCase()] || null }, text: async () => body, body: null });
const publicDns = async () => [{ address: "93.184.216.34" }];
const privateDns = async () => [{ address: "10.0.0.5" }];
let res = await api.checkLink("https://www.cir-safety.org/ingredient/glycerin", "Glycerin", "", async () => page(200, "<p>Glycerin report</p>"), publicDns);
equal(res.result, "checked", "a reachable page that names the ingredient: checked");
res = await api.checkLink("https://www.cir-safety.org/ingredient/glycerin", "Glycerin", "", async () => page(200, "<div id=app></div>"), publicDns);
equal(res.result, "unconfirmed", "a reachable page without the name: content not confirmed");
res = await api.checkLink("https://www.cir-safety.org/x", "Glycerin", "", async () => page(404, ""), publicDns);
equal(res.result, "dead", "404: link not working");
res = await api.checkLink("https://www.cir-safety.org/x", "Glycerin", "", async () => page(200, "Glycerin"), privateDns);
equal(res.result, "cantcheck", "a trusted name that resolves to a private address is not fetched");
res = await api.checkLink("https://example.com/x", "Glycerin", "", async () => { throw new Error("must not fetch"); }, publicDns);
equal(res.result, "cantcheck", "an untrusted site is not fetched");
let hops = 0;
res = await api.checkLink("https://www.cir-safety.org/x", "Glycerin", "", async () => { hops++; return page(301, "", { location: "http://www.cir-safety.org/y" }); }, publicDns);
check(res.result === "cantcheck" && hops === 1, "a redirect to http is refused");
res = await api.checkLink("https://www.cir-safety.org/x", "Glycerin", "", async () => page(302, "", { location: "https://evil.example/y" }), publicDns);
equal(res.result, "cantcheck", "a redirect to an untrusted site is refused");
res = await api.checkLink("https://www.cir-safety.org/x", "Glycerin", "", async () => page(302, "", { location: "/x" }), publicDns);
equal(res.result, "dead", "endless redirects end as not working");
res = await api.checkLink("javascript:alert(1)", "x", "", async () => page(200, ""), publicDns);
equal(res.result, "cantcheck", "a blocked address is never fetched");

// ---- CosIng Checker search: exact first, cache, 429
let calls = 0;
const json = (status, data, headers = {}) => ({ ok: status === 200, status, headers: { get: (k) => headers[k.toLowerCase()] || null }, json: async () => data });
const data = { results: [
    { inci_name: "GLYCERYL STEARATE", url: "/ingredients/1-glyceryl-stearate/", function: "EMOLLIENT" },
    { inci_name: "GLYCERIN", url: "/ingredients/34040-glycerin/", cas_number: "56-81-5", ec_number: "200-289-5", functions: ["HUMECTANT"], restriction: null, ref_number: "34040" },
    { inci_name: "X", url: "https://evil.com/x" }] };
api.cosingCache.clear();
let answer = await api.cosingSearch("Glycerin", async () => { calls++; return json(200, data); }, 1000);
check(answer.status === 200 && answer.body.results[0].inci === "GLYCERIN" && answer.body.results[0].exact, "the exact INCI name comes first");
equal(answer.body.results[0].page, "https://cosingchecker.com/ingredients/34040-glycerin/", "its page address");
equal(answer.body.results.find((x) => x.inci === "X").page, "", "a result without a trusted page has no page");
check(/CC BY 4.0/.test(answer.body.attribution) && /Unofficial/.test(answer.body.label), "attribution and label");
answer = await api.cosingSearch("glycerin", async () => { calls++; return json(200, data); }, 1000 + 3600 * 1000);
check(answer.body.cached === true && calls === 1, "the same name within 24 hours comes from the cache");
answer = await api.cosingSearch("glycerin", async () => { calls++; return json(200, data); }, 1000 + 25 * 3600 * 1000);
check(calls === 2 && !answer.body.cached, "after 24 hours it is asked again");
api.cosingCache.clear();
answer = await api.cosingSearch("Niacinamide", async () => json(429, {}, { "retry-after": "42" }), 5000);
check(answer.status === 429 && answer.body.retryAfter === 42, "429 passes on Retry-After");
check(!api.cosingCache.has("niacinamide"), "a 429 is not cached");
// ---- per-admin limit
api.recent.clear();
let allowedCount = 0;
for (let i = 0; i < 25; i++) if (api.allowed("admin1", 10000 + i)) allowedCount++;
check(allowedCount === 20 && api.allowed("admin1", 10000 + 61000), "20 a minute per admin, then free again");

console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
