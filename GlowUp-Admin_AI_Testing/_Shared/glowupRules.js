


















const SPECIAL = new Map([[0xdf, "ss"], [0xe6, "ae"], [0x153, "oe"], [0xf8, "o"], [0x111, "d"], [0xf0, "d"], [0x142, "l"], [0xfe, "th"], [0x131, "i"]]);
const LETTER_OR_DIGIT = /^[\p{L}\p{Nd}]$/u;


function lowerCase(text) {
    let lower = "";
    for (let i = 0; i < text.length; i++) lower += text[i].toLowerCase();
    return lower;
}


function fold(lower) {
    let plain = true;
    for (let i = 0; i < lower.length; i++) if (lower.charCodeAt(i) > 127) { plain = false; break; }
    if (plain) return lower;
    const apart = lower.normalize("NFD");
    let result = "";
    for (let i = 0; i < apart.length; i++) {
        const code = apart.charCodeAt(i);
        if (code >= 0x300 && code <= 0x36f) continue;

        if (code >= 65 && code <= 90) { result += String.fromCharCode(code + 32); continue; }
        const replacement = SPECIAL.get(code);
        result += replacement !== undefined ? replacement : apart[i];
    }
    return result;
}


export function normalizeName(text) {
    if (text === null || text === undefined) return "";
    const source = String(text);
    if (source.length === 0) return "";
    const folded = fold(lowerCase(source));
    let key = "", space = false;
    for (let i = 0; i < folded.length; i++) {
        const code = folded.charCodeAt(i);
        if ((code >= 97 && code <= 122) || (code >= 48 && code <= 57)) {
            if (space && key.length > 0) key += " ";
            key += folded[i]; space = false;
        } else space = true;
    }
    if (key.length > 0) return key;


    const raw = lowerCase(source.trim());
    let kept = "";
    space = false;
    for (let i = 0; i < raw.length; i++) {
        if (LETTER_OR_DIGIT.test(raw[i])) { if (space && kept.length > 0) kept += " "; kept += raw[i]; space = false; }
        else space = true;
    }
    return kept;
}


export const slugName = (text) => normalizeName(text).replace(/ /g, "_");


export const compactName = (text) => normalizeName(text).replace(/ /g, "");


export function nameWords(text) {
    const key = normalizeName(text);
    return key.length === 0 ? [] : key.split(" ");
}


export function sameName(a, b) {
    const key = normalizeName(a);
    return key.length > 0 && key === normalizeName(b);
}





export function squashKey(key) {
    if (!key) return "";
    let squashed = "";
    for (let i = 0; i < key.length; i++) {
        let c = key[i];
        if (c === " ") continue;
        if (c === "r" && key[i + 1] === "n") { squashed += "m"; i++; continue; }
        if (c === "v" && key[i + 1] === "v") { squashed += "w"; i++; continue; }
        if (c === "0") c = "o";
        else if (c === "1" || c === "i") c = "l";
        squashed += c;
    }
    return squashed;
}







export function matchesSearch(typed, texts) {
    const words = nameWords(typed);
    if (words.length === 0) return true;
    const keys = (Array.isArray(texts) ? texts : [texts]).map(normalizeName).filter((key) => key.length > 0);
    if (keys.length === 0) return false;
    const joined = " " + keys.join(" | ");

    if (words.every((word) => joined.includes(word.length <= 2 ? " " + word : word))) return true;

    const typedTogether = words.join("");
    return keys.some((key) => key.replace(/ /g, "").includes(typedTogether));
}












export const MAKEUP_TYPES = [
    "Lipstick", "Lip Gloss", "Lip Tint", "Lip Liner", "Foundation", "Concealer", "Primer", "Powder", "Blush", "Bronzer",
    "Highlighter", "Contour", "Eyeshadow", "Eyeliner", "Mascara", "Brow", "Setting Spray"
];



