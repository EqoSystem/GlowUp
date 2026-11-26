# Quick fill Skin Decisions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Quick fill on the Ingredient Curator proposes allergen, skin type, condition and risk decisions with sources or a reason, saved only on Save; the app stops counting unjudged ingredients as safe.

**Architecture:** `api/ingredient-fit.js` returns per-decision pages plus which pages did not open. A new pure module `_Shared/fitReasons.js` turns rules and that answer into what to tick and what text to show. The Curator form paints it. The app marks entries with no judgment as `unjudged`.

**Tech Stack:** Static HTML/JS, Vercel function (Node, CommonJS), Firestore, Unity C#. Tests: `node tests/run-all.mjs`, Unity Editor tests.

**Spec:** `docs/superpowers/specs/2026-11-26-quick-fill-skin-decisions-design.md`

All website paths are under `GlowUp-Admin_AI_Testing/`.

## Global Constraints

- Free tier only. AI keys stay in Vercel environment variables.
- Tiers come from `evidenceTier` (`_Shared/glowupRules.js`): avoid and good need tier 1 or 2; `harmful` needs tier 1; `caution` tier 1 or 2. `safe` is never suggested.
- Quick fill fills empty boxes and untouched groups only, and writes nothing to Firestore.
- Nothing about a user is sent to any service.
- No hover tooltips. No new visual style: reuse `field-hint`, `btn`, `chip`, `source-link`.
- DOM order = visual order = tab order. No scroll jump or focus move after Quick fill.
- Leave `sourcedFlags`, `autoFitPatch` and `autoDetails` behaviour unchanged.
- No APK build, no deploy. Commit after each task, staging only that task's files (the tree has unrelated changes).
- Follow `docs/NAVIGATION_RULES.md`; list the rules touched at the end.

## Review Focus

- Admin changes the INCI name after Quick fill: ticks and reasons Quick fill added are removed, and `fitChecked` is cleared (Task 3).
- Admin closes the form, or opens another entry, while the search is running: the late answer is ignored (Task 3).
- Quick fill filled boxes, then the admin closes without saving: the existing discard guard asks first (Task 3).
- Old entries with no risk level and no flags become "Not checked" and may stop products being recommendable: count them and tell the user before any deploy (Task 5).
- A cited page address points somewhere other than an accepted public site: the server never opens it (Task 1).

---

### Task 1: Server returns reasons

**Files:**
- Modify: `api/ingredient-fit.js`
- Test: `tests/ingredientFitAi.test.mjs`

**Interfaces:**
- Produces: the existing response fields unchanged (`found`, `allergenTags`, `avoidForSkinTypes`, `avoidForConditions`, `why`, `sources`; `found` still means an allergen or avoid flag exists), plus:
  - `status`: `"found" | "neutral" | "no-source"`
  - `items`: `[{ kind: "allergen"|"avoidType"|"goodType"|"avoidCondition"|"goodCondition", id, why, url, title }]`
  - `risk`: `{ level: "caution"|"harmful", why, url, title } | null`
  - `pages`: `[{ title, url }]` (every accepted citation)
  - `blocked`: `[url]`
- `async function unreachable(urls, fetcher) -> string[]` (exported). GET with 5 s timeout, `redirect: "manual"`; opens only `https:` URLs whose host is on `CREDIBLE_SITES`; status 200 to 399 is reachable; any other URL or result is returned as blocked.

- [ ] **Step 1: Write failing tests** (add to the existing file, same `ok` and `reply` helpers)

```js
const full = readAnswer(reply({ items: [
    { kind: "avoidType", id: "Sensitive", why: "irritant", url: "https://dermnetnz.org/a" },
    { kind: "goodCondition", id: "acne", why: "helps", url: "https://dermnetnz.org/a" },
    { kind: "avoidType", id: "Weird", why: "x", url: "https://dermnetnz.org/a" },
    { kind: "allergen", id: "nut", why: "x", url: "https://madeup-blog.example/p" }],
  risk: { level: "safe", why: "x", url: "https://dermnetnz.org/a" },
  sources: [{ title: "DermNet", url: "https://dermnetnz.org/a" }] }, ["dermnetnz.org"]), allergens, conditions);
ok(full.status === "found" && full.items.length === 2, "unknown ids and unopened links are dropped");
ok(full.found && full.avoidForSkinTypes.join() === "Sensitive" && full.risk === null, "avoid still reported; safe is never a risk");

const neutral = readAnswer(reply({ items: [], risk: null, sources: [{ title: "D", url: "https://dermnetnz.org/a" }] }, ["dermnetnz.org"]), allergens, conditions);
ok(neutral.status === "neutral" && !neutral.found && neutral.pages.length === 1, "pages read, nothing for or against");
ok(readAnswer(reply({ items: [], sources: [] }, []), allergens, conditions).status === "no-source", "no page = no-source");

const opened = [];
const bad = await unreachable(["https://dermnetnz.org/ok", "https://dermnetnz.org/no", "http://dermnetnz.org/x", "https://127.0.0.1/x"],
  async (u) => { opened.push(u); return { status: u.endsWith("/ok") ? 200 : 403 }; });
ok(bad.length === 3 && opened.length === 2, "refused pages are blocked; other addresses are never opened");
```

