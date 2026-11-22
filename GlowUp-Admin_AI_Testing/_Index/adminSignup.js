// Admin sign-up on the login page: "Sign Up" -> "Create Admin Account" -> "Check Your Email".
//
// Signing up NEVER makes anyone an admin. It makes (or reuses) a Firebase account, sends Firebase's e-mail confirmation
// link and writes a pending request to adminRequests/{uid}. An existing admin approves it in Quick settings > Admin requests
// (see _Shared/adminRequests.js); firestore.rules let an admin add an admin only from a pending, e-mail-confirmed request.
//
// The three cards are views of this one page. #signup and #check-email are real history entries, so the browser's Back
// returns to the log-in card; the check card REPLACES the form's entry, so Back never reopens a sent form.
// The check card is live: it notices the e-mail confirmation by itself (Firebase has no push for that, so it asks every
// few seconds while the card is open and when the tab comes back), and it follows the request document in real time,
// so an approval or a decline shows at once.

import { auth, db } from "../_Database/firebase-console.js";
import { createUserWithEmailAndPassword, signInWithEmailAndPassword, sendEmailVerification, updateProfile, signOut, reload } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-auth.js";
import { doc, getDoc, setDoc, updateDoc, deleteDoc, onSnapshot, serverTimestamp } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { askConfirm } from "../_Shared/adminShell.js";
import { PASSWORD_RULES, passwordChecks, cleanName, cleanPart, fullName, formProblem, signupProblem, requestNote } from "./signupRules.js";
import { attachCounters } from "../_Shared/charLimit.js";

const STEP_TIMEOUT_MS = 20000;
const CHECK_EVERY_MS = 4000;
const CHECK_FOR_MS = 30 * 60 * 1000;
const RESEND_WAIT_S = 60;
const VIEWS = { login: "loginView", signup: "signupView", check: "checkView" };
const HASH = { signup: "#signup", check: "#check-email" };

let busy = false;          // a request is being sent
let holding = null;        // the account kept signed in while the check card is open (for "Send the link again")
/** True while the sign-up owns the Firebase session: the login page's own auth listener must leave it alone. */
export const signupBusy = () => busy || !!holding;

const $ = (id) => document.getElementById(id);
const codeOf = (error) => (error && error.code ? String(error.code) : "");
const credentialError = (error) => { const c = codeOf(error); return c.includes("invalid-credential") || c.includes("wrong-password") || c.includes("invalid-login-credentials"); };
const calm = () => window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;

function withTimeout(promise, ms = STEP_TIMEOUT_MS) {
    let timer = 0;
    const late = new Promise((_, reject) => { timer = window.setTimeout(() => reject({ code: "glowup/timeout" }), ms); });
    return Promise.race([promise, late]).finally(() => window.clearTimeout(timer));
}

/** Firebase's confirmation link; its "Continue" leads back to the log-in page when Firebase allows that address. */
async function sendConfirmation(user) {
    const back = { url: new URL("../_Index/index.html", window.location.href).href };
    try { await sendEmailVerification(user, back); }
    catch (error) {
        const c = codeOf(error);
        if (!c.includes("continue-uri") && !c.includes("unauthorized-domain")) throw error;
        await sendEmailVerification(user);
    }
}

/**
 * Makes the account (or signs in to the one that already has this e-mail), then writes the pending request and sends
 * the confirmation link. Returns { user, verified }. Throws Firebase errors or { code: "glowup/..." }.
 */
async function sendRequest({ name, first, middle, last, email, password }, lockout) {
    let user = null, made = false;
    try {
        user = (await withTimeout(createUserWithEmailAndPassword(auth, email, password))).user;
        made = true;
    } catch (error) {
        if (!codeOf(error).includes("email-already-in-use")) throw error;
        // the e-mail already has an account (an app user, or an earlier try): its own password lets it ask too
        if (lockout && lockout.remaining(email) > 0) throw { code: "glowup/locked" };
        try { user = (await withTimeout(signInWithEmailAndPassword(auth, email, password))).user; }
        catch (signInError) {
            if (credentialError(signInError)) { if (lockout) lockout.fail(email); throw { code: "glowup/existing-account" }; }
            throw signInError;
        }
        if (lockout) lockout.clear(email);
    }
    if (made) { try { await withTimeout(updateProfile(user, { displayName: name })); } catch (error) { /* the request carries the name anyway */ } }

    const adminSnap = await withTimeout(getDoc(doc(db, "admins", user.uid))).catch(() => null);
    if (adminSnap && adminSnap.exists()) throw { code: "glowup/already-admin" };

    const ref = doc(db, "adminRequests", user.uid);
    const earlier = await withTimeout(getDoc(ref));
    if (earlier.exists() && earlier.data().status !== "pending") await withTimeout(deleteDoc(ref));      // an old, closed request: ask afresh
    if (!earlier.exists() || earlier.data().status !== "pending") {
        await withTimeout(setDoc(ref, { name, firstName: first, middleName: middle, lastName: last, email: user.email, status: "pending", emailVerified: !!user.emailVerified, createdAt: serverTimestamp() }));
    }
    if (!user.emailVerified) await withTimeout(sendConfirmation(user));
    return { user, verified: !!user.emailVerified };
}

