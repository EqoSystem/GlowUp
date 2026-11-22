# GlowUp: setup on another computer

GlowUp has two parts:

| Part | Folder | How it runs |
|---|---|---|
| Admin website | `GlowUp-Admin` (this folder) | Docker (steps 1 to 3) |
| Make-up scanner app | `GlowUp` (second zip) | Unity Editor, builds an Android APK (step 5). Docker cannot run it. |

Both parts read and write one Firestore database. Step 4 creates your own, so you do not touch the original owner's data.

## 1. Install

- Docker Desktop (docker.com). Start it and wait until it says it is running.
- A free Google account for Firebase.

## 2. Start the website

Open a terminal in this folder (the one that contains `Dockerfile` and `docker-compose.yml`) and run:

```
docker compose up --build
```

Open http://localhost:8080. Stop it with Ctrl+C. Start it again later with `docker compose up`.

Without compose:

```
docker build -t glowup-admin .
docker run -p 8080:80 glowup-admin
```

Only the web pages run in Docker. The folder `api/` (source check, archive purge, app AI) are Vercel functions and are not part of the image. Those features work only on a Vercel deployment.

## 3. Point the website at your own database

Until you do step 4 the site uses the original owner's Firebase project. Do step 4 first.

## 4. Create your own Firebase project (free plan only)

1. console.firebase.google.com, Add project.
2. Build, Authentication, Get started, enable **Email/Password**.
3. Build, Firestore Database, Create database (production mode).
4. Project settings, Your apps, add a **Web app**. Copy the `firebaseConfig` values.
5. Open `_Database/firebase-console.js`. Replace the `firebaseConfig` block with your values.
6. Publish the security rules: Firestore, Rules tab, paste the whole content of `firestore.rules`, Publish. (Or use the Firebase CLI: `npm i -g firebase-tools`, `firebase login`, `firebase use --add`, `firebase deploy --only firestore:rules`. The file `.firebaserc` names the original project, so `firebase use --add` is required.)
7. Rebuild and restart: `docker compose up --build`.
8. Open the site and create an account.
9. Make yourself admin. In the Firebase console, Authentication, copy your **User UID**. In Firestore, create the collection `admins` with a document whose ID is that UID (add any field, for example `email`). Reload the site.

Skip Firebase Storage: it needs a paid plan and the project does not need it.

Data layout: see `FIRESTORE_SCHEMA.md`. Your database starts empty. Import brands, products and ingredients with the import feature in the admin pages (format in `IMPORT_FORMAT.md`).

## 5. The Unity app (second zip)

1. Install Unity Hub and Unity **2022.3.62f3** with the **Android Build Support** module (including OpenJDK and Android SDK).
2. Unzip `GlowUp-App.zip`. In Unity Hub, Add, choose the `GlowUp` folder. The first open takes long: Unity rebuilds its `Library` folder.
3. The file `Assets/_Project/Scripts/Security/SecureProfileStore.cs` is not in the zip because it is kept private. The owner sends it separately. Put it in that folder. Without it the project does not compile.
4. In your Firebase project add an **Android app** with the package name used by the project (Unity, Edit, Project Settings, Player, Android, Package Name). Download `google-services.json` and replace `Assets/google-services.json`. Do the same for `Assets/StreamingAssets/google-services-desktop.json` (the desktop copy of the same config).
5. Enable Google sign-in in Firebase Authentication if you want it. It needs your debug key SHA-1 on the Android app.
6. File, Build Settings, Android, Build. Install the APK on a phone.

Scenes come from the builder menus under the Unity `GlowUp` or `Tools` menu (Editor scripts in `Assets/_Project/Editor`). Rebuild a scene from its menu entry if it looks wrong.

## 6. Do not

- Do not commit or share your Firebase admin credentials, `.env` files or `SecureProfileStore.cs`.
- Do not put any API key into the Docker image.
