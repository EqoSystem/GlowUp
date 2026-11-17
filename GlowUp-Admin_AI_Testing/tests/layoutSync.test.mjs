// Layout & Style across devices (17 Nov 2026): how this browser's layouts and the admin's database copy are put together.
// Run: node tests/layoutSync.test.mjs
import { mergeLayouts, MAX_SYNCED_PROFILES } from "../_Shared/layoutSync.js";

let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };
const names = (s) => s.profiles.map((p) => p.name).join(",");
const p = (id, name, extra = {}) => ({ id, name, appearance: {}, pages: {}, ...extra });

// the phone has nothing yet, the computer made "Echo": the phone gets it
let r = mergeLayouts({ active: "default", profiles: [] }, 0, { active: "e1", profiles: [p("e1", "Echo")] }, 500, true);
check(names(r.store) === "Echo" && r.apply && !r.push, "a phone with no layouts gets Echo from the database");
check(r.store.active === "default", "which layout is in use stays this device's choice");

// no database copy yet: this browser's layouts go up
r = mergeLayouts({ active: "e1", profiles: [p("e1", "Echo")] }, 300, null, 0, true);
check(r.push && !r.apply && names(r.store) === "Echo", "the first browser uploads its layouts");
r = mergeLayouts({ active: "default", profiles: [] }, 0, null, 0, true);
check(!r.push, "an empty browser uploads nothing");

// first meeting: both sides are kept, the newer side wins a layout that is on both
r = mergeLayouts({ active: "m1", profiles: [p("m1", "Mobile"), p("s1", "Same old")] }, 100, { active: "e1", profiles: [p("e1", "Echo"), p("s1", "Same new")] }, 200, true);
check(names(r.store).split(",").sort().join(",") === "Echo,Mobile,Same new", "first meeting keeps the layouts of both sides, the newer copy of a shared one");
check(r.apply && r.push, "first meeting: both sides get the joined list");
check(r.store.active === "m1", "the layout in use on this device stays in use");
r = mergeLayouts({ active: "default", profiles: [1, 2, 3, 4].map((i) => p("l" + i, "L" + i)) }, 100, { active: "default", profiles: [5, 6].map((i) => p("r" + i, "R" + i)) }, 200, true);
check(r.store.profiles.length === MAX_SYNCED_PROFILES, "never more layouts than the page allows");

// later: the side saved last wins as a whole (a delete or rename carries over)
r = mergeLayouts({ active: "e1", profiles: [p("e1", "Echo"), p("x1", "Old")] }, 100, { active: "e1", profiles: [p("e1", "Echo 2")] }, 200, false);
check(names(r.store) === "Echo 2" && r.apply && !r.push, "a newer database copy replaces this browser's (rename and delete carry over)");
r = mergeLayouts({ active: "e1", profiles: [p("e1", "Echo 3")] }, 300, { active: "e1", profiles: [p("e1", "Echo 2")] }, 200, false);
check(names(r.store) === "Echo 3" && !r.apply && r.push, "a newer browser copy goes up to the database");
r = mergeLayouts({ active: "x9", profiles: [p("x9", "Gone")] }, 100, { active: "e1", profiles: [p("e1", "Echo")] }, 200, false);
check(r.store.active === "default", "a layout in use that was deleted elsewhere falls back to the default");
r = mergeLayouts({ active: "e1", profiles: [p("e1", "Echo")] }, 200, { active: "e1", profiles: [p("e1", "Echo")] }, 200, false);
check(!r.apply && !r.push, "the same age on both sides: nothing moves");

console.log(failed === 0 ? `ALL PASSED  (layoutSync: ${checks} checks)` : `${failed} FAILED  (layoutSync: ${checks} checks)`);
process.exit(failed === 0 ? 0 : 1);
