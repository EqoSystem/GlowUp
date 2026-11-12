





















export const ALLOWED_HOSTS = [
    "ec.europa.eu", "single-market-economy.ec.europa.eu", "cosmileeurope.eu", "cir-safety.org", "pubchem.ncbi.nlm.nih.gov",
    "incipedia.personalcarecouncil.org", "cosmeticsinfo.org", "cosingchecker.com", "echa.europa.eu", "eur-lex.europa.eu"
];
export const SEARCH_ENGINES = ["google.", "bing.", "duckduckgo.", "yahoo.", "yandex.", "baidu.", "ecosia.", "startpage.", "brave.com", "search."];


export function hostAllowed(url) {
    let parsed;
    try { parsed = new URL(String(url)); } catch (error) { return false; }
    if (parsed.protocol !== "https:" || parsed.username || parsed.password) return false;
    const host = parsed.hostname.toLowerCase();
    if (SEARCH_ENGINES.some((engine) => host.includes(engine))) return false;
    return ALLOWED_HOSTS.some((allowed) => host === allowed || host.endsWith("." + allowed));
}

const enc = encodeURIComponent;






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


export const FIELD_LABELS = {
    purpose: "Purpose", description: "User-Friendly Definition",
    chemicalDescription: "What the chemical is", aliases: "Also Known As", cautionNote: "Caution Note", commonUses: "Commonly Used In",
    allergens: "Allergen Family"
};






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
    judgment: { sources: [], helper: ADMIN_JUDGMENT }
};






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


export function everyGeneratedUrl(name) {
    return Object.keys(SOURCES).flatMap((id) => [SOURCES[id].landing].concat(SOURCES[id].search ? [SOURCES[id].search(name)] : []));
}





export const pubchemLookupUrl = (name) => `https://pubchem.ncbi.nlm.nih.gov/rest/pug/compound/name/${enc(cleanLine(name))}/cids/TXT`;

export function firstCompoundId(text) {
    const first = String(text || "").trim().split(/\s+/)[0];
    return /^[1-9][0-9]{0,11}$/.test(first) ? Number(first) : 0;
}
export const pubchemPageUrl = (cid) => `https://pubchem.ncbi.nlm.nih.gov/compound/${Number(cid)}`;




const urlKey = (url) => String(url || "").trim().replace(/\/+$/, "").toLowerCase();
const addressOf = (line) => { const at = String(line).lastIndexOf(" | "); return (at >= 0 ? String(line).slice(at + 3) : String(line)).trim(); };






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


export function labelForHost(host) {
    const name = String(host || "").toLowerCase().replace(/^www\./, "");
    const known = Object.keys(HOST_LABELS).filter((h) => name === h || name.endsWith("." + h)).sort((a, b) => b.length - a.length)[0];
    return known ? HOST_LABELS[known] : "";
}





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





export const cleanLine = (text) => String(text == null ? "" : text).replace(/\s+/g, " ").trim();


export function looksLikeFunctionTerm(text) {
    const value = String(text || "");
    return /[A-Z].*[A-Z]/.test(value) && value === value.toUpperCase();
}


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





export function pasteResult(kind, pasted) {
    const text = kind === "aliases" ? normalizeNameList(pasted) : cleanLine(pasted);
    let note = "";
    if (kind === "purpose" && looksLikeFunctionTerm(text)) note = NOTES.functionTerm;
    else if (kind === "description" && String(pasted || "").length > LARGE_PASTE) note = NOTES.largePaste;
    else if (kind === "allergy" && String(pasted || "").length > LARGE_PASTE) note = NOTES.largePaste;
    else if (kind === "name" && (text.includes(" / ") || text.includes(","))) note = NOTES.inciList;
    return { text, note };
}






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






export const SOURCE_LINK_FIELDS = [
    { key: "purpose", label: "Purpose", input: "ingredientPurposeInput", data: "purposeSourceUrl", enabled: true, finder: ["cosmile", "cosing"] },
    { key: "description", label: "User-Friendly Definition", input: "ingredientDescInput", data: "definitionSourceUrl", enabled: true, finder: ["cosmile", "cosing", "cir", "cosmeticsinfo"] },
    { key: "chemicalDescription", label: "What the chemical is", input: "ingredientChemInput", data: "chemicalSourceUrl", enabled: true, finder: ["pubchem", "cosing"] },
    { key: "aliases", label: "Also Known As", input: "ingredientAliasesInput", data: "aliasesSourceUrl", enabled: true, finder: ["cosmile", "cosing", "pubchem", "incipedia"] },
    { key: "cautionNote", label: "Caution Note", input: "ingredientCautionInput", data: "cautionSourceUrl", enabled: true, finder: ["cir", "cosing"] },
    { key: "commonUses", label: "Commonly Used In", input: "ingredientUsesInput", data: "commonlyUsedInSourceUrl", enabled: true, finder: ["cosmile", "cosmeticsinfo"] }
];


export const COSING_INDEX_ENABLED = true;


export function finderWhat(field, id) {
    const guide = FIELD_GUIDE[field];
    const own = guide ? guide.sources.find(([sid]) => sid === id) : null;
    if (own) return own[1];
    return {
        cosmile: "Find the ingredient and read its page.", cosing: "Search the name and open its entry.", cir: "Open the ingredient's report.",
        cosmeticsinfo: "Open the ingredient's page.", pubchem: "Open the compound page.", incipedia: "Search the name (sign-in required)."
    }[id] || "Find the ingredient's page.";
}