export function mountSignup({ lockout, onBackToLogin }) {
    const views = Object.fromEntries(Object.entries(VIEWS).map(([key, id]) => [key, $(id)]));
    if (!views.signup || !views.check || !views.login) return;
    const form = $("signupForm");
    const fields = { first: $("signupFirst"), middle: $("signupMiddle"), last: $("signupLast"), email: $("signupEmail"), password: $("signupPassword"), confirm: $("signupConfirm"), agree: $("signupAgree") };
    const sendBtn = $("signupBtn");
    const message = $("signupMessage");
    const ruleList = $("pwRules");
    const matchLine = $("pwMatch");
    attachCounters(["signupFirst", "signupMiddle", "signupLast", "signupEmail", "signupPassword", "signupConfirm"]);
    let current = "login";
    let attempt = 0;

    // ---- password checklist: the app's five rules, ticked while typing ----
    ruleList.replaceChildren(...PASSWORD_RULES.map((rule) => {
        const li = document.createElement("li");
        li.className = "pw-rule";
        li.dataset.rule = rule.key;
        li.innerHTML = '<svg class="ic" viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12.5l4.5 4.5L19 7.5"/></svg>';
        const text = document.createElement("span");
        text.textContent = rule.label;
        li.append(text);
        return li;
    }));
    const paintRules = () => {
        const met = passwordChecks(fields.password.value);
        ruleList.querySelectorAll(".pw-rule").forEach((li) => {
            const on = !!met[li.dataset.rule];
            li.classList.toggle("met", on);
            li.setAttribute("aria-label", li.textContent + (on ? ": done" : ": not yet"));
        });
        const both = fields.confirm.value !== "";
        const same = fields.confirm.value === fields.password.value;
        matchLine.hidden = !both;
        matchLine.classList.toggle("met", both && same);
        matchLine.lastElementChild.textContent = same ? "Passwords match" : "Passwords do not match yet";
    };

    // ---- eyes: show / hide each password box ----
    document.querySelectorAll("#signupView .password-eye").forEach((eye) => {
        const input = $(eye.dataset.for);
        const set = (show) => {
            input.type = show ? "text" : "password";
            eye.classList.toggle("on", show);
            eye.setAttribute("aria-pressed", String(show));
            eye.setAttribute("aria-label", show ? "Hide the password" : "Show the password");
        };
        eye.addEventListener("click", () => { set(input.type === "password"); input.focus(); });
        eye._hide = () => set(false);
    });
    const hideEyes = () => document.querySelectorAll("#signupView .password-eye").forEach((eye) => eye._hide && eye._hide());

    // ---- message and button ----
    let messageTimer = 0;
    const say = (text, seconds = 8) => {
        window.clearTimeout(messageTimer);
        message.textContent = text || "";
        message.style.display = text ? "block" : "none";
        if (text) messageTimer = window.setTimeout(() => { if (message.textContent === text) say(""); }, seconds * 1000);
    };
    const filled = () => fields.first.value.trim() !== "" && fields.last.value.trim() !== "" && fields.email.value.trim() !== "" && fields.password.value !== "" && fields.confirm.value !== "" && fields.agree.checked;
    const paintButton = () => {
        sendBtn.disabled = busy || !filled();
        sendBtn.textContent = busy ? "Sending request..." : "Request Admin Access";
    };
    const dirty = () => current === "signup" && (fields.first.value.trim() !== "" || fields.middle.value.trim() !== "" || fields.last.value.trim() !== "" || fields.email.value.trim() !== "" || fields.password.value !== "" || fields.confirm.value !== "");
    form.addEventListener("input", () => { paintRules(); paintButton(); if (message.style.display === "block" && !busy) say(""); });
    form.addEventListener("change", paintButton);
    paintRules(); paintButton();

    const clearForm = () => {
        form.reset();
        hideEyes();
        say("");
        Object.values(fields).forEach((f) => f.removeAttribute("aria-invalid"));
        paintRules(); paintButton();
    };

    // ---- switching cards ----
    function show(view, { focus = true, back = false } = {}) {
        if (current === view) return;
        const leaving = current;
        current = view;
        Object.entries(views).forEach(([key, el]) => { el.hidden = key !== view; });
        const card = views[view];
        card.classList.remove("card-slide-in", "card-slide-back");
        if (!calm()) { void card.offsetWidth; card.classList.add(back ? "card-slide-back" : "card-slide-in"); }
        if (leaving === "check" && view !== "check") stopWatching(true);
        if (focus) {
            const target = view === "signup" ? fields.first : view === "check" ? $("checkTitle") : $("username");
            window.requestAnimationFrame(() => target && target.focus({ preventScroll: true }));
        }
        document.title = view === "signup" ? "Create Admin Account - GlowUp" : view === "check" ? "Check Your Email - GlowUp" : "GlowUp Admin Portal";
    }
    const viewOfHash = () => (location.hash === HASH.signup ? "signup" : location.hash === HASH.check && holding ? "check" : "login");

    async function leaveSignup() {
        if (!dirty()) return true;
        return askConfirm({ title: "Discard changes?", message: "What you typed in the sign-up form will be lost.", cancelLabel: "Keep editing", confirmLabel: "Discard", danger: true });
    }
    async function goLogin() {
        if (current === "signup" && !(await leaveSignup())) return;
        const email = current === "check" ? $("checkEmail").textContent : "";
        clearForm();
        attempt++;                                                    // a request still on its way is let go
        history.replaceState(null, "", location.pathname + location.search);
        show("login", { back: true, focus: !email });             // with an e-mail, onBackToLogin puts focus on the password
        if (onBackToLogin) onBackToLogin(email);
    }

    $("signupLink").addEventListener("click", () => {
        history.pushState({ view: "signup" }, "", HASH.signup);
        show("signup");
    });
    $("signupBack").addEventListener("click", () => { if (history.state && history.state.view === "signup") history.back(); else goLogin(); });
    $("signupToLogin").addEventListener("click", () => { if (history.state && history.state.view === "signup") history.back(); else goLogin(); });
    $("checkToLogin").addEventListener("click", goLogin);
    $("checkLogin").addEventListener("click", goLogin);

    // browser Back / Forward between the cards; a filled form asks first and stays when "Keep editing" is chosen
    window.addEventListener("popstate", async () => {
        const next = viewOfHash();
        if (next === current) return;
        if (current === "signup" && dirty()) {
            history.pushState({ view: "signup" }, "", HASH.signup);
            if (!(await leaveSignup())) return;
            clearForm();
            history.back();
            return;
        }
        if (current === "signup") { clearForm(); attempt++; }
        if (next === "login" && current === "check") { await goLogin(); return; }
        show(next, { back: next === "login" });
    });
    window.addEventListener("beforeunload", (event) => {
        if (!dirty() || window.__glowupLeaving) return;
        event.preventDefault();
        event.returnValue = "";
    });
    window.addEventListener("keydown", (event) => {
        if (event.key !== "Escape" || event.defaultPrevented || current !== "signup") return;
        if (document.querySelector(".modal-overlay, dialog[open]")) return;
        $("signupBack").click();
    });

    // ---- sending ----
    form.addEventListener("submit", async (event) => {
        event.preventDefault();
        if (busy) return;
        const values = { first: fields.first.value, middle: fields.middle.value, last: fields.last.value, email: fields.email.value.trim(), password: fields.password.value, confirm: fields.confirm.value, agreed: fields.agree.checked };
        const problem = formProblem(values);
        Object.values(fields).forEach((f) => f.removeAttribute("aria-invalid"));
        if (problem) {
            say(problem.text);
            const field = fields[problem.field];
            field.setAttribute("aria-invalid", "true");
            field.focus();
            return;
        }
        hideEyes();
        busy = true;
        const mine = ++attempt;
        say("");
        paintButton();
        try {
            const result = await sendRequest({ name: fullName(values), first: cleanPart(values.first), middle: cleanPart(values.middle), last: cleanPart(values.last), email: values.email, password: values.password }, lockout);
            if (mine !== attempt) { await signOut(auth).catch(() => { }); return; }   // the person left the form meanwhile
            holding = result.user;
            clearForm();
            history.replaceState({ view: "check" }, "", HASH.check);
            openCheck(result.user, result.verified);
        } catch (error) {
            console.warn("Admin sign-up failed:", codeOf(error) || error);
            await signOut(auth).catch(() => { });
            if (mine !== attempt) return;
            say(signupProblem(error), 12);
            if (codeOf(error) === "glowup/existing-account") { fields.password.value = ""; fields.confirm.value = ""; paintRules(); fields.password.focus(); }
        } finally {
            busy = false;
            paintButton();
        }
    });

    // ---- the live "Check Your Email" card ----
    let pollTimer = 0, pollUntil = 0, stopRequest = null, resendTimer = 0;
    const checkState = (state) => { views.check.dataset.state = state; };
    const checkText = $("checkText"), checkNote = $("checkNoteText"), resendBtn = $("resendBtn"), checkLogin = $("checkLogin");

    function paintCheck(state) {
        checkState(state);
        const email = $("checkEmail").textContent;
        $("checkLead").hidden = state !== "sent";
        resendBtn.hidden = state !== "sent";
        checkLogin.hidden = state !== "approved";
        $("checkToLogin").hidden = state === "approved";
        if (state === "sent") {
            $("checkTitle").textContent = "Check Your Email";
            checkText.textContent = "Open the link in that email to confirm your address. This card notices it by itself.";
            checkNote.textContent = "After that, an admin reviews your request. You can log in once it is approved.";
        } else if (state === "confirmed") {
            $("checkTitle").textContent = "Email Confirmed";
            checkText.textContent = `${email} is confirmed. Your request is now with the admins.`;
            checkNote.textContent = "You can log in once an admin approves it. Keep this page open to see the answer here.";
        } else if (state === "approved") {
            $("checkTitle").textContent = "Request Approved";
            checkText.textContent = "An admin approved your request. You can log in now.";
            checkNote.textContent = "Use the email and password you just chose.";
        } else if (state === "declined") {
            $("checkTitle").textContent = "Request Declined";
            checkText.textContent = "An admin declined this request, so this account has no admin access.";
            checkNote.textContent = "If you think this is a mistake, write to GlowUpAdminSupport@gmail.com.";
        } else if (state === "stale") {
            checkText.textContent = "Still waiting for the confirmation. Open the link, then press Check again.";
        }
    }

    async function checkConfirmed() {
        if (!holding || views.check.dataset.state !== "sent") return;
        try {
            await reload(holding);
            if (!holding || !holding.emailVerified) return;
            await holding.getIdToken(true);                                       // the rules read email_verified from a fresh token
            await updateDoc(doc(db, "adminRequests", holding.uid), { emailVerified: true });
            paintCheck("confirmed");
            stopPolling();
        } catch (error) { console.warn("Confirmation check failed:", codeOf(error) || error); }
    }
    const onReturn = () => { if (document.visibilityState === "visible") checkConfirmed(); };
    function stopPolling() {
        window.clearInterval(pollTimer); pollTimer = 0;
        document.removeEventListener("visibilitychange", onReturn);
        window.removeEventListener("focus", onReturn);
    }
    function startPolling() {
        stopPolling();
        pollUntil = Date.now() + CHECK_FOR_MS;
        pollTimer = window.setInterval(() => {
            if (Date.now() > pollUntil) { stopPolling(); paintCheck("stale"); resendBtn.hidden = false; return; }
            checkConfirmed();
        }, CHECK_EVERY_MS);
        document.addEventListener("visibilitychange", onReturn);
        window.addEventListener("focus", onReturn);
    }
    function stopWatching(signOutToo) {
        stopPolling();
        if (stopRequest) { stopRequest(); stopRequest = null; }
        window.clearInterval(resendTimer); resendTimer = 0;
        if (signOutToo && holding) { holding = null; signOut(auth).catch(() => { }); }
    }

    function resendCooldown() {
        let left = RESEND_WAIT_S;
        resendBtn.disabled = true;
        const tick = () => {
            if (left <= 0) { window.clearInterval(resendTimer); resendTimer = 0; resendBtn.disabled = false; resendBtn.textContent = "Send the link again"; return; }
            resendBtn.textContent = `Send again in ${left} s`;
            left--;
        };
        window.clearInterval(resendTimer);
        tick();
        resendTimer = window.setInterval(tick, 1000);
    }
    resendBtn.addEventListener("click", async () => {
        if (!holding || resendBtn.disabled) return;
        resendBtn.disabled = true; resendBtn.textContent = "Sending...";
        try {
            await withTimeout(sendConfirmation(holding));
            offerCode();
            checkNote.textContent = "A new link is on its way. Check the inbox and the spam folder.";
            if (!pollTimer) startPolling();
            if (views.check.dataset.state === "stale") paintCheck("sent");
            resendCooldown();
        } catch (error) {
            checkNote.textContent = codeOf(error).includes("too-many-requests") ? "Too many links were sent. Wait a few minutes, then try again." : "The link could not be sent. Check the connection and try again.";
            resendBtn.disabled = false; resendBtn.textContent = "Send the link again";
        }
    });

    // ---- the 6-digit code (api/email-otp): offered next to the link when the server is set up for it; otherwise nothing changes ----
    const codeBlock = $("codeBlock"), codeInput = $("codeInput"), codeBtn = $("codeBtn"), codeMessage = $("codeMessage");
    async function otp(action, extra = {}) {
        if (!holding) return { status: 0 };
        try {
            const idToken = await holding.getIdToken();
            const r = await withTimeout(fetch("/api/email-otp", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ action, idToken, ...extra }) }), 15000);
            let body = {}; try { body = await r.json(); } catch (error) { }
            return { status: r.status, body };
        } catch (error) { return { status: 0 }; }
    }
    async function offerCode() {
        codeBlock.hidden = true; codeMessage.textContent = "";
        const r = await otp("send");
        if (r.status === 200 && r.body && r.body.ok) { codeBlock.hidden = false; codeInput.value = ""; }
        else if (r.status === 429) { codeBlock.hidden = false; codeMessage.textContent = "A code was sent a moment ago. Check the e-mail, or wait a little before asking again."; }
        // 503 (not set up), an error or offline: the link above is the way
    }
    codeInput.addEventListener("input", () => { codeInput.value = codeInput.value.replace(/\D/g, "").slice(0, 6); if (codeInput.value.length === 6) codeBtn.click(); });
    codeBtn.addEventListener("click", async () => {
        if (codeBtn.disabled) return;
        const code = codeInput.value.trim();
        if (!/^\d{6}$/.test(code)) { codeMessage.textContent = "Type the 6 digits."; return; }
        codeBtn.disabled = true; codeMessage.textContent = "Checking...";
        const r = await otp("verify", { code });
        codeBtn.disabled = false;
        if (r.status === 200 && r.body && r.body.ok) { codeMessage.textContent = ""; codeBlock.hidden = true; await checkConfirmed(); }
        else if (r.status === 200) codeMessage.textContent = "That code is not right. Check the digits, or ask for a new one.";
        else if (r.status === 429) codeMessage.textContent = "Too many wrong codes. Wait about 15 minutes, or use the link.";
        else codeMessage.textContent = "The code could not be checked. Use the link in the e-mail instead.";
    });

    function openCheck(user, verified) {
        $("checkEmail").textContent = user.email || "";
        paintCheck(verified ? "confirmed" : "sent");
        show("check");
        if (!verified) { startPolling(); resendCooldown(); offerCode(); }
        // the request in real time: approval or decline shows at once
        let seen = false;
        stopRequest = onSnapshot(doc(db, "adminRequests", user.uid), (snap) => {
            if (!snap.exists()) { if (seen) { paintCheck("declined"); stopPolling(); } return; }
            seen = true;
            const data = snap.data();
            if (data.status === "approved") { paintCheck("approved"); stopPolling(); }
            else if (data.emailVerified && views.check.dataset.state === "sent") { paintCheck("confirmed"); stopPolling(); }
        }, () => { /* no live updates (offline or refused): the polling and the log-in page still tell */ });
    }

    // a reload on #signup opens the form again; #check-email without its session falls back to the log-in card
    if (location.hash === HASH.signup) { history.replaceState({ view: "signup" }, "", HASH.signup); show("signup", { focus: false }); }
    else if (location.hash) history.replaceState(null, "", location.pathname + location.search);
}

/**
 * Log-in by an account that is not an admin (yet): what its request says. Marks the request confirmed when the e-mail
 * now is, so the admins see it at once. "" when there is no request.
 */
export async function noteForRequest(user) {
    try {
        const ref = doc(db, "adminRequests", user.uid);
        const snap = await withTimeout(getDoc(ref));
        if (!snap.exists() || snap.data().status !== "pending") return "";
        try { await reload(user); } catch (error) { }
        if (user.emailVerified && !snap.data().emailVerified) {
            try { await user.getIdToken(true); await updateDoc(ref, { emailVerified: true }); } catch (error) { console.warn("Could not mark the request confirmed:", codeOf(error) || error); }
        }
        return requestNote(snap.data(), !!user.emailVerified, user.email || "");
    } catch (error) { return ""; }
}
