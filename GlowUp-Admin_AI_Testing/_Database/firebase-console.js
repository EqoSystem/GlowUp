import { initializeApp } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-app.js";
import { initializeFirestore, persistentLocalCache, persistentMultipleTabManager } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { getAuth } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-auth.js"; // Added Auth import

const firebaseConfig = {
  apiKey: "AIzaSyAkb23xQ69a2Q4tpucGty9yDEoH0ab1JDM",
  authDomain: "glowup-admin-portal.firebaseapp.com",
  projectId: "glowup-admin-portal",
  storageBucket: "glowup-admin-portal.firebasestorage.app",
  messagingSenderId: "996836291256",
  appId: "1:996836291256:web:54f83ac9fb913148523a4d"
};

const app = initializeApp(firebaseConfig);

// Firestore's offline cache in the browser (IndexedDB), shared by all open tabs (28 Nov 2026 quota fix).
// Every page of this site is a full page load, and without the cache each live list (the dashboard's latest scans, the bell,
// support reports...) downloaded its whole result again on every click. With it, a list opened again within 30 minutes is
// only billed for the documents that changed since. A browser without IndexedDB (some private windows) just uses memory.
export const db = initializeFirestore(app, { localCache: persistentLocalCache({ tabManager: persistentMultipleTabManager() }) });
export const auth = getAuth(app);