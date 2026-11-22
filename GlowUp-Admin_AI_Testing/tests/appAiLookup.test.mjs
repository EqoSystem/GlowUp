// api/app-ai.js with ?lookup=: reference data from the MCP tools is added to the question; any failure leaves the request as the app sent it.
import { createRequire } from "node:module";
process.env.GLOWUP_APP_AI = "test-key";
const handler = createRequire(import.meta.url)("../api/app-ai.js");
let pass = 0, fail = 0;
const check = (n, ok) => { if (ok) pass++; else { fail++; console.log("FAIL", n); } };

let obf = "ok", sentToAi = null;
globalThis.fetch = async (url, o) => {
    const u = String(url), json = (b, s = 200) => ({ ok: s < 400, status: s, json: async () => b, text: async () => JSON.stringify(b) });
    if (u.includes("identitytoolkit")) return json({ users: [{ localId: "u" + Math.random(), emailVerified: true, providerUserInfo: [{}] }] });
    if (u.includes("openbeautyfacts")) { if (obf === "down") throw new Error("offline"); return json({ products: [{ product_name: "Lip Tint", brands: "Acme", ingredients_text: "Aqua, Mica" }] }); }
    if (u.includes("generativelanguage")) { sentToAi = JSON.parse(o.body); return json({ candidates: [] }); }
    return json({}, 404);
};
const ask = (query) => new Promise((resolve) => {
    const res = { setHeader() {}, end(t) { resolve({ status: this.statusCode, body: t }); } };
    handler({ method: "POST", headers: { authorization: "Bearer t", "x-forwarded-for": "1.1.1.1" }, query: { model: "gemini-flash-latest", ...query },
        body: { contents: [{ role: "user", parts: [{ text: "Find the list." }] }] } }, res);
});
const lastParts = () => sentToAi.contents[0].parts;

await ask({ lookup: "Acme Lip Tint" });
check("reference data added as a text part", lastParts().length === 2 && lastParts()[1].text.includes("Open Beauty Facts") && lastParts()[1].text.includes("Aqua, Mica"));
check("question text kept first", lastParts()[0].text === "Find the list.");
obf = "down"; sentToAi = null;
const r = await ask({ lookup: "Acme Lip Tint" });
check("tool down: request still goes on, unchanged", r.status === 200 && lastParts().length === 1);
obf = "ok"; sentToAi = null;
await ask({});
check("no lookup: nothing added", lastParts().length === 1);
sentToAi = null;
await ask({ lookup: "x" });
check("one-letter lookup ignored", lastParts().length === 1);
console.log(fail === 0 ? `ALL PASSED  (checks made: ${pass})` : `${fail} FAILED  (checks made: ${pass + fail})`);
process.exit(fail ? 1 : 0);
