// Checks of the scan snapshot function (api/scan-snapshot.js) WITHOUT any network: Google's answers are pretend.
// Run: node tests/snapshot.test.mjs
import { createRequire } from "node:module";
const require = createRequire(import.meta.url);
const handler = require("../api/scan-snapshot.js");
const { photoOf, tidySnapshot, cleanLinks, dayStamp } = handler.helpers;

let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };
const equal = (a, b, what) => check(JSON.stringify(a) === JSON.stringify(b), `${what} (got ${JSON.stringify(a)})`);

// a tiny JPEG: SOI, one APPn-free header, SOS, data. Padded so the base64 text passes the length floor.
const jpeg = (extra = []) => Buffer.from([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x4A, 0x46, ...extra, 0xFF, 0xDA, 0x00, 0x02, ...new Array(90).fill(7), 0xFF, 0xD9]);
const exif = Buffer.from([0xFF, 0xD8, 0xFF, 0xE1, 0x00, 0x08, 0x45, 0x78, 0x69, 0x66, 0x00, 0x00, 0xFF, 0xDA, 0x00, 0x02, ...new Array(90).fill(7), 0xFF, 0xD9]);
const b64 = (b) => b.toString("base64");

// ---- the photo
check(photoOf(b64(jpeg())).bytes instanceof Buffer, "a plain JPEG is accepted");
equal(photoOf(b64(exif)).error, "The photo carries EXIF data. Send it without.", "a JPEG with an EXIF block is refused");
equal(photoOf(b64(Buffer.concat([Buffer.from("GIF89a"), Buffer.alloc(120, 1)]))).error, "Only a JPEG image is accepted.", "something that is not a JPEG is refused");
check(photoOf(b64(Buffer.concat([jpeg(), Buffer.alloc(80001, 1)]))).error === "The photo is larger than 80 KB.", "a photo over 80 KB is refused");
check(photoOf(123).error && photoOf("<svg>").error && photoOf("").error, "no text, not base64, or empty: refused");

// ---- the answer
const draft = tidySnapshot({ found: true, brand: "Maybelline", productName: "Maybelline Fit Me Matte", variant: "128 Warm Nude", size: "30 ml", cosmeticType: "Foundation",
    shadeHex: "c24b62", ingredients: ["Aqua", "aqua", "<b>x</b>", "see https://evil.example/", "Talc", 5], confidence: "high" });
equal([draft.brand, draft.productName, draft.variant, draft.size, draft.cosmeticType, draft.shadeHex], ["Maybelline", "Fit Me Matte", "128 Warm Nude", "30 ml", "foundation", "#C24B62"],
    "brand, name, variant, size and kind stay separate; the brand is taken out of the name");
equal(draft.ingredients, ["Aqua", "Talc"], "ingredients: no repeats, no markup, no links, no non-text");
equal(draft.confidence, 0.85, "a word for confidence becomes a number");
equal(tidySnapshot({ found: false }), { found: false }, "found:false stays false");
equal(tidySnapshot({ found: true, confidence: 7 }).found, false, "no brand and no name is not found");
equal(tidySnapshot({ found: true, brand: "A", confidence: 9 }).confidence, 1, "confidence is at most 1");

// ---- the links
const long = "https://example.com/" + "a".repeat(300);
equal(cleanLinks([{ url: "https://vertexaisearch.cloud.google.com/grounding-api-redirect/abc" }, { url: "http://shop.example/a" }, { url: "https://evil.example@x.test/" },
    { url: long }, { url: "https://brand.example/a" }, { url: "https://brand.example/a" }, { url: "javascript:alert(1)" }]),
    ["https://brand.example/a"], "a redirect address, http, a long link, a repeat and a script address are never shown");
equal(cleanLinks(Array.from({ length: 9 }, (_, i) => ({ url: `https://s${i}.example/` }))).length, 5, "at most 5 links");
equal(dayStamp(new Date(Date.UTC(2026, 9, 22, 23, 59))), "20261022", "the day is the UTC date");

