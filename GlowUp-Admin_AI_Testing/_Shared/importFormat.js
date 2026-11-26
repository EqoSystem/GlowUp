










import { MAKEUP_TYPES } from "./glowupRules.js";

export const KEYS = {
    BRAND: ["BRAND_NAME", "OTHER_NAMES", "MARKET", "OFFICIAL_WEBSITE", "WEBSITE_EVIDENCE", "LOGO_LINK", "PICTURE_LINK"],
    PRODUCT: ["BRAND_NAME", "PRODUCT_NAME", "OTHER_NAMES", "COSMETIC_TYPE", "DESCRIPTION", "SHADES", "MINIMUM_AGE", "BARCODE", "PRODUCT_PHOTO_LINK", "FDA_PH_LINK", "FDA_REGISTRATION_NUMBER", "INGREDIENTS", "SOURCES"],
    INGREDIENT: ["NAME", "LABEL_NAME", "OTHER_NAMES", "DESCRIPTION", "TECHNICAL_DESCRIPTION", "CAUTION_NOTE", "COMMON_USES", "MINIMUM_AGE", "RISK_LEVEL",
        "ALLERGEN_FAMILY", "AVOID_WITH_CONDITIONS", "GOOD_FOR_CONDITIONS", "AVOID_FOR_SKIN_TYPES", "GOOD_FOR_SKIN_TYPES", "HIGHLIGHTS", "SOURCES"]
};
const ALL_KEYS = Array.from(new Set([].concat(KEYS.BRAND, KEYS.PRODUCT, KEYS.INGREDIENT)));
const MARKETS = ["Global", "Local", "Asia", "Europe", "North America"];
const SKIN_TYPES = ["Oily", "Normal", "Dry", "Mix", "Sensitive"];
const RISKS = ["safe", "caution", "harmful"];
const NOTHING = new Set(["", "n/a", "na", "none", "null", "-", "--", "unknown", "not found", "not available", "not stated"]);

const squeeze = (value) => String(value == null ? "" : value).replace(/\s+/g, " ").trim();

const value = (raw, most) => { const t = squeeze(raw); return NOTHING.has(t.toLowerCase()) ? "" : t.slice(0, most); };

const listOf = (raw, length, most) => {
    const t = squeeze(raw);
    if (NOTHING.has(t.toLowerCase())) return [];
    const items = t.includes("|") ? t.split("|") : t.split(/;/);
    return items.map((item) => value(item, length)).filter(Boolean).filter((item, i, all) => all.findIndex((x) => x.toLowerCase() === item.toLowerCase()) === i).slice(0, most);
};
const httpsUrl = (raw) => {
    const t = squeeze(raw).replace(/[),.;]+$/, "");
    try { const u = new URL(t); return u.protocol === "https:" && !u.username && !u.password && u.hostname.includes(".") ? u.href : ""; } catch (error) { return ""; }
};
const httpsLinks = (raw, most) => listOf(raw, 500, most * 2).map(httpsUrl).filter((u, i, all) => u && all.indexOf(u) === i).slice(0, most);
const age = (raw) => { const n = Number(squeeze(raw)); return Number.isInteger(n) && n > 0 && n < 100 ? n : 0; };


