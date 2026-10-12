// The rules the admin website shares with the mobile app. PURE logic: no Firebase, no page and no imports, so this
// one file runs in the browser and in the tests (tests/rules.test.mjs, run with Node).
//
//   normalizeName ...........  the app's TextKey        (Assets/_Project/Scripts/Data/TextKey.cs)
//   judgeProduct, makeupType   the app's ProductScope   (Assets/_Project/Scripts/Scan/ProductScope.cs)
//   barcodeKey, scanProductId  the app's ProductIndex   (Assets/_Project/Scripts/Data/ProductIndex.cs)
//
// KEEP THE WORD LISTS AND THE STEPS THE SAME AS IN THE APP. Both sides are checked against the same lists of
// examples, MakeupScopeCases.txt and SharedRuleCases.txt in the app's Assets/_Project/Editor/Tests folder: when the
// rule changes on one side only, a test fails.

// =============================================================================================
//  1. One way of turning a name into a comparable key
// =============================================================================================
// "L'Oreal  PARIS" (with or without its accent), "l'oreal paris" and "L Oreal Paris" all give "l oreal paris".
// The rule, in order: lower case; accents removed; every character that is not a letter a-z or a digit becomes a
// space; several spaces become one; no space at either end.

// letters that do not come apart into "letter + accent" by themselves (given by their character code)
const SPECIAL = new Map([[0xdf, "ss"], [0xe6, "ae"], [0x153, "oe"], [0xf8, "o"], [0x111, "d"], [0xf0, "d"], [0x142, "l"], [0xfe, "th"], [0x131, "i"]]);
const LETTER_OR_DIGIT = /^[\p{L}\p{Nd}]$/u;

// lower case one character at a time, as the app does it (so a letter never changes because of its neighbours)
function lowerCase(text) {
    let lower = "";
    for (let i = 0; i < text.length; i++) lower += text[i].toLowerCase();
    return lower;
}

// removes accents from lower-case text
function fold(lower) {
    let plain = true;
    for (let i = 0; i < lower.length; i++) if (lower.charCodeAt(i) > 127) { plain = false; break; }
    if (plain) return lower;
    const apart = lower.normalize("NFD");
    let result = "";
    for (let i = 0; i < apart.length; i++) {
        const code = apart.charCodeAt(i);
        if (code >= 0x300 && code <= 0x36f) continue;               // the accent itself
        // a capital that only shows once its accent is off (the Turkish dotted capital I, which some systems do not lower-case)
        if (code >= 65 && code <= 90) { result += String.fromCharCode(code + 32); continue; }
        const replacement = SPECIAL.get(code);
        result += replacement !== undefined ? replacement : apart[i];
    }
    return result;
}

/** The comparable key of a name ("" for nothing). */
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

    // a name written in another alphabet has no a-z letters at all: it is compared as it is written
    const raw = lowerCase(source.trim());
    let kept = "";
    space = false;
    for (let i = 0; i < raw.length; i++) {
        if (LETTER_OR_DIGIT.test(raw[i])) { if (space && kept.length > 0) kept += " "; kept += raw[i]; space = false; }
        else space = true;
    }
    return kept;
}

/** The key with "_" between the words: the form used inside document ids ("l_oreal_paris"). */
export const slugName = (text) => normalizeName(text).replace(/ /g, "_");

/** The key without any spaces ("lorealparis"): "Maybelline New York" and "MaybellineNewYork" compare equal. */
export const compactName = (text) => normalizeName(text).replace(/ /g, "");

/** The words of the key, in order. */
export function nameWords(text) {
    const key = normalizeName(text);
    return key.length === 0 ? [] : key.split(" ");
}

/** True when both names give the same key (and the key is not empty). */
export function sameName(a, b) {
    const key = normalizeName(a);
    return key.length > 0 && key === normalizeName(b);
}

/**
 * A key with the differences a camera cannot be trusted on taken out: no spaces, "rn" as "m", "vv" as "w", and
 * 0 / o as one letter, 1 / l / i as one letter. `key` = a key from normalizeName.
 */
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

/**
 * Does what was typed in a search box match an entry? Every word typed must be found in one of the entry's texts (its
 * name, its other names, its kind ...): in any order, in any letter case, with or without accents and punctuation, and
 * also as a part of a word ("ink" finds "Superstay Matte Ink"; "super stay" finds "Superstay").
 * An empty search matches everything. `texts` = one text or a list of texts.
 */
