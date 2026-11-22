# GlowUp: Paper vs System Audit and Panel Question Bank

Checked: 19 November 2026, against branch `ar-latency` (app v0.71.0, code 71) and the admin site in `GlowUp-Admin_AI_Testing/`.
Paper checked: `GlowUp_FinalPaper_edited_Echo.pdf` (193 pages).

Severity: **High** = a panelist can prove the paper wrong from the app or code. **Medium** = inconsistent or unsupported. **Low** = formatting.

---

## Part 1. Where the paper and the system disagree

### 1.1 High: claims the system does not back up

| # | Paper says (page) | System actually does | Fix in paper or code |
|---|---|---|---|
| 1 | Skin Profile is "encrypted" and kept in a "secure local database" (p.5, p.9, test PROF-04) | Profile is cached as **plain JSON** in `persistentDataPath/GlowUpCache` (`Scripts/Data/OfflineCache.cs:14-22`). No AES or Android Keystore code found. Cloud copy is in Firestore `users/{uid}` (Google encrypts at rest, per-user rules). | Either add encryption (Android Keystore + AES-GCM) or reword: "stored in app-private storage; cloud copy protected by Firestore rules and Google's encryption at rest". PROF-04 will fail as written. |
| 2 | Appendix B "Product Scanner.cs" is the "relevant source code" | `Scripts/AI/ProductScanner.cs` is **legacy**. It sends the user's allergens, skin type and condition to Gemini (lines 259-260) and injects fake ingredients "Water, Glycerin, Niacinamide, Fragrance, Phenoxyethanol" when OCR reads nothing (line 219). This contradicts the paper's own claim that recommendations are matched "on the phone without sending personal data anywhere" (p.9, test DASH-04). It is still in `AiSmartAnalysisScene` (AR Try-On button calls `OnARTryOnClicked`). | Replace Appendix B with the live code: `Scan/ScanAnalysisPipeline.cs`, `Auth/Argon2idHasher.cs`, a `firestore.rules` excerpt, `api/app-ai.js`. Consider removing ProductScanner from the scene. |
| 3 | "Unity 6 (LTS)" (p.23); Table 2 "Unity 6 / 2022.3" | `ProjectVersion.txt`: **2022.3.62f3**. Not Unity 6. | Say Unity 2022.3.62f3 LTS everywhere. Drop "6000.3.x" and AR Foundation "v6.4.1" (actual: 5.2.2). |
| 4 | "FDA Cosmetic Ingredient Reference Database (fdaCatalogue.json) ... ingredient risk scoring, allergen classification ... detect prohibited and restricted compounds" (p.25, p.50) | `fdaCatalogue.json` is a **product notification list** from the FDA PH Verification Portal (name, brand, type, NN- number, company, validUntil), gathered 13 Oct 2026. It holds no ingredient risk data. | Describe it as "FDA Philippines notified-cosmetics catalogue used to check product registration". |
| 5 | Archive entries "are purged automatically" after 30 days (p.8, BIN-08) | Cron exists (`vercel.json`: `/api/archive-purge` at `0 3 * * *`), but `archive-purge.js` runs **dry-run** unless env `ARCHIVE_PURGE_LIVE=1`. | Confirm the env var is set on Vercel, or say "dry-run by default". |
| 6 | Admin Terms must be accepted before any module; acceptance recorded (ADM-09 to ADM-11) | Admin site shows a Terms **dialog link** on login ("By logging in, you agree..."). No forced acceptance step or stored acceptance found. (The mobile app does record acceptance.) | Reword tests to implied consent, or build an acceptance gate. |
| 7 | Lockout after repeated failed admin logins (ADM-08) | No own lockout. Relies on Firebase Auth `auth/too-many-requests` throttling (`_Index/indexApp.js:142`). | Say so in Expected Result. Panel will accept "provider-side throttling" if stated honestly. |
| 8 | AR Try-On "verifies product compatibility with the user's skin profile" (p.10, p.48, AR-07) | No compatibility check found in `Scripts/AR/`. Verdict lives on the Result screen. | Remove the claim from AR, or add a warning chip in AR. |
| 9 | Velasco et al.: "our system uses similar image recognition to analyze a user's skin" (p.22); Gallardo/Carreon: "GlowUp builds on these ideas by using similar AI technology" for skin tone (p.15) | The app does **not** analyse skin or detect skin tone from the camera. Skin type and tone are **self-selected** in onboarding. MobileNet is used for pack look-up only. Skin care is out of scope. | Rewrite both "relation to GlowUp" sentences. |
| 10 | "Firebase Hosting" for deployment (p.25, p.51) | `firebase.json` has only Firestore rules + emulator. Site is on **Vercel**. | Remove Firebase Hosting. |
| 11 | Results and Discussion (p.59) | Still template text. No results. Test-case "Actual Result / Status" columns all empty. Appendix D empty. No weighted mean of the ISO 25010 survey. | Fill before defense: run tests, compute means and verbal interpretation, add PERF-01 timings. |