// ---- the whole function
const GOOD = JSON.stringify({ found: true, brand: "Maybelline", productName: "Fit Me", variant: "128", size: "30 ml", cosmeticType: "foundation", shadeHex: "#C8A27A", ingredients: ["Aqua", "Talc"], confidence: 0.8 });
const reply = (status, json, headers = {}) => ({ ok: status >= 200 && status < 300, status, json: async () => json, headers: { get: (k) => headers[String(k).toLowerCase()] || null } });
async function call({ token = "good", image = b64(jpeg()), method = "POST", quota = 200, quotaDoc = null, gemini = null, env = true } = {}) {
    if (env) process.env.GEMINI_API_KEY = "test-key"; else delete process.env.GEMINI_API_KEY;
    const sent = [];
    globalThis.fetch = async (url, options = {}) => {
        url = String(url); sent.push({ url, options });
        if (url.includes("accounts:lookup")) return JSON.parse(options.body).idToken === "good" ? reply(200, { users: [{ localId: "user1" }] }) : reply(400, {});
        if (url.endsWith(":commit")) return reply(quota, {});
        if (url.includes("/documents/snapshot_quota/")) return quotaDoc ? reply(200, quotaDoc) : reply(404, {});
        if (url.includes("generativelanguage")) return gemini ? gemini(JSON.parse(options.body)) : reply(200, { candidates: [{ content: { parts: [{ text: GOOD }] } }] });
        if (url.includes("grounding-api-redirect/ok")) return reply(302, {}, { location: "https://www.maybelline.com/fit-me" });
        return reply(404, {});
    };
    return new Promise((done) => {
        const res = { statusCode: 0, setHeader() {}, end: (t) => done({ status: res.statusCode, body: JSON.parse(t), sent }) };
        handler({ method, headers: { authorization: token ? "Bearer " + token : "" }, body: { image } }, res);
    });
}
const calls = (r, part) => r.sent.filter((x) => x.url.includes(part));
let r;

r = await call({ method: "GET" }); equal(r.status, 405, "GET is refused");
r = await call({ env: false }); check(r.status === 503 && /type the product in/i.test(r.body.error), "no key on the server: said plainly, type it in by hand");
r = await call({ token: "" }); check(r.status === 401 && calls(r, "generativelanguage").length === 0, "no sign-in: refused, the AI is not called");
r = await call({ token: "forged" }); check(r.status === 401 && calls(r, ":commit").length === 0, "a token Google does not know: refused before any slot is spent");
check(calls(await call({ token: "forged" }), "admins").length === 0, "the admin list is not consulted: any signed-in user may send a photo");
r = await call({ image: b64(exif) }); check(r.status === 400 && calls(r, ":commit").length === 0, "an EXIF photo is refused before a slot is spent");
r = await call({ image: "not an image at all, just text that is long enough to pass the length floor of one hundred characters....." }); equal(r.status, 400, "text that is not an image: refused");

r = await call();
check(r.status === 200 && r.body.found && r.body.draft.brand === "Maybelline" && r.body.draft.productName === "Fit Me", "a good photo gives separate brand and product name");
check(calls(r, ":commit").length === 1 && r.sent.find((x) => x.url.endsWith(":commit")).options.headers.Authorization === "Bearer good", "one slot is spent, with the user's own token");
const commit = JSON.parse(r.sent.find((x) => x.url.endsWith(":commit")).options.body);
check(commit.writes.length === 2 && commit.writes[0].update.name.endsWith("/snapshot_quota/user1_" + dayStamp(new Date())) && commit.writes[1].update.name.endsWith("/snapshot_quota/all_" + dayStamp(new Date())), "the commit moves the account's counter and the global counter");
const ask = JSON.parse(calls(r, "generativelanguage")[0].options.body);
check(calls(r, "generativelanguage").length === 1 && ask.tools && ask.contents[0].parts[1].inline_data.mime_type === "image/jpeg", "ONE call to the AI, with the photo and web search");
check(calls(r, "generativelanguage")[0].options.headers["x-goog-api-key"] === "test-key" && !calls(r, "generativelanguage")[0].url.includes("test-key"), "the key goes in a header, never in the address");
check(JSON.stringify(r.body).indexOf("test-key") < 0 && /unverified/i.test(r.body.label) && /AI-generated/.test(r.body.label), "no key in the answer; the answer is labelled pending, unverified, AI-generated");

