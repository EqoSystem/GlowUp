// Sources for the Ingredient form: for each box, which dictionaries/databases hold its value, what to look for there, and
// where the link goes. PURE logic (no page) so tests/fieldSources.test.mjs can check it; the one network call (PubChem's
// name -> compound number) is made by the page, with the helpers below.
//
// LINK POLICY (2026-11-10)
//   * Every link goes to the source's OWN site (the allowlist below). Never a search engine, in any link or fallback.
//   * Three kinds of link, each tested by scripts/verify-source-links.mjs before it is shipped:
//       landing  the site's home or search page. Always works, is not about this ingredient. The INCI name is copied when it is
//                opened (copyName), so it can be pasted into the site's own search box.
//       search   the site's OWN search page, built from the INCI name through a plain GET address that shows results without
//                scripts (COSMILE Europe, CosmeticsInfo.org, CosIng Checker). Opens the results for this name.
//       page     the page of THIS ingredient (PubChem's compound page; the compound number is looked up with PubChem's documented
//                PUG REST service). Only a page can be added to Reference links with "+ ref": a landing or search page is not a source.
//   * Tried and NOT shipped (the script showed it): CIR /ingredients/<slug> answers 200 for any word, including nonsense, with the
//     same generic page; CosIng's details/<id> and CosmeticsInfo's /ingredient/<slug>/ need the site's own internal name or id
//     (404 for names it does not have); INCIpedia and CosIng are script-driven with no address for a name; the ECHA site answers
//     403 to a script, so ECHA CHEM is linked instead.
//
// The work stays by hand: the admin opens a source, finds the value, and types or pastes it. Nothing fetches, scrapes or fills
// a box. The paste helpers only tidy text the admin pastes (spaces, line breaks, list separators).

/** The only hosts a source link may have (a sub-site of one is fine: www.cir-safety.org, chem.echa.europa.eu). */
export const ALLOWED_HOSTS = [
    "ec.europa.eu", "single-market-economy.ec.europa.eu", "cosmileeurope.eu", "cir-safety.org", "pubchem.ncbi.nlm.nih.gov",
    "incipedia.personalcarecouncil.org", "cosmeticsinfo.org", "cosingchecker.com", "echa.europa.eu", "eur-lex.europa.eu"
];
export const SEARCH_ENGINES = ["google.", "bing.", "duckduckgo.", "yahoo.", "yandex.", "baidu.", "ecosia.", "startpage.", "brave.com", "search."];

/** true when `url` is https, has no user name, and its host is an allowed one (not a search engine). */
export function hostAllowed(url) {
    let parsed;
    try { parsed = new URL(String(url)); } catch (error) { return false; }
    if (parsed.protocol !== "https:" || parsed.username || parsed.password) return false;
    const host = parsed.hostname.toLowerCase();
    if (SEARCH_ENGINES.some((engine) => host.includes(engine))) return false;
    return ALLOWED_HOSTS.some((allowed) => host === allowed || host.endsWith("." + allowed));
}

const enc = encodeURIComponent;

/**
 * Every source. `landing` = the site's own page that always works. `search(name)` = the site's own search results for the name
 * (only where a plain GET address was tested). `copyName` = a landing page with a search box: the name is copied when it is
 * opened. `kind` = what the link is for the admin: "page" (PubChem, resolved on click), "search" or "landing".
 */
export const SOURCES = {
    cosing: { label: "EU CosIng", kind: "landing", copyName: true, landing: "https://ec.europa.eu/growth/tools-databases/cosing/" },
    incipedia: { label: "INCIpedia", kind: "landing", copyName: true, landing: "https://incipedia.personalcarecouncil.org/" },
    cosmile: { label: "COSMILE Europe", kind: "search", landing: "https://cosmileeurope.eu/", search: (name) => `https://cosmileeurope.eu/inci/results/?q=${enc(name)}` },
    cir: { label: "Cosmetic Ingredient Review", kind: "landing", copyName: true, landing: "https://www.cir-safety.org/ingredients" },
    pubchem: { label: "PubChem (NIH)", kind: "page", copyName: true, landing: "https://pubchem.ncbi.nlm.nih.gov/" },
    cosmeticsinfo: { label: "CosmeticsInfo.org", kind: "search", landing: "https://www.cosmeticsinfo.org/", search: (name) => `https://www.cosmeticsinfo.org/?s=${enc(name)}` },
    echa: { label: "ECHA CHEM (EU)", kind: "landing", copyName: true, landing: "https://chem.echa.europa.eu/" },
    fragrance: { label: "EU fragrance allergen list", kind: "landing", copyName: false, landing: "https://single-market-economy.ec.europa.eu/sectors/cosmetics/cosmetic-products-specific-topics/fragrance-allergens-labelling_en" },
    checker: { label: "CosIng Checker (unofficial)", kind: "search", landing: "https://cosingchecker.com/", search: (name) => `https://cosingchecker.com/search?q=${enc(name)}` }
};