### 1.2 Medium: inconsistent or unsupported

1. **Module name**: Scope says "Ingredient Master List"; figures and site say "Ingredient Curator". Pick one.
2. **"Restricting its database exclusively to FDA-notified cosmetics"** (p.19): the app also creates Pending `scan_` products and uses Open Beauty Facts. Say "verified against FDA PH; unverified entries stay Pending".
3. **Recent Scans** (p.45): "directory of verified brands managed by the administrator" not found in `Scripts/Recent/`. Remove or show it.
4. **App version**: Figure 19 shows "GlowUp version 0.1.0"; current build is 0.71.0. Retake screenshot.
5. **Abstract**: "Date of Completion" and "Keywords" blank. "Modernizing the way people shop worldwide" clashes with a San Jose del Monte / Philippine scope.
6. **Use-case diagrams** list Groq and Mistral. True only for admin Assist (`api/assist-ingredient.js:782-784`). The **app** uses Gemini only (`api/app-ai.js`). Fig. 26 should show Gemini only.
7. **Requirements Analysis** sets "minimum 12 MP camera, gyroscope, accelerometer" but Table 1 lists only the dev laptop and test phone. Add a minimum-spec table (Android 6.0 / API 23, ARMv7 + ARM64, ARCore optional).
8. **Calendar of Activities** stops at March (Capstone 1). No Capstone 2 development timeline / Gantt.
9. **ERD** omits real collections: `auditLog`, `adminLayouts`, `scanActivity` fields, support reports, `ingredientClassifications` is shown but `archive_purge_log` only in DFD. List in Figure caption says "Entity Role Diagram".
10. **DFD** has no level label (Context / Level 0) and process 1.0 talks directly to Gemini; the real path is App -> Vercel `app-ai` -> Gemini.
11. **Evaluation tool**: IT form says "three characteristics of ISO 25010" but rates eight. Scale labels "Poor Functional ... Very Functional" are used for Security and Maintainability. ISO/IEC 25010 was revised in 2023 (Usability -> Interaction capability, Portability -> Flexibility, new Safety). Only 2 IT experts and 3 end users; no mean, no interpretation table.
12. **Evaluator issues**: Feedback on p.98 sits after Cristley Alley's form; Vincent Diaca's feedback page is missing and his form is undated. Katrina Exconde is both an end user and the "ingredient expert" (a Nutritionist-Dietician, not a dermatologist). One end user is 17 years old (needs guardian consent).
13. **Expert 1 scores** Security and Maintainability mostly 2 (Moderately). Expect a panel question on what was fixed after (CSP, auditLog, AI key moved to server, rules published).
14. **Test cases** use skin-care samples ("Sample Toner A", "Sample Serum D"), out of the make-up-only scope. ONB-01 uses "Dullness", which is not an option. Module table says SCAN has 11 cases; only SCAN-01 to SCAN-10 exist.
15. **Objective 1**: "regulatory databases" flagging. The app checks FDA PH **registration**, not a banned-ingredient list. Reword or name the source of Harmful levels (admin-curated, COSMILE/CIR references).