const STRONG = [
    "lipstick", "lip stick", "lip gloss", "lipgloss", "lip tint", "lip stain", "lip liner", "lipliner", "lip lacquer", "lip cream",
    "lip colour", "lip color", "lip crayon", "lip pencil", "lip mousse", "lip matte", "liquid lip", "lip paint", "lip glaze", "lip shine",
    "eyeshadow", "eye shadow", "eyeliner", "eye liner", "mascara", "blush", "blusher", "concealer", "foundation",
    "eyebrow", "brow pencil", "brow gel", "brow pomade", "brow powder", "brow mascara", "brow pen", "brow liner", "brow tint", "brow wax",
    "brow definer", "brow soap", "soap brow", "kajal", "kohl", "face powder", "pressed powder", "loose powder", "compact powder",
    "setting powder", "finishing powder", "powder foundation", "cushion", "contour", "highlighter", "bronzer", "makeup", "make up",
    "cheek tint", "cheek stain", "cheek color", "cheek colour", "setting spray", "fixing spray", "setting mist", "fixing mist",


    "bb cream", "cc cream", "tinted moisturizer", "tinted moisturiser", "skin tint"
];

const GENERIC = ["powder", "primer", "gloss", "tint", "liner", "brow", "palette", "glitter", "shimmer", "corrector", "illuminator", "rouge", "stain"];

const HAIR_WORDS = ["shampoo", "conditioner", "hair", "scalp", "haircare"];
const NAIL_WORDS = ["nail", "cuticle", "manicure"];
const SCENT_WORDS = ["perfume", "cologne", "eau de", "parfum", "body mist", "fragrance mist"];
const BODY_WORDS = [
    "body", "bodylotion", "bodywash", "hand", "foot", "feet", "deodorant", "antiperspirant", "soap", "shower", "bath", "shaving", "shave",
    "toothpaste", "mouthwash", "oral", "intimate", "feminine wash", "baby", "tanning", "massage", "sanitizer", "sanitiser"
];
const CARE_WORDS = [
    "moisturizer", "moisturiser", "serum", "cleanser", "toner", "sunscreen", "sunblock", "sun screen", "sun block", "mask", "essence",
    "ampoule", "lotion", "face wash", "facial wash", "wash", "scrub", "exfoliant", "exfoliator", "peel", "emulsion", "acne", "pimple",
    "treatment", "skincare", "skin care", "eye cream", "night cream", "day cream", "sleeping pack", "spot corrector"
];

export const WEAK_CARE_WORDS = ["cream", "gel", "balm", "mist", "spf", "water", "butter", "oil", "uv"];


const NEVER_MAKEUP = [
    "makeup remover", "make up remover", "remover", "cleansing", "micellar", "lip scrub", "lip mask", "lip treatment", "lip care",
    "lip serum", "lip conditioner", "brow serum", "lash serum", "eyelash serum", "lash conditioner", "brow conditioner",

    "lip balm", "lip oil", "tinted sunscreen", "tinted sunblock", "tinted sun screen", "tinted sun block"
];
const NEVER_MAKEUP_OTHER = ["hair mascara", "nail polish", "nail lacquer", "makeup brush", "make up brush", "brush cleaner", "makeup sponge", "make up sponge"];








export const UNDECIDED_KINDS = ["tinted lip balm", "tinted lip oil", "lip balm tint", "lip oil tint"];


const LABEL_NOT_MAKEUP = [
    "shampoo", "hair conditioner", "body wash", "bodywash", "body lotion", "hand cream", "hand lotion", "deodorant", "antiperspirant",
    "eau de parfum", "eau de toilette", "cologne", "nail polish", "nail lacquer", "toothpaste", "mouthwash",
    "facial wash", "face wash", "facial cleanser", "facial foam", "micellar water", "makeup remover", "make up remover", "feminine wash",
    "baby oil", "baby powder", "body scrub", "bath soap", "hand soap", "hand sanitizer"
];


function declaredFamily(typeKey) {
    switch (typeKey) {
        case "skin care": case "skincare": case "face care": return "skin care";
        case "hair": case "hair care": case "haircare": return "hair";
        case "body": case "body care": case "bodycare": return "body care";
        case "nail": case "nails": case "nail care": return "nail";
        case "fragrance": case "perfume": return "fragrance";
        default: return null;
    }
}

const verdict = (kind, family, word) => ({ kind, family: family || "", word: word || "" });

function isLetters(word) {
    if (word.length === 0) return false;
    for (let i = 0; i < word.length; i++) { const c = word.charCodeAt(i); if (c < 97 || c > 122) return false; }
    return true;
}


