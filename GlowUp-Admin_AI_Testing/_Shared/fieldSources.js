// "Find it yourself" for the Ingredient form: for each box, which dictionaries/databases hold its value, what to look for
// there, and what to do with it. PURE logic (no page, no network) so tests/fieldSources.test.mjs can check it.
//
// The work stays by hand: the admin opens a source, finds the value, and types or pastes it. Nothing here fetches,
// scrapes or fills a box. The paste helpers only tidy text the admin pastes (spaces, line breaks, list separators).

const google = (host, name, extra) => `https://www.google.com/search?q=${encodeURIComponent("site:" + host + " " + name + (extra || ""))}`;

/**
 * Every source. `url(name)` = the link opened; `deep` = that link is built from the INCI name (the page already shows the
 * search for it), so the name need not be copied. `ref(name)` = the page to keep in Reference links: the ingredient's own results page where the link is that, else the site's home ("" = none).
 */
export const SOURCES = {
    cosing: { label: "EU CosIng", deep: true, url: (name) => google("ec.europa.eu", name, " cosing"), ref: () => "https://ec.europa.eu/growth/tools-databases/cosing/" },
    incipedia: { label: "INCIpedia", deep: false, url: () => "https://incipedia.personalcarecouncil.org/", ref: () => "https://incipedia.personalcarecouncil.org/" },
    cosmile: { label: "COSMILE Europe", deep: true, url: (name) => `https://cosmileeurope.eu/inci/results/?q=${encodeURIComponent(name)}`, ref: (name) => SOURCES.cosmile.url(name) },
    cir: { label: "Cosmetic Ingredient Review", deep: true, url: (name) => google("cir-safety.org", name), ref: () => "https://www.cir-safety.org/" },
    pubchem: { label: "PubChem (NIH)", deep: true, url: (name) => `https://pubchem.ncbi.nlm.nih.gov/#query=${encodeURIComponent(name)}`, ref: (name) => SOURCES.pubchem.url(name) },
    cosmeticsinfo: { label: "CosmeticsInfo.org", deep: true, url: (name) => google("cosmeticsinfo.org", name), ref: () => "https://www.cosmeticsinfo.org/" },
    echa: { label: "ECHA C&L Inventory (EU)", deep: false, url: () => "https://echa.europa.eu/information-on-chemicals/cl-inventory-database", ref: () => "https://echa.europa.eu/information-on-chemicals/cl-inventory-database" },
    fragrance: { label: "EU fragrance allergen list", deep: true, url: () => "https://single-market-economy.ec.europa.eu/sectors/cosmetics/cosmetic-products-specific-topics/fragrance-allergens-labelling_en", ref: () => "https://single-market-economy.ec.europa.eu/sectors/cosmetics/cosmetic-products-specific-topics/fragrance-allergens-labelling_en" },
    checker: { label: "CosIng Checker (unofficial)", deep: true, url: (name) => `https://www.google.com/search?q=${encodeURIComponent("CosIng checker " + name)}`, ref: () => "" }
};

const ADMIN_JUDGMENT = "Admin judgment. Add your evidence in Reference links.";

