// Checks on the whole catalogue (brands, products, ingredients): what is not make-up, what is there twice, which
// ingredient entries are label text, and the list of changes a clean-up would make.
//
// PURE logic: it only looks at lists of plain objects and answers with lists. It never reads or writes the database,
// so the same code serves three things and they can never disagree:
//   - the read-only report and dry run   (tools/cleanupReport.mjs, run with Node on a saved copy of the data)
//   - "Find duplicates" and "Clean up" on the admin pages (they show these answers and write only what the admin confirms)
//   - the tests                           (tests/catalog.test.mjs)
//
// The objects it expects (extra fields are ignored, missing ones count as empty):
//   brand      { id, name, category, status, scans, source, reviewed, binned, aliases[] }
//   product    { id, name, brandId, brandName, cosmeticType, ingredients[], shades[], status, scans, source, reviewed,
//                binned, barcode, aliases[], addedAt }
//   ingredient { id, name, techName, aliases[], source, reviewed, binned }
import {
    normalizeName, compactName, squashKey, judgeProduct, makeupType, notMakeupWords, barcodeKey, stoplistHit, wrongIngredientShape, MAKEUP_TYPES,
    WEAK_CARE_WORDS
} from "./glowupRules.js";

const list = (value) => (Array.isArray(value) ? value : []);
const text = (value) => (value === null || value === undefined ? "" : String(value));
const live = (items) => list(items).filter((item) => item && item.binned !== true);

/** How alike two names are, from 0 (nothing in common) to 1 (the same): the better of "same letters" and "same words". */
export function similarity(a, b) {
    const ka = normalizeName(a), kb = normalizeName(b);
    if (ka.length === 0 || kb.length === 0) return 0;
    if (ka === kb) return 1;
    const ca = ka.replace(/ /g, ""), cb = kb.replace(/ /g, "");
    if (ca === cb || squashKey(ka) === squashKey(kb)) return 1;
    const letters = 1 - editDistance(ca, cb) / Math.max(ca.length, cb.length);
    const wa = new Set(ka.split(" ")), wb = new Set(kb.split(" "));
    let shared = 0;
    wa.forEach((word) => { if (wb.has(word)) shared++; });
    const words = (2 * shared) / (wa.size + wb.size);
    return Math.max(letters, words);
}

/** The number of single-letter changes that turn one text into the other. */
export function editDistance(a, b) {
    if (a === b) return 0;
    if (a.length === 0) return b.length;
    if (b.length === 0) return a.length;
    let previous = new Array(b.length + 1), current = new Array(b.length + 1);
    for (let j = 0; j <= b.length; j++) previous[j] = j;
    for (let i = 1; i <= a.length; i++) {
        current[0] = i;
        for (let j = 1; j <= b.length; j++) {
            const cost = a.charCodeAt(i - 1) === b.charCodeAt(j - 1) ? 0 : 1;
            current[j] = Math.min(previous[j] + 1, current[j - 1] + 1, previous[j - 1] + cost);
        }
        const swap = previous; previous = current; current = swap;
    }
    return previous[b.length];
}

/**
 * A product name without its brand at the front: "L'Oreal Paris Infallible Matte" of the brand "L'Oreal Paris" gives
 * "Infallible Matte". The name is returned as it is when the brand is not at its front, or nothing would be left.
 */
export function nameWithoutBrand(productName, brandName) {
    const name = text(productName).trim(), brand = compactName(brandName);
    if (brand.length < 2 || name.length === 0) return name;
    // walk through the name word by word until the words read so far spell the brand
    const pieces = name.split(/(\s+)/);                      // words and the spaces between them, in order
    let read = "";
    for (let i = 0; i < pieces.length; i++) {
        if (/^\s*$/.test(pieces[i])) continue;
        read += compactName(pieces[i]);
        if (read.length > brand.length || !brand.startsWith(read)) return name;
        if (read === brand) {
            const rest = pieces.slice(i + 1).join("").replace(/^[\s\-:,.|/]+/, "").trim();
            return normalizeName(rest).length > 0 ? rest : name;
        }
    }
    return name;
}

// ---------------------------------------------------------------------------------------------
//  1. Make-up only
// ---------------------------------------------------------------------------------------------
/**
 * What the make-up rule says about every listed product (the Recycle bin is left out).
 * Answers { makeup[], skincare[], out[], unclear[] }; each entry is { product, judged, properType, sure }:
 *   judged     = the rule's answer with the word that decided it
 *   properType = the app's own word for the kind ("" when none)
 *   sure       = for a product that is not make-up: true when its NAME alone says so with a clear word ("Cleanser",
 *                "Shampoo", "Sunscreen"). false when only the kind stored with it says so, or only a vague word such as
 *                "gel", "cream" or "balm": the stored kind can be wrong (a brow gel filed as "Moisturizer"), so a person
 *                should look before it is removed.
 */