function plain(text) {
    const key = normalizeName(text);
    if (key.length === 0) return " ";
    const words = key.split(" ");
    let kept = " ";
    for (let i = 0; i < words.length; i++) {
        if (i + 1 < words.length && words[i + 1] === "free" && isLetters(words[i])) { i++; continue; }
        kept += words[i] + " ";
    }
    return kept;
}


const holds = (text, word) => text.includes(" " + word + " ") || text.includes(" " + word + "s ") || text.includes(" " + word + "es ");


function has(text, words) {
    for (const word of words) if (holds(text, word)) return word;
    return null;
}

const text0 = (value) => (value === null || value === undefined ? "" : String(value));






export function judgeProduct(cosmeticType, productName) {
    let type = text0(cosmeticType);
    const givenName = text0(productName);
    let typeKey = normalizeName(type);
    if (typeKey === "unknown" || typeKey === "other" || typeKey === "cosmetic" || typeKey === "cosmetics") { type = ""; typeKey = ""; }
    const name = plain(givenName), text = plain(type + " " + givenName);
    if (text.trim().length === 0) return verdict("unclear", "", "");


    const declared = declaredFamily(typeKey);
    if (declared !== null) {
        if (has(name, STRONG) !== null || has(name, UNDECIDED_KINDS) !== null) return verdict("unclear", "", typeKey);
        return verdict(declared === "skin care" ? "skincare" : "out", declared, typeKey);
    }

    let word = has(text, UNDECIDED_KINDS);
    if (word !== null) return verdict("unclear", "", word);
    word = has(text, NEVER_MAKEUP);
    if (word !== null) return verdict("skincare", "skin care", word);
    word = has(text, NEVER_MAKEUP_OTHER);
    if (word !== null) return verdict("out", word.startsWith("hair") ? "hair" : word.startsWith("nail") ? "nail" : "make-up tool", word);

    const strong = has(text, STRONG), generic = has(text, GENERIC);
    const browOrLash = has(text, ["brow", "eyebrow", "lash", "eyelash"]) !== null;
    let hair = has(text, HAIR_WORDS);
    if (hair === "hair" && browOrLash) hair = null;
    const nail = has(text, NAIL_WORDS), scent = has(text, SCENT_WORDS), body = has(text, BODY_WORDS), care = has(text, CARE_WORDS);
    const other = hair !== null ? hair : nail !== null ? nail : scent !== null ? scent : body;
    const family = hair !== null ? "hair" : nail !== null ? "nail" : scent !== null ? "fragrance" : body !== null ? "body care" : "";

    const nameSaysMakeup = has(name, STRONG) !== null || has(name, GENERIC) !== null;
    const nameSaysOther = has(name, HAIR_WORDS) !== null || has(name, NAIL_WORDS) !== null || has(name, SCENT_WORDS) !== null
                       || has(name, BODY_WORDS) !== null || has(name, CARE_WORDS) !== null;

    if (strong !== null) {
        if (hair !== null || nail !== null) return verdict("unclear", "", hair !== null ? hair : nail);

        if (!nameSaysMakeup && nameSaysOther) return verdict("unclear", "", other !== null ? other : care);
        return verdict("makeup", "", strong);
    }
    if (generic !== null) {
        if (other !== null) return has(name, GENERIC) !== null ? verdict("out", family, other) : verdict("unclear", "", other);
        if (care !== null) return verdict("unclear", "", care);
        return verdict("makeup", "", generic);
    }
    if (other !== null) return verdict("out", family, other);
    if (care !== null) return verdict("skincare", "skin care", care);
    word = has(text, WEAK_CARE_WORDS);
    if (word !== null) return verdict("skincare", "skin care", word);
    return verdict("unclear", "", "");
}





export function judgeLabel(frontText) {
    const text = plain(frontText);
    if (text.trim().length === 0) return verdict("unclear", "", "");
    const remover = has(text, ["makeup remover", "make up remover"]);
    if (remover !== null) return verdict("skincare", "skin care", remover);
    const makeup = has(text, STRONG);
    if (makeup !== null) return verdict("makeup", "", makeup);
    const word = has(text, LABEL_NOT_MAKEUP);
    if (word === null || has(text, UNDECIDED_KINDS) !== null) return verdict("unclear", "", "");
    const whole = judgeProduct("", word);
    return whole.kind === "skincare" || whole.kind === "out" ? whole : verdict("skincare", "skin care", word);
}


