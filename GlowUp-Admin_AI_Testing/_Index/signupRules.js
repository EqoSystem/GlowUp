// Admin sign-up: the checks of the "Create Admin Account" form. Pure functions, no page and no Firebase,
// so tests/signupRules.test.mjs can run them in node. The password rules are the app's (Onboarding Step 1):
// at least 8 characters, an uppercase letter, a lowercase letter, a number and a special character.

export const EMAIL_SHAPE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
export const NAME_MIN = 2;
export const NAME_MAX = 80;

export const PASSWORD_RULES = [
    { key: "length", label: "8 or more characters", test: (p) => p.length >= 8 },
    { key: "upper", label: "An uppercase letter", test: (p) => /[A-Z]/.test(p) },
    { key: "lower", label: "A lowercase letter", test: (p) => /[a-z]/.test(p) },
    { key: "number", label: "A number", test: (p) => /[0-9]/.test(p) },
    { key: "special", label: "A special character", test: (p) => /[^A-Za-z0-9\s]/.test(p) }
];

/** { length: true, upper: false, ... } for the checklist under the password box. */
export function passwordChecks(password) {
    const p = String(password || "");
    const result = {};
    PASSWORD_RULES.forEach((rule) => { result[rule.key] = rule.test(p); });
    return result;
}
export const passwordOk = (password) => Object.values(passwordChecks(password)).every(Boolean);

/** The name as it is saved: spaces squeezed, trimmed, at most NAME_MAX characters. */
export const cleanName = (name) => String(name || "").replace(/\s+/g, " ").trim().slice(0, NAME_MAX);

/** The first problem of the form, or "" when it can be sent. Field order, so the message names the field the eye meets first. */
export function formProblem({ name, email, password, confirm, agreed }) {
    const n = cleanName(name);
    const e = String(email || "").trim();
    if (n.length < NAME_MIN) return { field: "name", text: "Enter your full name." };
    if (!/\p{L}/u.test(n)) return { field: "name", text: "A name needs letters." };
    if (e === "") return { field: "email", text: "Enter your email address." };
    if (!EMAIL_SHAPE.test(e)) return { field: "email", text: "That does not look like an email address." };
    if (!password) return { field: "password", text: "Create a password." };
    if (!passwordOk(password)) return { field: "password", text: "The password does not meet every rule below it yet." };
    if (confirm !== password) return { field: "confirm", text: "The two passwords are not the same." };
    if (!agreed) return { field: "agree", text: "Agree to the Privacy Policy and the Terms and Conditions first." };
    return null;
}

/** Text for an error of the sign-up request (Firebase codes and our own "glowup/..." codes). */
export function signupProblem(error) {
    const code = error && error.code ? String(error.code) : "";
    if (code === "glowup/existing-account") return "This email already has a GlowUp account (for example from the app). Enter that account's password to request admin access with it, or use Forgot password? on the log-in page.";
    if (code === "glowup/already-admin") return "This account is already an admin. Go back and log in.";
    if (code === "glowup/locked") return "Too many wrong passwords for this email. Wait a few minutes, or use Forgot password? on the log-in page.";
    if (code === "glowup/timeout") return "This is taking too long. Check the connection and press Request Admin Access again.";
    if (code.includes("invalid-email")) return "That does not look like an email address.";
    if (code.includes("weak-password") || code.includes("password-does-not-meet-requirements")) return "Firebase finds this password too weak. Choose a stronger one.";
    if (code.includes("too-many-requests")) return "Too many attempts. Wait a few minutes and try again.";
    if (code.includes("network-request-failed")) return "No internet connection. Check it and try again.";
    if (code.includes("operation-not-allowed")) return "Email sign-up is switched off for this project. Ask the project owner.";
    if (code.includes("permission-denied")) return "Your account is ready, but the request could not be saved. Press Request Admin Access again in a moment.";
    return "The request could not be sent. Please try again.";
}

/** What a request document means for the person who sent it (shown at log-in while they are not an admin yet). */
export function requestNote(request, emailVerified, email) {
    if (!request) return "";
    if (request.status !== "pending") return "";
    if (!emailVerified) return `Your admin request is waiting. Confirm your email first: open the link we sent to ${email}.`;
    return "Your email is confirmed. Your admin request is waiting for an admin to approve it; you can log in once it is approved.";
}
