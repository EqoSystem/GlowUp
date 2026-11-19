
import "../_Shared/quietConsole.js";
import { auth } from "../_Database/firebase-console.js";
import { signInWithEmailAndPassword, onAuthStateChanged, signOut, sendPasswordResetEmail } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-auth.js";
import { DASHBOARD_PAGE, REQUIRE_ADMIN_RECORD, isAdmin } from "../_Shared/adminCommon.js";
import { mountThemeToggle, hydrate, dialogFrame } from "../_Shared/adminShell.js";
import { createLockout, mmss, LOCK_MS } from "./loginLockout.js";

console.log("GlowUp Admin Login Portal Initialized.");

const NOT_ADMIN = "This account is not an admin account.";

document.addEventListener("DOMContentLoaded", () => {
    hydrate(document);
    mountThemeToggle(document.getElementById("loginTools"));
    const loginForm = document.getElementById("login-form");
    const usernameInput = document.getElementById("username");
    const passwordInput = document.getElementById("password");
    const loginBtn = document.getElementById("loginBtn");
    const message = document.getElementById("loginMessage");
    let signingIn = false;


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
    // Highlighting the password (drag, double-click, Ctrl+A) shows the real dots and the highlight: the cover would hide them.
    const selectionMade = () => passwordInput.selectionStart !== passwordInput.selectionEnd;
    passwordInput.addEventListener("select", hideLengthCover);
    document.addEventListener("selectionchange", () => { if (lengthCover && document.activeElement === passwordInput && selectionMade()) hideLengthCover(); });


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

    // ---- lock-out: 3 wrong passwords for one e-mail lock it for 5 minutes and recommend "Forgot password?" ----
    const safeStorage = { getItem: (k) => { try { return localStorage.getItem(k); } catch (e) { return null; } }, setItem: (k, v) => { try { localStorage.setItem(k, v); } catch (e) { } } };
    const lockout = createLockout(safeStorage);
    const LOCK_MINUTES = Math.round(LOCK_MS / 60000);
    let lockTimer = 0;
    const forgotLinkEl = document.getElementById("forgotLink");
    const lockText = (ms) => `Too many wrong passwords. Log-in is locked for this email for ${mmss(ms)}. Use Forgot password? to choose a new password.`;
    function showLock(email) {
        window.clearInterval(lockTimer);
        lockTimer = 0;
        const tick = () => {
            const left = lockout.remaining(email);
            if (left <= 0 || usernameInput.value.trim().toLowerCase() !== email.toLowerCase()) {
                window.clearInterval(lockTimer); lockTimer = 0;
                if (forgotLinkEl) forgotLinkEl.classList.remove("recommend");
                if (left <= 0) say("");
                validateForm();
                return false;
            }
            say(lockText(left), Math.ceil(left / 1000) + 1);
            setButton(false, "Locked " + mmss(left));
            return true;
        };
        if (forgotLinkEl) forgotLinkEl.classList.add("recommend");
        if (tick()) lockTimer = window.setInterval(tick, 1000);
    }
    const credentialError = (error) => { const c = error && error.code ? String(error.code) : ""; return c.includes("invalid-credential") || c.includes("wrong-password") || c.includes("invalid-login-credentials"); };

    function validateForm() {
        if (signingIn) return;
        const email = usernameInput.value.trim();
        if (email && lockout.remaining(email) > 0) { if (!lockTimer) showLock(email); return; }
        setButton(email !== "" && passwordInput.value !== "", "Log in");
    }

    // Clearing the email also clears the password, so a typed password never stays behind for the next person.
    usernameInput.addEventListener("input", () => {
        if (usernameInput.value === "" && passwordInput.value !== "") { passwordInput.value = ""; hideLengthCover(); showPassword(false); }
        validateForm();
    });
    passwordInput.addEventListener("input", validateForm);
    validateForm();

    // "Forgot password?": Firebase e-mails a link for choosing a new password. Admin rights are not touched by it (they
    // come from the "admins" list), and the answer never says whether an e-mail belongs to an admin.
    const forgotLink = document.getElementById("forgotLink");
    if (forgotLink) forgotLink.addEventListener("click", () => openResetDialog({
        typed: usernameInput.value.trim(),
        onSent: (email) => { usernameInput.value = email; validateForm(); },
        onClosed: (sent) => (sent ? passwordInput : forgotLink).focus()
    }));


    if (new URLSearchParams(window.location.search).get("denied")) say(NOT_ADMIN);
    if (new URLSearchParams(window.location.search).get("idle")) {

        const idleNote = "You were signed out after 30 minutes without activity. Sign in again to continue.";
        say(idleNote, 2);

        try { history.replaceState(null, "", window.location.pathname + window.location.hash); } catch (error) {                        }
    }


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
        if (lockout.remaining(email) > 0) { showLock(email); return; }

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
            lockout.clear(email);
            console.log("Admin logged in successfully:", userCredential.user.email);
            window.location.href = DASHBOARD_PAGE;
        } catch (error) {
            console.error("Login failed:", error && error.code ? error.code : error);
            signingIn = false;
            if (credentialError(error)) {
                const result = lockout.fail(email);
                if (result.lockedMs > 0) { showLock(email); return; }
                if (result.left === 1) {
                    if (forgotLinkEl) forgotLinkEl.classList.add("recommend");
                    say(`${explain(error)} 1 try left before log-in is locked for ${LOCK_MINUTES} minutes. Forgot your password? Use Forgot password?.`, 8);
                    validateForm();
                    return;
                }
            }
            say(explain(error));
            validateForm();
        }
    });
});


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