export const productScope = (cosmeticType, productName) => judgeProduct(cosmeticType, productName).kind;
export const isMakeup = (cosmeticType, productName) => judgeProduct(cosmeticType, productName).kind === "makeup";

export const isNotMakeup = (cosmeticType, productName) => { const kind = judgeProduct(cosmeticType, productName).kind; return kind === "skincare" || kind === "out"; };


const TYPE_WORDS = [
    ["Lip Gloss", "lip gloss", "lipgloss", "lip glaze", "lip shine"],
    ["Lip Tint", "lip tint", "lip stain"],
    ["Lip Liner", "lip liner", "lipliner", "lip pencil"],
    ["Lipstick", "lipstick", "lip stick", "lip cream", "lip lacquer", "liquid lip", "lip colour", "lip color", "lip crayon", "lip mousse", "lip matte", "lip paint"],
    ["Setting Spray", "setting spray", "fixing spray", "setting mist", "fixing mist"],
    ["Brow", "eyebrow", "brow"],
    ["Foundation", "powder foundation", "foundation", "cushion", "bb cream", "cc cream", "tinted moisturizer", "tinted moisturiser", "skin tint"],
    ["Concealer", "concealer", "corrector"],
    ["Primer", "primer"],
    ["Eyeshadow", "eyeshadow", "eye shadow"],
    ["Eyeliner", "eyeliner", "eye liner", "kajal", "kohl"],
    ["Mascara", "mascara"],
    ["Blush", "blush", "blusher", "cheek tint", "cheek stain", "cheek color", "cheek colour"],
    ["Bronzer", "bronzer"],
    ["Contour", "contour"],
    ["Highlighter", "highlighter", "illuminator"],
    ["Powder", "powder"],
    ["Lip Gloss", "gloss"],
    ["Lip Tint", "tint"],
    ["Eyeliner", "liner"]
];





export function makeupType(cosmeticType, productName) {
    if (judgeProduct(cosmeticType, productName).kind !== "makeup") return "";
    const type = plain(cosmeticType), name = plain(productName);
    for (const known of MAKEUP_TYPES) if (type.trim() === known.toLowerCase()) return known;
    for (const text of [type, name])
        for (const words of TYPE_WORDS)
            for (let i = 1; i < words.length; i++)
                if (holds(text, words[i])) return words[0];
    return "";
}


export function notMakeupWords(judged) {
    if (!judged || (judged.kind !== "skincare" && judged.kind !== "out")) return "";
    if (!judged.family) return "not a make-up product";
    return "a " + judged.family + (judged.family === "make-up tool" ? "" : " product");
}





export function barcodeKey(barcode) {
    const digits = text0(barcode).replace(/[^0-9]/g, "").replace(/^0+/, "");
    return digits.length >= 6 && digits.length <= 14 ? digits : "";
}





export function brandsAgree(a, b) {
    const ka = normalizeName(a), kb = normalizeName(b);
    if (ka.length === 0 || kb.length === 0) return true;
    if (ka === kb || ka.replace(/ /g, "") === kb.replace(/ /g, "")) return true;
    const longer = " " + (ka.length >= kb.length ? ka : kb) + " ", shorter = " " + (ka.length >= kb.length ? kb : ka) + " ";
    return shorter.trim().length >= 3 && longer.includes(shorter);
}


export function scanProductId(brandName, productName) {
    const brand = slugName(brandName), name = slugName(productName);
    if (name.length === 0) return "";
    const id = "scan_" + (brand.length > 0 ? brand : "nobrand") + "__" + name;
    return id.length > 110 ? id.substring(0, 110) : id;
}


export function scanBrandId(brandName) {
    const brand = slugName(brandName);
    if (brand.length === 0) return "";
    const id = "scan_" + brand;
    return id.length > 90 ? id.substring(0, 90) : id;
}