### 1.3 Low: formatting and references

1. Table of Contents page numbers wrong (RRL listed p.21, is p.13; Methodology 21 vs 23; List of Figures offsets by 1).
2. Duplicate figure numbers: Figure 23 (ERD and Admin Log In), Figure 26 twice (Adding Brand, Adding Product), Figure 35 "Onboarding Phase 4" should be Phase 5. User Guide restarts numbering.
3. Figure 26 "Adding Product" shows the **Add Ingredient** form. Product form screenshot missing.
4. User Guide callout numbers do not match image labels (text 6-24, image 1-19). Mobile guide stops at Home / Account: no Scan, Result, AR, Recent, Settings.
5. p.4 ends with stray "Purpose and Description". p.17 repeats the Thakur paragraph twice. "Skin Profile Onboarding ... according to users" (p.9) unclear.
6. Appendix B code lost its angle brackets (`new List()`, `GetComponent()`) and has no line breaks.
7. References: Adawiyah 2024 in text, 2025 in list; An et al. 2022 in text, 2023 in list; Carreon entry fused into Butt; Thakur fused with An; Philippine Dermatological Society (2023) and Nguyen et al. (2025, p.13) cited but missing; references not alphabetical. Verify every DOI before defense, especially Santiago-Rivera (journal and URL look unverifiable) and Adawiyah.
8. **Privacy in a privacy paper**: real-looking e-mails in screenshots (Figs 13, 31, 35, 38) and home addresses + phone numbers in Appendix F. Blur or use test accounts.

### 1.4 Confirmed matches (safe to defend)

- AR Foundation 5.2.2, ARCore 5.2.2, URP 14.0.12, TMP 3.0.7, Mobile Notifications 2.4.3, ML Kit text 16.0.1 / barcode 17.2.0, MediaPipe tasks-vision 0.10.11.
- Tesseract + Leptonica native fallback exists (`OcrService.cs`, `libtesseract.so`, `liblept.so`).
- Argon2id offline hasher (`Auth/Argon2idHasher.cs`).
- Shaders `FaceMakeupBlend.shader`, `FaceMakeupGloss.shader`.
- Admin audit trail: `auditLog`, create-only, no update/delete (`firestore.rules:290-300`).
- Daily skin-log reminder, health-data consent separate from Terms, medical disclaimer on Dashboard and Result.
- Vercel cron for archive purge; security headers + CSP in `vercel.json`.
- Min SDK 23, ARMv7 + ARM64.

---

## Part 2. Panel question bank (IT specialist roles)

Each question has a short **answer cue** from the real system. Answer from the code, not from the paper, where they differ.

### 2.1 Information Security

