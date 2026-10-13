// Checks of the page layout's small sums (_Shared/layoutRules.js): how many rows of the packing grid a panel takes
// ("Auto resize"), when a panel is folded to its title line, and how a hole between panels is measured.
// No browser and no network.   Run: node tests/layout.test.mjs
import { ROW_UNIT, rowsFor, isFolded, cleanChoice, biggestGap } from "../_Shared/layoutRules.js";

let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };
const equal = (a, b, what) => check(JSON.stringify(a) === JSON.stringify(b), `${what} (got ${JSON.stringify(a)})`);

// ---- Auto resize: the rows a panel takes
equal(rowsFor(480, 16, 4), 124, "a panel 480 high with a 16 gap under it takes 124 rows of 4");
equal(rowsFor(481, 16, 4), 125, "one pixel more needs one more row: a panel is never cut");
equal(rowsFor(52, 16, 4), 17, "a folded panel takes few rows, so the next one moves up");
equal(rowsFor(0, 16, 4), 4, "a panel with no height still keeps its gap");
equal(rowsFor(0, 0, 4), 1, "never less than one row");
equal(rowsFor(-20, -5, 4), 1, "nonsense sizes count as nothing");
equal(rowsFor("abc", undefined), 1, "something that is not a number counts as nothing");
equal(rowsFor(100, 16), Math.ceil(116 / ROW_UNIT), "the row height of the site is used when none is given");
check(rowsFor(300, 16, 4) * 4 - 16 >= 300 && rowsFor(300, 16, 4) * 4 - 16 < 304, "the rows hold the panel and at most one row more");
// two short panels beside a tall one take no more rows than the tall one: they fit in the hole it used to leave
check(rowsFor(172, 16) + rowsFor(196, 16) <= rowsFor(480, 16), "two short panels stack beside a tall one");

// ---- folding: the admin's own choice wins, otherwise a panel with nothing to show folds by itself
equal(isFolded("", false), false, "a panel with something to show is open");
equal(isFolded("", true), true, "a panel with nothing to show folds by itself");
equal(isFolded("open", true), false, "opened by hand: it stays open even when it is empty");
equal(isFolded("closed", false), true, "folded by hand: it stays folded even when data arrives");
equal(isFolded("closed", true), true, "folded by hand and empty: folded");
equal(isFolded(undefined, undefined), false, "nothing known yet (still loading): open");
equal(cleanChoice("open"), "open", "a saved choice is kept");
equal(cleanChoice("closed"), "closed", "a saved choice is kept");
equal(cleanChoice("<script>"), "", "anything else in a saved choice counts as no choice");
equal(cleanChoice(1), "", "a number is no choice");

// ---- how a hole between panels is measured
const tall = { left: 0, right: 1060, top: 148, bottom: 628 }, short = { left: 1076, right: 1598, top: 148, bottom: 374 };
const below = (top) => [{ left: 0, right: 522, top, bottom: top + 196 }, { left: 538, right: 1060, top, bottom: top + 380 }, { left: 1076, right: 1598, top, bottom: top + 352 }];
equal(biggestGap([tall, short, ...below(644)]), 270, "every panel on its own row: the hole under the short panel is 270 high");
equal(biggestGap([tall, short, { left: 1076, right: 1598, top: 390, bottom: 586 }, { left: 1076, right: 1598, top: 602, bottom: 954 }, { left: 0, right: 1060, top: 644, bottom: 880 }]), 16,
    "packed: nothing but the normal 16 between panels");
equal(biggestGap([tall]), 0, "one panel has no hole");
equal(biggestGap([]), 0, "no panels, no hole");
equal(biggestGap([{ left: 0, right: 500, top: 0, bottom: 100 }, { left: 600, right: 1100, top: 400, bottom: 500 }]), 0, "panels in different columns are not under each other");

console.log("checks made: " + checks);
console.log(failed === 0 ? "ALL PASSED" : failed + " FAILED");
process.exit(failed === 0 ? 0 : 1);
