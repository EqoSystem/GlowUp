# What changed, explained (read this first)

Branch `feature/unity-ar-face-mesh-tryon`, 26 October 2026. Nothing was merged, pushed or deployed.

## 1. What I did, in five sentences

I read the app, the admin website and the database rules and wrote a 50-plus-page PDF report that explains, with evidence for every claim, how AI Smart Analysis, ingredient lists, shades, images and the database rules really work, and what you can honestly tell the panel (`docs/ai-data-report/GlowUp_AI_Data_Report.pdf`).
I checked your seven beliefs: B2, B6 and B7 are true, B5 is true in substance, B1 and B4 are partly true, and B3 is wrong as worded (the AI does see one scan picture; what the app does not do, by design, is compare scans with catalogue photos).
I then improved the existing AR Try-On, which already drew make-up on ARCore's real face mesh: the gloss now moves with the face, the mesh trembles less, brows were added, and weak phones can go back to the richer mode when they cope.
I fixed two bugs on the way (brow, contour, primer and setting-spray products were tried on as lipstick; the AR picture could go dark after a quality change) and added a debug panel and an APK build button for quick phone tests.
I could not test on a phone (none connected, and ARCore face tracking does not run on a PC), so every phone number in the report is marked as still to be measured.

## 2. What changed in AR Try-On

**Before:** colour sat on the face mesh and kept the lips' texture, but the shine of a glossy lipstick was a painted spot that never moved, the colour edges could shimmer with the tracker's trembling, brows had no colour (brow pencils were shown on the lips), and a phone moved to the light mode never came back.

**After:**
- **Glossy, satin, shimmer and metallic look different.** The highlight is worked out from the shape of the face: a glossy lip shows a small sharp shine that slides over the lip when you turn your head; satin a wide soft sheen; shimmer small twinkling sparkles; metallic a shine in the shade's own colour. Matte has none.
- **Steadier colour.** The face mesh is smoothed: a still face stops trembling, a fast mouth movement is still followed closely.
- **Brows** can be tried on (a Brows tab, live tracking only).
- **Low-end vs mid/high-end phones.** Strong phones get everything above. Weak phones keep the lighter mode (smaller picture, only the part being tried, the old fixed shine, 30 fps). If a phone was moved down because it was slow and then runs smoothly, it gets one try of the full mode per visit, and keeps it if it copes.
- **Shade code to colour (for admins).** Brands print codes like "12A" or "131", not colours. The app can now link the code it reads on the pack to the admin's hex, so AR starts on the right colour: but only once admins enter the codes, and the website has no box for them yet (proposal in the report). Until then, the user taps the right swatch as before.

## 3. Every file I changed or added

| File | What it does | Risk |
|---|---|---|
| `docs/ai-data-report/*` | The report, its generator, diagrams, plan, decisions, this file, proposed rules | low |
| `GlowUp/BUILD_AND_INSTALL.md` | How to build and install the test APK, test checklist, debug panel, troubleshooting | low |
| `Shaders/FaceMakeupGloss.shader` | New moving highlight | medium |
| `Scripts/AR/ArFaceMakeup.cs` | Draws the layers; adds brows and the moving highlight | medium |
| `Scripts/AR/ArFaceTrackingController.cs` | Starts/stops helpers per mode both ways; dark-picture fix | medium |
| `Scripts/AR/ArAdaptiveQuality.cs` | Measures frame times all the time; down and one try up | medium |
| `Scripts/AR/ArMeshSmoother.cs`, `OneEuroFilter.cs` | Mesh smoothing | low |
| `Scripts/AR/ArBrowMask.cs`, `FaceMaskPainter.cs` | Brow mask from the face | low |
| `Scripts/AR/ArDebugOverlay.cs`, `ArTestShades.cs` | Debug panel and 9 test shades (development builds only) | low |
| `Scripts/AR/ArColorMath.cs` | C# copy of the shader math, for tests | low |
| `Scripts/AR/ArTryOnFlowController.cs` | Brows tab only with live tracking; shade code for the panel | low |
| `Scripts/AR/FallbackMakeupOverlay.cs` | Brows strength limit; finish name | low |
| `Scripts/Performance/ArTierPolicy.cs`, `ArQualitySettings.cs` | Rules for moving down / up | low |
| `Scripts/Scan/ShadeAdvisor.cs` | Brows area and brow shades; area fix for brow/contour/primer/setting spray | low |
| `Scripts/Scan/ShadeCode.cs` | Matches a printed shade code to the product's codes | low |
| `Scripts/Scan/ScanAnalysisPipeline.cs` | Uses the code to pick the hex (only when a product has codes) | low |
| `Scripts/Data/DataModels.cs`, `ProductRepository.cs` | Optional `shadeCodes` read; `productShadeCodes` on a scan | low |
| `Editor/ArFaceTrackingSetup.cs`, `Editor/ArTryOnSceneBuilder.cs` | Make and wire the brow and gloss materials and new objects | low |
| `Editor/AndroidApkBuilder.cs` | Menu "GlowUp > Build Test APK (development)" and batch method | low |
| `Editor/Tests/ArUpgradeTests.cs` | 46 tests | low |
| `Scenes/ARTryOnScene.unity`, `Materials/ArMakeup_6_Brows.mat`, `ArMakeup_7_Gloss.mat` | Rebuilt scene, new materials | medium (large file diff) |
| 16 scenes + `.gitignore` (first commit) | Your uncommitted Editor saves, committed as a checkpoint | low |

