













import {
    normalizeName, compactName, squashKey, judgeProduct, makeupType, notMakeupWords, barcodeKey, stoplistHit, wrongIngredientShape, MAKEUP_TYPES,
    WEAK_CARE_WORDS
} from "./glowupRules.js";

const list = (value) => (Array.isArray(value) ? value : []);
const text = (value) => (value === null || value === undefined ? "" : String(value));
const live = (items) => list(items).filter((item) => item && item.binned !== true);


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





export function nameWithoutBrand(productName, brandName) {
    const name = text(productName).trim(), brand = compactName(brandName);
    if (brand.length < 2 || name.length === 0) return name;

    const pieces = name.split(/(\s+)/);
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


export function typeFixes(products) {
    return scopeReport(products).makeup
        .filter((entry) => entry.properType.length > 0 && entry.properType !== text(entry.product.cosmeticType).trim())
        .map((entry) => ({ product: entry.product, from: text(entry.product.cosmeticType), to: entry.properType }));
}






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










export function trustOf(item) {
    const source = text(item.source);
    if (!source || source === "admin") return 8;
    if (source === "starter" || source === "fda") return 6;
    const byTheApp = source === "ai" || source === "scan";
    if (item.reviewed === true && item.autoApproved !== true) return byTheApp ? 5 : 6;
    if (item.reviewed === true) return byTheApp ? 2 : 4;
    return byTheApp ? 0 : 4;
}


export function pickKeeper(items) {
    const score = (item) => trustOf(item) * 10 + (list(item.ingredients).length > 0 ? 4 : 0) + (text(item.status) === "Active" ? 2 : 0);

    const words = (item) => normalizeName(item.name).split(" ").length;
    return items.slice().sort((a, b) => score(b) - score(a) || (Number(b.scans) || 0) - (Number(a.scans) || 0) || words(b) - words(a)
        || (Number(a.addedAt) || 0) - (Number(b.addedAt) || 0) || text(a.id).localeCompare(text(b.id)))[0];
}







export function pickIngredientKeeper(items) {
    const key = (value) => normalizeName(text(value).replace(/\s*\([^)]*\)\s*/g, " "));
    const inci = new Set(), everyday = new Set();
    items.forEach((item) => {
        const parts = text(item.techName).split(/\s+\/\s+/).map((part) => part.trim()).filter(Boolean);
        if (parts.length > 0) inci.add(key(parts[0]));
        parts.slice(1).forEach((part) => everyday.add(key(part)));
        text(item.techName).replace(/\(([^)]+)\)/g, (match, inner) => { everyday.add(normalizeName(inner)); return match; });
    });
    const evidence = (item) => { const k = normalizeName(item.name); return everyday.has(k) && !inci.has(k) ? -1 : inci.has(k) ? 1 : 0; };

    const trusted = Math.max(...items.map(trustOf));
    const equals = items.filter((item) => trustOf(item) === trusted);
    const best = Math.max(...equals.map(evidence));
    return pickKeeper(equals.filter((item) => evidence(item) === best));
}



export const markedDifferent = (a, b) => list(a.differentFrom).includes(b.id) || list(b.differentFrom).includes(a.id);

function groupsOf(items, links) {
    const parent = new Map(items.map((item) => [item, item]));
    const find = (item) => { while (parent.get(item) !== item) { parent.set(item, parent.get(parent.get(item))); item = parent.get(item); } return item; };
    links.forEach(([a, b]) => { const ra = find(a), rb = find(b); if (ra !== rb) parent.set(ra, rb); });
    const groups = new Map();
    items.forEach((item) => { const root = find(item); if (!groups.has(root)) groups.set(root, []); groups.get(root).push(item); });
    return Array.from(groups.values()).filter((group) => group.length > 1);
}


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








