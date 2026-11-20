// The admin's own profile (22 November 2026), opened from the profile menu in the header (headerAccount.js).
// Edit Profile: the display name (Firebase Auth profile) and the password (the current password is asked again first:
// Firebase refuses a password change on an old sign-in, and it proves the person at the keyboard owns the account).
// The password rules are the sign-up rules (signupRules.js), checked live while typing.

import { auth } from "../_Database/firebase-console.js";
import { updateProfile, updatePassword, reauthenticateWithCredential, EmailAuthProvider } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-auth.js";
import { dialogFrame, askConfirm, toast } from "./adminShell.js";
import { PASSWORD_RULES, passwordChecks, passwordOk, cleanName } from "../_Index/signupRules.js";
import { attachCounters } from "./charLimit.js";

const make = (tag, className, text) => { const n = document.createElement(tag); if (className) n.className = className; if (text !== undefined) n.textContent = text; return n; };

/** "Nov 21, 2026 · 6:10 AM" for the profile menu, "" when Firebase gave no time. */
export function lastLoginText(user) {
    const raw = user && user.metadata && user.metadata.lastSignInTime;
    const at = raw ? new Date(raw) : null;
    if (!at || Number.isNaN(at.getTime())) return "";
    const day = at.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
    const time = at.toLocaleTimeString("en-US", { hour: "numeric", minute: "2-digit" });
    return `${day} · ${time}`;
}

/** Plain words for what Firebase answered. */
function reason(error) {
    const code = String((error && error.code) || "");
    if (/wrong-password|invalid-credential|invalid-login/.test(code)) return { field: "current", text: "The current password is not right." };
    if (/too-many-requests/.test(code)) return { field: "current", text: "Too many tries. Wait a few minutes, then try again." };
    if (/weak-password/.test(code)) return { field: "next", text: "Firebase says this password is too weak. Choose a longer one." };
    if (/network/.test(code)) return { field: "", text: "No connection. Check the internet and try again." };
    if (/requires-recent-login/.test(code)) return { field: "current", text: "Enter your current password again to confirm it is you." };
    return { field: "", text: "The changes could not be saved. Try again in a minute." };
}

export function openEditProfile() {
    const user = auth.currentUser;
    if (!user) return;
    let sending = false;
    const startName = user.displayName || "";
    const field = (labelText, input, id, hint) => {
        const wrap = make("div", "field");
        const label = make("label", "", labelText);
        label.htmlFor = id; input.id = id;
        wrap.append(label, input);
        if (hint) wrap.append(hint);
        return wrap;
    };
    const box = (type, auto, max) => { const i = document.createElement("input"); i.type = type; i.autocomplete = auto; if (max) i.maxLength = max; return i; };
    const name = box("text", "name", 60); name.value = startName; name.placeholder = "Your name";
    const email = box("email", "email", 254); email.value = user.email || ""; email.readOnly = true;
    const current = box("password", "current-password", 128); current.placeholder = "Needed only to change the password";
    const next = box("password", "new-password", 128); next.placeholder = "Leave empty to keep it";
    const confirm = box("password", "new-password", 128); confirm.placeholder = "Type the new password again";
    const rules = make("ul", "pw-rules ep-rules");
    PASSWORD_RULES.forEach((rule) => { const li = make("li", "pw-rule", rule.label); li.dataset.rule = rule.key; rules.append(li); });
    const problem = make("p", "form-error"); problem.setAttribute("role", "alert"); problem.style.display = "none";

    const dirty = () => cleanName(name.value) !== startName || current.value !== "" || next.value !== "" || confirm.value !== "";
    const leave = async () => {
        if (sending) return;
        if (dirty() && !(await askConfirm({ title: "Discard changes?", message: "What you changed will be lost.", cancelLabel: "Keep editing", confirmLabel: "Discard", danger: true }))) return;
        frame.close();
    };
    const frame = dialogFrame("Edit Profile", leave);
    frame.box.classList.add("ep-dialog");
    const intro = make("p", "modal-intro", "Change the name other admins see, or your password. Your e-mail is your sign-in and cannot be changed here.");
    const form = document.createElement("form");
    form.className = "ep-form"; form.noValidate = true;
    const pwTitle = make("h4", "ep-section", "Change password");
    const cancel = make("button", "cancel-btn", "Cancel"); cancel.type = "button";
    const save = make("button", "save-btn", "Save changes"); save.type = "submit";
    const bar = make("div", "modal-actions"); bar.append(cancel, save);
    form.append(field("Display name", name, "epName"), field("E-mail", email, "epEmail"), pwTitle,
        field("Current password", current, "epCurrent"), field("New password", next, "epNext", rules), field("Confirm new password", confirm, "epConfirm"), problem, bar);
    frame.box.append(intro, form);
    attachCounters(["epName", "epEmail", "epCurrent", "epNext", "epConfirm"]);
    name.focus();

    const show = (text, input) => {
        problem.textContent = text || ""; problem.style.display = text ? "block" : "none";
        [name, current, next, confirm].forEach((i) => i.removeAttribute("aria-invalid"));
        if (input) { input.setAttribute("aria-invalid", "true"); input.focus(); }
    };
    const paint = () => {
        const met = passwordChecks(next.value);
        rules.querySelectorAll(".pw-rule").forEach((li) => { const on = !!met[li.dataset.rule]; li.classList.toggle("met", on); li.setAttribute("aria-label", li.textContent + (on ? ": done" : ": not yet")); });
        rules.hidden = next.value === "";
        save.disabled = sending || !dirty();
    };
    form.addEventListener("input", () => { show(""); paint(); });
    cancel.addEventListener("click", leave);
    paint();

    form.addEventListener("submit", async (event) => {
        event.preventDefault();
        if (sending) return;
        const newName = cleanName(name.value);
        const wantsPassword = next.value !== "" || confirm.value !== "" || current.value !== "";
        if (newName.length < 2) return show("Enter your name (at least 2 letters).", name);
        if (wantsPassword) {
            if (!current.value) return show("Enter your current password to change it.", current);
            if (!passwordOk(next.value)) return show("The new password does not meet every rule below it.", next);
            if (next.value !== confirm.value) return show("The two new passwords are not the same.", confirm);
            if (next.value === current.value) return show("The new password is the same as the current one.", next);
        }
        sending = true; paint();
        cancel.disabled = true;
        save.innerHTML = '<span class="btn-spin" aria-hidden="true"></span>Saving...';
        try {
            if (wantsPassword) {
                await reauthenticateWithCredential(user, EmailAuthProvider.credential(user.email, current.value));
                await updatePassword(user, next.value);
            }
            if (newName !== startName) await updateProfile(user, { displayName: newName });
            frame.close();
            document.dispatchEvent(new CustomEvent("glowup:profile-changed"));
            toast(wantsPassword ? "Profile and password saved." : "Profile saved.", "ok");
        } catch (error) {
            console.warn("Edit profile:", error && error.code);
            sending = false; cancel.disabled = false; save.textContent = "Save changes"; paint();
            const why = reason(error);
            show(why.text, why.field === "current" ? current : why.field === "next" ? next : null);
        }
    });
}