Update the older cases in the file to the `items` answer shape, keeping what each one asserts.

- [ ] **Step 2:** `node tests/ingredientFitAi.test.mjs` → FAIL.
- [ ] **Step 3:** Change both prompts to ask for `items`, `risk` and `sources`, including "good for" decisions only when a medical or scientific page says it helps. `readAnswer` applies the same URL rule to every item and to `risk` as to `sources`, and derives the old fields from `items`. The handler adds `blocked = await unreachable(pages.map(p => p.url), fetch)`, returns 429 `{error:"wait"}` when Gemini answers 429, and returns `status: "no-source"` when the search finds no page. Update the header comment.
- [ ] **Step 4:** `node tests/ingredientFitAi.test.mjs` → ALL PASSED.
- [ ] **Step 5:** Commit `feat(website): ingredient-fit returns reasons, good-for and blocked pages`.

### Task 2: Decide what to tick and what to say

**Files:**
- Create: `_Shared/fitReasons.js`, `tests/fitReasons.test.mjs`

**Interfaces:**
- Consumes: Task 1's response; `suggestFit` (`ingredientFit.js`); `evidenceTier`.
- Produces:
  - `planFit({ rules, answer, error }) -> { allergenTags, avoidForSkinTypes, goodForSkinTypes, avoidForConditions, goodForConditions, risk, sources, notes, neutral }`
  - `rules` is `suggestFit`'s result; `answer` is the server response or `null`; `error` is `""`, `"wait"`, `"not-set-up"`, `"sign-in"`, `"ai"`, `"timeout"` or `"offline"`.
  - `notes`: `{ allergens, skinTypes, conditions, risk }`, each `[{ text, url }]`, never empty.
  - `sources`: `"title | url"` lines for accepted pages used. `neutral`: boolean, true only when `answer.status === "neutral"` with an accepted page, no error and no rule flag.

Exact note texts:

| Case | Text |
|---|---|
| Filled | `<label>: <why>` with its url |
| `no-source` | `Not filled: no source found for this name.` |
| Tier not accepted | `Not filled: unknown source, not used: <host>` |
| All pages for it blocked | `Not filled: website blocked or unreachable: <host>` with url |
| Nothing for that group, pages accepted | `Neutral: checked, no effect reported.` with url |
| `wait` | `Not filled: search limit reached. Try again later.` |
| other errors | `Not filled: look-up did not finish: <cause>` (`the service is not set up`, `sign in again`, `the AI service failed`, `it took too long`, `no internet connection`) |

- [ ] **Step 1: Failing test:** one case per table row, plus: PubChem url (tier 3) is unaccepted; `harmful` with a DermNet url gives `risk === null` and the unknown-source note; a good-and-avoid clash on one id keeps avoid only; with `error: "wait"`, rule flags still fill.
- [ ] **Step 2:** `node tests/fitReasons.test.mjs` → FAIL.
- [ ] **Step 3:** Implement `planFit` as a pure function (no DOM, no network).
- [ ] **Step 4:** Run → ALL PASSED.
- [ ] **Step 5:** Commit `feat(website): reasons for filled and unfilled skin decisions`.

### Task 3: The form

**Files:**
- Modify: `_Admin_Ingredient_Curator/adminIngredientCurator.html`, `adminIngredientCurator.js` (`quickFillIngredient`, `openIngredientForm`, `saveIngredient`, ingredient read mapping), its stylesheet only if existing classes do not suffice
- Modify: `FIRESTORE_SCHEMA.md` (field `fitChecked`: `"neutral"` or `""`, written by the website only)

**Interfaces:**
- Consumes: `planFit` (Task 2), `writeChecks`, `writeFit`, `readFit`, `readChecks`, `sourcedFlags`' request shape.
- Produces: `ingredients/{id}.fitChecked`.

No `firestore.rules` change: admin writes are not limited to a key list.