export function productDuplicates(products, brands, options) {
    const threshold = options && typeof options.threshold === "number" ? options.threshold : 0.85;
    const items = live(products);

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
                if (settled.has(a) && settled.get(a) === settled.get(b)) continue;
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


export function ingredientDuplicates(ingredients) {
    const items = live(ingredients), byKey = new Map(), links = [];
    items.forEach((ingredient) => {
        [ingredient.name, ingredient.techName].concat(list(ingredient.aliases)).forEach((name) => {
            const key = normalizeName(name);
            if (key.length < 2 || key.startsWith("hidden ")) return;
            if (byKey.has(key) && byKey.get(key) !== ingredient) { if (!markedDifferent(byKey.get(key), ingredient)) links.push([byKey.get(key), ingredient]); } else byKey.set(key, ingredient);
        });
    });
    return groupsOf(items, links).map((group) => {
        const keep = pickIngredientKeeper(group);
        return {
            reason: "The same name, label name or other spelling", items: group, keep,

            aiCopy: group.some((ingredient) => ingredient !== keep && text(ingredient.source) === "ai") && text(keep.source) !== "ai"
        };
    });
}






export function ingredientLookalikes(ingredients, threshold) {
    const scan = lookalikeScan(ingredients, threshold);
    while (!scan.next().done) {                                                                                      }
    return scan.result();
}






export async function ingredientLookalikesAsync(ingredients, threshold, options) {
    const { onProgress, cancelled, sliceMs } = Object.assign({ sliceMs: 12 }, options || {});
    const scan = lookalikeScan(ingredients, threshold);
    for (;;) {
        const started = Date.now();
        let step;
        do { step = scan.next(); } while (!step.done && Date.now() - started < sliceMs);
        if (step.done) break;
        if (onProgress) onProgress(step.value.done, step.value.total);
        await new Promise((resolve) => setTimeout(resolve, 0));
        if (cancelled && cancelled()) return null;
    }
    if (onProgress) onProgress(1, 1);
    return scan.result();
}


export const lookalikeCount = (ingredients) => live(ingredients).length;






function alikeAtLeast(x, y, limit) {
    if (x.k === y.k || x.c === y.c || x.sq === y.sq) return 1;
    let best = 0;
    const longest = Math.max(x.c.length, y.c.length);
    if (1 - Math.abs(x.c.length - y.c.length) / longest >= limit) best = 1 - editDistance(x.c, y.c) / longest;
    let shared = 0;
    x.w.forEach((word) => { if (y.w.has(word)) shared++; });
    best = Math.max(best, (2 * shared) / (x.w.size + y.w.size));
    return best >= limit ? best : 0;
}


const prepared = (key) => ({ k: key, c: key.replace(/ /g, ""), sq: squashKey(key), w: new Set(key.split(" ")) });


function lookalikeScan(ingredients, threshold) {
    const limit = typeof threshold === "number" ? threshold : 0.93;
    const items = live(ingredients);
    const same = new Map();
    ingredientDuplicates(items).forEach((group, index) => group.items.forEach((ingredient) => same.set(ingredient, index)));
    const namesOf = (ingredient) => [ingredient.name, ingredient.techName].map(normalizeName).filter((key) => key.length >= 3 && !key.startsWith("hidden "));
    const names = items.map((ingredient) => namesOf(ingredient).map(prepared));
    const links = [], alikeOf = new Map();
    let finished = null;
    function* run() {
        for (let a = 0; a < items.length; a++) {
            for (let b = a + 1; b < items.length; b++) {
                if (same.has(items[a]) && same.get(items[a]) === same.get(items[b])) continue;
                let best = 0;
                names[a].forEach((x) => names[b].forEach((y) => { best = Math.max(best, alikeAtLeast(x, y, limit)); }));
                if (best < limit || markedDifferent(items[a], items[b])) continue;
                links.push([items[a], items[b]]);
                alikeOf.set(items[a], Math.max(alikeOf.get(items[a]) || 0, best)); alikeOf.set(items[b], Math.max(alikeOf.get(items[b]) || 0, best));
            }
            yield { done: a + 1, total: items.length };
        }
        finished = groupsOf(items, links).map((group) => ({
            reason: "Names that are nearly the same", items: group, keep: pickIngredientKeeper(group), alike: Math.min(...group.map((ingredient) => alikeOf.get(ingredient) || 0))
        }));
    }
    const generator = run();
    return { next: () => generator.next(), result: () => finished || [] };
}





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


export function ingredientIssues(ingredients) {
    return live(ingredients).map((ingredient) => ({ ingredient, problem: ingredientProblem(ingredient) })).filter((entry) => entry.problem.length > 0);
}


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




const uniqueNames = (names, notThese) => {
    const seen = new Set(notThese.map(normalizeName)), kept = [];
    names.forEach((name) => { const key = normalizeName(name); if (key.length > 0 && !seen.has(key)) { seen.add(key); kept.push(text(name).trim()); } });
    return kept;
};






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
    const shades = Array.from(new Set(list(keep.shades).concat(...others.map((other) => list(other.shades))))).slice(0, 100);
    if (shades.length !== list(keep.shades).length) changes.shades = shades;
    return { keepChanges: changes, bin: others.map((other) => ({ item: other, reason: `The same product as "${text(keep.name)}": merged into it.` })) };
}






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





