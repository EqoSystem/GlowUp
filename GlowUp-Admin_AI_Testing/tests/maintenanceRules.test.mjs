// Maintenance Mode rules (_Shared/maintenanceRules.js). Run: node tests/maintenanceRules.test.mjs
import { maintenanceState, watchdogPatch, planSave, toLocalInput, fromLocalInput, noticeText } from "../_Shared/maintenanceRules.js";

let checks = 0, failed = 0;
const ok = (cond, what) => { checks++; if (!cond) { failed++; console.log("FAILED: " + what); } };
const now = Date.UTC(2026, 10, 20, 6, 0);
const h = 3600e3;
const ts = (ms) => ({ toMillis: () => ms });

ok(maintenanceState(null, now) === "online", "no document = online");
ok(maintenanceState({ isActive: false }, now) === "online", "off = online");
ok(maintenanceState({ isActive: false, scheduledStart: ts(now + h) }, now) === "scheduled", "future start = scheduled");
ok(maintenanceState({ isActive: false, scheduledStart: ts(now - h) }, now) === "online", "past start, not active = online");
ok(maintenanceState({ isActive: true }, now) === "active", "on = active");

ok(watchdogPatch({ isActive: true, scheduledEnd: ts(now - 1) }, now).isActive === false, "the end time passing switches it off");
ok(watchdogPatch({ isActive: true, scheduledEnd: ts(now - 1) }, now).notifyUsers === false, "and clears the notice");
ok(watchdogPatch({ isActive: true, scheduledEnd: ts(now + h) }, now) === null, "before the end: nothing");
ok(watchdogPatch({ isActive: true, scheduledEnd: null }, now) === null, "no end time: stays on");
ok(watchdogPatch({ isActive: false, scheduledStart: ts(now - 1), scheduledEnd: ts(now + h) }, now).isActive === true, "the start time coming switches it on");
ok(watchdogPatch({ isActive: false, scheduledStart: ts(now - 2 * h), scheduledEnd: ts(now - h) }, now).isActive === undefined, "a window that passed entirely only clears the schedule");
ok(watchdogPatch({ isActive: false, scheduledStart: ts(now + h) }, now) === null, "before the start: nothing");

const inH = (n) => toLocalInput(now + n * h);
ok(fromLocalInput(toLocalInput(now)) === now, "datetime-local round trip");
ok(planSave({ enable: true, start: "", end: "" }, now).isActive === true, "on with no start = now");
ok(planSave({ enable: true, start: inH(1), end: inH(3) }, now).isActive === false, "on with a future start = armed");
ok(planSave({ enable: true, start: inH(1), end: "" }, now).field === "end", "a schedule needs an end");
ok(planSave({ enable: true, start: inH(-1), end: inH(3) }, now).field === "start", "a past start is refused");
ok(planSave({ enable: true, start: inH(2), end: inH(1) }, now).field === "end", "the end must come after the start");
ok(planSave({ enable: false, start: inH(1), end: inH(3) }, now).isActive === false && planSave({ enable: false, start: inH(1) }, now).startMs === 0, "off clears the schedule");
ok(planSave({ enable: true, title: "x".repeat(81) }, now).field === "title", "title over 80 is refused");
ok(planSave({ enable: true, message: "x".repeat(301) }, now).field === "message", "message over 300 is refused");
ok(/^Heads up! GlowUp will be unavailable for updates starting at .+ until .+\.$/.test(noticeText({ scheduledStart: ts(now + h), scheduledEnd: ts(now + 2 * h) })), "notice text");
ok(noticeText({}) === "", "no start, no notice");

console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