export function scopeReport(products) {
    const report = { makeup: [], skincare: [], out: [], unclear: [] };
    live(products).forEach((product) => {
        const judged = judgeProduct(product.cosmeticType, product.name);
        const byName = judgeProduct("", product.name);
        const sure = (byName.kind === "skincare" || byName.kind === "out") && !WEAK_CARE_WORDS.includes(byName.word);
        report[judged.kind].push({ product, judged, properType: makeupType(product.cosmeticType, product.name), sure: judged.kind === "makeup" || sure });
    });
    return report;
}

/** Make-up products whose kind is not written with one of the app's own words ("Lip Cream" should read "Lipstick"). */
export function typeFixes(products) {
    return scopeReport(products).makeup
        .filter((entry) => entry.properType.length > 0 && entry.properType !== text(entry.product.cosmeticType).trim())
        .map((entry) => ({ product: entry.product, from: text(entry.product.cosmeticType), to: entry.properType }));
}

/**
 * Make-up products whose stored kind deserves a second look, because their own name points somewhere else.
 * Nothing is wrong for certain: a person decides. Answers [{ product, stored, hint, why }]; `hint` = the kind the name
 * reads like ("" when the name only raises a doubt).
 */
export function kindChecks(products) {
    const checks = [];
    scopeReport(products).makeup.forEach((entry) => {
        const product = entry.product;
        const byName = judgeProduct("", product.name), nameType = makeupType("", product.name);
        if (byName.kind === "skincare" || byName.kind === "out")
            checks.push({ product, stored: entry.properType, hint: "", why: `The name could also be ${notMakeupWords(byName)} (the word "${byName.word}"). Check that "${entry.properType || text(product.cosmeticType)}" is right.` });
        else if (nameType && entry.properType && nameType !== entry.properType)
            checks.push({ product, stored: entry.properType, hint: nameType, why: `The name reads like "${nameType}", the stored kind is "${entry.properType}".` });
    });
    return checks;
}

/** Products that carry their brand at the front of their name. Answers [{ product, from, to }]. */
export function nameFixes(products, brands) {
    const brandById = new Map(list(brands).map((brand) => [brand.id, brand]));
    const fixes = [];
    live(products).forEach((product) => {
        const brand = brandById.get(product.brandId);
        const brandName = brand ? brand.name : product.brandName;
        const shorter = nameWithoutBrand(product.name, brandName);
        if (shorter !== text(product.name).trim() && shorter.length >= 3) fixes.push({ product, from: text(product.name), to: shorter });
    });
    return fixes;
}

/**
 * What should happen to each listed brand now that GlowUp is make-up only.
 * Answers [{ brand, makeup, notMakeup, unclear, action, reason, category }]:
 *   action "keep"   - it has make-up (or nothing yet and is a make-up brand); `category` = "Makeup" when that must be written
 *   action "bin"    - none of its products is make-up, or it is not a make-up brand and has no products
 *   action "review" - it only has products the rule cannot place: a person must look
 */
export function brandReport(brands, products) {
    const byBrand = new Map();
    live(products).forEach((product) => {
        if (!byBrand.has(product.brandId)) byBrand.set(product.brandId, { makeup: 0, notMakeup: 0, unclear: 0 });
        const counts = byBrand.get(product.brandId);
        const kind = judgeProduct(product.cosmeticType, product.name).kind;
        if (kind === "makeup") counts.makeup++; else if (kind === "unclear") counts.unclear++; else counts.notMakeup++;
    });
    return live(brands).map((brand) => {
        const counts = byBrand.get(brand.id) || { makeup: 0, notMakeup: 0, unclear: 0 };
        const isMakeupBrand = normalizeName(brand.category) === "makeup" || normalizeName(brand.category) === "make up";
        let action = "keep", reason = "";
        if (counts.makeup > 0) reason = isMakeupBrand ? "" : "It has make-up products: its category becomes Makeup.";
        else if (counts.unclear > 0) { action = "review"; reason = "Its products are of kinds that are not decided yet."; }
        else if (counts.notMakeup > 0) { action = "bin"; reason = "None of its products is make-up."; }
        else if (!isMakeupBrand) { action = "bin"; reason = `It has no make-up products and its category is "${text(brand.category) || "empty"}", not Makeup.`; }
        return Object.assign({ brand, action, reason, category: action === "keep" && !isMakeupBrand ? "Makeup" : "" }, counts);
    });
}

// ---------------------------------------------------------------------------------------------
//  2. The same thing twice
// ---------------------------------------------------------------------------------------------
/**
 * How far an entry is trusted, by where it came from (the order of trust the app uses too):
 *   8 typed on the website   6 the app's starter list, the US FDA list, an import a person approved
 *   5 found by the app (AI lookup, scan) and approved by a person   4 an import nobody looked at twice
 *   2 found by the app and approved by itself   0 found by the app, not checked
 */
