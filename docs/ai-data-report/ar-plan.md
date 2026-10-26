# AR Try-On face-mesh upgrade: audit and technical plan

Branch `feature/unity-ar-face-mesh-tryon`, 26 October 2026. Checkpoint 1 of the brief (steps U0, 1, 2, 3).

## U0. Stack detection

- **Unity project, already.** `GlowUp/ProjectSettings/ProjectVersion.txt`: Unity **2022.3.62f3**. Unity Hub has 2022.3.62f3 and 6000.4.0f1 installed; the project's version is used.
- Packages (names in `Packages/manifest.json`): `com.unity.xr.arfoundation` 5.2.2, `com.unity.xr.arcore` 5.2.2, URP 14.0.12, Test Framework 1.1.33.
- No Flutter, React Native or separate native Android app. So the work goes **into the existing Unity app**; no `unity-ar-tryon/` side project is needed.
- The Unity Editor was closed at the start, so batch-mode builds and tests can run on this PC.

## 1. Audit of the current AR implementation

| Topic | Finding (file) |
|---|---|
| SDK | AR Foundation + ARCore XR Plugin, face tracking subsystem (`ARFaceManager`, 1 face). `Scripts/AR/ArFaceTrackingController.cs` |
| Camera frame | ARCore front camera, `ARCameraBackground` into a RenderTexture (max 1280 px high Full, 540 px Light), shown in a RawImage. Same file, `PreparePicture` |
| Landmarks / mesh | ARCore's 468-vertex face mesh through `ARFaceMeshVisualizer`; vertex numbers follow the MediaPipe canonical mesh (used by `ArMouthMask`) |
| Regions | 4 mask PNGs in mesh UV space (face, cheeks, eyelids, lips) + 1 "shine" layer. **No brows.** `Scripts/AR/ArFaceMakeup.cs`, `Editor/ArFaceTrackingSetup.cs` |
| Blend | `Shaders/FaceMakeupBlend.shader`: picture x (shade / assumed skin), `Blend DstColor SrcColor`, smoothstep feather. Linear colour space, so the blend is in linear light; luminance and texture of the lips are kept. Limit: at most x2 brighter |
| Finish | matte / satin / glossy / shimmer / metallic: opacity, lightening, a **fixed** highlight picture and a twinkle. The highlight does not move when the head turns. `Scripts/AR/FallbackMakeupOverlay.cs` |
| Lighting | `ArLightAdapter`: 32x32 sample every 0.4 s; skin hue adapts; highlight gain 0.35 to 1.15; low-light hint. Synchronous `ReadPixels` (small GPU stall every 0.4 s) |
| Mouth | `ArMouthMask`: inner-lip polygon in UV space, mouth-open ratio, colour cut out of teeth |
| Lost face | `ArFaceHold`: hold 0.5 s, predict 0.4 s, fade 0.6 s, ghost mesh |
| Jitter | **No smoothing** of the mesh |
| Tiers | `ArQualitySettings` + `ArAdaptiveQuality`: Full / Light from RAM (5500 / 3800 MB), one frame-rate check (<20 fps after 2 s) moves to Light and remembers it; **never moves back up** |
| Debug | `ArDebugOverlay`: fps, ms, worst, tier, light. Also switchable in release builds (PlayerPrefs). `.meta` file missing |
| Privacy | No picture encoding, file writing or upload in `Scripts/AR/` |
| Platforms | Android only, min API 23, ARMv7 + ARM64, GLES3 only, IL2CPP (`Editor/ArFaceTrackingSetup.cs`, `Editor/GlowUpProjectSetup.cs`) |
| Bug found | `ShadeAdvisor.AreaFor`: Brow, Contour, Primer, Setting Spray fall through to **Lips** |
| Measured numbers | **None.** No phone is connected; ARCore face tracking does not run in the Editor or on a PC |

## 2. Device tiers

Kept: two pipelines, **Full** and **Light** (the project already has them and a Settings choice: Automatic, High, Light).

Signals (measurable, no marketing names): `SystemInfo.systemMemorySize`, `graphicsMemorySize`, `processorCount`, plus **measured frame time** while AR runs.

