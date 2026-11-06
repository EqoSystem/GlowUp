# 07. Security plan

The lists separate protections found in project code from recommendations that are not established by that code. “Implemented” means a mechanism exists in the checked-in source; it does not prove deployment configuration, effectiveness, or complete coverage.

## Authentication

### IMPLEMENTED (found in code)
- Mobile online email/password authentication uses Firebase Authentication; the login screen has offline cached-account behavior (`GlowUp/Assets/_Project/Scripts/Auth/LoginFlowController.cs`, `UserProfileRepository.cs`).
- Website login uses Firebase Authentication; protected pages call `requireAdmin()` and require a matching admin record (`GlowUp-Admin_AI_Testing/_Index/indexApp.js`, `_Shared/adminCommon.js`).
- AI assistance endpoint validates Firebase identity and admin status before AI requests (`api/assist-ingredient.js`).

### RECOMMENDED (not implemented)
- Confirm and document live Firebase Auth provider settings and account recovery policy; source alone does not establish console configuration.
- Consider MFA for administrators. **NOT FOUND IN CODE:** an MFA requirement.
- Avoid treating offline app login as fresh server authentication; show the offline limitation clearly.

## Authorization and Firestore rules

### IMPLEMENTED (found in code)
- Firestore rules distinguish signed-in users, admin claims/admin documents, and owners (`GlowUp-Admin_AI_Testing/firestore.rules`).
- User profile and scan paths are owner-only; catalogue reads are public and admin writes are restricted.
- App-originated catalogue records are constrained to create-only pending/unreviewed forms with required owner/provenance fields.
- Scan activity is anonymous in the model and rules expose reads only to admins (`firestore.rules`, `GlowUp/Assets/_Project/Scripts/Data/DataModels.cs`).
- Snapshot records and quota documents have owner/admin checks and bounded fields; admin-only snapshot image reads are stated in the schema/rules.
- Archive purge restricts the collection path, requires cron authorization, uses an opt-in live mode and a per-run cap (`api/archive-purge.js`).

### RECOMMENDED (not implemented)
- Verify that the checked-in rules are the rules currently deployed to the Firebase project; **NOT FOUND IN CODE:** proof of deployment state.
- Add automated coverage for every collection/action, including reads, deletes, updates, and malformed nested data.
- Review the older, looser `faceProduct()` checks for snapshots/app submissions against current makeup-only product logic; schema notes this rules check is not identical to the app/website rule (`firestore.rules`, `FIRESTORE_SCHEMA.md`).

## Input validation

### IMPLEMENTED (found in code)
- Firestore rules constrain field names, types, lengths, list sizes, status values, owner IDs, makeup scope and HTTPS links for selected write paths (`firestore.rules`).
- Snapshot endpoint validates token shape, JPEG signature/size, EXIF marker, body size, text hints and quota limits (`api/scan-snapshot.js`).
- Admin login renders feedback with `textContent` and serializes submissions; shared website code contains HTML escaping/safe rendering helpers (`_Index/indexApp.js`, `_Shared/adminCommon.js`).

### RECOMMENDED (not implemented)
- Perform a complete endpoint and form-field validation audit, including import parsing and external URL handling; the current code's validation is path-specific.
- Add rate limits to other sign-in and admin write operations if supported by deployment configuration. **NOT FOUND IN CODE:** a general API rate-limiting layer for every route.

## Secret handling

### IMPLEMENTED (found in code)
- Vercel AI and archive-purge handlers read provider/service credentials from environment variables rather than embedding them in HTML (`api/assist-ingredient.js`, `api/scan-snapshot.js`, `api/archive-purge.js`).
- Website Firebase configuration is client-side; Firestore rules provide authorization.

### RECOMMENDED (not implemented)
- Move the mobile Gemini API credential out of the shipped application and proxy those requests through an authenticated, quota-controlled server endpoint. The current mobile code returns a literal from `SecureProfileStore.cs`; it ships in the app.
- Rotate any credential that has been committed or distributed, and restrict provider keys by API/service, quotas, and origin where applicable. **NOT FOUND IN CODE:** proof of live key restrictions except the comments/configuration describing the web key referrer restriction.
- Keep secrets out of repository history, build logs and documentation.

## Transport and web protections

### IMPLEMENTED (found in code)
- External service URLs shown in configuration use HTTPS; Firestore client SDK uses Firebase transport.
- Vercel response headers set `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, and `X-Robots-Tag` (`vercel.json`).
- API responses set JSON content type and `Cache-Control: no-store` in the serverless handlers.

### RECOMMENDED (not implemented)
- **NOT FOUND IN CODE:** an explicit Content-Security-Policy header. Add and test a restrictive CSP compatible with the Firebase CDN and app's external resources.
- Confirm HTTPS enforcement, HSTS and provider referrer/API restrictions in production deployment configuration.

## Admin protections

### IMPLEMENTED (found in code)
- Each admin page waits for an auth state and admin-record check; sign-out clears page store and asks for confirmation (`_Shared/adminCommon.js`).
- Firestore rules independently enforce admin writes. The AI assistance API independently checks the admin identity.
- Archive purge refuses to run without the cron secret and service account; dry-run is default and deletion is bounded (`api/archive-purge.js`).

### RECOMMENDED (not implemented)
- **NOT FOUND IN CODE:** MFA, per-admin fine-grained permissions, approval/audit log coverage for all catalogue changes, or a backend-issued admin role beyond Firebase rules.
- Review failure paths in the browser guard: an admin-record lookup error is treated as not-admin; provide an explicit connectivity error without weakening authorization.

## AI/OCR and personal data handling

### IMPLEMENTED (found in code)
- Admin AI endpoint sends only the selected ingredient/product/file-extracted text and no account/profile fields, as described in `api/assist-ingredient.js`.
- Snapshot API accepts a single size-limited JPEG, rejects EXIF-bearing files, validates the signed-in user and marks returned records as pending/unverified (`api/scan-snapshot.js`, `FIRESTORE_SCHEMA.md`).
- Mobile OCR is part of the scan flow; scan activity records omit UID, name and email (`SmartScanFlowController.cs`, `DataModels.cs`, `firestore.rules`).

### RECOMMENDED (not implemented)
- Publish and test a user-facing retention/consent policy for camera frames, image snapshots, AI provider processing and local cache; **NOT FOUND IN CODE:** a complete retention policy covering all these locations.
- Review the mobile Gemini direct request for data minimization and the shipped-key risk; do not assume photos/text are private after transmission to a third-party AI provider.
- Verify that local profile/cache files are protected appropriately on device. **NOT FOUND IN CODE:** an application-level encryption layer for `OfflineCache`.
