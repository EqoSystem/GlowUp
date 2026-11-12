










import { readFileSync, writeFileSync, existsSync } from "node:fs";
import { fileURLToPath, pathToFileURL } from "node:url";
import path from "node:path";

const here = path.dirname(fileURLToPath(import.meta.url));
const load = (file) => import(pathToFileURL(path.join(here, "..", "_Shared", file)).href);
let rules, catalog;
try { rules = await load("glowupRules.js"); catalog = await load("glowupCatalog.js"); }
catch (error) { console.error("The website's rules could not be loaded (Node 22.7 or newer is needed).\n" + error.message); process.exit(1); }

const folder = process.argv[2] ? path.resolve(process.argv[2]) : "";
if (!folder || !existsSync(path.join(folder, "products.json"))) {
    console.error("Give the backup folder, for example:  node tools/cleanupReport.mjs ../GlowUp-Data_Backup_2026-10-12");
    process.exit(1);
}
const threshold = process.argv[3] ? Number(process.argv[3]) : 0.85;




const read = (name) => JSON.parse(readFileSync(path.join(folder, name + ".json"), "utf8"));
const array = (value) => (Array.isArray(value) ? value.map((v) => String(v).trim()).filter((v) => v.length > 0) : []);
const seconds = (entry) => { const time = entry.createdAt && entry.createdAt["@time"] ? Date.parse(entry.createdAt["@time"]) / 1000 : Number(entry.addedAtUnix) || Date.parse(entry["@created"] || "") / 1000; return Number.isFinite(time) ? Math.floor(time) : 0; };
const status = (value) => { const s = String(value || "").trim(); return s ? s[0].toUpperCase() + s.slice(1).toLowerCase() : "Active"; };

const brands = read("brands").map((d) => ({
    id: d["@id"], name: d.name || "", category: d.category || "", market: d.market || "", status: status(d.status), scans: Number(d.scans) || 0,
    source: String(d.source || "").toLowerCase(), reviewed: d.reviewed === true, autoApproved: d.autoApproved === true, binned: d.binned === true, aliases: array(d.aliases), addedAt: seconds(d)
}));
const products = read("products").map((d) => ({
    id: d["@id"], name: d.name || d.productName || "", brandId: d.brandId || "", brandName: d.brandName || "", cosmeticType: d.cosmeticType || d.category || "",
    ingredients: array(d.ingredients), shades: array(d.shades), status: status(d.status), scans: Number(d.scans) || 0, source: String(d.source || "").toLowerCase(),
    sources: array(d.sources), reviewed: d.reviewed === true, autoApproved: d.autoApproved === true, binned: d.binned === true, barcode: String(d.barcode || ""), aliases: array(d.aliases), addedAt: seconds(d)
}));
const ingredients = read("ingredients").map((d) => ({
    id: d["@id"], name: d.name || "", techName: d.techName || "", aliases: array(d.aliases), source: String(d.source || "").toLowerCase(),
    reviewed: d.reviewed === true, autoApproved: d.autoApproved === true, binned: d.binned === true, addedAt: seconds(d)
}));




const plan = catalog.cleanupPlan(brands, products, ingredients, { threshold });
const totals = catalog.planTotals(plan);
const listed = { brands: brands.filter((b) => !b.binned).length, products: products.filter((p) => !p.binned).length, ingredients: ingredients.filter((i) => !i.binned).length };
const scope = catalog.scopeReport(products);
const use = catalog.ingredientUse(ingredients, products);
const brandNames = new Map(brands.map((b) => [b.id, b.name]));
const brandOf = (p) => brandNames.get(p.brandId) || p.brandName || "";

const withList = scope.makeup.filter((e) => e.product.ingredients.length > 0);
const facts = {
    makeupWithoutList: scope.makeup.length - withList.length,
    makeupWithList: withList.length,
    activeMakeupWithList: withList.filter((e) => e.product.status === "Active").length,
    colourantBlocks: products.filter((p) => !p.binned && p.ingredients.some((name) => /\+\s*\/\s*-|may contain/i.test(name))).map((p) => ({ id: p.id, name: p.name, brand: brandOf(p) })),
    listItemsThatAreNotIngredients: []
};
products.filter((p) => !p.binned).forEach((p) => p.ingredients.forEach((name) => {
    const hit = rules.stoplistHit(name);
    if (hit !== null || rules.wrongIngredientShape(name)) facts.listItemsThatAreNotIngredients.push({ id: p.id, product: p.name, brand: brandOf(p), item: name, why: hit !== null ? `holds "${hit}"` : "not the shape of an ingredient name" });
}));