export const STOPLIST = {
    "Headers of the list itself": [
        "ingredients", "ingredient", "ingredientes", "inci", "composition", "contains", "may contain",
        "active ingredients", "inactive ingredients", "other ingredients", "sangkap", "mga sangkap"
    ],
    "How much is in the pack": [
        "net wt", "net weight", "net vol", "net volume", "net content", "net contents", "netto", "ml", "fl oz", "oz", "gram", "grams", "kg", "mg"
    ],
    "Who made it and where": [
        "made in", "product of", "manufactured", "manufacturer", "distributed", "distributor", "imported", "importer", "marketed",
        "exclusively", "packed by", "repacked", "licensed", "under license", "trademark", "all rights reserved", "copyright",
        "inc", "corp", "corporation", "co ltd", "ltd", "limited", "company", "enterprises", "trading", "industries", "laboratories",
        "philippines", "manila", "quezon city", "makati", "paris", "new york", "london", "seoul", "tokyo"
    ],
    "Codes, dates and registration numbers": [
        "batch", "lot", "lot no", "lot number", "mfg", "mfd", "exp", "expiry", "expiration", "best before", "use before", "use by",
        "barcode", "notification no", "cpr no", "reg no", "fda", "lto"
    ],
    "Web, phone and social media": [
        "www", "http", "https", "com", "email", "e mail", "tel", "telephone", "hotline", "customer care", "customer service",
        "facebook", "instagram", "tiktok", "website", "follow us", "visit", "scan me", "qr"
    ],
    "How to use it, and warnings": [
        "directions", "direction", "how to use", "to use", "usage", "apply", "warning", "warnings", "caution", "cautions",
        "precaution", "precautions", "for external use", "external use only", "keep out of reach", "keep away", "avoid contact",
        "discontinue", "store in", "storage", "do not", "if irritation", "rinse", "children", "consult", "see bottom",
        "see packaging", "see box", "please recycle", "recycle", "shake well", "patch test"
    ],
    "Advertising words": [
        "new", "improved", "long lasting", "longlasting", "long wear", "longwear", "waterproof", "water proof", "smudge proof",
        "smudgeproof", "transfer proof", "transferproof", "kiss proof", "dermatologist tested", "dermatologically tested",
        "ophthalmologist tested", "clinically tested", "allergy tested", "hypoallergenic", "non comedogenic", "noncomedogenic",
        "cruelty free", "vegan", "halal", "free", "hour", "hours", "hr", "hrs", "24h", "12h", "all day", "matte", "glossy", "satin",
        "finish", "shade", "shades", "color", "colour", "cosmetic", "cosmetics", "beauty", "spf", "pa", "uva", "uvb", "broad spectrum"
    ]
};


export function stoplistHit(text) {
    const key = " " + normalizeName(text) + " ";
    if (key.length <= 2) return null;
    for (const group of Object.values(STOPLIST))
        for (const entry of group)
            if (key.includes(" " + entry + " ")) return entry;
    return null;
}


export const INGREDIENT_NAME_LIMITS = { length: 70, words: 7 };

const COLOUR_INDEX = /^C\.?I\.?\s*\p{Nd}{5}$/iu;
const HOLDS_COLOUR_INDEX = /\bC\.?I\.?\s*\p{Nd}{5}\b/iu;
const LETTER = /\p{L}/u, DIGIT = /\p{Nd}/u;


function codeLike(piece) {
    for (const word of piece.split(/[ \/\-(),]+/)) {
        let letters = 0, digits = 0;
        for (const c of word) { if (LETTER.test(c)) letters++; else if (DIGIT.test(c)) digits++; }
        if (digits >= 3 && letters >= 1 && letters <= 2) return true;
    }
    return false;
}





export function wrongIngredientShape(piece) {
    const p = text0(piece).trim();
    if (COLOUR_INDEX.test(p)) return false;
    if (p.length < 3 || p.length > INGREDIENT_NAME_LIMITS.length) return true;
    let letters = 0;
    for (let i = 0; i < p.length; i++) if (LETTER.test(p[i])) letters++;
    if (letters < 3 || letters < p.length * 0.5) return true;
    if (p.split(" ").filter((word) => word.length > 0).length > INGREDIENT_NAME_LIMITS.words) return true;
    const holdsColourIndex = HOLDS_COLOUR_INDEX.test(p);
    if (!holdsColourIndex && /\p{Nd}{4,}/u.test(p)) return true;
    if (!holdsColourIndex && codeLike(p)) return true;
    return false;
}









