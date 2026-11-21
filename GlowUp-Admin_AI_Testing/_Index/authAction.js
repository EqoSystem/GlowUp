// The links Firebase e-mails, handled on the log-in page in its own panels instead of Firebase's plain page.
// For this, the Firebase console's "Action URL" (Authentication > Templates > any template > Customize action URL) must be
// https://glowupapp.xyz/_Index/index.html . Until then Firebase keeps opening its own page; nothing here breaks.
//
//   ?mode=resetPassword&oobCode=...   1. "Email Verified!"  ->  2. "Set a New Password" (the sign-up rules, live)
//                                     ->  3. "Password Set Successfully!"  ->  Continue to Login
//   ?mode=verifyEmail&oobCode=...     "Email confirmed" (the sign-up request then waits for an admin)  ->  Back to log in
//
// The code from the link is checked first (verifyPasswordResetCode); a used or expired link says so and offers a new one.
// The new password is sent once, with the same code (confirmPasswordReset); Firebase then makes the code unusable.

import { auth } from "../_Database/firebase-console.js";
import { verifyPasswordResetCode, confirmPasswordReset, applyActionCode } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-auth.js";
import { icon } from "../_Shared/adminShell.js";
import { PASSWORD_RULES, passwordChecks, passwordOk } from "./signupRules.js";

const make = (tag, className, text) => { const n = document.createElement(tag); if (className) n.className = className; if (text !== undefined) n.textContent = text; return n; };

/** { mode, code } when the page was opened from a Firebase e-mail link, else null. */
export function actionFromUrl(search = window.location.search) {
    const q = new URLSearchParams(search);
    const mode = q.get("mode"), code = q.get("oobCode");
    return (mode === "resetPassword" || mode === "verifyEmail") && code ? { mode, code } : null;
}

/**
 * Shows the panels when the page came from an e-mail link. Returns true when it took over the page.
 * onBackToLogin(email) shows the log-in card again (with the e-mail filled in when there is one).
 * openResetDialog(email) asks for a new link when this one is used or expired.
 */
