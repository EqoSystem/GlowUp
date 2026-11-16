





export const COLLECTION = "ingredientClassifications";



export const TEST_COLLECTION = "ingredientClassificationsTest";
// glowupapp.xyz is the production domain (an alias of the same Vercel project): without it the live site ran as a test copy
export const LIVE_HOSTS = ["glowupapp.xyz", "www.glowupapp.xyz", "glowup-admin-snowy.vercel.app","glowup-admin-portal.firebaseapp.com", "glowup-admin-portal.web.app"];
export const isTestHost = (hostname) => !LIVE_HOSTS.includes(String(hostname || "").toLowerCase());
export const collectionFor = (hostname) => (isTestHost(hostname) ? TEST_COLLECTION : COLLECTION);





export const CLASSIFICATION_REQUIRED_ON_EXISTING = false;
export const PURPOSE_REQUIRED_ON_EXISTING = false;
export const FALLBACK_ID = "other";
export const MAX_NAME = 40;
export const MAX_PURPOSE = 120;
export const STARTING_NAMES = [
    "Preservative", "Antioxidant", "Chelating agent", "pH adjuster", "Emulsion stabiliser", "Emulsifier", "Thickener", "Solvent", "Binder",
    "Filler / Bulking agent", "Film former", "Wax / Structuring agent", "Emollient", "Humectant", "Skin conditioning agent", "Mattifier / Absorbent",
    "Slip agent / Smoothing agent", "Colorant / Pigment", "Opacifier", "Shimmer / Light reflector", "Fragrance", "UV filter", "Other"
];


export const cleanName = (value) => String(value === null || value === undefined ? "" : value).replace(/\s+/g, " ").trim();

export const nameKey = (value) => cleanName(value).toLowerCase();






export const startingId = (name) => cleanName(name).toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "");






export function missingStarting(existing) {
    const have = existing || [];
    return STARTING_NAMES.map((name) => ({ id: startingId(name), name }))
        .filter((item) => !have.some((c) => c.id === item.id || nameKey(c.name) === nameKey(item.name)));
}


export const startingList = () => missingStarting([]);


export function checkName(list, typed, ownId) {
    const name = cleanName(typed);
    if (!name) return "Type a name.";
    if (name.length > MAX_NAME) return `A name can have at most ${MAX_NAME} characters.`;
    if ((list || []).some((item) => item.id !== ownId && nameKey(item.name) === nameKey(name))) return "This classification already exists";
    return "";
}


export const usersOf = (ingredients, id) => (ingredients || []).filter((item) => item.classificationId === id);


export const canRemove = (id) => id !== FALLBACK_ID;


export const needsClassification = (item, list) => !item.classificationId || !(list || []).some((c) => c.id === item.classificationId);





export function aliasesWithInci(name, techName, aliases) {
    const list = Array.isArray(aliases) ? aliases.slice() : [];
    const tech = cleanName(techName);
    if (!tech || nameKey(tech) === nameKey(name) || list.some((alias) => nameKey(alias) === nameKey(tech))) return list;
    list.push(tech);
    return list;
}


export const firstAlias = (item) => (Array.isArray(item.aliases) && item.aliases.length > 0 ? item.aliases[0] : "");






export function migrationPlan(documents) {
    const updates = [];
    let moved = 0, sameAsName = 0, alreadyAlias = 0;
    (documents || []).forEach((entry) => {
        const hasTech = Object.prototype.hasOwnProperty.call(entry, "techName");
        const marked = Object.prototype.hasOwnProperty.call(entry, "classificationId");
        if (!hasTech && marked) return;
        const before = Array.isArray(entry.aliases) ? entry.aliases : [];
        const aliases = aliasesWithInci(entry.name, entry.techName, before);
        if (aliases.length > before.length) moved++;
        else if (hasTech && cleanName(entry.techName) && nameKey(entry.techName) === nameKey(entry.name)) sameAsName++;
        else if (hasTech && cleanName(entry.techName)) alreadyAlias++;
        updates.push({ id: entry.id, name: entry.name, techName: entry.techName || "", aliases, movedInci: aliases.length > before.length, hasTech, marked,
            hasPurpose: Object.prototype.hasOwnProperty.call(entry, "purpose"), binned: entry.binned === true });
    });
    return { updates, moved, sameAsName, alreadyAlias };
}


export const migrationWaits = (documents, classifications) => (classifications || []).length === 0 || migrationPlan(documents).updates.length > 0;