export const SOURCE_REGULATORS = ["europa.eu", "fda.gov", "fda.gov.ph", "nih.gov", "who.int", "aseancosmetics.org", "asean.org", "health.gov.au", "tga.gov.au", "canada.ca", "gov.uk", "echa.europa.eu"];
export const SOURCE_MEDICAL = ["cir-safety.org", "cir-reports.cir-safety.org", "aad.org", "dermnetnz.org", "contactderm.org", "sciencedirect.com", "wiley.com", "springer.com",
    "nature.com", "jamanetwork.com", "bmj.com", "mayoclinic.org", "clevelandclinic.org"];
export const SOURCE_REFERENCES = ["personalcarecouncil.org", "incibeauty.com", "incidecoder.com", "paulaschoice.com", "ewg.org", "cosmeticsinfo.org", "cosmileeurope.eu"];
export const SOURCE_PUBLIC_LISTS = ["openbeautyfacts.org"];


export const SOURCE_RETAILERS = ["watsons.com.ph", "watsons.com.my", "watsons.com.sg", "watsons.co.th", "watsons.com.hk", "watsons.com.tw", "watsons.co.id", "watsons.vn",
    "lazada.com.ph", "lazada.com.my", "lazada.sg", "lazada.co.th", "lazada.co.id", "lazada.vn",
    "shopee.ph", "shopee.com.my", "shopee.sg", "shopee.co.th", "shopee.co.id", "shopee.vn", "shopee.tw",
    "sephora.com", "sephora.ph", "sephora.sg", "sephora.my", "sephora.co.uk", "sephora.com.au", "sephora.fr",
    "ulta.com", "boots.com", "lookfantastic.com", "beautymnl.com", "zalora.com.ph", "zalora.com.my", "zalora.sg",
    "amazon.com", "amazon.co.uk", "amazon.de", "amazon.co.jp", "amazon.com.au", "amazon.ca", "walmart.com", "target.com",
    "superdrug.com", "cultbeauty.com", "cultbeauty.co.uk", "nykaa.com", "yesstyle.com", "stylevana.com", "sasa.com", "sasa.com.hk",
    "guardian.com.sg", "guardian.com.my", "oliveyoung.com", "oliveyoung.co.kr", "feelunique.com", "beautybay.com", "notino.com", "notino.co.uk",
    "douglas.de", "priceline.com.au", "chemistwarehouse.com.au", "mercurydrug.com", "southstardrug.com.ph", "landers.ph", "smmarkets.ph"];


const BACKSLASH = String.fromCharCode(92);