/** What a bare address in Reference links is called (the longest matching host wins). */
export const HOST_LABELS = {
    "cir-safety.org": "Cosmetic Ingredient Review",
    "ec.europa.eu": "EU CosIng",
    "single-market-economy.ec.europa.eu": "European Commission",
    "pubchem.ncbi.nlm.nih.gov": "PubChem (NIH)",
    "cosmileeurope.eu": "COSMILE Europe",
    "incipedia.personalcarecouncil.org": "INCIpedia",
    "cosmeticsinfo.org": "CosmeticsInfo.org",
    "cosingchecker.com": "CosIng Checker (unofficial)",
    "echa.europa.eu": "ECHA",
    "eur-lex.europa.eu": "EUR-Lex"
};

const ADMIN_JUDGMENT = "Admin judgment. Add your evidence in Reference links.";

/** The name of each box, as the admin reads it (used for "Used for: ..." in the Reference links source list). */
export const FIELD_LABELS = {
    purpose: "Purpose", description: "User-Friendly Definition",
    chemicalDescription: "What the chemical is", aliases: "Also Known As", cautionNote: "Caution Note", commonUses: "Commonly Used In",
    allergens: "Allergen Family", frequentAllergy: "Allergy risk with frequent use"
};

/**
 * Per box: `sources` = [source id, what to look for there], `helper` = one line under the list, `title` = a line above it.
 * Only boxes whose text is WRITTEN from a source have an entry. The INCI name, the classification and the allergen family are typed
 * or chosen from a list, so they have no Sources row (the admin's rule of 2026-11-10).
 */
export const FIELD_GUIDE = {
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
        helper: "Paste the page address from the browser address bar of the page you used. A bare address gets its label when you leave the box."
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
    frequentAllergy: {
        title: "Optional. Leave empty when no source lists a risk.",
        sources: [
            ["echa", "Search the name or CAS number (see Also Known As). If the classification lists H317, the official statement is \"May cause an allergic skin reaction\"."],
            ["pubchem", "Open the compound page, then Safety and Hazards, GHS Classification. Look for H317 (the same statement)."],
            ["fragrance", "For a fragrance ingredient: check whether it is on the EU list of fragrance allergens (named on labels because it can cause contact allergy)."]
        ],
        helper: "Write one plain sentence from what the source says, e.g. \"May cause an allergic skin reaction in some people.\" The EU classification is for the ingredient itself; the chance of a reaction in a finished product depends on the amount. Keep the page in Reference links."
    },
    judgment: { sources: [], helper: ADMIN_JUDGMENT }
};

/**
 * The links of one box for an INCI name: [{ id, label, kind, url, lookFor, copyName, canRef }]. `url` is what opens (the search
 * results for a "search" source, else the landing page; PubChem's page address is looked up when it is clicked). `canRef` = a "+ ref"
 * button is enabled: only for a "page" source. Every url passes hostAllowed().
 */
export function linksFor(field, name) {
    const guide = FIELD_GUIDE[field];
    const clean = cleanLine(name);
    if (!guide || clean.length < 2) return [];
    return guide.sources.map(([id, lookFor]) => linkOf(id, clean, lookFor));
}

export function linkOf(id, name, lookFor = "") {
    const source = SOURCES[id];
    return {
        id, label: source.label, kind: source.kind, lookFor,
        url: source.kind === "search" ? source.search(name) : source.landing,
        copyName: source.kind !== "search" && source.copyName === true,
        canRef: source.kind === "page"
    };
}

/** Each distinct source used by any box, with the boxes it serves: [{ ...link, usedFor: ["Purpose", ...] }] (the Reference links list). */
export function allSourcesFor(name) {
    const clean = cleanLine(name);
    if (clean.length < 2) return [];
    const used = new Map();
    Object.entries(FIELD_GUIDE).forEach(([field, guide]) => guide.sources.forEach(([id]) => {
        if (!used.has(id)) used.set(id, []);
        used.get(id).push(FIELD_LABELS[field]);
    }));
    return Array.from(used, ([id, boxes]) => ({ ...linkOf(id, clean), usedFor: boxes }));
}

