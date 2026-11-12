

import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";

const here = path.dirname(fileURLToPath(import.meta.url));
const gs1 = await import("data:text/javascript;base64," + Buffer.from(readFileSync(path.join(here, "..", "_Shared", "gs1Hint.js"), "utf8"), "utf8").toString("base64"));

let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };
const equal = (got, expected, what) => check(JSON.stringify(got) === JSON.stringify(expected), `${what} (got ${JSON.stringify(got)})`);

equal(gs1.gtinCheck("4006381333931").valid, true, "a valid 13-digit barcode");
equal(gs1.gtinCheck("036000291452").valid, true, "a valid 12-digit (UPC) barcode");
equal(gs1.gtinCheck("4006381333932").valid, false, "a wrong last digit is caught");
equal(gs1.gtinCheck("4800-0000-0001-9").digits, "4800000000019", "spaces and dashes are ignored");
equal(gs1.gtinCheck("12345").valid, false, "a code of the wrong length is not a barcode");
equal(gs1.gs1Office("4800000000019").office, "GS1 Philippines", "480 is GS1 Philippines");
equal(gs1.gs1Office("8800000000015").office, "GS1 Korea", "880 is GS1 Korea");
equal(gs1.gs1Office("6930000000011").office, "GS1 China", "690-699 is GS1 China");
equal(gs1.gs1Office("036000291452").office, "GS1 US (United States and Canada)", "a 12-digit code is read as 0 + its digits");
equal(gs1.gs1Office("9780000000002").note, "a book number (ISBN)", "978 is a book number, not a company");
equal(gs1.gs1Office("2000000000008").note, "a shop's own in-store number", "200-299 is an in-store number");
check(/GS1 Philippines/.test(gs1.gs1Hint("4800000000019").text) && /not where the product is made/.test(gs1.gs1Hint("4800000000019").text), "the hint names the office and what it does not prove");
check(/misread or typed wrong/.test(gs1.gs1Hint("4800000000010").text), "a barcode with a wrong check digit is said to be misread");
equal(gs1.gs1Hint("").text, "", "no barcode, no hint");
check(gs1.VERIFIED_BY_GS1.startsWith("https://"), "the Verified by GS1 page is an https address");

console.log("checks made: " + checks);
console.log(failed === 0 ? "ALL PASSED" : failed + " FAILED");
process.exit(failed === 0 ? 0 : 1);