export function trustOf(item) {
    const source = text(item.source);
    if (!source || source === "admin") return 8;
    if (source === "starter" || source === "fda") return 6;
    const byTheApp = source === "ai" || source === "scan";
    if (item.reviewed === true && item.autoApproved !== true) return byTheApp ? 5 : 6;
    if (item.reviewed === true) return byTheApp ? 2 : 4;
    return byTheApp ? 0 : 4;
}

/** Which entry of a group is the best one to keep: the most trusted, then with an ingredient list, Active, most scanned, oldest. */
export function pickKeeper(items) {
    const score = (item) => trustOf(item) * 10 + (list(item.ingredients).length > 0 ? 4 : 0) + (text(item.status) === "Active" ? 2 : 0);
    // between equals: the name written as separate words ("Cheek Powder", not "Cheekpowder"), then the older entry
    const words = (item) => normalizeName(item.name).split(" ").length;
    return items.slice().sort((a, b) => score(b) - score(a) || (Number(b.scans) || 0) - (Number(a.scans) || 0) || words(b) - words(a)
        || (Number(a.addedAt) || 0) - (Number(b.addedAt) || 0) || text(a.id).localeCompare(text(b.id)))[0];
}

// puts things that are linked to each other into groups ("a is like b" and "b is like c" make one group of three)
function groupsOf(items, links) {
    const parent = new Map(items.map((item) => [item, item]));
    const find = (item) => { while (parent.get(item) !== item) { parent.set(item, parent.get(parent.get(item))); item = parent.get(item); } return item; };
    links.forEach(([a, b]) => { const ra = find(a), rb = find(b); if (ra !== rb) parent.set(ra, rb); });
    const groups = new Map();
    items.forEach((item) => { const root = find(item); if (!groups.has(root)) groups.set(root, []); groups.get(root).push(item); });
    return Array.from(groups.values()).filter((group) => group.length > 1);
}

/** Brands that are the same brand written differently. Answers [{ reason, items[], keep }]. */
export function brandDuplicates(brands) {
    const items = live(brands), byKey = new Map(), links = [];
    items.forEach((brand) => {
        [brand.name].concat(list(brand.aliases)).forEach((name) => {
            const key = compactName(name);
            if (key.length < 2) return;
            if (byKey.has(key) && byKey.get(key) !== brand) links.push([byKey.get(key), brand]); else byKey.set(key, brand);
        });
    });
    return groupsOf(items, links).map((group) => ({ reason: "The same brand name once capitals, accents and spaces are ignored", items: group, keep: pickKeeper(group) }));
}

/**
 * Products that are in the inventory more than once.
 *   sure     - the same barcode, or the same brand and the same name (capitals, accents, spaces and the brand at the front ignored)
 *   possible - the same brand and a name at least `threshold` alike (0 to 1). These are often real variants (a shade, a size,
 *              a refill): a person decides.
 * Answers { sure: [{ reason, items[], keep }], possible: [{ reason, items[], keep, alike }] }.
 */
export function productDuplicates(products, brands, options) {
    const threshold = options && typeof options.threshold === "number" ? options.threshold : 0.85;
    const items = live(products);
    // one key per brand, the same for brands that are duplicates of each other
    const brandKey = new Map();
    list(brands).forEach((brand) => brandKey.set(brand.id, compactName(brand.name)));
    const keyOfBrand = (product) => brandKey.get(product.brandId) || compactName(product.brandName) || "?";
    const shortName = (product) => nameWithoutBrand(product.name, (list(brands).find((brand) => brand.id === product.brandId) || {}).name || product.brandName);

    const sureLinks = [], reasonOf = new Map();
    const link = (a, b, reason) => { sureLinks.push([a, b]); if (!reasonOf.has(a)) reasonOf.set(a, reason); if (!reasonOf.has(b)) reasonOf.set(b, reason); };

    const byCode = new Map(), byName = new Map(), inBrand = new Map();
    items.forEach((product) => {
        const code = barcodeKey(product.barcode);
        if (code) { if (byCode.has(code)) link(byCode.get(code), product, "The same barcode"); else byCode.set(code, product); }
        const brand = keyOfBrand(product);
        [shortName(product)].concat(list(product.aliases)).forEach((name) => {
            const key = compactName(name);
            if (key.length < 2) return;
            const full = brand + "|" + key;
            if (byName.has(full) && byName.get(full) !== product) link(byName.get(full), product, "The same brand and the same name"); else byName.set(full, product);
        });
        if (!inBrand.has(brand)) inBrand.set(brand, []);
        inBrand.get(brand).push(product);
    });
    const sure = groupsOf(items, sureLinks).map((group) => ({ reason: reasonOf.get(group[0]) || "The same product", items: group, keep: pickKeeper(group) }));
    const settled = new Map();
    sure.forEach((group, index) => group.items.forEach((product) => settled.set(product, index)));

    const possibleLinks = [], alikeOf = new Map();
    inBrand.forEach((mine) => {
        for (let i = 0; i < mine.length; i++)
            for (let j = i + 1; j < mine.length; j++) {
                const a = mine[i], b = mine[j];
                if (settled.has(a) && settled.get(a) === settled.get(b)) continue;          // already one sure group
                const alike = similarity(shortName(a), shortName(b));
                if (alike < threshold) continue;
                possibleLinks.push([a, b]);
                alikeOf.set(a, Math.max(alikeOf.get(a) || 0, alike)); alikeOf.set(b, Math.max(alikeOf.get(b) || 0, alike));
            }
    });
    const possible = groupsOf(items, possibleLinks).map((group) => ({
        reason: "The same brand and nearly the same name", items: group, keep: pickKeeper(group),
        alike: Math.min(...group.map((product) => alikeOf.get(product) || 0))
    }));
    return { sure, possible };
}