/** Every address this file can make for a name (the test checks each one against the allowlist). */
export function everyGeneratedUrl(name) {
    return Object.keys(SOURCES).flatMap((id) => [SOURCES[id].landing].concat(SOURCES[id].search ? [SOURCES[id].search(name)] : []));
}

// ----------------------------------------------------------------------------------------------------------------
// PubChem: the page of THIS ingredient
// ----------------------------------------------------------------------------------------------------------------
/** PubChem's documented PUG REST address that answers a name with its compound number(s), one per line. */
export const pubchemLookupUrl = (name) => `https://pubchem.ncbi.nlm.nih.gov/rest/pug/compound/name/${enc(cleanLine(name))}/cids/TXT`;
/** The first compound number of the answer, or 0. */
export function firstCompoundId(text) {
    const first = String(text || "").trim().split(/\s+/)[0];
    return /^[1-9][0-9]{0,11}$/.test(first) ? Number(first) : 0;
}
export const pubchemPageUrl = (cid) => `https://pubchem.ncbi.nlm.nih.gov/compound/${Number(cid)}`;

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

/** The label of a known host (the longest matching host wins), or "". */
export function labelForHost(host) {
    const name = String(host || "").toLowerCase().replace(/^www\./, "");
    const known = Object.keys(HOST_LABELS).filter((h) => name === h || name.endsWith("." + h)).sort((a, b) => b.length - a.length)[0];
    return known ? HOST_LABELS[known] : "";
}

/**
 * Every line that is only an https address and comes from a known site gets its label: "https://..." -> "Label | https://...".
 * Lines that already have a label, unknown sites, and anything that is not an https address are left as they are.
 */
export function labelBareUrls(text) {
    return String(text || "").split(/\r?\n/).map((line) => {
        const bare = line.trim();
        if (!/^https:\/\/[^\s"'<>|]+$/i.test(bare)) return line;
        let host = "";
        try { const parsed = new URL(bare); if (parsed.username || parsed.password) return line; host = parsed.hostname; } catch (error) { return line; }
        const label = labelForHost(host);
        return label ? `${label} | ${bare}` : line;
    }).join("\n");
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
 * What a paste into a box does. `kind` = "purpose" | "description" | "chemical" | "aliases" | "name" | "allergy".
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

// ----------------------------------------------------------------------------------------------------------------
// SOURCE LINK per box (10 November 2026): the one page that supports what a box says. One entry per box; `enabled` false hides it
// (and leaves it out of "Sources: n of N"). `data` = the ingredient document's field (a nullable string). `finder` = the sites listed
// under "Where to look" in the Source Finder, in this order. The rules for an address are in sourceLinkRules.mjs.
// ----------------------------------------------------------------------------------------------------------------
export const SOURCE_LINK_FIELDS = [
    { key: "purpose", label: "Purpose", input: "ingredientPurposeInput", data: "purposeSourceUrl", enabled: true, finder: ["cosmile", "cosing"] },
    { key: "description", label: "User-Friendly Definition", input: "ingredientDescInput", data: "definitionSourceUrl", enabled: true, finder: ["cosmile", "cosing", "cir", "cosmeticsinfo"] },
    { key: "chemicalDescription", label: "What the chemical is", input: "ingredientChemInput", data: "chemicalSourceUrl", enabled: true, finder: ["pubchem", "cosing"] },
    { key: "aliases", label: "Also Known As", input: "ingredientAliasesInput", data: "aliasesSourceUrl", enabled: true, finder: ["cosmile", "cosing", "pubchem", "incipedia"] },
    { key: "cautionNote", label: "Caution Note", input: "ingredientCautionInput", data: "cautionSourceUrl", enabled: true, finder: ["cir", "cosing"] },
    { key: "commonUses", label: "Commonly Used In", input: "ingredientUsesInput", data: "commonlyUsedInSourceUrl", enabled: true, finder: ["cosmile", "cosmeticsinfo"] }
];

/** The server search of the CosIng Checker index in the Source Finder (verified 10 November 2026: its /ingredients/<slug>/ pages answer 200 and name the ingredient). */
export const COSING_INDEX_ENABLED = true;

/** What to copy from a site for a box: the box's own instruction when it has one, else a general line. */
export function finderWhat(field, id) {
    const guide = FIELD_GUIDE[field];
    const own = guide ? guide.sources.find(([sid]) => sid === id) : null;
    if (own) return own[1];
    return {
        cosmile: "Find the ingredient and read its page.", cosing: "Search the name and open its entry.", cir: "Open the ingredient's report.",
        cosmeticsinfo: "Open the ingredient's page.", pubchem: "Open the compound page.", incipedia: "Search the name (sign-in required)."
    }[id] || "Find the ingredient's page.";
}
