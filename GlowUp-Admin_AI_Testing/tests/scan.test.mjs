


import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { ingredientLookalikes, ingredientLookalikesAsync, lookalikeCount } from "../_Shared/glowupCatalog.js";

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const read = (file) => readFileSync(path.join(root, file), "utf8");
let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };

const item = (id, name, techName, extra) => Object.assign({ id, name, techName: techName || name, aliases: [], source: "", reviewed: true, binned: false }, extra || {});
const many = [];
for (let i = 0; i < 400; i++) many.push(item("n" + i, "Ingredient " + String.fromCharCode(97 + (i % 26)) + String.fromCharCode(97 + ((i * 7) % 26)) + i));
many.push(item("p1", "Ethylhexylglycerin"), item("p2", "Ethylhexylgycerin"), item("p3", "Methylparaben"), item("p4", "Ethylparaben"), item("bin", "Niacinamide X", "Niacinamide X", { binned: true }));
const sig = (groups) => JSON.stringify(groups.map((g) => g.items.map((i) => i.id).sort()).sort());

const whole = ingredientLookalikes(many, 0.93);
check(whole.length >= 1 && whole.some((g) => g.items.some((i) => i.id === "p1") && g.items.some((i) => i.id === "p2")), "nearly the same names are found (Ethylhexylglycerin / Ethylhexylgycerin, one letter in 18)");
check(!whole.some((g) => g.items.some((i) => i.id === "p3") && g.items.some((i) => i.id === "p4")), "names 93% alike or less are not listed at 93% (methylparaben / ethylparaben)");
check(!whole.some((g) => g.items.some((i) => i.id === "bin")), "an entry in the Recycle bin is not searched");
equal(lookalikeCount(many), 404, "the count the window shows is the entries that are searched");
function equal(a, b, what) { check(a === b, `${what} (got ${a})`); }

let calls = 0, lastDone = 0, lastTotal = 0;
const sliced = await ingredientLookalikesAsync(many, 0.93, { sliceMs: 1, onProgress: (done, total) => { calls++; lastDone = done; lastTotal = total; } });
check(sig(sliced) === sig(whole), "the sliced search gives the same groups as the whole one");
check(calls > 1, "progress is reported more than once, so a bar can move");
check(lastDone === lastTotal && lastTotal > 0, "the last progress report says everything was checked");

let ticks = 0;
const timer = setInterval(() => { ticks++; }, 1);
await ingredientLookalikesAsync(many, 0.93, { sliceMs: 1 });
clearInterval(timer);
check(ticks > 0, "the page is given back between slices: a timer kept running during the search");

let stop = false, seen = 0;
const cancelled = await ingredientLookalikesAsync(many, 0.93, { sliceMs: 1, onProgress: () => { if (++seen === 2) stop = true; }, cancelled: () => stop });
check(cancelled === null, "a cancelled search answers null and stops");


const js = read("_Admin_Ingredient_Curator/adminIngredientCurator.js");
const cleanup = read("_Shared/adminCleanup.js");
check(/let scanning = false;/.test(js) && /if \(scanning\) \{ notify\("A scan is running already\./.test(js), "a second click while a scan runs is refused");
check(/const scan = showScan\(/.test(js) && js.indexOf("const scan = showScan(") < js.indexOf("await ingredientLookalikesAsync(everyIngredient, 0.93"), "the progress window is shown before the work starts");
check(/scan\.close\(\);\s*scanning = false;/.test(js), "the guard is lifted when the scan ends");
check(/catch \(error\) \{[^}]*scan\.close\(\); scanning = false; notify\("The scan did not finish\. Please try again\."/.test(js), "a failed scan lifts the guard and says so in plain words");
check(/\} finally \{ scanning = false; \}/.test(js), "Find duplicates lifts the guard when it ends or fails");
check(!/ingredientLookalikes\(everyIngredient/.test(js), "the page no longer runs the whole search in one block");
check(/sliderTimer = window\.setTimeout\(draw, 250\)/.test(cleanup), "the slider starts one search when it rests, not one per step");
check(/mine !== drawing/.test(cleanup) && /found === null/.test(cleanup), "an older or cancelled search is dropped");
check(/The search for duplicates did not work\. Close this window and try again\./.test(cleanup) && !/error\.message/.test(cleanup.split("Looking for duplicates failed")[1].split("return;")[0]), "a failed duplicates search shows no raw error");
check(/tab\.disabled = true/.test(cleanup) && /tab\.disabled = false/.test(cleanup), "the controls wait while the search runs");

console.log(`\nscan: ${checks} checks made`);
console.log(failed === 0 ? "ALL PASSED" : `${failed} FAILED`);
process.exit(failed === 0 ? 0 : 1);