/** Ingredient entries that explain the same ingredient (the same name, label name or other spelling). Answers [{ reason, items[], keep, aiCopy }]. */
export function ingredientDuplicates(ingredients) {
    const items = live(ingredients), byKey = new Map(), links = [];
    items.forEach((ingredient) => {
        [ingredient.name, ingredient.techName].concat(list(ingredient.aliases)).forEach((name) => {
            const key = normalizeName(name);
            if (key.length < 2 || key.startsWith("hidden ")) return;
            if (byKey.has(key) && byKey.get(key) !== ingredient) links.push([byKey.get(key), ingredient]); else byKey.set(key, ingredient);
        });
    });
    return groupsOf(items, links).map((group) => {
        const keep = pickKeeper(group);
        return {
            reason: "The same name, label name or other spelling", items: group, keep,
            // the most likely mistake: an entry the AI made although a trusted entry was there already
            aiCopy: group.some((ingredient) => ingredient !== keep && text(ingredient.source) === "ai") && text(keep.source) !== "ai"
        };
    });
}

/**
 * Ingredient entries whose names are at least `threshold` alike (0 to 1) without being the same. These are for a person
 * to look at: chemical names can differ by one letter and still be different ingredients (methylparaben, ethylparaben).
 * Answers [{ reason, items[], keep, alike }].
 */
export function ingredientLookalikes(ingredients, threshold) {
    const limit = typeof threshold === "number" ? threshold : 0.93;
    const items = live(ingredients);
    const same = new Map();                       // entries that are already one sure group are not listed again
    ingredientDuplicates(items).forEach((group, index) => group.items.forEach((ingredient) => same.set(ingredient, index)));
    const namesOf = (ingredient) => [ingredient.name, ingredient.techName].map(normalizeName).filter((key) => key.length >= 3 && !key.startsWith("hidden "));
    const links = [], alikeOf = new Map();
    for (let a = 0; a < items.length; a++)
        for (let b = a + 1; b < items.length; b++) {
            if (same.has(items[a]) && same.get(items[a]) === same.get(items[b])) continue;
            let best = 0;
            namesOf(items[a]).forEach((x) => namesOf(items[b]).forEach((y) => { best = Math.max(best, similarity(x, y)); }));
            if (best < limit) continue;
            links.push([items[a], items[b]]);
            alikeOf.set(items[a], Math.max(alikeOf.get(items[a]) || 0, best)); alikeOf.set(items[b], Math.max(alikeOf.get(items[b]) || 0, best));
        }
    return groupsOf(items, links).map((group) => ({
        reason: "Names that are nearly the same", items: group, keep: pickKeeper(group), alike: Math.min(...group.map((ingredient) => alikeOf.get(ingredient) || 0))
    }));
}

// ---------------------------------------------------------------------------------------------
//  3. Ingredient entries that are not ingredients
// ---------------------------------------------------------------------------------------------
/** What is wrong with an ingredient entry as an ingredient, in plain words ("" = nothing). */
export function ingredientProblem(ingredient) {
    const name = text(ingredient.name).trim(), tech = text(ingredient.techName).trim();
    if (/^hidden\s*:/i.test(tech) || /^hidden in\b/i.test(name)) return "A note about what a label word can hide, not an ingredient.";
    for (const value of [name, tech]) {
        if (value.length === 0) continue;
        if (/\s&\s|\s\+\s|;/.test(value) || (value.includes(",") && !/\d\s*,\s*\d/.test(value))) return `Two or more ingredients joined into one entry ("${value}").`;
        const hit = stoplistHit(value);
        if (hit !== null) return `Label text, not an ingredient: it holds "${hit}".`;
        if (wrongIngredientShape(value)) return "It does not have the shape of an ingredient name (a code, a number, a sentence, or too short or too long).";
    }
    if (name.length === 0 && tech.length === 0) return "It has no name.";
    return "";
}

/** The listed ingredient entries that are not ingredients. Answers [{ ingredient, problem }]. */
export function ingredientIssues(ingredients) {
    return live(ingredients).map((ingredient) => ({ ingredient, problem: ingredientProblem(ingredient) })).filter((entry) => entry.problem.length > 0);
}