r = await call({ gemini: () => reply(200, { candidates: [{ content: { parts: [{ text: GOOD }] }, groundingMetadata: { groundingChunks: [
    { web: { uri: "https://vertexaisearch.cloud.google.com/grounding-api-redirect/ok", title: "maybelline.com" } },
    { web: { uri: "https://vertexaisearch.cloud.google.com/grounding-api-redirect/lost", title: "somewhere.com" } }] } }] }) });
equal(r.body.sourceUrls, ["https://www.maybelline.com/fit-me"], "a search redirect is followed to the real page; one that cannot be followed is never shown");
equal(r.body.webSources, true, "web sources found: said so");

let geminiCalls = [];
r = await call({ gemini: (body) => { geminiCalls.push(!!body.tools); return body.tools ? reply(429, {}) : reply(200, { candidates: [{ content: { parts: [{ text: GOOD }] } }] }); } });
equal(geminiCalls, [true, false], "search grounding says 429: ONE photo-only reading follows, no more");
check(r.status === 200 && r.body.webSources === false && r.body.notes.includes("no web sources found") && r.body.sourceUrls.length === 0, "photo-only answer is marked: no web sources found");
geminiCalls = [];
r = await call({ gemini: (body) => { geminiCalls.push(!!body.tools); return reply(429, {}); } });
check(r.status === 502 && /by hand/.test(r.body.error) && geminiCalls.length === 2, "both 429: stops after two calls, tells the user to type the product in");
geminiCalls = [];
r = await call({ gemini: () => { geminiCalls.push(1); return reply(500, {}); } });
check(r.status === 502 && geminiCalls.length === 1, "a server error is NOT retried");
r = await call({ gemini: () => reply(200, { candidates: [{ content: { parts: [{ text: "no json here" }] } }] }) });
check(r.status === 502 && /by hand/.test(r.body.error), "an answer that is not JSON: type it in by hand");

// ---- the limits
r = await call({ quota: 403, quotaDoc: { fields: { count: { integerValue: "5" }, lastAt: { timestampValue: "2020-01-01T00:00:00Z" } } } });
check(r.status === 429 && r.body.code === "daily" && calls(r, "generativelanguage").length === 0, "5 photos today: refused, the AI is not called");
r = await call({ quota: 403, quotaDoc: { fields: { count: { integerValue: "2" }, lastAt: { timestampValue: new Date(Date.now() - 5000).toISOString() } } } });
check(r.status === 429 && r.body.code === "cooldown" && calls(r, "generativelanguage").length === 0, "less than 30 s since the last photo: refused");
r = await call({ quota: 403, quotaDoc: { fields: { count: { integerValue: "1" }, lastAt: { timestampValue: "2020-01-01T00:00:00Z" } } } });
check(r.status === 429 && r.body.code === "busy", "own limits fine but the commit refused: the global cap is reached");
equal([handler.helpers.COOLDOWN_SECONDS, handler.helpers.PER_UID_DAILY, handler.helpers.GLOBAL_DAILY], [30, 5, 150], "the constants: 30 s, 5 a day, 150 a day for everybody");

console.log("checks made: " + checks);
console.log(failed === 0 ? "ALL PASSED" : failed + " FAILED");
process.exit(failed === 0 ? 0 : 1);