const STRICT_ADDRESS = /^https?:\/\/([a-z0-9.-]+)(?::[0-9]{1,5})?([/?#][!-~]*)?$/i;

export const SOURCE_KINDS = {
    regulator: { label: "Regulator / government", advice: "An official body. The strongest kind of source here.", tone: "good" },
    medical: { label: "Medical / scientific body", advice: "A medical or scientific publisher. A dermatologist or pharmacist can check the cited study or review.", tone: "good" },
    maker: { label: "Manufacturer's own site", advice: "The brand's own website, confirmed by a GlowUp admin. Its list should match the pack; marketing claims are not evidence.", tone: "" },
    reference: { label: "Ingredient database", advice: "Explains what an ingredient is. Not peer-reviewed, and never proof of what one product contains.", tone: "" },
    public: { label: "Public list (anyone can edit)", advice: "A public list anybody can edit. Use it as a second opinion only.", tone: "" },
    retailer: { label: "Retailer listing", advice: "A shop's copy of the pack's list. It can be out of date: compare it with your pack.", tone: "" },
    other: { label: "Other website", advice: "Not on GlowUp's list of known sources. Compare it with the pack or another source before relying on it.", tone: "warn" }
};


export const SOURCE_LINK_GUIDE = "Links open another website and GlowUp does not control it. Check the address in the browser bar, never enter personal details, and let a dermatologist or pharmacist confirm any claim you rely on.";


function sourceAddress(source) {
    const s = text0(source);
    if (s.trim().length === 0) return "";
    const lower = s.toLowerCase();
    let at = lower.indexOf("http://");
    const secure = lower.indexOf("https://");
    if (secure >= 0 && (at < 0 || secure < at)) at = secure;
    if (at < 0) return "";
    let end = at;
    while (end < s.length && !/\s/.test(s[end])) end++;
    return s.slice(at, end).replace(/[.,;)"]+$/, "");
}


function sourceTitle(source) {
    const s = text0(source);
    const cut = s.lastIndexOf(" | ");
    return (cut > 0 ? s.slice(0, cut) : sourceAddress(s).length === 0 ? s : "").trim();
}

const withoutWww = (host) => (host.startsWith("www.") ? host.slice(4) : host);


function plainAddress(address) {
    const m = STRICT_ADDRESS.exec(address);
    if (!m || address.includes(BACKSLASH)) return null;
    return { host: m[1].toLowerCase().replace(/\.+$/, ""), path: (m[2] || "").split(/[?#]/)[0] };
}


function isRedirect(plain) {
    const host = withoutWww(plain.host);
    return host === "vertexaisearch.cloud.google.com" || (host === "google.com" && plain.path.startsWith("/url")) || (host === "bing.com" && plain.path.startsWith("/ck"));
}





export function sourceHost(source) {
    const plain = plainAddress(sourceAddress(source));
    if (!plain) return "";
    let host = plain.host;
    if (isRedirect(plain)) {
        host = sourceTitle(source).toLowerCase().trim().replace(/\.+$/, "");
        if (!/^[a-z0-9.-]+$/.test(host)) return "";
    }
    return withoutWww(host);
}

const onSite = (host, sites) => sites.some((site) => host === site || host.endsWith("." + site));





export function makerHost(confirmedWebsite) {
    const plain = plainAddress(sourceAddress(confirmedWebsite));
    return plain && !isRedirect(plain) ? withoutWww(plain.host) : "";
}





export function sourceKind(source, confirmedWebsite) {
    const host = sourceHost(source);
    if (host.length === 0) return "other";
    if (onSite(host, SOURCE_REGULATORS) || /\.gov(\.[a-z]{2})?$/.test(host)) return "regulator";
    if (onSite(host, SOURCE_MEDICAL) || /\.edu(\.[a-z]{2})?$/.test(host)) return "medical";
    const maker = makerHost(confirmedWebsite);
    if (maker && onSite(host, [maker])) return "maker";
    if (onSite(host, SOURCE_REFERENCES)) return "reference";
    if (onSite(host, SOURCE_PUBLIC_LISTS)) return "public";
    if (onSite(host, SOURCE_RETAILERS)) return "retailer";
    return "other";
}


export function describeSourceLine(source, confirmedWebsite) {
    const kind = sourceKind(source, confirmedWebsite);
    return { kind, ...SOURCE_KINDS[kind] };
}













const NOT_REGULATION = ["nih.gov", "who.int"];
const REGULATION_SITES = SOURCE_REGULATORS.filter((site) => !NOT_REGULATION.includes(site));


function isDatabasePage(source) {
    const plain = plainAddress(sourceAddress(source));
    if (!plain) return false;
    const host = withoutWww(plain.host), path = plain.path.toLowerCase();
    return host === "pubchem.ncbi.nlm.nih.gov" || (onSite(host, ["europa.eu"]) && (path.includes("/cosing") || path.includes("cosmetic-ingredient-database")));
}


export function evidenceTier(source, confirmedWebsite) {
    const kind = sourceKind(source, confirmedWebsite);
    if (kind === "regulator") return isDatabasePage(source) ? 3 : onSite(sourceHost(source), REGULATION_SITES) ? 1 : 2;
    if (kind === "medical") return 2;
    if (kind === "reference") return 3;
    if (kind === "maker" || kind === "retailer" || kind === "public") return 4;
    return 0;
}


export const RISK_NEEDS_TIER = { harmful: 1, caution: 2 };


export function bestEvidenceTier(sources) {
    const tiers = (Array.isArray(sources) ? sources : []).map((source) => evidenceTier(source, "")).filter((tier) => tier > 0);
    return tiers.length > 0 ? Math.min(...tiers) : 0;
}


export function evidenceProblem(riskLevel, sources) {
    const need = RISK_NEEDS_TIER[riskLevel];
    if (!need) return "";
    const best = bestEvidenceTier(sources);
    if (best > 0 && best <= need) return "";
    return riskLevel === "harmful"
        ? "Harmful means a regulator prohibits or restricts the ingredient in cosmetics. Add the regulator's page (EU Cosmetics Regulation, ASEAN, FDA Philippines or US FDA) as a reference link, or choose Caution."
        : "Caution needs a regulator's page or a page of a medical or scientific body (CIR, DermNet, a journal) as a reference link.";
}
