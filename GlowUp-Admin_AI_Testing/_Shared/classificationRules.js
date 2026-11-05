// The rules of ingredient classifications and of the field change that came with them. No browser, no database: tests/classification.test.mjs checks them.
//
// A classification is one document in "ingredientClassifications" ({ name }). An ingredient points at ONE by its document id
// (classificationId), never by its text, so a rename reaches every ingredient at once.
// "other" is the safe fallback: it can be renamed but never deleted.

export const COLLECTION = "ingredientClassifications";

// TEST COPY. Only the live site (and the Firebase hosts of the project) use the real collection. Any other address (a Vercel preview, localhost)
// reads and writes "ingredientClassificationsTest" instead, so looking at a preview can never put anything into the live list.
export const TEST_COLLECTION = "ingredientClassificationsTest";
export const LIVE_HOSTS = ["glowup-admin-snowy.vercel.app", "glowup-admin-portal.firebaseapp.com", "glowup-admin-portal.web.app"];
export const isTestHost = (hostname) => !LIVE_HOSTS.includes(String(hostname || "").toLowerCase());
export const collectionFor = (hostname) => (isTestHost(hostname) ? TEST_COLLECTION : COLLECTION);

// TWO SEPARATE DECISIONS about saving an ingredient that already exists and has nothing in these boxes yet (all 937 start that way):
//   CLASSIFICATION_REQUIRED_ON_EXISTING (asked for 5 November 2026): false = such an ingredient can still be saved while another box is edited.
//   PURPOSE_REQUIRED_ON_EXISTING (my decision, not asked for; same reason; change it here alone): false = the same for Purpose.
// A NEW ingredient always needs both, and an existing one that has them cannot lose them.
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

/** Spaces tidied, nothing else. */
export const cleanName = (value) => String(value === null || value === undefined ? "" : value).replace(/\s+/g, " ").trim();
/** "preservative" and "Preservative " are the same name. */
export const nameKey = (value) => cleanName(value).toLowerCase();

/**
 * The fixed id of a starting classification, made from its name: lowercase, every run of spaces, slashes and other signs becomes one
 * hyphen ("pH adjuster" -> "ph-adjuster", "Wax / Structuring agent" -> "wax-structuring-agent", "Other" -> "other"). Seeding writes by
 * this id, so two admins opening the Curator at the same moment write the same documents and nothing is duplicated.
 */
export const startingId = (name) => cleanName(name).toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "");

/**
 * The starting classifications this list does not have yet: [{ id, name }]. One is "there" when its name matches one already in the
 * list (capital letters and extra spaces ignored) or its id is taken (it was renamed: "other" stays the fallback whatever it is called).
 * Nothing is ever removed or renamed.
 */
export function missingStarting(existing) {
    const have = existing || [];
    return STARTING_NAMES.map((name) => ({ id: startingId(name), name }))
        .filter((item) => !have.some((c) => c.id === item.id || nameKey(c.name) === nameKey(item.name)));
}

/** The whole starting list with its ids (what an empty database gets). */
export const startingList = () => missingStarting([]);

/** What is wrong with a typed name ("" = nothing). `ownId` = the classification being renamed. */
export function checkName(list, typed, ownId) {
    const name = cleanName(typed);
    if (!name) return "Type a name.";
    if (name.length > MAX_NAME) return `A name can have at most ${MAX_NAME} characters.`;
    if ((list || []).some((item) => item.id !== ownId && nameKey(item.name) === nameKey(name))) return "This classification already exists";
    return "";
}

/** The ingredients that point at this classification. */
export const usersOf = (ingredients, id) => (ingredients || []).filter((item) => item.classificationId === id);

/** False for the fallback: it can never be deleted. */
export const canRemove = (id) => id !== FALLBACK_ID;

/** True when an ingredient points at no classification that exists. */
export const needsClassification = (item, list) => !item.classificationId || !(list || []).some((c) => c.id === item.classificationId);

/**
 * The aliases of an ingredient once its old INCI value is one of them: added at the end, only when it differs from the name and
 * from every alias (capital letters and extra spaces do not count as a difference).
 */
export function aliasesWithInci(name, techName, aliases) {
    const list = Array.isArray(aliases) ? aliases.slice() : [];
    const tech = cleanName(techName);
    if (!tech || nameKey(tech) === nameKey(name) || list.some((alias) => nameKey(alias) === nameKey(tech))) return list;
    list.push(tech);
    return list;
}

/** Plain words for a count and for the first alias. */
export const firstAlias = (item) => (Array.isArray(item.aliases) && item.aliases.length > 0 ? item.aliases[0] : "");

/**
 * What the field change does to the documents as they are in the database: [{ id, data }] plain objects.
 * Nothing is guessed: the classification and the purpose stay empty. An ingredient that was changed already is left alone, so a second
 * run only finishes what the first did not reach (and what the app's AI look-up added since).
 */
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

/** True while something is left for the field change to do. */
export const migrationWaits = (documents, classifications) => (classifications || []).length === 0 || migrationPlan(documents).updates.length > 0;
