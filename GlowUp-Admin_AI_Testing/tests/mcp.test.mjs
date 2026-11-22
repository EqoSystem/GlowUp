// api/mcp.js: sign-in, JSON-RPC shape, tools, limits, failure of outside services. Fetch is mocked: nothing leaves the machine.
import { createRequire } from "node:module";
const handler = createRequire(import.meta.url)("../api/mcp.js");
let pass = 0, fail = 0;
const check = (name, ok) => { if (ok) pass++; else { fail++; console.log("FAIL", name); } };

let outside = "ok";
globalThis.fetch = async (url) => {
    const u = String(url);
    const json = (b, status = 200) => ({ ok: status < 400, status, json: async () => b });
    if (u.includes("identitytoolkit")) return json({ users: [{ localId: "u1", emailVerified: true, providerUserInfo: [{}] }] });
    if (outside === "down") throw new Error("offline");
    if (u.includes("openbeautyfacts")) return json(u.includes("5000000000000") ? { product: { product_name: "Lip Tint", brands: "Acme", ingredients_text: "Aqua, Mica" } } : { products: [] });
    if (u.includes("pubchem")) return u.includes("Nope") ? json({}, 404) : json({ PropertyTable: { Properties: [{ CID: 936, MolecularFormula: "C6H6N2O", MolecularWeight: "122.12", IUPACName: "pyridine-3-carboxamide" }] } });
    return json({}, 404);
};

function call(body, headers = { authorization: "Bearer t" }, method = "POST") {
    return new Promise((resolve) => {
        const res = { headers: {}, setHeader(k, v) { this.headers[k] = v; }, end(t) { resolve({ status: this.statusCode, body: t ? JSON.parse(t) : null }); } };
        handler({ method, headers, body }, res);
    });
}
const rpc = (method, params, id = 1) => ({ jsonrpc: "2.0", id, method, params });
const run = async (name, args) => (await call(rpc("tools/call", { name, arguments: args }))).body.result.content[0].text;

check("GET refused", (await call(null, {}, "GET")).status === 405);
check("no sign-in refused", (await call(rpc("ping"), {})).status === 401);
const init = (await call(rpc("initialize", {}))).body;
check("initialize names the server", init.result.serverInfo.name === "glowup" && !!init.result.capabilities.tools);
check("notification gets 202", (await call({ jsonrpc: "2.0", method: "notifications/initialized" })).status === 202);
const list = (await call(rpc("tools/list"))).body.result.tools;
check("three tools listed", list.length === 3 && list.every((t) => t.inputSchema && t.description));
check("unknown method", (await call(rpc("nope"))).body.error.code === -32601);
check("unknown tool", (await call(rpc("tools/call", { name: "__proto__", arguments: {} }))).body.error.code === -32602);
check("barcode hit", (await run("product_lookup", { query: "5000000000000" })).includes("Acme - Lip Tint"));
check("name miss says no match", (await run("product_lookup", { query: "zzz" })).includes("no match"));
check("pubchem hit", (await run("ingredient_lookup", { name: "Niacinamide" })).includes("C6H6N2O"));
check("pubchem miss", (await run("ingredient_lookup", { name: "Nope" })).includes("no compound"));
check("fda finds a catalogue product", (await run("fda_check", { name: "Complexion Blur Translucent Perfecting Powder", brand: "Absidy" })).includes("NN-1000014495482"));
check("fda no match is not 'unregistered'", (await run("fda_check", { name: "qqqq xxxx" })).includes("does not prove"));
outside = "down";
check("outside down: plain text, no error", (await run("product_lookup", { query: "5000000000000" })).includes("not available"));
check("pubchem down: plain text", (await run("ingredient_lookup", { name: "Niacinamide" })).includes("not available"));
check("oversized body refused", (await call("x".repeat(20000))).status === 413);
let limited = 0; for (let i = 0; i < 40; i++) if ((await call(rpc("ping"))).status === 429) limited++;
check("per-user limit applies", limited > 0);
console.log(fail === 0 ? `ALL PASSED  (checks made: ${pass})` : `${fail} FAILED  (checks made: ${pass + fail})`);
process.exit(fail ? 1 : 0);
