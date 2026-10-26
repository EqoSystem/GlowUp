# Decisions made without asking (with the reason)

Each line can be reversed. Newest at the bottom.

1. **Branch from `scan-miss-flow`, not `main`.** `scan-miss-flow` holds the newest app and website work (the brief's code); `main` is older. Branch: `feature/unity-ar-face-mesh-tryon`. Nothing merged, nothing pushed.
2. **Committed the uncommitted files as a checkpoint** (16 scenes saved by the Editor, and two `.gitignore` lines `.vercel`, `.env*`) as the first commit of the branch. The project's `CLAUDE.md` asks for a checkpoint before edits, and leaving them unstaged would have mixed them into the AR commits. `scan-miss-flow` itself is unchanged.
3. **`docs/project-documentation/` left untracked and untouched.** It is an earlier generated document; its build tools were copied into `docs/ai-data-report/` instead of moving anything.
4. **Excluded folders.** `CLAUDE.md` says not to read folders listed in `.graphifyignore`. For Step U0 the brief requires the Unity version and the AR package versions, so `ProjectVersion.txt`, the package names in `Packages/manifest.json`, three setting lines of `ProjectSettings.asset` (colour space, min SDK, architectures, backend) and two dependency lines of `Plugins/Android/mainTemplate.gradle` were looked at once. No other file in those folders was read. New test files were written into `Editor/Tests/` (excluded from the knowledge graph) without reading the existing tests.
5. **No "Manual mode" invented.** The website has no feature of that name; the report describes the nearest behaviour (writing an entry by hand) and proposes a verification status instead of adding one.
6. **Rules: proposal only.** `docs/ai-data-report/proposed.firestore.rules` was NOT published and NOT copied over `GlowUp-Admin_AI_Testing/firestore.rules`. Its syntax was not checked on the emulator (not available offline here); it must be tested before use.
7. **Measurements from outside the repository** (FDA portal 12-15 s, Open Beauty Facts 0.6-5 s, logo check) are tagged Inferred, not Verified, because the files that recorded them are not in the repository.
8. **No MediaPipe face fallback.** Too heavy to add safely without a phone (new Java bridge, frame hand-off, second renderer). Phones without ARCore keep the guided preview; the limit is documented.
9. **Keep the existing tint blend shader.** It already keeps the lips' own light and texture in linear light (the brief's "multiply-style" option). Replacing it would be a bigger change than the brief's "smallest change" rule allows.
10. **Gloss and smoothing are Full-tier only; brows on both tiers.** Light tier keeps the old fixed highlight and no smoothing to save work on weak phones; the brow mask is built once per face and costs one extra draw only while brows are tried.
11. **Brows tab only in live tracking.** The guided 2D preview has no brow shapes; rather than drawing new shapes on the face outline (scene layout risk), the Brows tab is unavailable there.
12. **Shade codes: optional app read only.** The app reads an optional `shadeCodes` list (same order as `shades`) when a product has one. No website form, no import key and no data are added: that is a schema change for the developers to approve.
13. **Debug overlay off in release builds.** The existing PlayerPrefs switch also worked in release builds; now the overlay works only in the Editor and development builds.
14. **Upgrade back to Full at most once per visit** of AR Try-On, and only after an automatic downgrade, to avoid flip-flopping and heat.