export function matchesSearch(typed, texts) {
    const words = nameWords(typed);
    if (words.length === 0) return true;
    const keys = (Array.isArray(texts) ? texts : [texts]).map(normalizeName).filter((key) => key.length > 0);
    if (keys.length === 0) return false;
    const joined = " " + keys.join(" | ");
    // a word of one or two letters must START a word ("c" finds "Vitamin C", not the middle of "Tocopherol")
    if (words.every((word) => joined.includes(word.length <= 2 ? " " + word : word))) return true;
    // typed with the spaces somewhere else: "super stay" for "Superstay", "superstay" for "Super Stay"
    const typedTogether = words.join("");
    return keys.some((key) => key.replace(/ /g, "").includes(typedTogether));
}

// =============================================================================================
//  2. Make-up only
// =============================================================================================
//   "makeup"   - foundation, concealer, powder, blush, bronzer, highlighter, contour, eyeshadow, eyeliner, mascara, brow
//                products, lipstick, lip gloss, lip tint, lip liner, primer, setting spray and the like.
//   "skincare" - moisturizer, serum, cleanser, toner, sunscreen, mask, make-up remover ... : NOT make-up.
//   "out"      - hair, body, nail and fragrance products, and make-up tools: NOT make-up.
//   "unclear"  - cannot tell (no name and no kind), or a kind the project has not decided on yet (UNDECIDED_KINDS).
//                Kept for an admin to look at; not shown in the app and not recommended.

/** The kinds of make-up the app knows by name. The type lists on the website and in the app both use these words. */
export const MAKEUP_TYPES = [
    "Lipstick", "Lip Gloss", "Lip Tint", "Lip Liner", "Foundation", "Concealer", "Primer", "Powder", "Blush", "Bronzer",
    "Highlighter", "Contour", "Eyeshadow", "Eyeliner", "Mascara", "Brow", "Setting Spray"
];

// Each word is looked for as a whole word (a plural with -s or -es counts too).
// Words that always mean make-up.
const STRONG = [
    "lipstick", "lip stick", "lip gloss", "lipgloss", "lip tint", "lip stain", "lip liner", "lipliner", "lip lacquer", "lip cream",
    "lip colour", "lip color", "lip crayon", "lip pencil", "lip mousse", "lip matte", "liquid lip", "lip paint", "lip glaze", "lip shine",
    "eyeshadow", "eye shadow", "eyeliner", "eye liner", "mascara", "blush", "blusher", "concealer", "foundation",
    "eyebrow", "brow pencil", "brow gel", "brow pomade", "brow powder", "brow mascara", "brow pen", "brow liner", "brow tint", "brow wax",
    "brow definer", "brow soap", "soap brow", "kajal", "kohl", "face powder", "pressed powder", "loose powder", "compact powder",
    "setting powder", "finishing powder", "powder foundation", "cushion", "contour", "highlighter", "bronzer", "makeup", "make up",
    "cheek tint", "cheek stain", "cheek color", "cheek colour", "setting spray", "fixing spray", "setting mist", "fixing mist",
    // decided on 12 October 2026 by how they are sold and regulated: BB and CC creams, tinted moisturizers and skin tints are
    // face make-up (shops list them under Makeup > Face; the ASEAN list of cosmetics has "tinted bases")
    "bb cream", "cc cream", "tinted moisturizer", "tinted moisturiser", "skin tint"
];
// Words that mean make-up unless another word says otherwise ("baby powder", "hair gloss", "primer serum").
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
// Skin-care words that only count when the name holds no make-up word at all ("cream blush" is make-up).
export const WEAK_CARE_WORDS = ["cream", "gel", "balm", "mist", "spf", "water", "butter", "oil", "uv"];

// Checked first: these are never make-up, whatever else the name says.
const NEVER_MAKEUP = [
    "makeup remover", "make up remover", "remover", "cleansing", "micellar", "lip scrub", "lip mask", "lip treatment", "lip care",
    "lip serum", "lip conditioner", "brow serum", "lash serum", "eyelash serum", "lash conditioner", "brow conditioner",
    // decided on 12 October 2026: lip balms and lip oils are lip CARE, and a tinted sunscreen is a sunscreen (sun care)
    "lip balm", "lip oil", "tinted sunscreen", "tinted sunblock", "tinted sun screen", "tinted sun block"
];
const NEVER_MAKEUP_OTHER = ["hair mascara", "nail polish", "nail lacquer", "makeup brush", "make up brush", "brush cleaner", "makeup sponge", "make up sponge"];

/**
 * Kinds that are care and colour at once, which no source places clearly. They count as unclear: kept for an admin to
 * look at, not shown, not recommended. They are looked for BEFORE the "never make-up" words (a tinted lip balm is not
 * simply a lip balm). To decide one, move its words to STRONG (it is make-up) or to NEVER_MAKEUP (it is not), here and in
 * the app. (BB and CC cream, tinted moisturizer, skin tint, lip balm, lip oil and tinted sunscreen were decided on
 * 12 October 2026: see those two lists.)
 */
export const UNDECIDED_KINDS = ["tinted lip balm", "tinted lip oil", "lip balm tint", "lip oil tint"];

