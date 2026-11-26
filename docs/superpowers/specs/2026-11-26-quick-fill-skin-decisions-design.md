# Quick fill decides skin fit, with reasons (design)

Approved in conversation on 26 November 2026.

## Intent

The admin types an INCI name (or starts from a product's ingredient list) and the website fills in the rest, including
which skin types, sensitivities and chronic skin conditions the ingredient is bad for, good for or neutral for. The
admin checks it and saves. Every decision comes from a published source, not from the system's own judgment.

## Goals

- Quick fill on the Ingredient Curator also fills allergen families, skin types and skin conditions (avoid and good).
- Every filled decision shows its source. Every box left empty says why.
- Nothing filled by Quick fill reaches the database until the admin presses Save.
- An ingredient with no evidence is never treated as safe by the app.

## Non-goals

- Quick fill in the mobile app. The app only reads what the admin saved. Its existing AI look-up stays as it is.
- Changing the page-load automation that already saves avoid flags (`autoFitPatch`, `sourcedFlags`).
- Setting `Safe` automatically.

## Constraints

- Free tier only. AI keys stay in Vercel environment variables.
- Source tiers follow `docs/DATA_SOURCE_POLICY.md`. `Harmful` needs a T1 page. A "good for" decision needs a T1 or T2 page.
- Make-up and face products only. Nothing about a user is sent to any service.
- No hover tooltips on the admin site: reasons are visible text.
- Add a row to `docs/SOURCES_REGISTER.md` for every new source or rule, in the same change.
- No APK build and no deploy until the user asks.

## Approaches

1. **Extend Quick fill and `api/ingredient-fit.js`** to return reasons and "good for" decisions. Reuses the search,
   limits and source checks. **Picked.**
2. A separate new service. Repeats the same search and uses more of the free limits. Not picked.
3. Quick fill in the app. No admin review, answers could differ between phones, slower scans. Not picked.

## What the admin sees

Quick fill fills empty boxes only. Below each box it fills, or leaves empty, is a short line:

| Case | Text |
|---|---|
| Filled | What the source says, with its link |
| Search found nothing | No source found for this name. |
| Page is not from an accepted site | Unknown source, not used: *site* |
| Site refused or did not open | Website blocked or unreachable: *site* (link stays clickable) |
| Checked, nothing for or against | Neutral: checked, no effect reported. (links) |
| Daily or per-minute limit | Search limit reached. Try again later. |
| Timeout, service off, no internet | Look-up did not finish: *cause* |

Risk level is a suggestion with its reason, which the admin applies. `Harmful` is suggested only with a T1 page. With no
source it stays empty.

The server tries to open each cited page. A page that refuses automated visits may still open for the admin.

## UI requirements

- New buttons, lines and controls use the existing design system of the admin site; no new visual style.
- The form is worked from top to bottom: INCI name, Quick fill, filled boxes in order, Save. Tab and screen-reader order
  match the visual order. Quick fill sits next to the INCI name.
- After Quick fill, the page does not jump or move focus away. A summary says how many boxes were filled and how many
  were not.
- Works on the phone layout. Follows `docs/NAVIGATION_RULES.md`.

## App change

An entry with no risk level and no skin or allergen flags shows as "Not checked" instead of counting as `safe`
(`ProductAnalysisEngine`). An entry whose sources were checked and found neutral stays checked, so saving has to store
that fact. Built-in knowledge keeps working as it does now.

## Left to the builder

Field names, the shape of the server answer, where the stored "checked, neutral" mark lives (with the matching
`firestore.rules` and `FIRESTORE_SCHEMA.md` updates), and the tests.