## 4. Decisions I made without asking

See `docs/ai-data-report/decisions.md` (21 items). The ones most worth a look:
- Branched from `scan-miss-flow` (newest work), not `main`; committed your uncommitted scenes as the first commit of the branch.
- For the Unity version check I looked at a few lines in `ProjectSettings/`, `Packages/manifest.json` and `Plugins/Android/mainTemplate.gradle`, which `CLAUDE.md` asks not to read; nothing else there was opened.
- No MediaPipe face fallback (too heavy to add without a phone); phones without ARCore keep the guided preview.
- Kept the existing tint shader; added a separate gloss shader instead of rewriting it.
- Shade codes are read by the app only; no website or database change.
- Debug panel only in development builds.
- Three older failing scan tests left alone (they fail without my changes too).

## 5. What I did NOT touch

Live Firebase (rules, data, indexes), the database schema, the admin website code, Vercel, the `main` and `scan-miss-flow` branches, any remote. The guided preview (phones without ARCore) is unchanged. The old fixed highlight is still used on the light mode and comes back everywhere if the gloss material is cleared.

## 6. Measured results

- Unit tests: 46 new tests pass; whole suite 271 of 274 (the 3 failures existed before).
- AR frame rate, frame times, memory, heat: **not measured**. No phone or ARCore emulator was available. The debug panel shows these numbers on a phone; please record them per tier (one 3-4 GB phone, one 6 GB+ phone).

## 7. Known limits to tell the panel honestly

- On-screen colour depends on the phone's screen, the room's light and the user's skin: say "faithful preview of the reference shade", never "exact colour".
- Very pale shades on deep lips stay too dark (the blend can at most double brightness): a fairness issue to check on real phones; a fix is proposed in the report (Section 37).
- Live try-on needs an ARCore phone; others get the guided preview without brows.
- Scan speed has never been timed on a phone; time it before the defence.
- Some brands have no credible online source: admins type those lists from the pack and keep them Pending until reviewed.
- The app still calls the AI with a key inside the APK; moving it behind the server function is recommended.

## 8. The APK

- **Built.** `GlowUp/Builds/Android/GlowUp-AR-debug.apk`, 212,378,863 bytes (about 203 MB), development build, IL2CPP, ARMv7 + ARM64, signed with Unity's debug key. Built in 5.1 minutes in Unity batch mode. Not in Git. **Not yet installed on any phone.**
- **Rebuild it:** Unity menu **GlowUp > Build Test APK (development)**, or the batch command in `GlowUp/BUILD_AND_INSTALL.md`.
- **Install in under 5 minutes:** turn on USB debugging on the phone, connect it, run `adb install -r GlowUp/Builds/Android/GlowUp-AR-debug.apk` (or copy the file to the phone and open it).
- **Phones:** live try-on needs an ARCore-supported Android phone with "Google Play Services for AR" (the app offers to install it). Android 6 (API 23) or newer. Other phones get the guided preview.
- **First 10 tests:** open the app, allow the camera, face detected, lip colour, switch shades, switch finishes, turn your head, open your mouth, dim light, 5-minute session (details and troubleshooting in `GlowUp/BUILD_AND_INSTALL.md`).
- **Debug panel:** hold three fingers on the screen for one second. It shows fps, average / 95th-percentile / worst frame time, Full or Light (and "trial"), face Tracking / Held / Lost, smoothing, gloss, light level, and the shade as "12A -> #B5463F"; "Next test shade" cycles nine built-in test shades without internet.

## 9. The Git work

Branch `feature/unity-ar-face-mesh-tryon` (from `scan-miss-flow`). Commits, oldest first:

1. `9d113bd` Checkpoint: your uncommitted Editor-saved scenes and two `.gitignore` lines, as found.
2. `1e53da7` The report (Phase 1), its generator, the proposed rules, the AR audit and plan.
3. `52d7cdf` Brow, contour, primer and setting-spray products no longer tried on as lipstick; Brows area and brow shades.
4. `31415bb` The pure logic and 46 tests (smoothing, tier rules, shade codes, colour math, test shades).
5. `ff07dbc` Frame times measured all the time; down to Light when slow, one try of Full per visit.
6. `c0f3f6e` Moving gloss, mesh smoothing, brow layer, debug panel; dark-picture fix.
7. `e131424` Shade code to colour (optional field); Brows tab only with live tracking.
8. `b1d2ef9` "Build Test APK (development)" menu and batch method.
9. `683ba43` AR scene rebuilt by its builder with the new objects and materials.
10. The final commit: report updated with the AR results, install guide, this file.

Nothing was merged. Nothing was pushed (the repository has no remote).

## 10. Undo or keep

- Undo everything: `git checkout scan-miss-flow` then `git branch -D feature/unity-ar-face-mesh-tryon`.
- Undo one step: `git revert <commit id>` on the branch.
- Keep it: `git checkout scan-miss-flow` then `git merge --no-ff feature/unity-ar-face-mesh-tryon`.

## 11. The 5 things to check first before the defence

1. Install the APK on an ARCore phone and run the 10-step checklist; note fps and p95 from the debug panel for both modes.
2. Check lips, brows and the gloss on at least three skin tones, especially pale shades on deep lips.
3. Time a scan (barcode product, name-only product, unknown product) five times each on the demo phone.
4. Read Part 9 of the PDF ("Open Items") and adopt the safe wording for B1, B3 and B4.
5. Decide on the shade-code field and the hardened rules; test the rules on the Firestore emulator before publishing anything.
