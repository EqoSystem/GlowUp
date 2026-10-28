# Admin guide: adding a product in about two minutes

For the GlowUp admin website (`glowup-admin` on Vercel). You need an admin account (a document in `admins` with your account id; see
`GlowUp-Admin_AI_Testing/FIRESTORE_SCHEMA.md`, "Admin accounts").

## Before you start: what counts as a source

The **pack in your hand** is the best source of what a product contains. Take a sharp photo of the ingredient panel first; you type from it.
A shop page (Watsons, Shopee, Lazada ...) or Open Beauty Facts is a copy of the pack and can be out of date: use it only when you have no pack,
and say so in the sources. **FDA Philippines tells you whether a product is notified (legal to sell), never what is in it.** Do not use it as an
ingredient source. The full rules are in `docs/DATA_SOURCE_POLICY.md`.

## Add a product (Brand & Product Inventory)

1. **Add > Product.** If the brand is not listed yet, first **Add > Brand** (name exactly as printed on the pack, category Makeup).
2. **Product Name**: as printed on the front, without the brand. **Brand**: pick it. **Cosmetic Type**: one of the 17 make-up kinds (it tells AR Try-On
   where the colour goes). Skin care, hair, body, nail and fragrance products are refused: GlowUp lists make-up only.
3. **Ingredients**: type or paste the list from the pack, in the printed order. Commas, semicolons or one name per line all work. Names after
   "May contain" or "+/-" are the shade colours; keep them at the end.
4. **Shades** (optional): add each colour as `#RRGGBB` and, next to it, the **shade code printed on the pack** (`119`, `N20`). A code is only
   unique inside this product: the same number on another product means something else, and the form refuses the same code twice in one product.
   The colour on screen is approximate; say so if you estimated it.
5. **Sources**: the product page of the maker (if the brand's website is confirmed) or the shop page you used. Add the **FDA Philippines
   verification** link in its own box if you checked it (legality only), and the **FDA registration number** if it is printed.
6. **Status**: Active when you typed it from the pack; Pending if you want a second look later. **Save.**

The ingredient names that the Ingredient Curator does not explain yet now appear in the Curator under "ingredients that products in the inventory
list have no entry here yet", most used first. Explain the most used ones first: one entry helps every product that lists the name.

## Faster: import a file instead of typing

Drop a file on the import box. No AI and no quota is used for:

- a file in the GlowUp import format (`GlowUp-Admin_AI_Testing/IMPORT_FORMAT.md`): Word, PDF, Excel, **CSV** or text;
- a product sheet that labels each product with `Brand:`, `Product:` and `Ingredients:` lines (Word, PDF, text).

Any other document is read by the AI (that uses the free AI quota). Everything an import creates arrives as **Pending / not reviewed**; nothing reaches
the app until you approve it. Skin care and other non-make-up rows are flagged and not saved. Image-only (scanned) PDFs cannot be read: export the text,
or type from the pack.

## Approving what others added

- **Brand & Product Inventory**: products and brands the app met in scans (`scan_...`), imports, and photo snapshots (the Pending Snapshot Reviews window).
  Compare the list with the pack before you press Approve. Your account is recorded as the reviewer (`reviewedBy`).
- **Ingredient Curator**: entries the app's AI found wait for review. A **Harmful** rating can be saved or approved only with a regulator's page among
  its reference links (EU Cosmetics Regulation annex on EUR-Lex, ASEAN, FDA Philippines or US FDA). Without one, lower it to Caution or add the page.
  An ingredient database (INCIDecoder, CosIng, PubChem ...) explains what an ingredient is; it is not a reason for a rating.

## Filling the ingredient list from published sources (optional)

- `GlowUp-Admin_AI_Testing/tools/cosingToImport.mjs` turns a CosIng download into CSV files for the Curator (names, other names, CAS, function; never a rating).
  Give it the list of names you need (`--only names.txt`), not the whole inventory.
- `GlowUp-Admin_AI_Testing/tools/templates/t1_restricted_template.csv`: fill one row per prohibited or restricted substance **from the regulation's annex**,
  with the annex and entry number, then run `tools/t1ToImport.mjs`. Rows without a regulator page are rejected with the reason.
