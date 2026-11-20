// User Management rules (_Shared/userRules.js). Run: node tests/userRules.test.mjs
import { normalizeUser, inTab, summarize, filterUsers, sortUsers, statusPatch, restoreTarget, CSV_COLUMNS, editProblem, WEEK_MS } from "../_Shared/userRules.js";
import { toCsv } from "../_Shared/adminExport.js";

let checks = 0, failed = 0;
const ok = (cond, what) => { checks++; if (!cond) { failed++; console.log("FAILED: " + what); } };
const now = Date.UTC(2026, 10, 20, 12);
const day = 24 * 3600 * 1000;
const t = (ago) => ({ toMillis: () => now - ago });

const users = [
    normalizeUser("u1", { fullName: "Ana Reyes", username: "ana", email: "ana@example.com", createdAt: t(2 * day), skinType: "Oily", skinConditions: ["Acne", ""], knownAllergens: ["Nickel"] }),
    normalizeUser("u2", { firstName: "Ben", lastName: "Cruz", email: "ben@example.com", createdAt: t(20 * day), status: "archived", statusChangedAt: t(1 * day), role: "admin" }),
    normalizeUser("u3", { fullName: "Cara Lim", email: "cara@example.com", createdAt: t(40 * day), status: "banned" }),
    normalizeUser("u4", { fullName: "Dee", email: "dee@example.com", createdAt: t(30 * day), inBin: true, statusBefore: "archived", statusChangedAt: t(3 * day) }),
    normalizeUser("u5", { email: "eve@example.com", createdAt: t(3 * day), status: "deleted" }),
    normalizeUser("u6", { fullName: "Fay", createdAt: { seconds: Math.floor((now - 10 * day) / 1000) }, photoURL: "http://insecure/x.png" })
];

ok(users[0].status === "active" && users[0].role === "user", "a user with no status is an active user");
ok(users[1].name === "Ben Cruz" && users[1].role === "admin", "the name is joined from first and last name; the admin role is read");
ok(users[3].status === "deleted" && users[4].status === "deleted", "inBin:true and status:deleted both mean the Recycle Bin");
ok(users[4].name === "eve@example.com", "a user with no name shows the e-mail");
ok(users[0].skin.skinConditions.length === 1, "empty skin tags are dropped");
ok(users[5].createdAt === now - 10 * day && users[5].photoURL === "", "seconds are read; only an https photo is used");

const s = summarize(users, now);
ok(s.all.count === 6 && s.active.count === 2 && s.archived.count === 1 && s.bin.count === 2, `counts ${JSON.stringify(s)}`);
ok(s.all.week === 2, `+N this week for all: created within 7 days (u1, u5) = ${s.all.week}`);
ok(s.active.week === 1, `active this week = ${s.active.week}`);
ok(s.archived.week === 1, "archived this week counts the status change time");
ok(s.bin.week === 2, "bin this week counts status change or creation");
ok(!inTab(users[2], "active") && inTab(users[2], "all"), "a banned user is in All Users only");

ok(filterUsers(users, { tab: "all", query: "ana" }).length === 1, "search finds a name");
ok(filterUsers(users, { query: "EXAMPLE.com" }).length === 5, "search finds an e-mail, any case");
ok(filterUsers(users, { query: "u3" }).length === 1, "search finds a UID");
ok(filterUsers(users, { role: "admin" }).length === 1 && filterUsers(users, { status: "banned" }).length === 1, "role and status filters");
ok(filterUsers(users, { tab: "bin" }).length === 2 && filterUsers(users, { tab: "archived", query: "ben" }).length === 1, "a tab and a search work together");
ok(sortUsers(users)[0].uid === "u1", "newest first");

const p = statusPatch(users[0], "deleted", "NOW", "a1");
ok(p.status === "deleted" && p.inBin === true && p.statusBefore === "active" && p.statusChangedBy === "a1", "moving to the bin remembers the status before");
ok(statusPatch(users[3], "active", "NOW", "a1").inBin === false, "restoring clears inBin");
ok(restoreTarget(users[3]) === "archived" && restoreTarget(users[4]) === "active" && restoreTarget(users[2]) === "active", "restore goes back to what it was");

const csv = toCsv(CSV_COLUMNS, users, 1000).split("\r\n");
ok(csv[0] === "UID,Name,Email,Role,Status,Joined At,Last Active" && csv.length === 7, "CSV has the seven columns and one row per user");
ok(/^u1,Ana Reyes,ana@example.com,User,Active,2026-11-18T/.test(csv[1]), `CSV row ${csv[1]}`);
ok(toCsv(CSV_COLUMNS, [normalizeUser("x", { fullName: "=cmd()", email: "a@b.co" })], 10).includes("'=cmd()"), "a spreadsheet formula is neutralised");

ok(editProblem({ name: "Ana", username: "ana", role: "user", status: "active" }) === null, "a valid edit passes");
ok(editProblem({ name: "A", username: "", role: "user", status: "active" }).field === "name", "a short name fails");
ok(editProblem({ name: "Ana", username: "", role: "boss", status: "active" }).field === "role", "an unknown role fails");
ok(editProblem({ name: "Ana", username: "", role: "user", status: "deleted" }).field === "status", "the edit form cannot send a user to the bin");
ok(WEEK_MS === 7 * day, "a week is 7 days");

console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