const RESET_TIMEOUT_MS = 20000;
const EMAIL_SHAPE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** Sends the reset e-mail; the link's "Continue" leads back to this login page when Firebase allows that address. */
async function sendReset(email) {
    const back = { url: new URL("../_Index/index.html", window.location.href).href };
    try { await sendPasswordResetEmail(auth, email, back); }
    catch (error) {
        const code = error && error.code ? String(error.code) : "";
        if (!code.includes("continue-uri") && !code.includes("unauthorized-domain")) throw error;
        await sendPasswordResetEmail(auth, email);          // the login address is not on Firebase's list: send the plain link
    }
}

function withTimeout(promise, ms) {
    let timer = 0;
    const late = new Promise((_, reject) => { timer = window.setTimeout(() => reject({ code: "glowup/timeout" }), ms); });
    return Promise.race([promise, late]).finally(() => window.clearTimeout(timer));
}

function resetProblem(error) {
    const code = error && error.code ? String(error.code) : "";
    if (code.includes("invalid-email") || code.includes("missing-email")) return "That does not look like an email address.";
    if (code.includes("network-request-failed")) return "No internet connection. Check it and press Send link again.";
    if (code.includes("too-many-requests")) return "Too many requests. Wait a few minutes and try again.";
    if (code === "glowup/timeout") return "This is taking too long. Check the connection and press Send link again.";
    return "The link could not be sent. Press Send link to try again.";
}

// An unknown e-mail gets the same answer as a known one, so the page never tells who has an account.
const looksSent = (error) => { const code = error && error.code ? String(error.code) : ""; return code.includes("user-not-found") || code.includes("invalid-credential"); };

function openResetDialog({ typed, onSent, onClosed }) {
    let sending = false, sent = false, closed = false;
    const close = () => { if (closed) return; closed = true; frame.close(); onClosed(sent); };
    // Escape and a click beside the box close it, except while the link is being sent (Cancel always works)
    const frame = dialogFrame("Reset your password", () => { if (!sending) close(); });
    frame.box.classList.add("reset-dialog");
    const heading = frame.box.querySelector("h3");
    heading.id = "resetTitle";
    frame.box.setAttribute("aria-labelledby", "resetTitle");

    const intro = document.createElement("p");
    intro.className = "modal-intro";
    intro.textContent = "Enter the email of your admin account. We will send it a link to choose a new password.";

    const form = document.createElement("form");
    form.className = "modal-form";
    form.noValidate = true;
    const field = document.createElement("div");
    field.className = "field";
    const label = document.createElement("label");
    label.textContent = "Email";
    const input = document.createElement("input");
    input.type = "email"; input.id = "resetEmail"; input.maxLength = 254; input.autocomplete = "username"; input.placeholder = "Admin email";
    input.value = EMAIL_SHAPE.test(typed || "") ? typed : "";
    label.htmlFor = input.id;
    field.append(label, input);
    const problem = document.createElement("p");
    problem.className = "form-error";
    problem.setAttribute("role", "alert");
    const actions = document.createElement("div");
    actions.className = "modal-actions";
    const cancel = document.createElement("button");
    cancel.type = "button"; cancel.className = "cancel-btn"; cancel.textContent = "Cancel";
    const send = document.createElement("button");
    send.type = "submit"; send.className = "save-btn"; send.textContent = "Send link";
    actions.append(cancel, send);
    form.append(field, problem, actions);
    frame.box.append(intro, form);

    const showProblem = (text) => { problem.textContent = text || ""; problem.style.display = text ? "block" : "none"; };
    input.addEventListener("input", () => showProblem(""));
    cancel.addEventListener("click", close);

    const showSent = (email) => {
        sent = true;
        onSent(email);
        intro.remove(); form.remove();
        const note = document.createElement("p");
        note.className = "reset-sent";
        note.setAttribute("role", "status");
        const who = document.createElement("strong");
        who.textContent = email;
        note.append("If ", who, " belongs to an admin account, a link to choose a new password is on its way. Check the inbox and the spam folder, then log in with the new password.");
        const done = document.createElement("div");
        done.className = "modal-actions reset-actions";
        const back = document.createElement("button");
        back.type = "button"; back.className = "save-btn"; back.textContent = "Back to log in";
        back.addEventListener("click", close);
        done.append(back);
        frame.box.append(note, done);
        back.focus();
    };

    form.addEventListener("submit", async (event) => {
        event.preventDefault();
        if (sending) return;
        const email = input.value.trim();
        if (!EMAIL_SHAPE.test(email)) { showProblem(email === "" ? "Enter the email of your admin account." : "That does not look like an email address."); input.focus(); return; }
        sending = true;
        showProblem("");
        send.disabled = true; send.textContent = "Sending..."; input.readOnly = true;
        try {
            await withTimeout(sendReset(email), RESET_TIMEOUT_MS);
            if (!closed) showSent(email);
        } catch (error) {
            if (closed) return;
            if (looksSent(error)) { showSent(email); return; }
            console.error("Password reset failed:", error && error.code ? error.code : error);
            showProblem(resetProblem(error));
        } finally {
            sending = false;
            if (!sent && !closed) { send.disabled = false; send.textContent = "Send link"; input.readOnly = false; }
        }
    });

    (input.value ? send : input).focus();
}
