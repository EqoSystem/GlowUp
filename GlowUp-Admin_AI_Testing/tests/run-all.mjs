// Runs every check in this folder (tests/*.test.mjs) and prints one line each. Exit code 1 if any of them fails.
//   node tests/run-all.mjs
// The emulator checks of the security rules are separate (they need Java): npm run test:rules
import { readdirSync } from "node:fs";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import path from "node:path";

const here = path.dirname(fileURLToPath(import.meta.url));
const files = readdirSync(here).filter((f) => f.endsWith(".test.mjs")).sort();
let failed = 0;
for (const file of files) {
    const run = spawnSync(process.execPath, [path.join(here, file)], { cwd: path.join(here, ".."), encoding: "utf8" });
    const lines = (run.stdout + run.stderr).trim().split(/\r?\n/);
    const last = lines[lines.length - 1] || "(no output)";
    // Most legacy checks print "ALL PASSED"; native node:test files print a
    // summary instead. Accept either format, while still requiring a clean
    // process exit and an explicit zero-failure summary for the latter.
    const nativeTestPassed = /\bfail 0\b/.test(run.stdout) && /\bpass \d+\b/.test(run.stdout);
    const ok = run.status === 0 && (/ALL PASSED/.test(run.stdout) || nativeTestPassed);
    if (!ok) { failed++; console.log(`FAIL  ${file}\n${lines.slice(-12).map((l) => "      " + l).join("\n")}`); }
    else console.log(`ok    ${file}  (${lines.filter((l) => /checks made/.test(l)).join("").replace(/^.*checks made:\s*/, "") || "?"} checks)`);
}
console.log(failed === 0 ? `\nALL PASSED (${files.length} files)` : `\n${failed} of ${files.length} files FAILED`);
process.exit(failed === 0 ? 0 : 1);
