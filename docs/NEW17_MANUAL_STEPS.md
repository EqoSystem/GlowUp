# New17: steps that must be done by hand

Written 16 November 2026. Everything else of New17 is committed, deployed (glowupapp.xyz) and built (`GlowUp/Builds/GlowUp.43.apk`).

## 1. Firebase email action URL: not possible, nothing to do

Firebase blocks changes to the email templates, including **Customize action URL**, for this project.
The console shows an error, and the API answers `EMAIL_TEMPLATE_UPDATE_NOT_ALLOWED` (checked 16 November 2026, free plan).

Email confirmation works without it:
1. The link in the email opens Firebase's own page: "Your email has been verified".
2. Back in GlowUp, Step 1 checks every few seconds and moves on by itself. The **I have confirmed** button does the same at once.

The page `https://glowupapp.xyz/app/verified/` stays online, unused. It would only be used if the action URL could be set one day.
No SHA-256 fingerprint is needed anywhere.

## 2. Install and test the APK on a phone

1. Copy `GlowUp/Builds/GlowUp.43.apk` to the phone and open it.
   - Older GlowUp builds update in place, because they use the same debug key.
2. Check these, in this order:
   1. **Sign up**: Step 1 puts the cursor in the first empty box. A wrong retyped email or password shows a hint. Step 5 shows the Terms.
   2. **Email confirmation**: open the email and tap the link. Firebase's page says the email is verified. Go back to GlowUp: Step 1 moves on by itself within a few seconds, or tap **I have confirmed**.
   3. **Log in**: the password shows as dots, and the last character shows for 1 second. Changing the email clears the password. Test **Forgot password** with an address that exists and with one that doesn't: both show the same message.
   4. **Settings**:
      - **Log Out?** panel: Cancel and Log Out.
      - **Retake**: Steps 1 to 5 open with your answers filled in. Test both **Save edits** and **Cancel**.
      - Volume keys **On / Off**.
      - Finger signs are gone.
   5. **Scan**: with volume keys On, one key takes a photo and both keys together switch to live scan. Each shows a toast.
   6. **Delete account with Google**: type the confirm phrase. When you signed in less than 4 minutes ago, Google does not ask again.
   7. **Messages**: one message at a time, near the bottom. When the keyboard is open, they move to the top.

## 3. Decisions waiting for you

1. **Cleanup (A3)**: these are unused and were **not** deleted. Say which to remove, if any:
   - `GlowUp/Assets/_Project/Scripts/Settings/SupportSuggestions.cs`
   - `GlowUp/Assets/_Project/Scripts/Data/CatalogStatus.cs` (an unbuilt "Offline data" row for Settings)
   - `GlowUp-Admin_AI_Testing/tests/responsive/dashboard_suggestions_360.png` and `_390.png`
2. **Paper, Scope and Limitations**: offline use is not listed. Text you can paste:
   > **Offline Use**: Without internet, the application can sign in only accounts that have signed in online on the same device, and can recognize only products, brands and ingredients already stored on the phone. Sign-up, Google sign-in, email confirmation, password reset, AI analysis and FDA verification require an internet connection; changes made offline are synchronized once the device reconnects.
