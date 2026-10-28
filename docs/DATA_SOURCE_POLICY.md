# Data source policy: what a source may be used to support

GlowUp does not wait for dermatologists and pharmacists to agree on one source. It replaces opinion with rules that can be checked: every claim in
the database is tied to the **kind of source** that is allowed to support it, and the app only says things that a source of that kind supports.

The kind of a source page is worked out from its web address by `sourceKind` in `GlowUp-Admin_AI_Testing/_Shared/glowupRules.js` (the same rule is in the
app, `ListSources.SourceKind`, and in `api/assist-ingredient.js`, and all three are tested against `SharedRuleCases.txt`). The tier is worked out
from the kind by `evidenceTier` in the same file. Nothing about a source is stored besides the line `page title | https://address`.

## The tiers

| Tier | Source kind | Examples (the lists are in `glowupRules.js`) | May support |
|---|---|---|---|
| **T1** Regulation | `regulator` (regulation pages) | EU Cosmetics Regulation on europa.eu / eur-lex, ASEAN (aseancosmetics.org, asean.org), FDA Philippines (fda.gov.ph), US FDA (fda.gov), TGA, Health Canada, gov.uk, ECHA | A rating of **Harmful** (prohibited or restricted for cosmetics), or Caution |
| **T2** Safety assessment and science | `medical`, and `nih.gov` / `who.int` pages that are not databases | CIR (cir-safety.org), DermNet, American Academy of Dermatology, journals (ScienceDirect, Wiley, Springer, Nature, JAMA, BMJ), universities (.edu), PubMed, WHO | A rating of **Caution**, usage notes |
| **T3** Reference databases | `reference` | INCI Beauty, INCIDecoder, Paula's Choice dictionary, EWG, CosmeticsInfo, Personal Care Products Council, **PubChem** and **EU CosIng** (their addresses sit on regulator hosts, so `evidenceTier` overrides the host: nih.gov PubChem pages and europa.eu pages with `/cosing` or `cosmetic-ingredient-database` in the path are tier 3) | What an ingredient **is**: INCI name, aliases, function, plain-language description. Not a rating |
| **T4** Packaging side | `maker`, `retailer`, `public` | The brand's own confirmed site, shops (Watsons, Shopee, Lazada, Sephora ...), Open Beauty Facts | Which ingredients are **in a given product**. Nothing about risk |
| none | `other` | Any site that is not recognised, and any address that is not plain (`https://fda.gov@evil.com`) | Nothing |

The packaging itself is the best source of what is in a product. A shop's or a volunteer's list is a copy of it and can be out of date.

## The rules the code enforces

1. **Harmful needs a T1 page.** In the Ingredient Curator a new Harmful rating cannot be saved, and an AI entry rated Harmful cannot be approved, unless
   at least one of its reference links is a T1 page. Bulk approve leaves such entries out and says so. An entry that was already Harmful keeps working
   (so that old entries can still be edited), and the Curator lists them under "Harmful without a regulator's page" until a page is added.
2. **Caution from the AI is approved by itself only with a T1 or T2 page.** An ingredient database alone no longer counts. Anything else waits for an admin.
3. **Entries that came from a file import or from a user's photo are never approved by themselves** (they were not before this policy either).
4. **A rating of Safe, or no rating, is never approved by itself**, because an invented "safe" could hide a harmful ingredient.
5. **Unknown is not safe.** An ingredient that is not in the database is shown as "Not checked" and is not counted as checked (`ProductAnalysisEngine`).
   An unreviewed AI entry may warn but never clears an ingredient.
6. **Who approved it is recorded**: `reviewedBy` (the admin's account id) next to `reviewed` and `reviewedAt`.

## What the app may say

The app does not say "unsafe". It says what it can show:

- *Contains an ingredient you marked to avoid* (the user's own allergen matched: objective, needs no expert);
- *Restricted or prohibited under [regulator]* (a T1 page backs the Harmful rating);
- *Caution: [note]* (T1 or T2, or an admin's own note);
- *Not checked* (the name is not in the database yet);
- and always: *informational only, not medical advice* (Terms section 3, and the note under results).

## Seed data

Nothing in the database was typed from memory for this policy. Two tools exist to bring in data **with a source attached**:

- `GlowUp-Admin_AI_Testing/tools/cosingToImport.mjs` turns the EU CosIng ingredient file (downloaded by a person from the European Commission) into the website's ingredient import
  format. It writes INCI names, CAS numbers and functions only. It never writes a risk level.
- `GlowUp-Admin_AI_Testing/tools/templates/t1_restricted_template.csv` is an empty template for the prohibited and restricted ingredients of a regulation. A person fills it
  from the regulation's own annex and writes the annex and entry number in the `reference` column; `GlowUp-Admin_AI_Testing/tools/t1ToImport.mjs` turns it into the import format
  with the regulator link as the source. The template contains **no ingredient rows** on purpose.

## Open points (decisions for a person)

- **CosIng.** A CosIng page is tier 3 by rule (it lists names, functions and where a substance appears in the regulation's annexes, but it is not the
  regulation). Cite the EUR-Lex annex itself for Harmful.
- **US FDA.** `fda.gov` hosts regulation and also advice pages. An admin should link the specific page that states the prohibition or restriction.
- **Two admins.** The brief suggested requiring two different admins when no T1 to T3 page exists. This was not built: with a small admin team it would stall
  every entry, and the evidence rule above plus `reviewedBy` give traceability. Revisit if the team grows.
