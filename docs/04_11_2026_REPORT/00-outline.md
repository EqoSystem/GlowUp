# GlowUp technical documentation outline

This outline names the chapters and their main code sources. It is an index, not a substitute for reading those sources. Claims in the chapters are limited to what those sources show. If an item or behavior cannot be confirmed, the chapter says **NOT FOUND IN CODE** or **PARTIALLY TRACED**.

| Chapter | Subject | Main sources |
|---|---|---|
| 01 | System overview and architecture | `GlowUp/Assets/_Project/Scripts/Data/FirebaseBootstrap.cs`; `GlowUp/Assets/_Project/Scripts/Scan/SmartScanFlowController.cs`; `GlowUp/Assets/_Project/Scripts/Scanning/Geminiapiclient.cs`; `GlowUp-Admin_AI_Testing/_Database/firebase-console.js`; `GlowUp-Admin_AI_Testing/api/assist-ingredient.js`; `GlowUp-Admin_AI_Testing/vercel.json` |
| 02 | Frameworks, libraries and tools, with declared versions | `GlowUp/ProjectSettings/ProjectVersion.txt`; `GlowUp/Packages/manifest.json`; `GlowUp-Admin_AI_Testing` HTML CDN import maps/module URLs; `GlowUp-Admin_AI_Testing/tests/rules/package.json`; Vercel/Firebase config files |
| 03 | Backend details | `GlowUp-Admin_AI_Testing/api/*.js`; `GlowUp-Admin_AI_Testing/firestore.rules`; `GlowUp-Admin_AI_Testing/firebase.json`; `GlowUp-Admin_AI_Testing/vercel.json`; mobile `GlowUp/Assets/_Project/Scripts/Data/*Repository.cs` |
| 04 | Authentication and authorization | Mobile `GlowUp/Assets/_Project/Scripts/Auth/LoginFlowController.cs`, `SocialSignIn.cs`, `Data/UserProfileRepository.cs`; website `_Index/indexApp.js`, `_Shared/adminCommon.js`; `firestore.rules`; API handlers |
| 05 | Database | `GlowUp-Admin_AI_Testing/FIRESTORE_SCHEMA.md`; mobile `Data/DataModels.cs`, `Data/FirestoreFields.cs`, repositories, `Data/SyncQueue.cs`, `Data/OfflineCache.cs`; `firestore.rules` |
| 06 | API keys and secrets | `GlowUp/Assets/_Project/Scripts/Security/SecureProfileStore.cs`; `GlowUp/Assets/_Project/Scripts/Scanning/Geminiapiconfig.cs`; `GlowUp-Admin_AI_Testing/_Database/firebase-console.js`; `GlowUp-Admin_AI_Testing/api/*.js`; Firebase Android configuration file location |
| 07 | Security plan | `GlowUp-Admin_AI_Testing/firestore.rules`; `GlowUp-Admin_AI_Testing/api/*.js`; mobile auth, permission, and scanning sources; `GlowUp-Admin_AI_Testing/vercel.json` |
| 08 | Mobile app fallbacks | `GlowUp/Assets/_Project/Scripts/Scan/SmartScanFlowController.cs`; `CameraFeedController.cs`; `ScanSnapshotController.cs`; `FrameQuality.cs`; `CameraPermission.cs`; `AR/CameraWaitCard.cs`; `AR/ArFaceTrackingController.cs`; `Data/FirebaseBootstrap.cs`; `Data/OfflineCache.cs`; `Net/ConnectivityMonitor.cs` |
| 09 | Mobile UI: every scene and its controls | All `.unity` files under `GlowUp/Assets/_Project/Scenes/`; each scene's matching `*FlowController.cs`; `GlowUp/Assets/_Project/Scripts/Common/BottomActionBar.cs`; Settings and onboarding controllers |
| 10 | Admin website pages and controls | `GlowUp-Admin_AI_Testing/_Index/index.html`; `_Admin_Dashboard/adminDashboard.html`; `_Admin_Brand_Inventory/adminBrandInventory.html`; `_Admin_Ingredient_Curator/adminIngredientCurator.html`; `_Admin_Archive/adminArchive.html`; `_Admin_Recycle_Bin/adminRecycleBin.html`; corresponding page modules and `_Shared/pageFrame.js`, `adminShell.js`, `adminSettingsMenu.js` |
| 11 | User/admin flows and data flow | Mobile scan, result, recent, and AR controllers; admin page modules; `api/scan-snapshot.js`; `api/assist-ingredient.js`; `firestore.rules`; `firebase.json`; `vercel.json` |

## Evidence and limits

- The mobile client is the Unity project in `GlowUp/`; the six page files listed in Chapter 10 are the HTML routes in `GlowUp-Admin_AI_Testing/`.
- The admin site and mobile client point at the same Firebase project in their configuration sources. A setting in source does not prove a live deployment setting unless the code states that explicitly.
- Version numbers are reported only where a manifest, project-version file, or dependency URL declares them. If a library is present but no version is declared in the inspected source, the chapter says **NOT FOUND IN CODE**.
- Admin functions and flows are described from the static website and Vercel functions. No separate application server is assumed.
- This outline does not certify external console settings, deployed rules, credentials, or runtime behavior.
