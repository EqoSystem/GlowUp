// Checks of "Assist with AI" (api/assist-ingredient.js) WITHOUT any network: the helpers, and the whole function with
// pretend answers from Google in place of the real services.   Run: node tests/assist.test.mjs
import { createRequire } from "node:module";
const require = createRequire(import.meta.url);
const handler = require("../api/assist-ingredient.js");
const { firstObject, tidy, isCredible } = handler.helpers;

let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };
const equal = (a, b, what) => check(JSON.stringify(a) === JSON.stringify(b), `${what} (got ${JSON.stringify(a)})`);

// ---- reading the AI's text
equal(firstObject('Here it is:\n```json\n{"name":"Vitamin E","note":"a } inside text"}\n```'), { name: "Vitamin E", note: "a } inside text" }, "the object is found inside other text");
equal(firstObject("no object here"), null, "no object gives nothing");

// ---- only what the form knows
const tidied = tidy({ name: " Vitamin E ", techName: "Tocopheryl Acetate", riskLevel: "SAFE", cautionNote: "It can clog pores.", allergenTags: ["Fragrance", "made-up"],
    avoidForSkinTypes: ["oily"], goodForSkinTypes: ["Oily", "Dry"], avoidForConditions: ["acne"], goodForConditions: ["acne", "eczema"], minAge: "12", keyActive: "yes", confidence: "certain" }, "X");
equal(tidied.riskLevel, "safe", "risk level in small letters");
equal(tidied.cautionNote, "can clog pores", "the caution note finishes the sentence 'It ...'");
equal(tidied.allergenTags, ["fragrance"], "an unknown allergen family is dropped");
equal(tidied.goodForSkinTypes, ["Dry"], "a skin type under both lists stays under Avoid only");
equal(tidied.goodForConditions, ["eczema"], "a condition under both lists stays under Avoid only");
equal(tidied.minAge, 0, "an age that is not a whole number is not taken");
equal(tidied.keyActive, false, "only a real true counts");
equal(tidied.confidence, "low", "an unknown confidence counts as low");
equal(tidy({}, "Mica").techName, "Mica", "an empty answer keeps the name that was asked");

// ---- which sites count as credible
check(isCredible("ec.europa.eu") && isCredible("pubchem.ncbi.nlm.nih.gov") && isCredible("cir-safety.org") && isCredible("verification.fda.gov.ph"), "regulators and scientific bodies are credible");
check(!isCredible("someblog.com") && !isCredible("shopee.ph") && !isCredible("fda.gov.evil.com"), "a blog, a shop and a look-alike address are not");

// ---- the whole function, with pretend services
function call({ method = "POST", token = "good", body = { name: "Tocopheryl Acetate" }, key = "test-key", gemini } = {}) {
    process.env.GEMINI_API_KEY = key; if (!key) delete process.env.GEMINI_API_KEY;
    const sent = [];
    globalThis.fetch = async (url, options = {}) => {
        sent.push({ url: String(url), options });
        const reply = (status, json, headers = {}) => ({ ok: status >= 200 && status < 300, status, json: async () => json, headers: { get: (h) => headers[h.toLowerCase()] || null } });
        if (String(url).includes("accounts:lookup")) return JSON.parse(options.body).idToken === "good" || JSON.parse(options.body).idToken === "notadmin" ? reply(200, { users: [{ localId: JSON.parse(options.body).idToken === "good" ? "admin1" : "user9" }] }) : reply(400, {});
        if (String(url).includes("/documents/admins/")) return String(url).endsWith("/admin1") ? reply(200, {}) : reply(403, {});
        if (String(url).includes("generativelanguage")) return gemini ? gemini(reply) : reply(200, {
            candidates: [{
                content: { parts: [{ text: '{"name":"Vitamin E","techName":"Tocopheryl Acetate","description":"A form of vitamin E.","riskLevel":"safe","confidence":"high"}' }] },
                groundingMetadata: { groundingChunks: [{ web: { uri: "https://vertexaisearch.cloud.google.com/grounding-api-redirect/abc", title: "someblog.com" } }, { web: { uri: "https://vertexaisearch.cloud.google.com/grounding-api-redirect/def", title: "cir-safety.org" } }] }
            }]
        });
        if (String(url).includes("grounding-api-redirect/abc")) return reply(302, {}, { location: "https://someblog.com/vitamin-e" });
        if (String(url).includes("grounding-api-redirect/def")) return reply(302, {}, { location: "https://www.cir-safety.org/ingredients/tocopheryl-acetate" });
        return reply(404, {});
    };
    return new Promise((done) => {
        const res = { statusCode: 0, setHeader() {}, end: (text) => done({ status: res.statusCode, body: JSON.parse(text), sent }) };
        handler({ method, headers: token ? { authorization: "Bearer " + token } : {}, body }, res);
    });
}

let r = await call();
equal(r.status, 200, "an admin gets an answer");
check(r.body.found === true && r.body.draft.name === "Vitamin E" && r.body.draft.riskLevel === "safe", "the draft is returned");
equal(r.body.sources.map((s) => s.url), ["https://www.cir-safety.org/ingredients/tocopheryl-acetate", "https://someblog.com/vitamin-e"], "real addresses, the credible page first");
equal(r.body.credibleSources, 1, "credible pages are counted");
const toAi = r.sent.find((s) => s.url.includes("generativelanguage"));
check(toAi.options.headers["x-goog-api-key"] === "test-key" && !toAi.url.includes("test-key"), "the key goes in a header, never in the address");
check(JSON.stringify(r.body).indexOf("test-key") < 0, "the key is never in the answer");
check(toAi.options.body.includes("Tocopheryl Acetate") && !toAi.options.body.includes("admin1"), "the AI is sent the ingredient's name and not who asked");

equal((await call({ token: "" })).status, 401, "no sign-in: refused");
equal((await call({ token: "forged" })).status, 401, "a sign-in Google does not know: refused");
r = await call({ token: "notadmin" });
equal(r.status, 401, "a signed-in account that is not an admin: refused");
check(!r.sent.some((s) => s.url.includes("generativelanguage")), "and the AI is not asked for it");
equal((await call({ method: "GET" })).status, 405, "only POST");
equal((await call({ body: { name: " " } })).status, 400, "no name: refused");
equal((await call({ key: "" })).status, 503, "no key set on the server: said plainly");
r = await call({ gemini: (reply) => reply(429, {}) });
check(r.status === 502 && /quota/i.test(r.body.error), "a used-up quota is said in plain words");
r = await call({ gemini: (reply) => reply(200, { candidates: [{ content: { parts: [{ text: '{"notFound":true}' }] } }] }) });
check(r.status === 200 && r.body.found === false, "nothing credible found: no draft, no made-up entry");

console.log("checks made: " + checks);
console.log(failed === 0 ? "ALL PASSED" : failed + " FAILED");
process.exit(failed === 0 ? 0 : 1);