const unclearGroups = new Map();
plan.forReview.unclear.forEach((entry) => {
    const word = entry.judged.word || "(nothing recognised in the name or the kind)";
    if (!unclearGroups.has(word)) unclearGroups.set(word, []);
    unclearGroups.get(word).push(entry.product);
});




const dryRun = {
    madeAt: new Date().toISOString(), from: folder, note: "A DRY RUN: nothing was changed. Each change is what the clean-up would write, in this order.",
    before: listed, after: plan.after, totals,
    steps: plan.steps.map((step) => ({ id: step.id, title: step.title, changes: step.changes })),
    forReview: {
        unclear: plan.forReview.unclear.map((e) => ({ id: e.product.id, name: e.product.name, brand: brandOf(e.product), kind: e.product.cosmeticType, word: e.judged.word })),
        kinds: plan.forReview.kinds.map((e) => ({ id: e.product.id, name: e.product.name, brand: brandOf(e.product), stored: e.stored, hint: e.hint, why: e.why })),
        brands: plan.forReview.brands.map((e) => ({ id: e.brand.id, name: e.brand.name, reason: e.reason })),
        possibleDuplicates: plan.forReview.possibleDuplicates.map((g) => ({ alike: Number(g.alike.toFixed(2)), items: g.items.map((p) => ({ id: p.id, name: p.name, brand: brandOf(p) })) }))
    }
};
writeFileSync(path.join(folder, "cleanup-dry-run.json"), JSON.stringify(dryRun, null, 1), "utf8");




const esc = (value) => String(value === null || value === undefined ? "" : value).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
const many = (n, one, more) => `${n} ${n === 1 ? one : more}`;
const WHAT = { bin: "to the Recycle bin", update: "changed" };
const KIND = { brands: "Brand", products: "Product", ingredients: "Ingredient" };
const fieldsText = (change) => (change.action === "bin" ? "" : Object.entries(change.fields).map(([key, value]) => `${key} = ${Array.isArray(value) ? "[" + value.join(", ") + "]" : value}`).join("; "));

function table(head, rows) {
    return `<div class="scroll"><table><thead><tr>${head.map((h) => `<th>${esc(h)}</th>`).join("")}</tr></thead><tbody>${rows.map((row) => `<tr>${row.map((cell) => `<td>${cell}</td>`).join("")}</tr>`).join("")}</tbody></table></div>`;
}
function fold(summary, body, open) { return `<details${open ? " open" : ""}><summary>${summary}</summary>${body}</details>`; }

const stepHtml = plan.steps.map((step, index) => {
    const bins = step.changes.filter((c) => c.action === "bin").length, updates = step.changes.length - bins;
    const counts = step.changes.length === 0 ? "nothing to do" : [bins > 0 ? many(bins, "entry", "entries") + " to the Recycle bin" : "", updates > 0 ? many(updates, "entry", "entries") + " changed" : ""].filter((t) => t).join(", ");
    const rows = step.changes.map((c) => [esc(KIND[c.collection]), `<b>${esc(c.name || "(no name)")}</b>${c.brand ? `<br><span class="muted">${esc(c.brand)}</span>` : ""}`,
        `<span class="tag ${c.action}">${WHAT[c.action]}</span>${c.action === "update" ? `<br><span class="muted small">${esc(fieldsText(c))}</span>` : ""}`, esc(c.reason), `<code>${esc(c.docId)}</code>`]);
    return `<section><h2><span class="num">${index + 1}</span>${esc(step.title)}</h2><p>${esc(step.why)}</p><p class="count">${esc(counts)}</p>${
        step.changes.length > 0 ? fold(`Show the ${many(step.changes.length, "change", "changes")}`, table(["Kind", "Name", "What happens", "Why", "Document id"], rows), step.changes.length <= 12) : ""}</section>`;
}).join("");

const unclearHtml = Array.from(unclearGroups.entries()).sort((a, b) => b[1].length - a[1].length).map(([word, items]) =>
    fold(`<b>${esc(word)}</b>: ${many(items.length, "product", "products")}`, table(["Product", "Brand", "Kind in the database", "Status"],
        items.map((p) => [esc(p.name), esc(brandOf(p)), esc(p.cosmeticType || "(empty)"), esc(p.status)])), false)).join("");

const possibleHtml = plan.forReview.possibleDuplicates.length === 0 ? "<p>None.</p>" : table(["How alike", "Brand", "The names"],
    plan.forReview.possibleDuplicates.sort((a, b) => b.alike - a.alike).map((g) => [Math.round(g.alike * 100) + "%", esc(brandOf(g.items[0])), g.items.map((p) => `${esc(p.name)} <span class="muted small">(${many(p.ingredients.length, "ingredient", "ingredients")}, ${many(p.scans, "scan", "scans")})</span>`).join("<br>")]));

