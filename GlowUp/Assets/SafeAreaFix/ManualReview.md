# SafeAreaFix: manual review

Objects the rules were not sure about. They were LEFT WHERE THEY WERE; nothing was guessed.

- none: every canvas child matched a rule.

Always worth a look on the phone (by design, not moved):
- Pop-ups (OverlayPanel) stay on the canvas: their dim layer must cover the whole screen and their box is centred. A very tall pop-up box could reach the camera hole on a short phone.
- The top toast (FormalToast, its own ToastLayer canvas) places itself from Screen.safeArea already.
- The screen-change sheet (UiServices > TransitionCanvas) is a full-screen loading sheet: left full-bleed.