// Product words that are unmistakable on the front of a pack (used for text a camera read).
const LABEL_NOT_MAKEUP = [
    "shampoo", "hair conditioner", "body wash", "bodywash", "body lotion", "hand cream", "hand lotion", "deodorant", "antiperspirant",
    "eau de parfum", "eau de toilette", "cologne", "nail polish", "nail lacquer", "toothpaste", "mouthwash",
    "facial wash", "face wash", "facial cleanser", "facial foam", "micellar water", "makeup remover", "make up remover", "feminine wash",
    "baby oil", "baby powder", "body scrub", "bath soap", "hand soap", "hand sanitizer"
];

// what the AI answers for a product that is not make-up, and the old catch-all kind "Skincare"
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

// lower case with a space at both ends, punctuation as spaces, and "... free" claims taken out ("fragrance free" says nothing about the kind)
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

// true when the text holds the word as a whole word (also with -s or -es at its end)
const holds = (text, word) => text.includes(" " + word + " ") || text.includes(" " + word + "s ") || text.includes(" " + word + "es ");

// the first word of the list that the text holds, or null
function has(text, words) {
    for (const word of words) if (holds(text, word)) return word;
    return null;
}

const text0 = (value) => (value === null || value === undefined ? "" : String(value));

/**
 * Is this product make-up? `cosmeticType` is the kind someone gave it (an admin, the AI, a user); either may be empty.
 * Returns { kind: "makeup" | "skincare" | "out" | "unclear", family, word }: `family` says what a product that is not
 * make-up is ("skin care", "hair", "body care", "nail", "fragrance", "make-up tool"), `word` is the word that decided it.
 */