const tile = (value, label) => `<div class="tile"><div class="value">${esc(value)}</div><div class="label">${esc(label)}</div></div>`;
const page = `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>GlowUp data clean-up: report and dry run</title>
<style>
  :root { --bg: #fbf7f5; --card: #ffffff; --ink: #2b2226; --muted: #6f6368; --line: #eadfe2; --pink: #c2476b; --gold: #a57a2b; --soft: #f7e9ee; }
  @media (prefers-color-scheme: dark) { :root { --bg: #1d1719; --card: #272023; --ink: #f3e9ec; --muted: #b9aab0; --line: #3b3035; --pink: #ee8fab; --gold: #d9b56a; --soft: #33262b; } }
  * { box-sizing: border-box; }
  body { margin: 0; background: var(--bg); color: var(--ink); font: 16px/1.55 system-ui, "Segoe UI", Roboto, sans-serif; }
  main { max-width: 1060px; margin: 0 auto; padding: 28px 16px 60px; }
  h1 { font-size: 30px; line-height: 1.2; margin: 0 0 6px; }
  h2 { font-size: 21px; margin: 0 0 8px; display: flex; align-items: center; gap: 10px; }
  h3 { font-size: 17px; margin: 22px 0 6px; }
  .num { flex: none; width: 30px; height: 30px; border-radius: 50%; background: var(--pink); color: #fff; font-size: 16px; display: inline-flex; align-items: center; justify-content: center; }
  .lead { color: var(--muted); margin: 0 0 18px; }
  .notice { background: var(--soft); border-left: 4px solid var(--pink); padding: 12px 16px; border-radius: 8px; margin: 16px 0 22px; }
  section { background: var(--card); border: 1px solid var(--line); border-radius: 14px; padding: 18px 20px; margin: 16px 0; }
  .tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 12px; margin: 14px 0; }
  .tile { background: var(--card); border: 1px solid var(--line); border-radius: 12px; padding: 12px 14px; }
  .tile .value { font-size: 26px; font-weight: 650; }
  .tile .label { color: var(--muted); font-size: 14px; }
  .count { font-weight: 600; color: var(--gold); margin: 6px 0; }
  .muted { color: var(--muted); } .small { font-size: 13px; }
  details { margin: 8px 0; } summary { cursor: pointer; color: var(--pink); font-weight: 600; padding: 4px 0; }
  .scroll { overflow-x: auto; }
  table { border-collapse: collapse; width: 100%; font-size: 14px; margin-top: 6px; }
  th, td { text-align: left; vertical-align: top; padding: 7px 10px; border-bottom: 1px solid var(--line); }
  th { color: var(--muted); font-weight: 600; white-space: nowrap; }
  code { font-size: 12px; color: var(--muted); word-break: break-all; }
  .tag { display: inline-block; padding: 1px 9px; border-radius: 999px; font-size: 13px; border: 1px solid var(--line); white-space: nowrap; }
  .tag.bin { border-color: var(--pink); color: var(--pink); } .tag.update { border-color: var(--gold); color: var(--gold); }
  ol li, ul li { margin: 4px 0; }
</style></head><body><main>
<h1>GlowUp data clean-up: report and dry run</h1>
<p class="lead">Made on ${esc(new Date().toLocaleString("en-GB", { dateStyle: "long", timeStyle: "short" }))} from the saved copy in <code>${esc(folder)}</code>.</p>
<div class="notice"><b>Nothing has been changed.</b> This page shows what the clean-up <i>would</i> do, so you can check it first.
Nothing is ever deleted: an entry that is removed goes to the Recycle bin of the admin website and can be restored there.</div>

<h3>In short</h3>
<div class="tiles">
  ${tile(`${listed.products} → ${plan.after.products}`, "products listed, now → after")}
  ${tile(`${listed.brands} → ${plan.after.brands}`, "brands listed, now → after")}
  ${tile(`${listed.ingredients} → ${plan.after.ingredients}`, "ingredient entries, now → after")}
  ${tile(totals.bin, "entries to the Recycle bin")}
  ${tile(totals.update, "entries changed")}
  ${tile(plan.forReview.unclear.length, "products a person must decide on")}
</div>

${stepHtml}

<section><h2>A person must decide these (the clean-up does not touch them)</h2>
<h3>Products the rule cannot place: ${plan.forReview.unclear.length}</h3>
<p>Some are care and colour at once (a tinted lip balm, a tinted lip oil), which no source places clearly. The others have a name and a kind that say nothing clear. The app does not show or recommend any of them until they are decided. They are grouped by the word that made them unclear.</p>
${unclearHtml || "<p>None.</p>"}
<h3>Make-up whose stored kind deserves a second look: ${plan.forReview.kinds.length}</h3>
<p>The kind stored with a product was often filled in by an import, not by a person, and some are wrong (a lip liner filed as "Lipstick", a tinted sunscreen base filed as "Lip Tint"). The kind decides where AR Try-On puts the colour. Nothing is changed for these: an admin corrects the ones that are wrong.</p>
${plan.forReview.kinds.length === 0 ? "<p>None.</p>" : fold("Show them", table(["Product", "Brand", "Stored kind", "The name reads like", "Why it is listed"],
    plan.forReview.kinds.map((e) => [esc(e.product.name), esc(brandOf(e.product)), esc(e.stored), esc(e.hint || "not sure"), esc(e.why)])), false)}
<h3>Brands that only have such products: ${plan.forReview.brands.length}</h3>
${plan.forReview.brands.length === 0 ? "<p>None.</p>" : table(["Brand", "Why"], plan.forReview.brands.map((e) => [esc(e.brand.name), esc(e.reason)]))}
<h3>Products with nearly the same name in one brand: ${plan.forReview.possibleDuplicates.length} groups</h3>
<p>These are often real variants (a shade, a size, a refill, a waterproof version). They are only listed. "Find duplicates" on the website lets an admin merge the ones that really are the same.</p>
${fold("Show the groups", possibleHtml, false)}
</section>

<section><h2>Good to know (not changed by the clean-up)</h2>
<ul>
  <li><b>${facts.makeupWithoutList}</b> make-up products have <b>no ingredient list</b>; ${facts.makeupWithList} have one (${facts.activeMakeupWithList} of them Active). A product without a list can be recognised by its name, but the app cannot check or recommend it until a list is added.</li>
  <li><b>${facts.colourantBlocks.length}</b> ingredient lists hold a "may contain" or "+/-" colourant block mixed into the main list${facts.colourantBlocks.length > 0 ? ": " + facts.colourantBlocks.map((p) => esc(p.brand + " " + p.name)).join("; ") : ""}.</li>
  <li><b>${facts.listItemsThatAreNotIngredients.length}</b> items inside product ingredient lists are label text, not ingredients.${facts.listItemsThatAreNotIngredients.length > 0 ? fold("Show them", table(["Product", "Item in its list", "Why"], facts.listItemsThatAreNotIngredients.map((f) => [esc(f.brand + " " + f.product), esc(f.item), esc(f.why)])), false) : ""}</li>
  <li>Ingredient entries used by no product list: <b>${ingredients.filter((i) => !i.binned && !use.has(i.id)).length}</b> of ${listed.ingredients} (they still explain ingredients the camera reads from a pack).</li>
</ul></section>

<section><h2>How the clean-up is applied</h2>
<ol>
  <li>Read this report. Tell us which steps you approve, and decide the unclear kinds.</li>
  <li>The backup this report was made from stays in its folder. Keep it until you are happy with the result.</li>
  <li>An admin opens Brand &amp; Product Inventory on the website, chooses <b>Clean up</b>, sees the same steps with the same numbers, ticks the approved ones and confirms. The website then writes the changes in small groups and says how many it made.</li>
  <li>Anything that went to the Recycle bin by mistake is brought back with <b>Restore</b>.</li>
</ol>
<p class="muted small">The exact list of changes, in order, is in <code>cleanup-dry-run.json</code> next to this file.</p></section>
</main></body></html>`;
writeFileSync(path.join(folder, "cleanup-report.html"), page, "utf8");




console.log(`From ${folder}`);
console.log(`Listed now:   ${listed.brands} brands, ${listed.products} products, ${listed.ingredients} ingredient entries`);
console.log(`After:        ${plan.after.brands} brands, ${plan.after.products} products (${plan.after.makeup} make-up), ${plan.after.ingredients} ingredient entries`);
plan.steps.forEach((step, index) => {
    const bins = step.changes.filter((c) => c.action === "bin").length;
    console.log(`  ${index + 1}. ${step.title}: ${bins} to the Recycle bin, ${step.changes.length - bins} changed`);
});
console.log(`For a person: ${plan.forReview.unclear.length} unclear products, ${plan.forReview.kinds.length} kinds worth a look, ${plan.forReview.brands.length} brands, ${plan.forReview.possibleDuplicates.length} groups of nearly the same name`);
console.log(`Make-up with an ingredient list: ${facts.makeupWithList} of ${scope.makeup.length} (${facts.activeMakeupWithList} Active)`);
console.log("Written: cleanup-report.html and cleanup-dry-run.json. NOTHING WAS CHANGED in the database.");
