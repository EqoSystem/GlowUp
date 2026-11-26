// What Quick fill ticks on an ingredient, and the note it shows under every group. No DOM, no network, no AI.
// Two inputs: GlowUp's name rules (ingredientFit.js) and the skin hazard codes PubChem lists for the substance (ECHA C&L notifications).
// Turning a hazard code into a skin type or condition is GlowUp's own rule (docs/SOURCES_REGISTER.md); a code describes the pure
// substance, not the amount in make-up. Nothing found is never called neutral or safe.

const HAZARDS = {
    H315: { text: "H315 causes skin irritation", types: ["Sensitive"], conditions: ["irritantcontact"] },
    H317: { text: "H317 may cause an allergic skin reaction", types: ["Sensitive"], conditions: ["allergiccontact", "contact"] }
};
const LEAST_SHARE = 50;      // percent of notifiers: below this a self-reported code is left out
const NOTHING = "Not filled: no hazard or rule found for this name. This does not mean it is safe.";
const NO_ANSWER = "Not filled: no rule found, and PubChem has no entry for this name or did not answer.";

/** The skin codes in a PubChem "GHS Classification" answer, sorted. */
export function hazardCodes(view) {
    const shares = {};
    const walk = (node) => {
        if (typeof node === "string") {
            const m = /^(H31[57])(?: \(([\d.]+)%\))?:/.exec(node);
            if (!m) return;
            const share = m[2] === undefined ? null : Number(m[2]);
            if (share !== null) shares[m[1]] = Math.max(shares[m[1]] || 0, share);
            else if (!(m[1] in shares)) shares[m[1]] = null;
        } else if (node && typeof node === "object") Object.values(node).forEach(walk);
    };
    walk(view);
    return Object.keys(shares).filter((code) => shares[code] === null || shares[code] >= LEAST_SHARE).sort();
}

const urlOf = (line) => String(line || "").split("|").pop().trim();

/**
 * rules = suggestFit()'s answer; hazards = { ok, codes, url } from PubChem, or null.
 * Returns what to tick (avoid lists only), the reference lines used, and notes { allergens, skinTypes, conditions, risk }: [{ text, url }].
 */
export function planFit({ rules, hazards }) {
    const out = { allergenTags: [], avoidForSkinTypes: [], avoidForConditions: [], umbrella: false, sources: [], notes: { allergens: [], skinTypes: [], conditions: [], risk: [] } };
    const add = (into, values) => values.forEach((v) => { if (v && !into.includes(v)) into.push(v); });
    const take = (allergen, types, conditions, text, source) => {
        add(out.allergenTags, [allergen]); add(out.avoidForSkinTypes, types); add(out.avoidForConditions, conditions);
        const note = { text, url: urlOf(source) };
        if (allergen) out.notes.allergens.push(note);
        if (types.length) out.notes.skinTypes.push(note);
        if (conditions.length) out.notes.conditions.push(note);
        add(out.sources, [source]);
    };
    ((rules && rules.details) || []).forEach((d) => {
        take(d.allergen, d.types, d.conditions, `GlowUp rule: ${d.why}` + (d.source ? "" : " (no source page for this rule yet)"), d.source);
        if (d.umbrella) out.umbrella = true;
    });
    if (hazards && hazards.ok) hazards.codes.forEach((code) => {
        const h = HAZARDS[code];
        if (h) take("", h.types, h.conditions, `PubChem (ECHA notifications): ${h.text}. This is about the pure substance: check it.`, `PubChem safety and hazards | ${hazards.url}`);
    });
    const failed = !!hazards && !hazards.ok;
    ["allergens", "skinTypes", "conditions"].forEach((group) => {
        if (out.notes[group].length === 0) out.notes[group].push({ text: failed ? NO_ANSWER : NOTHING, url: "" });
    });
    out.notes.risk.push({ text: "Not filled: risk level needs a regulator or medical page. Set it yourself.", url: "" });
    return out;
}