/** Per box: `sources` = [source id, what to look for there], `helper` = one line under the guide. */
export const FIELD_GUIDE = {
    name: {
        sources: [
            ["cosing", "Search the name, open the entry, copy \"INCI name\"."],
            ["incipedia", "Search, copy the INCI name (sign-in required)."],
            ["cosmile", "Search, copy the INCI name as shown."]
        ]
    },
    classification: {
        title: "Choose from the dropdown, do not paste",
        sources: [
            ["cosing", "Check the \"Restriction\" field. Empty = no EU restriction listed. Otherwise note the Annex."],
            ["cir", "Open the ingredient's report, read the conclusion (safe as used / safe with qualifications / insufficient data / unsafe)."],
            ["pubchem", "Open the \"Classification\" section for the chemical family."]
        ]
    },
    purpose: {
        sources: [
            ["cosing", "Copy the \"Function\" value(s). These are short terms like PRESERVATIVE or HUMECTANT, not sentences."],
            ["cosmile", "Read the ingredient's function text."]
        ],
        helper: "The source gives a function term. Write one plain line from it, e.g. PRESERVATIVE -> Helps prevent microbial growth and keep the product safe."
    },
    description: {
        sources: [
            ["cosmile", "Read the plain-language description."],
            ["cosmeticsinfo", "Read the plain-language description."],
            ["cir", "Read the plain-language description."]
        ],
        helper: "Rewrite in your own words. Do not paste text from these sites (copyright)."
    },
    chemicalDescription: {
        sources: [
            ["cosing", "Copy \"Chem/IUPAC Name\"."],
            ["pubchem", "Copy the description from the compound page (Names and Identifiers)."]
        ]
    },
    aliases: {
        sources: [
            ["cosing", "Copy the INN name, Ph. Eur. name, Chem/IUPAC name, CAS and EC numbers."],
            ["pubchem", "Copy from the \"Synonyms\" list."],
            ["cosmile", "Copy the common names."],
            ["incipedia", "Copy the synonyms."]
        ]
    },
    sources: {
        sources: [],
        helper: "Copy the page address from the browser address bar of the page you used, or use the \"+ ref\" buttons next to the links above."
    },
    cautionNote: {
        sources: [
            ["cir", "Copy any qualification or limit from the conclusion."],
            ["cosing", "Check \"Restriction\" for conditions and warnings."]
        ]
    },
    commonUses: {
        sources: [
            ["cosmile", "Copy the product types listed for the ingredient."],
            ["cosmeticsinfo", "Copy the product types listed for the ingredient."]
        ]
    },
    allergens: {
        sources: [
            ["cosing", "Annex III entries: check whether the ingredient appears as a fragrance allergen or in another family; pick the matching chip."],
            ["checker", "Same check; pick the matching chip."]
        ]
    },
    frequentAllergy: {
        title: "Optional. Leave empty when no source lists a risk",
        sources: [
            ["echa", "Search the name or CAS number (see Also Known As). If the classification lists H317, the official statement is \"May cause an allergic skin reaction\"."],
            ["pubchem", "Open the compound page, then Safety and Hazards, GHS Classification. Look for H317 (the same statement, easier to search by name)."],
            ["fragrance", "For a fragrance ingredient: check whether it is on the EU list of fragrance allergens (named on labels because it can cause contact allergy)."]
        ],
        helper: "Write one plain sentence from what the source says, e.g. \"May cause an allergic skin reaction in some people.\" The EU classification is for the ingredient itself; the chance of a reaction in a finished product depends on the amount. Keep the page in Reference links."
    },
    judgment: { sources: [], helper: ADMIN_JUDGMENT }
};

/** The links of one box for an INCI name: [{ id, label, url, ref, deep, lookFor }]. Every link is https. */
export function linksFor(field, name) {
    const guide = FIELD_GUIDE[field];
    const clean = cleanLine(name);
    if (!guide || clean.length < 2) return [];
    return guide.sources.map(([id, lookFor]) => ({ id, label: SOURCES[id].label, url: SOURCES[id].url(clean), ref: SOURCES[id].ref(clean), deep: SOURCES[id].deep, lookFor }));
}

// ----------------------------------------------------------------------------------------------------------------
// Reference links: "Label | https://address", one per line
// ----------------------------------------------------------------------------------------------------------------
const urlKey = (url) => String(url || "").trim().replace(/\/+$/, "").toLowerCase();
const addressOf = (line) => { const at = String(line).lastIndexOf(" | "); return (at >= 0 ? String(line).slice(at + 3) : String(line)).trim(); };

/**
 * Adds "Label | url" to the text of the Reference links box. Never overwrites: every line already there stays, in order.
 * A page already listed (same address, a trailing slash or capitals do not matter) is not added again.
 * Returns { text, added, reason } with reason "" | "exists" | "bad" | "full".
 */