export function judgeProduct(cosmeticType, productName) {
    let type = text0(cosmeticType);
    const givenName = text0(productName);
    let typeKey = normalizeName(type);
    if (typeKey === "unknown" || typeKey === "other" || typeKey === "cosmetic" || typeKey === "cosmetics") { type = ""; typeKey = ""; }   // "I cannot tell" is not a kind
    const name = plain(givenName), text = plain(type + " " + givenName);
    if (text.trim().length === 0) return verdict("unclear", "", "");

    // the kind was given as "not make-up" outright: only a make-up word in the name can still make it unclear
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
    if (hair === "hair" && browOrLash) hair = null;                 // "hair-like strokes" on a brow pen is not a hair product
    const nail = has(text, NAIL_WORDS), scent = has(text, SCENT_WORDS), body = has(text, BODY_WORDS), care = has(text, CARE_WORDS);
    const other = hair !== null ? hair : nail !== null ? nail : scent !== null ? scent : body;
    const family = hair !== null ? "hair" : nail !== null ? "nail" : scent !== null ? "fragrance" : body !== null ? "body care" : "";

    const nameSaysMakeup = has(name, STRONG) !== null || has(name, GENERIC) !== null;
    const nameSaysOther = has(name, HAIR_WORDS) !== null || has(name, NAIL_WORDS) !== null || has(name, SCENT_WORDS) !== null
                       || has(name, BODY_WORDS) !== null || has(name, CARE_WORDS) !== null;

    if (strong !== null) {
        if (hair !== null || nail !== null) return verdict("unclear", "", hair !== null ? hair : nail);
        // the make-up word is only in the kind, while the name speaks of something else ("Blush" + "Body Lotion")
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

/**
 * The same question for the raw text a camera read on the FRONT of a pack. Careful on purpose: it only says
 * "not make-up" for unmistakable product words, and never when a make-up word is on the pack as well.
 */
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

/** "makeup", "skincare", "out" or "unclear". */
export const productScope = (cosmeticType, productName) => judgeProduct(cosmeticType, productName).kind;
export const isMakeup = (cosmeticType, productName) => judgeProduct(cosmeticType, productName).kind === "makeup";
/** True when the product is clearly something else than make-up (an unclear product is not). */
export const isNotMakeup = (cosmeticType, productName) => { const kind = judgeProduct(cosmeticType, productName).kind; return kind === "skincare" || kind === "out"; };

// "Lip Cream" is sold as a lipstick, a "Cushion" is a foundation ... : the app's own word for a kind of make-up
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

/**
 * The app's own word for this kind of make-up ("Liquid Lipstick" and "Lip Cream" give "Lipstick"), or "" when the
 * product is not clearly make-up or its kind cannot be told.
 */
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

/** In plain words, why a product is not kept: "a hair product", "a skin care product", "a make-up tool". "" when it is make-up or unclear. */
export function notMakeupWords(judged) {
    if (!judged || (judged.kind !== "skincare" && judged.kind !== "out")) return "";
    if (!judged.family) return "not a make-up product";
    return "a " + judged.family + (judged.family === "make-up tool" ? "" : " product");
}

// =============================================================================================
//  3. Barcodes and the ids the app gives what it adds
// =============================================================================================
/** The digits of a barcode without leading zeros ("" when it cannot be a barcode: fewer than 6 or more than 14 digits). */
export function barcodeKey(barcode) {
    const digits = text0(barcode).replace(/[^0-9]/g, "").replace(/^0+/, "");
    return digits.length >= 6 && digits.length <= 14 ? digits : "";
}

/**
 * True when two brand names mean the same brand: the same once capitals, accents and spaces are taken out, or one is
 * the other with words added ("Maybelline" / "Maybelline New York"). An empty name agrees with any brand.
 */
export function brandsAgree(a, b) {
    const ka = normalizeName(a), kb = normalizeName(b);
    if (ka.length === 0 || kb.length === 0) return true;
    if (ka === kb || ka.replace(/ /g, "") === kb.replace(/ /g, "")) return true;
    const longer = " " + (ka.length >= kb.length ? ka : kb) + " ", shorter = " " + (ka.length >= kb.length ? kb : ka) + " ";
    return shorter.trim().length >= 3 && longer.includes(shorter);
}

/** The id a product gets when the app adds it from a scan: always the same for the same brand and name ("scan_maybelline__superstay_matte_ink"). */
export function scanProductId(brandName, productName) {
    const brand = slugName(brandName), name = slugName(productName);
    if (name.length === 0) return "";
    const id = "scan_" + (brand.length > 0 ? brand : "nobrand") + "__" + name;
    return id.length > 110 ? id.substring(0, 110) : id;
}

/** The id a brand gets when the app adds it from a scan ("scan_maybelline"). */
export function scanBrandId(brandName) {
    const brand = slugName(brandName);
    if (brand.length === 0) return "";
    const id = "scan_" + brand;
    return id.length > 90 ? id.substring(0, 90) : id;
}

// =============================================================================================
//  4. The stoplist: label text that is never part of an ingredient name
// =============================================================================================
// The same list as the app's IngredientStoplist.cs (Assets/_Project/Scripts/Scan). The app's ingredient reader throws
// away a piece of label text that holds one of these; the website uses the list to point out database entries that
// are label text and not ingredients. Each entry is in small letters without punctuation and only matches WHOLE
// WORDS: "ml" matches "30 ml" but not "Emulsifier".
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

/** The stoplist entry this text holds, or null. `text` may be raw label text. */
export function stoplistHit(text) {
    const key = " " + normalizeName(text) + " ";
    if (key.length <= 2) return null;
    for (const group of Object.values(STOPLIST))
        for (const entry of group)
            if (key.includes(" " + entry + " ")) return entry;
    return null;
}

/** The longest text that can still be one ingredient name, and the most words it can have. */
export const INGREDIENT_NAME_LIMITS = { length: 70, words: 7 };

const COLOUR_INDEX = /^C\.?I\.?\s*\p{Nd}{5}$/iu;
const HOLDS_COLOUR_INDEX = /\bC\.?I\.?\s*\p{Nd}{5}\b/iu;
const LETTER = /\p{L}/u, DIGIT = /\p{Nd}/u;

// true when one word of the piece is a code: three or more digits and at most two letters ("L2304A", "B23X9")
function codeLike(piece) {
    for (const word of piece.split(/[ \/\-(),]+/)) {
        let letters = 0, digits = 0;
        for (const c of word) { if (LETTER.test(c)) letters++; else if (DIGIT.test(c)) digits++; }
        if (digits >= 3 && letters >= 1 && letters <= 2) return true;
    }
    return false;
}

/**
 * Simple checks on the SHAPE of a piece of text: true when it cannot be an ingredient name (a number, a code, a phone
 * number, a sentence, far too short or too long). A colourant written as its Colour Index number ("CI 77491") is fine.
 */
export function wrongIngredientShape(piece) {
    const p = text0(piece).trim();
    if (COLOUR_INDEX.test(p)) return false;
    if (p.length < 3 || p.length > INGREDIENT_NAME_LIMITS.length) return true;
    let letters = 0;
    for (let i = 0; i < p.length; i++) if (LETTER.test(p[i])) letters++;
    if (letters < 3 || letters < p.length * 0.5) return true;                        // mostly digits or symbols
    if (p.split(" ").filter((word) => word.length > 0).length > INGREDIENT_NAME_LIMITS.words) return true;   // a sentence
    const holdsColourIndex = HOLDS_COLOUR_INDEX.test(p);
    if (!holdsColourIndex && /\p{Nd}{4,}/u.test(p)) return true;                     // phone numbers and long codes
    if (!holdsColourIndex && codeLike(p)) return true;                               // batch and lot codes such as L2304A
    return false;
}