/** How many listed products name each ingredient entry in their ingredient list. Answers a Map: ingredient id -> count. */
export function ingredientUse(ingredients, products) {
    const idOfKey = new Map();
    live(ingredients).forEach((ingredient) => [ingredient.name, ingredient.techName].concat(list(ingredient.aliases)).forEach((name) => {
        const key = normalizeName(name);
        if (key.length > 0 && !idOfKey.has(key)) idOfKey.set(key, ingredient.id);
    }));
    const use = new Map();
    live(products).forEach((product) => {
        const seen = new Set();
        list(product.ingredients).forEach((name) => {
            const id = idOfKey.get(normalizeName(name));
            if (id !== undefined && !seen.has(id)) { seen.add(id); use.set(id, (use.get(id) || 0) + 1); }
        });
    });
    return use;
}

// ---------------------------------------------------------------------------------------------
//  4. What a merge writes
// ---------------------------------------------------------------------------------------------
const uniqueNames = (names, notThese) => {
    const seen = new Set(notThese.map(normalizeName)), kept = [];
    names.forEach((name) => { const key = normalizeName(name); if (key.length > 0 && !seen.has(key)) { seen.add(key); kept.push(text(name).trim()); } });
    return kept;
};

/**
 * Merging products into one: the kept product takes the others' names as aliases, their scans, and what it lacks itself
 * (ingredient list, barcode, shades). The others go to the Recycle bin. Nothing is deleted.
 * Answers { keepChanges, bin: [{ item, reason }] }. keepChanges only holds the fields that really change.
 */
export function mergeProducts(keep, others) {
    const changes = {};
    const aliases = uniqueNames(list(keep.aliases).concat(...others.map((other) => [other.name].concat(list(other.aliases)))), [keep.name]);
    if (aliases.length !== list(keep.aliases).length) changes.aliases = aliases.slice(0, 20);
    const scans = others.reduce((sum, other) => sum + (Number(other.scans) || 0), Number(keep.scans) || 0);
    if (scans !== (Number(keep.scans) || 0)) changes.scans = scans;
    if (list(keep.ingredients).length === 0) {
        const donor = others.filter((other) => list(other.ingredients).length > 0).sort((a, b) => list(b.ingredients).length - list(a.ingredients).length)[0];
        if (donor) { changes.ingredients = list(donor.ingredients).slice(); if (list(donor.sources).length > 0) changes.sources = uniqueNames(list(keep.sources).concat(list(donor.sources)), []); }
    }
    if (!barcodeKey(keep.barcode)) { const donor = others.find((other) => barcodeKey(other.barcode)); if (donor) changes.barcode = text(donor.barcode); }
    const shades = Array.from(new Set(list(keep.shades).concat(...others.map((other) => list(other.shades))))).slice(0, 12);
    if (shades.length !== list(keep.shades).length) changes.shades = shades;
    return { keepChanges: changes, bin: others.map((other) => ({ item: other, reason: `The same product as "${text(keep.name)}": merged into it.` })) };
}

/**
 * Merging brands into one: the kept brand takes the others' names as aliases and their scans, the others' products move
 * to it, and the others go to the Recycle bin (without their products, which now belong to the kept brand).
 * Answers { keepChanges, moveProducts: [{ product, brandId, brandName }], bin: [{ item, reason }] }.
 */
export function mergeBrands(keep, others, products) {
    const changes = {};
    const aliases = uniqueNames(list(keep.aliases).concat(...others.map((other) => [other.name].concat(list(other.aliases)))), [keep.name]);
    if (aliases.length !== list(keep.aliases).length) changes.aliases = aliases.slice(0, 20);
    const scans = others.reduce((sum, other) => sum + (Number(other.scans) || 0), Number(keep.scans) || 0);
    if (scans !== (Number(keep.scans) || 0)) changes.scans = scans;
    const gone = new Set(others.map((other) => other.id));
    const moveProducts = list(products).filter((product) => gone.has(product.brandId)).map((product) => ({ product, brandId: keep.id, brandName: text(keep.name) }));
    return { keepChanges: changes, moveProducts, bin: others.map((other) => ({ item: other, reason: `The same brand as "${text(keep.name)}": merged into it.` })) };
}

/**
 * Merging ingredient entries into one: the kept entry takes the others' names as other spellings, so every product
 * list that used one of them is still explained. The others go to the Recycle bin.
 */
export function mergeIngredients(keep, others) {
    const changes = {};
    const aliases = uniqueNames(list(keep.aliases).concat(...others.map((other) => [other.name, other.techName].concat(list(other.aliases)))), [keep.name, keep.techName]);
    if (aliases.length !== list(keep.aliases).length) changes.aliases = aliases.slice(0, 30);
    return { keepChanges: changes, bin: others.map((other) => ({ item: other, reason: `The same ingredient as "${text(keep.name)}": merged into it.` })) };
}

