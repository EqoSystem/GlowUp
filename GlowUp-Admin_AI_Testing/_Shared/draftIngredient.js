// One new Curator entry from a name alone, for both "Search and import" panels (inventory and Curator). PubChem names and hazard
// codes plus GlowUp's name rules. Never sets a risk level: the entry is saved as Pending and an admin reviews it.
import { lookupIngredient, lookupHazards } from "./quickFill.js";
import { suggestFit } from "./ingredientFit.js";
import { planFit } from "./fitReasons.js";
import { pubchemPage, suggestClassification, friendlyStarter } from "./smartAdd.js";

export const MAX_NAMES = 30;

/** Typed names: one per line, or separated by ";" or ", ". A comma without a space stays inside the name (1,2-Hexanediol). */
export function parseNames(text) {
    const seen = new Set();
    return String(text || "").split(/[\r\n;]+|,\s+/).map((n) => n.replace(/\s+/g, " ").replace(/,+$/, "").trim().slice(0, 80)).filter((n) => {
        const key = n.toLowerCase();
        if (n.length < 2 || seen.has(key)) return false;
        seen.add(key);
        return true;
    });
}

let refs;
async function loadRefs() {
    if (refs !== undefined) return refs;
    try { const r = await fetch(new URL("./data/ingredientRefs.json", import.meta.url).href, { credentials: "omit" }); refs = r.ok ? await r.json() : null; }
    catch (error) { refs = null; }
    return refs;
}

/** strict: a failed PubChem call is thrown (the Curator says so) instead of saving an entry that only looks unknown. */
export async function draftIngredient(name, { strict = false } = {}) {
    const table = await loadRefs();
    const found = await lookupIngredient(name).catch((error) => { if (strict) throw error; return null; });
    const key = (t) => String(t || "").toLowerCase().replace(/[^a-z0-9]+/g, "");
    const aliases = (found ? found.aliases : []).filter((a) => key(a) !== key(name)).slice(0, 6);
    const page = found && found.cid ? pubchemPage(found.cid) : "";
    const fit = suggestFit([name].concat(aliases), table);
    const hazards = found && found.cid ? await lookupHazards(found.cid).catch(() => null) : null;
    const plan = planFit({ rules: fit, hazards });
    const sources = Array.from(new Set([page ? "PubChem | " + page : ""].concat(plan.sources).filter(Boolean))).slice(0, 10);
    return {
        name: String(name).replace(/\s+/g, " ").trim().slice(0, 80), aliases, identified: !!page || fit.rules.length > 0,
        allergenTags: plan.allergenTags, avoidForSkinTypes: plan.avoidForSkinTypes, sources,
        cautionNote: "", fitWhy: fit.why.join(" ").slice(0, 400), chemical: found ? String(found.chemical || "").slice(0, 600) : "", page
    };
}

/** The Firestore fields of a drafted entry. `stamp` is serverTimestamp(); `classes` fills the classification when the name says it. */
export function ingredientRecord(d, { uid, via, stamp, classes = [], maxPurpose = 120 }) {
    const kind = suggestClassification(d.name, d.chemical, classes);
    const kindName = kind ? kind.name.split("/")[0].trim() : "";
    const data = {
        name: d.name, techName: "", aliases: d.aliases || [], classificationId: kind ? kind.id : "", purpose: kindName.slice(0, maxPurpose),
        description: kind ? friendlyStarter(d.name, kindName) : "", chemicalDescription: d.chemical || "",
        riskLevel: "", cautionNote: "", commonUses: "", minAge: 0, allergenTags: d.allergenTags || [], avoidForSkinTypes: d.avoidForSkinTypes || [],
        avoidForConditions: [], goodForSkinTypes: [], goodForConditions: [], sources: d.sources || [], folder: "", source: "import", via,
        reviewed: false, identified: d.identified !== false, fitAuto: (d.allergenTags || []).length + (d.avoidForSkinTypes || []).length > 0, fitAutoWhy: d.fitWhy || "",
        detailsAutoChecked: false, addedBy: uid, createdAt: stamp, updatedAt: stamp
    };
    if (d.page) data.aliasesSourceUrl = d.page;
    return data;
}
