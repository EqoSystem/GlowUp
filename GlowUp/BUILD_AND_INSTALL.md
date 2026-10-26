# Build and install the GlowUp test APK (AR Try-On)

## Build it

**From the Unity Editor (2022.3.62f3):** open the project `GlowUp`, then menu **GlowUp > Build Test APK (development)**.
The file is `Builds/Android/GlowUp-AR-debug.apk` (inside this folder, ignored by Git).

**With the Editor closed (command line, Git Bash):**

```
"/c/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe" -batchmode -nographics \
  -projectPath 'C:\Unity\GlowUp\GlowUp' -buildTarget Android \
  -executeMethod GlowUp.EditorTools.AndroidApkBuilder.BuildAndroidDebug -logFile build.log
```

No `-quit`: it closes Unity itself. Exit code 0 = built. On failure, search `build.log` for `What went wrong` or `error`.

The build is a **development build** signed with Unity's debug key. A Play Store release needs the team's own keystore
(never commit it), and its fingerprints must be added to Firebase.

If Unity is not installed: Unity Hub > Installs > Install Editor 2022.3.62f3 with **Android Build Support** (OpenJDK, Android SDK and NDK tools), open the project, then use the menu above.

## Install it (under 5 minutes)

1. Phone: Settings > About phone > tap "Build number" 7 times; then Developer options > USB debugging on.
2. Connect the USB cable, allow the computer on the phone.
3. `adb install -r Builds/Android/GlowUp-AR-debug.apk`
   (Unity's adb: `<Editor>/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe`).
   Without a cable: copy the APK to the phone and open it (allow "install unknown apps" for the file manager).
4. Live AR Try-On needs an **ARCore-supported phone** with **Google Play Services for AR** (the app offers to install it).
   Other phones get the guided preview (face outline), without brows.

## What to test first (10 steps)

1. Open the app and sign in.
2. Open AR Try-On; allow the camera.
3. Face detected: the status line says "Live try-on ...".
4. Lips colour on a lipstick; the colour stays on the lips.
5. Switch shades quickly; switch finishes (matte, satin, glossy, shimmer, metallic): the glossy highlight should slide over the lip when you turn your head.
6. Turn your head left and right; nod.
7. Open your mouth and smile: no colour on the teeth.
8. A brow product (or the Brows tab): colour on the brows only.
9. Dim light: colour stays believable; a low-light hint may appear.
10. Leave it running 5 minutes: no stutter getting worse, phone not hot; go to the home screen and back.

## The debug overlay

Hold **three fingers** on the screen for one second (development builds only). It shows:

- `fps`, average `ms`, `worst ms`; `Full pipeline` or `Light pipeline` (and `(trial)` while Full is tried again), `p95` (95% of frames are faster than this)
- `Face: Tracking / Held / Lost`, `smoothing on/off`, `gloss moving/fixed`, `light` (picture brightness 0-1)
- `Shade: 12A -> #B5463F` (code when the product has shade codes), the part and the finish
- **Next test shade**: nine built-in test shades (warm, cool, neutral; every finish; every part), no internet needed.

Logs: `adb logcat -s Unity` and look for `[GlowUp] AR Try-On`.

## Troubleshooting

| Problem | What to check |
|---|---|
| Black camera screen | Leave and reopen AR Try-On; the app restarts a black picture by itself. Then `adb logcat -s Unity` for `AR Try-On uses the guided preview: <reason>`. |
| Camera permission denied | The card's button opens the app's settings; allow Camera there. |
| "Device not supported" / guided preview only | The phone is not on Google's ARCore list, or Google Play Services for AR is missing or outdated. |
| Face not detected in low light | Face a window or a lamp; ARCore needs light. |
| Crash on launch of AR | A crash during AR start switches live AR off on that phone until the next build (FeatureCrashGuard); install a new build to try again. 32-bit-only builds are refused on 64-bit phones. |
| Mesh visible but no colour | Choose a shade; check the Strength slider; if colour sits on the forehead, tick `Flip Masks Upside Down` on ArFaceMakeup and rebuild. |
| Brows show nothing | The brow mask is made from the first tracked frame; turn to face the camera. Brows are not available in the guided preview. |
