# 02. Frameworks, libraries and tools

Versions below are copied from project manifests, project metadata or literal CDN URLs. A source declaration does not prove which version is deployed at runtime.

| Framework/library/tool | Version | Where used | What it is used for |
|---|---|---|---|
| Unity Editor | 2022.3.62f3 | `GlowUp/ProjectSettings/ProjectVersion.txt` | Mobile application editor/build target. |
| Unity Universal Render Pipeline | 14.0.12 | `GlowUp/Packages/manifest.json` | Unity rendering pipeline package. |
| Unity AR Foundation | 5.2.2 | `GlowUp/Packages/manifest.json` | AR feature framework. |
| Unity ARCore XR Plugin | 5.2.2 | `GlowUp/Packages/manifest.json` | Android AR platform provider. |
| Unity UI (uGUI) | 1.0.0 | `GlowUp/Packages/manifest.json` | Mobile screen UI. |
| TextMesh Pro | 3.0.7 | `GlowUp/Packages/manifest.json` | Text and input controls in Unity UI. |
| Unity Mobile Notifications | 2.4.3 | `GlowUp/Packages/manifest.json` | Local notifications package. |
| Unity Test Framework | 1.1.33 | `GlowUp/Packages/manifest.json` | Unity test support. |
| Unity Visual Scripting | 1.9.4 | `GlowUp/Packages/manifest.json` | Visual scripting package dependency. |
| Unity Timeline | 1.7.7 | `GlowUp/Packages/manifest.json` | Timeline package dependency. |
| Firebase JavaScript SDK: app | 10.12.0 | CDN module imports in `GlowUp-Admin_AI_Testing/_Database/firebase-console.js` and page preload declarations | Initialize the website Firebase app. |
| Firebase JavaScript SDK: Auth | 10.12.0 | `_Database/firebase-console.js`, `_Shared/adminCommon.js`, `_Index/indexApp.js` | Admin login, sign-out, and auth state. |
| Firebase JavaScript SDK: Firestore | 10.12.0 | `_Database/firebase-console.js` and page modules | Browser Firestore reads/writes. |
| Firebase Unity SDK | **NOT FOUND IN CODE** as a package version in the inspected `Packages/manifest.json` | `GlowUp/Assets/_Project/Scripts/Data/FirebaseBootstrap.cs` and Firebase C# namespaces | Mobile Firebase initialization, Auth and Firestore. A directory name is not proof of the resolved SDK version. |
| Gemini Generative Language API | **NOT FOUND IN CODE** as a service version number | `GlowUp/Assets/_Project/Scripts/Scanning/Geminiapiconfig.cs`, `Geminiapiclient.cs`; Vercel APIs | AI scan, ingredient/product assistance and photo reading. Model IDs are configuration, not framework versions. |
| Vercel Functions | **NOT FOUND IN CODE** as a package version | `GlowUp-Admin_AI_Testing/api/*.js`, `vercel.json` | Serverless API endpoints and scheduled archive purge. |
| Node.js | **NOT FOUND IN CODE** as a required runtime version | Vercel handlers use JavaScript/Node built-ins and tests are `.mjs` | Serverless code and project test scripts. |
| Tesseract.js | **NOT FOUND IN CODE** as a package version | `GlowUp-Admin_AI_Testing/FIRESTORE_SCHEMA.md` and the browser document reader/import modules | Browser OCR for image-only PDF imports, as documented by the project schema. |
| Firebase CLI / Firestore Emulator | **NOT FOUND IN CODE** as a tool version | `GlowUp-Admin_AI_Testing/firebase.json` | Emulator host/port are configured; no CLI version is declared. |
| Mermaid CLI / Pandoc / LaTeX PDF engine | **NOT FOUND IN CODE** as installed versions | `docs/build-pdf.mjs` (this documentation build helper) | Render diagrams and convert documentation to PDF; install versions are environment-dependent. |

The Unity manifest also declares Unity IDE integrations and built-in `com.unity.modules.*` packages. Their exact manifest versions are listed in `GlowUp/Packages/manifest.json`; these are Unity package identifiers, not separately evidenced external libraries. No admin root `package.json` was found in the repository map. **NOT FOUND IN CODE:** an admin website-wide lockfile or manifest declaring versions for all browser-side utilities.
