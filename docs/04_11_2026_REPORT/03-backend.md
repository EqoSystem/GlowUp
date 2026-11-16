# 03. Backend details

## What runs on the server

The project has three Vercel functions. Their maximum duration is configured as 60 seconds each in `GlowUp-Admin_AI_Testing/vercel.json`; the same file configures a daily cron at 03:00 for `/api/archive-purge`.

| Route | Request and server work | Data/services |
|---|---|---|
| `/api/assist-ingredient` | Admin AI assistance for ingredient descriptions, product ingredient lists and imported text. It validates the Firebase ID token and checks the caller's `admins/{uid}` document before asking Gemini. The AI output is a draft; website actions control saving. | Firebase Identity Toolkit, Firestore REST, Gemini; optional model/provider environment variables are described in the function source. `api/assist-ingredient.js`. |
| `/api/scan-snapshot` | Signed-in mobile caller sends one compressed JPEG and scan hints. Function validates identity and image, spends quota counters, calls Gemini with search grounding and falls back once to photo-only reading when search grounding is rate-limited. | Firebase Auth token verification, Firestore REST quota writes, Gemini. `api/scan-snapshot.js`. |
| `/api/archive-purge` | Cron-only, dry-run by default; checks `CRON_SECRET`, obtains a Google access token from the configured service account, deletes only expired direct children of `archive`, at a capped batch size, then writes a run log. | Firestore REST and Vercel environment variables. `api/archive-purge.js`. |

The website's `/` route is redirected to `/_Index/index.html`; Firebase Firestore rules are declared by `firebase.json`. The emulator is configured for `127.0.0.1:8085` (`vercel.json`, `firebase.json`).

## How clients reach data

- The admin website initializes Firebase JavaScript SDK in `_Database/firebase-console.js`. It reads and writes Firestore from browser modules; `firestore.rules` is the authorization boundary.
- The Unity app initializes Firebase and uses repositories for brands, products, ingredients, lists, user profile and user scan history. `FirebaseBootstrap.cs` configures Firestore persistence; repositories use cached data and `CatalogSync` for refreshing.
- The mobile app's Gemini scan client sends requests directly to Google's Generative Language API (`GlowUp/Assets/_Project/Scripts/Scanning/Geminiapiclient.cs`). This is not a GlowUp backend route.
- The mobile app also calls `/api/scan-snapshot` when its scan flow offers the photo fallback. Vercel returns the AI reading; the app stages the result in its own Firestore path as governed by the rules.

Sources: `GlowUp/Assets/_Project/Scripts/Data/FirebaseBootstrap.cs`, `Data/BrandRepository.cs`, `Data/ProductRepository.cs`, `Data/IngredientRepository.cs`, `Data/SyncQueue.cs`; `GlowUp-Admin_AI_Testing/_Database/firebase-console.js`, `firestore.rules`, `firebase.json`, `vercel.json`, and the three `api/*.js` files.

## Jobs and scheduled work

One cron is configured: the archive purge runs daily at `0 3 * * *`. It does nothing without the secret and matching service-account environment settings; deletion is opt-in via the live-mode environment value. No other scheduled server job is found in `vercel.json`. Firebase scheduled functions are **NOT FOUND IN CODE**.

## Endpoints not present

The admin app does not expose a conventional CRUD REST API for the catalogue in the checked source. Browser pages use Firestore client calls directly. **NOT FOUND IN CODE:** separate custom API routes for user profiles, scan history, brand/product inventory CRUD, or ingredient curation.