export function mergeIngredients(keep, others) {
    const changes = {};
    const base = (value) => text(value).replace(/\s*\([^)]*\)\s*/g, " ").replace(/\s+/g, " ").trim();
    const tidy = (value) => {
        const brackets = [];
        text(value).replace(/\(([^)]+)\)/g, (match, inner) => { brackets.push(inner.trim()); return match; });
        return normalizeName(base(value)) === normalizeName(keep.name) && brackets.length > 0 ? brackets : [value];
    };
    const aliases = uniqueNames(list(keep.aliases).concat(...others.map((other) => [other.name, other.techName].concat(list(other.aliases)).flatMap(tidy))), [keep.name, keep.techName, base(keep.techName)]);
    if (aliases.length !== list(keep.aliases).length) changes.aliases = aliases.slice(0, 30);
    return { keepChanges: changes, bin: others.map((other) => ({ item: other, reason: `The same ingredient as "${text(keep.name)}": merged into it.` })) };
}











export const DEFINITION_FIXES = [
    { name: "D&C Red 21 Al Lake / CI 45380", wrong: /candelilla/i, marker: "it is the text of Candelilla Wax",
      to: "A red colorant (an aluminium lake of the dye eosin, also called D&C Red 21) that gives products a bright pink to red colour." },
    { name: "TRIETHANOLAMINE", wrong: /synthetic polymer used to thicken/i, marker: "it is the text of Acrylates/C10-30 Alkyl Acrylate Crosspolymer",
      to: "A pH adjuster that balances the acidity of a formula and helps some thickening polymers work." },
    { name: "TITANIUM DIOXIDE", wrong: /metallic element/i, marker: "it is the text of Aluminum",
      to: "A white mineral pigment that makes products opaque and lightens colours. It is also used as a sunscreen filter." }
];


export function definitionFixes(ingredients) {
    const out = [];
    list(ingredients).forEach((ingredient) => {
        if (!ingredient) return;
        const fix = DEFINITION_FIXES.find((entry) => normalizeName(entry.name) === normalizeName(ingredient.name) && entry.wrong.test(text(ingredient.description)));
        if (fix) out.push({ ingredient, to: fix.to, marker: fix.marker });
    });
    return out;
}





const JOINER = /\s+\/\s+/;
const withoutBrackets = (value) => text(value).replace(/\s*\([^)]*\)\s*/g, " ").replace(/\s+/g, " ").trim();
const bracketed = (value) => { const found = []; text(value).replace(/\(([^)]+)\)/g, (m, inner) => { found.push(inner.trim()); return m; }); return found; };
const splitJoined = (value) => text(value).split(JOINER).map((part) => part.trim()).filter((part) => part.length > 0);











