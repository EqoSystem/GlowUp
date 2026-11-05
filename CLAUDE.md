# GlowUp

Make-up scanner app (Unity) plus an admin website. Both share one Firestore database.

## Folder map
- `GlowUp/` Unity app. Own code: `Assets/_Project/` (Scripts, Editor, Scenes). `Assets/Plugins/Android/GlowUp*.java` is our Android code.
- `GlowUp-Admin_AI_Testing/` admin website, plain static HTML/JS, deployed to Vercel (`glowup-admin`).
  - `_Index/`, `_Admin_*/` pages; `_Shared/` code used by every page; `_Database/firebase-console.js` Firebase setup.
  - `api/assist-ingredient.js` Vercel function (Gemini key is a Vercel env var only).
  - `firestore.rules`, `firebase.json`, `.firebaserc` (project `glowup-admin-portal`).
  - `tests/*.test.mjs` and `tools/` checks, run by hand with `node`.
- `docs/` PDFs. `_quarantine/` moved files, restorable. See `CLEANUP_LOG.md`.

## Firestore data contract (full text: `GlowUp-Admin_AI_Testing/FIRESTORE_SCHEMA.md`)
- `brands/{id}`: name, category (Makeup), aliases[], market, status (Active|Pending|Inactive), scans, source, reviewed.
- `products/{id}`: name, brandId, brandName, cosmeticType, ingredients[], shades[], sources[], status, scans, source, reviewed, barcode. App-added ids start `scan_`.
- `ingredients/{id}`: name (shown as INCI name), classificationId, purpose, riskLevel (safe|caution|harmful), description, aliases[], sources[], source, reviewed. App-added ids start `ai_`.
- `ingredientClassifications/{id}`: name. Everyone reads, admins write; `other` is never deleted.
- `productIngredientSuggestions`: app creates, admin reviews.
- `scanActivity`: anonymous, no user id. status safe|caution|harmful, scannedAtUnix, counts.
- `allergenSuggestions`, `conditionSuggestions`: app creates, admins read.
- `users/{uid}` and `users/{uid}/scans`: that user only. `admins/{uid}`: set by hand in the console.
- The app may only create Pending/unreviewed entries and add 1 to `scans`. Make-up and face products only.

## Rules
- Read `graphify-out/GRAPH_REPORT.md` first, then use grep.
- Never read excluded folders (see `.graphifyignore`): `Library`, `Temp`, `Plugins`, `StreamingAssets`, `Firebase`, backups, SDKs.
- Never print secrets: the AI key line in `SecureProfileStore.cs`, `.env*`, admin password.
- Do not move or delete anything under `GlowUp/Assets/` outside the Unity Editor (`.meta` files).
- After large changes, run `/graphify . --update`.
- Commit a checkpoint before edits.

## graphify
Knowledge graph in `graphify-out/` (GRAPH_REPORT.md, graph.json, graph.html).
Read GRAPH_REPORT.md first, then grep. Do not run `graphify query` before grep.
After code changes run: `"C:\Users\HIFI BLITZ\AppData\Local\Python\pythoncore-3.14-64\Scripts\graphify.exe" update .` (AST only, no API cost).