const fdaNumberOf = (raw) => { const t = value(raw, 40); return /^[A-Za-z0-9][A-Za-z0-9 .,#()_\-\/]{0,39}$/.test(t) ? t : ""; };


export function hexOf(raw) {
    let color = squeeze(raw).toUpperCase();
    const rgb = color.match(/^RGB\(\s*(\d{1,3})\s*[, ]\s*(\d{1,3})\s*[, ]\s*(\d{1,3})\s*\)$/);
    if (rgb) return [1, 2, 3].every((i) => Number(rgb[i]) <= 255) ? "#" + [1, 2, 3].map((i) => Number(rgb[i]).toString(16).padStart(2, "0")).join("").toUpperCase() : "";
    if (color && color[0] !== "#") {
        if (!/^[0-9A-F]{6}$/.test(color) || !/[A-F]/.test(color)) return "";
        color = "#" + color;
    }
    if (/^#[0-9A-F]{3}$/.test(color)) {
        if (/^#[0-9]{3}$/.test(color)) return "";
        color = "#" + color[1] + color[1] + color[2] + color[2] + color[3] + color[3];
    }
    return /^#[0-9A-F]{6}$/.test(color) ? color : "";
}







export function shadeCodeOf(raw) {
    const code = squeeze(raw).toUpperCase().replace(/^(?:SHADE\s+|NO\.\s*|NO\s+|#\s*)/, "").trim();
    return NOTHING.has(code.toLowerCase()) || !/^[A-Z0-9][A-Z0-9 .\-\/]{0,15}$/.test(code) ? "" : code;
}






export function pairShades(rawShades, rawCodes, most = 24) {
    const shades = [], codes = [];
    const list = Array.isArray(rawShades) ? rawShades : [], codeList = Array.isArray(rawCodes) ? rawCodes : [];
    list.forEach((raw, index) => {
        const hex = hexOf(raw);
        if (!hex) return;
        const code = shadeCodeOf(codeList[index]);
        const at = shades.indexOf(hex);
        if (at >= 0) { if (!codes[at]) codes[at] = code; return; }
        if (shades.length < most) { shades.push(hex); codes.push(code); }
    });
    return { shades, codes };
}


function homePage(raw) {
    const url = httpsUrl(raw);
    if (!url) return "";
    const host = new URL(url).hostname.toLowerCase().replace(/^www\./, "");
    return host === "vertexaisearch.cloud.google.com" || host.endsWith("duckduckgo.com") ? "" : "https://" + new URL(url).host + "/";
}


function splitBlocks(text) {
    const records = [];
    const marks = [...text.matchAll(/={2,}\s*(BRAND|PRODUCT|INGREDIENT)\s*={2,}/gi)];
    marks.forEach((mark, index) => {
        const body = text.slice(mark.index + mark[0].length, index + 1 < marks.length ? marks[index + 1].index : text.length);
        records.push({ type: mark[1].toUpperCase(), fields: fieldsOf(body, KEYS[mark[1].toUpperCase()]) });
    });
    return records;
}


function fieldsOf(body, keys) {
    const fields = {};
    const found = [...body.matchAll(new RegExp("(?:^|\\s)(" + keys.join("|") + ")\\s*:", "g"))];
    found.forEach((hit, index) => {
        const start = hit.index + hit[0].length;
        const end = index + 1 < found.length ? found[index + 1].index : body.length;
        if (!(hit[1] in fields)) fields[hit[1]] = body.slice(start, end);
    });
    return fields;
}

function csvRows(csv) {
    const rows = [];
    let row = [], cell = "", quoted = false;
    for (let i = 0; i < csv.length; i++) {
        const c = csv[i];
        if (quoted) { if (c === '"') { if (csv[i + 1] === '"') { cell += '"'; i++; } else quoted = false; } else cell += c; }
        else if (c === '"') quoted = true;
        else if (c === ",") { row.push(cell); cell = ""; }
        else if (c === "\n" || c === "\r") { if (c === "\r" && csv[i + 1] === "\n") i++; row.push(cell); rows.push(row); row = []; cell = ""; }
        else cell += c;
    }
    row.push(cell); rows.push(row);
    return rows.filter((r) => r.some((x) => squeeze(x)));
}


function sheetRecords(text) {
    const records = [];
    text.split(/^SHEET:[^\n]*\n/m).forEach((csv) => {
        const rows = csvRows(csv);
        if (rows.length < 2) return;
        const header = rows[0].map((h) => squeeze(h).toUpperCase());
        const has = (k) => header.includes(k);
        const type = has("LABEL_NAME") || (has("NAME") && !has("BRAND_NAME")) ? "INGREDIENT" : has("PRODUCT_NAME") ? "PRODUCT" : has("BRAND_NAME") ? "BRAND" : "";
        if (!type) return;
        rows.slice(1).forEach((row) => {
            const fields = {};
            header.forEach((key, i) => { if (KEYS[type].includes(key) && !(key in fields)) fields[key] = row[i] || ""; });
            records.push({ type, fields });
        });
    });
    return records;
}


function product(f) {
    const kind = value(f.COSMETIC_TYPE, 60);
    const fda = httpsLinks(f.FDA_PH_LINK, 1).find((u) => /(^|\.)fda\.gov\.ph$/i.test(new URL(u).hostname)) || "";
    const sources = httpsLinks(f.SOURCES, 10);
    return {
        brandName: value(f.BRAND_NAME, 60), productName: value(f.PRODUCT_NAME, 100),
        cosmeticType: MAKEUP_TYPES.find((t) => t.toLowerCase() === kind.toLowerCase()) || kind,
        description: value(f.DESCRIPTION, 400),
        ingredients: listOf(f.INGREDIENTS, 100, 80),
        shades: listOf(f.SHADES, 40, 100).map(hexOf).filter((s, i, all) => s && all.indexOf(s) === i).slice(0, 100),
        fdaVerifiedLink: fda,
        fdaNumber: fdaNumberOf(f.FDA_REGISTRATION_NUMBER),
        sources: Array.from(new Set(sources.concat(fda ? [fda] : []))).slice(0, 10),
        productAliases: listOf(f.OTHER_NAMES, 100, 6), minAge: age(f.MINIMUM_AGE),
        barcode: squeeze(f.BARCODE).replace(/[^0-9]/g, "").slice(0, 14),
        imageUrl: httpsLinks(f.PRODUCT_PHOTO_LINK, 1)[0] || "",
        brandMarket: "", brandAliases: [], brandWebsiteSuggested: "", brandLogoUrl: "", brandImageUrl: ""
    };
}
function brand(f) {
    const market = value(f.MARKET, 20);
    return {
        name: value(f.BRAND_NAME, 60), aliases: listOf(f.OTHER_NAMES, 60, 6), market: MARKETS.find((m) => m.toLowerCase() === market.toLowerCase()) || "",
        websiteSuggested: homePage(f.OFFICIAL_WEBSITE), logoUrl: httpsLinks(f.LOGO_LINK, 1)[0] || "", imageUrl: httpsLinks(f.PICTURE_LINK, 1)[0] || ""
    };
}
function ingredient(f) {
    const name = value(f.NAME, 80) || value(f.LABEL_NAME, 80);
    const risk = value(f.RISK_LEVEL, 20).toLowerCase();
    const pick = (raw, allowed) => listOf(raw, 20, 8).map((x) => allowed.find((a) => a.toLowerCase() === x.toLowerCase())).filter(Boolean);
    return {
        name, techName: value(f.LABEL_NAME, 120) || name, aliases: listOf(f.OTHER_NAMES, 80, 6), description: value(f.DESCRIPTION, 300),
        riskLevel: RISKS.includes(risk) ? risk : "", cautionNote: value(f.CAUTION_NOTE, 120).replace(/^it\s+/i, "").replace(/\.$/, ""),
        chemicalDescription: value(f.TECHNICAL_DESCRIPTION, 400), commonUses: value(f.COMMON_USES, 120), minAge: age(f.MINIMUM_AGE),
        allergens: listOf(f.ALLERGEN_FAMILY, 40, 8), avoidConditions: listOf(f.AVOID_WITH_CONDITIONS, 40, 8), goodConditions: listOf(f.GOOD_FOR_CONDITIONS, 40, 8),
        avoidSkinTypes: pick(f.AVOID_FOR_SKIN_TYPES, SKIN_TYPES), goodSkinTypes: pick(f.GOOD_FOR_SKIN_TYPES, SKIN_TYPES),
        highlights: listOf(f.HIGHLIGHTS, 40, 8), sources: httpsLinks(f.SOURCES, 10)
    };
}





export function parseImportText(text) {
    const source = String(text || "");
    const records = /={2,}\s*(BRAND|PRODUCT|INGREDIENT)\s*={2,}/i.test(source) ? splitBlocks(source) : sheetRecords(source);
    if (records.length === 0) return { kind: "", notes: [] };
    const hasIngredients = records.some((r) => r.type === "INGREDIENT"), hasInventory = records.some((r) => r.type !== "INGREDIENT");
    if (hasIngredients && hasInventory) return { kind: "mixed", notes: [], counts: { ingredients: records.filter((r) => r.type === "INGREDIENT").length, inventory: records.filter((r) => r.type !== "INGREDIENT").length } };
    const notes = [];
    if (hasIngredients) {
        const seen = new Set();
        const ingredients = [];
        records.forEach((r, i) => {
            const item = ingredient(r.fields);
            if (item.name.length < 2 || !/[a-z0-9]/i.test(item.name)) { notes.push(`Ingredient record ${i + 1} has no name: left out.`); return; }
            const key = item.name.toLowerCase();
            if (seen.has(key)) { notes.push(`"${item.name}" appears twice in the file: the first one is used.`); return; }
            seen.add(key); ingredients.push(item);
        });
        return { kind: "ingredients", draft: { ingredients }, notes };
    }
    const products = [], brands = [], seenProducts = new Set(), seenBrands = new Set();
    records.forEach((r, i) => {
        if (r.type === "BRAND") {
            const item = brand(r.fields);
            if (item.name.length < 2) { notes.push(`Brand record ${i + 1} has no name: left out.`); return; }
            if (seenBrands.has(item.name.toLowerCase())) { notes.push(`The brand "${item.name}" appears twice: the first one is used.`); return; }
            seenBrands.add(item.name.toLowerCase()); brands.push(item);
        } else {
            const item = product(r.fields);
            if (item.brandName.length < 2 || item.productName.length < 1) { notes.push(`Product record ${i + 1} has no brand or no product name: left out.`); return; }
            const key = item.brandName.toLowerCase() + "|" + item.productName.toLowerCase();
            if (seenProducts.has(key)) { notes.push(`"${item.productName}" of ${item.brandName} appears twice: the first one is used.`); return; }
            seenProducts.add(key); products.push(item);
        }
    });

    products.forEach((p) => {
        const b = brands.find((x) => x.name.toLowerCase() === p.brandName.toLowerCase());
        if (b) { p.brandMarket = b.market; p.brandAliases = b.aliases; p.brandWebsiteSuggested = b.websiteSuggested; p.brandLogoUrl = b.logoUrl; p.brandImageUrl = b.imageUrl; }
    });
    return { kind: "inventory", draft: { products, brands }, notes };
}





export function ingredientsNotHere(count) {
    const what = count > 0 ? `${count} ingredient record${count === 1 ? "" : "s"}` : "Ingredient records";
    return `${what} (what an ingredient is, its risk, classification or which skin it suits) cannot be extracted here. `
        + "Brand & Product Inventory only reads brands and products. Ingredient details live in the Ingredient Curator: there each one is matched against the ingredient list so it is not saved twice, gets a classification, and is checked by an admin before the app uses it to warn users. "
        + "Read here, an ingredient could be saved as a product, or an unchecked safety note could reach the app. "
        + "Put the ingredient part in a file of its own and import it in the Ingredient Curator.";
}


export function inventoryNotHere(count) {
    const what = count > 0 ? `${count} brand or product record${count === 1 ? "" : "s"}` : "Brand and product records";
    return `${what} cannot be extracted here. The Ingredient Curator only reads ingredients; brands and products are checked for duplicates, make-up only and FDA links in Brand & Product Inventory. `
        + "Put that part in a file of its own and import it there.";
}