// ---------------------------------------------------------------------------------------------
//  5. The clean-up: every change it would make, as a list (a dry run). Nothing is written here.
// ---------------------------------------------------------------------------------------------
/**
 * The whole clean-up as a list of steps. Each step: { id, title, why, careful, changes[] } (careful = a person should
 * look at every row before it is applied), each change:
 *   { collection, docId, name, brand, action: "bin" | "update", fields, reason }
 * "bin" = move to the Recycle bin (it can be restored; fields.binnedWith = the brand it goes there with, if any);
 * "update" = write `fields`. Nothing is ever deleted.
 *
 * The steps work on copies of the entries and each step sees what the steps before it changed, so the changes can be
 * applied one after the other in this order. `only` (a list of step ids) limits the plan to those steps.
 */
export function cleanupPlan(brands, products, ingredients, options) {
    const only = options && Array.isArray(options.only) ? new Set(options.only) : null;
    const wanted = (id) => only === null || only.has(id);
    const copy = (item) => Object.assign({}, item, { aliases: list(item.aliases).slice() });
    const B = list(brands).map(copy), P = list(products).map(copy), I = list(ingredients).map(copy);
    const brandById = new Map(B.map((brand) => [brand.id, brand]));
    const brandNameOf = (product) => (brandById.get(product.brandId) || {}).name || text(product.brandName);
    const describe = (collection, item) => ({ collection, docId: item.id, name: text(item.name), brand: collection === "products" ? brandNameOf(item) : "" });
    const bin = (collection, item, reason, withBrand) => {
        const change = Object.assign(describe(collection, item), { action: "bin", fields: { binnedWith: withBrand || "" }, reason });
        item.binned = true;
        return change;
    };
    const update = (collection, item, fields, reason) => {
        const change = Object.assign(describe(collection, item), { action: "update", fields, reason });
        Object.assign(item, fields);
        return change;
    };
    const steps = [];
    const step = (id, title, why, make, careful) => { if (wanted(id)) steps.push({ id, title, why, careful: careful === true, changes: make() }); };

    // --- 1. clearly not make-up (the name itself says so)
    const whyNot = (entry) => `Not make-up: ${notMakeupWords(entry.judged) || "another kind of product"} (decided by the word "${entry.judged.word}").`;
    step("notMakeup", "Products that are clearly not make-up go to the Recycle bin",
        "GlowUp holds make-up only. These products say what they are in their own name (cleanser, shampoo, sunscreen, body lotion ...). They are moved to the Recycle bin and can be restored from there.",
        () => { const scope = scopeReport(P); return scope.skincare.concat(scope.out).filter((entry) => entry.sure).map((entry) => bin("products", entry.product, whyNot(entry))); });

    // --- 2. probably not make-up: only the stored kind, or a vague word, says so
    step("notMakeupCheck", "Products that are probably not make-up: please check each one",
        "For these, only the kind stored with the product says they are not make-up, or only a vague word in the name (gel, cream, balm, SPF). The stored kind can be wrong: a brow gel may be filed as \"Moisturizer\". Look at each one: remove it, or give it its right kind so it stays.",
        () => {
            const scope = scopeReport(P);
            return scope.skincare.concat(scope.out).filter((entry) => !entry.sure).map((entry) => bin("products", entry.product,
                whyNot(entry) + ` Its stored kind is "${text(entry.product.cosmeticType) || "(empty)"}".`));
        }, true);

    // --- 3. brands that are left without make-up, and the category of the ones that stay
    step("brands", "Brands without make-up go to the Recycle bin; the others become Makeup brands",
        "The category Skincare is no longer used. A brand that is left without any make-up product goes to the Recycle bin. A brand that has make-up but another category gets the category Makeup.",
        () => {
            const brandPlan = brandReport(B, P);
            return brandPlan.filter((entry) => entry.action === "bin").map((entry) => bin("brands", entry.brand, entry.reason))
                .concat(brandPlan.filter((entry) => entry.action === "keep" && entry.category)
                    .map((entry) => update("brands", entry.brand, { category: entry.category }, `Its category "${text(entry.brand.category) || "(empty)"}" becomes "Makeup".`)));
        });

    // --- 4. the same brand twice
    step("brandDuplicates", "The same brand twice becomes one brand",
        "The better entry is kept, the other's products move to it, and the other entry goes to the Recycle bin.",
        () => {
            const changes = [];
            brandDuplicates(B).forEach((group) => {
                const others = group.items.filter((brand) => brand !== group.keep);
                const merged = mergeBrands(group.keep, others, live(P));
                if (Object.keys(merged.keepChanges).length > 0) changes.push(update("brands", group.keep, merged.keepChanges, "It takes over the names and scans of the entries merged into it."));
                merged.moveProducts.forEach((move) => changes.push(update("products", move.product, { brandId: move.brandId, brandName: move.brandName }, `Its brand entry is merged into "${move.brandName}".`)));
                merged.bin.forEach((gone) => changes.push(bin("brands", gone.item, gone.reason)));
            });
            return changes;
        });

    // --- 5. the same product twice (only the sure ones; "nearly the same" is left for a person)
    step("productDuplicates", "The same product twice becomes one product",
        "The best entry is kept. It takes the other names as aliases, the scans, and an ingredient list or barcode it lacks. The copies go to the Recycle bin.",
        () => {
            const changes = [];
            productDuplicates(P, B, options).sure.forEach((group) => {
                const others = group.items.filter((product) => product !== group.keep);
                const merged = mergeProducts(group.keep, others);
                if (Object.keys(merged.keepChanges).length > 0) changes.push(update("products", group.keep, merged.keepChanges, `${group.reason}: it takes over the names, scans and missing details of the copies.`));
                merged.bin.forEach((gone) => changes.push(bin("products", gone.item, gone.reason)));
            });
            return changes;
        });

    // --- 6. the kind of make-up, in the app's own words
    step("types", "Kinds of make-up are written in the app's own words",
        `The app and the website know these kinds: ${MAKEUP_TYPES.join(", ")}. A kind written another way is changed to the matching word.`,
        () => typeFixes(P).map((fix) => update("products", fix.product, { cosmeticType: fix.to }, `The kind "${fix.from || "(empty)"}" becomes "${fix.to}".`)));

    // --- 7. the brand at the front of a product name
    step("names", "The brand is taken off the front of product names",
        "The brand is stored separately, so a name that repeats it is found less well and reads double in lists. The old name is kept as an alias, so a search for it still finds the product.",
        () => nameFixes(P, B).map((fix) => update("products", fix.product, { name: fix.to, aliases: uniqueNames(list(fix.product.aliases).concat([fix.from]), [fix.to]) },
            `"${fix.from}" becomes "${fix.to}".`)));

    // --- 8. ingredient entries
    step("ingredients", "Ingredient entries that are not ingredients, or are there twice, go to the Recycle bin",
        "Notes, joined names and label text are not ingredients. Of two entries for the same ingredient the better one is kept and takes the other's spellings, so product lists that used them are still explained.",
        () => {
            const changes = ingredientIssues(I).map((entry) => bin("ingredients", entry.ingredient, entry.problem));
            ingredientDuplicates(I).forEach((group) => {
                const others = group.items.filter((ingredient) => ingredient !== group.keep);
                const merged = mergeIngredients(group.keep, others);
                if (Object.keys(merged.keepChanges).length > 0) changes.push(update("ingredients", group.keep, merged.keepChanges, "It takes over the spellings of the entries merged into it."));
                merged.bin.forEach((gone) => changes.push(bin("ingredients", gone.item, gone.reason)));
            });
            return changes;
        });

    // not changed by the clean-up, but a person should look at them
    const after = scopeReport(P);
    return {
        steps,
        forReview: {
            unclear: after.unclear,
            kinds: kindChecks(P),
            brands: brandReport(B, P).filter((entry) => entry.action === "review"),
            possibleDuplicates: productDuplicates(P, B, options).possible
        },
        // what is left when every step has been applied
        after: { brands: live(B).length, products: live(P).length, ingredients: live(I).length, makeup: after.makeup.length }
    };
}