1. **Where is the skin profile stored, and is it encrypted?**
   Cue: Firestore `users/{uid}` (owner-only rules, encrypted at rest by Google) + local JSON cache in app-private storage, backups off. Be honest that local cache is not app-encrypted (see 1.1 #1).
2. **How are AI keys protected? Can someone extract them from the APK?**
   Cue: Keys moved server-side to Vercel env vars (`api/app-ai.js`, `GLOWUP_APP_AI`, `GEMINI_*`). Old key in the app must be revoked.
3. **How does the server know a request comes from a real admin?**
   Cue: Firebase ID token verified, then `admins/{uid}` checked; Firestore rules enforce `isAdmin()` for writes, not only the UI (SEC-03).
4. **What stops a user from editing another user's scans or marking their own product "reviewed"?**
   Cue: Rules: users only touch `users/{uid}`; app may only create Pending/unreviewed entries and `scans + 1`.
5. **Why Argon2id, and what does it protect if Firebase Auth already handles passwords?**
   Cue: Offline sign-in check on the device only; Firebase stores the real credential. Memory-hard against brute force.
6. **How do you prevent XSS on the admin site from catalogue data?**
   Cue: CSP in `vercel.json`, `X-Frame-Options DENY`, `frame-ancestors 'none'`, text rendered as text (SEC-02).
7. **Is the activity log tamper-evident?**
   Cue: `auditLog` create-only, uid/email must match token, timestamp within +/-120 s, no update/delete.
8. **What happens after 10 wrong admin passwords?**
   Cue: Firebase Auth throttling (`too-many-requests`); no custom lockout. No 2FA because of free-tier limit.
9. **How does GlowUp comply with RA 10173 (Data Privacy Act) for sensitive personal information?**
   Cue: Separate, unticked health-data consent; purpose limitation; delete account removes profile and scans; no profile sent to AI.
10. **Does any part of the app send health data to a third party?**
    Cue: Live pipeline does not. Admit the legacy `ProductScanner.cs` still contains code that does, and that it is being removed.

### 2.2 AI Specialist

1. **What exactly is "AI" in GlowUp? Which parts are rules and which are models?**
   Cue: ML Kit OCR + barcode (on-device), MobileNet pack-look matcher, MediaPipe hand/face landmarks, Gemini for unmatched products and admin drafts. Safety verdict is rule-based on admin-curated ingredients.
2. **How do you stop the LLM from inventing ingredients (hallucination)?**
   Cue: Lists must come from cited pages; trust order of sources; AI drafts stay Pending until an admin approves; "Matched 2 sources".
3. **How accurate is product recognition? Show the number.**
   Cue: Report the alpha test (20 products) and ScannerEval results; MobileNet thresholds 0.70 / 0.55. Without numbers this is the weakest answer.
4. **What is Levenshtein fuzzy matching doing, and what threshold prevents false matches?**
   Cue: OCR names vs catalogue names/aliases; explain the cut-off and the shortlist when confidence is low.
5. **Your RRL cites CNN skin analysis (92-98%). Does GlowUp analyse skin from the camera?**
   Cue: No. Skin type and tone are self-reported. Fix the RRL wording (1.1 #9).
6. **Why Gemini, and what is the fallback when the quota runs out?**
   Cue: Model list fallback in `app-ai.js`; Groq/Mistral only for admin Assist; app falls back to shortlist / typed entry.
7. **How does the recommender work? Is it machine learning?**
   Cue: Content filtering on the phone: exclude products with any ingredient flagged for the profile. Not collaborative filtering.
8. **How do you handle an ingredient that is not in the master list?**
   Cue: Marked unrecognised; overall rating never says "safe" as if fully checked (RES-06, LIM-02).
9. **How does AR keep lipstick on the lips when the head moves? What is the One-Euro filter?**
   Cue: ARCore 468-point mesh or MediaPipe Face Landmarker; One-Euro smoothing trades jitter vs lag.
10. **What bias risk exists for Filipino skin tones in the AR shading?**
    Cue: Pigment caps per type, skin-tone-aware foundation tuning tested on the owner's phone; admit limited device and tone sample.

### 2.3 Software / Web Application Specialist

1. **Draw the architecture: app, admin site, Vercel functions, Firestore. Where does each call go?**
2. **Why plain HTML/ES modules instead of React/Angular?**
   Cue: No build step, static hosting on Vercel, shared modules in `_Shared/`.
3. **How do the app and the website stay consistent on rules (e.g., source kinds)?**
   Cue: One shared rule set tested by `SharedRuleCases.txt` on both sides.
4. **How does Recycle Bin -> Archive -> purge work, and what if the purge fails half-way?**
   Cue: Soft delete, archive with `expires_at`, daily cron, dry-run unless `ARCHIVE_PURGE_LIVE=1`, purge log with errors.
5. **How does the import handle a 500-row Excel with duplicates?**
   Cue: Parsed client-side, duplicates/blank rows skipped and reported, everything saved as Pending (BRD-08).
6. **What testing did you do? Where are the results?**
   Cue: Node tests in `tests/*.test.mjs`, rules emulator tests, Unity EditMode tests, headless-Chrome scenario rig. Must fill Results chapter.
7. **The paper says Unity 6, the project is 2022.3. Which is it and why?**
   Cue: 2022.3.62f3 LTS for AR Foundation 5.2.2 / ARCore stability.
8. **How does the admin site behave on a phone or tablet?**
   Cue: Container-query board, breakpoints 1024/768, phone menu (COMP-02).
9. **How are concurrent edits by two admins handled?**
   Cue: Explain last-write-wins vs transactions in `adminStore.js`; audit log attributes each save.
10. **What is your deployment process and rollback plan?**
    Cue: `npx vercel` deploy, Git commits as checkpoints, Vercel instant rollback, rules published separately.

### 2.4 Systems Administrator

1. **What are the minimum and recommended device specs?**
   Cue: Android 6.0 (API 23), ARMv7/ARM64, camera; ARCore optional (guided preview fallback). Add a table to the paper.
2. **How do you onboard a new admin?**
   Cue: Create Firebase Auth user, add `admins/{uid}` by hand in the console.
3. **Where are secrets stored and who can rotate them?**
   Cue: Vercel env vars (Gemini, Groq, Mistral, purge settings); Firebase browser key referrer-restricted.
4. **What are the free-tier limits and what breaks first at scale?**
   Cue: Firestore reads/writes per day, Gemini quota, Vercel Hobby function limits and 1 daily cron. No Cloud Functions (billing).
5. **How is the database backed up?**
   Cue: Delete Permanently saves JSON to the admin's computer; no scheduled Firestore export on free tier. Admit the gap.
6. **How do you monitor errors in production?**
   Cue: Vercel function logs, Android logcat, support reports from the app. No crash analytics yet.
7. **How are Firestore rules deployed and tested?**
   Cue: `firebase.json` + emulator on 8085, rules tests, then publish.
8. **What happens if Vercel or Gemini is down?**
   Cue: App scans catalogued products from local cache/index; AI path shows a clear message; admin site cannot load.
9. **How do you distribute the APK and handle updates?**
   Cue: Debug-signed APK sideloaded (GlowUp.71.apk). Explain the plan for a release key / Play Store.
10. **Why is the domain glowupapp.xyz and how is HTTPS handled?**
    Cue: Spaceship registrar, Vercel-managed TLS.

### 2.5 IT Support Specialist

1. **A user says the camera screen is black. What do you check?**
   Cue: Camera permission, black-frame watchdog switches to guided preview, ARCore services, low light.
2. **A user's scan result is wrong. How do they report it and what do you do?**
   Cue: Settings > Contact support / "Wrong scan result" report -> admin dashboard panel -> fix product or ingredient.
3. **A user forgot their password. Walk through the reset.**
4. **The confirmation e-mail never arrived. What now?**
   Cue: Resend after 60 s, check spam, "Use another email", domain/MX check.
5. **AR Try-On is slow on an old phone. What setting helps?**
   Cue: Settings > AR Try-On quality, frame-rate cap, graphics quality (AR-08).
6. **The app works offline? What can a user still do?**
   Cue: Offline sign-in (Argon2id), cached profile and history, catalogued scans; no AI look-up.
7. **A user wants to delete everything about them. How?**
   Cue: Settings > Delete Account removes account, skin profile and scan history.
8. **A user is under 18. Can they use the app?**
   Cue: Age from birthday; minimum-age ingredients; state the policy for minors.
9. **An admin removed a brand by mistake. How do you recover it?**
   Cue: Recycle Bin > Restore; logged in audit trail.
10. **What does the user guide cover, and where is the guide for scanning and AR?**
    Cue: Currently missing in Appendix E. Add it before defense.

---

## Part 3. Fix list before defense (priority order)

1. Fill Results and Discussion, test-case Actual Result/Status, Appendix D, survey means.
2. Fix encryption claim (1.1 #1) and Appendix B source code (1.1 #2).
3. Correct Unity version, fdaCatalogue description, Firebase Hosting, AR compatibility, RRL skin-analysis sentences.
4. Confirm `ARCHIVE_PURGE_LIVE` on Vercel; reword ADM-08 to ADM-11.
5. Renumber figures, fix TOC, complete the mobile user guide, fix references.
6. Blur personal data in screenshots and Appendix F.