export function splitJoinedName(ingredient, classifications) {
    const nameParts = splitJoined(ingredient.name);
    if (nameParts.length < 2 || nameParts.some((part) => part.replace(/[^a-z0-9]/gi, "").length < 3)) return null;
    const kinds = list(classifications);
    const kindOf = (word) => kinds.find((kind) => normalizeName(kind.name) === normalizeName(word));
    const techParts = splitJoined(ingredient.techName);
    let inci = "", guess = false;
    if (techParts.length === 1 && withoutBrackets(techParts[0]).length >= 3) inci = withoutBrackets(techParts[0]);
    else {
        const pool = techParts.length > 1 ? techParts : nameParts;
        inci = pool.find((part) => !kindOf(part)) || pool[0];
        guess = techParts.length > 1 || techParts.length === 0 ? true : false;
        inci = withoutBrackets(inci) || inci;
    }
    const everyName = nameParts.concat(techParts, bracketed(ingredient.techName), ...list(ingredient.aliases).map(splitJoined));
    let classificationId = "";
    const names = [];
    everyName.forEach((part) => {
        const kind = kindOf(part);
        if (kind && normalizeName(part) !== normalizeName(inci)) { if (!text(ingredient.classificationId) && !classificationId) classificationId = kind.id; return; }
        names.push(part);
    });
    const aliases = uniqueNames(names, [inci]).slice(0, 30);
    const result = { name: inci, techName: inci, aliases, guess, from: text(ingredient.name) };
    if (classificationId) result.classificationId = classificationId;
    return result;
}






export function friendlyNameFixes(ingredients) {
    const out = [];
    live(ingredients).forEach((ingredient) => {
        const name = text(ingredient.name).trim(), tech = text(ingredient.techName).trim();
        if (!name || !tech || JOINER.test(name) || JOINER.test(tech)) return;
        const inci = withoutBrackets(tech);
        if (inci.replace(/[^a-z0-9]/gi, "").length < 3 || normalizeName(inci) === normalizeName(withoutBrackets(name))) return;
        const aliases = uniqueNames(list(ingredient.aliases).concat([name], bracketed(tech)), [inci]).slice(0, 30);
        out.push({ ingredient, to: { name: inci, techName: inci, aliases, from: name } });
    });
    return out;
}


export function joinedNameFixes(ingredients, classifications) {
    const out = [];
    live(ingredients).forEach((ingredient) => {
        const to = splitJoinedName(ingredient, classifications);
        if (to) out.push({ ingredient, to });
    });
    return out;
}


const SAFETY_SENTENCE = /\b(is|are)\s+(completely\s+|very\s+|generally\s+|considered\s+|widely\s+|fully\s+)?safe\b|\bsafe\s+(for|to)\s+(cosmetic|use|skin|all|most|everyday|daily|sensitive)|non-?toxic|\b(is|are|and|completely|totally)\s+harmless\b|\bnot harmful\b/i;

const SAFETY_ADJECTIVE = /\b(a|an|the)\s+(safe|non-?toxic|harmless),?\s+/i;





export function withoutSafetyClaims(description) {
    const original = text(description).trim();
    const removed = [];
    const kept = [];
    original.split(/(?<=[.!?])\s+/).forEach((sentence) => {

        const aboutTheProduct = /\b(keeps?|keeping|helps?\s+keep|ensur\w*|remain\w*|stay\w*|protects?)\b[^.]{0,60}\bsafe\b|harmless\s+heat/i.test(sentence);
        if (!aboutTheProduct && SAFETY_SENTENCE.test(sentence)) { removed.push(sentence.trim()); return; }
        const adjective = sentence.match(SAFETY_ADJECTIVE);
        if (adjective) {
            removed.push(adjective[0].trim());
            kept.push(sentence.replace(SAFETY_ADJECTIVE, (match, article) => article[0].toUpperCase() === article[0] ? article + " " : article + " "));
            return;
        }
        kept.push(sentence);
    });
    const to = kept.join(" ").replace(/\s+/g, " ").trim();
    if (removed.length === 0 || to.length < 40 || to === original) return null;
    return { to, removed };
}