/** How many entries a plan moves to the Recycle bin and how many it changes. */
export function planTotals(plan) {
    const totals = { bin: 0, update: 0, byCollection: { brands: { bin: 0, update: 0 }, products: { bin: 0, update: 0 }, ingredients: { bin: 0, update: 0 } } };
    plan.steps.forEach((step) => step.changes.forEach((change) => { totals[change.action]++; totals.byCollection[change.collection][change.action]++; }));
    return totals;
}

/**
 * Whether the app shows a product, and if not why. THE SAME RULE as the app's: ProductRepository.Usable() and
 * BrandRepository.IsSwitchedOff() (GlowUp/Assets/_Project/Scripts/Data/). Change one, change the other.
 *   - the product is not in the Recycle bin and its status is "Active" (an EMPTY status is NOT Active)
 *   - its brand, when found by brandId, is not in the Recycle bin and its status is "Active" (a brand that is not found
 *     does not hide the product)
 *   - the product is make-up (a kind that is unclear is hidden too)
 * The status is read from rawStatus (what the database holds) when the page keeps it, else from status. normalizeStatus
 * turns an empty status into "Active" for display, which is not what the app does.
 * Answers { live, reason }: reason is "live", "binned", "product-not-active", "not-makeup", "brand-binned",
 * "brand-pending" or "brand-not-active".
 */
