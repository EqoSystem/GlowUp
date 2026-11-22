// Admin sign-up form rules (_Index/signupRules.js). Run: node tests/signupRules.test.mjs
import { passwordChecks, passwordOk, cleanName, cleanPart, fullName, formProblem, signupProblem, requestNote } from "../_Index/signupRules.js";

let checks = 0, failed = 0;
const ok = (cond, what) => { checks++; if (!cond) { failed++; console.log("FAILED: " + what); } };
const good = { first: "Maria", middle: "", last: "Santos", email: "maria@example.com", password: "Glow#2026x", confirm: "Glow#2026x", agreed: true };
const problemOf = (change) => { const p = formProblem(Object.assign({}, good, change)); return p ? p.field : null; };

ok(passwordOk("Glow#2026x"), "a password with all five parts passes");
ok(!passwordOk("glow#2026x"), "no uppercase fails");
ok(!passwordOk("GLOW#2026X"), "no lowercase fails");
ok(!passwordOk("Glow#abcdx"), "no number fails");
ok(!passwordOk("Glow2026xx"), "no special character fails");
ok(!passwordOk("Gl#26x"), "fewer than 8 characters fails");
ok(passwordChecks("Glow 2026").special === false, "a space is not a special character");
ok(JSON.stringify(Object.keys(passwordChecks(""))) === JSON.stringify(["length", "upper", "lower", "number", "special"]), "the five rules, in the app's order");

ok(cleanName("  Maria   Santos ") === "Maria Santos", "the name is squeezed and trimmed");
ok(cleanName("x".repeat(120)).length === 80, "the name is cut at 80");

ok(formProblem(good) === null, "a complete form has no problem");
ok(problemOf({ first: " " }) === "first", "an empty first name is caught");
ok(problemOf({ first: "12" }) === "first", "a first name without letters is caught");
ok(problemOf({ first: "Ñora" }) === null, "a name with accented letters is fine");
ok(problemOf({ email: "" }) === "email", "an empty email is caught");
ok(problemOf({ email: "maria@example" }) === "email", "an email without a dot in the domain is caught");
ok(problemOf({ password: "weak", confirm: "weak" }) === "password", "a weak password is caught");
ok(problemOf({ confirm: "Glow#2026y" }) === "confirm", "different passwords are caught");
ok(problemOf({ agreed: false }) === "agree", "the box must be ticked");
ok(problemOf({ first: "", last: "", email: "" }) === "first", "the first field's problem comes first");

ok(/already has a GlowUp account/.test(signupProblem({ code: "glowup/existing-account" })), "existing account explained");
ok(/already an admin/.test(signupProblem({ code: "glowup/already-admin" })), "already an admin explained");
ok(/internet/.test(signupProblem({ code: "auth/network-request-failed" })), "network fault explained");
ok(/request could not be saved/.test(signupProblem({ code: "permission-denied" })), "refused write explained");
ok(/try again/i.test(signupProblem({})), "unknown fault has a retry text");

ok(requestNote(null, true, "a@b.co") === "", "no request, no note");
ok(requestNote({ status: "approved" }, true, "a@b.co") === "", "a closed request gives no note");
ok(/Confirm your email first: open the link we sent to a@b\.co/.test(requestNote({ status: "pending" }, false, "a@b.co")), "unconfirmed request asks for the link");
ok(/waiting for an admin to approve/.test(requestNote({ status: "pending" }, true, "a@b.co")), "confirmed request is waiting");

console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
