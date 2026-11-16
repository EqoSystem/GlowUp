# Privacy notes (Philippine Data Privacy Act, RA 10173)

**Not legal advice.** These are working notes for the team, written from the code on 28 October 2026, to prepare a review by someone qualified
(a data protection officer or a lawyer). They describe what the app does, where it already meets the usual expectations of RA 10173 and its
Implementing Rules, and what is still open.

## 1. Why this matters for GlowUp

Skin conditions (acne, eczema, psoriasis, rosacea, dermatitis ...) and allergies are information about a person's **health**. Under RA 10173,
health information is **sensitive personal information**, which as a rule may be processed only with the person's **specific, informed consent**
(or another lawful basis the law names). Birthdays and the ages of minors need care too.

## 2. What the app collects, and where it goes (from the code)

| Data | Kept where | Who can read it | Sent to the AI? |
|---|---|---|---|
| First, middle and last name, e-mail | `users/{uid}`; Firebase Authentication | the user only (rules); not admins | No |
| Birthday and age | `users/{uid}` | the user only | No |
| Skin type, skin tone, skin conditions, allergens | `users/{uid}`, and a copy on the phone | the user only | **No** (checked in `ScanAnalysisPipeline`, `AiResearchClient`, `ProductAiAdvisor`; the policy says the same). The comparison with a product is made on the phone |
| Scan history | `users/{uid}/scans`, and on the phone | the user only | The label text and up to four photos **of the product** may be sent to recognise it; never the profile |
| Anonymous scan note | `scanActivity` | admins | Holds no user id, name or e-mail: product, verdict, counts, whether an allergen matched (yes/no) |
| Allergens / conditions the user typed that are not on the lists | `allergenSuggestions`, `conditionSuggestions` with the user's **account id** | admins | No |
| Product photo snapshot | `pending_products` (+ photo in `pending_product_images`) with the sender's account id, the cleaned text read on the phone, the list's page and kind | the sender (own photo) and admins (rules of 7 November 2026) | Only the crop around the product leaves the phone. The AI only names the product; the ingredient list is cut from fetched pages by the server or read from the pack. The photo is deleted when an admin approves or rejects the entry. Firebase Storage is NOT used (it needs the paid Blaze plan; the project stays on the free plan): the photo is a Firestore document of at most 80 KB. Policy section "Product Photo Submissions". |
| Face picture in AR Try-On, selfie camera for finger signs | not stored | nobody | No (processed on the phone) |

No analytics or crash-reporting SDK was found in the app (no Firebase Analytics, Crashlytics or Unity Analytics calls), so no health field goes to an analytics service.

## 3. What is already in place

- **Consent screen**: after Step 5, the Terms and Privacy Policy are shown in full and the user must tick a box before the sign-up finishes
  (`TermsAndConditionScene`, `Legal/PrivacyPolicyAndTerms.txt`). The policy states the purposes (section 3) and that the profile never goes to the AI (section 5).
- **Delete my account and data**: Settings > Delete My Account (`Scripts/Settings/AccountActions.cs`). It asks for the password again, deletes the scan
  history, the profile and the sign-in, then everything on the phone. The policy text said deletion was only "by contacting our support team"; it was
  corrected on 28 October 2026 to describe the button and what it does not remove (see 4.2).
- **Not a medical device**: Terms section 3 (not a medical device, not a diagnosis, see a dermatologist, patch test). The result screen and the dashboard
  carry "not medical advice" notes; the analysis summary ends with "This is guidance, not a medical opinion. Patch-test any new product first."
- **No "Digital Medical ID" wording**: searched for in the app and the website; none was found. The feature is called the Skin Profile.
- **Minimum age 13**: the birthday is checked on Step 1 and again on Step 5 (`minimumAge = 13`); Terms section 2 asks under-18s to have a parent or guardian agree.
- **Access control**: only the user can read or write `users/{uid}`; admins cannot (tested in the emulator, `tests/rules`).
- **Minimisation of what goes out**: only the domain part of an e-mail address goes to the DNS check, only a barcode to Open Beauty Facts, only a product
  name to the FDA Philippines portal (policy section 5).

## 4. What is open

1. **Separate, specific consent for health data.** Today one tick box covers the whole policy and terms. RA 10173 expects consent to sensitive personal
   information to be specific. Suggested: a second tick box on Step 4 or 5, worded for example *"I agree that GlowUp keeps my skin conditions and allergies
   to check products for me. I can change or delete them at any time in Settings."* Needs a builder change and a scene rebuild in Unity.
2. **What Delete My Account leaves behind.** Suggestions (`allergenSuggestions`, `conditionSuggestions`) and photo snapshots (`pending_products`) keep the
   account id after the account is deleted. A suggestion can itself be health information ("nickel allergy"). Options: let the rules allow a user to delete
   their own suggestions and snapshots and have Delete My Account remove them, or have an admin remove them on request. The policy now says these are
   kept until reviewed and can be removed on request.
3. **Contact details.** Both the Privacy Policy (section 12) and the Terms (section 16) still say `[add your support email here]`. RA 10173 also expects
   the contact of a **data protection officer**. Fill both before the app is given to anyone.
4. **Rights in the policy.** Section 10 names access, correction, deletion and withdrawal of consent "depending on where you live". The Philippine
   rights are wider (to be informed, to object, to data portability, to damages, and to complain to the National Privacy Commission). Suggested: name
   RA 10173 and the NPC in section 10.
5. **Birthday (data minimisation).** The full birthday is kept. Justification found in the code: products can carry a minimum age (`minAge`), and the
   app needs the user's **current** age for every scan (`ProductAnalysisEngine`, age checks); a stored age goes stale. Keeping only the year and month
   would be enough for that check and is the smaller option; this is a decision for the team.
6. **Minors (13 to 17).** The app accepts users from 13 and only asks, in the Terms, that a parent or guardian agreed. There is no step where the parent
   confirms. Either raise the minimum age to 18, or add a parent/guardian confirmation for users under 18. Decision for the team; the code keeps 13
   (it matches Terms section 2).
7. **AI key inside the app.** Not a privacy breach by itself (the profile is never sent), but anyone who unpacks the APK can use the project's Gemini key.
   Moving the phone's AI calls behind a server function (as `api/scan-snapshot.js` already does for photos) would close it.
8. **AR Try-On on-screen note.** The Terms say the preview is simulated and colours are approximate (section 5), but the AR screen itself does not say it.
   Suggested line under the shade picker: *"Preview only: colours on screen are approximate."* Needs a builder change and a scene rebuild.
