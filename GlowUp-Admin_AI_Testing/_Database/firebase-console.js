import { initializeApp } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-app.js";
import { getFirestore } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
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

export const db = getFirestore(app);
export const auth = getAuth(app);