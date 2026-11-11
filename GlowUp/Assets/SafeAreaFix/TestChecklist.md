# SafeAreaFix: test checklist and rollback (12 November 2026)

## Build
Use `GlowUp.35-dev.apk` (a DEVELOPMENT build: File > Build Settings > Development Build, or menu GlowUp > Build Test APK (development)).
The overlay only exists in development builds (or with the scripting define SAFEAREA_DEBUG). The release `GlowUp.35.apk` has the safe area but no overlay.
Install it, open the app. A small round **SA** button sits at the bottom left of every screen: it opens the panel.

## Before the first screenshot (once per phone)
1. Tap SA. Drag the blue line to the BOTTOM edge of the camera hole as you see it on the phone, tap **Mark hole bottom**. The phone remembers it.
2. Make sure **Top: ON** and the other three are off (tap **Reset to Top only** if not).

## Screens to screenshot (phone's own screenshot button, panel open, portrait; then again in landscape if the phone rotates)
Login, Onboarding step 1, Terms, Dashboard, Recent Scans, AI Smart Scan, AI Scan Result, AR Try-On, Settings, Loading (any one is enough to see the rule).

PASS on every screen:
- the header text / back button is fully BELOW the red box (the camera hole) and below the marked blue line;
- the pink / paper background reaches the very top edge (no strip of another colour above the header);
- no strip or gap at the bottom (Bottom is off, so the card keeps its normal 40-unit margin);
- the panel's PASS/FAIL line for that screen says PASS (content top is at or below the marked hole bottom).
AI Smart Scan and AR Try-On: the camera picture and the face light still fill their frame; only the buttons moved down.

## Edge combinations (one APK)
Tap Top / Bottom / Left / Right to turn an edge ON or off. Bottom ON: the bottom bar rises above the gesture bar. Left / Right ON: matters in landscape only.

## If the red box is missing
The panel then shows a yellow warning "Android reports NO cutout. Fallback in use: ..." and names the source (Android DisplayCutout, status_bar_height or manual). Send that screenshot.

## Rollback
- Quick: in the overlay tap **Reset to Top only**; or edit `Assets/SafeAreaFix/Resources/SafeAreaConfig.asset` (applyBottom / applyLeft / applyRight = false, applyTop = false to switch the whole system off).
- Full: copy the `.bak` files from `Assets/SafeAreaFix/Backup/` back over the scenes in `Assets/_Project/Scenes/` (remove the `.<date>.bak` ending), and delete `Assets/SafeAreaFix`.
