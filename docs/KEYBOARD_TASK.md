# Keyboard avoidance task

Rules: no scene edits by hand, no builder run by Claude, no commit. Each step waits for approval.

- [x] Step 0: audit and design (approved with changes)
- [ ] Step 1: runtime (written, waiting for approval and the Editor/device checks)
  - [x] KeyboardAvoider.cs rewritten (events, 10 Hz only while focused, bottom padding, slide only without ScrollRect)
  - [x] KeyboardInsets.cs patched (absolute share, dev-only number log, no text)
  - [x] KeyboardKeepVisible.cs patched (list stays inside the field's container)
  - [ ] Unity compiles with no CS errors
  - [ ] Editor check (Pretend A Keyboard In Editor)
  - [ ] Device check (Android, iOS if available)
  - [x] Android only: IME window insets (API 30+), window frame fallback, TouchScreenKeyboard.area last fallback; every Java call wrapped, objects cached; dev-only on-screen label (Log Numbers)
  - iOS untested and out of scope (the TouchScreenKeyboard.area code stays as the Android fallback)
- [ ] Step 2: KeyboardKit.cs, idempotent Ensure() in ThemeKit.FinishScene, ScrollRect wrap for Login and Onboarding Step 1 (one builder at a time), badge grouping
- [ ] Step 3: list of forms whose badges need moving (list only, no edits)
