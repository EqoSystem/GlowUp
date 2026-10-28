# Thesis fixes: written-text corrections

For the owner, to bring the paper in line with the app as built. Figures, diagrams and the Methodology chapter are out of scope here (they are outdated
and will be redone from the code; `docs/ARCHITECTURE_AS_FOUND.md` has Mermaid diagrams drawn from the code).

**The thesis PDF is not in the repository**, so no item below could be checked against the paper's own text. Section A lists the places where the app
deliberately differs from what the paper promises (from the owner's summary of the Objectives, Scope and Limitations); section B lists corrections the
owner raised earlier, which must be confirmed in the PDF before they are applied.

Format: *what the paper says -> what the app does -> why the app's way is better -> suggested wording.*

## A. The paper should follow the app

1. **Regulatory databases (Objective 1).**
   Paper: the app cross-references extracted chemicals "against personal and regulatory databases".
   App: it compares the list with the user's profile on the phone, and with GlowUp's own ingredient list, which admins curate from regulator pages
   (a Harmful rating needs a regulator's page) and other cited sources. It does not query a regulator live.
   Why: regulators publish annexes and notification portals, not a database of every cosmetic's ingredients that a program can query; a curated list
   with the regulation cited per entry is checkable and works offline.
   Suggested: *"... cross-references the ingredients read from the label against the user's Skin Profile and against a curated ingredient database whose
   restrictions cite the regulator that imposes them (EU, ASEAN / FDA Philippines, US FDA)."*

2. **"AI recommendation" (Objective 3).**
   Paper: an AI recognition and recommendation system that suggests products ("ingredients and performance benefits").
   App: recognition uses machine learning (ML Kit text and barcode reading, an on-device picture model) and a generative AI only as a last step;
   recommendations are **rules**: make-up of the same kind, nothing flagged for the user's profile, stricter for users with a skin condition.
   Why: rules can be explained and tested (`ProductRecommenderTests`); an AI recommendation could not be defended for a health-related choice.
   Suggested: *"... an AI-assisted product recognition and ingredient analysis, and rule-based recommendations of make-up that contains nothing flagged
   for the user's Skin Profile. Ingredients known for a benefit (key actives) are highlighted."*

3. **FDA notification is not an ingredient source.**
   If the paper says the database is restricted to, or verified by, FDA-notified products: the app checks FDA Philippines per product and shows it as a
   badge, and never takes ingredients from it (the portal does not publish ingredient lists).
   Suggested: *"FDA Philippines notification is checked and shown as proof that a product may be sold; ingredient lists come from the packaging and
   cited sources."*

4. **"Curated, verified database".**
   App: every entry carries where it came from (`source`, `sources`), whether an admin reviewed it (`reviewed`, `reviewedBy`), and the kind of each source
   page; rules decide which kind of source may support which rating (`docs/DATA_SOURCE_POLICY.md`).
   Suggested: *"a curated database in which every rating is tied to a cited source of a defined kind (regulation, safety assessment, reference database,
   packaging), and every entry records who reviewed it."*

5. **Real-time AR on every phone (Objective 4).**
   App: real-time face tracking needs a phone with ARCore; other phones get a guided preview.
   Suggested: *"... real-time AR try-on on phones that support ARCore, with a guided preview on other phones."*

6. **Shade matching.**
   If the paper speaks of personalised shade matching: the app suggests shades from the skin tone chosen in onboarding; it does not measure skin colour
   with the camera. Shade codes are read from the pack and looked up **within that product** only.
   Suggested: *"suggests shades based on the skin tone saved in the Skin Profile, and recognises the shade code printed on a scanned product."*

7. **Text recognition wording.**
   App: label text is read on the phone with Google ML Kit (machine-learning OCR); a generative AI is called only when the product cannot be named.
   Suggested: *"on-device text recognition (Google ML Kit)"* where the paper says "AI text recognition".

8. **Scanner accuracy target.**
   If the paper sets "100% of Global Safety Flag ingredients" on 20 products: OCR cannot promise that. `tools/ScannerEval` measures ingredient-level precision
   and recall on labelled packs; a defensible target is 0.90 precision and recall and 0.95 flag recall on real packs (to be measured).
   Suggested: *"... measured as ingredient-level precision and recall on 20 real products; the target is 0.90 or better, and a name the app cannot read
   is shown as not checked, never as safe."*

9. **Limitations to add.** OCR misses on curved or glossy packs; on-screen colours are approximate; FDA status says nothing about ingredients; AI steps
   need the internet and a free quota.

10. **Scope additions.** The Scope lists fewer modules than were built (see `THESIS_ALIGNMENT.md` section 2): Recycle Bin, file import, Assist with AI,
    Pending Snapshot Reviews, Settings, Delete My Account, finger-sign navigation, typed entry, offline use. Either add them or call them "additional features".

## B. Raised earlier by the owner, to be confirmed in the PDF

Not checked (the PDF is not in the repository). Apply only after confirming each one in the text:

- The title page and the endorsement form list three authors, while the abstract and the approval sheet list four (Azcueta).
- "Scientific name" should read **"INCI name"** (the name printed on labels). The admin website now uses "INCI name" too.
- Adawiyah et al.: the year differs between the in-text citation (2024) and the reference list (2025).
- "Carreon et a" should read "Carreon et al.".
- The reference list is not in alphabetical order.
- Santiago-Rivera (2025) and the other cited sources: confirm each exists as cited (author, year, title, venue). Do not replace a source that cannot be
  found with another one without reading it.
- "Digital Medical ID" (if used) should read **"Skin Profile"**: the app never uses medical-ID wording, and the app is not a medical device (Terms section 3).