export function productVisibility(product, brand) {
    const statusOf = (item) => text(item.rawStatus !== undefined ? item.rawStatus : item.status).trim().toLowerCase();
    if (!product || product.binned === true) return { live: false, reason: "binned" };
    if (statusOf(product) !== "active") return { live: false, reason: "product-not-active" };
    if (judgeProduct(product.cosmeticType, product.name).kind !== "makeup") return { live: false, reason: "not-makeup" };
    if (brand) {
        if (brand.binned === true) return { live: false, reason: "brand-binned" };
        const brandStatus = statusOf(brand);
        if (brandStatus !== "active") return { live: false, reason: brandStatus === "pending" ? "brand-pending" : "brand-not-active" };
    }
    return { live: true, reason: "live" };
}

export const RECOMMENDABILITY_REASON_TEXT = {
    "product-missing": "Product record is missing.",
    "product-name-missing": "Product name is missing.",
    "product-archived": "Product is in the Recycle bin.",
    "product-not-active": "Product is not Active.",
    "brand-missing": "A valid brand record is missing.",
    "brand-archived": "Brand is in the Recycle bin.",
    "brand-inactive": "Brand is not Active.",
    "brand-name-missing": "Brand name is missing.",
    "brand-category-missing": "Brand category is missing.",
    "brand-category-not-makeup": "Brand category is not Makeup.",
    "brand-name-mismatch": "Product brand does not match its linked brand.",
    "category-missing": "Product category is missing.",
    "not-makeup": "Product is not makeup.",
    "ingredients-missing": "Ingredient list is empty.",
    "ingredient-unresolved": "One or more ingredients are not in the Ingredient Curator.",
    "ingredient-not-evaluable": "One or more ingredients are not yet evaluable."
};

function ingredientNameForms(value) {
    const raw = text(value).trim();
    const forms = [raw, raw.replace(/\s*\([^)]*\)/g, " ").trim()];
    const bracketed = raw.match(/\(([^)]*)\)/g) || [];
    bracketed.forEach((part) => forms.push(part.slice(1, -1)));
    return Array.from(new Set(forms.map(normalizeName).filter(Boolean)));
}

export function createRecommendabilityChecker(ingredients) {
    const knownByName = new Map();
    live(ingredients).forEach((ingredient) => {
        const keys = [ingredient.name, ingredient.techName, ...list(ingredient.aliases)].flatMap(ingredientNameForms);
        const usable = ingredient.generic !== true && !(text(ingredient.source).trim().toLowerCase() === "ai" && ingredient.reviewed !== true);
        keys.forEach((key) => {
            const previous = knownByName.get(key);
            if (!previous || (!previous.usable && usable)) knownByName.set(key, { usable });
        });
    });
    const knownNames = Array.from(knownByName.keys());

    const findMatches = (forms) => {
        const exact = forms.map((form) => knownByName.get(form)).filter(Boolean);
        if (exact.length > 0) return exact;
        const close = [];
        forms.forEach((form) => {
            knownNames.forEach((known) => {
                const longest = Math.max(form.length, known.length);
                if (longest < 4 || Math.abs(form.length - known.length) / longest > 0.15) return;
                if (editDistance(form, known) / longest <= 0.15) close.push(knownByName.get(known));
            });
        });
        return close;
    };

    return function recommendationEligibility(product, brand) {
        const reasonCodes = [];
        const add = (code) => { if (!reasonCodes.includes(code)) reasonCodes.push(code); };
        const statusOf = (item) => text(item && (item.rawStatus !== undefined ? item.rawStatus : item.status)).trim().toLowerCase();

        if (!product) return { recommendable: false, reasonCodes: ["product-missing"] };
        if (!text(product.name).trim()) add("product-name-missing");
        if (product.binned === true) add("product-archived");
        if (statusOf(product) !== "active") add("product-not-active");

        if (!brand || !text(product.brandId).trim() || product.brandId !== brand.id || brand.binned === true) {
            add(brand && brand.binned === true ? "brand-archived" : "brand-missing");
        } else {
            if (statusOf(brand) !== "active") add("brand-inactive");
            if (!text(brand.name).trim()) add("brand-name-missing");
            if (!text(brand.category).trim()) add("brand-category-missing");
            else if (normalizeName(brand.category) !== "makeup") add("brand-category-not-makeup");
            const brandNames = [brand.name, ...list(brand.aliases)].map(normalizeName).filter(Boolean);
            if (!text(product.brandName).trim() || !brandNames.includes(normalizeName(product.brandName))) add("brand-name-mismatch");
        }

        if (!text(product.cosmeticType).trim()) add("category-missing");
        else if (judgeProduct(product.cosmeticType, product.name).kind !== "makeup") add("not-makeup");

        const productIngredients = list(product.ingredients);
        if (productIngredients.length === 0) add("ingredients-missing");
        else productIngredients.forEach((ingredient) => {
            const matches = findMatches(ingredientNameForms(ingredient));
            if (matches.length === 0) add("ingredient-unresolved");
            else if (!matches.some((match) => match.usable)) add("ingredient-not-evaluable");
        });

        return { recommendable: reasonCodes.length === 0, reasonCodes };
    };
}