export function safetyClaimFixes(ingredients) {
    const out = [];
    live(ingredients).forEach((ingredient) => {
        const fix = withoutSafetyClaims(ingredient.description);
        if (fix) out.push({ ingredient, to: fix.to, removed: fix.removed });
    });
    return out;
}











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


    const whyNot = (entry) => `Not make-up: ${notMakeupWords(entry.judged) || "another kind of product"} (decided by the word "${entry.judged.word}").`;
    step("notMakeup", "Products that are clearly not make-up go to the Recycle bin",
        "GlowUp holds make-up only. These products say what they are in their own name (cleanser, shampoo, sunscreen, body lotion ...). They are moved to the Recycle bin and can be restored from there.",
        () => { const scope = scopeReport(P); return scope.skincare.concat(scope.out).filter((entry) => entry.sure).map((entry) => bin("products", entry.product, whyNot(entry))); });


    step("notMakeupCheck", "Products that are probably not make-up: please check each one",
        "For these, only the kind stored with the product says they are not make-up, or only a vague word in the name (gel, cream, balm, SPF). The stored kind can be wrong: a brow gel may be filed as \"Moisturizer\". Look at each one: remove it, or give it its right kind so it stays.",
        () => {
            const scope = scopeReport(P);
            return scope.skincare.concat(scope.out).filter((entry) => !entry.sure).map((entry) => bin("products", entry.product,
                whyNot(entry) + ` Its stored kind is "${text(entry.product.cosmeticType) || "(empty)"}".`));
        }, true);


    step("brands", "Brands without make-up go to the Recycle bin; the others become Makeup brands",
        "The category Skincare is no longer used. A brand that is left without any make-up product goes to the Recycle bin. A brand that has make-up but another category gets the category Makeup.",
        () => {
            const brandPlan = brandReport(B, P);
            return brandPlan.filter((entry) => entry.action === "bin").map((entry) => bin("brands", entry.brand, entry.reason))
                .concat(brandPlan.filter((entry) => entry.action === "keep" && entry.category)
                    .map((entry) => update("brands", entry.brand, { category: entry.category }, `Its category "${text(entry.brand.category) || "(empty)"}" becomes "Makeup".`)));
        });


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


    step("types", "Kinds of make-up are written in the app's own words",
        `The app and the website know these kinds: ${MAKEUP_TYPES.join(", ")}. A kind written another way is changed to the matching word.`,
        () => typeFixes(P).map((fix) => update("products", fix.product, { cosmeticType: fix.to }, `The kind "${fix.from || "(empty)"}" becomes "${fix.to}".`)));


    step("names", "The brand is taken off the front of product names",
        "The brand is stored separately, so a name that repeats it is found less well and reads double in lists. The old name is kept as an alias, so a search for it still finds the product.",
        () => nameFixes(P, B).map((fix) => update("products", fix.product, { name: fix.to, aliases: uniqueNames(list(fix.product.aliases).concat([fix.from]), [fix.to]) },
            `"${fix.from}" becomes "${fix.to}".`)));


    step("definitions", "Definitions that describe another ingredient",
        "Each of these entries carries the description of a different ingredient (a copy that was never changed). The new text is a short plain-words definition written for the step; read it, correct it if you know better, and tick the rows you accept. Nothing is ticked at first.",
        () => definitionFixes(I).map((fix) => update("ingredients", fix.ingredient, { description: fix.to }, `Its definition describes another ingredient (${fix.marker}). It becomes: "${fix.to}"`)), true);


    step("markdown", "Stray ** marks are taken out of definitions",
        "Some definitions were pasted with ** around words (a way of writing bold text). The app and the website show text as it is, so the users see the stars. Only the stars are taken out.",
        () => live(I).filter((ingredient) => /\*\*/.test(text(ingredient.description)))
            .map((ingredient) => update("ingredients", ingredient, { description: text(ingredient.description).replace(/\*\*/g, "") }, "The ** marks around words are taken out.")));


    step("safetyClaims", "Sentences that call an ingredient safe",
        "GlowUp never counts an entry nobody has checked as proof that an ingredient is safe, so a definition must not say it is. Only the claim is taken out (\"It is safe for cosmetic use.\", \"A safe, mineral-based pigment\" becomes \"A mineral-based pigment\"). \"Helps keep products safe from bacteria\" is about the product and stays. Nothing is ticked at first.",
        () => safetyClaimFixes(I).map((fix) => update("ingredients", fix.ingredient, { description: fix.to }, `Takes out: ${fix.removed.map((r) => `"${r}"`).join(", ")}`)), true);



    step("joinedNames", "Joined names are split: the INCI name stays, the other names become Also known as",
        "Many entries were typed as \"INCI name / everyday name\" in the one name box (\"Aqua / Water\", \"Dimethicone / Silicone Oil\"). The name box holds only the INCI name; the other names go to Also known as, so every spelling still finds the ingredient. A name that is a kind of ingredient (\"Solvent\") becomes the classification when the entry has none. Where the entry gave no single label name, the first name was taken: the row says so, please check those. Nothing is ticked at first.",
        () => joinedNameFixes(I, options && options.classifications).map((fix) => {
            const fields = { name: fix.to.name, techName: fix.to.techName, aliases: fix.to.aliases };
            if (fix.to.classificationId) fields.classificationId = fix.to.classificationId;
            const aliasLine = fix.to.aliases.length > 0 ? ` Also known as: ${fix.to.aliases.join(", ")}.` : "";
            return update("ingredients", fix.ingredient, fields, `"${fix.to.from}" becomes "${fix.to.name}".${aliasLine}${fix.to.guess ? " (The first name was taken as the INCI name: check it.)" : ""}${fix.to.classificationId ? " The kind of ingredient is set from the name." : ""}`);
        }), true);


    step("friendlyNames", "Everyday names are moved to Also known as, and the INCI name takes their place",
        "These entries carry an everyday name in the name box (\"Water\", \"Avocado Oil\") while their own label name is another one (\"AQUA (WATER)\", \"Persea Gratissima Oil\"). The name box holds the INCI name, so the label name becomes the name and the everyday name an alias: a scan of either finds the ingredient. The label name is only a good INCI name if it was typed right, so look at each row. Where an entry for the INCI name exists already, the duplicates step merges them. Nothing is ticked at first.",
        () => friendlyNameFixes(I).map((fix) => update("ingredients", fix.ingredient, { name: fix.to.name, techName: fix.to.techName, aliases: fix.to.aliases },
            `"${fix.to.from}" is an everyday name: "${fix.to.name}" becomes the name, and "${fix.to.from}" is kept under Also known as.`)), true);


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


    const after = scopeReport(P);
    return {
        steps,
        forReview: {
            unclear: after.unclear,
            kinds: kindChecks(P),
            brands: brandReport(B, P).filter((entry) => entry.action === "review"),
            possibleDuplicates: productDuplicates(P, B, options).possible
        },

        after: { brands: live(B).length, products: live(P).length, ingredients: live(I).length, makeup: after.makeup.length }
    };
}


export function planTotals(plan) {
    const totals = { bin: 0, update: 0, byCollection: { brands: { bin: 0, update: 0 }, products: { bin: 0, update: 0 }, ingredients: { bin: 0, update: 0 } } };
    plan.steps.forEach((step) => step.changes.forEach((change) => { totals[change.action]++; totals.byCollection[change.collection][change.action]++; }));
    return totals;
}













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