| | Full (mid/high) | Light (low) |
|---|---|---|
| Picture height | 1280 px | 540 px |
| Layers drawn | all (face, cheeks, eyes, lips, brows) + highlight | only the area being tried + highlight |
| Highlight | **new:** gloss follows the mesh normals (moves with the head), sparkle for shimmer | fixed highlight picture (as before) |
| Light adaptation | on | off |
| Mouth mask | on | off |
| Brow mask | on | on (built once per face, cheap) |
| Mesh smoothing | **new:** One Euro filter on the vertices | off |
| Frame cap | app's own | 30 fps |
| Budget | 30 fps target, p95 frame time < 50 ms | 30 fps cap, p95 < 66 ms |

Runtime changes (Automatic only):
- **Down:** a window (1.5 s) with average below 20 fps **or** p95 frame time above 80 ms moves Full to Light (as before, plus p95).
- **Up (new):** after an automatic downgrade, if Light runs at its cap with p95 below 45 ms for 20 s, **one** trial of Full per visit; if the trial window fails, back to Light for the rest of the visit; if it passes 3 windows, the "found slow" mark is cleared.
- Never overrides a user's own High/Light choice.

## 3. Technical plan

**Face tracking:** keep ARCore face mesh (already in the app, 468 vertices). No new dependency. MediaPipe Face Landmarker as a fallback was evaluated: the app has MediaPipe tasks-vision 0.10.11 for hands and pack looks, but a face path would need a new Java bridge, camera frames in Java, and a second renderer for a 2D mesh. That is a heavy, untestable-here change; **not adopted**. Phones without ARCore keep the guided preview (documented limit).

**Rendering (smallest change that meets the brief):**
1. Brows region: mask built at run time from the mesh's own UVs and the canonical brow vertices (same technique as the mouth mask), drawn with the existing tint shader. New area "Brows" (tab shown only in live tracking). Fix `AreaFor` (brow to Brows, contour to Cheeks, primer and setting spray to Face).
2. Gloss that follows the surface: new shader `GlowUp/FaceMakeupGloss` (Blinn-Phong on the mesh normals with a soft virtual front light, masked by the existing highlight pictures, sparkle cells for shimmer, kept off an open mouth). Full tier only; Light keeps the fixed highlight.
3. Keep the tint blend (already luminance-keeping, in linear light). Document its x2 limit.
4. Mesh smoothing: One Euro filter on vertex positions in face space (pure, unit-tested class), Full tier, Inspector switch.

**Shade data:** optional product field `shadeCodes[]` (same order as `shades[]`), read by the app only if present; a scanned code ("12A", "12 a", "I2A") is matched to its hex with camera-confusion folding (pure, unit-tested). Nothing is written to the database; the website form change and the rule change are proposals.

**Debug and testing:** overlay adds tracking state, p95, tier changes, smoothing, active shade code to hex; only in development builds (or the Editor); three fingers held 1 s toggles it; a built-in list of 8 test shades (different undertones and finishes) can be cycled from the overlay, offline.

**Build:** `GlowUp > Build Test APK (development)` and `AndroidApkBuilder.BuildAndroidDebug` for batch mode, writing `Builds/Android/GlowUp-AR-debug.apk` (ignored by Git), signed with Unity's debug keystore.

**Memory plan:** one 256x256 brow mask per face (made once), 468-vertex filter state (about 11 KB), no per-frame allocation in the new code (reused arrays; `List<Vector3>` reused for `Mesh.SetVertices`).

**Battery / thermal:** Light caps at 30 fps; the upgrade trial runs at most once per visit; camera and tracking stop when the screen is left or paused (existing `StopTracking`, `OnApplicationPause`).

**Test devices needed:** one ARCore phone with 3-4 GB RAM (Light), one with 6 GB+ (Full), one phone without ARCore (guided preview). Several skin tones, bright / normal / dim light.

**Risks:** brow vertex numbers assume ARCore's mesh matches MediaPipe's canonical numbering (the mouth mask already assumes it, unproven on a phone); normals may be missing on some phones (the shader falls back to a view-facing normal); smoothing can add lag if set too strong (adaptive filter, switch in the Inspector).

**Rollback:** every new behaviour has an Inspector switch on its own object (AR Mesh Smoother, AR Brow Mask, gloss material empty = old highlight, tier upgrade trial off). Whole branch: `git checkout scan-miss-flow` (nothing merged). Single change: `git revert <commit>`.