export function addReference(current, label, url, maxLength = 900) {
    const text = String(current || "");
    const address = String(url || "").trim();
    if (!/^https:\/\/[^\s"'<>|]+$/i.test(address) || !String(label || "").trim()) return { text, added: false, reason: "bad" };
    const lines = text.split(/\r?\n/).map((l) => l.trim()).filter(Boolean);
    if (lines.some((l) => urlKey(addressOf(l)) === urlKey(address))) return { text, added: false, reason: "exists" };
    const next = lines.concat(`${String(label).replace(/\s*\|\s*/g, " ").trim()} | ${address}`).join("\n");
    if (next.length > maxLength) return { text, added: false, reason: "full" };
    return { text: next, added: true, reason: "" };
}

// ----------------------------------------------------------------------------------------------------------------
// Paste handling
// ----------------------------------------------------------------------------------------------------------------
/** Trim, and every run of spaces and line breaks becomes one space. */
export const cleanLine = (text) => String(text == null ? "" : text).replace(/\s+/g, " ").trim();

/** Two or more letters and no lower-case letter: a raw CosIng function term such as "PRESERVATIVE" or "HUMECTANT, SKIN CONDITIONING". */
export function looksLikeFunctionTerm(text) {
    const value = String(text || "");
    return /[A-Z].*[A-Z]/.test(value) && value === value.toUpperCase();
}

/** A list pasted with commas, semicolons or line breaks -> "a, b, c": each item trimmed, no empty item, no repeat (capitals do not matter). */
export function normalizeNameList(text) {
    const seen = new Set(), kept = [];
    String(text || "").split(/[,;\r\n]+/).forEach((part) => {
        const item = cleanLine(part), key = item.toLowerCase();
        if (item && !seen.has(key)) { seen.add(key); kept.push(item); }
    });
    return kept.join(", ");
}

export const LARGE_PASTE = 300;
export const NOTES = {
    functionTerm: "Looks like a function term. Turn it into a plain sentence.",
    largePaste: "Large paste detected. Please rewrite in your own words.",
    inciList: "This box takes one official name. Put other names under Also Known As (a \" / \" or a comma means more than one name)."
};

/**
 * What a paste into a box does. `kind` = "purpose" | "description" | "chemical" | "aliases" | "name".
 * Returns { text, note } : the text to insert (the pasted part only) and a non-blocking notice ("" = none).
 */
export function pasteResult(kind, pasted) {
    const text = kind === "aliases" ? normalizeNameList(pasted) : cleanLine(pasted);
    let note = "";
    if (kind === "purpose" && looksLikeFunctionTerm(text)) note = NOTES.functionTerm;
    else if (kind === "description" && String(pasted || "").length > LARGE_PASTE) note = NOTES.largePaste;
    else if (kind === "allergy" && String(pasted || "").length > LARGE_PASTE) note = NOTES.largePaste;
    else if (kind === "name" && (text.includes(" / ") || text.includes(","))) note = NOTES.inciList;
    return { text, note };
}

/**
 * Puts pasted text into a box value at the caret. Returns { value, caret, note }.
 * Pasted line breaks and runs of spaces become single spaces; one space is kept at a pasted edge when the neighbouring text
 * would otherwise be glued to it. A list (Also Known As) is normalised as a whole. Text past `maxLength` is cut.
 */
export function insertPaste(kind, value, start, end, pasted, maxLength = 0) {
    const before = String(value).slice(0, start), after = String(value).slice(end);
    const result = pasteResult(kind, pasted);
    let text = result.text;
    if (kind !== "aliases" && text) {
        if (/^\s/.test(String(pasted)) && before && !/\s$/.test(before)) text = " " + text;
        if (/\s$/.test(String(pasted)) && after && !/^\s/.test(after)) text += " ";
    }
    if (kind === "aliases" && text) {
        if (before.trim() && !/[,;\r\n]\s*$/.test(before)) text = ", " + text;
        if (after.trim() && !/^\s*[,;\r\n]/.test(after)) text += ", ";
    }
    let next = before + text + after, caret = before.length + text.length;
    if (kind === "aliases") { next = normalizeNameList(next); caret = next.length; }
    let note = result.note;
    if (maxLength > 0 && next.length > maxLength) {
        next = next.slice(0, maxLength);
        caret = Math.min(caret, next.length);
        if (!note) note = `Cut to ${maxLength} characters.`;
    }
    return { value: next, caret, note };
}
