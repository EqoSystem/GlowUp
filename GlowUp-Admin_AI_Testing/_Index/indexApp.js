// Admin login. Signs in with Firebase Authentication, then opens the dashboard.
import "../_Shared/quietConsole.js";                 // first: no debug output on the live site
import { auth } from "../_Database/firebase-console.js";
import { signInWithEmailAndPassword, onAuthStateChanged, signOut } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-auth.js";
import { DASHBOARD_PAGE, REQUIRE_ADMIN_RECORD, isAdmin } from "../_Shared/adminCommon.js";
import { mountThemeToggle, hydrate } from "../_Shared/adminShell.js";

console.log("GlowUp Admin Login Portal Initialized.");

const NOT_ADMIN = "This account is not an admin account.";

document.addEventListener("DOMContentLoaded", () => {
    hydrate(document);                                           // icons, guide badge, the tile animation on the button
    mountThemeToggle(document.getElementById("loginTools"));     // dark / light, upper right
    const loginForm = document.getElementById("login-form");
    const usernameInput = document.getElementById("username");
    const passwordInput = document.getElementById("password");
    const loginBtn = document.getElementById("loginBtn");
    const message = document.getElementById("loginMessage");
    let signingIn = false;

    // the eye in the password box: shows what was typed, and hides it again. The password is hidden again when the form is sent.
    const eye = document.getElementById("passwordEye");
    const showPassword = (show) => {
        passwordInput.type = show ? "text" : "password";
        eye.classList.toggle("on", show);
        eye.setAttribute("aria-pressed", String(show));
        eye.setAttribute("aria-label", show ? "Hide the password" : "Show the password");
    };
    if (eye) {
        eye.addEventListener("click", () => { showPassword(passwordInput.type === "password"); passwordInput.focus(); });
        loginForm.addEventListener("submit", () => showPassword(false));
    }

    // When the form is sent, the dots in the password box suddenly become a long row, so someone looking over the shoulder cannot count
    // the characters. Only a picture is laid over the box: the real value stays in the input (password managers and sign-in read it).
    // The long row goes away as soon as the box is used again (typing, or the eye).
    const passwordBox = passwordInput.parentElement;
    let lengthCover = null;
    const hideLengthCover = () => { if (lengthCover) { lengthCover.remove(); lengthCover = null; passwordBox.classList.remove("length-hidden"); } };
    const hidePasswordLength = () => {
        hideLengthCover();
        const style = getComputedStyle(passwordInput);
        lengthCover = document.createElement("span");
        lengthCover.className = "password-length-cover";
        lengthCover.setAttribute("aria-hidden", "true");
        lengthCover.textContent = String.fromCharCode(8226).repeat(60);
        lengthCover.style.fontSize = style.fontSize;
        lengthCover.style.fontFamily = style.fontFamily;
        lengthCover.style.paddingLeft = style.paddingLeft;
        passwordBox.append(lengthCover);
        passwordBox.classList.add("length-hidden");
    };
    passwordInput.addEventListener("input", hideLengthCover);
    if (eye) eye.addEventListener("click", hideLengthCover);

    // a message goes away by itself after 1.5 seconds (the "signed out" note passes its own, longer time)
    let messageTimer = 0;
    function say(text, seconds = 1.5) {
        window.clearTimeout(messageTimer);
        message.textContent = text || "";
        message.style.display = text ? "block" : "none";
        if (text) messageTimer = window.setTimeout(() => { if (message.textContent === text) say(""); }, seconds * 1000);
    }

    function setButton(enabled, label) {
        if (enabled) loginBtn.removeAttribute("disabled"); else loginBtn.setAttribute("disabled", "true");
        if (label) loginBtn.textContent = label;
    }

    function validateForm() {
        if (signingIn) return;
        setButton(usernameInput.value.trim() !== "" && passwordInput.value !== "", "Log in");
    }

    usernameInput.addEventListener("input", validateForm);
    passwordInput.addEventListener("input", validateForm);
    validateForm();

    // sent back here by an admin page because the account is not in the admins list
    if (new URLSearchParams(window.location.search).get("denied")) say(NOT_ADMIN);
    if (new URLSearchParams(window.location.search).get("idle")) {
        // the "signed out" note goes by itself after 7 seconds (9 November 2026), unless another message has replaced it meanwhile
        const idleNote = "You were signed out after 30 minutes without activity. Sign in again to continue.";
        say(idleNote, 7);
        // the address loses "?idle=1", so a reload does not show the note again
        try { history.replaceState(null, "", window.location.pathname + window.location.hash); } catch (error) { /* keep the address */ }
    }

    // already signed in (for example the page was reopened): go straight to the dashboard
    onAuthStateChanged(auth, async (user) => {
        if (!user || signingIn) return;
        if (REQUIRE_ADMIN_RECORD && !(await isAdmin(user))) { await signOut(auth); return; }
        window.location.replace(DASHBOARD_PAGE);
    });

    loginForm.addEventListener("submit", async (e) => {
        e.preventDefault();
        if (signingIn) return;

        const email = usernameInput.value.trim();
        const password = passwordInput.value;
        if (email === "" || password === "") { say("Enter your email and your password."); return; }

        hidePasswordLength();
        signingIn = true;
        say("");
        setButton(false, "Signing in...");
        try {
            const userCredential = await signInWithEmailAndPassword(auth, email, password);
            if (REQUIRE_ADMIN_RECORD && !(await isAdmin(userCredential.user))) {
                await signOut(auth);
                throw { code: "glowup/not-admin" };
            }
            console.log("Admin logged in successfully:", userCredential.user.email);
            window.location.href = DASHBOARD_PAGE;
        } catch (error) {
            console.error("Login failed:", error && error.code ? error.code : error);
            say(explain(error));
            signingIn = false;
            validateForm();
        }
    });
});

/** A sign-in error in plain words. */
function explain(error) {
    const code = error && error.code ? String(error.code) : "";
    if (code === "glowup/not-admin") return NOT_ADMIN;
    if (code.includes("invalid-credential") || code.includes("wrong-password") || code.includes("user-not-found"))
        return "The email or the password is not correct.";
    if (code.includes("invalid-email")) return "That does not look like an email address.";
    if (code.includes("user-disabled")) return "This account has been disabled.";
    if (code.includes("too-many-requests")) return "Too many attempts. Wait a few minutes and try again.";
    if (code.includes("network-request-failed")) return "No internet connection. Check it and try again.";
    return "Sign-in failed. " + (error && error.message ? error.message : "Please try again.");
}
