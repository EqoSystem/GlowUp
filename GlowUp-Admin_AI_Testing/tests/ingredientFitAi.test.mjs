// api/ingredient-fit.js readAnswer: an answer is used only with a page the search really opened; ids are cut to the official lists. No network.
import { createRequire } from "node:module";
import { evidenceProblem } from "../_Shared/glowupRules.js";
const { readAnswer } = createRequire(import.meta.url)("../api/ingredient-fit.js");
let checks = 0, failed = 0;
const ok = (c, t) => { checks++; if (!c) { failed++; console.log("FAIL: " + t); } };
const allergens = [{ id: "fragrance", label: "Fragrance" }, { id: "nut", label: "Nut" }], conditions = [{ id: "rosacea", label: "Rosacea" }, { id: "acne", label: "Acne" }];
const reply = (json, sites) => ({ candidates: [{ content: { parts: [{ text: "```json\n" + JSON.stringify(json) + "\n```" }] }, groundingMetadata: { groundingChunks: sites.map((t) => ({ web: { title: t, uri: "https://vertexaisearch.cloud.google.com/x" } })) } }] });

const good = readAnswer(reply({ allergenTags: ["fragrance", "madeup"], avoidForSkinTypes: ["Sensitive", "Weird"], avoidForConditions: ["rosacea", "nothing"], why: "DermNet lists it as a sensitiser.",
    sources: [{ title: "DermNet", url: "https://dermnetnz.org/topics/x" }] }, ["dermnetnz.org"]), allergens, conditions);
ok(good.found && good.allergenTags.join() === "fragrance", "ids outside the allergen list are dropped");
ok(good.avoidForSkinTypes.join() === "Sensitive" && good.avoidForConditions.join() === "rosacea", "skin types and conditions are cut to the lists");
ok(good.sources.length === 1 && /dermnetnz\.org/.test(good.sources[0]), "the opened page is kept");
ok(evidenceProblem("caution", good.sources) === "", "a DermNet page passes the website's evidence tiers");

const invented = readAnswer(reply({ allergenTags: ["fragrance"], avoidForSkinTypes: [], avoidForConditions: [], why: "x", sources: [{ title: "Blog", url: "https://madeup-blog.example/post" }] }, ["dermnetnz.org"]), allergens, conditions);
ok(!invented.found, "a link the search never opened is dropped, and with it the answer");
const sub = readAnswer(reply({ allergenTags: ["fragrance"], avoidForSkinTypes: [], avoidForConditions: [], why: "x", sources: [{ title: "D", url: "https://evil.dermnetnz.org/a" }] }, ["dermnetnz.org"]), allergens, conditions);
ok(!sub.found, "a sub-domain of an opened site is not the opened site");
ok(!readAnswer(reply({ allergenTags: [], avoidForSkinTypes: [], avoidForConditions: [], why: "", sources: [{ title: "D", url: "https://dermnetnz.org/a" }] }, ["dermnetnz.org"]), allergens, conditions).found, "no flags = not found");
ok(!readAnswer({ candidates: [{ content: { parts: [{ text: "I am not sure." }] } }] }, allergens, conditions).found, "plain text with no JSON = not found");
ok(!readAnswer(null, allergens, conditions).found, "empty answer = not found");
ok(evidenceProblem("caution", ["Some blog | https://madeup-blog.example/post"]) !== "", "a blog page does not pass the evidence tiers");
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED of ${checks}`);
process.exit(failed === 0 ? 0 : 1);