export function mountAuthAction({ onBackToLogin, openResetDialog }) {
    const action = actionFromUrl();
    if (!action) return false;
    const login = document.getElementById("loginView");
    const card = make("section", "login-card action-card");
    card.id = "actionView";
    card.setAttribute("aria-live", "polite");
    login.hidden = true;
    login.after(card);
    // the code must not stay in the address bar (or in the history) once read
    try { history.replaceState(null, "", window.location.pathname); } catch (error) { }

    const badge = (name, tone) => { const b = make("span", "action-badge" + (tone ? " " + tone : "")); b.setAttribute("aria-hidden", "true"); b.innerHTML = icon(name); return b; };
    const back = (email) => { card.remove(); login.hidden = false; onBackToLogin(email || ""); };
    const backLink = (email) => { const a = make("button", "text-link action-back"); a.type = "button"; a.innerHTML = icon("left"); a.append(" Back to Login"); a.addEventListener("click", () => back(email)); return a; };
    const show = (...nodes) => { card.replaceChildren(...nodes); const h = card.querySelector("h1"); if (h) { h.tabIndex = -1; h.focus({ preventScroll: true }); } };

    const checking = () => show(badge("lock"), make("h1", "", "Checking your link..."), make("p", "action-text", "One moment."));
    const failed = (text, email) => {
        const again = make("button", "login-btn", "Send a new link");
        again.type = "button";
        again.addEventListener("click", () => { back(email); openResetDialog(email); });
        show(badge("alert", "warn"), make("h1", "", "This link does not work any more"), make("p", "action-text", text), again, backLink(email));
    };

    if (action.mode === "verifyEmail") {
        checking();
        applyActionCode(auth, action.code).then(() => {
            show(badge("check", "ok"), make("h1", "", "Email confirmed"), make("p", "action-text", "Your email address is confirmed. If you asked for admin access, an admin can now approve it; you can log in once they do."), backLink(""));
        }).catch(() => failed("The link was already used or has expired. Log in or sign up again to get a new one.", ""));
        return true;
    }

    // ---- reset password ----
    let email = "";
    const verified = () => {
        const next = make("button", "login-btn", "Set New Password");
        next.type = "button";
        next.addEventListener("click", setNew);
        show(badge("check", "ok"), make("h1", "", "Email Verified!"),
            make("p", "action-text", "Your email address has been verified. You can now set a new password to get back into your account."), next, backLink(email));
    };
    const setNew = () => {
        const form = make("form", "action-form");
        form.noValidate = true;
        const who = make("p", "action-email"); who.append("For ", make("strong", "", email));
        const field = (labelText, id, auto) => {
            const wrap = make("div", "field");
            const label = make("label", "", labelText); label.htmlFor = id;
            const box = make("div", "password-box");
            const input = document.createElement("input"); input.type = "password"; input.id = id; input.autocomplete = auto; input.maxLength = 128;
            const eye = make("button", "password-eye"); eye.type = "button"; eye.setAttribute("aria-label", "Show the password"); eye.setAttribute("aria-pressed", "false"); eye.innerHTML = icon("eye");
            eye.addEventListener("click", () => { const on = input.type === "password"; input.type = on ? "text" : "password"; eye.innerHTML = icon(on ? "eyeOff" : "eye"); eye.setAttribute("aria-pressed", String(on)); eye.setAttribute("aria-label", on ? "Hide the password" : "Show the password"); input.focus(); });
            box.append(input, eye);
            wrap.append(label, box);
            return { wrap, input };
        };
        const pw = field("New Password", "newPassword", "new-password");
        pw.input.placeholder = "Enter new password";
        const again = field("Confirm Password", "confirmPassword", "new-password");
        again.input.placeholder = "Confirm new password";
        const rules = make("ul", "pw-rules action-rules");
        rules.setAttribute("aria-label", "Password requirements");
        const TICK = '<svg class="ic" viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12.5l4.5 4.5L19 7.5"/></svg>';
        PASSWORD_RULES.forEach((rule) => { const li = make("li", "pw-rule"); li.dataset.rule = rule.key; li.innerHTML = TICK; li.append(make("span", "", rule.label)); rules.append(li); });
        const match = make("p", "pw-rule pw-match"); match.innerHTML = TICK; match.append(make("span", "", "Passwords match")); match.hidden = true;
        const problem = make("p", "form-error"); problem.setAttribute("role", "alert"); problem.style.display = "none";
        const save = make("button", "login-btn", "Save New Password"); save.type = "submit"; save.disabled = true;
        form.append(who, pw.wrap, again.wrap, rules, match, problem, save);
        let sending = false;
        const paint = () => {
            const met = passwordChecks(pw.input.value);
            rules.querySelectorAll(".pw-rule").forEach((li) => { const on = !!met[li.dataset.rule]; li.classList.toggle("met", on); li.setAttribute("aria-label", li.textContent + (on ? ": done" : ": not yet")); });
            const both = again.input.value !== "", same = again.input.value === pw.input.value;
            match.hidden = !both; match.classList.toggle("met", both && same); match.lastElementChild.textContent = same ? "Passwords match" : "Passwords do not match yet";
            save.disabled = sending || !passwordOk(pw.input.value) || !same || !both;
            problem.style.display = "none";
        };
        form.addEventListener("input", paint);
        form.addEventListener("submit", async (event) => {
            event.preventDefault();
            if (save.disabled) return;
            sending = true; paint(); save.textContent = "Saving...";
            try {
                await confirmPasswordReset(auth, action.code, pw.input.value);
                done();
            } catch (error) {
                const code = String((error && error.code) || "");
                if (/expired|invalid-action-code/.test(code)) { failed("The link expired while you were typing. Ask for a new one; it takes a minute.", email); return; }
                sending = false; save.textContent = "Save New Password"; paint();
                problem.textContent = /weak-password/.test(code) ? "Firebase says this password is too weak. Choose a longer one." : /network/.test(code) ? "No connection. Check the internet and press Save again." : "The password could not be saved. Press Save to try again.";
                problem.style.display = "block";
            }
        });
        show(badge("lock"), make("h1", "", "Set a New Password"), make("p", "action-text", "Your email has been verified. Enter your new password below."), form, backLink(email));
        pw.input.focus({ preventScroll: true });
    };
    const done = () => {
        const go = make("button", "login-btn", "Continue to Login");
        go.type = "button";
        go.addEventListener("click", () => back(email));
        const note = make("p", "action-note", "Keep your password safe and do not share it with anyone.");
        show(badge("check", "ok"), make("h1", "", "Password Set Successfully!"), make("p", "action-text", "Your new password has been saved. You can now log in to your account."), note, go);
    };

    checking();
    verifyPasswordResetCode(auth, action.code).then((address) => { email = address || ""; verified(); })
        .catch(() => failed("This reset link was already used or has expired. Ask for a new one; it takes a minute.", ""));
    return true;
}
