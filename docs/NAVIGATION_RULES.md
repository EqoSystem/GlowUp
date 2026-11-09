# GlowUp: Navigation, Exit Safeguard & Seamless UX Rules

Given by the project owner on 9 November 2026. Apply these rules whenever you write or refactor routes, screens, modals,
sheets, drawers or transitions. Before finishing any change, list which rules it touched and how each was satisfied.

Context: GlowUp is a cosmetic-product scanning app. Web = the admin website (plain static HTML/JS on Vercel,
`GlowUp-Admin_AI_Testing/`; shared dialogs in `_Shared/adminShell.js`: `askConfirm`, `askText`). Mobile = the Unity app
(`GlowUp/`, Android). Two roles: User (the app) and Admin (the website).

Still open, to be set by the owner: the OCR timeout `[N]` seconds in rule 3a.

## 0. Role & Auth Routing
- User and Admin flows are separate navigation stacks. Back/exit must never land a user on a screen belonging to the other role.
- On login, logout and onboarding completion, REPLACE the route (don't push), so Back can't return to auth screens, the
  finished quiz, or protected screens after logout.
- An unauthorized or invalid deep link redirects to a valid screen for that role, never to a blank screen or an infinite spinner.

## 1. Device & Browser Back / Gestures
- Web: preserve history. Only push history entries for real navigation states. A modal opened via URL state must close on browser Back.
- Mobile: never disable Android Back or iOS edge-swipe. You may intercept them ONLY to confirm discarding unsaved work.
  Intercepting must never block leaving.

## 2. Modals, Sheets & Overlays
- Every modal, sheet, drawer or overlay has a visible exit (X / Cancel / Back).
- Web: Escape closes it. Backdrop click closes it, except while a save is in progress.
- Mobile: bottom sheets and full-screen cards support swipe-down to dismiss.
- Destructive confirms (delete brand/product, reject snapshot): Escape, backdrop and Back always mean CANCEL, never confirm.

## 3. GlowUp-Specific Flows
### a) AI Smart Scan (camera + OCR)
- Camera permission denied: show an explainer with "Open Settings" and "Enter manually" / "Back". Never a black camera view.
- While OCR is processing: a visible Cancel, a timeout of [N] s, and on failure or "no text detected" offer Retry, Manual entry and Back.
- Back or leaving mid-scan cancels the in-flight request and releases the camera.
- The results screen always offers: Scan again, View product/ingredients, Home.

### b) Onboarding Skin Quiz
- Back goes to the previous question and keeps the answers already given.
- On question 1, Back asks before leaving onboarding instead of silently dropping progress.
- Show progress. If skipping is allowed, it is visible and confirmed.
- On completion, replace the route so Back can't re-enter the quiz.

### c) Admin Portal (Brand & Product Inventory, Pending Snapshots)
- Add/edit forms guard against losing unsaved changes on route change, browser tab close (beforeunload) and Android Back.
  The prompt is "Discard changes?" with "Keep editing" / "Discard".
- After approving or rejecting a pending snapshot, return to the list with its filters and scroll position kept. Show the
  next item, or an explicit empty state ("No pending snapshots"). Never a blank screen.
- Tables and lists have loading skeletons, an error state with Retry, and empty states.

## 4. Placement & Visual Integration
- Back arrow top-left. Modal close X top-right. Primary/secondary actions follow the site's design system (`adminCommon.css`:
  `.save-btn`, `.cancel-btn`, `.btn`, `.modal-actions`) and the app's own components.
- Exit controls use existing design-system components and spacing tokens. No one-off or unstyled buttons.
- Enter/exit animations must not cause layout shift or blank flashes. Respect reduced-motion settings.

## 5. Dead Ends & Async
- Every async state has: a loading UI, a timeout, a cancel path, an error UI with Retry, and a way back.
- Success/confirmation screens always have a next action. No terminal screens.

## Checklist (verify per change)
- [ ] Exit is guessable without trial and error
- [ ] Escape / Android Back / browser Back map to a safe exit or route pop
- [ ] Exit controls use design-system components
- [ ] No role crossover, no Back into auth or a completed onboarding
- [ ] Scan, quiz and admin-form edge cases above handled where relevant
