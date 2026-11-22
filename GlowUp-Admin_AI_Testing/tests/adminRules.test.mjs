// Admin Management rules (_Shared/adminRules.js). Run: node tests/adminRules.test.mjs
import { normalizeAdmin, inTab, summarize, filterAdmins, sortAdmins, statusPatch, restoreTarget, canChange, editProblem, isOnline, CSV_COLUMNS } from "../_Shared/adminRules.js";

let checks = 0, failed = 0;
const ok = (cond, what) => { checks++; if (!cond) { failed++; console.log("FAILED: " + what); } };
const now = Date.UTC(2026, 10, 22, 12), day = 86400000;
const t = (ago) => ({ toMillis: () => now - ago });
const a = [
    normalizeAdmin("a1", { name: "Ana Reyes", email: "ana@example.com", approvedAt: t(2 * day), lastActiveAt: t(60000) }),
    normalizeAdmin("a2", { name: "Ben Cruz", email: "ben@example.com", approvedAt: t(20 * day), status: "archived", statusChangedAt: t(1 * day), lastActiveAt: t(1000) }),
    normalizeAdmin("a3", { email: "cara@example.com", approvedAt: t(40 * day), protected: true }),
    normalizeAdmin("a4", { name: "Dee", email: "dee@example.com", approvedAt: t(30 * day), inBin: true, statusBefore: "archived", statusChangedAt: t(3 * day) }),
    normalizeAdmin("a5", { name: "Eve", email: "eve@example.com", approvedAt: t(3 * day), status: "deleted" })
];
ok(a[0].status === "active" && a[2].status === "active", "an admin with no status is active");
ok(a[3].status === "deleted" && a[4].status === "deleted", "inBin:true and status:deleted both mean the bin");
ok(a[2].name === "cara@example.com" && a[2].protectedAdmin === true, "no name shows the e-mail; protected is read");
ok(normalizeAdmin("x", { status: "banned" }).status === "active", "an unknown status is read as active, never as something that grants less than the rules");
ok(isOnline(a[0], now) && !isOnline(a[1], now), "online = active and seen within 5 minutes (an archived admin is never online)");
const s = summarize(a, now);
ok(s.all.count === 5 && s.active.count === 2 && s.archived.count === 1 && s.bin.count === 2 && s.online.count === 1, JSON.stringify(s));
ok(s.all.week === 2 && s.archived.week === 1, `weekly deltas ${s.all.week}/${s.archived.week}`);
ok(filterAdmins(a, { tab: "bin" }).length === 2 && filterAdmins(a, { query: "ben@" }).map((x) => x.uid).join() === "a2" && filterAdmins(a, { status: "archived" }).length === 1, "tabs, search and status filter");
ok(sortAdmins(a).map((x) => x.uid).join() === "a1,a5,a2,a4,a3", "newest first");
ok(canChange(a[0], "me") && !canChange(a[0], "a1") && !canChange(a[2], "me"), "never yourself, never a protected admin");
const p = statusPatch(a[0], "deleted", "NOW", "me");
ok(p.status === "deleted" && p.inBin === true && p.statusBefore === "active" && p.statusChangedBy === "me" && p.statusChangedAt === "NOW", "bin patch remembers the status before");
ok(statusPatch(a[1], "active", "N", "me").inBin === false && !("statusBefore" in statusPatch(a[1], "active", "N", "me")), "restore patch does not set statusBefore");
ok(restoreTarget(a[3]) === "archived" && restoreTarget(a[4]) === "active" && restoreTarget(a[0]) === "active", "restore goes back to where it came from");
ok(editProblem({ name: "A", status: "active" }) && editProblem({ name: "Ana", status: "deleted" }) && editProblem({ name: "x".repeat(81), status: "active" }) && !editProblem({ name: "Ana R", status: "archived" }), "edit checks: name 2-80, status active or archived only");
ok(CSV_COLUMNS.map((c) => c.title).join() === "UID,Name,Email,Status,Approved At,Last Active", "csv columns");
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