- [ ] **Step 1:** Move the Quick fill button and status directly under `#ingredientNameInput` (remove the separate `#ingredientQuickFill` section, keep the ids `ingredientQuickBtn` and `ingredientQuickStatus`). Hint text: `Fills empty boxes and proposes skin decisions with their sources. Nothing is saved until you press Save.`
- [ ] **Step 2:** Add `<div class="field-hint fit-why" id="allergenWhy|skinTypesWhy|conditionsWhy|riskWhy">` directly under each of the four controls. Links use `source-link` with `target="_blank" rel="noopener noreferrer"`.
- [ ] **Step 3:** In `quickFillIngredient`, after the PubChem part, run `suggestFit`, call `/api/ingredient-fit` (30 s timeout), map failures to `error`, then apply `planFit`: tick only untouched groups, append `sources` within 10 lines and 900 characters, paint the notes. A risk suggestion shows a `btn` labelled `Use <Level>` that sets the select; Quick fill never sets it itself.
- [ ] **Step 4:** Summary in `ingredientQuickStatus`: `Filled <n> boxes, <m> not filled: see the notes below each. Nothing is saved yet.`
- [ ] **Step 5:** Drop a late answer if the form closed, another entry opened or the name changed. On name change, undo the ticks Quick fill made, clear notes and `fitChecked`. `openIngredientForm` clears notes and loads the stored `fitChecked`; `saveIngredient` writes it.
- [ ] **Step 6:** Check on a local copy with a stubbed `/api/ingredient-fit` (see memory: website test rig; no live Firebase): found, neutral, no-source, 429, blocked; Tab order from name to Save; 390 px width; Escape after filling asks to discard. Screenshot each.
- [ ] **Step 7:** `node tests/run-all.mjs` → no failures. Commit `feat(website): Quick fill shows skin decisions with reasons`.

### Task 4: Unjudged is not safe

**Files:**
- Modify: `GlowUp/Assets/_Project/Scripts/Data/DataModels.cs`, `Data/IngredientRepository.cs`, `Scan/IngredientKnowledge.cs` (`Merge`, `Clone`), `Scan/ProductAnalysisEngine.cs`, `Scan/ProductRecommender.cs`
- Modify: `GlowUp-Admin_AI_Testing/_Shared/glowupCatalog.js` (the `usable` rule)
- Test: `GlowUp/Assets/_Project/Editor/Tests/IngredientReaderTests.cs`, `GlowUp-Admin_AI_Testing/tests/recommendability.test.mjs`

**Interfaces:**
- Produces: `IngredientData.fitChecked` (string, read from Firestore), `IngredientData.unjudged` (bool).
- Rule (same in C# and JS): unjudged = no built-in entry, `riskLevel` empty, no allergen, avoid or good value, and `fitChecked != "neutral"`.

- [ ] **Step 1: Failing tests.** An unjudged website entry gives finding level `unknown`; the same with `fitChecked = "neutral"` gives `safe`; a user's allergen on it still gives `harmful`. JS: unjudged ingredient is not usable; neutral-checked is.
- [ ] **Step 2:** Run both → FAIL. Unity tests run in batch mode with the Editor closed; if it is open, compile-check with the bundled csc and report the Unity tests as not run.
- [ ] **Step 3:** `Merge` sets `unjudged` where it now sets `riskLevel = "safe"` (keep that assignment). The engine treats `unjudged` like `generic` for a `safe` level, with the sentence `Nobody has judged this ingredient yet, so it is not part of this result.` `ProductRecommender` adds `unjudged` to its not-evaluable test. Check whether the website's built-in knowledge is visible to `glowupCatalog.js`; if not, report that difference to the user before changing the rule.
- [ ] **Step 4:** Run both → pass.
- [ ] **Step 5:** Commit `fix: an ingredient nobody judged is not counted as safe`.

### Task 5: Records and counts

**Files:**
- Modify: `docs/SOURCES_REGISTER.md` (it already has uncommitted changes: do not commit the user's lines without saying so), `docs/DATA_SOURCE_POLICY.md`

- [ ] **Step 1:** Add the rule rows: AI-read pages decide avoid and good under tiers 1 and 2; unjudged is not safe. Cite nothing not seen.
- [ ] **Step 2:** Count catalogue ingredients that are unjudged (public read of `ingredients`), and how many `recommendable` products use one. Report the numbers.
- [ ] **Step 3:** `node tests/run-all.mjs`; run `graphify.exe update .`; commit.
- [ ] **Step 4:** Report: tests run, not-run items, navigation rules touched, nothing built or deployed.
